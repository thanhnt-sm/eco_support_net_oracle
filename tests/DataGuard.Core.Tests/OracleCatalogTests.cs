using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataGuard.Core.Abstractions;
using DataGuard.Oracle.Adapter;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

/// <summary>
/// Unit tests for the Oracle catalog grouping (ALL_ARGUMENTS + ALL_PROCEDURES) without a database.
/// Row shapes mirror what Oracle 23 returned for the CUST_PKG fixture used by the live test.
/// </summary>
public class OracleCatalogTests
{
    [Fact]
    public void BuildCatalogSql_NeverReadsPackageNameFromAllProcedures()
    {
        foreach (var filter in new[] { null, string.Empty, "CUST_PKG" })
        {
            var sql = AllArgumentsReader.BuildCatalogSql(filter, filterByName: true);
            sql.Should().NotContain("p.package_name");
            sql.Should().Contain("a.data_level = 0");
            sql.Should().Contain("a.owner = UPPER(:owner)");
        }

        AllArgumentsReader.BuildCatalogSql(string.Empty, false).Should().Contain("a.package_name IS NULL");
        AllArgumentsReader.BuildCatalogSql("X", false).Should().Contain("UPPER(a.package_name) = UPPER(:packageName)")
            .And.Contain("p.object_type = 'PACKAGE' AND UPPER(p.object_name) = UPPER(:packageName)");
        AllArgumentsReader.BuildCatalogSql(null, false).Should().NotContain(":packageName");
    }

    [Fact]
    public void Group_KeepsEveryOverloadIncludingZeroArgumentOnes()
    {
        var entries = OracleCatalog.Group("dataguard", CustPkgRows());

        var getOne = entries.Where(e => e.Name == "GET_ONE").ToList();
        getOne.Should().HaveCount(2);
        getOne.Select(e => e.Parameters.Count).Should().BeEquivalentTo(new[] { 2, 3 });
        getOne.Select(e => e.SubprogramId).Should().BeEquivalentTo(new[] { 1, 2 });

        var countAll = entries.Where(e => e.Name == "COUNT_ALL").ToList();
        countAll.Should().HaveCount(2);
        countAll.Should().ContainSingle(e => e.Parameters.Count == 0 && e.ReturnType == "NUMBER" && e.SubprogramId == 3);
        countAll.Should().ContainSingle(e => e.Parameters.Count == 1 && e.Parameters[0].Name == "P_MIN");

        // 0-argument procedure: Oracle 18c+ lists it only in ALL_PROCEDURES.
        entries.Should().ContainSingle(e => e.Name == "NOARGS" && e.Parameters.Count == 0 && e.ReturnType == null && e.PackageName == "CUST_PKG");
        entries.Should().ContainSingle(e => e.Name == "STANDALONE_P" && e.PackageName == null && e.Parameters.Count == 1);
        entries.Should().ContainSingle(e => e.Name == "STANDALONE_NOARGS" && e.Parameters.Count == 0);
    }

    [Fact]
    public void Group_IdsCarryOwnerPackageAndSubprogramId()
    {
        var entries = OracleCatalog.Group("dataguard", CustPkgRows());

        entries.Select(e => e.Id).Should().Contain(new[]
        {
            "oracle:DATAGUARD.CUST_PKG.GET_ONE#1",
            "oracle:DATAGUARD.CUST_PKG.GET_ONE#2",
            "oracle:DATAGUARD.CUST_PKG.COUNT_ALL#3",
            "oracle:DATAGUARD.CUST_PKG.COUNT_ALL#4",
            "oracle:DATAGUARD._.STANDALONE_P#1",
        });
        entries.Select(e => e.Id).Should().OnlyHaveUniqueItems();

        var descriptor = OracleCatalog.ToDescriptor(entries.First(e => e.Name == "GET_ONE"));
        descriptor.PackageName.Should().Be("CUST_PKG");
        descriptor.Schema.Should().Be("DATAGUARD");
        OracleCatalog.ToDescriptor(entries.First(e => e.Name == "STANDALONE_P")).PackageName.Should().BeEmpty();
    }

