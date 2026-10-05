using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DataGuard.Core.Tests;

/// <summary>
/// DG005 (C5): nullability is read from <see cref="PropertyDescriptor.IsNullable"/>, the column is resolved per
/// <c>(entity table, column)</c> and never merged across tables, and both directions are reported.
/// </summary>
public class NullableMismatchRuleTests
{
    private static ColumnDescriptor Col(string name, bool nullable) => new(name, "NVARCHAR", 100, null, null, nullable, null);

    private static PropertyDescriptor Prop(string name, string column, bool nullable, bool required = false) =>
        new(
            name,
            "string",
            column,
            null,
            nullable,
            Annotations: required ? new Dictionary<string, object?> { ["Required"] = true } : null);

    private static EntityDescriptor Entity(string table, params PropertyDescriptor[] properties) =>
        new("entity:Customer", "Customer", "App.Customer", table, properties);

    private static DatabaseSchemaDescriptor Schema(params DatabaseTableDescriptor[] tables) => new("schema:1", tables, "CHAR");

    private static async Task<IReadOnlyList<ContractViolation>> RunAsync(EntityDescriptor entity, DatabaseSchemaDescriptor schema)
    {
        var rule = new NullableMismatchRule();
        var violations = await rule.ValidateAsync(entity, new ContractDescriptor[] { entity, schema });
        violations.Where(v => v.RuleId != rule.RuleId || v.Severity != DiagnosticSeverity.Warning).Should().BeEmpty();
        return violations;
    }

    [Fact]
    public async Task NullableProperty_NotNullColumn_Flags()
    {
        var schema = Schema(new DatabaseTableDescriptor("CUSTOMERS", new[] { Col("NAME", nullable: false) }));

        var violations = await RunAsync(Entity("CUSTOMERS", Prop("Name", "NAME", nullable: true)), schema);

        var violation = violations.Should().ContainSingle().Subject;
        violation.Message.Should().Contain("is nullable").And.Contain("is NOT NULL");
        violation.Properties.Should().Contain(new KeyValuePair<string, object?>("entity", "Customer"));
        violation.Properties.Should().Contain(new KeyValuePair<string, object?>("property", "Name"));
        violation.Properties.Should().Contain(new KeyValuePair<string, object?>("table", "CUSTOMERS"));
        violation.Properties.Should().Contain(new KeyValuePair<string, object?>("column", "NAME"));
    }

    [Fact]
    public async Task NonNullableProperty_NullableColumn_FlagsRuntimeRisk()
    {
        var schema = Schema(new DatabaseTableDescriptor("CUSTOMERS", new[] { Col("NAME", nullable: true) }));

        var violations = await RunAsync(Entity("CUSTOMERS", Prop("Name", "NAME", nullable: false)), schema);

        violations.Should().ContainSingle().Which.Message.Should().Contain("is non-nullable").And.Contain("allows NULL").And.Contain("runtime");
    }

    [Fact]
    public async Task RequiredAnnotation_OverridesNullableProperty()
    {
        var schema = Schema(new DatabaseTableDescriptor("CUSTOMERS", new[] { Col("NAME", nullable: true) }));

        var violations = await RunAsync(Entity("CUSTOMERS", Prop("Name", "NAME", nullable: true, required: true)), schema);

        violations.Should().ContainSingle().Which.Message.Should().Contain("allows NULL");
    }

    [Fact]
    public async Task NonNullablePrimaryKey_NotNullColumn_NotFlagged()
    {
        // C5 regression: every NOT NULL column (including PK Id) used to be reported as "property is nullable".
        var schema = Schema(new DatabaseTableDescriptor("CUSTOMERS", new[] { Col("ID", nullable: false) }));
        var id = new PropertyDescriptor("Id", "int", "ID", "NUMBER", IsNullable: false, IsPrimaryKey: true);

        (await RunAsync(Entity("CUSTOMERS", id), schema)).Should().BeEmpty();
    }

    [Fact]
    public async Task NullableProperty_NullableColumn_NotFlagged()
    {
        var schema = Schema(new DatabaseTableDescriptor("CUSTOMERS", new[] { Col("EMAIL", nullable: true) }));

        (await RunAsync(Entity("CUSTOMERS", Prop("Email", "EMAIL", nullable: true)), schema)).Should().BeEmpty();
    }

    [Fact]
    public async Task SameColumnNameInTwoTables_UsesEntityTableOnly()
    {
        // CUSTOMERS.NAME is NOT NULL; AUDIT_LOG.NAME is nullable and listed last (the old merged dictionary let it win).
        var schema = Schema(
            new DatabaseTableDescriptor("CUSTOMERS", new[] { Col("NAME", nullable: false) }),
            new DatabaseTableDescriptor("AUDIT_LOG", new[] { Col("NAME", nullable: true) }));

        (await RunAsync(Entity("CUSTOMERS", Prop("Name", "NAME", nullable: false)), schema)).Should().BeEmpty();

        var audit = await RunAsync(Entity("AUDIT_LOG", Prop("Name", "NAME", nullable: false)), schema);
        audit.Should().ContainSingle().Which.Properties!["table"].Should().Be("AUDIT_LOG");
    }

    [Fact]
    public async Task EntityTableWithSchemaPrefix_ResolvesExactSchemaFirst()
    {
        var schema = Schema(
            new DatabaseTableDescriptor("dbo.Orders", new[] { Col("Total", nullable: false) }),
            new DatabaseTableDescriptor("audit.Orders", new[] { Col("Total", nullable: true) }));

        var dbo = await RunAsync(Entity("dbo.Orders", Prop("Total", "Total", nullable: true)), schema);
        dbo.Should().ContainSingle().Which.Properties!["table"].Should().Be("DBO.ORDERS");

        (await RunAsync(Entity("[audit].[Orders]", Prop("Total", "Total", nullable: true)), schema)).Should().BeEmpty();

        // A bare name shared by two schemas is ambiguous: no single ground truth, no finding.
        (await RunAsync(Entity("Orders", Prop("Total", "Total", nullable: true)), schema)).Should().BeEmpty();
    }

    [Fact]
    public async Task BareEntityTable_ResolvesSchemaQualifiedCatalogKey()
    {
        var schema = Schema(new DatabaseTableDescriptor("dbo.Orders", new[] { Col("Total", nullable: false) }));

        var violations = await RunAsync(Entity("Orders", Prop("Total", "TOTAL", nullable: true)), schema);

        violations.Should().ContainSingle().Which.Properties!["column"].Should().Be("Total");
    }

    [Fact]
    public async Task UnknownTableOrColumn_NotFlagged()
    {
        var schema = Schema(new DatabaseTableDescriptor("CUSTOMERS", new[] { Col("NAME", nullable: false) }));

        (await RunAsync(Entity("SUPPLIERS", Prop("Name", "NAME", nullable: true)), schema)).Should().BeEmpty();
        (await RunAsync(Entity("CUSTOMERS", Prop("Phone", "PHONE", nullable: true)), schema)).Should().BeEmpty();
    }
}
