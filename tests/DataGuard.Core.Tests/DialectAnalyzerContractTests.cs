using DataGuard.Core.Abstractions;
using DataGuard.MySql.Adapter;
using DataGuard.Oracle.Adapter;
using DataGuard.PostgreSql.Adapter;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

public sealed class DialectAnalyzerContractTests
{
    [Theory]
    [InlineData("oracle")]
    [InlineData("postgresql")]
    [InlineData("mysql")]
    public void Analyzer_ReportsCanonicalDialectName(string dialectName)
    {
        var analyzer = Create(dialectName);

        analyzer.DialectName.Should().Be(dialectName);
    }

    [Theory]
    [InlineData("oracle", "SELECT NVL(value, 0) FROM dual", "DG010")]
    [InlineData("postgresql", "SELECT id::text FROM users", "PG001")]
    [InlineData("mysql", "SELECT `id` FROM users", "MY001")]
    public void Analyze_DetectsDialectSyntaxOutsideTargetContext(string dialectName, string sql, string ruleId)
    {
        var violations = Create(dialectName).Analyze(sql, isTargetDialect: false);

        violations.Should().Contain(violation => violation.RuleId == ruleId);
    }

    [Fact]
    public void OracleDialectChecker_DetectsSqlServerExecWithBracketedIdentifiers()
    {
        var checker = new OracleDialectChecker();
        var violations = checker.CheckSqlServerSyntaxLeak("EXEC [dbo].[MyProcedure]", isOracleContext: true);
        violations.Should().ContainSingle().Which.RuleId.Should().Be("DG013");
    }

    [Fact]
    public void OracleDialectChecker_IgnoresKeywordsInsideCommentsAndStringLiterals()
    {
        var checker = new OracleDialectChecker();
        var violations = checker.CheckOracleSyntaxInNonOracleContext("SELECT id, 'DUAL' AS name FROM users -- NOTE: DUAL table\n/* SYSDATE */", isOracleContext: false);
        violations.Should().BeEmpty();
    }

    [Fact]
    public void OracleDialectChecker_DoesNotFlagStandardConcatenationOperator()
    {
        var checker = new OracleDialectChecker();
        var violations = checker.CheckOracleSyntaxInNonOracleContext("SELECT first_name || ' ' || last_name FROM users", isOracleContext: false);
        violations.Should().BeEmpty();
    }

    [Fact]
    public void OracleDialectChecker_CheckSqlServerSyntaxLeak_IgnoresComments()
    {
        var checker = new OracleDialectChecker();
        var violations = checker.CheckSqlServerSyntaxLeak("SELECT id FROM users -- Replaced EXEC dbo.MyProc with CALL\n/* EXEC dbo.OldProc */", isOracleContext: true);
        violations.Should().BeEmpty();
    }

    [Fact]
    public void OracleDialectChecker_CheckRawSqlUnmappedTypeUsage_IgnoresCommentsAndLiterals()
    {
        var checker = new OracleDialectChecker();
        var violations = checker.CheckRawSqlUnmappedTypeUsage("SELECT id, 'Spent MONEY on groceries' AS note FROM users -- DATETIME2 not used", isOracleContext: true);
        violations.Should().BeEmpty();
    }

    private static IDialectAnalyzer Create(string dialectName) => dialectName switch
    {
        "oracle" => new OracleDialectChecker(),
        "postgresql" => new PostgreSqlDialectChecker(),
        "mysql" => new MySqlDialectChecker(),
        _ => throw new ArgumentOutOfRangeException(nameof(dialectName)),
    };
}
