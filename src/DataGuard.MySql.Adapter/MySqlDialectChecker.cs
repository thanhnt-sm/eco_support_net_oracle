using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules;
using Microsoft.CodeAnalysis;

namespace DataGuard.MySql.Adapter;

/// <summary>
/// Detects MySQL-specific syntax in non-MySQL context, and vice versa.
/// </summary>
public sealed class MySqlDialectChecker
{
    private static readonly string[] MySqlOnly = { "LIMIT", "ON DUPLICATE KEY", "REPLACE INTO", "`", "ENGINE=InnoDB", "AUTO_INCREMENT" };
    private static readonly string[] NonMySql = { "NVL", "ISNULL", "TOP ", "ROWNUM", "GETDATE", "FETCH FIRST", "SEQUENCE" };

    public IReadOnlyList<ContractViolation> CheckMySqlSyntaxInNonMySqlContext(string sqlText, bool isMySqlContext, Location? location = null)
    {
        var violations = new List<ContractViolation>();
        if (isMySqlContext) return violations;

        foreach (var syntax in MySqlOnly)
        {
            if (sqlText.Contains(syntax, StringComparison.OrdinalIgnoreCase))
            {
                violations.Add(new ContractViolation(
                    "MY001",
                    $"MySQL-specific syntax '{syntax}' used in non-MySQL context",
                    DiagnosticSeverity.Warning,
                    location,
                    new Dictionary<string, object?> { { "syntax", syntax } }));
            }
        }
        return violations;
    }

    public IReadOnlyList<ContractViolation> CheckNonMySqlSyntaxInMySqlContext(string sqlText, bool isMySqlContext, Location? location = null)
    {
        var violations = new List<ContractViolation>();
        if (!isMySqlContext) return violations;

        foreach (var syntax in NonMySql)
        {
            if (sqlText.Contains(syntax, StringComparison.OrdinalIgnoreCase))
            {
                violations.Add(new ContractViolation(
                    "MY002",
                    $"Non-MySQL syntax '{syntax}' used in MySQL context",
                    DiagnosticSeverity.Warning,
                    location,
                    new Dictionary<string, object?> { { "syntax", syntax } }));
            }
        }
        return violations;
    }
}

public class MySqlSyntaxRule : ContractRuleBase
{
    public override string RuleId => "MY001";
    public override string Name => "MySQL Syntax in Non-MySQL Context";
    public override DiagnosticSeverity Severity => DiagnosticSeverity.Warning;
    public override string Description => "MySQL-specific syntax detected in non-MySQL context";

    protected override Task ValidateCoreAsync(ContractDescriptor contract, IReadOnlyList<ContractDescriptor> allContracts, List<ContractViolation> violations, CancellationToken cancellationToken)
    {
        if (contract is RawSqlDescriptor rawSql)
        {
            violations.AddRange(new MySqlDialectChecker().CheckMySqlSyntaxInNonMySqlContext(rawSql.SqlText, false, contract.Location));
        }
        return Task.CompletedTask;
    }
}

public class NonMySqlSyntaxRule : ContractRuleBase
{
    public override string RuleId => "MY002";
    public override string Name => "Non-MySQL Syntax in MySQL Context";
    public override DiagnosticSeverity Severity => DiagnosticSeverity.Warning;
    public override string Description => "Non-MySQL syntax detected in MySQL context";

    protected override Task ValidateCoreAsync(ContractDescriptor contract, IReadOnlyList<ContractDescriptor> allContracts, List<ContractViolation> violations, CancellationToken cancellationToken)
    {
        if (contract is RawSqlDescriptor rawSql)
        {
            violations.AddRange(new MySqlDialectChecker().CheckNonMySqlSyntaxInMySqlContext(rawSql.SqlText, true, contract.Location));
        }
        return Task.CompletedTask;
    }
}
