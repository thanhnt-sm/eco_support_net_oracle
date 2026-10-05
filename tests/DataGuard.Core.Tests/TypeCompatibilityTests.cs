using DataGuard.Core.Rules;
using DataGuard.Core.Rules.TypeCompatibility;
using DataGuard.MySql.Adapter;
using DataGuard.Oracle.Adapter;
using DataGuard.PostgreSql.Adapter;
using DataGuard.SqlServer.Adapter;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

/// <summary>Phase 3.1 provider type tables (red-team H6): per-provider rows, CLR spellings and Unknown cases.</summary>
public class TypeCompatibilityTests
{
    private const TypeCompatibilityResult C = TypeCompatibilityResult.Compatible;
    private const TypeCompatibilityResult I = TypeCompatibilityResult.Incompatible;
    private const TypeCompatibilityResult U = TypeCompatibilityResult.Unknown;

    [Theory]
    [InlineData("int", "int", C)]
    [InlineData("System.Int32", "INT", C)]
    [InlineData("Nullable<int>", "int", C)]
    [InlineData("long", "int", I)]
    [InlineData("bool?", "bit", C)]
    [InlineData("enum:System.Byte", "tinyint", C)]
    [InlineData("decimal", "money", C)]
    [InlineData("double", "float", C)]
    [InlineData("float", "real", C)]
    [InlineData("string", "nvarchar(max)", C)]
    [InlineData("DateTime", "datetime2(7)", C)]
    [InlineData("DateOnly", "date", C)]
    [InlineData("DateTime", "date", C)]
    [InlineData("TimeOnly", "time", C)]
    [InlineData("DateTime", "time", I)]
    [InlineData("DateTimeOffset", "datetimeoffset", C)]
    [InlineData("Guid", "uniqueidentifier", C)]
    [InlineData("System.Byte[]", "varbinary(100)", C)]
    [InlineData("int", "varchar(50)", I)]
    [InlineData("int", "geography", U)]
    [InlineData("MyCompany.Money", "decimal", U)]
    [InlineData("object", "sql_variant", U)]
    public void SqlServer(string clr, string db, TypeCompatibilityResult expected) =>
        SqlServerTypeCompatibility.Instance.Check(clr, db).Should().Be(expected);

    [Theory]
    [InlineData("int", "NUMBER", null, null, C)]
    [InlineData("bool", "NUMBER", null, null, C)]
    [InlineData("bool", "NUMBER(1)", null, null, C)]
    [InlineData("bool", "NUMBER(10)", null, null, I)]
    [InlineData("System.Int32", "NUMBER(10)", null, null, C)]
    [InlineData("long?", "NUMBER", 19, 0, C)]
    [InlineData("int", "NUMBER(10,2)", null, null, I)]
    [InlineData("decimal", "NUMBER", 10, 2, C)]
    [InlineData("double", "BINARY_DOUBLE", null, null, C)]
    [InlineData("float", "BINARY_FLOAT", null, null, C)]
    [InlineData("string", "NVARCHAR2(50)", null, null, C)]
    [InlineData("string", "NUMBER", null, null, I)]
    [InlineData("DateTime", "DATE", null, null, C)]
    [InlineData("DateTimeOffset", "TIMESTAMP(6) WITH TIME ZONE", null, null, C)]
    [InlineData("DateTimeOffset", "TIMESTAMP", null, null, I)]
    [InlineData("Guid", "RAW(16)", null, null, C)]
    [InlineData("Guid", "RAW(32)", null, null, I)]
    [InlineData("byte[]", "BLOB", null, null, C)]
    [InlineData("bool", "BOOLEAN", null, null, C)]
    [InlineData("TimeSpan", "INTERVAL DAY(2) TO SECOND(6)", null, null, C)]
    [InlineData("int", "REF CURSOR", null, null, U)]
    [InlineData("string", "HR.ADDRESS_T", null, null, U)]
    [InlineData("enum:int", "PLS_INTEGER", null, null, C)]
    public void Oracle(string clr, string db, int? precision, int? scale, TypeCompatibilityResult expected) =>
        OracleTypeCompatibility.Instance.Check(clr, db, precision, scale).Should().Be(expected);

    [Theory]
    [InlineData("int", "integer", C)]
    [InlineData("int", "int4", C)]
    [InlineData("long", "integer", I)]
    [InlineData("System.Int64", "bigint", C)]
    [InlineData("short", "int2", C)]
    [InlineData("bool", "boolean", C)]
    [InlineData("int?", "bool", I)]
    [InlineData("decimal", "numeric(12,2)", C)]
    [InlineData("double", "double precision", C)]
    [InlineData("float", "real", C)]
    [InlineData("string", "character varying(100)", C)]
    [InlineData("string", "citext", C)]
    [InlineData("DateTime", "timestamp without time zone", C)]
    [InlineData("DateTimeOffset", "timestamptz", C)]
    [InlineData("DateTimeOffset", "timestamp", I)]
    [InlineData("DateOnly", "date", C)]
    [InlineData("TimeSpan", "interval", C)]
    [InlineData("Guid", "uuid", C)]
    [InlineData("byte[]", "bytea", C)]
    [InlineData("string", "jsonb", C)]
    [InlineData("int", "integer[]", U)]
    [InlineData("string", "mood_enum", U)]
    public void PostgreSql(string clr, string db, TypeCompatibilityResult expected) =>
        PostgreSqlTypeCompatibility.Instance.Check(clr, db).Should().Be(expected);

