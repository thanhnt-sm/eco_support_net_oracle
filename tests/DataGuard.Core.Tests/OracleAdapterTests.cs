using DataGuard.Core.Abstractions;
using DataGuard.Oracle.Adapter;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DataGuard.Core.Tests;

/// <summary>
/// Unit tests for Oracle adapter dialect checker (no DB required).
/// </summary>
public class OracleDialectCheckerTests
{
    private readonly OracleDialectChecker _checker = new();

    [Fact]
    public void CheckOracleSyntax_NonOracleContext_DetectsDecode()
    {
        var violations = _checker.CheckOracleSyntaxInNonOracleContext("SELECT DECODE(col, 1, 'a', 'b') FROM t", isOracleContext: false);
        violations.Should().ContainSingle(v => v.RuleId == "DG010");
    }

    [Fact]
    public void CheckOracleSyntax_NonOracleContext_DetectsNvl()
    {
        var violations = _checker.CheckOracleSyntaxInNonOracleContext("SELECT NVL(col, 0) FROM t", isOracleContext: false);
        violations.Should().ContainSingle(v => v.RuleId == "DG010");
    }

    [Fact]
    public void CheckOracleSyntax_NonOracleContext_DetectsRowNum()
    {
        var violations = _checker.CheckOracleSyntaxInNonOracleContext("SELECT * FROM t WHERE ROWNUM <= 10", isOracleContext: false);
        violations.Should().ContainSingle(v => v.RuleId == "DG010");
    }

    [Fact]
    public void CheckOracleSyntax_NonOracleContext_DetectsDual()
    {
        var violations = _checker.CheckOracleSyntaxInNonOracleContext("SELECT 1 FROM DUAL", isOracleContext: false);
        violations.Should().ContainSingle(v => v.RuleId == "DG010");
    }

    [Fact]
    public void CheckOracleSyntax_NonOracleContext_DetectsConnectBy()
    {
        var violations = _checker.CheckOracleSyntaxInNonOracleContext("SELECT * FROM t CONNECT BY PRIOR id = parent_id", isOracleContext: false);
        violations.Should().ContainSingle(v => v.RuleId == "DG010");
    }

    [Fact]
    public void CheckOracleSyntax_OracleContext_NoViolation()
    {
        var violations = _checker.CheckOracleSyntaxInNonOracleContext("SELECT DECODE(col, 1, 'a', 'b') FROM t", isOracleContext: true);
        violations.Should().BeEmpty();
    }

    [Fact]
    public void CheckOracleSyntax_EmptySql_NoViolation()
    {
        var violations = _checker.CheckOracleSyntaxInNonOracleContext("", isOracleContext: false);
        violations.Should().BeEmpty();
    }

    [Fact]
    public void CheckNonOracleSyntax_OracleContext_DetectsIsNull()
    {
        var violations = _checker.CheckNonOracleSyntaxInOracleContext("SELECT ISNULL(col, 0) FROM t", isOracleContext: true);
        violations.Should().ContainSingle(v => v.RuleId == "DG011");
    }

    [Fact]
    public void CheckNonOracleSyntax_OracleContext_DetectsGetDate()
    {
        var violations = _checker.CheckNonOracleSyntaxInOracleContext("SELECT GETDATE()", isOracleContext: true);
        violations.Should().ContainSingle(v => v.RuleId == "DG011");
    }

    [Fact]
    public void CheckNonOracleSyntax_OracleContext_DetectsIdentity()
    {
        var violations = _checker.CheckNonOracleSyntaxInOracleContext("SELECT IDENTITY(int, 1, 1) AS Id FROM t", isOracleContext: true);
        violations.Should().ContainSingle(v => v.RuleId == "DG011");
    }

    [Fact]
    public void CheckNonOracleSyntax_OracleContext_DetectsNewId()
    {
        var violations = _checker.CheckNonOracleSyntaxInOracleContext("SELECT NEWID()", isOracleContext: true);
        violations.Should().ContainSingle(v => v.RuleId == "DG011");
    }

    [Fact]
    public void CheckNonOracleSyntax_NonOracleContext_NoViolation()
    {
        var violations = _checker.CheckNonOracleSyntaxInOracleContext("SELECT ISNULL(col, 0) FROM t", isOracleContext: false);
        violations.Should().BeEmpty();
    }

    [Fact]
    public void CheckNonOracleSyntax_EmptySql_NoViolation()
    {
        var violations = _checker.CheckNonOracleSyntaxInOracleContext("", isOracleContext: true);
        violations.Should().BeEmpty();
    }
}
