// <copyright file="ProgressReadModels.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

/// <summary>
/// The optional policy fields of the validate <c>Summary</c> event: how many rules and contracts were
/// not evaluated, how many acquisition diagnostics were printed, and what ground truth was compared
/// (<c>none</c>, <c>entities</c>, <c>schema</c> or <c>schema+entities</c>). Absent fields stay null.
/// </summary>
internal readonly struct SummaryDetails
{
    public SummaryDetails(int? unevaluatedContractCount, int? unavailableRuleCount, int? acquisitionDiagnosticCount, string? groundTruth)
    {
        this.UnevaluatedContractCount = unevaluatedContractCount;
        this.UnavailableRuleCount = unavailableRuleCount;
        this.AcquisitionDiagnosticCount = acquisitionDiagnosticCount;
        this.GroundTruth = groundTruth;
    }

    public int? UnevaluatedContractCount { get; }

    public int? UnavailableRuleCount { get; }

    public int? AcquisitionDiagnosticCount { get; }

    public string? GroundTruth { get; }
}

/// <summary>Result of parsing one CLI stderr line.</summary>
internal readonly struct ParsedProgress
{
    public ParsedProgress(string? formattedOutput, int? errorCount, int? warningCount)
        : this(formattedOutput, errorCount, warningCount, null, false)
    {
    }

    public ParsedProgress(string? formattedOutput, int? errorCount, int? warningCount, RuleInventoryItem? inventoryEntry, bool isProgressEvent)
        : this(formattedOutput, errorCount, warningCount, inventoryEntry, isProgressEvent, default)
    {
    }

    public ParsedProgress(string? formattedOutput, int? errorCount, int? warningCount, RuleInventoryItem? inventoryEntry, bool isProgressEvent, SummaryDetails summary)
    {
        this.FormattedOutput = formattedOutput;
        this.ErrorCount = errorCount;
        this.WarningCount = warningCount;
        this.InventoryEntry = inventoryEntry;
        this.IsProgressEvent = isProgressEvent;
        this.Summary = summary;
    }

    public string? FormattedOutput { get; }

    public int? ErrorCount { get; }

    public int? WarningCount { get; }

    public RuleInventoryItem? InventoryEntry { get; }

    /// <summary>Policy fields of the Summary event; all null unless <see cref="IsSummary"/>.</summary>
    public SummaryDetails Summary { get; }

    /// <summary>True when the line was a well-formed progress event (any Kind the parser understands).</summary>
    public bool IsProgressEvent { get; }

    /// <summary>True when this line carried the final Summary event.</summary>
    public bool IsSummary => this.ErrorCount.HasValue && this.WarningCount.HasValue;
}

/// <summary>Aggregate of everything learned from the CLI stderr stream during one run.</summary>
internal sealed class ProgressReadResult
{
    public int ErrorCount { get; set; }

    public int WarningCount { get; set; }

    public bool HasSummary { get; set; }

    /// <summary>Contracts no rule could evaluate after ground truth loaded (Summary <c>UnevaluatedContractCount</c>); null when absent.</summary>
    public int? UnevaluatedContractCount { get; set; }

    /// <summary>Rules unavailable for the configured provider or prerequisites (Summary <c>UnavailableRuleCount</c>); null when absent.</summary>
    public int? UnavailableRuleCount { get; set; }

    /// <summary>Acquisition diagnostics the CLI printed as <c>ACQUISITION:</c> lines (Summary <c>AcquisitionDiagnosticCount</c>); null when absent.</summary>
    public int? AcquisitionDiagnosticCount { get; set; }

    /// <summary>Ground truth the CLI compared (<c>none</c>, <c>entities</c>, <c>schema</c>, <c>schema+entities</c>); null when the Summary did not say.</summary>
    public string? GroundTruth { get; set; }

    /// <summary>
    /// Set when a raw stderr line started with the CLI's exact "no ground truth" notice: only syntactic
    /// rules ran and the database contract was not compared. Never set from a substring.
    /// </summary>
    public bool GroundTruthMissing { get; set; }

    /// <summary>
    /// Set when a stderr line started with the System.CommandLine rejection of <c>--ide-safe</c>.
    /// A raw observation only; the publish verdict is made by <see cref="PublishGate"/>.
    /// </summary>
    public bool IdeSafeUnsupported { get; set; }

    /// <summary>Set when the first non-empty stderr line was exactly <c>ide-safe: active</c>.</summary>
    public bool IdeSafeAcknowledged { get; set; }

    /// <summary>Set when at least one well-formed progress event was parsed.</summary>
    public bool SawAnyProgressEvent { get; set; }
}

/// <summary>What the package may do with a finished run.</summary>
internal enum PublishVerdict
{
    /// <summary>The CLI confirmed ide-safe mode; results may be loaded.</summary>
    Publish,

    /// <summary>The CLI rejected <c>--ide-safe</c> and did nothing else: an old executable.</summary>
    CliTooOld,

    /// <summary>The CLI never confirmed ide-safe mode; results are discarded (fail closed).</summary>
    HandshakeMissing,
}

/// <summary>
/// Publishing requires the positive <c>ide-safe: active</c> handshake. "CLI too old" is recognised only
/// by the full signature of an old System.CommandLine rejection (exit 1, no progress events, rejection
/// line) so that file names or messages containing the same words cannot spoof it.
/// </summary>
internal static class PublishGate
{
    internal static PublishVerdict Decide(ProgressReadResult progress, int exitCode)
    {
        if (progress.IdeSafeAcknowledged)
        {
            return PublishVerdict.Publish;
        }

        if (progress.IdeSafeUnsupported && !progress.SawAnyProgressEvent && exitCode == 1)
        {
            return PublishVerdict.CliTooOld;
        }

        return PublishVerdict.HandshakeMissing;
    }
}
