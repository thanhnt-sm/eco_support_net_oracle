using DataGuard.Core.Abstractions;
using DataGuard.Core.Baseline;
using DataGuard.Core.Rules;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace DataGuard.Core.Tests;

/// <summary>Baseline fingerprint v2 (red-team H3): stable identity, no collisions, multiset suppression, legacy compat.</summary>
public class BaselineFingerprintTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "dg-fp-root");

    private static Location At(string relativePath, int line, string? root = null)
    {
        var path = Path.Combine(root ?? Root, relativePath);
        var start = new LinePosition(line, 4);
        var end = new LinePosition(line, 20);
        return Location.Create(path, new TextSpan(line * 10, 16), new LinePositionSpan(start, end));
    }

    private static ContractViolation Finding(
        string ruleId,
        string message,
        Location? location = null,
        IReadOnlyDictionary<string, object?>? properties = null) =>
        new(ruleId, message, DiagnosticSeverity.Warning, location, properties);

    private static Dictionary<string, object?> Props(params (string Key, object? Value)[] pairs) =>
        pairs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    [Fact]
    public void ComputeFingerprint_IsLowercaseSha256Hex()
    {
        var fingerprint = BaselineManager.ComputeFingerprint(Finding("DG015", "x", At("src/A.cs", 1), Props(("table", "T"))), Root);

        fingerprint.Should().HaveLength(64).And.MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void ComputeFingerprint_IsStableWhenTheFindingMovesToAnotherLine()
    {
        var properties = Props(("table", "CUSTOMERS"), ("column", "NAME"));
        var before = Finding("DG016", "Column 'NAME' does not exist in table 'CUSTOMERS'", At("src/Repo.cs", 10), properties);
        var shifted = Finding("DG016", "Column 'NAME' does not exist in table 'CUSTOMERS'", At("src/Repo.cs", 42), properties);

        BaselineManager.ComputeFingerprint(shifted, Root).Should().Be(BaselineManager.ComputeFingerprint(before, Root));
    }

    [Fact]
    public void ComputeFingerprint_RenamingTheFileChangesIt_PathIsPartOfTheIdentity()
    {
        // Documented behavior: the repo-relative path is part of the location, so a rename or move is a new finding
        // (the old baseline entry stops matching and must be re-baselined with 'dataguard baseline').
        var properties = Props(("table", "CUSTOMERS"));
        var original = Finding("DG015", "Table 'CUSTOMERS' does not exist in database", At("src/Repo.cs", 3), properties);
        var renamed = Finding("DG015", "Table 'CUSTOMERS' does not exist in database", At("src/CustomerRepo.cs", 3), properties);

        BaselineManager.ComputeFingerprint(renamed, Root).Should().NotBe(BaselineManager.ComputeFingerprint(original, Root));
    }

    [Fact]
    public void ComputeFingerprint_UsesTheRepoRelativePath_SoCheckoutLocationDoesNotMatter()
    {
        var properties = Props(("table", "T"));
        var rootA = Path.Combine(Path.GetTempPath(), "checkout-a");
        var rootB = Path.Combine(Path.GetTempPath(), "nested", "checkout-b");

        var a = Finding("DG015", "m", At("src/Repo.cs", 1, rootA), properties);
        var b = Finding("DG015", "m", At("src/Repo.cs", 1, rootB), properties);

        BaselineManager.ComputeFingerprint(a, rootA).Should().Be(BaselineManager.ComputeFingerprint(b, rootB));
    }

    [Fact]
    public void NormalizePath_UsesForwardSlashes()
    {
        BaselineManager.NormalizePath("src\\Data\\Repo.cs", null).Should().Be("src/Data/Repo.cs");
        BaselineManager.NormalizePath(Path.Combine(Root, "src", "Repo.cs"), Root).Should().Be("src/Repo.cs");
        BaselineManager.NormalizePath(null, Root).Should().BeEmpty();
    }

    [Fact]
    public void ComputeFingerprint_IgnoresVolatileProperties()
    {
        var stable = Finding("DG004", "Result set has 3 extra columns", At("src/A.cs", 1), Props(("columns", "A,B,C"), ("count", 3), ("line", 7)));
        var changed = Finding("DG004", "Result set has 3 extra columns", At("src/A.cs", 1), Props(("columns", "A,B,C"), ("count", 4), ("line", 99), ("extraCount", 12), ("message", "other")));

        BaselineManager.ComputeFingerprint(changed, Root).Should().Be(BaselineManager.ComputeFingerprint(stable, Root));
        BaselineManager.IsVolatilePropertyKey("startLine").Should().BeTrue();
        BaselineManager.IsVolatilePropertyKey("violationCount").Should().BeTrue();
        BaselineManager.IsVolatilePropertyKey("column").Should().BeFalse("'column' names a database column, it is subject");
        BaselineManager.IsVolatilePropertyKey("table").Should().BeFalse();
    }

    [Fact]
    public void ComputeFingerprint_DoesNotDependOnPropertyOrderOrMessageWhenPropertiesExist()
    {
        var first = Finding("DG016", "message one", null, Props(("table", "T"), ("column", "C")));
        var second = Finding("DG016", "message two (count 5)", null, Props(("column", "C"), ("table", "T")));

        BaselineManager.ComputeFingerprint(second, Root).Should().Be(BaselineManager.ComputeFingerprint(first, Root));
    }

    [Fact]
    public void ComputeFingerprint_DependsOnRuleIdAndSubject()
    {
        var properties = Props(("table", "T"));
        var dg015 = BaselineManager.ComputeFingerprint(Finding("DG015", "m", null, properties), Root);

        BaselineManager.ComputeFingerprint(Finding("DG016", "m", null, properties), Root).Should().NotBe(dg015);
        BaselineManager.ComputeFingerprint(Finding("DG015", "m", null, Props(("table", "U"))), Root).Should().NotBe(dg015);
    }

    [Fact]
    public void ComputeFingerprint_WithoutPropertiesFallsBackToTheMessage()
    {
        var a = BaselineManager.ComputeFingerprint(Finding("DG006", "Property 'A' mismatch"), Root);

        BaselineManager.ComputeFingerprint(Finding("DG006", "Property 'A' mismatch"), Root).Should().Be(a);
        BaselineManager.ComputeFingerprint(Finding("DG006", "Property 'B' mismatch"), Root).Should().NotBe(a);
    }

    [Fact]
    public void ComputeFingerprint_SeparatorsInsideValuesCannotForgeACollision()
    {
        var a = Finding("DG016", "m", null, Props(("table", "A|column=B")));
        var b = Finding("DG016", "m", null, Props(("table", "A"), ("column", "B")));

        BaselineManager.ComputeFingerprint(a, Root).Should().NotBe(BaselineManager.ComputeFingerprint(b, Root));
    }

    [Fact]
    public void ComputeFingerprint_SqlHashSeparatesFindingsInTheSameFile()
    {
        var first = Finding("DG017", "Avoid SELECT *", At("src/Repo.cs", 1), Props(("sqlHash", "aaaaaaaaaaaaaaaa")));
        var second = Finding("DG017", "Avoid SELECT *", At("src/Repo.cs", 1), Props(("sqlHash", "bbbbbbbbbbbbbbbb")));

        BaselineManager.ComputeFingerprint(first, Root).Should().NotBe(BaselineManager.ComputeFingerprint(second, Root));
    }

    [Fact]
    public async Task SelectStar_TwoSitesInDifferentFiles_HaveDifferentFingerprints()
    {
        // Red-team H3 collision: the DG017 message is identical everywhere, so RuleId:Message suppressed every SELECT *.
        var rule = new SelectStarUsageRule();
        var sql = "SELECT * FROM CUSTOMERS";
        var first = new RawSqlDescriptor("q1", sql, Array.Empty<ParameterDescriptor>(), Array.Empty<ColumnDescriptor>(), At("src/A.cs", 5), ReferencedTables: new[] { "CUSTOMERS" });
        var second = first with { Id = "q2", Location = At("src/B.cs", 5) };

        var a = (await rule.ValidateAsync(first, new[] { first })).Should().ContainSingle().Subject;
        var b = (await rule.ValidateAsync(second, new[] { second })).Should().ContainSingle().Subject;

        a.Message.Should().Be(b.Message);
        BaselineManager.ComputeFingerprint(a, Root).Should().NotBe(BaselineManager.ComputeFingerprint(b, Root));
    }

    [Fact]
    public async Task SelectStar_TwoDifferentQueriesInTheSameFile_HaveDifferentFingerprints()
    {
        var rule = new SelectStarUsageRule();
        var first = new RawSqlDescriptor("q1", "SELECT * FROM CUSTOMERS", Array.Empty<ParameterDescriptor>(), Array.Empty<ColumnDescriptor>(), At("src/A.cs", 5));
        var second = new RawSqlDescriptor("q2", "SELECT * FROM ORDERS", Array.Empty<ParameterDescriptor>(), Array.Empty<ColumnDescriptor>(), At("src/A.cs", 9));

        var a = (await rule.ValidateAsync(first, new[] { first })).Single();
        var b = (await rule.ValidateAsync(second, new[] { second })).Single();

        a.Properties.Should().ContainKey("sqlHash");
        BaselineManager.ComputeFingerprint(a, Root).Should().NotBe(BaselineManager.ComputeFingerprint(b, Root));
    }

    [Fact]
    public async Task SelectStar_ReformattedQuery_KeepsItsFingerprint()
    {
        var rule = new SelectStarUsageRule();
        var compact = new RawSqlDescriptor("q1", "SELECT * FROM CUSTOMERS", Array.Empty<ParameterDescriptor>(), Array.Empty<ColumnDescriptor>(), At("src/A.cs", 5));
        var reformatted = compact with { SqlText = "SELECT *\n    FROM   CUSTOMERS", Location = At("src/A.cs", 30) };

        var a = (await rule.ValidateAsync(compact, new[] { compact })).Single();
        var b = (await rule.ValidateAsync(reformatted, new[] { reformatted })).Single();

        BaselineManager.ComputeFingerprint(b, Root).Should().Be(BaselineManager.ComputeFingerprint(a, Root));
    }

    [Fact]
    public async Task ColumnShape_MissingAtoE_And_MissingAtoF_ShareTheMessageButNotTheFingerprint()
    {
        // Red-team H3 collision: Take(5) made both messages "missing required columns: A, B, C, D, E".
        var rule = new ColumnShapeMatchRule();
        var location = At("src/Repo.cs", 12);
        RawSqlDescriptor Query(params string[] expected) => new(
            "q",
            "SELECT Id FROM CUSTOMERS",
            Array.Empty<ParameterDescriptor>(),
            Array.Empty<ColumnDescriptor>(),
            location,
            ExpectedProperties: new[] { "Id" }.Concat(expected).Select(name => new PropertyDescriptor(name, "System.String")).ToList(),
            TargetTypeName: "Customer");
        var fiveMissing = Query("A", "B", "C", "D", "E");
        var sixMissing = Query("A", "B", "C", "D", "E", "F");

        var a = (await rule.ValidateAsync(fiveMissing, new[] { fiveMissing })).Should().ContainSingle().Subject;
        var b = (await rule.ValidateAsync(sixMissing, new[] { sixMissing })).Should().ContainSingle().Subject;

        a.Message.Should().Be(b.Message, "the display text still truncates to five columns");
        b.Properties!["columns"].Should().Be("A,B,C,D,E,F", "Properties carry the full list");
        b.Properties!["entity"].Should().Be("Customer");
        BaselineManager.ComputeFingerprint(a, Root).Should().NotBe(BaselineManager.ComputeFingerprint(b, Root));
    }

    [Fact]
    public async Task ColumnShape_EntityBranch_CarriesEntityColumnsAndSqlHash()
    {
        var rule = new ColumnShapeMatchRule();
        var entity = new EntityDescriptor("e", "Customer", "App.Customer", "CUSTOMERS", new[]
        {
            new PropertyDescriptor("Id", "System.Int32"),
            new PropertyDescriptor("Name", "System.String"),
        });
        var query = new RawSqlDescriptor("q", "SELECT Id FROM CUSTOMERS", Array.Empty<ParameterDescriptor>(), Array.Empty<ColumnDescriptor>(), TargetTypeName: "Customer");

        var violation = (await rule.ValidateAsync(entity, new ContractDescriptor[] { entity, query })).Should().ContainSingle().Subject;

        violation.Properties.Should().NotBeNull();
        violation.Properties!["entity"].Should().Be("Customer");
        violation.Properties["columns"].Should().Be("Name");
        violation.Properties["sqlHash"].Should().BeOfType<string>().Which.Should().HaveLength(16);
    }

    [Fact]
    public async Task FilterNewViolations_IsAMultiset_BaselineOneCurrentTwo_ReportsOne()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dg-fp-{Guid.NewGuid():N}.json");
        try
        {
            var manager = new BaselineManager(path, Root);
            var finding = Finding("DG017", "Avoid SELECT *", At("src/A.cs", 1), Props(("sqlHash", "0123456789abcdef")));
            var baseline = await manager.CreateBaselineAsync(new[] { finding }, "1.0", "Snapshot");

            var fresh = manager.FilterNewViolations(new[] { finding, finding with { Location = At("src/A.cs", 40) } }, baseline).ToList();

            fresh.Should().ContainSingle("one occurrence is accepted, the second identical one is new");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task FilterNewViolations_CountIsHonored_AndOnlyTheExcessIsNew()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dg-fp-{Guid.NewGuid():N}.json");
        try
        {
            var manager = new BaselineManager(path, Root);
            var finding = Finding("DG015", "Table 'T' does not exist", At("src/A.cs", 1), Props(("table", "T")));
            var baseline = await manager.CreateBaselineAsync(new[] { finding, finding }, "1.0", "Snapshot");

            baseline.Violations.Should().ContainSingle().Which.Count.Should().Be(2);
            manager.FilterNewViolations(new[] { finding, finding }, baseline).Should().BeEmpty();
            manager.FilterNewViolations(new[] { finding, finding, finding }, baseline).Should().HaveCount(1);
            manager.FilterNewViolations(new[] { finding }, baseline).Should().BeEmpty();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FilterNewViolations_LegacyEntriesMatchByRuleIdAndMessage()
    {
        var baseline = new BaselineFile(2, DateTimeOffset.UtcNow, "1.0", "Snapshot", "unknown", "hash", new[]
        {
            new BaselineViolation("DG017", "Avoid SELECT *", "Warning", null, null),
        }, null);
        var manager = new BaselineManager("unused.json", Root);
        var current = new[]
        {
            Finding("DG017", "Avoid SELECT *", At("src/A.cs", 1), Props(("sqlHash", "a"))),
            Finding("DG017", "Avoid SELECT *", At("src/B.cs", 1), Props(("sqlHash", "b"))),
            Finding("DG015", "Table 'T' does not exist", null, Props(("table", "T"))),
        };

        var fresh = manager.FilterNewViolations(current, baseline).ToList();

        fresh.Should().ContainSingle().Which.RuleId.Should().Be("DG015", "legacy entries keep the old set semantics for one major");
        BaselineManager.CountLegacyEntries(baseline).Should().Be(1);
    }

    [Fact]
    public async Task CreateBaselineAsync_WritesFingerprintCountAndProperties_AndRoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dg-fp-{Guid.NewGuid():N}.json");
        try
        {
            var manager = new BaselineManager(path, Root);
            var a = Finding("DG016", "Column 'C' does not exist in table 'T'", At("src/A.cs", 3), Props(("table", "T"), ("column", "C")));
            var b = Finding("DG015", "Table 'U' does not exist", At("src/B.cs", 8), Props(("table", "U")));
            await manager.CreateBaselineAsync(new[] { a, b, a }, "1.0", "Snapshot");

            var loaded = await new BaselineManager(path, Root).LoadAsync();

            loaded!.Violations.Should().HaveCount(2).And.OnlyContain(entry => entry.Fingerprint != null && entry.Fingerprint.Length == 64);
            loaded.Violations.Single(entry => entry.RuleId == "DG016").Count.Should().Be(2);
            loaded.Violations.Single(entry => entry.RuleId == "DG016").Properties.Should().ContainKey("column");
            BaselineManager.CountLegacyEntries(loaded).Should().Be(0);
            new BaselineManager(path, Root).FilterNewViolations(new[] { a, a, b }, loaded).Should().BeEmpty();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task FilterNewViolations_BaselineFromAnotherCheckout_StillMatches()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dg-fp-{Guid.NewGuid():N}.json");
        var rootA = Path.Combine(Path.GetTempPath(), "ci-workspace-a");
        var rootB = Path.Combine(Path.GetTempPath(), "dev-machine", "repo");
        try
        {
            var properties = Props(("table", "T"));
            var baseline = await new BaselineManager(path, rootA).CreateBaselineAsync(
                new[] { Finding("DG015", "m", At("src/A.cs", 1, rootA), properties) }, "1.0", "Snapshot");

            var fresh = new BaselineManager(path, rootB).FilterNewViolations(new[] { Finding("DG015", "m", At("src/A.cs", 77, rootB), properties) }, baseline);

            fresh.Should().BeEmpty();
        }
        finally
        {
            File.Delete(path);
        }
    }
}
