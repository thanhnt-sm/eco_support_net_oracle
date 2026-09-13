using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules;

namespace DataGuard.Core.Validation;

/// <summary>
/// Runs contract rules concurrently with bounded parallelism (backpressure).
/// MaxDegreeOfParallelism bounds memory: at most N rule executions run at once,
/// each contributing at most its own violations before being collected.
/// </summary>
public sealed class ConcurrentValidationEngine
{
    public const int DefaultMaxViolationQueueSize = 100_000;
    private readonly int _maxDegreeOfParallelism;
    private readonly int _maxViolationQueueSize;

    public ConcurrentValidationEngine(int maxDegreeOfParallelism = 0, int maxViolationQueueSize = DefaultMaxViolationQueueSize)
    {
        _maxDegreeOfParallelism = maxDegreeOfParallelism > 0
            ? maxDegreeOfParallelism
            : Math.Max(1, Environment.ProcessorCount);
        _maxViolationQueueSize = NormalizeMaxViolationQueueSize(maxViolationQueueSize);
    }

    internal static int NormalizeMaxViolationQueueSize(int value) =>
        value >= 0 ? value : DefaultMaxViolationQueueSize;

    public async Task<IReadOnlyList<ContractViolation>> ValidateAsync(
        IReadOnlyList<ContractDescriptor> contracts,
        IReadOnlyList<IContractRule> rules,
        CancellationToken cancellationToken = default)
    {
        var result = await ValidateDetailedAsync(contracts, rules, cancellationToken);
        if (result.IsIncomplete)
        {
            throw new ValidationIncompleteException("Validation result exceeded the configured violation cap.", result);
        }

        return result.Violations;
    }

    public async Task<ValidationExecutionResult> ValidateDetailedAsync(
        IReadOnlyList<ContractDescriptor> contracts,
        IReadOnlyList<IContractRule> rules,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ContractViolation>();
        var ruleViolations = rules.ToDictionary(rule => rule.RuleId, _ => new List<ContractViolation>(), StringComparer.Ordinal);
        var ruleFailures = new Dictionary<string, string>(StringComparer.Ordinal);
        var droppedCount = 0;
        var batch = new List<(IContractRule Rule, ContractDescriptor Contract)>();

        async Task DrainBatchAsync()
        {
            var completed = new (IReadOnlyList<ContractViolation> Violations, string? Failure)[batch.Count];
            await Parallel.ForEachAsync(
                Enumerable.Range(0, batch.Count),
                new ParallelOptions { MaxDegreeOfParallelism = _maxDegreeOfParallelism, CancellationToken = cancellationToken },
                async (index, ct) =>
                {
                    try
                    {
                        completed[index] = (await batch[index].Rule.ValidateAsync(batch[index].Contract, contracts, ct), null);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        completed[index] = (Array.Empty<ContractViolation>(), exception.GetType().Name);
                    }
                });
            for (var index = 0; index < completed.Length; index++)
            {
                var (violations, failure) = completed[index];
                if (failure is not null)
                {
                    ruleFailures[batch[index].Rule.RuleId] = failure;
                }
                ruleViolations[batch[index].Rule.RuleId].AddRange(violations);
                var available = Math.Max(0, _maxViolationQueueSize - results.Count);
                results.AddRange(violations.Take(available));
                droppedCount += violations.Count - Math.Min(available, violations.Count);
            }

            batch.Clear();
        }

        foreach (var rule in rules)
        {
            foreach (var contract in contracts)
            {
                batch.Add((rule, contract));
                if (batch.Count == _maxDegreeOfParallelism)
                {
                    await DrainBatchAsync();
                }
            }
        }

        if (batch.Count > 0)
        {
            await DrainBatchAsync();
        }

        var ordered = results
            .OrderBy(violation => violation.RuleId, StringComparer.Ordinal)
            .ThenBy(violation => violation.Message, StringComparer.Ordinal)
            .ToList();
        return new ValidationExecutionResult(ordered, droppedCount > 0 || ruleFailures.Count > 0, droppedCount)
        {
            RuleOutcomes = rules.Select(rule => new RuleExecutionOutcome(
                rule.RuleId,
                ruleFailures.ContainsKey(rule.RuleId) ? RuleExecutionState.Failed : RuleExecutionState.Evaluated,
                ruleViolations[rule.RuleId],
                FailureReason: ruleFailures.GetValueOrDefault(rule.RuleId))).ToList(),
        };
    }
}

public sealed record ValidationExecutionResult(
    IReadOnlyList<ContractViolation> Violations,
    bool IsIncomplete,
    int? DroppedViolationCount)
{
    /// <summary>Per-rule coverage and execution outcomes when the caller requests detailed reporting.</summary>
    public IReadOnlyList<RuleExecutionOutcome> RuleOutcomes { get; init; } = Array.Empty<RuleExecutionOutcome>();
}

public sealed class ValidationIncompleteException : InvalidOperationException
{
    public ValidationIncompleteException(string message, ValidationExecutionResult result)
        : base(message)
    {
        Result = result;
    }

    public ValidationExecutionResult Result { get; }
}

public static class GraphValidationExecutor
{
    public static async Task<ValidationExecutionResult> ValidateAsync(
        RuleDependencyGraph graph,
        IReadOnlyList<ContractDescriptor> contracts,
        int maxDegreeOfParallelism,
        int maxViolationQueueSize,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<ContractViolation>();
        var remaining = ConcurrentValidationEngine.NormalizeMaxViolationQueueSize(maxViolationQueueSize);
        var dropped = 0;
        var known = true;
        var outcomes = new List<RuleExecutionOutcome>();

        foreach (var level in graph.GetParallelGroups())
        {
            var result = await new ConcurrentValidationEngine(maxDegreeOfParallelism, remaining)
                .ValidateDetailedAsync(contracts, level, cancellationToken);
            violations.AddRange(result.Violations);
            outcomes.AddRange(result.RuleOutcomes);
            if (result.RuleOutcomes.Any(outcome => outcome.MakesValidationIncomplete))
            {
                known = false;
            }
            remaining -= result.Violations.Count;
            if (result.DroppedViolationCount.HasValue)
            {
                dropped += result.DroppedViolationCount.Value;
            }
            else
            {
                known = false;
            }
        }

        return new ValidationExecutionResult(
            violations.OrderBy(violation => violation.RuleId, StringComparer.Ordinal)
                .ThenBy(violation => violation.Message, StringComparer.Ordinal).ToList(),
            dropped > 0 || !known,
            known ? dropped : null)
        {
            RuleOutcomes = outcomes,
        };
    }
}
