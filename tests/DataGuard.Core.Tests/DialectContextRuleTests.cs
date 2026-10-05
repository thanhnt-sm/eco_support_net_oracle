using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataGuard.Cli;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules;
using DataGuard.MySql.Adapter;
using DataGuard.Oracle.Adapter;
using DataGuard.PostgreSql.Adapter;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

/// <summary>
/// "Foreign syntax" dialect rules (MY001, PG001, DG010) never report a provider's own dialect, PG002 reports each foreign
/// construct once, COALESCE (ANSI) is never treated as Oracle-specific, and DG010 names the context provider as target.
/// </summary>
public class DialectContextRuleTests
{
    private const string MySqlSql = "SELECT `id`, IFNULL(`status`, 'new') FROM `orders` LIMIT 0, 10";
    private const string PostgreSqlSql = "UPDATE orders SET status = 'x' WHERE status::text ILIKE 'open%' RETURNING id";
    private const string OracleSql = "SELECT NVL(status, 'new'), DECODE(kind, 1, 'a', 'b') FROM orders WHERE ROWNUM <= 10";

    [Theory]
    [InlineData("SELECT NVL(status, 'new') FROM orders", "NVL")]
    [InlineData("SELECT DECODE(kind, 1, 'a', 'b') FROM orders", "DECODE")]
    [InlineData("SELECT TOP 10 id FROM orders", "TOP")]
    [InlineData("SELECT TOP (10) id FROM orders", "TOP")]
    [InlineData("SELECT ISNULL(status, 'x') FROM orders", "ISNULL")]
    [InlineData("SELECT GETDATE()", "GETDATE")]
    public void Pg002_ReportsEachForeignConstructOnce(string sql, string keyword)
    {
        var violations = new PostgreSqlDialectChecker().CheckNonPostgreSqlSyntaxInPostgreSqlContext(sql, isPostgreSqlContext: true);

        violations.Should().ContainSingle(v => v.RuleId == "PG002")
            .Which.Properties!["keyword"].Should().Be(keyword);
    }

    [Theory]
    [InlineData("SELECT id FROM orders ORDER BY id LIMIT 10")]
    [InlineData("SELECT id FROM orders LIMIT 10 OFFSET 20")]
    [InlineData("CREATE TABLE t (id INT GENERATED ALWAYS AS IDENTITY)")]
    [InlineData("SELECT COALESCE(status, 'new') FROM orders")]
    public void Pg002_PostgreSqlOwnSyntax_IsNotReported(string sql) =>
        new PostgreSqlDialectChecker().CheckNonPostgreSqlSyntaxInPostgreSqlContext(sql, isPostgreSqlContext: true).Should().BeEmpty();

    [Fact]
    public void Coalesce_IsAnsi_InEveryDialectChecker()
    {
        const string sql = "SELECT COALESCE(status, 'new') AS status FROM orders";
        var postgres = new PostgreSqlDialectChecker();
        var mysql = new MySqlDialectChecker();
        var oracle = new OracleDialectChecker();

        postgres.CheckNonPostgreSqlSyntaxInPostgreSqlContext(sql, isPostgreSqlContext: true).Should().BeEmpty();
        postgres.CheckPostgreSqlSyntaxInNonPostgreSqlContext(sql, isPostgreSqlContext: false).Should().BeEmpty();
        mysql.CheckNonMySqlSyntaxInMySqlContext(sql, isMySqlContext: true).Should().BeEmpty();
        mysql.CheckMySqlSyntaxInNonMySqlContext(sql, isMySqlContext: false).Should().BeEmpty();
        oracle.CheckOracleSyntaxInNonOracleContext(sql, isOracleContext: false, targetProvider: "postgresql").Should().BeEmpty();
        oracle.CheckNonOracleSyntaxInOracleContext(sql, isOracleContext: true).Should().BeEmpty();
    }

    [Theory]
    [InlineData("mysql", null)] // catalog provider, no descriptor hint
    [InlineData(null, "mysql")] // descriptor hint only
    [InlineData("postgresql", "mysql")] // the hint wins over the catalog provider
    public async Task My001_OwnDialect_IsNoOp(string? provider, string? hint) =>
        (await RunAsync(new MySqlSyntaxInNonMySqlContextRule(provider), MySqlSql, hint)).Should().BeEmpty();

