namespace DataGuard.Core.Reporting;

/// <summary>
/// Identifies a safe, structural milestone in a DataGuard operation.
/// </summary>
public enum ProgressEventKind
{
    PhaseStarted,
    PhaseCompleted,
    ContractDiscovered,
    RuleExecuted,
    Summary,
}

/// <summary>
/// A line-delimited progress payload intended for operator-facing consumers.
/// Values must not contain credentials or connection strings.
/// </summary>
public sealed record ProgressEvent(
    ProgressEventKind Kind,
    string Phase,
    string? Detail,
    IReadOnlyDictionary<string, object?>? Data = null);
