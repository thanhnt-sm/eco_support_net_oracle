using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DataGuard.Core.Abstractions;
using Microsoft.Extensions.Caching.Memory;

namespace DataGuard.Core.Baseline;

/// <summary>
/// Manages baseline files for legacy codebases.
/// Supports database version tracking and schema hash for drift detection.
/// </summary>
public class BaselineManager
{
    /// <summary>Maximum serialized baseline size accepted for persistence and loading.</summary>
    public const long MaxBaselineBytes = 16 * 1024 * 1024;

    private readonly string _baselineFilePath;
    private static readonly MemoryCache _schemaHashCache = new MemoryCache(new MemoryCacheOptions
    {
        SizeLimit = 10000,
        ExpirationScanFrequency = TimeSpan.FromMinutes(5),
    });

    private static readonly ConcurrentDictionary<string, string> _fileHashCache = new();
    private static long _baselineCacheHits;
    private static long _baselineCacheMisses;

    public BaselineManager(string baselineFilePath)
    {
        _baselineFilePath = baselineFilePath ?? throw new ArgumentNullException(nameof(baselineFilePath));
    }

    /// <summary>Returns bounded in-memory baseline-cache counters for operational observation.</summary>
    public static BaselineCacheMetrics CacheMetrics => new(Interlocked.Read(ref _baselineCacheHits), Interlocked.Read(ref _baselineCacheMisses));

