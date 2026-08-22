using DataGuard.Core.Abstractions;
using DataGuard.Core.Models;
using DataGuard.MySql.Adapter;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DataGuard.Core.Tests;

/// <summary>
/// Unit tests for MySql adapter pure logic (no DB required).
/// </summary>
public class MySqlDialectCheckerTests
{
    private readonly MySqlDialectChecker _checker = new();

    [Fact]
    public void CheckMySqlSyntax_NonMySqlContext_DetectsBacktick()
    {
        var violations = _checker.CheckMySqlSyntaxInNonMySqlContext("SELECT `Id` FROM Users", isMySqlContext: false);
        violations.Should().ContainSingle(v => v.RuleId == "MY001");
    }

    [Fact]
    public void CheckMySqlSyntax_NonMySqlContext_DetectsOnDuplicateKey()
    {
        var violations = _checker.CheckMySqlSyntaxInNonMySqlContext("INSERT INTO t ON DUPLICATE KEY UPDATE x=1", isMySqlContext: false);
        violations.Should().ContainSingle(v => v.RuleId == "MY001");
    }

    [Fact]
    public void CheckMySqlSyntax_MySqlContext_NoViolation()
    {
        var violations = _checker.CheckMySqlSyntaxInNonMySqlContext("SELECT `Id` FROM Users", isMySqlContext: true);
        violations.Should().BeEmpty();
    }

    [Fact]
    public void CheckNonMySqlSyntax_MySqlContext_DetectsNvl()
    {
        var violations = _checker.CheckNonMySqlSyntaxInMySqlContext("SELECT NVL(col, 0) FROM t", isMySqlContext: true);
        violations.Should().ContainSingle(v => v.RuleId == "MY002");
    }

    [Fact]
    public void CheckNonMySqlSyntax_MySqlContext_DetectsTop()
    {
        var violations = _checker.CheckNonMySqlSyntaxInMySqlContext("SELECT TOP 10 * FROM t", isMySqlContext: true);
        violations.Should().ContainSingle(v => v.RuleId == "MY002");
    }

    [Fact]
    public void CheckNonMySqlSyntax_NonMySqlContext_NoViolation()
    {
        var violations = _checker.CheckNonMySqlSyntaxInMySqlContext("SELECT NVL(col, 0) FROM t", isMySqlContext: false);
        violations.Should().BeEmpty();
    }

    [Fact]
    public void CheckMySqlSyntax_EmptySql_NoViolation()
    {
        var violations = _checker.CheckMySqlSyntaxInNonMySqlContext("", isMySqlContext: false);
        violations.Should().BeEmpty();
    }
}

public class MySqlLengthMismatchDetectorTests
{
    private readonly MySqlLengthMismatchDetector _detector = new();

    [Fact]
    public void Detect_ExceedsLength_ReturnsViolation()
    {
        var entity = new EntityDescriptor("e1", "User", "Users", "dbo",
            new[] { new PropertyDescriptor("Name", "string", "name", "nvarchar", true, 200, false, false, null) });
        var columns = new[] { new ColumnDescriptor("name", "nvarchar", 100, null, null, true, null) };

        var violations = _detector.Detect(entity, columns);

        violations.Should().ContainSingle(v =>
            v.RuleId == "MY003" &&
            v.Properties != null &&
            v.Properties.ContainsKey("entityMaxLength"));
    }

    [Fact]
    public void Detect_WithinLength_NoViolation()
    {
        var entity = new EntityDescriptor("e1", "User", "Users", "dbo",
            new[] { new PropertyDescriptor("Name", "string", "name", "nvarchar", true, 50, false, false, null) });
        var columns = new[] { new ColumnDescriptor("name", "nvarchar", 100, null, null, true, null) };

        var violations = _detector.Detect(entity, columns);

        violations.Should().BeEmpty();
    }

    [Fact]
    public void Detect_NoMaxLengthProperty_Skips()
    {
        var entity = new EntityDescriptor("e1", "User", "Users", "dbo",
            new[] { new PropertyDescriptor("Name", "string", "name", "nvarchar", true, null, false, false, null) });
        var columns = new[] { new ColumnDescriptor("name", "nvarchar", 100, null, null, true, null) };

        var violations = _detector.Detect(entity, columns);

        violations.Should().BeEmpty();
    }

    [Fact]
    public void Detect_ColumnNotFound_Skips()
    {
        var entity = new EntityDescriptor("e1", "User", "Users", "dbo",
            new[] { new PropertyDescriptor("Name", "string", "name", "nvarchar", true, 200, false, false, null) });
        var columns = new[] { new ColumnDescriptor("other", "nvarchar", 100, null, null, true, null) };

        var violations = _detector.Detect(entity, columns);

        violations.Should().BeEmpty();
    }

    [Fact]
    public void Detect_CaseInsensitiveMatch()
    {
        var entity = new EntityDescriptor("e1", "User", "Users", "dbo",
            new[] { new PropertyDescriptor("Name", "string", "NAME", "nvarchar", true, 200, false, false, null) });
        var columns = new[] { new ColumnDescriptor("name", "nvarchar", 100, null, null, true, null) };

        var violations = _detector.Detect(entity, columns);

        violations.Should().ContainSingle(v => v.RuleId == "MY003");
    }
}
