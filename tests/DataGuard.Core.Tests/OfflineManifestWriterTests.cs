using System.Text.Json;
using DataGuard.Cli;
using DataGuard.Core.Abstractions;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

public sealed class OfflineManifestWriterTests
{
    [Fact]
    public async Task WriteAsync_RejectsOverlongTarget()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dg-preflight-{Guid.NewGuid():N}.json");
        var act = () => OfflineManifestWriter.WriteAsync(path, new string('x', 129), "sqlserver", Array.Empty<ContractDescriptor>());

        await act.Should().ThrowAsync<ArgumentException>();
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public async Task WriteAsync_RejectsMoreThanThousandContracts()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dg-preflight-{Guid.NewGuid():N}.json");
        var contracts = Enumerable.Range(0, 1_001)
            .Select(index => (ContractDescriptor)new EntityDescriptor($"entity:{index}", $"Entity{index}", "Entity", null, Array.Empty<PropertyDescriptor>()))
            .ToArray();
        var act = () => OfflineManifestWriter.WriteAsync(path, "fixture", "sqlserver", contracts);

        await act.Should().ThrowAsync<ArgumentException>();
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public async Task WriteAsync_ProducesBoundedRedactedManifest()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dg-preflight-{Guid.NewGuid():N}.json");
        try
        {
            var contracts = new ContractDescriptor[]
            {
                new EntityDescriptor("entity", "Customer", "Customer", "Customers", Array.Empty<PropertyDescriptor>()),
            };
            await OfflineManifestWriter.WriteAsync(path, "fixture", "sqlserver", contracts);

            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            document.RootElement.GetProperty("schemaVersion").GetInt32().Should().Be(1);
            document.RootElement.GetProperty("target").GetString().Should().Be("fixture");
            document.RootElement.GetProperty("provider").GetString().Should().Be("sqlserver");
            document.RootElement.GetProperty("contentDigest").GetString().Should().MatchRegex("^[0-9a-f]{64}$");
            document.RootElement.GetProperty("contractCount").GetInt32().Should().Be(1);
            document.RootElement.GetProperty("contracts").GetArrayLength().Should().Be(1);
            document.RootElement.GetProperty("contracts")[0].GetProperty("name").GetString().Should().Be("Customer");
            document.RootElement.GetProperty("findings").GetArrayLength().Should().Be(0);
            (await File.ReadAllTextAsync(path)).Should().NotContain("connectionString");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