    [Theory]
    [InlineData("int", "int", C)]
    [InlineData("uint", "int unsigned", C)]
    [InlineData("long", "bigint(20)", C)]
    [InlineData("bool", "tinyint(1)", C)]
    [InlineData("bool", "tinyint", U)]
    [InlineData("bool", "tinyint(4)", I)]
    [InlineData("byte", "tinyint unsigned", C)]
    [InlineData("bool", "bit(1)", C)]
    [InlineData("decimal", "decimal(10,2)", C)]
    [InlineData("double", "double", C)]
    [InlineData("string", "longtext", C)]
    [InlineData("Guid", "char(36)", C)]
    [InlineData("Guid", "binary(16)", C)]
    [InlineData("Guid", "binary(8)", I)]
    [InlineData("DateTime", "datetime(6)", C)]
    [InlineData("TimeSpan", "time", C)]
    [InlineData("byte[]", "mediumblob", C)]
    [InlineData("string", "json", C)]
    [InlineData("int", "varchar(20)", I)]
    [InlineData("string", "geometry", U)]
    public void MySql(string clr, string db, TypeCompatibilityResult expected) =>
        MySqlTypeCompatibility.Instance.Check(clr, db).Should().Be(expected);

    [Theory]
    [InlineData("System.Int32", "int")]
    [InlineData("Int32", "int")]
    [InlineData("int?", "int")]
    [InlineData("System.Nullable<System.Int32>", "int")]
    [InlineData("global::System.Guid", "Guid")]
    [InlineData("System.Nullable`1[[System.DateTime, System.Private.CoreLib]]", "DateTime")]
    [InlineData("enum:System.Int16", "short")]
    [InlineData("byte[]", "byte[]")]
    [InlineData("System.Char[]", "string")]
    [InlineData("Single", "float")]
    [InlineData("MyEnum", null)]
    [InlineData("int[]", null)]
    [InlineData("", null)]
    public void ClrTypeNames_Normalize(string input, string? expected) =>
        ClrTypeNames.Normalize(input).Should().Be(expected);

    [Fact]
    public void DbTypeInfo_ParsesFacets()
    {
        DbTypeInfo.Parse("TIMESTAMP(6) WITH LOCAL TIME ZONE")!.Base.Should().Be("timestamp with local time zone");
        DbTypeInfo.Parse("NUMBER(*,0)").Should().BeEquivalentTo(new { Base = "number", Precision = (int?)null, Scale = (int?)0 });
        DbTypeInfo.Parse("tinyint(1) unsigned").Should().BeEquivalentTo(new { Base = "tinyint", Length = (int?)1, IsUnsigned = true });
        DbTypeInfo.Parse("nvarchar(max)")!.IsMax.Should().BeTrue();
        DbTypeInfo.Parse("NUMBER", precision: 10, scale: 2).Should().BeEquivalentTo(new { Precision = (int?)10, Scale = (int?)2 });
        DbTypeInfo.Parse("  ").Should().BeNull();
    }

    [Fact]
    public void Registry_FallsBackToUnknownWithoutRegistration_AndShimDelegates()
    {
        TypeCompatibilityRegistry.NormalizeProvider("postgres").Should().Be("postgresql");

        // Core has no SQL Server table any more (red-team A1): an unregistered provider answers Unknown, never another
        // dialect's mappings. "sqlite" is never registered by any test, so this is independent of test ordering.
        var unregistered = TypeCompatibilityRegistry.Resolve("sqlite");
        unregistered.Should().BeOfType<UnknownTypeCompatibility>();
        unregistered.Check("int", "nvarchar(50)").Should().Be(TypeCompatibilityResult.Unknown);
        unregistered.Check("Guid", "uniqueidentifier").Should().Be(TypeCompatibilityResult.Unknown);

        // The SQL Server table comes from the adapter; ProviderRuleCatalog (or a library caller) registers it.
        TypeCompatibilityRegistry.Register(SqlServerTypeCompatibility.Instance);
        TypeCompatibilityRegistry.Resolve(null).Should().BeSameAs(SqlServerTypeCompatibility.Instance);
        ParameterTypeMatchRule.IsTypeCompatible("Guid", "uniqueidentifier", isOracle: false).Should().BeTrue();
        ParameterTypeMatchRule.IsTypeCompatible("int", "NUMBER(10)", isOracle: true).Should().BeTrue();
        ParameterTypeMatchRule.IsTypeCompatible("MyEnum", "int", isOracle: false).Should().BeFalse();
    }
}
