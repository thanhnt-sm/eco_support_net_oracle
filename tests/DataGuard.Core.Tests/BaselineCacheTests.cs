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

    [Theory]
    [InlineData(1_000_001)]
    [InlineData((1024 * 1024) + 1)]
    [InlineData((1024 * 1024) + 4097)]
    public async Task LoadAsync_LargeValidJson_ReadsExactlyTheFileLength(int size)
    {
        // Files above 1 MiB go through the memory-mapped path, whose view capacity is page-rounded and zero-filled;
        // reading Capacity bytes handed trailing NULs to the JSON parser (JsonException).
        var path = Path.Combine(Path.GetTempPath(), "dataguard-baseline-large-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            const string Prefix = "{\"Version\":2,\"CreatedAt\":\"2026-01-01T00:00:00Z\",\"SchemaVersion\":\"1.0\",\"GroundTruthMode\":\"Snapshot\","
                + "\"DatabaseVersion\":\"unknown\",\"SchemaHash\":\"ABCDEF0123456789\",\"Violations\":[],\"Padding\":\"";
            const string Suffix = "\"}";
            var json = Prefix + new string('x', size - Prefix.Length - Suffix.Length) + Suffix;
            await File.WriteAllTextAsync(path, json);
            new FileInfo(path).Length.Should().Be(size);

            var loaded = await new BaselineManager(path).LoadAsync();

            loaded.Should().NotBeNull();
            loaded!.SchemaHash.Should().Be("ABCDEF0123456789");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
