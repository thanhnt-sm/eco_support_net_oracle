using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Models;
using DataGuard.Core.Rules;
using DataGuard.Core.Validation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DataGuard.Core.Tests;

public sealed class FullScanLifetimeStabilityTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public async Task CancelledBuiltInRule_DoesNotReportPriorRunOnSuccessfulRerun(bool concurrent, int repetition)
    {
        using var cancellation = new CancellationTokenSource();
        var provider = new CancelAfterFirstFailure(cancellation);
        var rule = new LiveSqlShapeValidationRule(schemaProvider: provider);
        var graph = new RuleDependencyGraph();
        graph.RegisterRule(rule);
        var cancelled = Contracts($"cancelled-{repetition}", 2);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run(graph, cancelled, concurrent, cancellation.Token));

        provider.Recover = true;
        var rerunContracts = Contracts($"rerun-{repetition}", 2);
        var freshGraph = new RuleDependencyGraph();
        freshGraph.RegisterRule(new LiveSqlShapeValidationRule(schemaProvider: new CancelAfterFirstFailure(cancellation) { Recover = true }));
        var fresh = await Run(freshGraph, rerunContracts, concurrent);
        var rerun = await Run(graph, rerunContracts, concurrent);
        Assert.Equal(rerunContracts.Select(contract => contract.Id), rerun.UnevaluatedContracts.Select(entry => entry.ContractId));
        foreach (var entry in rerun.UnevaluatedContracts)
        {
            Assert.Same(rerunContracts.Single(contract => contract.Id == entry.ContractId).Location, entry.Location);
        }

        Assert.Equal(StatusDigest(fresh), StatusDigest(rerun));
        Assert.Empty(rule.DrainUnevaluatedContracts());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReusedPublicPipeline_CancellationDoesNotContaminateNextResult(bool concurrent)
    {
        using var cancellation = new CancellationTokenSource();
        var provider = new CancelAfterFirstFailure(cancellation);
        var rule = new LiveSqlShapeValidationRule(schemaProvider: provider);
        using var pipeline = DataGuardApi.CreatePipeline(new DataGuardConfiguration
        {
            EnableConcurrentValidation = concurrent,
            MaxDegreeOfParallelism = 1,
            EnableSmartDefaults = false,
            EnableTelemetry = false,
            EnableAuditLogging = false,
            EnableBaseline = false,
        }).WithProviderRules(new[] { rule });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pipeline.ValidateAsync(Contracts("cancelled", 2), cancellation.Token));

        provider.Recover = true;
        var contracts = Contracts("pipeline-rerun", 2);
        var result = await pipeline.ValidateAsync(contracts);
        Assert.Equal(2, result.ContractsValidated);
        Assert.Equal(contracts.Select(contract => contract.Id), result.UnevaluatedContracts.Select(entry => entry.ContractId));
        Assert.All(result.UnevaluatedContracts, entry => Assert.Same(contracts.Single(contract => contract.Id == entry.ContractId).Location, entry.Location));
        Assert.Empty(rule.DrainUnevaluatedContracts());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task OverlappingRuns_SharedRule_KeepsSurvivingRunEntries(bool concurrent, bool finishSurvivorFirst)
    {
        using var cancellation = new CancellationTokenSource();
        var rule = new GatedRule();
        var graph = new RuleDependencyGraph();
        graph.RegisterRule(rule);
        var cancelled = Run(graph, Contracts("cancelled", 1), concurrent, cancellation.Token);
        Task<ValidationExecutionResult>? survivor = null;
        try
        {
            await rule.CancelledStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            survivor = Run(graph, Contracts("survivor", 1), concurrent);
            await rule.SurvivorStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (finishSurvivorFirst)
            {
                rule.ReleaseSurvivor.SetResult();
                Assert.Equal("survivor:0", Assert.Single((await survivor).UnevaluatedContracts).ContractId);
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
            }
            else
            {
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
                rule.ReleaseSurvivor.SetResult();
                Assert.Equal("survivor:0", Assert.Single((await survivor).UnevaluatedContracts).ContractId);
            }
        }
        finally
        {
            cancellation.Cancel();
            rule.ReleaseSurvivor.TrySetResult();
            try
            {
                await cancelled;
            }
            catch (OperationCanceledException)
            {
            }

            if (survivor is not null)
            {
                await survivor;
            }
        }

        Assert.Empty(rule.DrainUnevaluatedContracts());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NestedValidation_RestoresOuterStatusOwner(bool concurrent)
    {
        var rule = new NestedRule(concurrent);
        var graph = new RuleDependencyGraph();
        graph.RegisterRule(rule);
        rule.Graph = graph;
        var outer = await Run(graph, Contracts("outer", 1), concurrent);
        Assert.Equal(new[] { "outer-after", "outer-before" }, outer.UnevaluatedContracts.Select(entry => entry.Reason));
        Assert.Equal("inner:0", Assert.Single(rule.Inner!.UnevaluatedContracts).ContractId);
        Assert.Empty(rule.DrainUnevaluatedContracts());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NestedCancelledValidation_RestoresOuterStatusOwner(bool concurrent)
    {
        var rule = new NestedCancellingRule(concurrent);
        var graph = new RuleDependencyGraph();
        graph.RegisterRule(rule);
        rule.Graph = graph;
        var outer = await Run(graph, Contracts("outer", 1), concurrent);
        Assert.True(rule.InnerCancelled);
        Assert.Equal(new[] { "outer-after", "outer-before" }, outer.UnevaluatedContracts.Select(entry => entry.Reason));
        Assert.All(outer.UnevaluatedContracts, entry => Assert.Equal("outer:0", entry.ContractId));
        Assert.Empty(rule.DrainUnevaluatedContracts());
    }

    [Fact]
    public async Task ParallelRules_AcrossGraphLevels_KeepEveryCurrentStatus()
    {
        var first = new YieldingRule("FIRST");
        var second = new YieldingRule("SECOND");
        var final = new YieldingRule("FINAL");
        var graph = new RuleDependencyGraph();
        graph.RegisterRule(first);
        graph.RegisterRule(second);
        graph.RegisterRule(final, new[] { first.RuleId, second.RuleId });
        var contracts = Contracts("parallel", 12);
        var result = await GraphValidationExecutor.ValidateAsync(graph, contracts, concurrent: true, maxDegreeOfParallelism: 4, maxViolationQueueSize: 100_000);
        Assert.Equal(72, result.UnevaluatedContracts.Count);
        foreach (var rule in new[] { first, second, final })
        {
            var entries = result.UnevaluatedContracts.Where(entry => entry.RuleId == rule.RuleId).ToArray();
            Assert.Equal(24, entries.Length);
            foreach (var contract in contracts)
            {
                var pair = entries.Where(entry => entry.ContractId == contract.Id).ToArray();
                Assert.Equal(new[] { "after-yield", "before-yield" }, pair.Select(entry => entry.Reason));
                Assert.All(pair, entry => Assert.Same(contract.Location, entry.Location));
            }

            Assert.Empty(rule.DrainUnevaluatedContracts());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectCallStatus_RemainsAvailableToDirectCaller(bool concurrent)
    {
        var rule = new ReportingRule();
        var direct = Contracts("direct", 1);
        await rule.ValidateAsync(direct[0], direct);
        var graph = new RuleDependencyGraph();
        graph.RegisterRule(rule);
        var result = await Run(graph, Contracts("engine", 1), concurrent);
        Assert.Equal("engine:0", Assert.Single(result.UnevaluatedContracts).ContractId);
        Assert.Equal("direct:0", Assert.Single(rule.DrainUnevaluatedContracts()).ContractId);
        Assert.Empty(rule.DrainUnevaluatedContracts());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RuleCallingStandaloneRule_CanDrainItsPublicStatuses(bool concurrent)
    {
        var rule = new StandaloneCallingRule();
        var graph = new RuleDependencyGraph();
        graph.RegisterRule(rule);
        var result = await Run(graph, Contracts("engine", 1), concurrent);
        Assert.Equal("STANDALONE", Assert.Single(rule.Drained!).RuleId);
        Assert.Equal("engine:0", Assert.Single(rule.Drained!).ContractId);
        Assert.Equal(rule.RuleId, Assert.Single(result.UnevaluatedContracts).RuleId);
        Assert.Empty(rule.Standalone.DrainUnevaluatedContracts());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActiveRegisteredRule_CanDrainItsOwnPublicStatuses(bool concurrent)
    {
        var rule = new DrainingRule();
        var graph = new RuleDependencyGraph();
        graph.RegisterRule(rule);
        graph.RegisterRule(new CompanionRule());
        var result = await Run(graph, Contracts("engine", 1), concurrent);
        Assert.Equal("A_COMPANION", Assert.Single(result.UnevaluatedContracts).RuleId);
        Assert.Equal("engine:0", Assert.Single(rule.Drained!).ContractId);
        Assert.Equal(rule.RuleId, Assert.Single(rule.Drained!).RuleId);
        Assert.Empty(rule.DrainUnevaluatedContracts());
    }

    [Fact]
    public async Task LateChildStatus_AfterRunEnds_DoesNotFallBackToRuleQueue()
    {
        var rule = new LateChildRule();
        var graph = new RuleDependencyGraph();
        graph.RegisterRule(rule);
        try
        {
            Assert.Empty((await Run(graph, Contracts("late", 1), false)).UnevaluatedContracts);
            rule.Release.TrySetResult();
            await rule.Child!.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Empty(rule.DrainUnevaluatedContracts());
        }
        finally
        {
            rule.Release.TrySetResult();
            if (rule.Child is not null)
            {
                await rule.Child.WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DerivedRule_ReimplementedReporter_RemainsInvokedThroughInterface(bool concurrent)
    {
        var rule = new ReimplementedReporter();
        var graph = new RuleDependencyGraph();
        graph.RegisterRule(rule);
        var contracts = Contracts("reimplemented", 2);
        var result = await Run(graph, contracts, concurrent);
        Assert.Equal(contracts.Select(contract => contract.Id), result.UnevaluatedContracts.Select(entry => entry.ContractId));
        Assert.All(result.UnevaluatedContracts, entry => Assert.Same(contracts.Single(contract => contract.Id == entry.ContractId).Location, entry.Location));
        Assert.Empty(((IContractEvaluationStatusReporter)rule).DrainUnevaluatedContracts());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CustomReporter_StillDrainsThroughItsPublicContract(bool concurrent)
    {
        var rule = new LegacyReporter();
        var graph = new RuleDependencyGraph();
        graph.RegisterRule(rule);
        foreach (var prefix in new[] { "first", "second" })
        {
            var contracts = Contracts(prefix, 2);
            var result = await Run(graph, contracts, concurrent);
            Assert.Equal(contracts.Select(contract => contract.Id), result.UnevaluatedContracts.Select(entry => entry.ContractId));
            Assert.All(result.UnevaluatedContracts, entry => Assert.Same(contracts.Single(contract => contract.Id == entry.ContractId).Location, entry.Location));
            Assert.Empty(rule.DrainUnevaluatedContracts());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecoverableRuleError_ReportsCurrentStatus_ThenRerunsWithoutPriorEntries(bool concurrent)
    {
        var rule = new RecoverableRule();
        var graph = new RuleDependencyGraph();
        graph.RegisterRule(rule);
        var failedContracts = Contracts("failed", 2);
        var failed = await Run(graph, failedContracts, concurrent);
        Assert.True(failed.IsIncomplete);
        Assert.Equal("InvalidOperationException", Assert.Single(failed.RuleOutcomes).FailureReason);
        Assert.NotEmpty(failed.UnevaluatedContracts);
        Assert.All(failed.UnevaluatedContracts, entry => Assert.Same(failedContracts.Single(contract => contract.Id == entry.ContractId).Location, entry.Location));

        rule.Fail = false;
        var recovered = await Run(graph, Contracts("recovered", 2), concurrent);
        Assert.False(recovered.IsIncomplete);
        Assert.Empty(recovered.UnevaluatedContracts);
        Assert.Empty(rule.DrainUnevaluatedContracts());
    }

    private static string StatusDigest(ValidationExecutionResult result) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            result.IsIncomplete,
            result.DroppedViolationCount,
            entries = result.UnevaluatedContracts.Select(entry => new
            {
                entry.RuleId,
                entry.ContractId,
                entry.Reason,
                path = entry.Location?.GetLineSpan().Path,
                start = entry.Location?.SourceSpan.Start,
                length = entry.Location?.SourceSpan.Length,
            }),
            outcomes = result.RuleOutcomes.Select(outcome => new { outcome.RuleId, outcome.State, outcome.FailureReason }),
            violations = result.Violations.Select(violation => new { violation.RuleId, violation.Message }),
        }))));

    private static Task<ValidationExecutionResult> Run(RuleDependencyGraph graph, IReadOnlyList<ContractDescriptor> contracts, bool concurrent, CancellationToken token = default) =>
        GraphValidationExecutor.ValidateAsync(graph, contracts, concurrent, 1, 100_000, token);

    private static IReadOnlyList<ContractDescriptor> Contracts(string prefix, int count) =>
        Enumerable.Range(0, count).Select(
            index => (ContractDescriptor)new RawSqlDescriptor(
            $"{prefix}:{index}", "SELECT Id FROM Orders", Array.Empty<ParameterDescriptor>(), Array.Empty<ColumnDescriptor>(),
            CSharpSyntaxTree.ParseText("class Fixture {}", path: $"{prefix}-{index}.cs").GetRoot().GetLocation(),
            ExpectedProperties: new[] { new PropertyDescriptor("Id", "int") })).ToArray();

    private sealed class ReimplementedReporter : ReportingRule, IContractEvaluationStatusReporter
    {
        private readonly UnevaluatedContractCollector entries = new();
        IReadOnlyList<UnevaluatedContract> IContractEvaluationStatusReporter.DrainUnevaluatedContracts() => entries.Drain();
        protected override Task ValidateCoreAsync(ContractDescriptor contract, IReadOnlyList<ContractDescriptor> allContracts, List<ContractViolation> violations, CancellationToken cancellationToken)
        {
            entries.Add(new UnevaluatedContract(RuleId, contract.Id, "reimplemented reporter", contract.Location));
            return Task.CompletedTask;
        }
    }

    private sealed class LegacyReporter : IContractRule, IContractEvaluationStatusReporter
    {
        private readonly UnevaluatedContractCollector entries = new();
        public string RuleId => "LEGACY";
        public string Name => RuleId;
        public string Description => RuleId;
        public DiagnosticSeverity Severity => DiagnosticSeverity.Warning;
        public IReadOnlyList<UnevaluatedContract> DrainUnevaluatedContracts() => entries.Drain();
        public Task<IReadOnlyList<ContractViolation>> ValidateAsync(ContractDescriptor contract, IReadOnlyList<ContractDescriptor> allContracts, CancellationToken cancellationToken = default)
        {
            entries.Add(new UnevaluatedContract(RuleId, contract.Id, "legacy custom reporter", contract.Location));
            return Task.FromResult<IReadOnlyList<ContractViolation>>(Array.Empty<ContractViolation>());
        }
    }

    private sealed class CancelAfterFirstFailure(CancellationTokenSource cancellation) : ILiveQuerySchemaProvider
    {
        private int calls;
        public bool Recover { get; set; }
        public Task<LiveSchemaResult> DescribeResultSetAsync(string sqlText, CancellationToken cancellationToken)
        {
            if (Recover)
            {
                return Task.FromResult(LiveSchemaResult.Fail("synthetic recovered describe unavailable"));
            }
            if (++calls == 2)
            {
                cancellation.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }
            return Task.FromResult(LiveSchemaResult.Fail("synthetic describe failure"));
        }
    }

    private class ReportingRule : ContractRuleBase
    {
        public override string RuleId => "LIFETIME";
        public override string Name => RuleId;
        public override string Description => RuleId;
        public override DiagnosticSeverity Severity => DiagnosticSeverity.Warning;
        protected override Task ValidateCoreAsync(ContractDescriptor contract, IReadOnlyList<ContractDescriptor> allContracts, List<ContractViolation> violations, CancellationToken cancellationToken)
        {
            MarkUnevaluated(contract, contract.Id);
            return Task.CompletedTask;
        }
    }

    private sealed class CompanionRule : ReportingRule
    {
        public override string RuleId => "A_COMPANION";
    }

    private sealed class StandaloneRule : ReportingRule
    {
        public override string RuleId => "STANDALONE";
    }

    private sealed class StandaloneCallingRule : ReportingRule
    {
        public StandaloneRule Standalone { get; } = new();
        public IReadOnlyList<UnevaluatedContract>? Drained { get; private set; }
        protected override async Task ValidateCoreAsync(ContractDescriptor contract, IReadOnlyList<ContractDescriptor> allContracts, List<ContractViolation> violations, CancellationToken cancellationToken)
        {
            MarkUnevaluated(contract, "registered");
            await Standalone.ValidateAsync(contract, allContracts, cancellationToken);
            Drained = Standalone.DrainUnevaluatedContracts();
        }
    }

    private sealed class DrainingRule : ReportingRule
    {
        public IReadOnlyList<UnevaluatedContract>? Drained { get; private set; }
        protected override Task ValidateCoreAsync(ContractDescriptor contract, IReadOnlyList<ContractDescriptor> allContracts, List<ContractViolation> violations, CancellationToken cancellationToken)
        {
            MarkUnevaluated(contract, contract.Id);
            Drained = DrainUnevaluatedContracts();
            return Task.CompletedTask;
        }
    }

    private sealed class RecoverableRule : ReportingRule
    {
        public bool Fail { get; set; } = true;
        protected override Task ValidateCoreAsync(ContractDescriptor contract, IReadOnlyList<ContractDescriptor> allContracts, List<ContractViolation> violations, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                MarkUnevaluated(contract, "synthetic failure before rule exception");
                throw new InvalidOperationException("synthetic rule exception");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class GatedRule : ReportingRule
    {
        public TaskCompletionSource CancelledStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SurvivorStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSurvivor { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task ValidateCoreAsync(ContractDescriptor contract, IReadOnlyList<ContractDescriptor> allContracts, List<ContractViolation> violations, CancellationToken cancellationToken)
        {
            MarkUnevaluated(contract, contract.Id);
            if (contract.Id.StartsWith("cancelled", StringComparison.Ordinal))
            {
                CancelledStarted.SetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            else
            {
                SurvivorStarted.SetResult();
                await ReleaseSurvivor.Task.WaitAsync(cancellationToken);
            }
        }
    }

    private sealed class NestedRule(bool concurrent) : ReportingRule
    {
        public RuleDependencyGraph Graph { get; set; } = null!;
        public ValidationExecutionResult? Inner { get; private set; }
        protected override async Task ValidateCoreAsync(ContractDescriptor contract, IReadOnlyList<ContractDescriptor> allContracts, List<ContractViolation> violations, CancellationToken cancellationToken)
        {
            if (contract.Id.StartsWith("outer", StringComparison.Ordinal))
            {
                MarkUnevaluated(contract, "outer-before");
                Inner = await Run(Graph, Contracts("inner", 1), concurrent, cancellationToken);
                MarkUnevaluated(contract, "outer-after");
            }
            else
            {
                MarkUnevaluated(contract, contract.Id);
            }
        }
    }

    private sealed class YieldingRule(string id) : ReportingRule
    {
        public override string RuleId => id;
        protected override async Task ValidateCoreAsync(ContractDescriptor contract, IReadOnlyList<ContractDescriptor> allContracts, List<ContractViolation> violations, CancellationToken cancellationToken)
        {
            MarkUnevaluated(contract, "before-yield");
            await Task.Yield();
            MarkUnevaluated(contract, "after-yield");
        }
    }

    private sealed class NestedCancellingRule(bool concurrent) : ReportingRule
    {
        private CancellationTokenSource? innerCancellation;
        public RuleDependencyGraph Graph { get; set; } = null!;
        public bool InnerCancelled { get; private set; }
        protected override async Task ValidateCoreAsync(ContractDescriptor contract, IReadOnlyList<ContractDescriptor> allContracts, List<ContractViolation> violations, CancellationToken cancellationToken)
        {
            if (contract.Id.StartsWith("outer", StringComparison.Ordinal))
            {
                MarkUnevaluated(contract, "outer-before");
                using var child = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                innerCancellation = child;
                try
                {
                    await Run(Graph, Contracts("inner-cancelled", 1), concurrent, child.Token);
                }
                catch (OperationCanceledException) when (child.IsCancellationRequested)
                {
                    InnerCancelled = true;
                }

                MarkUnevaluated(contract, "outer-after");
            }
            else
            {
                MarkUnevaluated(contract, contract.Id);
                innerCancellation!.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }

    private sealed class LateChildRule : ReportingRule
    {
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task? Child { get; private set; }
        protected override Task ValidateCoreAsync(ContractDescriptor contract, IReadOnlyList<ContractDescriptor> allContracts, List<ContractViolation> violations, CancellationToken cancellationToken)
        {
            Child = Task.Run(
                async () =>
                {
                    await Release.Task;
                    MarkUnevaluated(contract, "late child");
                },
                cancellationToken);
            return Task.CompletedTask;
        }
    }
}
