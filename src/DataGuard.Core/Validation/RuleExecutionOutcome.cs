using DataGuard.Core.Abstractions;

namespace DataGuard.Core.Validation;

/// <summary>
/// Describes whether a requested rule was actually evaluated and the findings it produced.
/// This additive envelope preserves the existing <see cref="IContractRule"/> contract.
/// </summary>
public sealed record RuleExecutionOutcome(
    string RuleId,
    RuleExecutionState State,
    IReadOnlyList<ContractViolation> Violations,
    string? PrerequisiteReason = null,
    string? FailureReason = null)
{
    /// <summary>True when the requested rule could not be evaluated completely.</summary>
    public bool MakesValidationIncomplete => State is RuleExecutionState.Unavailable or RuleExecutionState.Failed;
}

/// <summary>Observable evaluation state for one selected rule.</summary>
public enum RuleExecutionState
{
    Evaluated,
    Unavailable,
    Skipped,
    Failed,
}