    /// <summary>
    /// Creates a new baseline from current violations.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public async Task<BaselineFile> CreateBaselineAsync(
        IEnumerable<ContractViolation> violations,
        string schemaVersion,
        string groundTruthMode,
        string? databaseVersion = null,
        string? schemaHash = null,
        IReadOnlyList<SnapshotTable>? schema = null,
        string? schemaHashKind = null,
        string? provider = null,
        string? schemaScope = null,
        string? schemaCanonicalizationVersion = null,
        CancellationToken cancellationToken = default)
    {
        var baselineViolations = violations.Select(v => new BaselineViolation(
            v.RuleId,
            v.Message,
            v.Severity.ToString(),
            v.Location != null ? new BaselineLocation(
                v.Location.SourceTree?.FilePath ?? "",
                v.Location.GetLineSpan().StartLinePosition.Line + 1,
                v.Location.GetLineSpan().StartLinePosition.Character + 1,
                v.Location.GetLineSpan().EndLinePosition.Line + 1,
                v.Location.GetLineSpan().EndLinePosition.Character + 1) : null,
            v.Properties?.ToImmutableDictionary())).ToList();

        var computedSchemaHash = schemaHash ?? (schema is not null
            ? ComputeSchemaHash(schema, provider, schemaScope, schemaCanonicalizationVersion ?? "v1")
            : ComputeSchemaHash(violations));
        var dbVersion = databaseVersion ?? "unknown";

        var baseline = new BaselineFile(
            Version: schema is null ? 2 : 3,
            CreatedAt: DateTimeOffset.UtcNow,
            SchemaVersion: schemaVersion,
            GroundTruthMode: groundTruthMode,
            DatabaseVersion: dbVersion,
            SchemaHash: computedSchemaHash,
            Violations: baselineViolations,
            Schema: schema,
            SchemaHashKind: schemaHashKind ?? (schema is null ? "violation-sha256-prefix" : "canonical-schema-v1"),
            Provider: provider,
            SchemaScope: schemaScope,
            SchemaCanonicalizationVersion: schemaCanonicalizationVersion ?? (schema is null ? null : "v1"));

        await SaveAsync(baseline, cancellationToken);
        return baseline;
    }

    /// <summary>
    /// Loads an existing baseline file.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public async Task<BaselineFile?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_baselineFilePath))
        {
            return null;
        }

        var fileInfo = new FileInfo(_baselineFilePath);
        if (fileInfo.Length > MaxBaselineBytes)
        {
            throw new InvalidDataException($"Baseline exceeds the {MaxBaselineBytes} byte limit.");
        }

        if (fileInfo.Length > 1024 * 1024)
        {
            return await LoadWithMemoryMappedFileAsync(cancellationToken);
        }

        try
        {
            var json = await File.ReadAllTextAsync(_baselineFilePath, cancellationToken);
            var cacheKey = "baseline-content-v1:" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json)));
            if (_schemaHashCache.TryGetValue(cacheKey, out BaselineFile? cached))
            {
                Interlocked.Increment(ref _baselineCacheHits);
                return cached;
            }

            Interlocked.Increment(ref _baselineCacheMisses);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() },
            };
            var parsed = JsonSerializer.Deserialize<BaselineFile>(json, options);
            if (parsed is not null)
            {
                _schemaHashCache.Set(cacheKey, parsed, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1), Size = 1 });
            }

            return parsed;
        }
        catch (JsonException)
        {
            // Try to load as version 1 format
            try
            {
                var json = await File.ReadAllTextAsync(_baselineFilePath, cancellationToken);
                var legacy = JsonSerializer.Deserialize<LegacyBaselineFile>(json);
                if (legacy != null)
                {
                    return MigrateFromLegacy(legacy);
                }
            }
            catch
            {
                // Ignore
            }

            return null;
        }
    }

    /// <summary>
    /// Loads baseline using memory-mapped file for large files.
    /// </summary>
    private async Task<BaselineFile?> LoadWithMemoryMappedFileAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var mmf = MemoryMappedFile.CreateFromFile(_baselineFilePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        using var accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

        if (accessor.Capacity > MaxBaselineBytes)
        {
            throw new InvalidDataException($"Baseline exceeds the {MaxBaselineBytes} byte limit.");
        }

        var length = checked((int)accessor.Capacity);
        var buffer = new byte[length];
        accessor.ReadArray(0, buffer, 0, length);

        var json = System.Text.Encoding.UTF8.GetString(buffer);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };
        return JsonSerializer.Deserialize<BaselineFile>(json, options);
    }

    private async Task SaveAsync(BaselineFile baseline, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };
        var json = JsonSerializer.Serialize(baseline, options);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        if (bytes.Length > MaxBaselineBytes)
        {
            throw new InvalidDataException($"Baseline exceeds the {MaxBaselineBytes} byte limit.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        await SaveAtomicallyAsync(bytes, cancellationToken);
    }

    private async Task SaveAtomicallyAsync(byte[] data, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(_baselineFilePath))!;
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(_baselineFilePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough | FileOptions.Asynchronous))
            {
                await stream.WriteAsync(data, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(_baselineFilePath))
            {
                File.Move(tempPath, _baselineFilePath, overwrite: true);
            }
            else
            {
                File.Move(tempPath, _baselineFilePath);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                }
            }
        }
    }

    /// <summary>
    /// Computes a schema hash for the given violations.
    /// </summary>
    /// <returns></returns>
    public static string ComputeSchemaHash(IEnumerable<ContractViolation> violations)
    {
        var data = string.Join("|", violations
            .OrderBy(v => v.RuleId)
            .ThenBy(v => v.Message)
            .Select(v => $"{v.RuleId}:{v.Message}"));

        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash)[..16];
    }

    /// <summary>
    /// Computes a deterministic full SHA256 hex hash from the schema itself
    /// (tables/columns canonicalized by name) - not from violations, so schema
    /// changes that produce no violations still change the hash.
    /// </summary>
    public static string ComputeSchemaHash(IReadOnlyList<SnapshotTable> schema)
        => ComputeSchemaHashCore(schema, null);

    /// <summary>
    /// Computes the canonical v3 schema hash, including provider scope metadata.
    /// </summary>
    public static string ComputeSchemaHash(
        IReadOnlyList<SnapshotTable> schema,
        string? provider,
        string? schemaScope,
        string? canonicalizationVersion = "v1")
    {
        var metadata = string.Join(
            "|",
            provider ?? "unknown-provider",
            schemaScope ?? "unknown-scope",
            canonicalizationVersion ?? "unknown-canonicalizer");
        return ComputeSchemaHashCore(schema, metadata);
    }

    private static string ComputeSchemaHashCore(IReadOnlyList<SnapshotTable> schema, string? metadata)
    {
        var canonicalSchema = string.Join("|", schema
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .Select(t =>
            {
                var cols = string.Join(";", t.Columns
                    .OrderBy(c => c.Name, StringComparer.Ordinal)
                    .Select(c => string.Join(
                        ",",
                        c.Name,
                        c.DataType,
                        c.IsNullable,
                        c.MaxLength?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null",
                        c.CharLength?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null",
                        c.Precision?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null",
                        c.Scale?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null",
                        c.CharUsed ?? "null",
                        c.DataDefault ?? "null",
                        c.ColumnId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null")));
                return $"{t.Name}{{{cols}}}";
            }));
        var canonical = metadata is null ? canonicalSchema : metadata + "||" + canonicalSchema;

        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash);
    }

    /// <summary>Computes the same canonical schema hash directly from a fresh database descriptor.</summary>
    public static string ComputeSchemaHash(DatabaseSchemaDescriptor schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        var snapshot = schema.Tables.Select(table => new SnapshotTable(
            table.Name,
            table.Columns.Select(column => new SnapshotColumn(
                column.Name,
                column.DataType,
                column.MaxLength,
                column.CharLength,
                column.Precision,
                column.Scale,
                column.IsNullable,
                column.CharUsed,
                column.DataDefault,
                column.ColumnId)).ToArray())).ToArray();
        return ComputeSchemaHash(snapshot);
    }

    /// <summary>
    /// Violation-based hash (legacy semantics, 16-hex prefix) used when diffing
    /// snapshots that were never migrated and carry no SchemaHash.
    /// </summary>
    public static string ComputeSchemaHash(IReadOnlyList<BaselineViolation> violations)
    {
        var data = string.Join("|", violations
            .OrderBy(v => v.RuleId, StringComparer.Ordinal)
            .ThenBy(v => v.Message, StringComparer.Ordinal)
            .Select(v => $"{v.RuleId}:{v.Message}"));

        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash)[..16];
    }

    /// <summary>
    /// Filters violations to only return new ones not in baseline.
    /// </summary>
    /// <returns></returns>
    public IEnumerable<ContractViolation> FilterNewViolations(IEnumerable<ContractViolation> current, BaselineFile baseline)
    {
        var baselineSignatures = new HashSet<string>(
            baseline.Violations.Select(v => $"{v.RuleId}:{v.Message}"));

        return current.Where(v => !baselineSignatures.Contains($"{v.RuleId}:{v.Message}"));
    }

    private static string ExtractMajorMinor(string version)
    {
        var match = System.Text.RegularExpressions.Regex.Match(version, @"(\d+)\.(\d+)");
        if (match.Success)
        {
            return $"{match.Groups[1]}.{match.Groups[2]}";
        }

        return version;
    }

    private static BaselineFile MigrateFromLegacy(LegacyBaselineFile legacy)
    {
        return new BaselineFile(
            Version: 2,
            CreatedAt: legacy.CreatedAt,
            SchemaVersion: legacy.SchemaVersion,
            GroundTruthMode: legacy.GroundTruthMode,
            DatabaseVersion: "unknown",
            SchemaHash: ComputeSchemaHashFromLegacy(legacy),
            Violations: legacy.Violations,
            SchemaHashKind: "violation-sha256-prefix");
    }

    /// <summary>
    /// Migrates a legacy (v1) baseline file to v2 in-place. Returns the migrated baseline,
    /// or null when the file is missing or already v2.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public async Task<BaselineFile?> MigrateBaselineAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_baselineFilePath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(_baselineFilePath, cancellationToken);
        LegacyBaselineFile? legacy;
        try
        {
            legacy = JsonSerializer.Deserialize<LegacyBaselineFile>(json);
        }
        catch (JsonException)
        {
            return null; // Corrupt or non-v1 file - treat as "nothing to migrate".
        }

        if (legacy == null)
        {
            return null;
        }

        var migrated = MigrateFromLegacy(legacy);
        await SaveAsync(migrated, cancellationToken);
        return migrated;
    }

    private static string ComputeSchemaHashFromLegacy(LegacyBaselineFile legacy)
    {
        var data = string.Join("|", legacy.Violations
            .OrderBy(v => v.RuleId)
            .ThenBy(v => v.Message)
            .Select(v => $"{v.RuleId}:{v.Message}"));

        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash)[..16];
    }
}

