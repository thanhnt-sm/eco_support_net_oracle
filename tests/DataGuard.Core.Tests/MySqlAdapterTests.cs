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

    [Fact]
    public void CheckNonMySqlSyntax_MySqlContext_MultipleViolations_NvlAndTop()
    {
        var violations = _checker.CheckNonMySqlSyntaxInMySqlContext(
            "SELECT TOP 10 NVL(col, 0) FROM t", isMySqlContext: true);

        violations.Count.Should().BeGreaterThanOrEqualTo(2);
        violations.Should().Contain(v => v.Message.Contains("NVL"));
        violations.Should().Contain(v => v.Message.Contains("TOP"));
    }

    [Fact]
    public void CheckMySqlSyntax_NonMySqlContext_MultipleViolations_BacktickAndOnDuplicateKey()
    {
        var violations = _checker.CheckMySqlSyntaxInNonMySqlContext(
            "INSERT INTO `t` (x) VALUES (1) ON DUPLICATE KEY UPDATE x=1", isMySqlContext: false);

        violations.Count.Should().BeGreaterThanOrEqualTo(2);
        violations.Should().Contain(v => v.Message.Contains("backtick"));
        violations.Should().Contain(v => v.Message.Contains("ON DUPLICATE KEY"));
    }

    [Theory]
    [InlineData("ALTER TABLE t AUTO_INCREMENT=100", "AUTO_INCREMENT")]
    [InlineData("SELECT IFNULL(col, 0) FROM t", "IFNULL")]
    [InlineData("SELECT GROUP_CONCAT(name) FROM t", "GROUP_CONCAT")]
    public void CheckMySqlSyntax_NonMySqlContext_DetectsMySqlKeywords(string sql, string expectedSyntax)
    {
        var violations = _checker.CheckMySqlSyntaxInNonMySqlContext(sql, isMySqlContext: false);

        violations.Should().Contain(v =>
            v.RuleId == "MY001" &&
            v.Properties != null &&
            v.Properties.ContainsKey("syntax") &&
            v.Properties["syntax"]!.ToString()!.Contains(expectedSyntax, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CheckMySqlSyntax_NonMySqlContext_DetectsLimitOffsetCount()
    {
        var violations = _checker.CheckMySqlSyntaxInNonMySqlContext(
            "SELECT * FROM t LIMIT 10, 20", isMySqlContext: false);

        violations.Should().Contain(v =>
            v.RuleId == "MY001" &&
            v.Message.Contains("LIMIT offset, count"));
    }

    [Theory]
    [InlineData("SELECT GETDATE()", "GETDATE")]
    [InlineData("SELECT ISNULL(col, 0) FROM t", "ISNULL")]
    [InlineData("SELECT NEWID()", "NEWID")]
    [InlineData("SELECT IDENTITY(int, 1, 1) AS Id FROM t", "IDENTITY")]
    [InlineData("SELECT SCOPE_IDENTITY()", "SCOPE_IDENTITY")]
    [InlineData("SELECT TRY_CAST(col AS int) FROM t", "TRY_CAST")]
    [InlineData("SELECT TRY_CONVERT(int, col) FROM t", "TRY_CONVERT")]
    public void CheckNonMySqlSyntax_MySqlContext_DetectsSqlServerKeywords(string sql, string expectedSyntax)
    {
        var violations = _checker.CheckNonMySqlSyntaxInMySqlContext(sql, isMySqlContext: true);

        violations.Should().Contain(v =>
            v.RuleId == "MY002" &&
            v.Properties != null &&
            v.Properties.ContainsKey("syntax") &&
            v.Properties["syntax"]!.ToString()!.Contains(expectedSyntax, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CheckNonMySqlSyntax_MySqlContext_DetectsConnectBy()
    {
        var violations = _checker.CheckNonMySqlSyntaxInMySqlContext(
            "SELECT * FROM t CONNECT BY PRIOR id = parent_id START WITH parent_id IS NULL",
            isMySqlContext: true);

        violations.Should().Contain(v => v.RuleId == "MY002" && v.Message.Contains("CONNECT BY"));
    }

    [Fact]
    public void CheckNonMySqlSyntax_MySqlContext_DetectsOracleOuterJoin()
    {
        var violations = _checker.CheckNonMySqlSyntaxInMySqlContext(
            "SELECT * FROM a, b WHERE a.id = b.id(+)", isMySqlContext: true);

        violations.Should().Contain(v => v.RuleId == "MY002" && v.Message.Contains("(+)"));
    }

    [Fact]
    public void CheckNonMySqlSyntax_MySqlContext_DetectsRowNum()
    {
        var violations = _checker.CheckNonMySqlSyntaxInMySqlContext(
            "SELECT * FROM t WHERE ROWNUM <= 10", isMySqlContext: true);

        violations.Should().Contain(v => v.RuleId == "MY002" && v.Message.Contains("ROWNUM"));
    }

    [Fact]
    public void CheckNonMySqlSyntax_MySqlContext_CleanMySqlSql_NoViolation()
    {
        var violations = _checker.CheckNonMySqlSyntaxInMySqlContext(
            "SELECT id, name FROM users WHERE active = 1 ORDER BY name LIMIT 10",
            isMySqlContext: true);

        violations.Should().BeEmpty();
    }

    [Fact]
    public void CheckMySqlSyntax_NonMySqlContext_CleanStandardSql_NoViolation()
    {
        var violations = _checker.CheckMySqlSyntaxInNonMySqlContext(
            "SELECT id, name FROM users WHERE active = 1",
            isMySqlContext: false);

        violations.Should().BeEmpty();
    }

    [Fact]
    public void CheckNonMySqlSyntax_MySqlContext_NullSql_NoViolation()
    {
        var violations = _checker.CheckNonMySqlSyntaxInMySqlContext(null!, isMySqlContext: true);
        violations.Should().BeEmpty();
    }

    [Fact]
    public void CheckMySqlSyntax_NonMySqlContext_NullSql_NoViolation()
    {
        var violations = _checker.CheckMySqlSyntaxInNonMySqlContext(null!, isMySqlContext: false);
        violations.Should().BeEmpty();
    }

    [Fact]
    public void CheckNonMySqlSyntax_NonMySqlContext_ReturnsEmpty()
    {
        // When isMySqlContext=false, CheckNonMySqlSyntaxInMySqlContext should return empty
        var violations = _checker.CheckNonMySqlSyntaxInMySqlContext(
            "SELECT GETDATE(), NVL(col,0), TOP 5 * FROM t", isMySqlContext: false);

        violations.Should().BeEmpty();
    }

    [Fact]
    public void CheckMySqlSyntax_MySqlContext_ReturnsEmpty()
    {
        // When isMySqlContext=true, CheckMySqlSyntaxInNonMySqlContext should return empty
        var violations = _checker.CheckMySqlSyntaxInNonMySqlContext(
            "SELECT `Id`, IFNULL(col,0) FROM t LIMIT 10, 20", isMySqlContext: true);

        violations.Should().BeEmpty();
    }

    [Fact]
    public void CheckMySqlLengthLimits_VarcharExceeds65535Bytes_ReturnsViolation()
    {
        var entity = new EntityDescriptor("e1", "User", "User", "Users",
            new[] { new PropertyDescriptor("Name", "string", "name", "varchar", true, 20000, false, false, null) });
        var columns = new[] { new ColumnDescriptor("name", "VARCHAR", 20000, null, null, true, "utf8mb4") };

        var violations = _checker.CheckMySqlLengthLimits(entity, columns);

        // 20000 chars × 4 bytes/char = 80000 > 65535
        violations.Should().ContainSingle(v => v.RuleId == "MY003");
    }

    [Fact]
    public void CheckMySqlLengthLimits_MultipleColumnsContributingToRowSize()
    {
        var entity = new EntityDescriptor("e1", "User", "User", "Users",
            new[]
            {
                new PropertyDescriptor("Col1", "string", "col1", "varchar", true, 10000, false, false, null),
                new PropertyDescriptor("Col2", "string", "col2", "varchar", true, 10000, false, false, null),
            });
        var columns = new[]
        {
            new ColumnDescriptor("col1", "VARCHAR", 10000, null, null, true, "utf8mb4"),
            new ColumnDescriptor("col2", "VARCHAR", 10000, null, null, true, "utf8mb4"),
        };

        var violations = _checker.CheckMySqlLengthLimits(entity, columns);

        // Each column: 10000 × 4 = 40000 < 65535 — no per-column violation
        violations.Should().BeEmpty();
    }

    [Fact]
    public void CheckMySqlLengthLimits_CharVsVarchar_ByteCalculation()
    {
        var entity = new EntityDescriptor("e1", "User", "User", "Users",
            new[] { new PropertyDescriptor("Code", "string", "code", "char", true, 20000, false, false, null) });
        var columns = new[] { new ColumnDescriptor("code", "CHAR", 20000, null, null, true, "utf8mb4") };

        var violations = _checker.CheckMySqlLengthLimits(entity, columns);

        // 20000 chars × 4 bytes = 80000 > 65535
        violations.Should().ContainSingle(v => v.RuleId == "MY003");
    }

    [Fact]
    public void CheckMySqlLengthLimits_VarcharWithinLimit_NoViolation()
    {
        var entity = new EntityDescriptor("e1", "User", "User", "Users",
            new[] { new PropertyDescriptor("Name", "string", "name", "varchar", true, 100, false, false, null) });
        var columns = new[] { new ColumnDescriptor("name", "VARCHAR", 100, null, null, true, "utf8mb4") };

        var violations = _checker.CheckMySqlLengthLimits(entity, columns);

        // 100 × 4 = 400 < 65535
        violations.Should().BeEmpty();
    }

    [Fact]
    public void CheckMySqlLengthLimits_TextColumn_EntityExceedsMax_ReturnsViolation()
    {
        var entity = new EntityDescriptor("e1", "User", "User", "Users",
            new[] { new PropertyDescriptor("Body", "string", "body", "text", true, 100_000, false, false, null) });
        var columns = new[] { new ColumnDescriptor("body", "TEXT", null, null, null, true, null) };

        var violations = _checker.CheckMySqlLengthLimits(entity, columns);

        // TEXT max = 65535, entity MaxLength = 100000 > 65535
        violations.Should().Contain(v => v.RuleId == "MY003" && v.Message.Contains("TEXT"));
    }

    [Fact]
    public void CheckMySqlLengthLimits_LongTextColumn_EntityWithinMax_NoViolation()
    {
        // LONGTEXT max = 4294967295, int.MaxValue = 2147483647 < max → no violation
        var entity = new EntityDescriptor("e1", "User", "User", "Users",
            new[] { new PropertyDescriptor("Content", "string", "content", "longtext", true, 2147483647, false, false, null) });
        var columns = new[] { new ColumnDescriptor("content", "LONGTEXT", null, null, null, true, null) };

        var violations = _checker.CheckMySqlLengthLimits(entity, columns);

        violations.Should().BeEmpty();
    }

    [Fact]
    public void CheckMySqlLengthLimits_MediumTextColumn_EntityWithinMax_NoViolation()
    {
        var entity = new EntityDescriptor("e1", "User", "User", "Users",
            new[] { new PropertyDescriptor("Body", "string", "body", "mediumtext", true, 1_000_000, false, false, null) });
        var columns = new[] { new ColumnDescriptor("body", "MEDIUMTEXT", null, null, null, true, null) };

        var violations = _checker.CheckMySqlLengthLimits(entity, columns);

        // MEDIUMTEXT max = 16777215, entity = 1000000 < max
        violations.Should().BeEmpty();
    }

    [Fact]
    public void CheckMySqlLengthLimits_EntityNoMaxLength_Skips()
    {
        var entity = new EntityDescriptor("e1", "User", "User", "Users",
            new[] { new PropertyDescriptor("Name", "string", "name", "varchar", true, null, false, false, null) });
        var columns = new[] { new ColumnDescriptor("name", "VARCHAR", 20000, null, null, true, "utf8mb4") };

        var violations = _checker.CheckMySqlLengthLimits(entity, columns);

        violations.Should().BeEmpty();
    }

    [Fact]
    public void CheckMySqlLengthLimits_ColumnNotFound_Skips()
    {
        var entity = new EntityDescriptor("e1", "User", "User", "Users",
            new[] { new PropertyDescriptor("Name", "string", "name", "varchar", true, 20000, false, false, null) });
        var columns = new[] { new ColumnDescriptor("other_col", "VARCHAR", 20000, null, null, true, "utf8mb4") };

        var violations = _checker.CheckMySqlLengthLimits(entity, columns);

        violations.Should().BeEmpty();
    }

    [Fact]
    public void CheckMySqlLengthLimits_Latin1Charset_LowerByteCount()
    {
        var entity = new EntityDescriptor("e1", "User", "User", "Users",
            new[] { new PropertyDescriptor("Name", "string", "name", "varchar", true, 20000, false, false, null) });
        var columns = new[] { new ColumnDescriptor("name", "VARCHAR", 20000, null, null, true, "latin1") };

        var violations = _checker.CheckMySqlLengthLimits(entity, columns);

        // latin1 = 1 byte/char, 20000 × 1 = 20000 < 65535
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
            v.RuleId == "MY004" &&
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

        violations.Should().ContainSingle(v => v.RuleId == "MY004");
    }

    [Fact]
    public void Detect_MaxLengthExactlyEqualsColumnLength_NoViolation()
    {
        var entity = new EntityDescriptor("e1", "User", "Users", "dbo",
            new[] { new PropertyDescriptor("Name", "string", "name", "varchar", true, 100, false, false, null) });
        var columns = new[] { new ColumnDescriptor("name", "VARCHAR", 100, null, null, true, null) };

        var violations = _detector.Detect(entity, columns);

        violations.Should().BeEmpty();
    }

    [Fact]
    public void Detect_MultipleProperties_SomeMatchSomeNot()
    {
        var entity = new EntityDescriptor("e1", "User", "Users", "dbo",
            new[]
            {
                new PropertyDescriptor("Name", "string", "name", "varchar", true, 200, false, false, null),
                new PropertyDescriptor("Email", "string", "email", "varchar", true, 50, false, false, null),
                new PropertyDescriptor("Bio", "string", "bio", "text", true, null, false, false, null),
            });
        var columns = new[]
        {
            new ColumnDescriptor("name", "VARCHAR", 100, null, null, true, null),
            new ColumnDescriptor("email", "VARCHAR", 200, null, null, true, null),
            new ColumnDescriptor("bio", "TEXT", null, null, null, true, null),
        };

        var violations = _detector.Detect(entity, columns).ToList();

        // Name: 200 > 100 → MY004 violation
        // Email: 50 < 200 → no violation
        // Bio: no MaxLength on entity, TEXT type → MY007 (inferred VARCHAR(255) risk)
        violations.Should().Contain(v => v.RuleId == "MY004" && v.Message.Contains("Name"));
        violations.Should().NotContain(v => v.Message.Contains("Email") && v.RuleId == "MY004");
    }

    [Fact]
    public void Detect_Utf8mb4ByteOverflow_ColumnExceeds65535_ReturnsViolation()
    {
        // VARCHAR(20000) with utf8mb4 = 80000 bytes > 65535
        var entity = new EntityDescriptor("e1", "User", "Users", "dbo",
            new[] { new PropertyDescriptor("Name", "string", "name", "varchar", true, 20000, false, false, null) });
        var columns = new[] { new ColumnDescriptor("name", "VARCHAR", 20000, null, null, true, "utf8mb4") };

        var violations = _detector.Detect(entity, columns);

        violations.Should().Contain(v => v.RuleId == "MY005");
    }

    [Fact]
    public void Detect_Utf8mb4ByteOverflow_EntityMaxBytesExceedsColumnCapacity()
    {
        // Entity MaxLength=20000, column MaxLength=10000, utf8mb4
        // entity bytes = 20000 × 4 = 80000 > 65535 AND > column bytes (10000 × 4 = 40000)
        var entity = new EntityDescriptor("e1", "User", "Users", "dbo",
            new[] { new PropertyDescriptor("Name", "string", "name", "varchar", true, 20000, false, false, null) });
        var columns = new[] { new ColumnDescriptor("name", "VARCHAR", 10000, null, null, true, "utf8mb4") };

        var violations = _detector.Detect(entity, columns).ToList();

        // Should have MY004 (direct length mismatch) and MY005 (byte overflow)
        violations.Should().Contain(v => v.RuleId == "MY004");
        violations.Should().Contain(v => v.RuleId == "MY005");
    }

    [Fact]
    public void Detect_TextColumn_EntityExceedsMax_ReturnsMy006()
    {
        var entity = new EntityDescriptor("e1", "User", "Users", "dbo",
            new[] { new PropertyDescriptor("Body", "string", "body", "text", true, 100_000, false, false, null) });
        var columns = new[] { new ColumnDescriptor("body", "TEXT", null, null, null, true, null) };

        var violations = _detector.Detect(entity, columns);

        // TEXT max = 65535, entity = 100000 > 65535 → MY006
        violations.Should().Contain(v => v.RuleId == "MY006");
    }

    [Fact]
    public void Detect_LongTextColumn_EntityWithinByteMax_NoMy006()
    {
        // LONGTEXT holds 4294967295 BYTES; 1e9 chars × 3 bytes (utf8mb4 per UTF-16 unit) = 3e9 fits.
        var entity = new EntityDescriptor("e1", "User", "Users", "dbo",
            new[] { new PropertyDescriptor("Content", "string", "content", "longtext", true, 1_000_000_000, false, false, null) });
        var columns = new[] { new ColumnDescriptor("content", "LONGTEXT", null, null, null, true, null) };

        var violations = _detector.Detect(entity, columns);

        violations.Should().NotContain(v => v.RuleId == "MY006");
    }

    [Fact]
    public void Detect_LongTextColumn_IntMaxCharsExceedByteMax_ReturnsMy006()
    {
        // int.MaxValue chars × 3 bytes = 6.4e9 > 4294967295 bytes: the limit is bytes, not characters.
        var entity = new EntityDescriptor("e1", "User", "Users", "dbo",
            new[] { new PropertyDescriptor("Content", "string", "content", "longtext", true, int.MaxValue, false, false, null) });
        var columns = new[] { new ColumnDescriptor("content", "LONGTEXT", null, null, null, true, null) };

        _detector.Detect(entity, columns).Should().Contain(v => v.RuleId == "MY006");
    }

    [Fact]
    public void Detect_MediumTextColumn_EntityWithinMax_NoViolation()
    {
        var entity = new EntityDescriptor("e1", "User", "Users", "dbo",
            new[] { new PropertyDescriptor("Body", "string", "body", "mediumtext", true, 1_000_000, false, false, null) });
        var columns = new[] { new ColumnDescriptor("body", "MEDIUMTEXT", null, null, null, true, null) };

        var violations = _detector.Detect(entity, columns);

        // MEDIUMTEXT max = 16777215, entity = 1000000 < max
        violations.Should().NotContain(v => v.RuleId == "MY006");
    }

    [Fact]
    public void Detect_TextColumn_NoEntityMaxLength_NoMy007()
    {
        // Pomelo maps an unbounded string to longtext: a TEXT column behind it is not a truncation risk.
        var entity = new EntityDescriptor("e1", "User", "Users", "dbo",
            new[] { new PropertyDescriptor("Body", "string", "body", "text", true, null, false, false, null) });
        var columns = new[] { new ColumnDescriptor("body", "TEXT", null, null, null, true, null) };

        var violations = _detector.Detect(entity, columns);

        violations.Should().NotContain(v => v.RuleId == "MY007");
    }

    [Fact]
    public void Detect_VarcharColumn_NoEntityMaxLength_ReturnsMy007()
    {
        // Unbounded string (longtext in the model) over VARCHAR(255): values over 255 chars fail at runtime.
        var entity = new EntityDescriptor("e1", "User", "Users", "dbo",
            new[] { new PropertyDescriptor("Name", "string", "name", "varchar", true, null, false, false, null) });
        var columns = new[] { new ColumnDescriptor("name", "VARCHAR", 255, null, null, true, null) };

        var violations = _detector.Detect(entity, columns);

        violations.Should().ContainSingle(v => v.RuleId == "MY007" && v.Message.Contains("longtext") && v.Message.Contains("VARCHAR(255)"));
    }

    [Fact]
    public void Detect_ColumnNullMaxLength_SkipsDirectLengthCheck()
    {
        // Column has null MaxLength (e.g. TEXT type), entity has MaxLength
        // Direct length check (MY004) should be skipped since column.MaxLength is null
        var entity = new EntityDescriptor("e1", "User", "Users", "dbo",
            new[] { new PropertyDescriptor("Name", "string", "name", "varchar", true, 200, false, false, null) });
        var columns = new[] { new ColumnDescriptor("name", "VARCHAR", null, null, null, true, null) };

        var violations = _detector.Detect(entity, columns);

        violations.Should().NotContain(v => v.RuleId == "MY004");
    }
}

/// <summary>
/// MySQL byte semantics, row size, charset placement and catalog keys.
/// </summary>
public class MySqlLengthSemanticsTests
{
    private readonly MySqlLengthMismatchDetector _detector = new();

    [Theory]
    [InlineData(21845, false)] // 21845 × 3 = 65535 bytes: fits TEXT exactly
    [InlineData(21846, true)] // 65538 > 65535
    public void TextLimitIsBytes_Utf8mb4UsesThreeBytesPerUtf16Unit(int maxLength, bool expectMy006)
    {
        var entity = Entity(new PropertyDescriptor("Body", "string", "body", null, true, maxLength, false, false, null));
        var columns = new[] { new ColumnDescriptor("body", "TEXT", 65535, null, null, true, null, Charset: "utf8mb4") };

        _detector.Detect(entity, columns).Any(v => v.RuleId == "MY006").Should().Be(expectMy006);
        _detector.Detect(entity, columns).Should().NotContain(v => v.RuleId == "MY004");
    }

    [Fact]
    public void TextLimit_Latin1CountsOneBytePerCharacter()
    {
        var entity = Entity(new PropertyDescriptor("Body", "string", "body", null, true, 65535, false, false, null));
        var columns = new[] { new ColumnDescriptor("body", "TEXT", 65535, null, null, true, null, Charset: "latin1") };

        _detector.Detect(entity, columns).Should().NotContain(v => v.RuleId == "MY006");
    }

    [Theory]
    [InlineData("utf8mb3", 21845, false)] // 21845 x 3 = 65535: fits (utf8mb3 is 3 bytes, not 4)
    [InlineData("utf8mb4", 21845, true)] // 87380 > 65535
    [InlineData("latin1", 60000, false)]
    public void My003_UsesColumnCharsetAndMaxCharacterWidth(string charset, int length, bool expectMy003)
    {
        var entity = Entity(new PropertyDescriptor("Name", "string", "name", null, true, length, false, false, null));
        var columns = new[] { new ColumnDescriptor("name", "VARCHAR", length, null, null, true, null, Charset: charset) };

        new MySqlDialectChecker().CheckMySqlLengthLimits(entity, columns)
            .Any(v => v.RuleId == "MY003" && v.Message.Contains("65535-byte")).Should().Be(expectMy003);
    }

    [Theory]
    [InlineData("utf8mb4", 3)]
    [InlineData("utf8mb3", 3)]
    [InlineData("utf16", 2)]
    [InlineData("latin1", 1)]
    [InlineData("nonsense", 3)]
    public void BytesPerUtf16Unit_FollowsCharset(string charset, int expected)
    {
        MySqlLengthMismatchDetector.BytesPerUtf16Unit(charset).Should().Be(expected);
    }

    [Fact]
    public void My005_RowSizeSumsAllVarcharColumnsWithCharset()
    {
        // Three VARCHAR(6000) utf8mb4 columns: 3 × (24000 + 2) = 72006 > 65535 even though each fits alone.
        var entity = Entity(
            new PropertyDescriptor("A", "string", "a", null, true, 6000, false, false, null),
            new PropertyDescriptor("B", "string", "b", null, true, 6000, false, false, null));
        var columns = new[]
        {
            new ColumnDescriptor("a", "VARCHAR", 6000, null, null, true, null, Charset: "utf8mb4"),
            new ColumnDescriptor("b", "VARCHAR", 6000, null, null, true, null, Charset: "utf8mb4"),
            new ColumnDescriptor("c", "VARCHAR", 6000, null, null, true, null, Charset: "utf8mb4"),
        };

        var violation = _detector.Detect(entity, columns).Should().ContainSingle(v => v.RuleId == "MY005").Subject;
        Convert.ToInt64(violation.Properties!["rowBytes"]).Should().Be(72006);
    }

    [Fact]
    public void My005_SameWidthsInLatin1FitTheRow()
    {
        var entity = Entity(new PropertyDescriptor("A", "string", "a", null, true, 6000, false, false, null));
        var columns = new[]
        {
            new ColumnDescriptor("a", "VARCHAR", 6000, null, null, true, null, Charset: "latin1"),
            new ColumnDescriptor("b", "VARCHAR", 6000, null, null, true, null, Charset: "latin1"),
        };

        _detector.Detect(entity, columns).Should().NotContain(v => v.RuleId == "MY005");
    }

    [Fact]
    public void LegacySnapshotCharsetInCharUsedIsStillHonored()
    {
        var entity = Entity(new PropertyDescriptor("Body", "string", "body", null, true, 65535, false, false, null));
        var columns = new[] { new ColumnDescriptor("body", "TEXT", 65535, null, null, true, "latin1") };

        _detector.Detect(entity, columns).Should().NotContain(v => v.RuleId == "MY006");
    }

    [Theory]
    [InlineData(0, "Shop.Orders")]
    [InlineData(1, "shop.orders")]
    [InlineData(2, "shop.orders")]
    public void TableKey_HonorsLowerCaseTableNames(int lowerCaseTableNames, string expected)
    {
        MySqlStoredProcedureParser.TableKey("Shop", "Orders", lowerCaseTableNames).Should().Be(expected);
    }

    [Fact]
    public async Task Rule_ExactCaseTableWinsAndSchemaIsRespected()
    {
        var entity = new EntityDescriptor("e1", "Order", "Order", "shop.Orders",
            new[] { new PropertyDescriptor("Code", "string", "code", null, true, 50, false, false, null) });
        var schema = new DatabaseSchemaDescriptor(
            "mysql:schema:shop",
            new[]
            {
                new DatabaseTableDescriptor("orders", new[] { new ColumnDescriptor("code", "VARCHAR", 100, null, null, true, null) }, "shop"),
                new DatabaseTableDescriptor("Orders", new[] { new ColumnDescriptor("code", "VARCHAR", 10, null, null, true, null) }, "shop"),
            },
            "CHAR");

        var violations = await new MySqlLengthExceedsColumnRule().ValidateAsync(entity, new ContractDescriptor[] { entity, schema });

        violations.Should().ContainSingle(v => v.RuleId == "MY004" && (int)v.Properties!["columnMaxLength"]! == 10);
    }

    private static EntityDescriptor Entity(params PropertyDescriptor[] properties)
        => new("e1", "Doc", "Doc", "docs", properties);
}
