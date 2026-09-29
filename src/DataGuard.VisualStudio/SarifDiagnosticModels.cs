// <copyright file="SarifDiagnosticModels.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

using System.Collections.Generic;

/// <summary>Zero-based editor position derived from a 1-based SARIF region.</summary>
internal readonly struct SarifPosition
{
    public SarifPosition(int line, int column)
    {
        this.Line = line;
        this.Column = column;
    }

    public int Line { get; }

    public int Column { get; }
}

/// <summary>One SARIF result that passed validation and is safe to show in the Error List.</summary>
internal sealed class SarifDiagnostic
{
    public string Document { get; set; } = string.Empty;

    public int Line { get; set; }

    public int Column { get; set; }

    public string? RuleId { get; set; }

    public string Message { get; set; } = string.Empty;

    public string? Level { get; set; }
}

/// <summary>Outcome of reading one SARIF file: accepted diagnostics plus skip/truncation counters.</summary>
internal sealed class SarifLoadResult
{
    public List<SarifDiagnostic> Diagnostics { get; } = new();

    /// <summary>Results dropped because a field was missing, malformed, or outside the solution.</summary>
    public int SkippedCount { get; set; }

    /// <summary>Results dropped because the Error List cap was reached.</summary>
    public int TruncatedCount { get; set; }

    /// <summary>Non-null when the whole document could not be read.</summary>
    public string? Error { get; set; }
}
