using DataGuard;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Baseline;
using DataGuard.Core.Models;
using DataGuard.Core.Rules;
using DataGuard.Core.Validation;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DataGuard.Core.Tests;

public class ConcurrentValidationExecutionTests
{
    [Fact]
    public async Task GraphExecutor_RespectsDependencyLevelsAndGlobalCap()
    {
        var contracts = Contracts(2);
        var parentsFinished = 0;
        var parent = new TestRule("DG100", async (_, ct) =>
        {
            await Task.Delay(10, ct);
            Interlocked.Increment(ref parentsFinished);
            return Violations("DG100", "parent");
        });
        var child = new TestRule("DG200", (_, _) =>
        {
            parentsFinished.Should().Be(contracts.Count, "a dependent level begins only after its dependency level completes");
            return Task.FromResult(Violations("DG200", "child"));
        });
        var graph = new RuleDependencyGraph().AddRule(parent).AddRule(child, parent.RuleId);

        var result = await GraphValidationExecutor.ValidateAsync(graph, contracts, maxDegreeOfParallelism: 2, maxViolationQueueSize: 2);

        result.Violations.Should().HaveCount(2);
        result.IsIncomplete.Should().BeTrue();
        result.DroppedViolationCount.Should().Be(2);
    }

    [Fact]
    public async Task GraphExecutor_ZeroCap_DistinguishesCleanFromDroppedWork()
    {
        var contract = Contracts(1);
        var clean = new RuleDependencyGraph().AddRule(new TestRule("DG100", (_, _) => Task.FromResult(EmptyViolations())));
        var finding = new RuleDependencyGraph().AddRule(new TestRule("DG101", (_, _) => Task.FromResult(Violations("DG101", "finding"))));

        var cleanResult = await GraphValidationExecutor.ValidateAsync(clean, contract, 1, 0);
        var findingResult = await GraphValidationExecutor.ValidateAsync(finding, contract, 1, 0);

        cleanResult.IsIncomplete.Should().BeFalse();
        cleanResult.DroppedViolationCount.Should().Be(0);
        findingResult.IsIncomplete.Should().BeTrue();
        findingResult.DroppedViolationCount.Should().Be(1);
    }

    [Fact]
    public async Task GraphExecutor_ExactCapWithCleanLaterLevel_IsComplete()
    {
        var first = new TestRule("DG900", (_, _) => Task.FromResult(Violations("DG900", "retained")));
        var laterClean = new TestRule("DG901", (_, _) => Task.FromResult(EmptyViolations()));
        var graph = new RuleDependencyGraph().AddRule(first).AddRule(laterClean, first.RuleId);

        var result = await GraphValidationExecutor.ValidateAsync(graph, Contracts(1), 1, 1);

        result.Violations.Should().ContainSingle();
        result.IsIncomplete.Should().BeFalse();
        result.DroppedViolationCount.Should().Be(0);
    }

