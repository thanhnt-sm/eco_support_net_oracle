using System;
using System.Linq;
using DataGuard.Cli;
using DataGuard.Core.Rules;
using DataGuard.Oracle.Adapter;
using DataGuard.PostgreSql.Adapter;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

/// <summary>
/// Oracle/PostgreSQL live describers: the statement guard runs before any connection, and a failed describe is reported as
/// <see cref="LiveSchemaStatus.Failed"/> without fabricated columns (red-team H1). "Failed" in these tests therefore also
/// proves the guard let the statement through to the (unreachable) database; "Unsupported" proves the guard refused it.
/// </summary>
public class LiveQuerySchemaProviderTests
{
    private const string UnreachableOracle = "Data Source=127.0.0.1:1/XEPDB1;User Id=u;Password=oracle-secret;Connection Timeout=2";
    private const string UnreachablePostgres = "Host=127.0.0.1;Port=1;Database=test;Username=u;Password=pg-secret;Timeout=2";

    [Fact]
    public void OracleLiveQuerySchemaProvider_Constructor_RejectsNull()
    {
        var act = () => new OracleLiveQuerySchemaProvider(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void PostgreSqlLiveQuerySchemaProvider_Constructor_RejectsNull()
    {
        var act = () => new PostgreSqlLiveQuerySchemaProvider(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ProviderRuleCatalog_WiresOracleLiveQueryProvider_WhenConnectionSupplied()
    {
        var rules = ProviderRuleCatalog.Get("oracle", "Data Source=test;User Id=u;Password=p;");
        var liveRuleReg = rules.FirstOrDefault(r => r.Rule is LiveSqlShapeValidationRule);

        liveRuleReg.Should().NotBeNull();
    }

    [Fact]
    public void ProviderRuleCatalog_WiresPostgreSqlLiveQueryProvider_WhenConnectionSupplied()
    {
        var rules = ProviderRuleCatalog.Get("postgresql", "Host=localhost;Database=test;");
        var liveRuleReg = rules.FirstOrDefault(r => r.Rule is LiveSqlShapeValidationRule);
        liveRuleReg.Should().NotBeNull();
        liveRuleReg!.Availability.Should().Be(RuleAvailability.Ready);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OracleAndPostgres_UnreachableDatabase_ReturnFailedWithoutFabricatedColumns(bool postgres)
    {
        ILiveQuerySchemaProvider provider = postgres
            ? new PostgreSqlLiveQuerySchemaProvider(UnreachablePostgres)
            : new OracleLiveQuerySchemaProvider(UnreachableOracle);

        var result = await provider.DescribeResultSetAsync("SELECT id, name FROM users", default);

        result.Status.Should().Be(LiveSchemaStatus.Failed);
        result.Columns.Should().BeEmpty("a failed describe must not invent VARCHAR2/text columns from the SQL text");
        result.Error.Should().NotBeNullOrWhiteSpace();
        result.Error.Should().NotContain("oracle-secret").And.NotContain("pg-secret");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OracleAndPostgres_ExplicitSyntacticFallback_IsNeverReportedAsDescribed(bool postgres)
    {
        ILiveQuerySchemaProvider provider = postgres
            ? new PostgreSqlLiveQuerySchemaProvider(UnreachablePostgres, allowSyntacticFallback: true)
            : new OracleLiveQuerySchemaProvider(UnreachableOracle, allowSyntacticFallback: true);

        var result = await provider.DescribeResultSetAsync("SELECT id, name FROM users", default);

        result.Status.Should().Be(LiveSchemaStatus.Failed, "syntactic columns are a non-live hint, never ground truth");
        result.Columns.Select(c => c.Name).Should().Contain(new[] { "id", "name" });
    }

    [Fact]
    public async Task QuerySchemaProvider_StackedQueriesRejected_ButLiteralsWithSemicolonAllowed()
    {
        var oracle = new OracleLiveQuerySchemaProvider(UnreachableOracle, allowSyntacticFallback: true);
        var stacked = await oracle.DescribeResultSetAsync("SELECT 1 FROM dual; DROP TABLE users;", default);
        stacked.Status.Should().Be(LiveSchemaStatus.Unsupported);
        stacked.Columns.Should().BeEmpty();

        var pg = new PostgreSqlLiveQuerySchemaProvider(UnreachablePostgres, allowSyntacticFallback: true);
        var pgStacked = await pg.DescribeResultSetAsync("SELECT 1; DROP TABLE users;", default);
        pgStacked.Status.Should().Be(LiveSchemaStatus.Unsupported);
        pgStacked.Columns.Should().BeEmpty();

        // A semicolon inside a literal passes the guard and reaches the (unreachable) database.
        var withLiteral = await oracle.DescribeResultSetAsync("SELECT 'hello;world' FROM dual", default);
        withLiteral.Status.Should().Be(LiveSchemaStatus.Failed);
    }

    [Fact]
    public async Task OracleAndPostgres_DataModifyingStatement_IsUnsupportedBeforeConnecting()
    {
        var oracle = await new OracleLiveQuerySchemaProvider(UnreachableOracle).DescribeResultSetAsync("UPDATE users SET name = 'x'", default);
        var pg = await new PostgreSqlLiveQuerySchemaProvider(UnreachablePostgres).DescribeResultSetAsync("DELETE FROM users", default);

        oracle.Status.Should().Be(LiveSchemaStatus.Unsupported);
        pg.Status.Should().Be(LiveSchemaStatus.Unsupported);
        oracle.Columns.Should().BeEmpty();
        pg.Columns.Should().BeEmpty();
    }

    [Fact]
    public async Task PostgreSqlLiveQuerySchemaProvider_HandlesMultiLineDollarQuotedStrings()
    {
        var pg = new PostgreSqlLiveQuerySchemaProvider(UnreachablePostgres);
        var sql = "SELECT $$multi\nline\nUPDATE users$$ AS note, 1 AS id;";
        var result = await pg.DescribeResultSetAsync(sql, default);
        result.Status.Should().Be(LiveSchemaStatus.Failed, "the dollar-quoted UPDATE is a literal, so the guard lets the query through");
    }

    [Fact]
    public async Task QuerySchemaProvider_SafeQueriesWithComments_AreNotRejected()
    {
        var pg = new PostgreSqlLiveQuerySchemaProvider(UnreachablePostgres);
        var sql = "SELECT id, name FROM users -- fetch active users\n/* block comment */ WHERE is_active = 1;";
        (await pg.DescribeResultSetAsync(sql, default)).Status.Should().Be(LiveSchemaStatus.Failed);

        var oracle = new OracleLiveQuerySchemaProvider(UnreachableOracle);
        var oracleSql = "SELECT id, name FROM users -- comment; with semicolon\nWHERE is_active = 1";
        (await oracle.DescribeResultSetAsync(oracleSql, default)).Status.Should().Be(LiveSchemaStatus.Failed);
    }

    [Fact]
    public async Task PostgreSqlLiveQuerySchemaProvider_HandlesDollarSignsInsideSingleQuotes()
    {
        var pg = new PostgreSqlLiveQuerySchemaProvider(UnreachablePostgres);
        var sql = "SELECT 'Price is $100$ and fee is $100$' AS fee_note, 1 AS order_id;";
        (await pg.DescribeResultSetAsync(sql, default)).Status.Should().Be(LiveSchemaStatus.Failed);
    }

    [Fact]
    public async Task QuerySchemaProvider_IgnoresParametersInsideComments()
    {
        var pg = new PostgreSqlLiveQuerySchemaProvider(UnreachablePostgres);
        var pgSql = "SELECT id, name FROM users /* @ignored_param */ -- @other_param\nWHERE id = @id;";
        (await pg.DescribeResultSetAsync(pgSql, default)).Status.Should().Be(LiveSchemaStatus.Failed);

        var oracle = new OracleLiveQuerySchemaProvider(UnreachableOracle);
        var oracleSql = "SELECT id, name FROM users /* :ignored_param */ -- :other_param\nWHERE id = :id";
        (await oracle.DescribeResultSetAsync(oracleSql, default)).Status.Should().Be(LiveSchemaStatus.Failed);
    }

    [Fact]
    public async Task QuerySchemaProvider_CommentsWithApostrophes_DoNotCorruptParametersOrQuery()
    {
        var pg = new PostgreSqlLiveQuerySchemaProvider(UnreachablePostgres);
        var pgSql = "SELECT id, name FROM users -- don't break\nWHERE id = @id AND status = 'active';";
        (await pg.DescribeResultSetAsync(pgSql, default)).Status.Should().Be(LiveSchemaStatus.Failed);

        var oracle = new OracleLiveQuerySchemaProvider(UnreachableOracle);
        var oracleSql = "SELECT id, name FROM users /* it's a test */ -- don't fail\nWHERE id = :id";
        (await oracle.DescribeResultSetAsync(oracleSql, default)).Status.Should().Be(LiveSchemaStatus.Failed);
    }

    [Fact]
    public async Task QuerySchemaProvider_UnclosedBlockComment_IsUnsupported_WithHintOnlyWhenOptedIn()
    {
        var pgSql = "SELECT id, name FROM users /* unclosed comment";
        var pg = await new PostgreSqlLiveQuerySchemaProvider(UnreachablePostgres).DescribeResultSetAsync(pgSql, default);
        pg.Status.Should().Be(LiveSchemaStatus.Unsupported);
        pg.Columns.Should().BeEmpty();

        var oracleSql = "SELECT id, name FROM users /* unclosed comment";
        var oracle = await new OracleLiveQuerySchemaProvider(UnreachableOracle, allowSyntacticFallback: true).DescribeResultSetAsync(oracleSql, default);
        oracle.Status.Should().Be(LiveSchemaStatus.Unsupported);
        oracle.Columns.Select(c => c.Name).Should().Contain(new[] { "id", "name" });
    }

    [Fact]
    public async Task QuerySchemaProvider_PrematureClosingParenBreakout_IsUnsupported()
    {
        var pg = new PostgreSqlLiveQuerySchemaProvider(UnreachablePostgres);
        var pgSql = "SELECT id, name FROM users) AS _dg_subq WHERE 1=1 UNION ALL SELECT 1, 2 --";
        (await pg.DescribeResultSetAsync(pgSql, default)).Status.Should().Be(LiveSchemaStatus.Unsupported);

        var oracle = new OracleLiveQuerySchemaProvider(UnreachableOracle);
        var oracleSql = "SELECT id, name FROM users) WHERE 1=1 UNION ALL SELECT 1, 2 FROM dual --";
        (await oracle.DescribeResultSetAsync(oracleSql, default)).Status.Should().Be(LiveSchemaStatus.Unsupported);
    }

    [Fact]
    public void SanitizeErrorMessage_RedactsCloudAndIamCredentials()
    {
        var raw = "Connection failed: server=db.example.com;client_secret=\"my-super-secret\";api_key='abc123xyz';access_token={token_456};authorization=Bearer test";
        var sanitized = LiveSqlShapeValidationRule.SanitizeErrorMessage(raw);
        sanitized.Should().NotContain("my-super-secret");
        sanitized.Should().NotContain("abc123xyz");
        sanitized.Should().NotContain("token_456");
        sanitized.Should().NotContain("Bearer test");
        sanitized.Should().Contain("client_secret=[REDACTED]");
        sanitized.Should().Contain("api_key=[REDACTED]");
        sanitized.Should().Contain("access_token=[REDACTED]");
        sanitized.Should().Contain("authorization=[REDACTED]");
    }
}
