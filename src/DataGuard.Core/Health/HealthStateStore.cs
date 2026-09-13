namespace DataGuard.Core.Health;

/// <summary>Thread-safe store for the last completed health observation.</summary>
public sealed class HealthStateStore
{
    private HealthSnapshot _snapshot = new(DateTimeOffset.MinValue, false, Array.Empty<HealthComponentStatus>());

    public HealthSnapshot Snapshot => Volatile.Read(ref _snapshot);

    public void Publish(HealthSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Volatile.Write(ref _snapshot, snapshot);
    }
}