    [Fact]
    public void Group_MapsDirectionDefaultsAndReturnRow()
    {
        var entries = OracleCatalog.Group("DATAGUARD", CustPkgRows());

        var withDefault = entries.Single(e => e.Name == "WITH_DEF");
        withDefault.Parameters.Single(p => p.Name == "P_A").HasDefault.Should().BeFalse();
        withDefault.Parameters.Single(p => p.Name == "P_B").HasDefault.Should().BeTrue();

        var getOne = entries.First(e => e.Name == "GET_ONE" && e.SubprogramId == 1);
        getOne.Parameters.Select(p => (p.Name, p.Direction)).Should().Equal(
            ("P_ID", ParameterDirection.Input),
            ("P_NAME", ParameterDirection.Output));
        getOne.Parameters.Should().OnlyContain(p => p.Overload == 1);

        var function = entries.Single(e => e.Name == "STANDALONE_F");
        function.ReturnType.Should().Be("VARCHAR2");
        function.Parameters.Should().BeEmpty();
        function.IsFunction.Should().BeTrue();
    }

    [Fact]
    public void Group_DetectsRefCursorOutputsAndDescribability()
    {
        var entries = OracleCatalog.Group("DATAGUARD", CustPkgRows());

        var getCur = entries.Single(e => e.Name == "GET_CUR");
        getCur.ReturnsRefCursor.Should().BeTrue();
        getCur.RefCursorParameters.Should().Equal("P_CUR");
        OracleCatalog.IsDescribable(getCur).Should().BeTrue();
        OracleCatalog.ToDescriptor(getCur).ReturnsRefCursor.Should().BeTrue();

        entries.Single(e => e.Name == "WITH_DEF").ReturnsRefCursor.Should().BeFalse();
        OracleCatalog.IsDescribable(entries.First(e => e.Name == "GET_ONE")).Should().BeFalse();
    }

    [Fact]
    public void Group_IgnoresLegacyPlaceholderRowsAndObjectTypeMethods()
    {
        var rows = new List<OracleArgumentRow>
        {
            Header("PKG", "P0", 1),

            // Pre-18c placeholder row for a 0-argument procedure: no name, no data type.
            Arg("PKG", "P0", 1, null, 1, null, null),

            // A TYPE method has argument rows but no PACKAGE header in ALL_PROCEDURES.
            Arg("ADDRESS_T", "FORMAT", 1, "SELF", 1, "IN", "OBJECT"),
        };

        var entries = OracleCatalog.Group("APP", rows);

        entries.Should().ContainSingle();
        entries[0].Name.Should().Be("P0");
        entries[0].Parameters.Should().BeEmpty();
    }

    [Fact]
    public void BuildSchemaDescriptor_StampsCharsetPerColumnAndCarriesNlsFacts()
    {
        var columns = new Dictionary<string, List<ColumnDescriptor>>
        {
            ["CUSTOMERS"] = new()
            {
                new ColumnDescriptor("NAME", "VARCHAR2", 300, null, null, true, "B", 300),
                new ColumnDescriptor("NOTE", "NVARCHAR2", 200, null, null, true, "C", 100),
                new ColumnDescriptor("ID", "NUMBER", 22, 10, 0, false, null),
            },
        };
        var nls = new NlsParameters
        {
            LengthSemantics = LengthSemantics.Byte,
            CharacterSet = "AL32UTF8",
            NCharCharacterSet = "AL16UTF16",
            MaxStringSize = "EXTENDED",
        };

        var schema = OracleCatalogBuilder.BuildSchemaDescriptor("app", columns, nls);

        schema.Id.Should().Be("oracle:schema:APP");
        schema.LengthSemantics.Should().Be("BYTE");
        schema.DatabaseCharset.Should().Be("AL32UTF8");
        schema.NationalCharset.Should().Be("AL16UTF16");
        schema.MaxStringSize.Should().Be("EXTENDED");
        var table = schema.Tables.Should().ContainSingle().Subject;
        table.Schema.Should().Be("APP");
        table.Columns.Single(c => c.Name == "NAME").Charset.Should().Be("AL32UTF8");
        table.Columns.Single(c => c.Name == "NOTE").Charset.Should().Be("AL16UTF16");
        table.Columns.Single(c => c.Name == "ID").Charset.Should().BeNull();
    }

