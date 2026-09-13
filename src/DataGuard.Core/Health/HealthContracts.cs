namespace DataGuard.Core.Health;

/// <summary>Public, non-secret state of one bounded local health probe.</summary>
public sealed record HealthComponentStatus(string Component, HealthComponentState State, string? Detail = null);

/// <summary>Allowed component states for readiness aggregation.</summary>
public enum HealthComponentState
{
    Healthy,
    Degraded,
    Unhealthy,
    Unknown,
}

/// <summary>Atomic observation consumed by a transport without triggering probe work.</summary>
public sealed record HealthSnapshot(
    DateTimeOffset ObservedAt,
    bool StartupComplete,
    IReadOnlyList<HealthComponentStatus> Components)
{
    public bool IsReady => StartupComplete && Components.Count > 0 && Components.All(component => component.State == HealthComponentState.Healthy);
}

/// <summary>Transport-neutral, bounded local readiness probe.</summary>
public interface IHealthProbe
{
    string Name { get; }

    Task<HealthComponentStatus> ProbeAsync(CancellationToken cancellationToken = default);
}
