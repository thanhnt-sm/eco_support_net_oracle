using System.Diagnostics.Tracing;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules;
using DataGuard.Core.Validation;
using Xunit;

namespace DataGuard.Core.Tests;

[Collection("Validation trace isolation")]
public sealed class ValidationTraceTests
{
    [Fact]
    public async Task ExecutorEmitsVersionedCorrelatedPair()
    {
        using var listener = new Capture();
        await GraphValidationExecutor.ValidateAsync(new RuleDependencyGraph(), Array.Empty<ContractDescriptor>(), false, 1, 10);
        var events = listener.Events.ToArray();
        Assert.Equal(new[] { 1, 2 }, events.Select(e => e.Id));
        Assert.All(events, e => Assert.Equal(1, e.Version));
        Assert.Equal(events[0].Payload[0], events[1].Payload[0]);
        Assert.Equal(new[] { "requestId", "contracts", "concurrent" }, events[0].Names);
        Assert.Equal(new[] { "requestId", "elapsedMs", "outcome", "violations", "unevaluated", "dropped", "reasonFlags" }, events[1].Names);
    }

    [Fact]
    public void NoListenerBeginHasNoPerCallAllocationAfterInitialization()
    {
        var source = DataGuard.Core.Telemetry.ValidationTrace.Log;
        Assert.False(source.IsEnabled());
        for (var i = 0; i < 100; i++)
        {
            Assert.Equal(0, source.TryBegin(Array.Empty<ContractDescriptor>(), false));
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++)
        {
            source.TryBegin(Array.Empty<ContractDescriptor>(), false);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void BudgetIsAtomicAndExpiresWithoutReset()
    {
        var budget = new DataGuard.Core.Telemetry.ValidationTraceBudget(20, 100);
        var accepted = 0;
        Parallel.For(0, 1000, _ =>
        {
            if (budget.TryAdmit(1))
            {
                Interlocked.Increment(ref accepted);
            }
        });
        Assert.Equal(20, accepted);
        Assert.False(budget.TryAdmit(2));
        var expiring = new DataGuard.Core.Telemetry.ValidationTraceBudget(20, 100);
        Assert.True(expiring.TryAdmit(1));
        Assert.False(expiring.TryAdmit(101));
        Assert.False(expiring.TryAdmit(102));
    }

    [Fact]
    public async Task NestedConcurrentRequestsHaveIndependentPairsAndNoSecrets()
    {
        using var listener = new Capture();
        var secret = "sentinel_password_SQL_path";
        var contracts = new ContractDescriptor[] { new RawSqlDescriptor(secret, secret, Array.Empty<ParameterDescriptor>(), Array.Empty<ColumnDescriptor>()) };
        var graph = new RuleDependencyGraph().AddRule(new Rule(async ct =>
        {
            await GraphValidationExecutor.ValidateSequentialAsync(new RuleDependencyGraph(), Array.Empty<ContractDescriptor>(), 10, ct);
            return new[] { new ContractViolation("DG100", secret, Microsoft.CodeAnalysis.DiagnosticSeverity.Warning) };
        }));
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => GraphValidationExecutor.ValidateAsync(graph, contracts, 1, 10)));
        Assert.All(results, r => Assert.Single(r.Violations));
        var events = listener.Events.ToArray();
        Assert.Equal(16, events.Length);
        Assert.All(events.GroupBy(e => e.Payload[0]), g => Assert.Equal(new[] { 1, 2 }, g.Select(e => e.Id)));
        Assert.DoesNotContain(secret, System.Text.Json.JsonSerializer.Serialize(events.Select(e => e.Payload)));
        Assert.All(events, e => Assert.All(e.Payload, p => Assert.True(p is long or int or double)));
    }