    private static List<OracleArgumentRow> CustPkgRows() => new()
    {
        Header("CUST_PKG", "GET_ONE", 1, "1"),
        Header("CUST_PKG", "GET_ONE", 2, "2"),
        Header("CUST_PKG", "COUNT_ALL", 3, "1"),
        Header("CUST_PKG", "COUNT_ALL", 4, "2"),
        Header("CUST_PKG", "NOARGS", 5),
        Header("CUST_PKG", "WITH_DEF", 6),
        Header("CUST_PKG", "GET_CUR", 7),
        Header(null, "STANDALONE_P", 1, objectType: "PROCEDURE"),
        Header(null, "STANDALONE_NOARGS", 1, objectType: "PROCEDURE"),
        Header(null, "STANDALONE_F", 1, objectType: "FUNCTION"),
        Arg("CUST_PKG", "GET_ONE", 1, "P_ID", 1, "IN", "NUMBER", overload: "1"),
        Arg("CUST_PKG", "GET_ONE", 1, "P_NAME", 2, "OUT", "VARCHAR2", overload: "1"),
        Arg("CUST_PKG", "GET_ONE", 2, "P_ID", 1, "IN", "NUMBER", overload: "2"),
        Arg("CUST_PKG", "GET_ONE", 2, "P_CODE", 2, "IN", "VARCHAR2", overload: "2"),
        Arg("CUST_PKG", "GET_ONE", 2, "P_NAME", 3, "OUT", "VARCHAR2", overload: "2"),
        Arg("CUST_PKG", "COUNT_ALL", 3, null, 0, "OUT", "NUMBER", overload: "1"),
        Arg("CUST_PKG", "COUNT_ALL", 4, null, 0, "OUT", "NUMBER", overload: "2"),
        Arg("CUST_PKG", "COUNT_ALL", 4, "P_MIN", 1, "IN", "NUMBER", overload: "2"),
        Arg("CUST_PKG", "WITH_DEF", 6, "P_A", 1, "IN", "NUMBER"),
        Arg("CUST_PKG", "WITH_DEF", 6, "P_B", 2, "IN", "VARCHAR2", defaulted: "Y"),
        Arg("CUST_PKG", "GET_CUR", 7, "P_ID", 1, "IN", "NUMBER"),
        Arg("CUST_PKG", "GET_CUR", 7, "P_CUR", 2, "OUT", "REF CURSOR"),
        Arg(null, "STANDALONE_P", 1, "P_X", 1, "IN", "NUMBER"),
        Arg(null, "STANDALONE_F", 1, null, 0, "OUT", "VARCHAR2"),
    };

    private static OracleArgumentRow Header(string? package, string name, int subprogramId, string? overload = null, string objectType = "PACKAGE")
        => new(package, name, subprogramId, overload, null, null, null, null, null, null, null, null, null, null, null, null, null, null, IsHeader: true, ObjectType: objectType);

    private static OracleArgumentRow Arg(
        string? package,
        string name,
        int subprogramId,
        string? argumentName,
        int position,
        string? inOut,
        string? dataType,
        string? overload = null,
        string defaulted = "N")
        => new(package, name, subprogramId, overload, position, position + 1, argumentName, inOut, dataType, null, null, null, null, null, defaulted, null, null, null);
}

/// <summary>
/// Length-semantics tests for the Oracle DG007/DG008 detector (charset factor, CHAR caps, column/table lookup).
/// </summary>
public class OracleLengthSemanticsTests
{
    private readonly LengthMismatchDetector _detector = new();

