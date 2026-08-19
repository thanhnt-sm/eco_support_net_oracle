using System.Collections.Concurrent;
using DataGuard.Core.Abstractions;

namespace DataGuard.Core.Validation;

/// <summary>
/// Runs contract rules concurrently with bounded parallelism (backpressure).
/// MaxDegreeOfParallelism bounds memory: at most N rule executions run at once,
/// each contributing at most its own violations before being collected.
/// </summary>
public sealed class ConcurrentValidationEngine
{
    private readonly int _maxDegreeOfParallelism;
    private readonly int _maxViolationQueueSize;

    public ConcurrentValidationEngine(int maxDegreeOfParallelism = 0, int maxViolationQueueSize = 100_000)
    {
        _maxDegreeOfParallelism = maxDegreeOfParallelism > 0
            ? maxDegreeOfParallelism
            : Math.Max(1, Environment.ProcessorCount);
        _maxViolationQueueSize = maxViolationQueueSize > 0 ? maxViolationQueueSize : 100_000;
    }

    public async Task<IReadOnlyList<ContractViolation>> ValidateAsync(
        IReadOnlyList<ContractDescriptor> contracts,
        IReadOnlyList<IContractRule> rules,
        CancellationToken cancellationToken = default)
    {
        var results = new ConcurrentBag<ContractViolation>();
        var addedCount = 0;

        var jobs = from rule in rules
                   from contract in contracts
                   select (rule, contract);

        await Parallel.ForEachAsync(
            jobs,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = _maxDegreeOfParallelism,
                CancellationToken = cancellationToken
            },
            async (job, ct) =>
            {
                var violations = await job.rule.ValidateAsync(job.contract, contracts, ct);
                foreach (var violation in violations)
                {
                    if (Interlocked.Increment(ref addedCount) > _maxViolationQueueSize)
                        return; // Backpressure: stop adding beyond the bound (atomic).
                    results.Add(violation);
                }
            });

        return results.Take(_maxViolationQueueSize)
            .OrderBy(v => v.RuleId, StringComparer.Ordinal)
            .ThenBy(v => v.Message, StringComparer.Ordinal)
            .ToList();
    }
}
