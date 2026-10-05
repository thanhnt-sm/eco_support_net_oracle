using System;
using System.Linq;
using System.Threading.Tasks;
using DataGuard.Cli;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules;
using DataGuard.Core.Validation;
using DataGuard.MySql.Adapter;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

/// <summary>
/// MySQL live describer without a server: the guard runs before connecting, an unreachable server is
/// <see cref="LiveSchemaStatus.Failed"/>, and nothing is fabricated. The live describe itself is covered by
/// <see cref="MySqlIntegrationTests"/> behind DATAGUARD_REQUIRE_LIVE_RELATIONAL=1.
/// </summary>
public class MySqlLiveQuerySchemaProviderTests
{
    private const string Unreachable = "Server=127.0.0.1;Port=1;Database=dataguard;User ID=u;Password=mysql-secret;Connection Timeout=2";

    [Fact]
    public void Constructor_RejectsNull()
    {
        var act = () => new MySqlLiveQuerySchemaProvider(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task UnreachableServer_ReturnsFailedWithSanitizedErrorAndNoColumns()
    {
        var provider = new MySqlLiveQuerySchemaProvider(Unreachable);

        var result = await provider.DescribeResultSetAsync("SELECT id, name FROM t WHERE id = @p", default);

        result.Status.Should().Be(LiveSchemaStatus.Failed);
        result.Columns.Should().BeEmpty();
        result.Error.Should().NotBeNullOrWhiteSpace().And.NotContain("mysql-secret");
    }

    [Theory]
    [InlineData("SELECT 1; DROP TABLE t")]
    [InlineData("UPDATE t SET name = 'x'")]
    [InlineData("REPLACE INTO t VALUES (1)")]
    [InlineData("SELECT id FROM t FOR UPDATE")]
    [InlineData("SELECT id INTO OUTFILE '/tmp/x' FROM t")]
    [InlineData("SELECT LOAD_FILE('/etc/passwd') AS f")]
    [InlineData("SELECT 1 # comment hides '\n; DROP TABLE t; -- '")]
    [InlineData("SELECT 'a\\'; DROP TABLE t; -- ' AS x")]
    [InlineData("SELECT 1 /*!50000 ; DROP TABLE t */")]
    [InlineData("SELECT 1 --1) AS a; DROP TABLE t; SELECT (1")]
    [InlineData("SELECT id FROM t) AS x UNION SELECT 1 FROM (SELECT 1")]
    [InlineData("SELECT id, name FROM t /* unclosed")]
    [InlineData("   ")]
    public async Task GuardRefusesBeforeConnecting(string sql)
    {
        var provider = new MySqlLiveQuerySchemaProvider(Unreachable);

        var result = await provider.DescribeResultSetAsync(sql, default);

        result.Status.Should().Be(LiveSchemaStatus.Unsupported);
        result.Columns.Should().BeEmpty();
    }

    [Theory]
    [InlineData("SELECT REPLACE(name, 'a', 'b') AS n FROM t")]
    [InlineData("SELECT id, name FROM t -- trailing comment\nWHERE id = @p")]
    [InlineData("SELECT 'semi;colon' AS s FROM t")]
    public async Task GuardAllowsReadOnlyQueries_ThatThenFailOnUnreachableServer(string sql)
    {
        var provider = new MySqlLiveQuerySchemaProvider(Unreachable);

        var result = await provider.DescribeResultSetAsync(sql, default);

        result.Status.Should().Be(LiveSchemaStatus.Failed);
    }

    [Fact]
    public void ProviderRuleCatalog_WiresMySqlLiveQueryProvider_WhenConnectionSupplied()
    {
        var registration = ProviderRuleCatalog.Get("mysql", Unreachable)
            .Single(r => r.Rule is LiveSqlShapeValidationRule);

        registration.Availability.Should().Be(RuleAvailability.Ready);
        ((LiveSqlShapeValidationRule)registration.Rule).HasLiveConnection.Should().BeTrue();
    }

    [Fact]
    public async Task CatalogRule_UnreachableMySql_RecordsUnevaluatedInsteadOfSkippingSilently()
    {
        var rule = (LiveSqlShapeValidationRule)ProviderRuleCatalog.Get("mysql", Unreachable)
            .Single(r => r.Rule is LiveSqlShapeValidationRule).Rule;
        var query = new RawSqlDescriptor(
            Id: "project-sql:Repo.cs:10",
            SqlText: "SELECT id, name FROM t WHERE id = @p",
            Parameters: Array.Empty<ParameterDescriptor>(),
            ResultColumns: Array.Empty<ColumnDescriptor>(),
            ExpectedProperties: new[] { new PropertyDescriptor("Id", "int", null, null, false, null, true, false) },
            TargetTypeName: "Row");

        var result = await new ConcurrentValidationEngine(1).ValidateDetailedAsync(new ContractDescriptor[] { query }, new IContractRule[] { rule });

        result.Violations.Should().BeEmpty();
        result.UnevaluatedContracts.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { RuleId = "DG020", ContractId = "project-sql:Repo.cs:10" });
    }
}
