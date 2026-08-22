using DataGuard.Core.Abstractions;
using DataGuard.Core.Models;
using DataGuard.PostgreSql.Adapter;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DataGuard.Core.Tests;

/// <summary>
/// Unit tests for PostgreSQL adapter pure logic (no DB required).
/// </summary>
public class PostgreSqlDialectCheckerTests
{
    private readonly PostgreSqlDialectChecker _checker = new();

    [Fact]
    public void CheckPostgreSqlSyntax_NonPgContext_DetectsSerial()
    {
        var violations = _checker.CheckPostgreSqlSyntaxInNonPostgreSqlContext("CREATE TABLE t (id SERIAL)", isPostgreSqlContext: false);
        violations.Should().ContainSingle(v => v.RuleId == "PG001");
    }

    [Fact]
    public void CheckPostgreSqlSyntax_NonPgContext_DetectsILike()
    {
        var violations = _checker.CheckPostgreSqlSyntaxInNonPostgreSqlContext("SELECT * FROM t WHERE name ILIKE '%test%'", isPostgreSqlContext: false);
        violations.Should().ContainSingle(v => v.RuleId == "PG001");
    }

    [Fact]
    public void CheckPostgreSqlSyntax_NonPgContext_DetectsCastOperator()
    {
        var violations = _checker.CheckPostgreSqlSyntaxInNonPostgreSqlContext("SELECT col::text FROM t", isPostgreSqlContext: false);
        violations.Should().ContainSingle(v => v.RuleId == "PG001");
    }

    [Fact]
    public void CheckPostgreSqlSyntax_PgContext_NoViolation()
    {
        var violations = _checker.CheckPostgreSqlSyntaxInNonPostgreSqlContext("CREATE TABLE t (id SERIAL)", isPostgreSqlContext: true);
        violations.Should().BeEmpty();
    }

    [Fact]
    public void CheckNonPostgreSqlSyntax_PgContext_DetectsNvl()
    {
        var violations = _checker.CheckNonPostgreSqlSyntaxInPostgreSqlContext("SELECT NVL(col, 0) FROM t", isPostgreSqlContext: true);
        violations.Should().ContainSingle(v => v.RuleId == "PG002");
    }

    [Fact]
    public void CheckNonPostgreSqlSyntax_PgContext_DetectsGetDate()
    {
        var violations = _checker.CheckNonPostgreSqlSyntaxInPostgreSqlContext("SELECT GETDATE()", isPostgreSqlContext: true);
        violations.Should().ContainSingle(v => v.RuleId == "PG002");
    }

    [Fact]
    public void CheckNonPostgreSqlSyntax_PgContext_DetectsConvert()
    {
        var violations = _checker.CheckNonPostgreSqlSyntaxInPostgreSqlContext("SELECT CONVERT(int, col) FROM t", isPostgreSqlContext: true);
        violations.Should().ContainSingle(v => v.RuleId == "PG002");
    }

    [Fact]
    public void CheckNonPostgreSqlSyntax_NonPgContext_NoViolation()
    {
        var violations = _checker.CheckNonPostgreSqlSyntaxInPostgreSqlContext("SELECT NVL(col, 0) FROM t", isPostgreSqlContext: false);
        violations.Should().BeEmpty();
    }

    [Fact]
    public void CheckPostgreSqlSyntax_EmptySql_NoViolation()
    {
        var violations = _checker.CheckPostgreSqlSyntaxInNonPostgreSqlContext("", isPostgreSqlContext: false);
        violations.Should().BeEmpty();
    }
}

public class PostgreSqlLengthMismatchDetectorTests
{
    private readonly PostgreSqlLengthMismatchDetector _detector = new();

    [Fact]
    public void Detect_ExceedsLength_ReturnsViolation()
    {
        var entity = new EntityDescriptor("e1", "User", "Users", "dbo",
            new[] { new PropertyDescriptor("Name", "string", "name", "varchar", true, 200, false, false, null) });
        var columns = new[] { new ColumnDescriptor("name", "varchar", 100, null, null, true, null) };

        var violations = _detector.Detect(entity, columns);

        violations.Should().ContainSingle(v => v.RuleId == "PG003");
    }

    [Fact]
    public void Detect_WithinLength_NoViolation()
    {
        var entity = new EntityDescriptor("e1", "User", "Users", "dbo",
            new[] { new PropertyDescriptor("Name", "string", "name", "varchar", true, 50, false, false, null) });
        var columns = new[] { new ColumnDescriptor("name", "varchar", 100, null, null, true, null) };

        var violations = _detector.Detect(entity, columns);

        violations.Should().BeEmpty();
    }

    [Fact]
    public void Detect_NoMaxLength_Skips()
    {
        var entity = new EntityDescriptor("e1", "User", "Users", "dbo",
            new[] { new PropertyDescriptor("Name", "string", "name", "varchar", true, null, false, false, null) });
        var columns = new[] { new ColumnDescriptor("name", "varchar", 100, null, null, true, null) };

        var violations = _detector.Detect(entity, columns);

        violations.Should().BeEmpty();
    }
}