    [Fact]
    public async Task CancellationErrorAndContainedTimeoutAreSafe()
    {
        using var listener = new Capture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var contract = new ContractDescriptor[] { new RawSqlDescriptor("secret", "secret", Array.Empty<ParameterDescriptor>(), Array.Empty<ColumnDescriptor>()) };
        var cancelled = new RuleDependencyGraph().AddRule(new Rule(ct =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<ContractViolation>>(Array.Empty<ContractViolation>());
        }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => GraphValidationExecutor.ValidateSequentialAsync(cancelled, contract, 10, cancellation.Token));
        await Assert.ThrowsAsync<ArgumentNullException>(() => GraphValidationExecutor.ValidateSequentialAsync(null!, contract, 10));
        var timeout = new RuleDependencyGraph().AddRule(new Rule(_ => throw new TimeoutException("sentinel_password")));
        var result = await GraphValidationExecutor.ValidateSequentialAsync(timeout, contract, 10);
        Assert.True(result.IsIncomplete);
        var ends = listener.Events.Where(e => e.Id == 2).ToArray();
        Assert.Equal(new[] { 2, 4, 1 }, ends.Select(e => (int)e.Payload[2]!));
        Assert.Equal(20, (int)ends[2].Payload[6]!);
        Assert.DoesNotContain("sentinel_password", System.Text.Json.JsonSerializer.Serialize(ends.Select(e => e.Payload)));
    }

    [Fact]
    public async Task ListenerFailureDoesNotReplaceResultAndDisposeDisablesProvider()
    {
        using (var listener = new Capture { Throw = true })
        {
            var result = await GraphValidationExecutor.ValidateSequentialAsync(new RuleDependencyGraph(), Array.Empty<ContractDescriptor>(), 10);
            Assert.False(result.IsIncomplete);
        }
        Assert.False(DataGuard.Core.Telemetry.ValidationTrace.Log.IsEnabled());
    }

    [Fact]
    public async Task MetadataGetterFailureDoesNotChangeValidation()
    {
        var contracts = new ThrowingCount();
        var baseline = await GraphValidationExecutor.ValidateSequentialAsync(new RuleDependencyGraph(), contracts, 10);
        using var listener = new Capture();
        var traced = await GraphValidationExecutor.ValidateSequentialAsync(new RuleDependencyGraph(), contracts, 10);
        Assert.Equal(baseline.IsIncomplete, traced.IsIncomplete);
        Assert.Empty(traced.Violations);
        Assert.Empty(listener.Events);
    }

    [Fact]
    public void ErrorOnlyListenerDoesNotAdmitInformationalRequests()
    {
        using var listener = new Capture();
        var source = DataGuard.Core.Telemetry.ValidationTrace.Log;
        listener.DisableEvents(source);
        listener.EnableEvents(source, EventLevel.Error);
        Assert.Equal(0, source.TryBegin(Array.Empty<ContractDescriptor>(), false));
        Assert.Empty(listener.Events);
    }

    private sealed class ThrowingCount : IReadOnlyList<ContractDescriptor>
    {
        public int Count => throw new InvalidOperationException("sentinel count secret");
        public ContractDescriptor this[int index] => throw new IndexOutOfRangeException();
        public IEnumerator<ContractDescriptor> GetEnumerator() => Enumerable.Empty<ContractDescriptor>().GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class Rule(Func<CancellationToken, Task<IReadOnlyList<ContractViolation>>> run) : IContractRule
    {
        public string RuleId => "DG100";
        public string Name => RuleId;
        public string Description => RuleId;
        public Microsoft.CodeAnalysis.DiagnosticSeverity Severity => Microsoft.CodeAnalysis.DiagnosticSeverity.Warning;
        public Task<IReadOnlyList<ContractViolation>> ValidateAsync(ContractDescriptor contract, IReadOnlyList<ContractDescriptor> all, CancellationToken cancellationToken = default) => run(cancellationToken);
    }

    private sealed class Capture : EventListener
    {
        public bool Throw { get; init; }
        public System.Collections.Concurrent.ConcurrentQueue<(int Id, int Version, object?[] Payload, string[] Names)> Events { get; } = new();
        protected override void OnEventSourceCreated(EventSource source)
        {
            if (source.Name == "DataGuard-Validation")
            {
                EnableEvents(source, EventLevel.Informational);
            }
        }
        protected override void OnEventWritten(EventWrittenEventArgs e)
        {
            if (Throw)
            {
                throw new InvalidOperationException("listener failure");
            }

            if (e.EventId > 0)
            {
                Events.Enqueue((e.EventId, e.Version, e.Payload!.ToArray(), e.PayloadNames!.ToArray()));
            }
        }
    }
}

[CollectionDefinition("Validation trace isolation", DisableParallelization = true)]
public sealed class ValidationTraceCollection
{
}
