using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules;
using DataGuard.Core.Rules.Sql;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

public class PhantomColumnIdentityTests
{
    public static IEnumerable<object[]> FoldingCases()
    {
        foreach (var provider in new[] { "oracle", "postgresql" })
        {
            var catalogName = provider == "oracle" ? "TOTAL" : "total";
            foreach (var column in new[] { "total", "TOTAL", $"\"{catalogName}\"", "\"Total\"" })
            {
                foreach (var shape in new[]
                {
                    "SELECT {0} FROM orders o",
                    "SELECT o.{0} FROM orders o",
                    "SELECT i.id FROM items i WHERE EXISTS (SELECT o.{0} FROM orders o)",
                    "SELECT o.id FROM orders o WHERE EXISTS (SELECT {0} FROM items i)",
                    "SELECT o.id FROM orders o WHERE EXISTS (SELECT i.id FROM items i WHERE o.{0} = 1)",
                })
                {
                    yield return new object[] { provider, catalogName, string.Format(shape, column), column == "\"Total\"" ? 1 : 0 };
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(FoldingCases))]
    public async Task ProviderFolding_AppliesAcrossScopes(string provider, string catalogName, string sql, int expected)
    {
        (await Analyze(sql, provider, catalogName)).Should().HaveCount(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("unknown")]
    [InlineData("sqlserver")]
    [InlineData("mysql")]
    public async Task NeutralProviders_KeepCaseInsensitiveColumns(string? provider)
    {
        (await Analyze("SELECT o.\"Total\", \"Total\" FROM orders o", provider, "TOTAL")).Should().BeEmpty();
    }

    [Theory]
    [InlineData("postgresql", "total", "Total")]
    [InlineData("postgresql", "Total", "total")]
    [InlineData("oracle", "TOTAL", "Total")]
    [InlineData("oracle", "Total", "TOTAL")]
    public async Task CaseDistinctCatalogColumns_AreBothRetained(string provider, string first, string second)
    {
        (await Analyze($"SELECT o.\"{first}\", o.\"{second}\", \"{first}\", \"{second}\" FROM orders o", provider, first, second)).Should().BeEmpty();
        (await Analyze("SELECT o.\"ToTaL\" FROM orders o", provider, first, second)).Should().ContainSingle();
    }

    [Theory]
    [InlineData("oracle", "TOTAL")]
    [InlineData("postgresql", "total")]
    public async Task UnknownSources_SuppressUnprovableColumns(string provider, string column)
    {
        foreach (var sql in new[]
        {
            "SELECT \"Total\" FROM orders o JOIN missing m ON 1 = 1",
            "SELECT m.\"Total\" FROM missing m",
            "SELECT m.id FROM missing m WHERE EXISTS (SELECT \"Total\" FROM orders o)",
            "WITH c AS (SELECT * FROM orders) SELECT c.\"Total\" FROM c",
            "SELECT d.\"Total\" FROM (SELECT * FROM orders) d",
        })
        {
            (await Analyze(sql, provider, column)).Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData("oracle", "TOTAL")]
    [InlineData("postgresql", "total")]
    public async Task RepeatedMissingColumn_IsDeduplicated(string provider, string column)
    {
        (await Analyze("SELECT o.\"Total\", \"Total\" FROM orders o WHERE o.\"Total\" = 1 ORDER BY o.\"Total\"", provider, column))
            .Should().ContainSingle();
    }

    [Theory]
    [InlineData("oracle")]
    [InlineData("postgresql")]
    public async Task MixedCaseCatalogColumn_RequiresExactQuotedReference(string provider)
    {
        (await Analyze("SELECT o.\"Total\", \"Total\" FROM orders o", provider, "Total")).Should().BeEmpty();
        (await Analyze("SELECT total FROM orders", provider, "Total")).Should().ContainSingle();
        (await Analyze("SELECT TOTAL FROM orders", provider, "Total")).Should().ContainSingle();
    }

    [Theory]
    [InlineData("oracle")]
    [InlineData("postgresql")]
    public async Task CatalogNames_WithEscapedQuotesAndSpaces_AreVerbatim(string provider)
    {
        foreach (var name in new[] { "a\"b", " total ", "a.b" })
        {
            var quoted = SchemaObjectName.Quote(name);
            (await Analyze($"SELECT o.{quoted}, {quoted} FROM orders o", provider, name)).Should().BeEmpty();
        }
    }

    [Fact]
    public void PublicCanonicalColumns_KeepLegacyContract()
    {
        var table = SchemaTableIndex.For(Catalog("postgresql", "total", "Total")).Resolve(null, "orders").Single();
        table.Columns.Should().ContainKey("TOTAL");
        table.Columns.Should().HaveCount(2, "id plus the canonical total entry remain available to DG005 and adapters");
    }

    private static ColumnDescriptor Col(string name) => new(name, "NUMBER", null, null, null, false, null);

    private static DatabaseSchemaDescriptor Catalog(string? provider, params string[] columns) => new(
        "schema:identity",
        new[]
        {
            new DatabaseTableDescriptor(provider == "oracle" ? "ORDERS" : "orders", new[] { Col("id"), Col("ID") }.Concat(columns.Select(Col)).ToArray()),
            new DatabaseTableDescriptor(provider == "oracle" ? "ITEMS" : "items", new[] { Col("id"), Col("ID") }),
        },
        "CHAR");

    private static async Task<List<ContractViolation>> Analyze(string sql, string? provider, params string[] columns)
    {
        var raw = new RawSqlDescriptor("raw:identity", sql, Array.Empty<ParameterDescriptor>(), Array.Empty<ColumnDescriptor>())
        {
            ConnectionProviderHint = provider,
        };
        return (await new PhantomColumnRule().ValidateAsync(raw, new ContractDescriptor[] { Catalog(provider, columns) })).ToList();
    }
}
