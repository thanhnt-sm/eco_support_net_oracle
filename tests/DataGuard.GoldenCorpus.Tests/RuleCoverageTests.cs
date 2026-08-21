using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules;
using DataGuard.MySql.Adapter;
using DataGuard.Oracle.Adapter;
using DataGuard.PostgreSql.Adapter;
using FluentAssertions;
using Xunit;

namespace DataGuard.GoldenCorpus.Tests;

/// <summary>
/// Per-rule coverage for rules that ship in production but had no test:
/// DG003/DG004/DG005, DG010-DG014, MY001-003, PG001-003.
/// </summary>
public class RuleCoverageTests
{
    private static async Task<List<ContractViolation>> RunAsync(IContractRule rule, ContractDescriptor contract, params ContractDescriptor[] all)
    {
        return (await rule.ValidateAsync(contract, all, CancellationToken.None)).ToList();
    }

    private static RawSqlDescriptor Raw(string sql, params ParameterDescriptor[] parameters) =>
        new($"raw:{Guid.NewGuid():N}", sql, parameters, new List<ColumnDescriptor>());

    // ---- DG003 Direction ----
    [Fact]
    public async Task Dg003_OutputParameter_Flags()
    {
        var raw = Raw("EXEC p @x", new ParameterDescriptor("x", "NUMBER", ParameterDirection.Output, null, null, null, false, 1) { CallSiteDirection = ParameterDirection.Input });
        var vs = await RunAsync(new ParameterDirectionRule(), raw, raw);
        vs.Should().Contain(v => v.RuleId == "DG003" && v.Message.Contains("x"));
    }

    [Fact]
    public async Task Dg003_RefCursorInput_NotFlagged()
    {
        var raw = Raw("EXEC p @c", new ParameterDescriptor("c", "REF CURSOR", ParameterDirection.Input, null, null, null, false, 1));
        var vs = await RunAsync(new ParameterDirectionRule(), raw, raw);
        vs.Should().BeEmpty();
    }

    // ---- DG004 Column shape ----
    [Fact]
    public async Task Dg004_MissingColumn_Flags()
    {
        var entity = new EntityDescriptor("e1", "Order", "Order", "ORDERS", new List<PropertyDescriptor>
        {
            new ("Id", "int", "ID", null, false, null, true, false),
            new ("Total", "decimal", "TOTAL", null, false, null, false, false),
        });
        var raw = Raw("SELECT id FROM orders");
        var vs = await RunAsync(new ColumnShapeMatchRule(), entity, entity, raw);
        vs.Should().Contain(v => v.RuleId == "DG004" && v.Message.Contains("Total"));
    }

    [Fact]
    public async Task Dg004_SnakeCaseColumnName_MatchesViaMappedName()
    {
        var entity = new EntityDescriptor("e1", "Customer", "Customer", "CUSTOMERS", new List<PropertyDescriptor>
        {
            new ("FullName", "string", "FULL_NAME", null, true, 100, false, false),
        });
        var raw = Raw("SELECT full_name FROM customers");
        var vs = await RunAsync(new ColumnShapeMatchRule(), entity, entity, raw);
        vs.Should().BeEmpty();
    }

    // ---- DG005 Nullable ----
    [Fact]
    public async Task Dg005_RequiredButNullable_Flags()
    {
        var entity = new EntityDescriptor("e1", "Customer", "Customer", "CUSTOMERS", new List<PropertyDescriptor>
        {
            new ("Name", "string", "NAME", null, true, 100, false, false,
                new Dictionary<string, object?> { ["Required"] = true }),
        });
        var schema = new DatabaseSchemaDescriptor("s1", new List<DatabaseTableDescriptor>
        {
            new ("CUSTOMERS", new List<ColumnDescriptor> { new ("NAME", "VARCHAR2", 100, null, null, true, "C", 100) }),
        }, "CHAR");
        var vs = await RunAsync(new NullableMismatchRule(), entity, entity, schema);
        vs.Should().Contain(v => v.RuleId == "DG005");
    }

    // ---- Oracle dialect rules DG010-DG014 ----
    [Fact]
    public async Task Dg010_DecodeInNonOracle_Flags()
    {
        var raw = Raw("SELECT DECODE(x, 1, 'a') FROM t");
        var vs = await RunAsync(new OracleSyntaxInNonOracleContextRule(), raw, raw);
        vs.Should().Contain(v => v.RuleId == "DG010");
    }

