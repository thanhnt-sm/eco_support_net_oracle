using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules.Sql;
using Microsoft.CodeAnalysis;

namespace DataGuard.Core.Rules;

/// <summary>
/// Rule DG005: nullability must match between the database column and the mapped entity property.
/// The column is resolved by <c>(entity.TableName, property.ColumnName)</c> (schema-qualified table names are
/// resolved by full key first, then bare name); columns are never merged across tables. Property nullability is
/// <see cref="PropertyDescriptor.IsNullable"/>, overridden to non-nullable by a <c>Required</c> annotation.
/// </summary>
public class NullableMismatchRule : ContractRuleBase
{
    public override string RuleId => "DG005";

    public override string Name => "Nullable Match";

    public override DiagnosticSeverity Severity => DiagnosticSeverity.Warning;

    public override string Description => "Database column nullability should match the mapped entity property nullability";

    protected override Task ValidateCoreAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        List<ContractViolation> violations,
        CancellationToken cancellationToken)
    {
        if (contract is not EntityDescriptor entityDesc || string.IsNullOrWhiteSpace(entityDesc.TableName))
        {
            return Task.CompletedTask;
        }

        var schema = allContracts.OfType<DatabaseSchemaDescriptor>().FirstOrDefault();
        if (schema == null || schema.Tables.Count == 0)
        {
            return Task.CompletedTask;
        }

        var tableName = SchemaObjectName.Parse(entityDesc.TableName);
        var candidates = SchemaTableIndex.For(schema).Resolve(tableName.Schema, tableName.Name);
        if (candidates.Count != 1)
        {
            // Unknown table, or a bare name shared by several schemas: no single ground truth to compare against.
            return Task.CompletedTask;
        }

        var table = candidates[0];
        foreach (var prop in entityDesc.Properties)
        {
            if (string.IsNullOrEmpty(prop.ColumnName) ||
                !table.Columns.TryGetValue(SchemaObjectName.Canonical(prop.ColumnName), out var column))
            {
                continue;
            }

            var propertyIsNullable = prop.IsNullable && !IsRequired(prop);
            if (propertyIsNullable == column.IsNullable)
            {
                continue;
            }

            var message = propertyIsNullable
                ? $"Property '{entityDesc.Name}.{prop.Name}' is nullable but database column '{table.DisplayName}.{column.Name}' is NOT NULL; writing null will fail with a constraint violation"
                : $"Property '{entityDesc.Name}.{prop.Name}' is non-nullable but database column '{table.DisplayName}.{column.Name}' allows NULL; reading a NULL value will fail at runtime";
            violations.Add(CreateViolation(
                RuleId,
                message,
                Severity,
                entityDesc.Location,
                new Dictionary<string, object?>
                {
                    ["entity"] = entityDesc.Name,
                    ["property"] = prop.Name,
                    ["table"] = table.DisplayName,
                    ["column"] = column.Name,
                }));
        }

        return Task.CompletedTask;
    }

    private static bool IsRequired(PropertyDescriptor prop)
    {
        if (prop.Annotations is null || !prop.Annotations.TryGetValue("Required", out var value))
        {
            return false;
        }

        return value is not false && !(value is string text && bool.TryParse(text, out var parsed) && !parsed);
    }
}
