// <copyright file="ProgressReadModels.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

/// <summary>Result of parsing one CLI stderr line.</summary>
internal readonly struct ParsedProgress
{
    public ParsedProgress(string? formattedOutput, int? errorCount, int? warningCount)
        : this(formattedOutput, errorCount, warningCount, null)
    {
    }

    public ParsedProgress(string? formattedOutput, int? errorCount, int? warningCount, RuleInventoryItem? inventoryEntry)
    {
        this.FormattedOutput = formattedOutput;
        this.ErrorCount = errorCount;
        this.WarningCount = warningCount;
        this.InventoryEntry = inventoryEntry;
    }

    public string? FormattedOutput { get; }

    public int? ErrorCount { get; }

    public int? WarningCount { get; }

    public RuleInventoryItem? InventoryEntry { get; }

    /// <summary>True when this line carried the final Summary event.</summary>
    public bool IsSummary => this.ErrorCount.HasValue && this.WarningCount.HasValue;
}

/// <summary>Aggregate of everything learned from the CLI stderr stream during one run.</summary>
internal sealed class ProgressReadResult
{
    public int ErrorCount { get; set; }

    public int WarningCount { get; set; }

    public bool HasSummary { get; set; }

    /// <summary>
    /// Set when the CLI reported that it does not understand <c>--ide-safe</c>. The run must be
    /// treated as failed; the extension never retries without the flag.
    /// </summary>
    public bool IdeSafeUnsupported { get; set; }
}
