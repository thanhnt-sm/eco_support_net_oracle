using System.Text.Json;
using DataGuard.Core.Abstractions;
using Microsoft.CodeAnalysis;

namespace DataGuard.Core.Reporting;

/// <summary>
/// Emits diagnostics in multiple formats.
/// </summary>
public class DiagnosticEmitter
{
    private readonly List<ISarifSink> _sarifSinks = new();
    private readonly List<IDiagnosticSink> _diagnosticSinks = new();
    private readonly string? _sourceRoot;

    internal static readonly HashSet<string> SafePropertyKeys = new(StringComparer.Ordinal)
    {
        "column", "columnMaxBytes", "columnMaxLength", "dbColumnType",
        "entityMaxBytes", "entityMaxLength", "function", "inferredType",
        "keyword", "operator", "property", "referencedIssue", "semantics",
        "suggestion", "syntax", "table", "type",
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="DiagnosticEmitter"/> class.
    /// Artifact paths are projected relative to an operator-controlled source root;
    /// paths outside that root are omitted.
    /// </summary>
    public DiagnosticEmitter(string? sourceRoot = null)
    {
        _sourceRoot = sourceRoot ?? Directory.GetCurrentDirectory();
    }

    public void AddSarifSink(ISarifSink sink) => _sarifSinks.Add(sink);

    public void AddDiagnosticSink(IDiagnosticSink sink) => _diagnosticSinks.Add(sink);

    public async Task EmitAsync(
        IEnumerable<ContractViolation> violations,
        CancellationToken cancellationToken = default)
    {
        var sarifLog = CreateSarifLog(violations, _sourceRoot);

        foreach (var sink in _sarifSinks)
        {
            await sink.WriteAsync(sarifLog, cancellationToken);
        }

        foreach (var sink in _diagnosticSinks)
        {
            await sink.WriteAsync(violations, cancellationToken);
        }
    }

    internal static SarifLog CreateSarifLog(IEnumerable<ContractViolation> violations, string? sourceRoot)
    {
        var run = new Run
        {
            Tool = new Tool
            {
                Driver = new ToolComponent
                {
                    Name = "DataGuard",
                    Version = "0.1.0-alpha.1",
                    InformationUri = "https://github.com/DataGuard/DataGuard",
                    Rules = violations
                        .GroupBy(v => v.RuleId)
                        .Select(g => new ReportingDescriptor
                        {
                            Id = g.Key,
                            Name = SafeText(g.First().Message).Split(':')[0],
                            ShortDescription = new MultiformatMessageString
                            {
                                Text = SafeText(g.First().Message)
                            },
                            DefaultConfiguration = new ReportingConfiguration
                            {
                                Level = g.First().Severity switch
                                {
                                    DiagnosticSeverity.Error => "error",
                                    DiagnosticSeverity.Warning => "warning",
                                    DiagnosticSeverity.Info => "note",
                                    _ => "none"
                                }
                            }
                        }).ToList()
                },
            },
            Results = violations.Select(v => new Result
            {
                RuleId = v.RuleId,
                Message = new Message { Text = SafeText(v.Message) },
                Level = v.Severity switch
                {
                    DiagnosticSeverity.Error => "error",
                    DiagnosticSeverity.Warning => "warning",
                    DiagnosticSeverity.Info => "note",
                    _ => "none"
                },
                Locations = v.Location != null
                    ? new List<SarifLocation>
                    {
                        new SarifLocation
                        {
                            PhysicalLocation = new PhysicalLocation
                            {
                                ArtifactLocation = new ArtifactLocation
                                {
                                    Uri = ProjectArtifactUri(v.Location.SourceTree?.FilePath, sourceRoot),
                                    UriBaseId = "%SRCROOT%"
                                },
                                Region = new Region
                                {
                                    StartLine = v.Location.GetLineSpan().StartLinePosition.Line + 1,
                                    StartColumn = v.Location.GetLineSpan().StartLinePosition.Character + 1,
                                    EndLine = v.Location.GetLineSpan().EndLinePosition.Line + 1,
                                    EndColumn = v.Location.GetLineSpan().EndLinePosition.Character + 1
                                }
                            }
                        }
                    }
                    : new List<SarifLocation>(),
                Properties = CreateSafeProperties(v.Properties)
            }).ToList(),
        };

        return new SarifLog
        {
            Runs = new List<Run> { run },
        };
    }

    internal static PropertyBag CreateSafeProperties(IReadOnlyDictionary<string, object?>? properties)
    {
        if (properties == null)
        {
            return new PropertyBag();
        }

        var safeProperties = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in properties)
        {
            if (SafePropertyKeys.Contains(key) && IsSafePropertyValue(value))
            {
                safeProperties[key] = value;
            }
        }

        return new PropertyBag(safeProperties);
    }

