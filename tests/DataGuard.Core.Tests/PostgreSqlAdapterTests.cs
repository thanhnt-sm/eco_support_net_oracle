using System.Linq;
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
        violations.Should().Contain(v => v.RuleId == "PG001");
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
        violations.Should().Contain(v => v.RuleId == "PG002");
    }

    [Fact]
    public void CheckNonPostgreSqlSyntax_PgContext_DetectsGetDate()
    {
        var violations = _checker.CheckNonPostgreSqlSyntaxInPostgreSqlContext("SELECT GETDATE()", isPostgreSqlContext: true);
        violations.Should().Contain(v => v.RuleId == "PG002");
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

    [Fact]
    public void CheckPostgreSqlSyntax_NonPgContext_MultipleViolations()
    {
        // SERIAL keyword + ILIKE keyword + :: cast operator = 3+ PG001 violations
        var violations = _checker.CheckPostgreSqlSyntaxInNonPostgreSqlContext(
            "CREATE TABLE t (id SERIAL); SELECT * FROM users WHERE name ILIKE '%test%' AND bio::text IS NOT NULL",
            isPostgreSqlContext: false);

        violations.Count.Should().BeGreaterThanOrEqualTo(3);
        violations.Should().OnlyContain(v => v.RuleId == "PG001");
    }

    [Fact]
    public void CheckPostgreSqlSyntax_NonPgContext_DetectsBigSerial()
    {
        var violations = _checker.CheckPostgreSqlSyntaxInNonPostgreSqlContext(
            "CREATE TABLE t (id BIGSERIAL)", isPostgreSqlContext: false);

        violations.Should().Contain(v =>
            v.RuleId == "PG001" &&
            v.Properties != null &&
            v.Properties.ContainsKey("keyword") &&
            v.Properties["keyword"]!.ToString() == "BIGSERIAL");
    }

    [Fact]
    public void CheckPostgreSqlSyntax_NonPgContext_DetectsJsonb()
    {
        var violations = _checker.CheckPostgreSqlSyntaxInNonPostgreSqlContext(
            "SELECT data::jsonb FROM t", isPostgreSqlContext: false);

        // JSONB keyword + :: operator = at least 2 violations
        violations.Count.Should().BeGreaterThanOrEqualTo(2);
        violations.Should().Contain(v =>
            v.Properties != null &&
            v.Properties.ContainsKey("keyword") &&
            v.Properties["keyword"]!.ToString() == "JSONB");
    }

    [Fact]
    public void CheckPostgreSqlSyntax_NonPgContext_DetectsConcatOperator()
    {
        var violations = _checker.CheckPostgreSqlSyntaxInNonPostgreSqlContext(
            "SELECT first_name || ' ' || last_name FROM users", isPostgreSqlContext: false);

        violations.Should().Contain(v =>
            v.RuleId == "PG001" &&
            v.Properties != null &&
            v.Properties.ContainsKey("operator") &&
            v.Properties["operator"]!.ToString() == "||");
    }

    [Fact]
    public void CheckPostgreSqlSyntax_NonPgContext_DetectsRegexMatchOperator()
    {
        var violations = _checker.CheckPostgreSqlSyntaxInNonPostgreSqlContext(
            "SELECT * FROM t WHERE col ~ '^test'", isPostgreSqlContext: false);

        violations.Should().Contain(v =>
            v.RuleId == "PG001" &&
            v.Properties != null &&
            v.Properties.ContainsKey("operator") &&
            v.Properties["operator"]!.ToString() == "~");
    }

    [Fact]
    public void CheckPostgreSqlSyntax_NonPgContext_DetectsContainmentOperator()
    {
        var violations = _checker.CheckPostgreSqlSyntaxInNonPostgreSqlContext(
            "SELECT * FROM t WHERE data @> '{\"key\": 1}'", isPostgreSqlContext: false);

        violations.Should().Contain(v =>
            v.RuleId == "PG001" &&
            v.Properties != null &&
            v.Properties.ContainsKey("operator") &&
            v.Properties["operator"]!.ToString() == "@>");
    }

    [Fact]
    public void CheckPostgreSqlSyntax_NonPgContext_DetectsJsonbExistenceOperator()
    {
        var violations = _checker.CheckPostgreSqlSyntaxInNonPostgreSqlContext(
            "SELECT * FROM t WHERE data ? 'key'", isPostgreSqlContext: false);

        violations.Should().Contain(v =>
            v.RuleId == "PG001" &&
            v.Properties != null &&
            v.Properties.ContainsKey("operator") &&
            v.Properties["operator"]!.ToString() == "?");
    }

    [Fact]
    public void CheckNonPostgreSqlSyntax_PgContext_DetectsTop()
    {
        var violations = _checker.CheckNonPostgreSqlSyntaxInPostgreSqlContext(
            "SELECT TOP 10 * FROM t", isPostgreSqlContext: true);

        violations.Should().Contain(v => v.RuleId == "PG002");
    }

    [Fact]
    public void CheckNonPostgreSqlSyntax_PgContext_DetectsIsNull()
    {
        var violations = _checker.CheckNonPostgreSqlSyntaxInPostgreSqlContext(
            "SELECT ISNULL(col, 0) FROM t", isPostgreSqlContext: true);

        violations.Should().Contain(v => v.RuleId == "PG002");
    }

    [Fact]
    public void CheckNonPostgreSqlSyntax_PgContext_DetectsAutoIncrement()
    {
        var violations = _checker.CheckNonPostgreSqlSyntaxInPostgreSqlContext(
            "CREATE TABLE t (id INT AUTO_INCREMENT)", isPostgreSqlContext: true);

        violations.Should().Contain(v =>
            v.RuleId == "PG002" &&
            v.Properties != null &&
            v.Properties.ContainsKey("keyword") &&
            v.Properties["keyword"]!.ToString() == "AUTO_INCREMENT");
    }

    [Fact]
    public void CheckNonPostgreSqlSyntax_PgContext_DetectsMySqlLimitOffset()
    {
        var violations = _checker.CheckNonPostgreSqlSyntaxInPostgreSqlContext(
            "SELECT * FROM t LIMIT 10, 20", isPostgreSqlContext: true);

        violations.Should().Contain(v =>
            v.RuleId == "PG002" &&
            v.Properties != null &&
            v.Properties.ContainsKey("suggestion"));
    }

    [Fact]
    public void CheckNonPostgreSqlSyntax_PgContext_DetectsConnectBy()
    {
        var violations = _checker.CheckNonPostgreSqlSyntaxInPostgreSqlContext(
            "SELECT * FROM t CONNECT BY PRIOR id = parent_id", isPostgreSqlContext: true);

        violations.Should().Contain(v =>
            v.RuleId == "PG002" &&
            v.Properties != null &&
            v.Properties.ContainsKey("source") &&
            v.Properties["source"]!.ToString() == "Oracle");
    }

    [Fact]
    public void CheckNonPostgreSqlSyntax_PgContext_DetectsRowNum()
    {
        var violations = _checker.CheckNonPostgreSqlSyntaxInPostgreSqlContext(
            "SELECT * FROM t WHERE ROWNUM <= 10", isPostgreSqlContext: true);

        violations.Should().Contain(v =>
            v.RuleId == "PG002" &&
            v.Properties != null &&
            v.Properties.ContainsKey("keyword") &&
            v.Properties["keyword"]!.ToString() == "ROWNUM");
    }

    [Fact]
    public void CheckNonPostgreSqlSyntax_PgContext_DetectsSysDate()
    {
        var violations = _checker.CheckNonPostgreSqlSyntaxInPostgreSqlContext(
            "SELECT SYSDATE FROM t", isPostgreSqlContext: true);

        violations.Should().Contain(v =>
            v.RuleId == "PG002" &&
            v.Properties != null &&
            v.Properties.ContainsKey("keyword") &&
            v.Properties["keyword"]!.ToString() == "SYSDATE");
    }

    [Fact]
    public void CheckNonPostgreSqlSyntax_PgContext_MultipleViolations()
    {
        // NVL (Oracle) + GETDATE (SQL Server) = multiple PG002 violations
        var violations = _checker.CheckNonPostgreSqlSyntaxInPostgreSqlContext(
            "SELECT NVL(col, 0), GETDATE() FROM t", isPostgreSqlContext: true);

        violations.Count.Should().BeGreaterThanOrEqualTo(2);
        violations.Should().OnlyContain(v => v.RuleId == "PG002");
    }

    [Fact]
    public void CheckPostgreSqlSyntax_NonPgContext_CleanSql_NoViolation()
    {
        var violations = _checker.CheckPostgreSqlSyntaxInNonPostgreSqlContext(
            "SELECT * FROM users WHERE id = 1 AND name = 'test'", isPostgreSqlContext: false);

        violations.Should().BeEmpty();
    }

    [Fact]
    public void CheckPostgreSqlSyntax_WhitespaceOnlySql_NoViolation()
    {
        var violations = _checker.CheckPostgreSqlSyntaxInNonPostgreSqlContext(
            "   \t\n  ", isPostgreSqlContext: false);

        violations.Should().BeEmpty();
    }

    [Fact]
    public void CheckProviderOptionMismatch_PgContext_NonPgProvider_ReturnsViolation()
    {
        var violations = _checker.CheckProviderOptionMismatch(
            isPostgreSqlContext: true,
            providerName: "Microsoft.EntityFrameworkCore.SqlServer");

        violations.Should().ContainSingle(v => v.RuleId == "PG004");
    }

    [Fact]
    public void CheckProviderOptionMismatch_NonPgContext_AnyProvider_NoViolation()
    {
        var violations = _checker.CheckProviderOptionMismatch(
            isPostgreSqlContext: false,
            providerName: "Microsoft.EntityFrameworkCore.SqlServer");

        violations.Should().BeEmpty();
    }

    [Fact]
    public void CheckProviderOptionMismatch_PgContext_NpgsqlProvider_NoViolation()
    {
        var violations = _checker.CheckProviderOptionMismatch(
            isPostgreSqlContext: true,
            providerName: "Npgsql.EntityFrameworkCore.PostgreSQL");

        violations.Should().BeEmpty();
    }

    [Fact]
    public void CheckRawSqlUnmappedTypeUsage_PgContext_SqlServerType_ReturnsViolation()
    {
        var violations = _checker.CheckRawSqlUnmappedTypeUsage(
            "SELECT CAST(id AS UNIQUEIDENTIFIER) FROM t", isPostgreSqlContext: true);

        violations.Should().Contain(v =>
            v.RuleId == "PG005" &&
            v.Properties != null &&
            v.Properties.ContainsKey("type") &&
            v.Properties["type"]!.ToString() == "UNIQUEIDENTIFIER");
    }

    [Fact]
    public void CheckRawSqlUnmappedTypeUsage_PgContext_OracleType_ReturnsViolation()
    {
        var violations = _checker.CheckRawSqlUnmappedTypeUsage(
            "SELECT CAST(col AS VARCHAR2(100)) FROM t", isPostgreSqlContext: true);

        violations.Should().Contain(v =>
            v.RuleId == "PG005" &&
            v.Properties != null &&
            v.Properties.ContainsKey("source") &&
            v.Properties["source"]!.ToString() == "Oracle");
    }

    [Fact]
    public void CheckRawSqlUnmappedTypeUsage_NonPgContext_NoViolation()
    {
        var violations = _checker.CheckRawSqlUnmappedTypeUsage(
            "SELECT CAST(id AS UNIQUEIDENTIFIER) FROM t", isPostgreSqlContext: false);

        violations.Should().BeEmpty();
    }

    [Fact]
    public void CheckRawSqlUnmappedTypeUsage_PgContext_PgType_NoViolation()
    {
        // jsonb is a valid PostgreSQL type — no PG005 violation
        var violations = _checker.CheckRawSqlUnmappedTypeUsage(
            "SELECT CAST(col AS jsonb) FROM t", isPostgreSqlContext: true);

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

        violations.Should().Contain(v => v.RuleId == "PG003");
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
    public void Detect_NoMaxLength_WarnsAboutInferredSize()
    {
        var entity = new EntityDescriptor("e1", "User", "Users", "dbo",
            new[] { new PropertyDescriptor("Name", "string", "name", "varchar", true, null, false, false, null) });
        var columns = new[] { new ColumnDescriptor("name", "varchar", 100, null, null, true, null) };

        var violations = _detector.Detect(entity, columns);

        // Adapter warns when entity has no MaxLength but column is VARCHAR(n)
        violations.Should().Contain(v => v.RuleId == "PG003");
    }

    [Fact]
    public void Detect_ExactLength_NoViolation()
    {
        var entity = new EntityDescriptor("e1", "User", "Users", "dbo",
            new[] { new PropertyDescriptor("Name", "string", "name", "varchar", true, 100, false, false, null) });
        var columns = new[] { new ColumnDescriptor("name", "varchar", 100, null, null, true, null) };

        var violations = _detector.Detect(entity, columns);

        violations.Should().BeEmpty();
    }

    [Fact]
    public void Detect_ColumnNotFound_Skips()
    {
        // Both property.ColumnName and property.Name must not match any column
        var entity = new EntityDescriptor("e1", "User", "Users", "dbo",
            new[] { new PropertyDescriptor("FullName", "string", "full_name", "varchar", true, 200, false, false, null) });
        var columns = new[] { new ColumnDescriptor("name", "varchar", 100, null, null, true, null) };

        var violations = _detector.Detect(entity, columns);

        violations.Should().BeEmpty();
    }

    [Fact]
    public void Detect_MultipleProperties_MixedResults()
    {
        var entity = new EntityDescriptor("e1", "User", "Users", "dbo",
            new[]
            {
                new PropertyDescriptor("Name", "string", "name", "varchar", true, 200, false, false, null),
                new PropertyDescriptor("Email", "string", "email", "varchar", true, 50, false, false, null),
            });
        var columns = new[]
        {
            new ColumnDescriptor("name", "varchar", 100, null, null, true, null),
            new ColumnDescriptor("email", "varchar", 200, null, null, true, null),
        };

        var violations = _detector.Detect(entity, columns).ToList();

        // Name exceeds (200 > 100), Email does not (50 < 200)
        violations.Should().Contain(v =>
            v.RuleId == "PG003" &&
            v.Properties != null &&
            v.Properties.ContainsKey("property") &&
            v.Properties["property"]!.ToString() == "Name");
        violations.Should().NotContain(v =>
            v.Properties != null &&
            v.Properties.ContainsKey("property") &&
            v.Properties["property"]!.ToString() == "Email");
    }

    [Fact]
    public void Detect_TextColumnWithMaxLength_WarnsUnlimited()
    {
        var entity = new EntityDescriptor("e1", "User", "Users", "dbo",
            new[] { new PropertyDescriptor("Bio", "string", "bio", "text", true, 500, false, false, null) });
        var columns = new[] { new ColumnDescriptor("bio", "text", null, null, null, true, null) };

        var violations = _detector.Detect(entity, columns);

        violations.Should().Contain(v =>
            v.RuleId == "PG003" &&
            v.Properties != null &&
            v.Properties.ContainsKey("columnIsUnlimited") &&
            v.Properties["columnIsUnlimited"]!.ToString() == bool.TrueString);
    }

    [Fact]
    public void Detect_JsonbColumnWithMaxLength_WarnsUnlimited()
    {
        var entity = new EntityDescriptor("e1", "User", "Users", "dbo",
            new[] { new PropertyDescriptor("Metadata", "string", "metadata", "jsonb", true, 1000, false, false, null) });
        var columns = new[] { new ColumnDescriptor("metadata", "jsonb", null, null, null, true, null) };

        var violations = _detector.Detect(entity, columns);

        violations.Should().Contain(v =>
            v.RuleId == "PG003" &&
            v.Properties != null &&
            v.Properties.ContainsKey("columnType") &&
            v.Properties["columnType"]!.ToString() == "jsonb");
    }

    [Fact]
    public void Detect_VarcharExceedsPgMax_ReturnsViolation()
    {
        // MaxLength exceeds PgVarcharMaxLength (10485760)
        var entity = new EntityDescriptor("e1", "User", "Users", "dbo",
            new[] { new PropertyDescriptor("Data", "string", "data", "varchar", true, 20_000_000, false, false, null) });
        var columns = new[] { new ColumnDescriptor("data", "varchar", null, null, null, true, null) };

        var violations = _detector.Detect(entity, columns);

        violations.Should().Contain(v =>
            v.RuleId == "PG003" &&
            v.Properties != null &&
            v.Properties.ContainsKey("pgVarcharMax"));
    }

    [Fact]
    public void Detect_CaseInsensitiveColumnMatch()
    {
        var entity = new EntityDescriptor("e1", "User", "Users", "dbo",
            new[] { new PropertyDescriptor("Name", "string", "NAME", "varchar", true, 200, false, false, null) });
        var columns = new[] { new ColumnDescriptor("name", "varchar", 100, null, null, true, null) };

        var violations = _detector.Detect(entity, columns);

        violations.Should().Contain(v => v.RuleId == "PG003");
    }

    [Fact]
    public void Detect_Utf8ByteOverflow_Detected()
    {
        // string property with MaxLength > column MaxLength triggers UTF-8 overflow warning
        var entity = new EntityDescriptor("e1", "User", "Users", "dbo",
            new[] { new PropertyDescriptor("Name", "string", "name", "varchar", true, 200, false, false, null) });
        var columns = new[] { new ColumnDescriptor("name", "varchar", 100, null, null, true, null) };

        var violations = _detector.Detect(entity, columns);

        // Should have at least one violation with encoding info (UTF-8 overflow)
        violations.Should().Contain(v =>
            v.RuleId == "PG003" &&
            v.Properties != null &&
            v.Properties.ContainsKey("encoding") &&
            v.Properties["encoding"]!.ToString() == "UTF-8");
    }

    [Fact]
    public void Detect_EntityNoMaxLength_VarcharColumn_WarnsRuntimeRisk()
    {
        var entity = new EntityDescriptor("e1", "User", "Users", "dbo",
            new[] { new PropertyDescriptor("Name", "string", "name", "varchar", true, null, false, false, null) });
        var columns = new[] { new ColumnDescriptor("name", "varchar", 255, null, null, true, null) };

        var violations = _detector.Detect(entity, columns);

        violations.Should().Contain(v =>
            v.RuleId == "PG003" &&
            v.Properties != null &&
            v.Properties.ContainsKey("inferredType") &&
            v.Properties["inferredType"]!.ToString() == "character varying");
    }

    [Fact]
    public void Detect_ColumnNullMaxLength_NoLengthViolation()
    {
        var entity = new EntityDescriptor("e1", "User", "Users", "dbo",
            new[] { new PropertyDescriptor("Name", "string", "name", "varchar", true, 200, false, false, null) });

        // Column has no MaxLength (e.g., defined as varchar without length)
        var columns = new[] { new ColumnDescriptor("name", "varchar", null, null, null, true, null) };

        var violations = _detector.Detect(entity, columns);

        // Check 1 skips (column.MaxLength is null), check 2 skips (200 < PgMax),
        // check 3 skips (column.MaxLength is null), check 4 skips (varchar not unlimited),
        // check 5 skips (property has MaxLength)
        violations.Should().BeEmpty();
    }
}
