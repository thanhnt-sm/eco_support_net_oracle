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

    private static readonly HashSet<string> SafePropertyKeys = new(StringComparer.Ordinal)
    {
        "column", "columnMaxBytes", "columnMaxLength", "dbColumnType",
        "entityMaxBytes", "entityMaxLength", "function", "inferredType",
        "keyword", "operator", "property", "referencedIssue", "semantics",
        "suggestion", "syntax", "table", "type",
    };

    public void AddSarifSink(ISarifSink sink) => _sarifSinks.Add(sink);

    public void AddDiagnosticSink(IDiagnosticSink sink) => _diagnosticSinks.Add(sink);

    public async Task EmitAsync(
        IEnumerable<ContractViolation> violations,
        CancellationToken cancellationToken = default)
    {
        var sarifLog = CreateSarifLog(violations);

        foreach (var sink in _sarifSinks)
        {
            await sink.WriteAsync(sarifLog, cancellationToken);
        }

        foreach (var sink in _diagnosticSinks)
        {
            await sink.WriteAsync(violations, cancellationToken);
        }
    }

    private SarifLog CreateSarifLog(IEnumerable<ContractViolation> violations)
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
                                    Uri = v.Location.SourceTree?.FilePath ?? "",
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

    private static PropertyBag CreateSafeProperties(IReadOnlyDictionary<string, object?>? properties)
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

    private static bool IsSafePropertyValue(object? value)
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

    private static string SafeText(string? text) =>
        string.IsNullOrEmpty(text) || !ContainsSensitiveValue(text) ? text ?? string.Empty : "[REDACTED]";

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

    public FileSarifSink(string outputPath, bool streaming = false)
    {
        _outputPath = outputPath ?? throw new ArgumentNullException(nameof(outputPath));
        _streaming = streaming;
    }

    public async Task WriteAsync(SarifLog log, CancellationToken cancellationToken = default)
    {
        if (_streaming)
        {
            await WriteStreamingAsync(log, cancellationToken);
        }
        else
        {
            var json = log.ToJson();
            await File.WriteAllTextAsync(_outputPath, json, cancellationToken);
        }
    }

    private async Task WriteStreamingAsync(SarifLog log, CancellationToken cancellationToken)
    {
        // Stream SARIF output directly to file without loading full object graph
        await using var fileStream = new FileStream(_outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
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
    }
}

/// <summary>
/// Streaming SARIF sink for very large codebases - writes directly to stream without buffering.
/// </summary>
public class StreamingSarifSink : ISarifSink
{
    private readonly string _outputPath;
    private readonly int _bufferSize;

    public StreamingSarifSink(string outputPath, int bufferSize = 81920)
    {
        _outputPath = outputPath ?? throw new ArgumentNullException(nameof(outputPath));
        _bufferSize = bufferSize;
    }

    public async Task WriteAsync(IEnumerable<ContractViolation> violations, CancellationToken cancellationToken = default)
    {
        await using var fileStream = new FileStream(_outputPath, FileMode.Create, FileAccess.Write, FileShare.None, _bufferSize, true);
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
            writer.WriteString("id", ruleId);
            writer.WriteString("name", ruleId);
            writer.WritePropertyName("shortDescription");
            writer.WriteStartObject();
            writer.WriteString("text", ruleId);
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
            writer.WriteString("ruleId", violation.RuleId);
            writer.WritePropertyName("message");
            writer.WriteStartObject();
            writer.WriteString("text", violation.Message);
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
                writer.WriteString("uri", violation.Location.SourceTree?.FilePath ?? "");
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
                foreach (var prop in violation.Properties)
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
        // Convert violations from log and use streaming write
        var violations = log.Runs?.SelectMany(r => r.Results ?? Enumerable.Empty<Result>())
            .Select(r => new ContractViolation(r.RuleId, r.Message?.Text ?? "", Enum.Parse<DiagnosticSeverity>(r.Level, true)))
            ?? Enumerable.Empty<ContractViolation>();

        return WriteAsync(violations, cancellationToken);
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
            Console.WriteLine($"[{severity}] {violation.RuleId}: {violation.Message}{location}");
        }

        await Task.CompletedTask;
    }
}