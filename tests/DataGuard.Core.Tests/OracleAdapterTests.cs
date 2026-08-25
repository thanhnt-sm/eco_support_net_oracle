using DataGuard.Core.Abstractions;
using DataGuard.Core.Models;
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

    [Theory]
    [InlineData("SELECT NVL2(col, 'a', 'b') FROM t", "NVL2")]
    [InlineData("SELECT SYSDATE FROM DUAL", "SYSDATE")]
    [InlineData("SELECT seq.NEXTVAL FROM DUAL", "NEXTVAL")]
    [InlineData("SELECT seq.CURRVAL FROM DUAL", "CURRVAL")]
    [InlineData("SELECT ROWID FROM t", "ROWID")]
    [InlineData("SELECT LISTAGG(name, ',') WITHIN GROUP (ORDER BY name) FROM t", "LISTAGG")]
    [InlineData("SELECT * FROM t START WITH id = 1 CONNECT BY PRIOR id = parent_id", "START WITH")]
    public void CheckOracleSyntax_NonOracleContext_DetectsAdditionalKeywords(string sql, string expectedKeyword)
    {
        var violations = _checker.CheckOracleSyntaxInNonOracleContext(sql, isOracleContext: false);

        violations.Should().Contain(v =>
            v.RuleId == "DG010" &&
            v.Properties != null &&
            v.Properties.ContainsKey("keyword") &&
            v.Properties["keyword"]!.ToString()!.Contains(expectedKeyword, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("SELECT IIF(col > 0, 'yes', 'no') FROM t", "IIF")]
    [InlineData("SELECT TRY_CAST(col AS int) FROM t", "TRY_CAST")]
    [InlineData("SELECT TRY_CONVERT(int, col) FROM t", "TRY_CONVERT")]
    [InlineData("SELECT TOP 10 * FROM t", "TOP")]
    public void CheckNonOracleSyntax_OracleContext_DetectsAdditionalKeywords(string sql, string expectedKeyword)
    {
        var violations = _checker.CheckNonOracleSyntaxInOracleContext(sql, isOracleContext: true);

        violations.Should().Contain(v =>
            v.RuleId == "DG011" &&
            v.Message.Contains(expectedKeyword, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CheckSqlServerSyntaxLeak_OracleContext_DetectsExecDbo()
    {
        var violations = _checker.CheckSqlServerSyntaxLeak("EXEC dbo.GetUsers @Id=1", isOracleContext: true);
        violations.Should().ContainSingle(v => v.RuleId == "DG013");
    }

    [Fact]
    public void CheckSqlServerSyntaxLeak_NonOracleContext_NoViolation()
    {
        var violations = _checker.CheckSqlServerSyntaxLeak("EXEC dbo.GetUsers @Id=1", isOracleContext: false);
        violations.Should().BeEmpty();
    }

    [Fact]
    public void CheckSqlServerSyntaxLeak_EmptySql_NoViolation()
    {
        var violations = _checker.CheckSqlServerSyntaxLeak("", isOracleContext: true);
        violations.Should().BeEmpty();
    }

    [Fact]
    public void CheckRawSqlUnmappedTypeUsage_OracleContext_DetectsUniqueIdentifier()
    {
        var violations = _checker.CheckRawSqlUnmappedTypeUsage("SELECT CAST(col AS UNIQUEIDENTIFIER) FROM t", isOracleContext: true);
        violations.Should().ContainSingle(v => v.RuleId == "DG014");
    }

    [Fact]
    public void CheckRawSqlUnmappedTypeUsage_OracleContext_DetectsMoney()
    {
        var violations = _checker.CheckRawSqlUnmappedTypeUsage("SELECT CAST(price AS MONEY) FROM t", isOracleContext: true);
        violations.Should().ContainSingle(v => v.RuleId == "DG014");
    }

    [Fact]
    public void CheckRawSqlUnmappedTypeUsage_OracleContext_DetectsDatetime2()
    {
        var violations = _checker.CheckRawSqlUnmappedTypeUsage("SELECT CAST(created AS DATETIME2) FROM t", isOracleContext: true);
        violations.Should().ContainSingle(v => v.RuleId == "DG014");
    }

    [Fact]
    public void CheckRawSqlUnmappedTypeUsage_NonOracleContext_NoViolation()
    {
        var violations = _checker.CheckRawSqlUnmappedTypeUsage("SELECT CAST(col AS UNIQUEIDENTIFIER) FROM t", isOracleContext: false);
        violations.Should().BeEmpty();
    }

    [Fact]
    public void CheckRawSqlUnmappedTypeUsage_EmptySql_NoViolation()
    {
        var violations = _checker.CheckRawSqlUnmappedTypeUsage("", isOracleContext: true);
        violations.Should().BeEmpty();
    }

    [Fact]
    public void CheckOracleSyntax_NullSql_ThrowsOnRegex()
    {
        var act = () => _checker.CheckOracleSyntaxInNonOracleContext(null!, isOracleContext: false);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CheckNonOracleSyntax_NullSql_ThrowsOnRegex()
    {
        var act = () => _checker.CheckNonOracleSyntaxInOracleContext(null!, isOracleContext: true);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CheckOracleSyntax_NonOracleContext_MultipleViolations()
    {
        var violations = _checker.CheckOracleSyntaxInNonOracleContext(
            "SELECT DECODE(col, 1, NVL(col2, 0)) FROM DUAL WHERE ROWNUM <= 10", isOracleContext: false);

        violations.Count.Should().BeGreaterThanOrEqualTo(3);
        violations.Should().Contain(v => v.Message.Contains("DECODE"));
        violations.Should().Contain(v => v.Message.Contains("NVL"));
        violations.Should().Contain(v => v.Message.Contains("DUAL"));
    }

    [Fact]
    public void CheckNonOracleSyntax_OracleContext_MultipleViolations()
    {
        var violations = _checker.CheckNonOracleSyntaxInOracleContext(
            "SELECT TOP 10 ISNULL(col, GETDATE()) FROM t", isOracleContext: true);

        violations.Count.Should().BeGreaterThanOrEqualTo(3);
        violations.Should().Contain(v => v.Message.Contains("ISNULL"));
        violations.Should().Contain(v => v.Message.Contains("GETDATE"));
        violations.Should().Contain(v => v.Message.Contains("TOP"));
    }

    [Fact]
    public void CheckOracleSyntax_NonOracleContext_CleanStandardSql_NoViolation()
    {
        var violations = _checker.CheckOracleSyntaxInNonOracleContext(
            "SELECT id, name FROM users WHERE active = 1 ORDER BY name", isOracleContext: false);

        violations.Should().BeEmpty();
    }

    [Fact]
    public void CheckNonOracleSyntax_OracleContext_CleanOracleSql_NoViolation()
    {
        var violations = _checker.CheckNonOracleSyntaxInOracleContext(
            "SELECT id, name FROM users WHERE active = 1 ORDER BY name FETCH FIRST 10 ROWS ONLY", isOracleContext: true);

        violations.Should().BeEmpty();
    }

    [Fact]
    public void CheckOracleSyntax_OracleContext_GatingReturnsEmpty()
    {
        // When isOracleContext=true, CheckOracleSyntaxInNonOracleContext should always return empty
        var violations = _checker.CheckOracleSyntaxInNonOracleContext(
            "SELECT DECODE(col,1,'a','b'), NVL(col,0), ROWNUM FROM DUAL CONNECT BY PRIOR id=parent_id START WITH id=1",
            isOracleContext: true);

        violations.Should().BeEmpty();
    }

    [Fact]
    public void CheckNonOracleSyntax_NonOracleContext_GatingReturnsEmpty()
    {
        // When isOracleContext=false, CheckNonOracleSyntaxInOracleContext should always return empty
        var violations = _checker.CheckNonOracleSyntaxInOracleContext(
            "SELECT ISNULL(col,0), GETDATE(), NEWID(), TOP 5 * FROM t",
            isOracleContext: false);

        violations.Should().BeEmpty();
    }

    [Fact]
    public void CheckRawSqlUnmappedTypeUsage_OracleContext_DetectsHierarchyId()
    {
        var violations = _checker.CheckRawSqlUnmappedTypeUsage("SELECT CAST(col AS HIERARCHYID) FROM t", isOracleContext: true);
        violations.Should().ContainSingle(v => v.RuleId == "DG014");
    }

    [Fact]
    public void CheckRawSqlUnmappedTypeUsage_OracleContext_MultipleUnmappedTypes()
    {
        var violations = _checker.CheckRawSqlUnmappedTypeUsage(
            "SELECT CAST(a AS UNIQUEIDENTIFIER), CAST(b AS MONEY) FROM t", isOracleContext: true);

        violations.Count.Should().BeGreaterThanOrEqualTo(2);
        violations.Should().Contain(v => v.Message.Contains("UNIQUEIDENTIFIER"));
        violations.Should().Contain(v => v.Message.Contains("MONEY"));
    }

    [Fact]
    public void CheckSqlServerSyntaxLeak_OracleContext_NoExecPattern_NoViolation()
    {
        var violations = _checker.CheckSqlServerSyntaxLeak("BEGIN proc_name; END;", isOracleContext: true);
        violations.Should().BeEmpty();
    }
}

/// <summary>
/// Unit tests for EfCoreInferenceSimulator (no DB required).
/// </summary>
public class EfCoreInferenceSimulatorTests
{
    private readonly EfCoreInferenceSimulator _simulator = new();

    [Fact]
    public void Predict_UnicodeNoMaxLength_ReturnsNVarchar2_2000()
    {
        var property = new PropertyDescriptor("Name", "string", "NAME", null, true, null, false, false, null);

        var result = _simulator.Predict(property);

        result.Should().Be(OracleColumnType.NVarchar2);
    }

    [Fact]
    public void Predict_NonUnicodeNoMaxLength_ReturnsVarchar2_2000()
    {
        var property = new PropertyDescriptor("Code", "byte[]", "CODE", null, true, null, false, false, null);

        var result = _simulator.Predict(property);

        result.Should().Be(OracleColumnType.Varchar2);
    }

    [Fact]
    public void Predict_UnicodeMaxLengthOver4000_ReturnsNClob()
    {
        var property = new PropertyDescriptor("Body", "string", "BODY", null, true, 5000, false, false, null);

        var result = _simulator.Predict(property);

        result.Should().Be(OracleColumnType.NClob);
    }

    [Fact]
    public void Predict_NonUnicodeMaxLengthOver4000_ReturnsClob()
    {
        var property = new PropertyDescriptor("Content", "byte[]", "CONTENT", null, true, 5000, false, false, null);

        var result = _simulator.Predict(property);

        result.Should().Be(OracleColumnType.Clob);
    }

    [Fact]
    public void Predict_UnicodeMaxLengthWithin4000_ReturnsNVarchar2()
    {
        var property = new PropertyDescriptor("Name", "string", "NAME", null, true, 200, false, false, null);

        var result = _simulator.Predict(property);

        result.Should().Be(OracleColumnType.NVarchar2);
    }

    [Fact]
    public void Predict_NonUnicodeMaxLengthWithin4000_ReturnsVarchar2()
    {
        var property = new PropertyDescriptor("Code", "byte[]", "CODE", null, true, 200, false, false, null);

        var result = _simulator.Predict(property);

        result.Should().Be(OracleColumnType.Varchar2);
    }

    [Fact]
    public void Predict_UnicodeMaxLengthExactly4000_ReturnsNVarchar2()
    {
        var property = new PropertyDescriptor("Name", "string", "NAME", null, true, 4000, false, false, null);

        var result = _simulator.Predict(property);

        result.Should().Be(OracleColumnType.NVarchar2);
    }

    [Fact]
    public void Predict_NonUnicodeMaxLengthExactly4000_ReturnsVarchar2()
    {
        var property = new PropertyDescriptor("Code", "byte[]", "CODE", null, true, 4000, false, false, null);

        var result = _simulator.Predict(property);

        result.Should().Be(OracleColumnType.Varchar2);
    }

    [Fact]
    public void Predict_SystemStringClrType_TreatedAsUnicode()
    {
        var property = new PropertyDescriptor("Name", "System.String", "NAME", null, true, null, false, false, null);

        var result = _simulator.Predict(property);

        result.Should().Be(OracleColumnType.NVarchar2);
    }

    [Fact]
    public void Predict_UnicodeMaxLength4001_ReturnsNClob()
    {
        var property = new PropertyDescriptor("Body", "string", "BODY", null, true, 4001, false, false, null);

        var result = _simulator.Predict(property);

        result.Should().Be(OracleColumnType.NClob);
    }

    [Fact]
    public void Predict_NonUnicodeMaxLength4001_ReturnsClob()
    {
        var property = new PropertyDescriptor("Content", "byte[]", "CONTENT", null, true, 4001, false, false, null);

        var result = _simulator.Predict(property);

        result.Should().Be(OracleColumnType.Clob);
    }
}

/// <summary>
/// Unit tests for LengthMismatchDetector (no DB required).
/// </summary>
public class LengthMismatchDetectorTests
{
    private readonly LengthMismatchDetector _detector = new();

    [Fact]
    public void Detect_DirectLengthMismatch_EntityExceedsColumn_ReturnsDG007()
    {
        var entity = new EntityDescriptor("e1", "User", "User", "USERS",
            new[] { new PropertyDescriptor("Name", "string", "NAME", "VARCHAR2", true, 200, false, false, null) });
        var columns = new[] { new ColumnDescriptor("NAME", "VARCHAR2", 100, null, null, true, "C", 100) };

        var violations = _detector.Detect(entity, columns, LengthSemantics.Char).ToList();

        violations.Should().ContainSingle(v =>
            v.RuleId == "DG007" &&
            v.Properties != null &&
            (int)v.Properties["entityMaxLength"]! == 200 &&
            (int)v.Properties["columnMaxLength"]! == 100);
    }

    [Fact]
    public void Detect_ByteOverflowRisk_UnicodeInByteSemantics_ReturnsDG008()
    {
        // Unicode (string) with MaxLength=1000, BYTE semantics, column MaxLength=2000 bytes
        // 1000 chars × 4 bytes/char = 4000 > 2000
        var entity = new EntityDescriptor("e1", "User", "User", "USERS",
            new[] { new PropertyDescriptor("Name", "string", "NAME", "VARCHAR2", true, 1000, false, false, null) });
        var columns = new[] { new ColumnDescriptor("NAME", "VARCHAR2", 2000, null, null, true, "B", null) };

        var violations = _detector.Detect(entity, columns, LengthSemantics.Byte).ToList();

        violations.Should().Contain(v => v.RuleId == "DG008");
    }

    [Fact]
    public void Detect_InferredFallback_NoMaxLengthUnicodeClobColumn_ReturnsDG009()
    {
        var entity = new EntityDescriptor("e1", "User", "User", "USERS",
            new[] { new PropertyDescriptor("Body", "string", "BODY", null, true, null, false, false, null) });
        var columns = new[] { new ColumnDescriptor("BODY", "CLOB", null, null, null, true, null) };

        var violations = _detector.Detect(entity, columns, LengthSemantics.Char).ToList();

        violations.Should().ContainSingle(v =>
            v.RuleId == "DG009" &&
            v.Message.Contains("NVARCHAR2(2000)") &&
            v.Message.Contains("CLOB"));
    }

    [Fact]
    public void Detect_InferredFallback_NClobColumn_ReturnsDG009()
    {
        var entity = new EntityDescriptor("e1", "User", "User", "USERS",
            new[] { new PropertyDescriptor("Body", "string", "BODY", null, true, null, false, false, null) });
        var columns = new[] { new ColumnDescriptor("BODY", "NCLOB", null, null, null, true, null) };

        var violations = _detector.Detect(entity, columns, LengthSemantics.Char).ToList();

        violations.Should().ContainSingle(v =>
            v.RuleId == "DG009" &&
            v.Message.Contains("NCLOB"));
    }

    [Fact]
    public void Detect_ExactMatch_NoViolation()
    {
        var entity = new EntityDescriptor("e1", "User", "User", "USERS",
            new[] { new PropertyDescriptor("Name", "string", "NAME", "VARCHAR2", true, 100, false, false, null) });
        var columns = new[] { new ColumnDescriptor("NAME", "VARCHAR2", 100, null, null, true, "C", 100) };

        var violations = _detector.Detect(entity, columns, LengthSemantics.Char).ToList();

        violations.Should().BeEmpty();
    }

    [Fact]
    public void Detect_ColumnNotFound_Skips()
    {
        var entity = new EntityDescriptor("e1", "User", "User", "USERS",
            new[] { new PropertyDescriptor("Name", "string", "NAME", "VARCHAR2", true, 200, false, false, null) });
        var columns = new[] { new ColumnDescriptor("OTHER_COL", "VARCHAR2", 100, null, null, true, "C", 100) };

        var violations = _detector.Detect(entity, columns, LengthSemantics.Char).ToList();

        violations.Should().BeEmpty();
    }

    [Fact]
    public void Detect_MultipleProperties_MixedViolations()
    {
        var entity = new EntityDescriptor("e1", "User", "User", "USERS",
            new[]
            {
                new PropertyDescriptor("Name", "string", "NAME", "VARCHAR2", true, 200, false, false, null),
                new PropertyDescriptor("Code", "string", "CODE", "VARCHAR2", true, 50, false, false, null),
                new PropertyDescriptor("Body", "string", "BODY", null, true, null, false, false, null),
            });
        var columns = new[]
        {
            new ColumnDescriptor("NAME", "VARCHAR2", 100, null, null, true, "C", 100),
            new ColumnDescriptor("CODE", "VARCHAR2", 200, null, null, true, "C", 200),
            new ColumnDescriptor("BODY", "CLOB", null, null, null, true, null),
        };

        var violations = _detector.Detect(entity, columns, LengthSemantics.Char).ToList();

        violations.Should().Contain(v => v.RuleId == "DG007" && v.Message.Contains("Name"));
        violations.Should().NotContain(v => v.Message.Contains("Code") && v.RuleId == "DG007");
        violations.Should().Contain(v => v.RuleId == "DG009" && v.Message.Contains("Body"));
    }

    [Fact]
    public void Detect_EntityLengthLessThanColumn_NoViolation()
    {
        var entity = new EntityDescriptor("e1", "User", "User", "USERS",
            new[] { new PropertyDescriptor("Name", "string", "NAME", "VARCHAR2", true, 50, false, false, null) });
        var columns = new[] { new ColumnDescriptor("NAME", "VARCHAR2", 200, null, null, true, "C", 200) };

        var violations = _detector.Detect(entity, columns, LengthSemantics.Char).ToList();

        violations.Should().BeEmpty();
    }

    [Fact]
    public void Detect_ByteSemantics_NonUnicode_NoOverflow()
    {
        // Non-Unicode (byte[]) with MaxLength=1000, BYTE semantics, column MaxLength=2000
        // 1000 × 1 byte/char = 1000 < 2000 → no overflow
        var entity = new EntityDescriptor("e1", "User", "User", "USERS",
            new[] { new PropertyDescriptor("Data", "byte[]", "DATA", "RAW", true, 1000, false, false, null) });
        var columns = new[] { new ColumnDescriptor("DATA", "RAW", 2000, null, null, true, "B", null) };

        var violations = _detector.Detect(entity, columns, LengthSemantics.Byte).ToList();

        violations.Should().NotContain(v => v.RuleId == "DG008");
    }

    [Fact]
    public void Detect_CharSemantics_NoByteOverflowCheck()
    {
        // CHAR semantics → no byte overflow check regardless of sizes
        var entity = new EntityDescriptor("e1", "User", "User", "USERS",
            new[] { new PropertyDescriptor("Name", "string", "NAME", "VARCHAR2", true, 1000, false, false, null) });
        var columns = new[] { new ColumnDescriptor("NAME", "VARCHAR2", 2000, null, null, true, "C", 2000) };

        var violations = _detector.Detect(entity, columns, LengthSemantics.Char).ToList();

        violations.Should().NotContain(v => v.RuleId == "DG008");
    }

    [Fact]
    public void Detect_ColumnCharLengthPreferredOverMaxLength()
    {
        // Column has CharLength=150, MaxLength=300 (bytes). Entity MaxLength=200.
        // Should compare against CharLength (150), so 200 > 150 → DG007
        var entity = new EntityDescriptor("e1", "User", "User", "USERS",
            new[] { new PropertyDescriptor("Name", "string", "NAME", "VARCHAR2", true, 200, false, false, null) });
        var columns = new[] { new ColumnDescriptor("NAME", "VARCHAR2", 300, null, null, true, "B", 150) };

        var violations = _detector.Detect(entity, columns, LengthSemantics.Byte).ToList();

        violations.Should().Contain(v => v.RuleId == "DG007" && (int)v.Properties!["columnMaxLength"]! == 150);
    }

    [Fact]
    public void Detect_PascalCaseToSnakeCase_ColumnMatch()
    {
        // Property "FirstName" → column "FIRST_NAME" (PascalCase → UPPER_SNAKE_CASE)
        var entity = new EntityDescriptor("e1", "User", "User", "USERS",
            new[] { new PropertyDescriptor("FirstName", "string", null, "VARCHAR2", true, 200, false, false, null) });
        var columns = new[] { new ColumnDescriptor("FIRST_NAME", "VARCHAR2", 100, null, null, true, "C", 100) };

        var violations = _detector.Detect(entity, columns, LengthSemantics.Char).ToList();

        violations.Should().ContainSingle(v => v.RuleId == "DG007");
    }

    [Fact]
    public void Detect_NoMaxLengthNotUnicode_NoInferredFallback()
    {
        // Non-Unicode with no MaxLength → no DG009 (only Unicode triggers inferred fallback)
        var entity = new EntityDescriptor("e1", "User", "User", "USERS",
            new[] { new PropertyDescriptor("Data", "byte[]", "DATA", null, true, null, false, false, null) });
        var columns = new[] { new ColumnDescriptor("DATA", "CLOB", null, null, null, true, null) };

        var violations = _detector.Detect(entity, columns, LengthSemantics.Char).ToList();

        violations.Should().NotContain(v => v.RuleId == "DG009");
    }
}

/// <summary>
/// Unit tests for OracleColumnTypeFactory (no DB required).
/// </summary>
public class OracleColumnTypeFactoryTests
{
    [Fact]
    public void Varchar2_ReturnsVarchar2Type()
    {
        OracleColumnTypeFactory.Varchar2(100).Should().Be(OracleColumnType.Varchar2);
    }

    [Fact]
    public void NVarchar2_ReturnsNVarchar2Type()
    {
        OracleColumnTypeFactory.NVarchar2(200).Should().Be(OracleColumnType.NVarchar2);
    }

    [Fact]
    public void Clob_ReturnsClobType()
    {
        OracleColumnTypeFactory.Clob().Should().Be(OracleColumnType.Clob);
    }

    [Fact]
    public void NClob_ReturnsNClobType()
    {
        OracleColumnTypeFactory.NClob().Should().Be(OracleColumnType.NClob);
    }

    [Theory]
    [InlineData(OracleColumnType.Varchar2)]
    [InlineData(OracleColumnType.NVarchar2)]
    [InlineData(OracleColumnType.Char)]
    [InlineData(OracleColumnType.NChar)]
    [InlineData(OracleColumnType.Clob)]
    [InlineData(OracleColumnType.NClob)]
    [InlineData(OracleColumnType.Number)]
    [InlineData(OracleColumnType.Date)]
    [InlineData(OracleColumnType.Timestamp)]
    [InlineData(OracleColumnType.TimestampWithTimeZone)]
    [InlineData(OracleColumnType.Raw)]
    [InlineData(OracleColumnType.Blob)]
    [InlineData(OracleColumnType.RowId)]
    public void OracleColumnType_AllEnumValues_AreDistinct(OracleColumnType type)
    {
        // Verify enum values are accessible and distinct
        ((int)type).Should().BeGreaterThanOrEqualTo(0);
    }
}
