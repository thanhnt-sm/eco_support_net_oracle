using System.Text.RegularExpressions;
using DataGuard.Core.Abstractions;
using Microsoft.CodeAnalysis;

namespace DataGuard.Core.Rules;

/// <summary>
/// Detects table/column references in raw SQL that do not exist in the database schema
/// ("phantom identifiers" - a common AI hallucination failure mode).
/// Emits DG015 (phantom table) and DG016 (phantom column).
/// </summary>
public class PhantomIdentifierRule : ContractRuleBase
{
    public override string RuleId => "DG015";

    public override string Name => "Phantom Identifier Detection";

    public override DiagnosticSeverity Severity => DiagnosticSeverity.Error;

    public override string Description => "Raw SQL references a table or column that does not exist in the database schema";

    private static readonly Regex TableRefRegex = new (
        @"\b(?:FROM|JOIN)\s+([A-Za-z_][\w]*(?:\.[A-Za-z_][\w]*)?)(?:\s+(?:AS\s+)?([A-Za-z_][\w]*))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CteNameRegex = new (
        @"\bWITH\s+([A-Za-z_][\w]*)\s+AS\s*\(",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SimpleIdentifierRegex = new (
        @"^[A-Za-z_]\w*$",
        RegexOptions.Compiled);

    private static readonly Regex QualifiedColumnRegex = new (
        @"\b([A-Za-z_][\w]*)\.([A-Za-z_][\w]*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SelectListRegex = new (
        @"\bSELECT\s+(.+?)\s+FROM\b",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly HashSet<string> SqlKeywords = new (StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "FROM", "WHERE", "JOIN", "INNER", "LEFT", "RIGHT", "OUTER", "ON",
        "AND", "OR", "NOT", "NULL", "AS", "ORDER", "GROUP", "BY", "HAVING", "LIMIT",
        "OFFSET", "FETCH", "UNION", "DISTINCT", "INTO", "VALUES", "SET", "CASE",
        "WHEN", "THEN", "ELSE", "END", "EXISTS", "BETWEEN", "LIKE", "IN", "IS",
    };

    protected override Task ValidateCoreAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        List<ContractViolation> violations,
        CancellationToken cancellationToken)
    {
        if (contract is not RawSqlDescriptor rawSql || string.IsNullOrWhiteSpace(rawSql.SqlText))
        {
            return Task.CompletedTask;
        }

        var schema = allContracts.OfType<DatabaseSchemaDescriptor>().FirstOrDefault();
        if (schema == null || schema.Tables.Count == 0)
        {
            return Task.CompletedTask;
        }

        var sql = rawSql.SqlText;

        var tables = schema.Tables
            .Where(t => !string.IsNullOrEmpty(t.Name))
            .GroupBy(t => t.Name!.ToUpperInvariant(), StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.SelectMany(t => t.Columns).Select(c => c.Name.ToUpperInvariant()).ToHashSet(),
                StringComparer.Ordinal);

        // 0. Collect CTE names (WITH clause) so they are not reported as phantom tables.
        var cteNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in CteNameRegex.Matches(sql))
        {
            cteNames.Add(m.Groups[1].Value);
        }

        // 1. Table references.
        var tableRefs = new List<(string Table, string Alias)>();
        foreach (Match m in TableRefRegex.Matches(sql))
        {
            var table = m.Groups[1].Value;
            var alias = m.Groups[2].Value;

            // Strip schema qualifier: FROM dbo.Users -> table "Users".
            var dotIdx = table.LastIndexOf('.');
            if (dotIdx >= 0)
            {
                table = table[(dotIdx + 1) ..];
            }

            // A SQL keyword following the table name is not an alias.
            if (SqlKeywords.Contains(alias))
            {
                alias = string.Empty;
            }

            tableRefs.Add((table, alias));
        }

        foreach (var (table, _) in tableRefs)
        {
            if (cteNames.Contains(table))
            {
                continue;
            }

            if (!tables.ContainsKey(table.ToUpperInvariant()))
            {
                violations.Add(new ContractViolation(
                    "DG015",
                    $"Table '{table.ToUpperInvariant()}' does not exist in database",
                    Severity,
                    rawSql.Location,
                    new Dictionary<string, object?> { { "table", table.ToUpperInvariant() } }));
            }
        }

        // 2. Qualified column references (alias.column).
        foreach (Match m in QualifiedColumnRegex.Matches(sql))
        {
            var alias = m.Groups[1].Value;
            var column = m.Groups[2].Value;
            var entry = tableRefs.FirstOrDefault(t =>
                t.Alias.Equals(alias, StringComparison.OrdinalIgnoreCase) ||
                t.Table.Equals(alias, StringComparison.OrdinalIgnoreCase));
            if (entry == default || string.IsNullOrEmpty(entry.Table))
            {
                continue;
            }

            var upperTable = entry.Table.ToUpperInvariant();
            if (tables.TryGetValue(upperTable, out var cols) && !cols.Contains(column.ToUpperInvariant()))
            {
                violations.Add(new ContractViolation(
                    "DG016",
                    $"Column '{column.ToUpperInvariant()}' does not exist in table '{upperTable}'",
                    Severity,
                    rawSql.Location,
                    new Dictionary<string, object?> { { "column", column.ToUpperInvariant() }, { "table", upperTable } }));
            }
        }

        // 3. Unqualified columns in the SELECT list (checked against the primary table).
        var selectMatch = SelectListRegex.Match(sql);
        if (selectMatch.Success && tableRefs.Count > 0)
        {
            var primary = tableRefs[0].Table.ToUpperInvariant();
            if (tables.TryGetValue(primary, out var primaryCols))
            {
                foreach (var rawCol in selectMatch.Groups[1].Value.Split(','))
                {
                    var col = rawCol.Trim();
                    if (col.Length == 0 || col == "*" || col.Contains('.') || col.Contains('('))
                    {
                        continue;
                    }

                    // Skip literals and operators that indicate expressions.
                    if (col.Contains('+') || col.Contains('-') || col.Contains('*') || col.Contains('/'))
                    {
                        continue;
                    }

                    // Take the alias if present ("expr AS alias"), else the last whitespace token.
                    var asIdx = col.IndexOf(" AS ", StringComparison.OrdinalIgnoreCase);
                    var clean = asIdx >= 0 ? col[(asIdx + 4) ..].Trim() : col;

                    var tokens = clean.Split((char[] ?)null, StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length == 0)
                    {
                        continue;
                    }

                    var candidate = tokens[tokens.Length - 1];

                    // Only compare simple identifiers (skip keywords, literals, expressions).
                    if (!SimpleIdentifierRegex.IsMatch(candidate) || SqlKeywords.Contains(candidate))
                    {
                        continue;
                    }

                    if (!primaryCols.Contains(candidate.ToUpperInvariant()))
                    {
                        violations.Add(new ContractViolation(
                            "DG016",
                            $"Column '{candidate.ToUpperInvariant()}' does not exist in table '{primary}'",
                            Severity,
                            rawSql.Location,
                            new Dictionary<string, object?> { { "column", candidate.ToUpperInvariant() }, { "table", primary } }));
                    }
                }
            }
        }

        return Task.CompletedTask;
    }
}