    internal static bool IsSafePropertyValue(object? value)
    {
        if (ContainsSensitiveValue(value))
        {
            return false;
        }

        return value is null
            || value is string
            || value is bool
            || value is byte or sbyte or short or ushort or int or uint or long or ulong
            || value is float or double or decimal
            || value.GetType().IsEnum;
    }

    internal static string SafeText(string? text) =>
        string.IsNullOrEmpty(text) || !ContainsSensitiveValue(text) ? text ?? string.Empty : "[REDACTED]";

    internal static string ProjectArtifactUri(string? path, string? sourceRoot)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(sourceRoot))
        {
            return string.Empty;
        }

        try
        {
            var root = Path.GetFullPath(sourceRoot);
            var fullPath = Path.IsPathFullyQualified(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(path, root);
            var relative = Path.GetRelativePath(root, fullPath);
            if (relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal)
                || Path.IsPathFullyQualified(relative) || ContainsSensitivePathComponent(relative))
            {
                return string.Empty;
            }

            return NormalizePath(relative);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            return string.Empty;
        }
    }

    internal static SarifLog SanitizeSarifLog(SarifLog log, string? sourceRoot)
    {
        ArgumentNullException.ThrowIfNull(log);

        return new SarifLog
        {
            Version = log.Version,
            SchemaUri = log.SchemaUri,
            Runs = (log.Runs ?? new List<Run>()).Select(run => new Run
            {
                Tool = new Tool
                {
                    Driver = new ToolComponent
                    {
                        Name = SafeText(run.Tool?.Driver?.Name),
                        Version = SafeText(run.Tool?.Driver?.Version),
                        InformationUri = SafeText(run.Tool?.Driver?.InformationUri),
                        Rules = (run.Tool?.Driver?.Rules ?? new List<ReportingDescriptor>()).Select(rule => new ReportingDescriptor
                        {
                            Id = SafeText(rule.Id),
                            Name = SafeText(rule.Name),
                            ShortDescription = new MultiformatMessageString { Text = SafeText(rule.ShortDescription?.Text) },
                            DefaultConfiguration = new ReportingConfiguration { Level = SafeText(rule.DefaultConfiguration?.Level) },
                        }).ToList(),
                    },
                },
                Results = (run.Results ?? new List<Result>()).Select(result => new Result
                {
                    RuleId = SafeText(result.RuleId),
                    Message = new Message { Text = SafeText(result.Message?.Text) },
                    Level = SafeText(result.Level),
                    Locations = (result.Locations ?? new List<SarifLocation>()).Select(location => new SarifLocation
                    {
                        PhysicalLocation = new PhysicalLocation
                        {
                            ArtifactLocation = new ArtifactLocation
                            {
                                Uri = ProjectArtifactUri(location.PhysicalLocation?.ArtifactLocation?.Uri, sourceRoot),
                                UriBaseId = SafeText(location.PhysicalLocation?.ArtifactLocation?.UriBaseId),
                            },
                            Region = new Region
                            {
                                StartLine = location.PhysicalLocation?.Region?.StartLine ?? 0,
                                StartColumn = location.PhysicalLocation?.Region?.StartColumn ?? 0,
                                EndLine = location.PhysicalLocation?.Region?.EndLine ?? 0,
                                EndColumn = location.PhysicalLocation?.Region?.EndColumn ?? 0,
                            },
                        },
                    }).ToList(),
                    Properties = CreateSafeProperties(result.Properties.ToDictionary(pair => pair.Key, pair => (object?)pair.Value)),
                }).ToList(),
            }).ToList(),
        };
    }

    private static bool ContainsSensitivePathComponent(string path) =>
        path.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
            .Any(component => ContainsSensitiveValue(component));

    private static string NormalizePath(string path) => path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

    private static bool ContainsSensitiveValue(object? value)
    {
        if (value is not string text)
        {
            return false;
        }

        return text.Contains("password=", StringComparison.OrdinalIgnoreCase)
            || text.Contains("pwd=", StringComparison.OrdinalIgnoreCase)
            || text.Contains("connectionstring=", StringComparison.OrdinalIgnoreCase)
            || text.Contains("connection string=", StringComparison.OrdinalIgnoreCase)
            || text.Contains("access_token=", StringComparison.OrdinalIgnoreCase)
            || text.Contains("token=", StringComparison.OrdinalIgnoreCase)
            || text.Contains("token:", StringComparison.OrdinalIgnoreCase)
            || text.Contains("secret=", StringComparison.OrdinalIgnoreCase)
            || text.Contains("secret:", StringComparison.OrdinalIgnoreCase)
            || text.Contains("authorization: bearer", StringComparison.OrdinalIgnoreCase)
            || text.Contains("api_key=", StringComparison.OrdinalIgnoreCase)
            || text.Contains("api-key:", StringComparison.OrdinalIgnoreCase)
            || text.Contains("api key=", StringComparison.OrdinalIgnoreCase)
            || (text.StartsWith("eyJ", StringComparison.Ordinal) && text.Length > 20);
    }
}

