using System.Text.Json.Serialization;

namespace DataGuard.Core.Assessment;

/// <summary>Request for a read-only assessment of a workspace.</summary>
public sealed record AssessmentRequest
{
    /// <summary>Absolute or relative path to the solution/project root to assess. Must stay inside the workspace.</summary>
    required public string WorkspaceRoot { get; init; }

    /// <summary>Optional glob-like filters (relative paths) limiting which projects are assessed.</summary>
    public IReadOnlyList<string> ProjectFilters { get; init; } = Array.Empty<string>();

    /// <summary>When true, remote advisory lookups may run if explicitly enabled in configuration; default false keeps the run local-only.</summary>
    public bool AllowRemoteLookups { get; init; }
}

/// <summary>Severity levels for assessment findings, ordered from most to least severe.</summary>
public enum FindingSeverity
{
    /// <summary>Blocking problem that invalidates an upgrade path.</summary>
    Critical = 0,

    /// <summary>Likely problem requiring attention.</summary>
    Error = 1,

    /// <summary>Suspected issue needing human review.</summary>
    Warning = 2,

    /// <summary>FYI context with no action implied.</summary>
    Information = 3,
}

/// <summary>Confidence that a finding reflects reality, given evidence quality.</summary>
public enum FindingConfidence
{
    /// <summary>Deterministic fact read from project/config metadata.</summary>
    High = 0,

    /// <summary>Inferred from partial metadata.</summary>
    Medium = 1,

    /// <summary>Heuristic match that requires human confirmation.</summary>
    Low = 2,
}

/// <summary>Pointer to the exact source of a finding's evidence (file + optional key/line).</summary>
public sealed record FindingEvidence
{
    /// <summary>Workspace-relative file path the evidence came from.</summary>
    required public string Path { get; init; }

    /// <summary>Optional property/key/XML element name within the file.</summary>
    public string? Key { get; init; }

    /// <summary>Optional 1-based line number when evidence is line-addressable.</summary>
    public int? Line { get; init; }

    /// <summary>Raw value snippet; must never contain secret material.</summary>
    public string? ValuePreview { get; init; }
}

/// <summary>A single structured assessment finding with stable identity and evidence.</summary>
public sealed record AssessmentFinding
{
    /// <summary>Stable rule identifier, e.g. DG1001.</summary>
    required public string RuleId { get; init; }

    /// <summary>Severity of this finding.</summary>
    required public FindingSeverity Severity { get; init; }

    /// <summary>How confident the rule is in this finding.</summary>
    required public FindingConfidence Confidence { get; init; }

    /// <summary>Human-readable message; must not embed secret values.</summary>
    required public string Message { get; init; }

    /// <summary>Evidence backing this finding; at least one entry per emitted finding.</summary>
    public IReadOnlyList<FindingEvidence> Evidence { get; init; } = Array.Empty<FindingEvidence>();

    /// <summary>Optional deterministic suggested action text; never auto-applied.</summary>
    public string? SuggestedAction { get; init; }

    /// <summary>Target framework(s) this finding applies to, when rule applicability is version-bound.</summary>
    public IReadOnlyList<string> AppliesTo { get; init; } = Array.Empty<string>();
}

/// <summary>Operational error for one assessed unit; siblings continue after any single failure.</summary>
public sealed record ToolError
{
    /// <summary>Stable error code, e.g. DG1002.</summary>
    required public string Code { get; init; }

    /// <summary>Workspace-relative path the error relates to, when applicable.</summary>
    public string? Path { get; init; }

    /// <summary>Human-readable description without sensitive content.</summary>
    required public string Message { get; init; }
}

/// <summary>Structured assessment report returned to callers instead of console text.</summary>
public sealed record AssessmentReport
{
    /// <summary>Bumping schema version whenever serialized shape changes.</summary>
    public const string CurrentSchemaVersion = "1.0";

    /// <summary>Schema version of this report payload.</summary>
    public string SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>DataGuard tool version that produced this report.</summary>
    required public string ToolVersion { get; init; }

    /// <summary>The requested target root, normalized workspace-relative.</summary>
    required public string Target { get; init; }

    /// <summary>UTC timestamp of report generation.</summary>
    required public DateTimeOffset GeneratedAt { get; init; }

    /// <summary>All findings across executed packs, in stable rule order.</summary>
    public IReadOnlyList<AssessmentFinding> Findings { get; init; } = Array.Empty<AssessmentFinding>();

    /// <summary>Per-unit operational errors encountered during assessment.</summary>
    public IReadOnlyList<ToolError> Errors { get; init; } = Array.Empty<ToolError>();

    /// <summary>Coarse counts summary for quick triage.</summary>
    public AssessmentSummary Summary { get; init; } = new AssessmentSummary();
}

/// <summary>Aggregate counts by severity/error for quick display.</summary>
public sealed record AssessmentSummary
{
    /// <summary>Total findings count.</summary>
    public int TotalFindings => Critical + Errors_ + Warnings + Information;

    /// <summary>Critical-severity findings count.</summary>
    public int Critical { get; init; }

    /// <summary>Error-severity findings count.</summary>
    [JsonPropertyName("errors")]
    public int Errors_ { get; init; }

    /// <summary>Warning-severity findings count.</summary>
    public int Warnings { get; init; }

    /// <summary>Information-severity findings count.</summary>
    public int Information { get; init; }

    /// <summary>Operational errors count (distinct from severity).</summary>
    public int ToolErrors { get; init; }
}
