using System.Text.Json;
using DataGuard.Core.Assessment;

namespace DataGuard.Core.Assessment.Internal;

/// <summary>
/// Serializes AssessmentReport to JSON using the tool-wide serializer conventions.
/// Machine-readable output never goes to stdout when a file sink is configured.
/// </summary>
public static class AssessmentReportWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Writes the report as indented JSON to the given file path.</summary>
    public static async Task WriteJsonAsync(AssessmentReport report, string outputPath, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(report, Options);
        await DataGuard.Core.Reporting.ContractExportWriter.WriteAtomicallyAsync(outputPath, json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Serializes the report to a JSON string (for tests and programmatic callers).</summary>
    public static string ToJson(AssessmentReport report) => JsonSerializer.Serialize(report, Options);
}
