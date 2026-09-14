using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DataGuard.Core.Telemetry;

/// <summary>
/// Configuration for the product-native observability text sink.
/// The sink writes one JSON object per line and partitions files by UTC day.
/// It is diagnostic observability data; it is not an audit ledger.
/// </summary>
public sealed record ObservabilityFileOptions
{
    /// <summary>Enables the local text sink.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Root directory for daily archive files. A null value resolves to the
    /// operating system's local application-data directory.
    /// </summary>
    public string? DirectoryPath { get; init; }

    /// <summary>File prefix used for daily NDJSON files.</summary>
    public string FilePrefix { get; init; } = "observability";

    /// <summary>Maximum serialized record size in UTF-8 bytes.</summary>
    public int MaxRecordBytes { get; init; } = 64 * 1024;

    /// <summary>
    /// Allows an explicitly requested, redacted event body. Disabled by default
    /// so arbitrary diagnostic details cannot become a data-exfiltration path.
    /// </summary>
    public bool IncludeEventDetails { get; init; }

    /// <summary>Stable service name in the local resource envelope.</summary>
    public string ServiceName { get; init; } = "dataguard";

    /// <summary>Service version in the local resource envelope.</summary>
    public string ServiceVersion { get; init; } = "unknown";

    /// <summary>Deployment environment in the local resource envelope.</summary>
    public string EnvironmentName { get; init; } = "local";

    internal string ResolveDirectoryPath()
    {
        if (!string.IsNullOrWhiteSpace(DirectoryPath))
        {
            return Path.GetFullPath(DirectoryPath);
        }

        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            localApplicationData = Path.GetTempPath();
        }

        return Path.GetFullPath(Path.Combine(localApplicationData, "DataGuard", "observability", "archive"));
    }
}

/// <summary>Result of one bounded local file write.</summary>
public sealed record ObservabilityWriteResult(
    int WrittenRecords,
    int DroppedRecords,
    IReadOnlyList<string> Files);

/// <summary>
/// A stable, text-friendly envelope mapped to OpenTelemetry concepts.
/// This is deliberately not advertised as an OTLP wire payload: OTLP export
/// remains a separate, disabled-by-default adapter concern.
/// </summary>
public sealed record ObservabilityRecord(
    [property: JsonPropertyName("record_id")] string RecordId,
    [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp,
    [property: JsonPropertyName("signal")] string Signal,
    [property: JsonPropertyName("event_name")] string EventName,
    [property: JsonPropertyName("service_name")] string ServiceName,
    [property: JsonPropertyName("service_version")] string? ServiceVersion,
    [property: JsonPropertyName("operation_name")] string? OperationName,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("severity_text")] string? SeverityText,
    [property: JsonPropertyName("value")] double? Value,
    [property: JsonPropertyName("unit")] string? Unit,
    [property: JsonPropertyName("body")] string? Body,
    [property: JsonPropertyName("trace_id")] string? TraceId,
    [property: JsonPropertyName("span_id")] string? SpanId,
    [property: JsonPropertyName("attributes")] IReadOnlyDictionary<string, string> Attributes,
    [property: JsonPropertyName("resource")] IReadOnlyDictionary<string, string> Resource);