    [Fact]
    public async Task Engine_HonorsMaximumConcurrencyAndCancellation()
    {
        var active = 0;
        var maximum = 0;
        var started = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bounded = new TestRule("DG100", async (_, ct) =>
        {
            var now = Interlocked.Increment(ref active);
            int observed;
            do
            {
                observed = Volatile.Read(ref maximum);
                if (now <= observed)
                {
                    break;
                }
            }
            while (Interlocked.CompareExchange(ref maximum, now, observed) != observed);
            if (Interlocked.Increment(ref started) == 2)
            {
                release.TrySetResult();
            }
            try
            {
                await release.Task.WaitAsync(ct);
                return EmptyViolations();
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        });
        var engine = new ConcurrentValidationEngine(maxDegreeOfParallelism: 2);

        using var overlapTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await engine.ValidateDetailedAsync(Contracts(2), new IContractRule[] { bounded }, overlapTimeout.Token);
        started.Should().Be(2, "two jobs must have reached the synchronization barrier");
        maximum.Should().Be(2, "the configured degree allows two jobs to overlap");

        using var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(10));
        var cancellable = new TestRule("DG101", async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
            return EmptyViolations();
        });
        var act = async () => await engine.ValidateDetailedAsync(Contracts(1), new IContractRule[] { cancellable }, cancellation.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task NegativeCap_UsesTheSameDefaultAtEveryConcurrentEntryPoint()
    {
        var contracts = Contracts(2);
        var rule = new TestRule("DG900", (_, _) => Task.FromResult(Violations("DG900", "finding")));
        var engine = new ConcurrentValidationEngine(1, maxViolationQueueSize: -1);
        var graph = new RuleDependencyGraph().AddRule(rule);

        var direct = await engine.ValidateDetailedAsync(contracts, new IContractRule[] { rule });
        var grouped = await GraphValidationExecutor.ValidateAsync(graph, contracts, 1, -1);

        direct.IsIncomplete.Should().BeFalse();
        grouped.IsIncomplete.Should().BeFalse();
        direct.Violations.Should().HaveCount(2);
        grouped.Violations.Should().HaveCount(2);
    }

    [Fact]
    public async Task Pipeline_ReportsIncompleteAfterBaselineFiltersCollectedViolations()
    {
        var baselinePath = Path.Combine(Path.GetTempPath(), $"dg-baseline-{Guid.NewGuid():N}.json");
        var violation = Violations("DG900", "finding").Single();
        try
        {
            await new BaselineManager(baselinePath).CreateBaselineAsync(new[] { violation }, "1.0", "Snapshot");
            using var pipeline = DataGuardApi.CreatePipeline(new DataGuardConfiguration(
                EnableConcurrentValidation: true,
                EnableSmartDefaults: false,
                EnableBaseline: true,
                BaselineFilePath: baselinePath,
                MaxDegreeOfParallelism: 1,
                MaxViolationQueueSize: 1))
                .WithRules(
                    new TestRule("DG900", (_, _) => Task.FromResult(Violations("DG900", "finding"))),
                    new TestRule("DG901", (_, _) => Task.FromResult(Violations("DG901", "overflow"))));

            var result = await pipeline.ValidateAsync(CleanEntityContracts());

            result.TotalViolations.Should().Be(0, "the retained violation is covered by the baseline");
            result.ExecutionStatus.Should().Be(ValidationExecutionStatus.Incomplete);
            result.DroppedViolationCount.Should().Be(1);
            result.IsClean.Should().BeFalse();
        }
        finally
        {
            if (File.Exists(baselinePath))
            {
                File.Delete(baselinePath);
            }
        }
    }

    [Fact]
    public async Task DetailedExecution_RecordsCleanRuleAsEvaluated()
    {
        var rule = new TestRule("DG777", (_, _) => Task.FromResult(EmptyViolations()));
        var result = await new ConcurrentValidationEngine(maxDegreeOfParallelism: 1)
            .ValidateDetailedAsync(Contracts(1), new IContractRule[] { rule });

        var outcome = result.RuleOutcomes.Should().ContainSingle().Subject;
        outcome.RuleId.Should().Be("DG777");
        outcome.State.Should().Be(RuleExecutionState.Evaluated);
        outcome.Violations.Should().BeEmpty();
        outcome.MakesValidationIncomplete.Should().BeFalse();
    }

    [Fact]
    public async Task ConcurrentExecution_ContainsRuleFailureAndMarksResultIncomplete()
    {
        var throwingRule = new TestRule("DG778", (_, _) => throw new InvalidOperationException("plugin failure"));
        var healthyRule = new TestRule("DG779", (_, _) => Task.FromResult(Violations("DG779", "still runs")));

        var result = await new ConcurrentValidationEngine(maxDegreeOfParallelism: 2)
            .ValidateDetailedAsync(Contracts(1), new IContractRule[] { throwingRule, healthyRule });

        result.IsIncomplete.Should().BeTrue();
        result.Violations.Should().ContainSingle(violation => violation.RuleId == "DG779");
        var failed = result.RuleOutcomes.Single(outcome => outcome.RuleId == "DG778");
        failed.State.Should().Be(RuleExecutionState.Failed);
        failed.FailureReason.Should().Be(nameof(InvalidOperationException));
    }

    [Fact]
    public async Task SequentialPipeline_ReportsExactDroppedCountWhileEvaluatingAllRulesUnderCap()
    {
        using var pipeline = DataGuardApi.CreatePipeline(new DataGuardConfiguration(
            EnableConcurrentValidation: false,
            EnableSmartDefaults: false,
            MaxViolationQueueSize: 1))
            .WithRules(
                new TestRule("DG810", (_, _) => Task.FromResult(Violations("DG810", "retained"))),
                new TestRule("DG811", (_, _) => Task.FromResult(Violations("DG811", "dropped"))),
                new TestRule("DG812", (_, _) => Task.FromResult(Violations("DG812", "not evaluated"))));

        var result = await pipeline.ValidateAsync(CleanEntityContracts());

        result.ExecutionStatus.Should().Be(ValidationExecutionStatus.Incomplete);
        var evaluated = result.RuleOutcomes.Single(outcome => outcome.RuleId == "DG810");
        evaluated.State.Should().Be(RuleExecutionState.Evaluated);
        evaluated.Violations.Should().ContainSingle();

        result.DroppedViolationCount.Should().Be(2);
        result.RuleOutcomes.Should().OnlyContain(outcome => outcome.State == RuleExecutionState.Evaluated);
        result.RuleOutcomes.Single(outcome => outcome.RuleId == "DG812").Violations.Should().ContainSingle();
    }

    [Fact]
    public async Task SequentialPipeline_ContainsRuleFailureAndContinuesWithOtherRules()
    {
        using var pipeline = DataGuardApi.CreatePipeline(new DataGuardConfiguration(
            EnableConcurrentValidation: false,
            EnableSmartDefaults: false))
            .WithRules(
                new TestRule("DG813", (_, _) => throw new InvalidOperationException("plugin failure")),
                new TestRule("DG814", (_, _) => Task.FromResult(Violations("DG814", "still runs"))));

        var result = await pipeline.ValidateAsync(CleanEntityContracts());

        result.ExecutionStatus.Should().Be(ValidationExecutionStatus.Incomplete);
        result.RuleOutcomes.Single(outcome => outcome.RuleId == "DG813").State.Should().Be(RuleExecutionState.Failed);
        result.Violations.Should().ContainSingle(violation => violation.RuleId == "DG814");
    }

    private static IReadOnlyList<ContractDescriptor> Contracts(int count) =>
        Enumerable.Range(0, count)
            .Select(index => (ContractDescriptor)new RawSqlDescriptor($"raw:{index}", "EXEC p", Array.Empty<ParameterDescriptor>(), Array.Empty<ColumnDescriptor>()))
            .ToList();

    private static IReadOnlyList<ContractDescriptor> CleanEntityContracts() =>
    [
        new EntityDescriptor(
            "entity:1", "Customer", "Customer", "CUSTOMERS",
            [new PropertyDescriptor("Id", "int", "ID", "NUMBER", false, null, true, false)]),
    ];

    private static IReadOnlyList<ContractViolation> EmptyViolations() => Array.Empty<ContractViolation>();

    private static IReadOnlyList<ContractViolation> Violations(string ruleId, string message) =>
        new[] { new ContractViolation(ruleId, message, DiagnosticSeverity.Error) };

    private sealed class TestRule(string ruleId, Func<ContractDescriptor, CancellationToken, Task<IReadOnlyList<ContractViolation>>> validate) : IContractRule
    {
        public string RuleId { get; } = ruleId;
        public string Name => RuleId;
        public string Description => RuleId;
        public DiagnosticSeverity Severity => DiagnosticSeverity.Error;

        public Task<IReadOnlyList<ContractViolation>> ValidateAsync(ContractDescriptor contract, IReadOnlyList<ContractDescriptor> allContracts, CancellationToken cancellationToken = default) =>
            validate(contract, cancellationToken);
    }
}
