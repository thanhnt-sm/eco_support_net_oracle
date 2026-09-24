using System;
using System.Linq;
using DataGuard.Cli;
using DataGuard.Core.Rules;
using DataGuard.Oracle.Adapter;
using DataGuard.PostgreSql.Adapter;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

public class LiveQuerySchemaProviderTests
{
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

    [Fact]
    public async Task QuerySchemaProvider_StackedQueriesRejected_ButLiteralsWithSemicolonAllowed()
    {
        var oracle = new OracleLiveQuerySchemaProvider("Data Source=test;User Id=u;Password=p;");
        var stacked = await oracle.DescribeResultSetAsync("SELECT 1 FROM dual; DROP TABLE users;", default);
        stacked.Should().BeEmpty();

        var pg = new PostgreSqlLiveQuerySchemaProvider("Host=localhost;Database=test;");
        var pgStacked = await pg.DescribeResultSetAsync("SELECT 1; DROP TABLE users;", default);
        pgStacked.Should().BeEmpty();

        // Semicolon inside literal enters try/catch and falls back to syntactic column extraction
        // because no live DB is reachable, instead of early reject on semicolon.
        var withLiteral = await oracle.DescribeResultSetAsync("SELECT 'hello;world' FROM dual", default);
        withLiteral.Should().NotBeEmpty();
    }

    [Fact]
    public async Task PostgreSqlLiveQuerySchemaProvider_HandlesMultiLineDollarQuotedStrings()
    {
        var pg = new PostgreSqlLiveQuerySchemaProvider("Host=localhost;Database=test;");
        var sql = "SELECT $$multi\nline\nUPDATE users$$ AS note, 1 AS id;";
        var columns = await pg.DescribeResultSetAsync(sql, default);
        columns.Select(c => c.Name).Should().Contain(new[] { "note", "id" });
    }

    [Fact]
    public async Task QuerySchemaProvider_SafeQueriesWithComments_AreNotRejected()
    {
        var pg = new PostgreSqlLiveQuerySchemaProvider("Host=localhost;Database=test;");
        var sql = "SELECT id, name FROM users -- fetch active users\n/* block comment */ WHERE is_active = 1;";
        var columns = await pg.DescribeResultSetAsync(sql, default);
        columns.Select(c => c.Name).Should().Contain(new[] { "id", "name" });

        var oracle = new OracleLiveQuerySchemaProvider("Data Source=test;User Id=u;Password=p;");
        var oracleSql = "SELECT id, name FROM users -- comment; with semicolon\nWHERE is_active = 1";
        var oracleColumns = await oracle.DescribeResultSetAsync(oracleSql, default);
        oracleColumns.Select(c => c.Name).Should().Contain(new[] { "id", "name" });
    }

    [Fact]
    public async Task PostgreSqlLiveQuerySchemaProvider_HandlesDollarSignsInsideSingleQuotes()
    {
        var pg = new PostgreSqlLiveQuerySchemaProvider("Host=localhost;Database=test;");
        var sql = "SELECT 'Price is $100$ and fee is $100$' AS fee_note, 1 AS order_id;";
        var columns = await pg.DescribeResultSetAsync(sql, default);
        columns.Select(c => c.Name).Should().Contain(new[] { "fee_note", "order_id" });
    }

    [Fact]
    public async Task QuerySchemaProvider_IgnoresParametersInsideComments()
    {
        var pg = new PostgreSqlLiveQuerySchemaProvider("Host=localhost;Database=test;");
        var pgSql = "SELECT id, name FROM users /* @ignored_param */ -- @other_param\nWHERE id = @id;";
        var pgColumns = await pg.DescribeResultSetAsync(pgSql, default);
        pgColumns.Select(c => c.Name).Should().Contain(new[] { "id", "name" });

        var oracle = new OracleLiveQuerySchemaProvider("Data Source=test;User Id=u;Password=p;");
        var oracleSql = "SELECT id, name FROM users /* :ignored_param */ -- :other_param\nWHERE id = :id";
        var oracleColumns = await oracle.DescribeResultSetAsync(oracleSql, default);
        oracleColumns.Select(c => c.Name).Should().Contain(new[] { "id", "name" });
    }

    [Fact]
    public async Task QuerySchemaProvider_CommentsWithApostrophes_DoNotCorruptParametersOrQuery()
    {
        var pg = new PostgreSqlLiveQuerySchemaProvider("Host=localhost;Database=test;");
        var pgSql = "SELECT id, name FROM users -- don't break\nWHERE id = @id AND status = 'active';";
        var pgColumns = await pg.DescribeResultSetAsync(pgSql, default);
        pgColumns.Select(c => c.Name).Should().Contain(new[] { "id", "name" });

        var oracle = new OracleLiveQuerySchemaProvider("Data Source=test;User Id=u;Password=p;");
        var oracleSql = "SELECT id, name FROM users /* it's a test */ -- don't fail\nWHERE id = :id";
        var oracleColumns = await oracle.DescribeResultSetAsync(oracleSql, default);
        oracleColumns.Select(c => c.Name).Should().Contain(new[] { "id", "name" });
    }

    [Fact]
    public async Task QuerySchemaProvider_UnclosedBlockComment_FallsBackToSyntacticExtraction()
    {
        var pg = new PostgreSqlLiveQuerySchemaProvider("Host=localhost;Database=test;");
        var pgSql = "SELECT id, name FROM users /* unclosed comment";
        var pgColumns = await pg.DescribeResultSetAsync(pgSql, default);
        pgColumns.Select(c => c.Name).Should().Contain(new[] { "id", "name" });

        var oracle = new OracleLiveQuerySchemaProvider("Data Source=test;User Id=u;Password=p;");
        var oracleSql = "SELECT id, name FROM users /* unclosed comment";
        var oracleColumns = await oracle.DescribeResultSetAsync(oracleSql, default);
        oracleColumns.Select(c => c.Name).Should().Contain(new[] { "id", "name" });
    }

    [Fact]
    public async Task QuerySchemaProvider_PrematureClosingParenBreakout_FallsBackToSyntacticExtraction()
    {
        var pg = new PostgreSqlLiveQuerySchemaProvider("Host=localhost;Database=test;");
        var pgSql = "SELECT id, name FROM users) AS _dg_subq WHERE 1=1 UNION ALL SELECT 1, 2 --";
        var pgColumns = await pg.DescribeResultSetAsync(pgSql, default);
        pgColumns.Select(c => c.Name).Should().Contain(new[] { "id", "name" });

        var oracle = new OracleLiveQuerySchemaProvider("Data Source=test;User Id=u;Password=p;");
        var oracleSql = "SELECT id, name FROM users) WHERE 1=1 UNION ALL SELECT 1, 2 FROM dual --";
        var oracleColumns = await oracle.DescribeResultSetAsync(oracleSql, default);
        oracleColumns.Select(c => c.Name).Should().Contain(new[] { "id", "name" });
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
