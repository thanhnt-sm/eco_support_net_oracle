using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules;
using Microsoft.CodeAnalysis;

namespace DataGuard.PostgreSql.Adapter;

/// <summary>
/// Detects entity MaxLength exceeding PostgreSQL column character_maximum_length.
/// </summary>
public sealed class PostgreSqlLengthMismatchDetector
{
    public IEnumerable<ContractViolation> Detect(EntityDescriptor entity, IReadOnlyList<ColumnDescriptor> columns)
    {
        foreach (var property in entity.Properties)
        {
            var column = columns.FirstOrDefault(c =>
                string.Equals(c.Name, property.ColumnName, StringComparison.OrdinalIgnoreCase));
            if (column == null || !property.MaxLength.HasValue || !column.MaxLength.HasValue)
            {
                continue;
            }

            if (property.MaxLength.Value > column.MaxLength.Value)
            {
                yield return new ContractViolation(
                    "PG003",
                    $"Entity property '{property.Name}' MaxLength={property.MaxLength.Value} exceeds column '{column.Name}' length={column.MaxLength.Value}",
                    DiagnosticSeverity.Error,
                    null,
                    new Dictionary<string, object?>
                    {
                        { "property", property.Name },
                        { "entityMaxLength", property.MaxLength.Value },
                        { "columnMaxLength", column.MaxLength.Value },
                    });
            }
        }
    }
}

public class PostgreSqlLengthExceedsColumnRule : ContractRuleBase
{
    public override string RuleId => "PG003";

    public override string Name => "Entity Length Exceeds PostgreSQL Column Length";

    public override DiagnosticSeverity Severity => DiagnosticSeverity.Error;

    public override string Description => "Entity property MaxLength exceeds PostgreSQL column length";

    protected override Task ValidateCoreAsync(ContractDescriptor contract, IReadOnlyList<ContractDescriptor> allContracts, List<ContractViolation> violations, CancellationToken cancellationToken)
    {
        if (contract is EntityDescriptor entity && !string.IsNullOrEmpty(entity.TableName))
        {
            var schema = allContracts.OfType<DatabaseSchemaDescriptor>().FirstOrDefault();
            var table = schema?.Tables.FirstOrDefault(t => string.Equals(t.Name, entity.TableName, StringComparison.OrdinalIgnoreCase));
            if (table != null)
            {
                violations.AddRange(new PostgreSqlLengthMismatchDetector().Detect(entity, table.Columns));
            }
        }

        return Task.CompletedTask;
    }
}
