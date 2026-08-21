using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DataGuard.Core.Tests;

public class RulesEngineTests
{
    private static Task<IReadOnlyList<ContractViolation>> RunAsync(IContractRule rule, ContractDescriptor contract, params ContractDescriptor[] all)
        => rule.ValidateAsync(contract, all.ToList(), CancellationToken.None);

    private static RawSqlDescriptor RawSql(string sql, params ParameterDescriptor[] parameters)
        => new ("raw:1", sql, parameters, new List<ColumnDescriptor>());

    private static EntityDescriptor Entity(string name, params PropertyDescriptor[] properties)
        => new ($"entity:{name}", name, name, name, properties);

    private static PropertyDescriptor Prop(string name, string? column = null, IReadOnlyDictionary<string, object?>? annotations = null)
        => new (name, "string", column, "nvarchar", true, null, false, false, annotations);

    private static DatabaseSchemaDescriptor Schema(params (string table, string column, bool nullable)[] columns)
        => new ("schema:1",
            columns.GroupBy(c => c.table)
                .Select(g => new DatabaseTableDescriptor(g.Key, g.Select(c => new ColumnDescriptor(c.column, "nvarchar", 100, null, null, c.nullable, null)).ToList()))
                .ToList(),
            "CHAR");

    [Fact]
    public async Task ParameterCountRule_ExecWithoutParameters_Flags()
    {
        var violations = await RunAsync(new ParameterCountRule(), RawSql("EXEC dbo.GetCustomer"));
        violations.Should().ContainSingle().Which.RuleId.Should().Be("DG101");
    }

    [Fact]
    public async Task ParameterCountRule_PlainSelect_NoViolation()
    {
        var violations = await RunAsync(new ParameterCountRule(), RawSql("SELECT * FROM Orders"));
        violations.Should().BeEmpty();
    }

    [Fact]
    public async Task ParameterDirectionRule_OutputParameter_Flags()
    {
        var parameter = new ParameterDescriptor("OutValue", "nvarchar", ParameterDirection.Output, 100, null, null, false, 1)
        {
            CallSiteDirection = ParameterDirection.Input,
        };
        var violations = await RunAsync(new ParameterDirectionRule(), RawSql("EXEC dbo.GetCustomer @OutValue", parameter));
        violations.Should().ContainSingle().Which.RuleId.Should().Be("DG003");
    }

    [Fact]
    public async Task ParameterDirectionRule_InputParameter_NoViolation()
    {
        var parameter = new ParameterDescriptor("Id", "int", ParameterDirection.Input, null, null, null, false, 1)
        {
            CallSiteDirection = ParameterDirection.Input,
        };
        var violations = await RunAsync(new ParameterDirectionRule(), RawSql("EXEC dbo.GetCustomer @Id", parameter));
        violations.Should().BeEmpty();
    }

    [Fact]
    public async Task DG003_OutParamWithOutputCallSite_Passes()
    {
        var parameter = new ParameterDescriptor("OutValue", "nvarchar", ParameterDirection.Output, 100, null, null, false, 1)
        {
            CallSiteDirection = ParameterDirection.Output,
        };
        var violations = await RunAsync(new ParameterDirectionRule(), RawSql("EXEC dbo.GetCustomer @OutValue", parameter));
        violations.Should().BeEmpty();
    }

    [Fact]
    public async Task DG003_SkipsWhenNoCallSiteDirection()
    {
        var parameter = new ParameterDescriptor("OutValue", "nvarchar", ParameterDirection.Output, 100, null, null, false, 1);
        var violations = await RunAsync(new ParameterDirectionRule(), RawSql("EXEC dbo.GetCustomer @OutValue", parameter));
        violations.Should().BeEmpty();
    }

    [Fact]
    public async Task DG002_CatchesRealMismatch()
    {
        var parameter = new ParameterDescriptor("Id", "varchar", ParameterDirection.Input, null, null, null, false, 1)
        {
            ClrType = "int",
        };
        var violations = await RunAsync(new ParameterTypeMatchRule(), RawSql("EXEC dbo.GetCustomer @Id", parameter));
        violations.Should().ContainSingle().Which.RuleId.Should().Be("DG002");
    }

    [Fact]
    public async Task DG002_PassesCompatiblePair()
    {
        var sqlServer = new ParameterDescriptor("Id", "int", ParameterDirection.Input, null, null, null, false, 1)
        {
            ClrType = "int",
        };
        var oracle = new ParameterDescriptor("Id", "NUMBER", ParameterDirection.Input, null, null, null, false, 1)
        {
            ClrType = "int",
        };
        (await RunAsync(new ParameterTypeMatchRule(), RawSql("EXEC dbo.GetCustomer @Id", sqlServer))).Should().BeEmpty();
        (await RunAsync(new ParameterTypeMatchRule(), RawSql("EXEC p(:Id)", oracle))).Should().BeEmpty();
    }

    [Fact]
    public async Task DG002_SkipsWhenNoClrType()
    {
        var parameter = new ParameterDescriptor("Id", "varchar", ParameterDirection.Input, null, null, null, false, 1);
        var violations = await RunAsync(new ParameterTypeMatchRule(), RawSql("EXEC dbo.GetCustomer @Id", parameter));
        violations.Should().BeEmpty();
    }

    [Fact]
    public async Task DG002_NoSubstringFalsePositive()
    {
        var parameter = new ParameterDescriptor("Loc", "POINT", ParameterDirection.Input, null, null, null, false, 1)
        {
            ClrType = "int",
        };
        var violations = await RunAsync(new ParameterTypeMatchRule(), RawSql("EXEC dbo.GetPoint @Loc", parameter));
        violations.Should().ContainSingle().Which.RuleId.Should().Be("DG002");
    }

    [Fact]
    public async Task ColumnShapeMatchRule_MissingColumn_Flags()
    {
        var entity = Entity("Customer", Prop("Id"), Prop("Name"), Prop("Email"));
        var sql = RawSql("SELECT Id, Name FROM Customers");
        var violations = await RunAsync(new ColumnShapeMatchRule(), entity, entity, sql);
        violations.Should().ContainSingle(v => v.RuleId == "DG004" && v.Message.Contains("Email"));
    }

    [Fact]
    public async Task NullableMismatchRule_RequiredPropertyAgainstNullableColumn_Flags()
    {
        var entity = Entity("Customer", Prop("Name", "name", new Dictionary<string, object?> { ["Required"] = true }));
        var schema = Schema(("Customers", "name", true));
        var violations = await RunAsync(new NullableMismatchRule(), entity, entity, schema);
        violations.Should().ContainSingle().Which.RuleId.Should().Be("DG005");
    }

    [Fact]
    public async Task NamingConventionRule_MismatchedColumn_Flags()
    {
        var entity = Entity("Customer", Prop("CustomerName", "customer_name_typo"));
        var violations = await RunAsync(new NamingConventionRule(), entity);
        violations.Should().ContainSingle().Which.RuleId.Should().Be("DG006");
    }

    [Fact]
    public async Task PhantomIdentifierRule_UnknownTable_Flags()
    {
        var schema = Schema(("Orders", "Id", false));
        var sql = RawSql("SELECT Id FROM Ghost");
        var violations = await RunAsync(new PhantomIdentifierRule(), sql, sql, schema);
        violations.Should().Contain(v => v.RuleId == "DG015");
    }
}