/// <summary>
/// Sink for SARIF output.
/// </summary>
public interface ISarifSink
{
    Task WriteAsync(SarifLog log, CancellationToken cancellationToken = default);
}

/// <summary>
/// Sink for diagnostic output (console, file, etc.).
/// </summary>
public interface IDiagnosticSink
{
    Task WriteAsync(IEnumerable<ContractViolation> violations, CancellationToken cancellationToken = default);
}

/// <summary>
/// File-based SARIF sink with streaming support for large codebases.
/// </summary>
public class FileSarifSink : ISarifSink
{
    private readonly string _outputPath;
    private readonly bool _streaming;
    private readonly string? _sourceRoot;

    public FileSarifSink(string outputPath, bool streaming = false, string? sourceRoot = null)
    {
        _outputPath = outputPath ?? throw new ArgumentNullException(nameof(outputPath));
        _streaming = streaming;
        _sourceRoot = sourceRoot ?? Directory.GetCurrentDirectory();
    }

    public async Task WriteAsync(SarifLog log, CancellationToken cancellationToken = default)
    {
        var safeLog = DiagnosticEmitter.SanitizeSarifLog(log, _sourceRoot);
        if (_streaming)
        {
            await WriteStreamingAsync(safeLog, cancellationToken);
        }
        else
        {
            var json = safeLog.ToJson();
            await ContractExportWriter.WriteAtomicallyAsync(_outputPath, json, cancellationToken);
        }
    }