    [Fact]
    public async Task Dg010_StandardWindowFunction_NotFlagged()
    {
        var raw = Raw("SELECT ROW_NUMBER() OVER (PARTITION BY a ORDER BY b) FROM t");
        var vs = await RunAsync(new OracleSyntaxInNonOracleContextRule(), raw, raw);
        vs.Should().BeEmpty();
    }

    [Fact]
    public async Task Dg011_IsNullInOracle_Flags()
    {
        var raw = Raw("SELECT ISNULL(a, 0) FROM t");
        var vs = await RunAsync(new NonOracleFunctionInOracleContextRule(), raw, raw);
        vs.Should().Contain(v => v.RuleId == "DG011");
    }

    [Fact]
    public async Task Dg011_TopicWord_NotFlaggedAsTop()
    {
        var raw = Raw("SELECT TOPIC FROM t");
        var vs = await RunAsync(new NonOracleFunctionInOracleContextRule(), raw, raw);
        vs.Should().BeEmpty();
    }

    [Fact]
    public async Task Dg013_ExecLeakInOracle_Flags()
    {
        var raw = Raw("EXEC dbo.p");
        var vs = await RunAsync(new SqlServerSyntaxLeakRule(), raw, raw);
        vs.Should().Contain(v => v.RuleId == "DG013");
    }

    [Fact]
    public async Task Dg014_UnmappedTypeInOracle_Flags()
    {
        var raw = Raw("SELECT MONEY FROM t");
        var vs = await RunAsync(new RawSqlUnmappedTypeUsageRule(), raw, raw);
        vs.Should().Contain(v => v.RuleId == "DG014");
    }

    // ---- MySQL MY001-003 ----
    [Fact]
    public async Task My001_MySqlOnlySyntaxInNonMySql_Flags()
    {
        var raw = Raw("INSERT INTO t VALUES (1) ON DUPLICATE KEY UPDATE a=1");
        var vs = await RunAsync(new MySqlSyntaxRule(), raw, raw);
        vs.Should().Contain(v => v.RuleId == "MY001");
    }

    [Fact]
    public async Task My002_IsNullIsValidMySql_NotFlagged()
    {
        var raw = Raw("SELECT ISNULL(a, 0) FROM t");
        var vs = await RunAsync(new NonMySqlSyntaxRule(), raw, raw);
        vs.Should().BeEmpty();
    }

    [Fact]
    public async Task My003_LengthExceeds_Flags()
    {
        var entity = new EntityDescriptor("e1", "C", "C", "T", new List<PropertyDescriptor>
        {
            new ("Name", "string", "NAME", null, true, 200, false, false),
        });
        var schema = new DatabaseSchemaDescriptor("s1", new List<DatabaseTableDescriptor>
        {
            new ("T", new List<ColumnDescriptor> { new ("NAME", "VARCHAR", 100, null, null, true, null, null) }),
        }, "CHAR");
        var vs = await RunAsync(new MySqlLengthExceedsColumnRule(), entity, entity, schema);
        vs.Should().Contain(v => v.RuleId == "MY003");
    }

    // ---- PostgreSQL PG001-003 ----
    [Fact]
    public async Task Pg001_SpecialSerialInNonPg_Flags()
    {
        var raw = Raw("CREATE TABLE t (id SERIAL)");
        var vs = await RunAsync(new PostgreSqlSyntaxRule(), raw, raw);
        vs.Should().Contain(v => v.RuleId == "PG001");
    }

    [Fact]
    public async Task Pg002_IsNullIsValidPg_NotFlagged()
    {
        var raw = Raw("SELECT ISNULL(a, 0) FROM t");
        var vs = await RunAsync(new NonPostgreSqlSyntaxRule(), raw, raw);
        vs.Should().BeEmpty();
    }

    [Fact]
    public async Task Pg003_LengthExceeds_Flags()
    {
        var entity = new EntityDescriptor("e1", "C", "C", "T", new List<PropertyDescriptor>
        {
            new ("Name", "string", "NAME", null, true, 300, false, false),
        });
        var schema = new DatabaseSchemaDescriptor("s1", new List<DatabaseTableDescriptor>
        {
            new ("T", new List<ColumnDescriptor> { new ("NAME", "varchar", 150, null, null, true, null, null) }),
        }, "CHAR");
        var vs = await RunAsync(new PostgreSqlLengthExceedsColumnRule(), entity, entity, schema);
        vs.Should().Contain(v => v.RuleId == "PG003");
    }
}
