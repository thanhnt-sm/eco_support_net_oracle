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

    private static IDialectAnalyzer Create(string dialectName) => dialectName switch
    {
        "oracle" => new OracleDialectChecker(),
        "postgresql" => new PostgreSqlDialectChecker(),
        "mysql" => new MySqlDialectChecker(),
        _ => throw new ArgumentOutOfRangeException(nameof(dialectName)),
    };
}
