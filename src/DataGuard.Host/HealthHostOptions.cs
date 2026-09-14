public sealed class HealthHostOptions
{
    /// <summary>
    /// Enables the optional host process. The CLI/library product leaves this
    /// false so running the host assembly alone does not bind a listener.
    /// </summary>
    public bool EnableHost { get; init; }

    /// <summary>
    /// Enables the optional loopback health routes. The CLI/library product does
    /// not expose HTTP endpoints by default; set this explicitly only for a
    /// controlled host integration test or owner-approved process wrapper.
    /// </summary>
    public bool ExposeEndpoints { get; init; }

    public string? SnapshotPath { get; init; }

    public string? BaselinePath { get; init; }

    public long MinimumFreeDiskBytes { get; init; } = 100 * 1024 * 1024;

    public long MaximumManagedMemoryBytes { get; init; } = 1024 * 1024 * 1024;

    public int ProbeTimeoutSeconds { get; init; } = 5;

    public int MaximumSnapshotAgeSeconds { get; init; } = 30;

    public int RefreshIntervalSeconds { get; init; } = 10;

    public void Validate()
    {
        if (MaximumSnapshotAgeSeconds <= 0)
        {
            throw new InvalidOperationException("DataGuardHealth:MaximumSnapshotAgeSeconds must be greater than zero.");
        }

        if (RefreshIntervalSeconds <= 0 || RefreshIntervalSeconds > 3600)
        {
            throw new InvalidOperationException("DataGuardHealth:RefreshIntervalSeconds must be between 1 and 3600 seconds.");
        }

        if (RefreshIntervalSeconds >= MaximumSnapshotAgeSeconds)
        {
            throw new InvalidOperationException("DataGuardHealth:RefreshIntervalSeconds must be less than MaximumSnapshotAgeSeconds.");
        }
    }
}
