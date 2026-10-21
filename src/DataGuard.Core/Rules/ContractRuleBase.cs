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
    private readonly AsyncLocal<StatusCollection?> _activeStatuses = new();

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
    public IReadOnlyList<UnevaluatedContract> DrainUnevaluatedContracts() =>
        _activeStatuses.Value is { } statuses ? statuses.Drain() : _unevaluated.Drain();

    internal StatusCollection BeginStatusCollection() => new(this);

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
        var entry = new UnevaluatedContract(UnevaluatedRuleId, contract.Id, NormalizeUnevaluatedReason(reason ?? string.Empty), contract.Location);
        if (_activeStatuses.Value is { } statuses)
        {
            statuses.Add(entry);
        }
        else
        {
            _unevaluated.Add(entry);
        }
    }

    /// <summary>Normalizes an unevaluated reason to one trimmed line; rules that may echo driver errors override it to sanitize.</summary>
    /// <param name="reason">Raw reason.</param>
    /// <returns>The reason as shown to users.</returns>
    protected virtual string NormalizeUnevaluatedReason(string reason) =>
        string.Join(' ', reason.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>A single engine invocation's statuses for this rule; direct callers keep their separate queue.</summary>
    internal sealed class StatusCollection : IDisposable
    {
        private readonly object _gate = new();
        private readonly List<UnevaluatedContract> _entries = new();
        private ContractRuleBase? _owner;
        private StatusCollection? _previous;
        private bool _closed;

        internal StatusCollection(ContractRuleBase owner)
        {
            _owner = owner;
            _previous = owner._activeStatuses.Value;
            owner._activeStatuses.Value = this;
        }

        internal void Add(UnevaluatedContract entry)
        {
            lock (_gate)
            {
                // Child work can inherit this collection past its invocation. It must never fall back to the rule queue.
                if (!_closed)
                {
                    _entries.Add(entry);
                }
            }
        }

        internal IReadOnlyList<UnevaluatedContract> Drain()
        {
            lock (_gate)
            {
                var entries = _entries.ToArray();
                _entries.Clear();
                return entries;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_closed)
                {
                    return;
                }

                _closed = true;
                _entries.Clear();
                _owner!._activeStatuses.Value = _previous;
                _owner = null;
                _previous = null;
            }
        }
    }
}
