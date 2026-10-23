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

    protected override Task ValidateCoreAsync(
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
                cancellationToken.ThrowIfCancellationRequested();
                var columnName = prop.ColumnName;
                if (string.IsNullOrEmpty(columnName))
                {
                    continue;
                }

                if (_convention == NamingConvention.ExactMatch)
                {
                    // Exact means no naming transformation; retain the rule's case-insensitive identifier policy.
                    if (!columnName.Equals(prop.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        violations.Add(CreateViolation(
                            RuleId,
                            $"Property '{prop.Name}' doesn't match database column '{columnName}' under ExactMatch naming convention",
                            Severity,
                            entityDesc.Location));
                    }

                    continue;
                }

                var pascalCaseName = ToPascalCase(prop.Name);
                var snakeCaseName = ToSnakeCase(prop.Name);
                var matchesSnake = columnName.Equals(snakeCaseName, StringComparison.OrdinalIgnoreCase);
                var matchesPascal = columnName.Equals(pascalCaseName, StringComparison.OrdinalIgnoreCase);

                if (!matchesSnake && !matchesPascal)
                {
                    // The entity's own location (the Entity<T> call of a parsed ModelSnapshot): IDE hosts drop a result
                    // without one. The fingerprint includes the file path, so baselines of entity-derived DG006 change.
                    violations.Add(CreateViolation(
                        RuleId,
                        $"Property '{prop.Name}' (PascalCase: '{pascalCaseName}', snake_case: '{snakeCaseName}') doesn't match database column '{columnName}'",
                        Severity,
                        entityDesc.Location));
                }
            }
        }

        return Task.CompletedTask;
    }

    public static string ToSnakeCase(string pascalCase)
        => DataGuard.Contracts.NameConventions.ToSnakeCase(pascalCase);

    public static string ToPascalCase(string snakeCase)
        => DataGuard.Contracts.NameConventions.ToPascalCase(snakeCase);
}
