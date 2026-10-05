using DataGuard.Core.Abstractions;
using DataGuard.Core.Models;
using Microsoft.CodeAnalysis;

namespace DataGuard.Core.Rules;

/// <summary>
/// Rule: Naming convention between database columns and C# properties.
/// </summary>
public class NamingConventionRule : ContractRuleBase
{
    private readonly NamingConvention _convention;

    public override string RuleId => "DG006";

    public override string Name => "Naming Convention";

    public override DiagnosticSeverity Severity => DiagnosticSeverity.Info;

    public override string Description => "Database column names should follow naming convention vs C# properties";

    public NamingConventionRule(NamingConvention convention = NamingConvention.SnakeCaseToPascalCase)
    {
        _convention = convention;
    }

    protected override async Task ValidateCoreAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        List<ContractViolation> violations,
        CancellationToken cancellationToken)
    {
        // Handle EntityDescriptor
        if (contract is EntityDescriptor entityDesc)
        {
            foreach (var prop in entityDesc.Properties)
            {
                var pascalCaseName = ToPascalCase(prop.Name);
                var snakeCaseName = ToSnakeCase(prop.Name);

                var columnName = prop.ColumnName;
                if (string.IsNullOrEmpty(columnName))
                {
                    continue;
                }

                var matchesSnake = columnName.Equals(snakeCaseName, StringComparison.OrdinalIgnoreCase);
                var matchesPascal = columnName.Equals(pascalCaseName, StringComparison.OrdinalIgnoreCase);

                if (!matchesSnake && !matchesPascal)
                {
                    violations.Add(CreateViolation(
                        RuleId,
                        $"Property '{prop.Name}' (PascalCase: '{pascalCaseName}', snake_case: '{snakeCaseName}') doesn't match database column '{columnName}'",
                        Severity));
                }
            }
        }
    }

    public static string ToSnakeCase(string pascalCase)
        => DataGuard.Contracts.NameConventions.ToSnakeCase(pascalCase);

    public static string ToPascalCase(string snakeCase)
        => DataGuard.Contracts.NameConventions.ToPascalCase(snakeCase);
}