/// <summary>Monotonic cache counters; values are process-local and intentionally contain no baseline content.</summary>
public sealed record BaselineCacheMetrics(long Hits, long Misses);

/// <summary>
/// Legacy baseline file format (version 1) for migration.
/// </summary>
[JsonSerializable(typeof(LegacyBaselineFile))]
internal record LegacyBaselineFile(
    int Version,
    DateTimeOffset CreatedAt,
    string SchemaVersion,
    string GroundTruthMode,
    IReadOnlyList<BaselineViolation> Violations);

/// <summary>
/// Baseline file format. Version 2 adds database/hash metadata; version 3 adds
/// schema hash kind, provider scope and canonicalization metadata.
/// </summary>
[JsonSerializable(typeof(BaselineFile))]
[method: JsonConstructor]
public record BaselineFile(
    int Version,
    DateTimeOffset CreatedAt,
    string SchemaVersion,
    string GroundTruthMode,
    string DatabaseVersion,
    string SchemaHash,
    IReadOnlyList<BaselineViolation> Violations,
    IReadOnlyList<SnapshotTable>? Schema = null,
    string? SchemaHashKind = null,
    string? Provider = null,
    string? SchemaScope = null,
    string? SchemaCanonicalizationVersion = null)
{
    public BaselineFile(
        int version,
        DateTimeOffset createdAt,
        string schemaVersion,
        string groundTruthMode,
        string databaseVersion,
        string schemaHash,
        IReadOnlyList<BaselineViolation> violations,
        IReadOnlyList<SnapshotTable>? schema)
        : this(version, createdAt, schemaVersion, groundTruthMode, databaseVersion, schemaHash, violations, schema, null, null, null, null)
    {
    }

    public void Deconstruct(
        out int version,
        out DateTimeOffset createdAt,
        out string schemaVersion,
        out string groundTruthMode,
        out string databaseVersion,
        out string schemaHash,
        out IReadOnlyList<BaselineViolation> violations,
        out IReadOnlyList<SnapshotTable>? schema)
    {
        version = Version;
        createdAt = CreatedAt;
        schemaVersion = SchemaVersion;
        groundTruthMode = GroundTruthMode;
        databaseVersion = DatabaseVersion;
        schemaHash = SchemaHash;
        violations = Violations;
        schema = Schema;
    }
}

