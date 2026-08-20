using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules;
using Microsoft.CodeAnalysis;

namespace DataGuard.PostgreSql.Adapter;

/// <summary>
/// Detects PostgreSQL-specific syntax in non-PostgreSQL context, and vice versa.
/// </summary>
public sealed class PostgreSqlDialectChecker
{
    private static readonly string[] PostgreSqlOnly = { "SERIAL", "BIGSERIAL", "ILIKE", "::" };
    private static readonly string[] NonPostgreSql = { "NVL", "TOP ", "ROWNUM", "GETDATE", "CONVERT(", "DATEPART" };

    public IReadOnlyList<ContractViolation> CheckPostgreSqlSyntaxInNonPostgreSqlContext(string sqlText, bool isPostgreSqlContext, Location? location = null)
    {
        var violations = new List<ContractViolation>();
        if (isPostgreSqlContext) return violations;

        foreach (var syntax in PostgreSqlOnly)
        {
            if (sqlText.Contains(syntax, StringComparison.OrdinalIgnoreCase))
            {
                violations.Add(new ContractViolation(
                    "PG001",
                    $"PostgreSQL-specific syntax '{syntax}' used in non-PostgreSQL context",
                    DiagnosticSeverity.Warning,
                    location,
                    new Dictionary<string, object?> { { "syntax", syntax } }));
            }
        }
        return violations;
    }

    public IReadOnlyList<ContractViolation> CheckNonPostgreSqlSyntaxInPostgreSqlContext(string sqlText, bool isPostgreSqlContext, Location? location = null)
    {
        var violations = new List<ContractViolation>();
        if (!isPostgreSqlContext) return violations;

        foreach (var syntax in NonPostgreSql)
        {
            if (sqlText.Contains(syntax, StringComparison.OrdinalIgnoreCase))
            {
                violations.Add(new ContractViolation(
                    "PG002",
                    $"Non-PostgreSQL syntax '{syntax}' used in PostgreSQL context",
                    DiagnosticSeverity.Warning,
                    location,
                    new Dictionary<string, object?> { { "syntax", syntax } }));
            }
        }
        return violations;
    }
}

public class PostgreSqlSyntaxRule : ContractRuleBase
{
    public override string RuleId => "PG001";
    public override string Name => "PostgreSQL Syntax in Non-PostgreSQL Context";
    public override DiagnosticSeverity Severity => DiagnosticSeverity.Warning;
    public override string Description => "PostgreSQL-specific syntax detected in non-PostgreSQL context";

    protected override Task ValidateCoreAsync(ContractDescriptor contract, IReadOnlyList<ContractDescriptor> allContracts, List<ContractViolation> violations, CancellationToken cancellationToken)
    {
        if (contract is RawSqlDescriptor rawSql)
        {
            violations.AddRange(new PostgreSqlDialectChecker().CheckPostgreSqlSyntaxInNonPostgreSqlContext(rawSql.SqlText, false, contract.Location));
        }
        return Task.CompletedTask;
    }
}

public class NonPostgreSqlSyntaxRule : ContractRuleBase
{
    public override string RuleId => "PG002";
    public override string Name => "Non-PostgreSQL Syntax in PostgreSQL Context";
    public override DiagnosticSeverity Severity => DiagnosticSeverity.Warning;
    public override string Description => "Non-PostgreSQL syntax detected in PostgreSQL context";

    protected override Task ValidateCoreAsync(ContractDescriptor contract, IReadOnlyList<ContractDescriptor> allContracts, List<ContractViolation> violations, CancellationToken cancellationToken)
    {
        if (contract is RawSqlDescriptor rawSql)
        {
            violations.AddRange(new PostgreSqlDialectChecker().CheckNonPostgreSqlSyntaxInPostgreSqlContext(rawSql.SqlText, true, contract.Location));
        }
        return Task.CompletedTask;
    }
}
