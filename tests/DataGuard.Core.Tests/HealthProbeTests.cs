using System.Text.Json;
using DataGuard.Core.Health;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

public class HealthProbeTests
{
    [Fact]
    public void StartupBeforeFirstRefresh_IsNotReadyWithoutWaiting()
    {
        var clock = new TestTimeProvider(DateTimeOffset.Parse("2026-09-13T00:00:00Z"));
        var coordinator = new HealthProbeCoordinator(
            new IHealthProbe[] { new TestProbe("disk", HealthComponentState.Healthy) },
            new HealthStateStore(),
            timeProvider: clock);

        coordinator.Snapshot.StartupComplete.Should().BeFalse();
        coordinator.IsReady.Should().BeFalse();
    }

    [Theory]
    [InlineData(HealthComponentState.Healthy, true)]
    [InlineData(HealthComponentState.Degraded, false)]
    [InlineData(HealthComponentState.Unhealthy, false)]
    [InlineData(HealthComponentState.Unknown, false)]
    public async Task IsReady_UsesExplicitComponentStateMatrix(HealthComponentState state, bool expected)
    {
        var coordinator = new HealthProbeCoordinator(
            new IHealthProbe[] { new TestProbe("disk", state) },
            new HealthStateStore());

        await coordinator.RefreshAsync();

        coordinator.IsReady.Should().Be(expected);
    }

    [Fact]
    public async Task RefreshAsync_PublishesSortedReadinessSnapshot()
    {
        var store = new HealthStateStore();
        var coordinator = new HealthProbeCoordinator(
            new IHealthProbe[]
            {
                new TestProbe("disk", HealthComponentState.Healthy),
                new TestProbe("baseline", HealthComponentState.Healthy),
            },
            store);

        var snapshot = await coordinator.RefreshAsync();

        snapshot.StartupComplete.Should().BeTrue();
        snapshot.IsReady.Should().BeTrue();
        snapshot.Components.Select(component => component.Component).Should().ContainInOrder("baseline", "disk");
        store.Snapshot.Should().BeSameAs(snapshot);
    }

    [Fact]
    public void IsReady_ReadsSnapshotWithoutRunningProbeWork()
    {
        var probe = new CountingProbe("cached", HealthComponentState.Healthy);
        var coordinator = new HealthProbeCoordinator(new[] { probe }, new HealthStateStore());

        _ = coordinator.IsReady;
        _ = coordinator.Snapshot;

        probe.Calls.Should().Be(0);
    }

    [Fact]
    public async Task RefreshAsync_TimeoutPublishesUnhealthyWithoutExceptionDetails()
    {
        var coordinator = new HealthProbeCoordinator(
            new IHealthProbe[] { new DelayedProbe() },
            new HealthStateStore(),
            probeTimeout: TimeSpan.FromMilliseconds(10));

        var snapshot = await coordinator.RefreshAsync();

        snapshot.IsReady.Should().BeFalse();
        snapshot.Components.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new HealthComponentStatus("slow", HealthComponentState.Unhealthy, "Probe timed out."));
    }

    [Fact]
    public async Task RefreshAsync_WithoutConfiguredProbes_IsNotReady()
    {
        var coordinator = new HealthProbeCoordinator(Array.Empty<IHealthProbe>(), new HealthStateStore());

        var snapshot = await coordinator.RefreshAsync();

        snapshot.StartupComplete.Should().BeTrue();
        snapshot.Components.Should().BeEmpty();
        snapshot.IsReady.Should().BeFalse("an empty readiness definition must not report a false-green host");
    }

    [Fact]
    public async Task IsReady_BecomesFalseWhenSnapshotExceedsMaximumAge()
    {
        var clock = new TestTimeProvider(DateTimeOffset.Parse("2026-09-13T00:00:00Z"));
        var coordinator = new HealthProbeCoordinator(
            new IHealthProbe[] { new TestProbe("disk", HealthComponentState.Healthy) },
            new HealthStateStore(),
            timeProvider: clock,
            maximumSnapshotAge: TimeSpan.FromSeconds(30));

        await coordinator.RefreshAsync();
        coordinator.IsReady.Should().BeTrue();
        clock.Advance(TimeSpan.FromSeconds(31));

        coordinator.IsReady.Should().BeFalse();
    }

    [Fact]
    public async Task LocalProbes_ReportConfigurationAndResourceStateWithoutPathDetails()
    {
        var filePath = Path.GetTempFileName();
        try
        {
            var readable = await new FileReadableHealthProbe("snapshot", filePath).ProbeAsync();
            var unconfigured = await new FileReadableHealthProbe("baseline", null).ProbeAsync();
            var memory = await new MemoryHealthProbe(0).ProbeAsync();

            readable.State.Should().Be(HealthComponentState.Healthy);
            unconfigured.State.Should().Be(HealthComponentState.Unknown);
            unconfigured.Detail.Should().NotContain(filePath);
            memory.State.Should().Be(HealthComponentState.Unhealthy);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task FileProbe_UnreadablePathIsNotPublishedInSnapshotDetails()
    {
        var sensitivePath = Path.Combine(Path.GetTempPath(), "vault-token-secret-" + Guid.NewGuid().ToString("N"));
        var status = await new FileReadableHealthProbe("baseline", sensitivePath).ProbeAsync();
        var json = JsonSerializer.Serialize(new HealthSnapshot(DateTimeOffset.UtcNow, true, new[] { status }));

        status.State.Should().Be(HealthComponentState.Unhealthy);
        status.Detail.Should().NotContain(sensitivePath);
        json.Should().NotContain(sensitivePath);
        json.Should().NotContain("Exception");
    }

    private sealed class TestProbe(string name, HealthComponentState state) : IHealthProbe
    {
        public string Name => name;

        public Task<HealthComponentStatus> ProbeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new HealthComponentStatus(Name, state));
    }

    private sealed class CountingProbe(string name, HealthComponentState state) : IHealthProbe
    {
        public string Name => name;

        public int Calls { get; private set; }

        public Task<HealthComponentStatus> ProbeAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new HealthComponentStatus(Name, state));
        }
    }

    private sealed class DelayedProbe : IHealthProbe
    {
        public string Name => "slow";

        public async Task<HealthComponentStatus> ProbeAsync(CancellationToken cancellationToken = default)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            return new HealthComponentStatus(Name, HealthComponentState.Healthy);
        }
    }

    private sealed class TestTimeProvider(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset _now = initial;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan amount) => _now += amount;
    }
}
