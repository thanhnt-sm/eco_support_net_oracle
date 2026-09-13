namespace DataGuard.Core.Health;

/// <summary>Checks that a configured local artifact is readable without exposing its path.</summary>
public sealed class FileReadableHealthProbe(string name, string? filePath) : IHealthProbe
{
    public string Name { get; } = name;

    public async Task<HealthComponentStatus> ProbeAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return new HealthComponentStatus(Name, HealthComponentState.Unknown, "Not configured.");
        }

        try
        {
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.Asynchronous);
            _ = await stream.ReadAsync(Memory<byte>.Empty, cancellationToken).ConfigureAwait(false);
            return new HealthComponentStatus(Name, HealthComponentState.Healthy, "Readable.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new HealthComponentStatus(Name, HealthComponentState.Unhealthy, "Unreadable.");
        }
    }
}

/// <summary>Checks free space at a local path without publishing filesystem details.</summary>
public sealed class DiskSpaceHealthProbe(string rootPath, long minimumFreeBytes) : IHealthProbe
{
    public string Name => "disk";

    public Task<HealthComponentStatus> ProbeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(rootPath));
            var available = new DriveInfo(root!).AvailableFreeSpace;
            return Task.FromResult(new HealthComponentStatus(
                Name,
                available >= minimumFreeBytes ? HealthComponentState.Healthy : HealthComponentState.Unhealthy,
                available >= minimumFreeBytes ? "Free space is sufficient." : "Free space is below the configured threshold."));
        }
        catch
        {
            return Task.FromResult(new HealthComponentStatus(Name, HealthComponentState.Unknown, "Disk state is unavailable."));
        }
    }
}

/// <summary>Checks managed process memory against a configured local budget.</summary>
public sealed class MemoryHealthProbe(long maximumManagedBytes) : IHealthProbe
{
    public string Name => "memory";

    public Task<HealthComponentStatus> ProbeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var used = GC.GetTotalMemory(forceFullCollection: false);
        return Task.FromResult(new HealthComponentStatus(
            Name,
            used <= maximumManagedBytes ? HealthComponentState.Healthy : HealthComponentState.Unhealthy,
            used <= maximumManagedBytes ? "Managed memory is within the configured budget." : "Managed memory is above the configured budget."));
    }
}