/// <summary>
/// Writes bounded observability records into UTC-day archive files.
/// </summary>
public sealed class FileObservabilitySink : IAsyncDisposable, IDisposable
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private readonly ObservabilityFileOptions _options;
    private readonly string _rootDirectory;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private int _disposed;
    private string? _lastFilePath;

    public FileObservabilitySink(ObservabilityFileOptions? options = null)
    {
        _options = options ?? new ObservabilityFileOptions();
        if (!_options.Enabled)
        {
            throw new ArgumentException("The file sink must not be constructed with Enabled=false.", nameof(options));
        }

        if (_options.MaxRecordBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxRecordBytes must be positive.");
        }

        if (string.IsNullOrWhiteSpace(_options.FilePrefix)
            || _options.FilePrefix.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("FilePrefix must be a non-empty file-name-safe value.", nameof(options));
        }

        _rootDirectory = _options.ResolveDirectoryPath();
        ValidateExistingPath(_rootDirectory, directory: true);
    }

    /// <summary>Most recently written daily archive file, or null before the first write.</summary>
    public string? LastFilePath => Volatile.Read(ref _lastFilePath);

    /// <summary>
    /// Serializes and appends a bounded batch. Files are partitioned as
    /// <c>yyyy/MM/dd/{prefix}-yyyy-MM-dd.ndjson</c> under the configured root.
    /// </summary>
    public async Task<ObservabilityWriteResult> WriteBatchAsync(
        IReadOnlyList<ObservabilityRecord> records,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);
        ThrowIfDisposed();
        if (records.Count == 0)
        {
            return new ObservabilityWriteResult(0, 0, Array.Empty<string>());
        }

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var written = 0;
            var dropped = 0;
            var files = new List<string>();

            foreach (var group in records.GroupBy(GetUtcDate, DateOnlyComparer.Instance))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var date = group.Key;
                var dayDirectory = Path.Combine(
                    _rootDirectory,
                    date.Year.ToString("0000", CultureInfo.InvariantCulture),
                    date.Month.ToString("00", CultureInfo.InvariantCulture),
                    date.Day.ToString("00", CultureInfo.InvariantCulture));
                Directory.CreateDirectory(dayDirectory);
                ValidateExistingPath(dayDirectory, directory: true);

                var filePath = Path.Combine(
                    dayDirectory,
                    $"{_options.FilePrefix}-{date:yyyy-MM-dd}.ndjson");
                ValidateExistingPath(filePath, directory: false);

                await using var stream = new FileStream(
                    filePath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.Read,
                    bufferSize: 16 * 1024,
                    options: FileOptions.Asynchronous | FileOptions.SequentialScan);
                await using var writer = new StreamWriter(stream, Utf8NoBom, 16 * 1024, leaveOpen: false);

                var groupWritten = 0;
                foreach (var record in group)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var line = SerializeLine(record);
                    if (Utf8NoBom.GetByteCount(line) + Environment.NewLine.Length > _options.MaxRecordBytes)
                    {
                        dropped++;
                        continue;
                    }

                    await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
                    written++;
                    groupWritten++;
                }

                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                if (groupWritten > 0 && !files.Contains(filePath, StringComparer.Ordinal))
                {
                    files.Add(filePath);
                    Volatile.Write(ref _lastFilePath, filePath);
                }
            }

            return new ObservabilityWriteResult(written, dropped, files);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    internal static string SerializeLine(ObservabilityRecord record)
        => JsonSerializer.Serialize(record, JsonOptions);

    private static DateOnly GetUtcDate(ObservabilityRecord record)
        => DateOnly.FromDateTime(record.Timestamp.UtcDateTime.Date);

    private void ValidateExistingPath(string path, bool directory)
    {
        var fullPath = Path.GetFullPath(path);
        if (directory)
        {
            if (File.Exists(fullPath))
            {
                throw new IOException($"Observability archive directory is a file: {fullPath}");
            }

            if (Directory.Exists(fullPath)
                && new DirectoryInfo(fullPath).Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new IOException($"Observability archive directory is a symbolic link or reparse point: {fullPath}");
            }

            return;
        }

        try
        {
            if (File.GetAttributes(fullPath).HasFlag(FileAttributes.ReparsePoint))
            {
                throw new IOException($"Observability archive file is a symbolic link or reparse point: {fullPath}");
            }
        }
        catch (FileNotFoundException)
        {
            // A missing file is created by the append operation below.
        }
        catch (DirectoryNotFoundException)
        {
            // A missing parent is created before this validation.
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(FileObservabilitySink));
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            // A timed-out caller may still be finishing a local append. Do not
            // dispose the gate underneath it; wait briefly and leak only the
            // tiny semaphore object if the filesystem is genuinely hung.
            if (_writeGate.Wait(TimeSpan.FromSeconds(5)))
            {
                _writeGate.Release();
                _writeGate.Dispose();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            await _writeGate.WaitAsync().ConfigureAwait(false);
            _writeGate.Release();
            _writeGate.Dispose();
        }
    }

    private sealed class DateOnlyComparer : IEqualityComparer<DateOnly>
    {
        public static DateOnlyComparer Instance { get; } = new();

        public bool Equals(DateOnly x, DateOnly y) => x == y;

        public int GetHashCode(DateOnly obj) => obj.GetHashCode();
    }
}

internal static class ObservabilityRecordFactory
{
    private const int MaxAttributeLength = 128;
    private const int MaxBodyLength = 1024;
    private static readonly HashSet<string> AllowedAttributeKeys = new(StringComparer.Ordinal)
    {
        "operation",
        "operation.name",
        "result",
        "dependency",
        "protocol",
        "error.type",
        "slo.class",
        "rule",
        "success",
        "provider",
        "reason",
        "status",
    };

    private static readonly Regex SecretPattern = new(
        @"(?ix)\b(password|passwd|pwd|secret|token|api[_ -]?key|authorization|cookie|pan|cvv|account(?:[_ -]?number)?)\s*[:=]\s*(?:(?:bearer|basic|digest)\s+)?[^,;\r\n]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex BearerPattern = new(
        @"(?i)\bbearer\s+[A-Za-z0-9._~+/=-]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PanPattern = new(
        @"\b(?:\d[ -]?){13,19}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex EmailPattern = new(
        @"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static ObservabilityRecord CreateLog(
        ObservabilityFileOptions options,
        string eventName,
        string? details,
        IEnumerable<KeyValuePair<string, object?>>? properties = null,
        string? operationName = null,
        string? status = null,
        string? severity = "INFO")
    {
        var activity = Activity.Current;
        return new ObservabilityRecord(
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            "log",
            NormalizeName(eventName),
            NormalizeName(options.ServiceName),
            NormalizeOptionalToken(options.ServiceVersion),
            NormalizeOptionalToken(operationName),
            NormalizeOptionalToken(status),
            NormalizeOptionalToken(severity),
            null,
            null,
            options.IncludeEventDetails ? SanitizeBody(details) : null,
            GetTraceId(activity),
            GetSpanId(activity),
            Allowlist(properties),
            CreateResource(options));
    }