    [Theory]
    [InlineData(null, null)] // unknown context keeps the original behavior
    [InlineData("mysql", "sqlserver")] // a SQL Server call site in a MySQL project
    public async Task My001_ForeignContext_StillReports(string? provider, string? hint) =>
        (await RunAsync(new MySqlSyntaxInNonMySqlContextRule(provider), MySqlSql, hint)).Should().Contain(v => v.RuleId == "MY001");

    [Theory]
    [InlineData("postgresql", null)]
    [InlineData("postgres", null)]
    [InlineData(null, "postgresql")]
    public async Task Pg001_OwnDialect_IsNoOp(string? provider, string? hint) =>
        (await RunAsync(new PostgreSqlSyntaxInNonPostgreSqlContextRule(provider), PostgreSqlSql, hint)).Should().BeEmpty();

    [Theory]
    [InlineData(null, null)]
    [InlineData("postgresql", "mysql")]
    public async Task Pg001_ForeignContext_StillReports(string? provider, string? hint) =>
        (await RunAsync(new PostgreSqlSyntaxInNonPostgreSqlContextRule(provider), PostgreSqlSql, hint)).Should().Contain(v => v.RuleId == "PG001");

    [Theory]
    [InlineData("oracle", null)]
    [InlineData(null, "oracle")]
    [InlineData("sqlserver", "oracle")]
    public async Task Dg010_OwnDialect_IsNoOp(string? provider, string? hint) =>
        (await RunAsync(new OracleSyntaxInNonOracleContextRule(provider), OracleSql, hint)).Should().BeEmpty();

    [Theory]
    [InlineData("postgresql", null, "postgresql")]
    [InlineData(null, "postgres", "postgresql")]
    [InlineData("mysql", null, "mysql")]
    [InlineData("sqlserver", null, "sqlserver")]
    [InlineData(null, null, "sqlserver")] // unknown context: SQL Server, as before
    public async Task Dg010_MessageNamesTheContextProviderAsTarget(string? provider, string? hint, string expectedTarget)
    {
        var violations = await RunAsync(new OracleSyntaxInNonOracleContextRule(provider), "SELECT NVL(a, 0), SYSDATE FROM t", hint);

        violations.Should().HaveCount(2).And.OnlyContain(v =>
            v.RuleId == "DG010" &&
            v.Message.StartsWith($"[Migration: Oracle -> {expectedTarget}]", StringComparison.Ordinal) &&
            Equals(v.Properties!["targetProvider"], expectedTarget));
        if (expectedTarget == "postgresql")
        {
            violations.Single(v => Equals(v.Properties!["keyword"], "SYSDATE")).Message.Should().Contain("(PostgreSQL)").And.NotContain("GETDATE");
        }
    }

    [Theory]
    [InlineData("mysql", MySqlSql, "MY001")]
    [InlineData("postgresql", PostgreSqlSql, "PG001")]
    [InlineData("postgresql", OracleSql, "DG010")]
    public async Task Catalog_PassesItsProviderToTheForeignSyntaxRules(string provider, string sql, string ruleId)
    {
        var rule = ProviderRuleCatalog.GetReadyRules(provider).Should().ContainSingle(r => r.RuleId == ruleId).Subject;

        // No hint: the catalog provider is the context. MY001/PG001 are no-ops for their own provider; DG010 names it.
        var violations = await RunAsync(rule, sql, hint: null);
        if (ruleId == "DG010")
        {
            violations.Should().NotBeEmpty().And.OnlyContain(v => v.Message.StartsWith("[Migration: Oracle -> postgresql]", StringComparison.Ordinal));
        }
        else
        {
            violations.Should().BeEmpty();
        }
    }

    private static async Task<IReadOnlyList<ContractViolation>> RunAsync(IContractRule rule, string sql, string? hint)
    {
        var raw = new RawSqlDescriptor("raw:dialect", sql, Array.Empty<ParameterDescriptor>(), Array.Empty<ColumnDescriptor>(), ConnectionProviderHint: hint);
        return (await rule.ValidateAsync(raw, new ContractDescriptor[] { raw }, CancellationToken.None)).ToList();
    }
}
