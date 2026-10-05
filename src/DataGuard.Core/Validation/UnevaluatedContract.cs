using System.Collections.Concurrent;
using DataGuard.Core.Abstractions;
using Microsoft.CodeAnalysis;

namespace DataGuard.Core.Validation;

/// <summary>Whether a rule actually evaluated one contract (red-team H1/H2).</summary>
public enum ContractEvaluationStatus
{
    /// <summary>The rule compared the contract against ground truth (with or without findings).</summary>
    Evaluated,

    /// <summary>The rule needed ground truth it could not obtain (database error, unsupported statement); no verdict exists.</summary>
    Unevaluated,
}

/// <summary>
/// One contract a rule could not evaluate. It is neither a finding nor a pass: the CLI reports it and exits 3
/// unless <c>--allow-unevaluated</c> is set.
/// </summary>
/// <param name="RuleId">Rule ID shown in text output (for example <c>DG020</c> for an undetermined query shape).</param>
/// <param name="ContractId">The <see cref="ContractDescriptor.Id"/> of the contract that was not evaluated.</param>
/// <param name="Reason">Sanitized, single-line reason; never contains credentials.</param>
/// <param name="Location">Source location of the contract when known.</param>
public sealed record UnevaluatedContract(string RuleId, string ContractId, string Reason, Location? Location = null)
{
    /// <summary>Always <see cref="ContractEvaluationStatus.Unevaluated"/>; present so consumers can switch on one status type.</summary>
    public ContractEvaluationStatus Status => ContractEvaluationStatus.Unevaluated;
}

/// <summary>
/// Implemented by rules that can report contracts they could not evaluate. Validation engines call
/// <see cref="DrainUnevaluatedContracts"/> once after every contract has been offered to the rule.
/// </summary>
public interface IContractEvaluationStatusReporter
{
    /// <summary>Returns and clears the contracts recorded as unevaluated since the previous drain.</summary>
    IReadOnlyList<UnevaluatedContract> DrainUnevaluatedContracts();
}

/// <summary>Thread-safe store a rule uses to record unevaluated contracts; safe under concurrent rule execution.</summary>
public sealed class UnevaluatedContractCollector
{
    private readonly ConcurrentQueue<UnevaluatedContract> _entries = new();

    /// <summary>Records one unevaluated contract.</summary>
    public void Add(UnevaluatedContract entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _entries.Enqueue(entry);
    }

    /// <summary>Returns and removes every recorded entry.</summary>
    public IReadOnlyList<UnevaluatedContract> Drain()
    {
        var drained = new List<UnevaluatedContract>();
        while (_entries.TryDequeue(out var entry))
        {
            drained.Add(entry);
        }

        return drained;
    }
}

/// <summary>Shared drain used by the concurrent engine and by sequential callers (CLI fallback path).</summary>
public static class UnevaluatedContracts
{
    /// <summary>
    /// Drains every distinct rule that implements <see cref="IContractEvaluationStatusReporter"/> and returns the
    /// entries in a deterministic order (rule ID, then contract ID, then reason).
    /// </summary>
    public static IReadOnlyList<UnevaluatedContract> DrainFrom(IEnumerable<IContractRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var drained = new List<UnevaluatedContract>();
        var seen = new HashSet<IContractRule>(ReferenceEqualityComparer.Instance);
        foreach (var rule in rules)
        {
            if (seen.Add(rule) && rule is IContractEvaluationStatusReporter reporter)
            {
                drained.AddRange(reporter.DrainUnevaluatedContracts());
            }
        }

        return Order(drained);
    }

    /// <summary>Deterministic ordering for display and comparison.</summary>
    public static IReadOnlyList<UnevaluatedContract> Order(IEnumerable<UnevaluatedContract> entries) =>
        entries
            .OrderBy(entry => entry.RuleId, StringComparer.Ordinal)
            .ThenBy(entry => entry.ContractId, StringComparer.Ordinal)
            .ThenBy(entry => entry.Reason, StringComparer.Ordinal)
            .ToList();
}
