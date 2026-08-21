using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DataGuard.Core.Abstractions;
using Microsoft.CodeAnalysis;

namespace DataGuard.Core.Reporting;

/// <summary>
/// Versioned, redacted evidence artifact for CI and downstream contract consumers.
/// </summary>
public sealed class ContractEvidence
{
    /// <summary>Gets or sets evidence schema version.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Gets or sets database provider used for validation.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Gets or sets stable, sorted validation findings.</summary>
    public List<ContractEvidenceViolation> Violations { get; set; } = new();
}

/// <summary>
/// A redacted validation finding suitable for a durable evidence artifact.
/// </summary>
public sealed class ContractEvidenceViolation
{
    /// <summary>Gets or sets dataGuard rule identifier.</summary>
    public string RuleId { get; set; } = string.Empty;

    /// <summary>Gets or sets normalized severity.</summary>
    public string Severity { get; set; } = string.Empty;

    /// <summary>Gets or sets redacted diagnostic text.</summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Writes deterministic contract evidence without serializing arbitrary violation properties.
/// </summary>
public static class ContractEvidenceWriter
{
    /// <summary>Writes a versioned JSON evidence artifact.</summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public static Task WriteAsync(string outputPath, string provider, IEnumerable<ContractViolation> violations, CancellationToken cancellationToken = default)
    {
        var evidence = new ContractEvidence
        {
            Provider = provider,
            Violations = violations
                .Select(violation => new ContractEvidenceViolation
                {
                    RuleId = violation.RuleId,
                    Severity = ToSeverity(violation.Severity),
                    Message = Redact(violation.Message),
                })
                .OrderBy(violation => violation.RuleId, StringComparer.Ordinal)
                .ThenBy(violation => violation.Severity, StringComparer.Ordinal)
                .ThenBy(violation => violation.Message, StringComparer.Ordinal)
                .ToList(),
        };
        var json = JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return File.WriteAllTextAsync(outputPath, json, cancellationToken);
    }

    private static string ToSeverity(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => "error",
        DiagnosticSeverity.Warning => "warning",
        DiagnosticSeverity.Info => "info",
        _ => "hidden",
    };

    private static string Redact(string message)
    {
        return message.Contains("password=", StringComparison.OrdinalIgnoreCase)
            || message.Contains("token=", StringComparison.OrdinalIgnoreCase)
            || message.Contains("authorization: bearer", StringComparison.OrdinalIgnoreCase)
            ? "[REDACTED]"
            : message;
    }
}