    private async Task WriteStreamingAsync(SarifLog log, CancellationToken cancellationToken)
    {
        // Stream SARIF output directly to file without loading full object graph
        var directory = Path.GetDirectoryName(Path.GetFullPath(_outputPath))!;
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(_outputPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using var fileStream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            await using var writer = new System.Text.Json.Utf8JsonWriter(fileStream, new JsonWriterOptions { Indented = true });

            writer.WriteStartObject();
            writer.WriteString("version", log.Version ?? "2.1.0");
            writer.WriteString("$schema", log.SchemaUri ?? "https://schemastore.org/schemas/json/sarif-2.1.0.json");

            writer.WritePropertyName("runs");
            writer.WriteStartArray();

            foreach (var run in log.Runs ?? Enumerable.Empty<Run>())
            {
                writer.WriteStartObject();

                // Tool
                writer.WritePropertyName("tool");
                writer.WriteStartObject();
                writer.WritePropertyName("driver");
                writer.WriteStartObject();
                writer.WriteString("name", run.Tool?.Driver?.Name ?? "DataGuard");
                writer.WriteString("version", run.Tool?.Driver?.Version ?? "0.1.0");
                writer.WriteString("informationUri", run.Tool?.Driver?.InformationUri ?? "https://github.com/DataGuard/DataGuard");

                // Rules
                writer.WritePropertyName("rules");
                writer.WriteStartArray();
                foreach (var rule in run.Tool?.Driver?.Rules ?? Enumerable.Empty<ReportingDescriptor>())
                {
                    writer.WriteStartObject();
                    writer.WriteString("id", rule.Id ?? "");
                    writer.WriteString("name", rule.Name ?? "");
                    writer.WritePropertyName("shortDescription");
                    writer.WriteStartObject();
                    writer.WriteString("text", rule.ShortDescription?.Text ?? "");
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
                writer.WriteEndObject();

                // Results - stream one by one
                writer.WritePropertyName("results");
                writer.WriteStartArray();
                foreach (var result in run.Results ?? Enumerable.Empty<Result>())
                {
                    writer.WriteStartObject();
                    writer.WriteString("ruleId", result.RuleId ?? "");

                    writer.WritePropertyName("message");
                    writer.WriteStartObject();
                    writer.WriteString("text", result.Message?.Text ?? "");
                    writer.WriteEndObject();

                    writer.WriteString("level", result.Level ?? "error");

                    // Locations
                    if (result.Locations?.Any() == true)
                    {
                        writer.WritePropertyName("locations");
                        writer.WriteStartArray();
                        foreach (var loc in result.Locations)
                        {
                            writer.WriteStartObject();
                            writer.WritePropertyName("physicalLocation");
                            writer.WriteStartObject();
                            writer.WritePropertyName("artifactLocation");
                            writer.WriteStartObject();
                            writer.WriteString("uri", loc.PhysicalLocation?.ArtifactLocation?.Uri ?? "");
                            writer.WriteString("uriBaseId", loc.PhysicalLocation?.ArtifactLocation?.UriBaseId ?? "%SRCROOT%");
                            writer.WriteEndObject();
                            writer.WritePropertyName("region");
                            writer.WriteStartObject();
                            writer.WriteNumber("startLine", loc.PhysicalLocation?.Region?.StartLine ?? 0);
                            writer.WriteNumber("startColumn", loc.PhysicalLocation?.Region?.StartColumn ?? 0);
                            writer.WriteNumber("endLine", loc.PhysicalLocation?.Region?.EndLine ?? 0);
                            writer.WriteNumber("endColumn", loc.PhysicalLocation?.Region?.EndColumn ?? 0);
                            writer.WriteEndObject();
                            writer.WriteEndObject();
                            writer.WriteEndObject();
                        }

                        writer.WriteEndArray();
                    }

                    // Properties
                    if (result.Properties?.Count > 0)
                    {
                        writer.WritePropertyName("properties");
                        writer.WriteStartObject();
                        foreach (var prop in result.Properties)
                        {
                            writer.WriteString(prop.Key, prop.Value?.ToString() ?? "");
                        }

                        writer.WriteEndObject();
                    }

                    writer.WriteEndObject();
                }

                writer.WriteEndArray();

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            await writer.FlushAsync(cancellationToken);
            fileStream.Flush(flushToDisk: true);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(tempPath, _outputPath, overwrite: true);
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
}

/// <summary>
/// Streaming SARIF sink for very large codebases - writes directly to stream without buffering.
/// </summary>
public class StreamingSarifSink : ISarifSink
{
    private readonly string _outputPath;
    private readonly int _bufferSize;
    private readonly string? _sourceRoot;

    public StreamingSarifSink(string outputPath, int bufferSize = 81920, string? sourceRoot = null)
    {
        _outputPath = outputPath ?? throw new ArgumentNullException(nameof(outputPath));
        _bufferSize = bufferSize;
        _sourceRoot = sourceRoot ?? Directory.GetCurrentDirectory();
    }

    public async Task WriteAsync(IEnumerable<ContractViolation> violations, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(_outputPath))!;
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(_outputPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await WriteToPathAsync(tempPath, violations, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(tempPath, _outputPath, overwrite: true);
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

    private async Task WriteToPathAsync(string outputPath, IEnumerable<ContractViolation> violations, CancellationToken cancellationToken)
    {
        await using var fileStream = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, _bufferSize, true);
        await using var writer = new System.Text.Json.Utf8JsonWriter(fileStream, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();
        writer.WriteString("version", "2.1.0");
        writer.WriteString("$schema", "https://schemastore.org/schemas/json/sarif-2.1.0.json");

        writer.WritePropertyName("runs");
        writer.WriteStartArray();
        writer.WriteStartObject();

        writer.WritePropertyName("tool");
        writer.WriteStartObject();
        writer.WritePropertyName("driver");
        writer.WriteStartObject();
        writer.WriteString("name", "DataGuard");
        writer.WriteString("version", "0.1.0");
        writer.WriteString("informationUri", "https://github.com/DataGuard/DataGuard");

        // Collect unique rule IDs
        var ruleIds = violations.Select(v => v.RuleId).Distinct().ToArray();
        writer.WritePropertyName("rules");
        writer.WriteStartArray();
        foreach (var ruleId in ruleIds)
        {
            writer.WriteStartObject();
            writer.WriteString("id", DiagnosticEmitter.SafeText(ruleId));
            writer.WriteString("name", DiagnosticEmitter.SafeText(ruleId));
            writer.WritePropertyName("shortDescription");
            writer.WriteStartObject();
            writer.WriteString("text", DiagnosticEmitter.SafeText(ruleId));
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteEndObject();

        writer.WritePropertyName("results");
        writer.WriteStartArray();

        var flushCounter = 0;
        foreach (var violation in violations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            writer.WriteStartObject();
            writer.WriteString("ruleId", DiagnosticEmitter.SafeText(violation.RuleId));
            writer.WritePropertyName("message");
            writer.WriteStartObject();
            writer.WriteString("text", DiagnosticEmitter.SafeText(violation.Message));
            writer.WriteEndObject();
            writer.WriteString("level", violation.Severity.ToString().ToLowerInvariant());

            if (violation.Location != null)
            {
                writer.WritePropertyName("locations");
                writer.WriteStartArray();
                writer.WriteStartObject();
                writer.WritePropertyName("physicalLocation");
                writer.WriteStartObject();
                writer.WritePropertyName("artifactLocation");
                writer.WriteStartObject();
                writer.WriteString("uri", DiagnosticEmitter.ProjectArtifactUri(violation.Location.SourceTree?.FilePath, _sourceRoot));
                writer.WriteString("uriBaseId", "%SRCROOT%");
                writer.WriteEndObject();
                writer.WritePropertyName("region");
                writer.WriteStartObject();
                writer.WriteNumber("startLine", violation.Location.GetLineSpan().StartLinePosition.Line + 1);
                writer.WriteNumber("startColumn", violation.Location.GetLineSpan().StartLinePosition.Character + 1);
                writer.WriteNumber("endLine", violation.Location.GetLineSpan().EndLinePosition.Line + 1);
                writer.WriteNumber("endColumn", violation.Location.GetLineSpan().EndLinePosition.Character + 1);
                writer.WriteEndObject();
                writer.WriteEndObject();
                writer.WriteEndObject();
                writer.WriteEndArray();
            }

            if (violation.Properties?.Count > 0)
            {
                writer.WritePropertyName("properties");
                writer.WriteStartObject();
                foreach (var prop in DiagnosticEmitter.CreateSafeProperties(violation.Properties))
                {
                    writer.WriteString(prop.Key, prop.Value?.ToString() ?? "");
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            if (++flushCounter % 1000 == 0)
            {
                await writer.FlushAsync(cancellationToken);
            }
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteEndArray();
        writer.WriteEndObject();
        await writer.FlushAsync(cancellationToken);
    }

    public Task WriteAsync(SarifLog log, CancellationToken cancellationToken = default)
    {
        // Keep locations, safe properties, and run metadata from an already-buffered
        // SARIF log while applying the same boundary sanitizer used by every sink.
        return new FileSarifSink(_outputPath, streaming: true, sourceRoot: _sourceRoot)
            .WriteAsync(log, cancellationToken);
    }
}

/// <summary>
/// Console diagnostic sink.
/// </summary>
public class ConsoleDiagnosticSink : IDiagnosticSink
{
    public async Task WriteAsync(IEnumerable<ContractViolation> violations, CancellationToken cancellationToken = default)
    {
        foreach (var violation in violations)
        {
            var severity = violation.Severity.ToString().ToUpperInvariant();
            var location = violation.Location != null
                ? $" ({violation.Location.GetLineSpan().StartLinePosition.Line + 1}:{violation.Location.GetLineSpan().StartLinePosition.Character + 1})"
                : "";
            Console.WriteLine($"[{severity}] {DiagnosticEmitter.SafeText(violation.RuleId)}: {DiagnosticEmitter.SafeText(violation.Message)}{location}");
        }

        await Task.CompletedTask;
    }
}
