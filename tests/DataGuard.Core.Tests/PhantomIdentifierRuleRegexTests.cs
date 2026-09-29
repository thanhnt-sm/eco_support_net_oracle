using System.Diagnostics;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules;
using DataGuard.Core.Sources;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

/// <summary>
/// Regex-DoS hardening (red-team F11): the SELECT-list scan must be linear, keep its semantics on
/// sub-queries, and oversized SQL literals must be skipped before any rule regex sees them.
/// </summary>
public class PhantomIdentifierRuleRegexTests
{
    private static DatabaseSchemaDescriptor Schema() => new(
        "schema:1",
        new List<DatabaseTableDescriptor>
        {
            new("CUSTOMERS", new List<ColumnDescriptor>
            {
                new("ID", "NUMBER", null, null, 22, false, null),
                new("NAME", "VARCHAR2", 100, null, null, true, null),
            }),
            new("ORDERS", new List<ColumnDescriptor>
            {
                new("ID", "NUMBER", null, null, 22, false, null),
                new("TOTAL", "NUMBER", null, null, 22, false, null),
            }),
        },
        "CHAR");

    private static RawSqlDescriptor Sql(string text) =>
        new("raw:1", text, Array.Empty<ParameterDescriptor>(), Array.Empty<ColumnDescriptor>());

    [Fact]
    public async Task ValidateAsync_SelectFollowedBy200KSpaces_CompletesUnderOneSecond()
    {
        var rule = new PhantomIdentifierRule();
        var hostile = "SELECT" + new string(' ', 200_000) + "x";

        var stopwatch = Stopwatch.StartNew();
        var violations = await rule.ValidateAsync(Sql(hostile), new ContractDescriptor[] { Schema() });
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
        violations.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_SelectListStopsAtFirstFrom_WhenSubqueryFollows()
    {
        // Lazy semantics must survive the linear rewrite: the select list is "ID, NAME", not the subquery tail.
        var rule = new PhantomIdentifierRule();
        var sql = "SELECT ID, NAME FROM CUSTOMERS WHERE ID IN (SELECT ID FROM ORDERS)";

        var violations = await rule.ValidateAsync(Sql(sql), new ContractDescriptor[] { Schema() });

        violations.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_PhantomSelectListColumn_StillReportsDG016()
    {
        var rule = new PhantomIdentifierRule();
        var sql = "select id,\n  address\nfrom customers where id = :id";

        var violations = await rule.ValidateAsync(Sql(sql), new ContractDescriptor[] { Schema() });

        violations.Should().ContainSingle(v => v.RuleId == "DG016" && v.Message.Contains("'ADDRESS'"));
    }

    [Fact]
    public async Task ExtractContractsAsync_SqlLiteralOver256KiB_IsSkippedWithNote()
    {
        var dir = Directory.CreateTempSubdirectory("dg-literal-cap").FullName;
        try
        {
            var huge = "SELECT Id FROM Users WHERE Name IN ('" + new string('a', ProjectCSharpSqlSource.MaxSqlLiteralLength) + "')";
            var small = "SELECT Id FROM Users";
            File.WriteAllText(
                Path.Combine(dir, "Repo.cs"),
                "public class Repo { public const string A = \"" + huge + "\"; public const string B = \"" + small + "\"; }");

            var contracts = await new ProjectCSharpSqlSource(dir).ExtractContractsAsync();

            var sqls = contracts.OfType<RawSqlDescriptor>().ToList();
            sqls.Should().ContainSingle();
            sqls[0].SqlText.Should().Be(small);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
