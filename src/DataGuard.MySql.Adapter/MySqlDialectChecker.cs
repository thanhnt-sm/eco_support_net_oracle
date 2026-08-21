using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules;
using Microsoft.CodeAnalysis;

namespace DataGuard.MySql.Adapter;

/// <summary>
/// Detects MySQL-specific syntax in non-MySQL context, and vice versa.
/// </summary>
public sealed class MySqlDialectChecker
{
    private static readonly string[] MySqlOnly = { "ON DUPLICATE KEY", "REPLACE INTO", "`", "ENGINE=InnoDB", "AUTO_INCREMENT" };
    private static readonly string[] NonMySql = { "NVL", "TOP ", "ROWNUM", "GETDATE", "FETCH FIRST" };

    public IReadOnlyList<ContractViolation> CheckMySqlSyntaxInNonMySqlContext(string sqlText, bool isMySqlContext, Location? location = null)
    {
        var violations = new List<ContractViolation>();
        if (isMySqlContext)
        {
            return violations;
        }

        if (DataGuard.Core.Sources.SqlKeywordMatcher.ContainsAny(sqlText, MySqlOnly))
        {
            var matched = MySqlOnly.FirstOrDefault(k => sqlText.Contains(k, StringComparison.OrdinalIgnoreCase)) ?? "";
            violations.Add(new ContractViolation(
                "MY001",
                $"MySQL-specific syntax '{matched}' used in non-MySQL context",
                DiagnosticSeverity.Warning,
                location,
                new Dictionary<string, object?> { { "syntax", matched } }));
        }

        return violations;
    }

    public IReadOnlyList<ContractViolation> CheckNonMySqlSyntaxInMySqlContext(string sqlText, bool isMySqlContext, Location? location = null)
    {
        var violations = new List<ContractViolation>();
        if (!isMySqlContext)
        {
            return violations;
        }

        if (DataGuard.Core.Sources.SqlKeywordMatcher.ContainsAny(sqlText, NonMySql))
        {
            var matched = NonMySql.FirstOrDefault(k => sqlText.Contains(k, StringComparison.OrdinalIgnoreCase)) ?? "";
            violations.Add(new ContractViolation(
                "MY002",
                $"Non-MySQL syntax '{matched}' used in MySQL context",
                DiagnosticSeverity.Warning,
                location,
                new Dictionary<string, object?> { { "syntax", matched } }));
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
