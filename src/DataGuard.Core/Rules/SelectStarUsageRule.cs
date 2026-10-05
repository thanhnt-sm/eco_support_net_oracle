using System.Text.RegularExpressions;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules.Sql;
using Microsoft.CodeAnalysis;

namespace DataGuard.Core.Rules;

/// <summary>
/// Rule: Avoid SELECT *; specify explicit columns to reduce bandwidth and enable shape validation.
/// </summary>
public class SelectStarUsageRule : ContractRuleBase
{
    public override string RuleId => "DG017";

    public override string Name => "Select Star Usage";

    public override DiagnosticSeverity Severity => DiagnosticSeverity.Warning;

    public override string Description => "Avoid SELECT *; specify explicit columns to reduce bandwidth and enable shape validation";

    protected override Task ValidateCoreAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        List<ContractViolation> violations,
        CancellationToken cancellationToken)
    {
        if (contract is RawSqlDescriptor sqlDesc)
        {
            var sqlText = sqlDesc.SqlText;
            if (string.IsNullOrEmpty(sqlText))
            {
                return Task.CompletedTask;
            }

            if (ContainsSelectStar(sqlText))
            {
                // The message is identical for every site; the SQL hash (and referenced tables) identify this one.
                var properties = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["sqlHash"] = ColumnShapeMatchRule.ComputeSqlHash(sqlText),
                };
                if (sqlDesc.ReferencedTables.Count > 0)
                {
                    properties["table"] = string.Join(",", sqlDesc.ReferencedTables.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(table => table, StringComparer.OrdinalIgnoreCase));
                }

                violations.Add(CreateViolation(
                    RuleId,
                    "Avoid SELECT *; specify explicit columns to reduce bandwidth and enable shape validation.",
                    Severity,
                    contract.Location,
                    properties));
            }
        }

        return Task.CompletedTask;
    }

    public static bool ContainsSelectStar(string sqlText)
    {
        if (string.IsNullOrWhiteSpace(sqlText))
        {
            return false;
        }

        var stripped = SqlTextScanner.StripCommentsAndLiterals(sqlText);
        var setBranches = SqlTextScanner.SplitTopLevelSetBranches(sqlText);
        if (setBranches.Count > 1)
        {
            foreach (var branch in setBranches)
            {
                if (!string.IsNullOrWhiteSpace(branch) && ContainsSelectStar(branch))
                {
                    return true;
                }
            }
            return false;
        }

        var selectClause = SqlTextScanner.ExtractTopLevelSelectClause(sqlText);
        if (string.IsNullOrWhiteSpace(selectClause))
        {
            return Regex.IsMatch(stripped, @"\bSELECT\s+(DISTINCT\s+|ALL\s+)?(?:(?:\w+|\[[^\]]+\]|""[^""]+""|`[^`]+`)\.)*\*", RegexOptions.IgnoreCase);
        }

        var items = SqlTextScanner.SplitTopLevelClauses(selectClause);
        foreach (var item in items)
        {
            var trimmed = SqlTextScanner.StripCommentsAndLiterals(item).Trim();
            if (trimmed == "*" || Regex.IsMatch(trimmed, @"^(?:(?:\[[^\]]+\]|""[^""]+""|`[^`]+`|\w+)\.)*\*$", RegexOptions.IgnoreCase))
            {
                return true;
            }

            if (trimmed.StartsWith("(", StringComparison.Ordinal) && trimmed.EndsWith(")", StringComparison.Ordinal))
            {
                var inner = trimmed.Substring(1, trimmed.Length - 2).Trim();
                if (inner.IndexOf("SELECT", StringComparison.OrdinalIgnoreCase) >= 0 && ContainsSelectStar(inner))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