/// <summary>
/// Serializable ground-truth table snapshot (used by Snapshot mode offline validation).
/// </summary>
public record SnapshotTable(
    string Name,
    IReadOnlyList<SnapshotColumn> Columns);

[method: JsonConstructor]
public record SnapshotColumn(
    string Name,
    string DataType,
    int? MaxLength,
    int? CharLength,
    int? Precision,
    int? Scale,
    bool IsNullable,
    string? CharUsed,
    string? DataDefault = null,
    int? ColumnId = null)
{
    public SnapshotColumn(
        string name,
        string dataType,
        int? maxLength,
        int? charLength,
        int? precision,
        int? scale,
        bool isNullable,
        string? charUsed)
        : this(name, dataType, maxLength, charLength, precision, scale, isNullable, charUsed, null, null)
    {
    }

    public void Deconstruct(
        out string name,
        out string dataType,
        out int? maxLength,
        out int? charLength,
        out int? precision,
        out int? scale,
        out bool isNullable,
        out string? charUsed)
    {
        name = Name;
        dataType = DataType;
        maxLength = MaxLength;
        charLength = CharLength;
        precision = Precision;
        scale = Scale;
        isNullable = IsNullable;
        charUsed = CharUsed;
    }
}

/// <summary>
/// A violation in the baseline file.
/// </summary>
public record BaselineViolation(
    string RuleId,
    string Message,
    string Severity,
    BaselineLocation? Location,
    IReadOnlyDictionary<string, object?>? Properties);

/// <summary>
/// Location in baseline file.
/// </summary>
public record BaselineLocation(
    string FilePath,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn);

/// <summary>
/// Summary information about a baseline file.
/// </summary>
public record BaselineInfo(
    string FilePath,
    long FileSizeBytes,
    DateTimeOffset LastModified,
    BaselineFile? Baseline,
    string? ErrorMessage = null)
{
    public bool IsValid => Baseline != null;
    public bool HasViolations => Baseline?.Violations?.Count > 0;
}