    [Theory]
    [InlineData(300, false)] // 100 × 3 = 300 bytes fits exactly (boundary ==)
    [InlineData(299, true)] // one byte short
    public void Dg008_Al32Utf8_UsesThreeBytesPerUtf16Unit(int columnBytes, bool expectDg008)
    {
        var entity = Entity("CUSTOMERS", Property("Name", "NAME", 100));
        var columns = new[] { new ColumnDescriptor("NAME", "VARCHAR2", columnBytes, null, null, true, "B", columnBytes) };

        var violations = _detector.Detect(entity, columns, LengthSemantics.Byte, new OracleLengthContext("AL32UTF8", "AL16UTF16")).ToList();

        violations.Any(v => v.RuleId == "DG008").Should().Be(expectDg008);
        violations.Should().NotContain(v => v.RuleId == "DG007");
    }

    [Fact]
    public void Dg008_SingleByteCharset_NoFindingAtEqualLength()
    {
        var entity = Entity("CUSTOMERS", Property("Name", "NAME", 100));
        var columns = new[] { new ColumnDescriptor("NAME", "VARCHAR2", 100, null, null, true, "B", 100) };

        var violations = _detector.Detect(entity, columns, LengthSemantics.Byte, new OracleLengthContext("WE8MSWIN1252", "AL16UTF16"));

        violations.Should().BeEmpty();
    }

    [Fact]
    public void Dg008_ColumnCharsetWinsOverDatabaseCharset()
    {
        var entity = Entity("CUSTOMERS", Property("Name", "NAME", 100));
        var columns = new[] { new ColumnDescriptor("NAME", "VARCHAR2", 100, null, null, true, "B", 100, Charset: "WE8ISO8859P1") };

        _detector.Detect(entity, columns, LengthSemantics.Byte, new OracleLengthContext("AL32UTF8", null))
            .Should().NotContain(v => v.RuleId == "DG008");
    }

    [Fact]
    public void Dg008_IsUnicodeFalse_CountsOneBytePerCharacter()
    {
        var annotations = new Dictionary<string, object?> { ["IsUnicode"] = false };
        var entity = Entity("CUSTOMERS", Property("Code", "CODE", 100) with { Annotations = annotations });
        var columns = new[] { new ColumnDescriptor("CODE", "VARCHAR2", 100, null, null, true, "B", 100) };

        _detector.Detect(entity, columns, LengthSemantics.Byte, new OracleLengthContext("AL32UTF8", null))
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData("STANDARD", 1400, true)] // 1400 × 3 = 4200 > min(2000 × 4, 4000)
    [InlineData("STANDARD", 1333, false)] // 3999 ≤ 4000
    [InlineData("EXTENDED", 1400, false)] // 4200 ≤ min(8000, 32767)
    public void Dg008_CharSemantics_CappedByMaxStringSize(string maxStringSize, int entityMaxLength, bool expectDg008)
    {
        var entity = Entity("CUSTOMERS", Property("Bio", "BIO", entityMaxLength));
        var columns = new[] { new ColumnDescriptor("BIO", "VARCHAR2", 4000, null, null, true, "C", 2000) };

        var violations = _detector.Detect(entity, columns, LengthSemantics.Char, new OracleLengthContext("AL32UTF8", null, maxStringSize)).ToList();

        violations.Any(v => v.RuleId == "DG008").Should().Be(expectDg008);
        violations.Should().NotContain(v => v.RuleId == "DG007");
    }

    [Fact]
    public void Dg008_CharSemanticsCharColumn_CappedAt2000Bytes()
    {
        var entity = Entity("CUSTOMERS", Property("Code", "CODE", 700));
        var columns = new[] { new ColumnDescriptor("CODE", "CHAR", 2000, null, null, true, "C", 1000) };

        _detector.Detect(entity, columns, LengthSemantics.Char, new OracleLengthContext("AL32UTF8", null))
            .Should().Contain(v => v.RuleId == "DG008" && Convert.ToInt64(v.Properties!["columnMaxBytes"]) == 2000);
    }

