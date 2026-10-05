using DataGuard.Core.Abstractions;
using DataGuard.Core.Validation;
using Microsoft.CodeAnalysis;

namespace DataGuard.Core.Rules;

/// <summary>
/// Base class for contract rules. A rule that cannot obtain ground truth for a contract calls
/// <see cref="MarkUnevaluated"/>; validation engines drain those entries through <see cref="IContractEvaluationStatusReporter"/>.
/// </summary>
public abstract class ContractRuleBase : IContractRule, IContractEvaluationStatusReporter
{
    private readonly UnevaluatedContractCollector _unevaluated = new();

    public abstract string RuleId { get; }

    public abstract string Name { get; }

    public abstract DiagnosticSeverity Severity { get; }

    public abstract string Description { get; }

    public virtual async Task<IReadOnlyList<ContractViolation>> ValidateAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<ContractViolation>();
        await ValidateCoreAsync(contract, allContracts, violations, cancellationToken);
        return violations;
    }

    protected abstract Task ValidateCoreAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        List<ContractViolation> violations,
        CancellationToken cancellationToken);

    protected static ContractViolation CreateViolation(
        string ruleId,
        string message,
        DiagnosticSeverity severity,
        Location? location = null,
        IReadOnlyDictionary<string, object?>? properties = null)
    {
        return new ContractViolation(ruleId, message, severity, location, properties);
    }

    /// <inheritdoc />
    public IReadOnlyList<UnevaluatedContract> DrainUnevaluatedContracts() => _unevaluated.Drain();

    /// <summary>Gets the rule ID reported for unevaluated contracts; defaults to <see cref="RuleId"/>.</summary>
    protected virtual string UnevaluatedRuleId => RuleId;

    /// <summary>
    /// Records that <paramref name="contract"/> could not be evaluated (no verdict: neither a finding nor a pass). The
    /// engines drain these into <see cref="ValidationExecutionResult.UnevaluatedContracts"/>; thread-safe.
    /// </summary>
    /// <param name="contract">The contract that was not evaluated.</param>
    /// <param name="reason">Why; normalized by <see cref="NormalizeUnevaluatedReason"/>.</param>
    protected void MarkUnevaluated(ContractDescriptor contract, string reason)
    {
        ArgumentNullException.ThrowIfNull(contract);
        _unevaluated.Add(new UnevaluatedContract(UnevaluatedRuleId, contract.Id, NormalizeUnevaluatedReason(reason ?? string.Empty), contract.Location));
    }

    /// <summary>Normalizes an unevaluated reason to one trimmed line; rules that may echo driver errors override it to sanitize.</summary>
    /// <param name="reason">Raw reason.</param>
    /// <returns>The reason as shown to users.</returns>
    protected virtual string NormalizeUnevaluatedReason(string reason) =>
        string.Join(' ', reason.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