    public static ObservabilityRecord CreateMetric(
        ObservabilityFileOptions options,
        string name,
        double value,
        string unit,
        IEnumerable<KeyValuePair<string, object?>>? tags = null)
    {
        var activity = Activity.Current;
        var attributes = Allowlist(tags);
        var normalizedName = NormalizeName(name);
        return new ObservabilityRecord(
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            "metric",
            normalizedName,
            NormalizeName(options.ServiceName),
            NormalizeOptionalToken(options.ServiceVersion),
            attributes.TryGetValue("operation.name", out var operation) ? operation : null,
            null,
            null,
            value,
            Normalize(unit, "1"),
            null,
            GetTraceId(activity),
            GetSpanId(activity),
            attributes,
            CreateResource(options));
    }

    public static ObservabilityRecord CreateSpan(
        ObservabilityFileOptions options,
        string operationName,
        TimeSpan duration,
        string status,
        IEnumerable<KeyValuePair<string, object?>>? tags = null)
    {
        var activity = Activity.Current;
        return new ObservabilityRecord(
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            "span",
            NormalizeName(operationName),
            NormalizeName(options.ServiceName),
            NormalizeOptionalToken(options.ServiceVersion),
            NormalizeName(operationName),
            NormalizeOptionalToken(status),
            null,
            duration.TotalMilliseconds,
            "ms",
            null,
            GetTraceId(activity),
            GetSpanId(activity),
            Allowlist(tags),
            CreateResource(options));
    }

    private static IReadOnlyDictionary<string, string> CreateResource(ObservabilityFileOptions options)
        => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["service.name"] = NormalizeName(options.ServiceName),
            ["service.version"] = NormalizeName(options.ServiceVersion),
            ["deployment.environment.name"] = NormalizeName(options.EnvironmentName),
        };

    private static Dictionary<string, string> Allowlist(IEnumerable<KeyValuePair<string, object?>>? values)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (values is null)
        {
            return result;
        }

        foreach (var pair in values)
        {
            if (!AllowedAttributeKeys.Contains(pair.Key)
                || pair.Value is null)
            {
                continue;
            }

            string? text;
            try
            {
                text = Convert.ToString(pair.Value, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                // A faulty diagnostic value must not escape into the business path.
                continue;
            }
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            text = SanitizeBody(text);
            var normalized = NormalizeAttributeValue(pair.Key, text);
            if (normalized is not null)
            {
                result[pair.Key] = normalized.Length <= MaxAttributeLength
                    ? normalized
                    : normalized[..MaxAttributeLength];
            }
        }

        return result;
    }

    private static string SanitizeBody(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var sanitized = SecretPattern.Replace(value, "$1=[REDACTED]");
        sanitized = BearerPattern.Replace(sanitized, "bearer [REDACTED]");
        sanitized = PanPattern.Replace(sanitized, "[REDACTED_NUMBER]");
        sanitized = EmailPattern.Replace(sanitized, "[REDACTED_EMAIL]");
        sanitized = sanitized.Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
        return sanitized.Length <= MaxBodyLength ? sanitized : sanitized[..MaxBodyLength];
    }

    private static string NormalizeName(string? value)
    {
        var text = Normalize(value, "unknown");
        if (!IsStableToken(text) || text.Contains('/', StringComparison.Ordinal) || text.Contains('?', StringComparison.Ordinal))
        {
            return "unknown";
        }

        return text.Length <= MaxAttributeLength ? text : text[..MaxAttributeLength];
    }

    private static string? NormalizeAttributeValue(string key, string value)
    {
        if (!IsStableToken(value))
        {
            return null;
        }

        return key switch
        {
            "success" when value is not ("True" or "False" or "true" or "false") => null,
            "slo.class" when value is not ("critical" or "standard" or "low") => null,
            "result" when value is not ("success" or "accepted" or "eventually_completed"
                or "business_rejection" or "validation_failure" or "authentication_failure"
                or "authorization_denial" or "technical_failure" or "timeout"
                or "dependency_unavailable" or "cancellation" or "concurrency_conflict"
                or "duplicate" or "idempotent_no_op" or "unknown") => null,
            _ => value,
        };
    }

    private static bool IsStableToken(string value)
        => value.Length is > 0 and <= 128
            && !value.Any(char.IsWhiteSpace)
            && !value.Any(char.IsControl)
            && !value.Contains('{')
            && !value.Contains('}')
            && !value.Contains('/')
            && !value.Contains('?')
            && !value.Contains('=');

    private static string Normalize(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string? NormalizeOptionalToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        var normalized = NormalizeName(trimmed);
        return normalized == "unknown" && !string.Equals(trimmed, "unknown", StringComparison.Ordinal)
            ? null
            : normalized;
    }

    private static string? GetTraceId(Activity? activity)
        => activity is { IdFormat: ActivityIdFormat.W3C } && activity.TraceId != default
            ? activity.TraceId.ToHexString()
            : null;

    private static string? GetSpanId(Activity? activity)
        => activity is { IdFormat: ActivityIdFormat.W3C } && activity.SpanId != default
            ? activity.SpanId.ToHexString()
            : null;
}
