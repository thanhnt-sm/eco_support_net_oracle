using DataGuard.Core.Baseline;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

public class BaselineCacheTests
{
    [Fact]
    public async Task CreateBaselineAsync_FailedReplace_RemovesTemporaryFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dataguard-baseline-atomic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var manager = new BaselineManager(directory);

            var act = () => manager.CreateBaselineAsync(Array.Empty<DataGuard.Core.Abstractions.ContractViolation>(), "1", "Snapshot");

            await act.Should().ThrowAsync<Exception>();
            Directory.EnumerateFiles(Path.GetDirectoryName(directory)!, $".{Path.GetFileName(directory)}.*.tmp")
                .Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_UsesContentDigestCacheAndInvalidatesWhenFileChanges()
    {
        var path = Path.Combine(Path.GetTempPath(), "dataguard-baseline-cache-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var writer = new BaselineManager(path);
            await writer.CreateBaselineAsync(Array.Empty<DataGuard.Core.Abstractions.ContractViolation>(), "1", "Snapshot");
            var before = BaselineManager.CacheMetrics;

            (await new BaselineManager(path).LoadAsync()).Should().NotBeNull();
            (await new BaselineManager(path).LoadAsync()).Should().NotBeNull();
            var afterHit = BaselineManager.CacheMetrics;
            afterHit.Misses.Should().BeGreaterThanOrEqualTo(before.Misses + 1);
            afterHit.Hits.Should().BeGreaterThanOrEqualTo(before.Hits + 1);

            var json = await File.ReadAllTextAsync(path);
            await File.WriteAllTextAsync(path, json.Replace("\"SchemaVersion\": \"1\"", "\"SchemaVersion\": \"2\"", StringComparison.Ordinal));
            var changed = await new BaselineManager(path).LoadAsync();

            changed!.SchemaVersion.Should().Be("2");
            BaselineManager.CacheMetrics.Misses.Should().BeGreaterThanOrEqualTo(afterHit.Misses + 1);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
