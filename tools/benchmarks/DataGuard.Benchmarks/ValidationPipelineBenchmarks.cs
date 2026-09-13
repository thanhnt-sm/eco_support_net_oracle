using BenchmarkDotNet.Attributes;
using DataGuard;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Models;
using Microsoft.CodeAnalysis;

/// <summary>Measures equivalent sequential and dependency-level concurrent public pipeline runs.</summary>
[MemoryDiagnoser]
public class ValidationPipelineBenchmarks
{
    internal const string CorpusContract = "raw-sql:EXEC benchmark_proc @id;:four deterministic rules:100,1000:up-to-four-way-concurrency";

    internal static int MaxDegreeOfParallelism => Math.Min(4, Math.Max(1, Environment.ProcessorCount));

    private ValidationPipeline? sequential;
    private ValidationPipeline? concurrent;
    private IReadOnlyList<ContractDescriptor> contracts = Array.Empty<ContractDescriptor>();

    /// <summary>Gets the fixed contract corpus size used by both public-pipeline modes.</summary>
    [Params(100, 1_000)]
    public int ContractCount { get; set; }

    /// <summary>Builds one deterministic corpus and proves output equivalence before timing either mode.</summary>
    [GlobalSetup]
    public void SetUp()
    {
        contracts = Enumerable.Range(0, ContractCount)
            .Select(index => (ContractDescriptor)new RawSqlDescriptor(
                $"benchmark:{index}",
                "EXEC benchmark_proc @id;",
                Array.Empty<ParameterDescriptor>(),
                Array.Empty<ColumnDescriptor>()))
            .ToArray();

        sequential = CreatePipeline(enableConcurrentValidation: false);
        concurrent = CreatePipeline(enableConcurrentValidation: true);
        var expected = Normalize(sequential.ValidateAsync(contracts).GetAwaiter().GetResult());
        var actual = Normalize(concurrent.ValidateAsync(contracts).GetAwaiter().GetResult());
        if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Sequential and concurrent benchmark outputs differ.");
        }
    }

    /// <summary>Measures the public pipeline with concurrent validation disabled.</summary>
    [Benchmark(Baseline = true)]
    public Task<ValidationResult> Sequential() =>
        (sequential ?? throw new InvalidOperationException("Benchmark setup did not run.")).ValidateAsync(contracts);

    /// <summary>Measures the same corpus and rules with bounded dependency-level concurrency.</summary>
    [Benchmark]
    public Task<ValidationResult> Concurrent() =>
        (concurrent ?? throw new InvalidOperationException("Benchmark setup did not run.")).ValidateAsync(contracts);

    /// <summary>Disposes the pipelines after BenchmarkDotNet has completed the scenario.</summary>
    [GlobalCleanup]
    public void CleanUp()
    {
        sequential?.Dispose();
        concurrent?.Dispose();
    }

    private static ValidationPipeline CreatePipeline(bool enableConcurrentValidation)
    {
        var pipeline = DataGuardApi.CreatePipeline(new DataGuardConfiguration(
            EnableSmartDefaults: false,
            EnableBaseline: false,
            EnableAuditLogging: false,
            EnableConcurrentValidation: enableConcurrentValidation,
            MaxDegreeOfParallelism: MaxDegreeOfParallelism,
            MaxViolationQueueSize: int.MaxValue));
        return pipeline.WithRules(
            new DeterministicRule("B001", 17),
            new DeterministicRule("B002", 31),
            new DeterministicRule("B003", 47),
            new DeterministicRule("B004", 61));
    }

    private static IReadOnlyList<string> Normalize(ValidationResult result) => result.Violations
        .Select(violation => $"{violation.RuleId}|{violation.Message}|{violation.Severity}")
        .OrderBy(value => value, StringComparer.Ordinal)
        .Append($"status:{result.ExecutionStatus}|dropped:{result.DroppedViolationCount}")
        .ToArray();

    private sealed class DeterministicRule(string ruleId, int multiplier) : IContractRule
    {
        public string RuleId { get; } = ruleId;
        public string Name => RuleId;
        public string Description => "CPU-bound deterministic benchmark rule";
        public DiagnosticSeverity Severity => DiagnosticSeverity.Warning;

        public Task<IReadOnlyList<ContractViolation>> ValidateAsync(
            ContractDescriptor contract,
            IReadOnlyList<ContractDescriptor> allContracts,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hash = multiplier;
            foreach (var character in contract.Id)
            {
                hash = unchecked((hash * 16777619) ^ character);
            }

            return Task.FromResult<IReadOnlyList<ContractViolation>>(
                (hash & 7) == 0
                    ? new[] { new ContractViolation(RuleId, $"deterministic:{contract.Id}", Severity) }
                    : Array.Empty<ContractViolation>());
        }
    }
}
