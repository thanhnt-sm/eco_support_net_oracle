namespace DataGuard.Core.Health;

/// <summary>Runs local probes outside request handlers and publishes one snapshot at a time.</summary>
public sealed class HealthProbeCoordinator
{
    private readonly IReadOnlyList<IHealthProbe> _probes;
    private readonly HealthStateStore _stateStore;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _probeTimeout;
    private readonly TimeSpan _maximumSnapshotAge;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public HealthProbeCoordinator(
        IEnumerable<IHealthProbe> probes,
        HealthStateStore stateStore,
        TimeProvider? timeProvider = null,
        TimeSpan? probeTimeout = null,
        TimeSpan? maximumSnapshotAge = null)
    {
        ArgumentNullException.ThrowIfNull(probes);
        _probes = probes.OrderBy(probe => probe.Name, StringComparer.Ordinal).ToArray();
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _probeTimeout = probeTimeout is { } timeout && timeout > TimeSpan.Zero ? timeout : TimeSpan.FromSeconds(5);
        _maximumSnapshotAge = maximumSnapshotAge is { } age && age > TimeSpan.Zero ? age : TimeSpan.FromSeconds(30);
    }

    public HealthSnapshot Snapshot => _stateStore.Snapshot;

    /// <summary>Returns readiness only while the last completed observation remains fresh.</summary>
    public bool IsReady => Snapshot.IsReady && _timeProvider.GetUtcNow() - Snapshot.ObservedAt <= _maximumSnapshotAge;

    /// <summary>Runs each probe once, with a timeout, while preserving the previous snapshot on cancellation.</summary>
    public async Task<HealthSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!await _refreshGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return Snapshot;
        }

        try
        {
            var statuses = new List<HealthComponentStatus>(_probes.Count);
            foreach (var probe in _probes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var status = await probe.ProbeAsync(cancellationToken).WaitAsync(_probeTimeout, cancellationToken).ConfigureAwait(false);
                    statuses.Add(status with { Component = probe.Name });
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (TimeoutException)
                {
                    statuses.Add(new HealthComponentStatus(probe.Name, HealthComponentState.Unhealthy, "Probe timed out."));
                }
                catch
                {
                    statuses.Add(new HealthComponentStatus(probe.Name, HealthComponentState.Unhealthy, "Probe failed."));
                }
            }

            var snapshot = new HealthSnapshot(_timeProvider.GetUtcNow(), true, statuses);
            _stateStore.Publish(snapshot);
            return snapshot;
        }
        finally
        {
            _refreshGate.Release();
        }
    }
}