    [Fact]
    public void ColumnLookup_CustomerIdMatchesUpperAndSnakeForms()
    {
        var entity = Entity("CUSTOMERS", Property("CustomerID", "CustomerID", 50));

        _detector.Detect(entity, new[] { new ColumnDescriptor("CUSTOMERID", "VARCHAR2", 20, null, null, true, "C", 20) }, LengthSemantics.Char)
            .Should().Contain(v => v.RuleId == "DG007");
        _detector.Detect(entity, new[] { new ColumnDescriptor("CUSTOMER_ID", "VARCHAR2", 20, null, null, true, "C", 20) }, LengthSemantics.Char)
            .Should().Contain(v => v.RuleId == "DG007");
        LengthMismatchDetector.ToUpperSnakeCase("CustomerID").Should().Be("CUSTOMER_ID");
        LengthMismatchDetector.ToUpperSnakeCase("HTMLBody").Should().Be("HTML_BODY");
        LengthMismatchDetector.ToUpperSnakeCase("FirstName").Should().Be("FIRST_NAME");
    }

    [Fact]
    public async Task Rule_SchemaQualifiedEntityTableResolvesAgainstOwnerTables()
    {
        var entity = Entity("SALES.CUSTOMERS", Property("Name", "NAME", 200));
        var schema = new OracleDatabaseSchemaDescriptor(
            "oracle:schema:SALES",
            new[]
            {
                new DatabaseTableDescriptor("CUSTOMERS", new[] { new ColumnDescriptor("NAME", "VARCHAR2", 100, null, null, true, "C", 100) }, "SALES"),
                new DatabaseTableDescriptor("CUSTOMERS", new[] { new ColumnDescriptor("NAME", "VARCHAR2", 500, null, null, true, "C", 500) }, "HR"),
            },
            "CHAR",
            "AL32UTF8",
            "AL16UTF16");

        var violations = await new LengthExceedsColumnRule().ValidateAsync(entity, new ContractDescriptor[] { entity, schema });

        violations.Should().ContainSingle(v => v.RuleId == "DG007" && (int)v.Properties!["columnMaxLength"]! == 100);
    }

    [Fact]
    public async Task Rule_UsesSchemaCharsetForByteRule()
    {
        var entity = Entity("CUSTOMERS", Property("Name", "NAME", 100));
        DatabaseSchemaDescriptor Schema(string charset) => new OracleDatabaseSchemaDescriptor(
            "oracle:schema:APP",
            new[] { new DatabaseTableDescriptor("CUSTOMERS", new[] { new ColumnDescriptor("NAME", "VARCHAR2", 200, null, null, true, "B", 200) }) },
            "BYTE",
            charset,
            null);

        (await new ByteLengthOverflowRiskRule().ValidateAsync(entity, new ContractDescriptor[] { entity, Schema("AL32UTF8") }))
            .Should().ContainSingle(v => v.RuleId == "DG008");
        (await new ByteLengthOverflowRiskRule().ValidateAsync(entity, new ContractDescriptor[] { entity, Schema("WE8MSWIN1252") }))
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData("AL32UTF8", true, 3)]
    [InlineData("UTF8", true, 3)]
    [InlineData("AL16UTF16", true, 2)]
    [InlineData("AL16UTF16", false, 2)]
    [InlineData("WE8MSWIN1252", true, 1)]
    [InlineData("US7ASCII", true, 1)]
    [InlineData("EE8ISO8859P2", true, 1)]
    [InlineData("ZHS16GBK", true, 2)]
    [InlineData("ZHS32GB18030", true, 4)]
    [InlineData(null, true, 3)]
    [InlineData("UNKNOWN", true, 3)]
    [InlineData("AL32UTF8", false, 1)]
    public void BytesPerUtf16Unit_FollowsCharset(string? charset, bool isUnicode, int expected)
    {
        OracleCharsets.BytesPerUtf16Unit(charset, isUnicode).Should().Be(expected);
    }

    private static EntityDescriptor Entity(string table, params PropertyDescriptor[] properties)
        => new("e1", "Customer", "Customer", table, properties);

    private static PropertyDescriptor Property(string name, string? column, int maxLength)
        => new(name, "System.String", column, "VARCHAR2", true, maxLength, false, false, null);
}
