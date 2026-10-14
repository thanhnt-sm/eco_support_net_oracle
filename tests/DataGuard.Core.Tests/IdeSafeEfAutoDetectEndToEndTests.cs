using System.Text.Json;
using DataGuard.Core.Baseline;
using FluentAssertions;
using Xunit;
using static DataGuard.Core.Tests.IdeSafeEndToEndSupport;

namespace DataGuard.Core.Tests;

/// <summary>
/// Process-level tests for the EF Core design-time ground truth under <c>--ide-safe</c>: the Visual Studio argv
/// (<c>validate --ide-safe --project &lt;solutionDir&gt;</c>) against a workspace that carries a <c>*ModelSnapshot.cs</c>.
/// The fixture (<c>Fixtures/EfProject</c>) declares <c>Order</c> with a <c>HasColumnName</c> naming drift (DG006) and a
/// repository query that selects a column set smaller than the entity (DG004).
/// </summary>
public class IdeSafeEfAutoDetectEndToEndTests
{
    private const string SnapshotFileName = "ShopContextModelSnapshot.cs";

    [Fact]
    public void Validate_IdeSafe_Project_DoesNotAutoDetectEfModel_Today()
    {
        // Pin (3.T): the VS argv has no EF ground truth today (RC3); flipped by Phase 3.
        var ws = CreateEfWorkspace();
        try
        {
            var sarif = Path.Combine(ws, "out", "r.sarif");
            var (exitCode, _, stderr) = RunCli(null, ws, "validate", "--provider", "sqlserver", "--format", "sarif", "--output", sarif, "--project", ws, "--progress", "--ide-safe");

            Lines(stderr)[0].Should().Be("ide-safe: active");
            stderr.Should().Contain("Warning: no ground truth");
            stderr.Should().NotContain("ide-safe: ef model");
            exitCode.Should().Be(1, "the drifting query is a DG004 error; stderr: " + stderr);
            Results(sarif).Should().ContainSingle(r => r.RuleId == "DG004");
        }
        finally
        {
            Directory.Delete(ws, recursive: true);
        }
    }

    [Fact]
    public void Validate_ExplicitEfProject_WithoutIdeSafe_ParsesFixtureEntities()
    {
        // Proves and protects the fixture: the explicit option parses both entities and lifts the ground-truth gate.
        var ws = CreateEfWorkspace();
        try
        {
            var sarif = Path.Combine(ws, "out", "r.sarif");
            var (exitCode, _, stderr) = RunCli(null, ws, "validate", "--ef-project", ws, "--project", ws, "--format", "sarif", "--output", sarif, "--progress");

            stderr.Should().NotContain("no ground truth");
            stderr.Should().NotContain("ide-safe");
            Lines(stderr).Count(l => l.Contains("\"Detail\":\"EntityDescriptor\"", StringComparison.Ordinal)).Should().Be(2, "Customer and Order parsed");
            exitCode.Should().Be(1, stderr);
            var results = Results(sarif);
            results.Should().Contain(r => r.RuleId == "DG006" && r.Message.Contains("CustomerId"));
            results.Should().Contain(r => r.RuleId == "DG004" && r.Message.Contains("CustomerId"));
        }
        finally
        {
            Directory.Delete(ws, recursive: true);
        }
    }

    [Fact]
    public async Task Validate_IdeSafe_WithDefaultSnapshot_PrintsUsingSnapshot()
    {
        var ws = CreateEfWorkspace();
        try
        {
            await WriteDefaultSnapshotAsync(ws, "sqlserver");
            var sarif = Path.Combine(ws, "out", "r.sarif");
            var (_, stdout, stderr) = RunCli(null, ws, "validate", "--provider", "sqlserver", "--format", "sarif", "--output", sarif, "--project", ws, "--progress", "--ide-safe");

            Lines(stderr)[0].Should().Be("ide-safe: active");
            stdout.Should().Contain("Using snapshot .dataguard-snapshot.json");
            stderr.Should().NotContain("no ground truth");
        }
        finally
        {
            Directory.Delete(ws, recursive: true);
        }
    }

    internal sealed record SarifResult(string RuleId, string Level, string Message, IReadOnlyList<(string Uri, int StartLine, int StartColumn)> Locations);

    internal static IReadOnlyList<SarifResult> Results(string sarifPath)
    {
        File.Exists(sarifPath).Should().BeTrue("SARIF must be written at " + sarifPath);
        using var document = JsonDocument.Parse(File.ReadAllText(sarifPath));
        var results = new List<SarifResult>();
        foreach (var result in document.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray())
        {
            var locations = new List<(string, int, int)>();
            foreach (var location in result.GetProperty("locations").EnumerateArray())
            {
                var physical = location.GetProperty("physicalLocation");
                var region = physical.GetProperty("region");
                locations.Add((
                    physical.GetProperty("artifactLocation").GetProperty("uri").GetString() ?? string.Empty,
                    region.GetProperty("startLine").GetInt32(),
                    region.GetProperty("startColumn").GetInt32()));
            }

            results.Add(new SarifResult(
                result.GetProperty("ruleId").GetString() ?? string.Empty,
                result.GetProperty("level").GetString() ?? string.Empty,
                result.GetProperty("message").GetProperty("text").GetString() ?? string.Empty,
                locations));
        }

        return results;
    }

    /// <summary>The <c>Data</c> object of the <c>Summary</c> progress event, or null when none was emitted.</summary>
    internal static JsonElement? SummaryData(string stderr)
    {
        foreach (var line in Lines(stderr))
        {
            if (!line.StartsWith("{\"Kind\":\"Summary\"", StringComparison.Ordinal))
            {
                continue;
            }

            using var document = JsonDocument.Parse(line);
            return document.RootElement.GetProperty("Data").Clone();
        }

        return null;
    }

    /// <summary>Temp workspace (no csproj, directory mode) holding the EF fixture as <c>.cs</c> files.</summary>
    internal static string CreateEfWorkspace(bool includeSnapshot = true)
    {
        var ws = Directory.CreateTempSubdirectory("dg-ide-ef").FullName;
        File.Copy(Path.Combine(FixtureDirectory(), "OrderRepository.cs.txt"), Path.Combine(ws, "OrderRepository.cs"));
        if (includeSnapshot)
        {
            File.Copy(Path.Combine(FixtureDirectory(), SnapshotFileName + ".txt"), Path.Combine(ws, SnapshotFileName));
        }

        return ws;
    }

    internal static string FixtureDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "DataGuard.Core.Tests", "Fixtures", "EfProject");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("tests/DataGuard.Core.Tests/Fixtures/EfProject not found above " + AppContext.BaseDirectory);
    }

    internal static async Task<string> WriteDefaultSnapshotAsync(string dir, string provider)
    {
        var path = Path.Combine(dir, ".dataguard-snapshot.json");
        var schema = new List<SnapshotTable>
        {
            new("ORDERS", new List<SnapshotColumn>
            {
                new("ORDER_ID", "int", null, null, 10, 0, false, null),
                new("CUST_REF", "int", null, null, 10, 0, false, null),
                new("TOTAL_AMOUNT", "decimal", null, null, 18, 2, true, null),
            }),
        };
        await LegacySnapshotFiles.WriteV3Async(path, schema, provider);
        return path;
    }
}
