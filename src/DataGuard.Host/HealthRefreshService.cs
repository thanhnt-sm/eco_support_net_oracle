using DataGuard.Core.Health;

public sealed class HealthRefreshService : IHostedService
{
    private readonly TimeSpan _refreshInterval = TimeSpan.FromSeconds(10);
    private CancellationTokenSource? _stopSource;
    private Task? _refreshLoop;

    public HealthRefreshService(HealthProbeCoordinator coordinator, TimeSpan? refreshInterval = null)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        this.coordinator = coordinator;
        if (refreshInterval is { } interval)
        {
            if (interval <= TimeSpan.Zero || interval > TimeSpan.FromHours(1))
            {
                throw new ArgumentOutOfRangeException(nameof(refreshInterval), "Health refresh interval must be greater than zero and no more than one hour.");
            }

            _refreshInterval = interval;
        }
    }

    private readonly HealthProbeCoordinator coordinator;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await coordinator.RefreshAsync(cancellationToken).ConfigureAwait(false);
        _stopSource = new CancellationTokenSource();
        _refreshLoop = RefreshLoopAsync(_stopSource.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _stopSource?.Cancel();
        if (_refreshLoop is not null)
        {
            await _refreshLoop.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        _stopSource?.Dispose();
        _stopSource = null;
        _refreshLoop = null;
    }

    private async Task RefreshLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_refreshInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await coordinator.RefreshAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
    }
}
