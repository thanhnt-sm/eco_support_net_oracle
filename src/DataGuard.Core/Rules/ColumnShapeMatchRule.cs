using System.Text.RegularExpressions;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules.Sql;
using Microsoft.CodeAnalysis;

namespace DataGuard.Core.Rules;

/// <summary>
/// Rule: Result set columns must match entity properties.
/// </summary>
public class ColumnShapeMatchRule : ContractRuleBase
{
    public override string RuleId => "DG004";

    public override string Name => "Column Shape Match";

    public override DiagnosticSeverity Severity => DiagnosticSeverity.Error;

    public override string Description => "Result set columns must match entity properties";

    protected override async Task ValidateCoreAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        List<ContractViolation> violations,
        CancellationToken cancellationToken)
    {
        // Handle EntityDescriptor
        if (contract is EntityDescriptor entityDesc)
        {
            var entityPropertyNames = entityDesc.Properties
                .SelectMany(p => new[] { p.Name, p.ColumnName ?? string.Empty })
                .Where(n => n.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Handle RawSqlDescriptor for column extraction
            if (allContracts != null)
            {
                var sqlDescs = allContracts.OfType<RawSqlDescriptor>().ToList();
                var matchingSqlDescs = sqlDescs
                    .Where(s => !string.IsNullOrEmpty(s.TargetTypeName) &&
                                (string.Equals(s.TargetTypeName, entityDesc.Name, StringComparison.OrdinalIgnoreCase) ||
                                 s.TargetTypeName.EndsWith("." + entityDesc.Name, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                // If no query explicitly targets this entity, fallback to single untyped query only if exactly one query exists in allContracts
                if (matchingSqlDescs.Count == 0 && sqlDescs.Count == 1 && string.IsNullOrEmpty(sqlDescs[0].TargetTypeName))
                {
                    matchingSqlDescs.Add(sqlDescs[0]);
                }

                foreach (var sqlDesc in matchingSqlDescs)
                {
                    var columnNames = ExtractColumnNamesFromSql(sqlDesc.SqlText);

                    // If no columns could be extracted (SELECT *, expressions only), skip shape comparison.
                    if (columnNames.Count == 0)
                    {
                        continue;
                    }

                    // Check for missing required columns
                    var missingColumns = entityDesc.Properties
                        .Where(p => !columnNames.Contains(p.Name) &&
                                    (string.IsNullOrEmpty(p.ColumnName) ||
                                     !columnNames.Contains(p.ColumnName)))
                        .Select(p => p.Name)
                        .ToList();

                    // Take(5) is display text only; Properties carry the full list so baseline fingerprints never collide.
                    if (missingColumns.Count > 0)
                    {
                        violations.Add(CreateViolation(
                            RuleId,
                            $"Result set is missing required columns: {string.Join(", ", missingColumns.Take(5))}",
                            Severity,
                            properties: ShapeProperties("missing", entityDesc.Name, missingColumns, sqlDesc.SqlText)));
                    }

                    // Check for extra columns not mapped to entity
                    var extraColumns = columnNames.Where(c => !entityPropertyNames.Contains(c)).ToList();

                    if (extraColumns.Count > 0 && extraColumns.Count > entityPropertyNames.Count / 2)
                    {
                        violations.Add(CreateViolation(
                            RuleId,
                            $"Result set has {extraColumns.Count} extra columns not mapped to entity properties",
                            Severity,
                            properties: ShapeProperties("extra", entityDesc.Name, extraColumns, sqlDesc.SqlText)));
                    }
                }
            }
        }
        else if (contract is RawSqlDescriptor rawSql && rawSql.ExpectedProperties != null && rawSql.ExpectedProperties.Count > 0)
        {
            var columnNames = ExtractColumnNamesFromSql(rawSql.SqlText);
            if (columnNames.Count > 0)
            {
                var expectedPropertyNames = rawSql.ExpectedProperties
                    .SelectMany(p => new[] { p.Name, p.ColumnName ?? string.Empty })
                    .Where(n => n.Length > 0)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var missingColumns = rawSql.ExpectedProperties
                    .Where(p => !columnNames.Contains(p.Name) &&
                                (string.IsNullOrEmpty(p.ColumnName) || !columnNames.Contains(p.ColumnName)))
                    .Select(p => p.Name)
                    .ToList();

                if (missingColumns.Count > 0)
                {
                    violations.Add(CreateViolation(
                        RuleId,
                        $"Result set is missing required columns: {string.Join(", ", missingColumns.Take(5))}",
                        Severity,
                        rawSql.Location,
                        ShapeProperties("missing", rawSql.TargetTypeName, missingColumns, rawSql.SqlText)));
                }

                var extraColumns = columnNames.Where(c => !expectedPropertyNames.Contains(c)).ToList();
                if (extraColumns.Count > 0 && extraColumns.Count > expectedPropertyNames.Count / 2)
                {
                    violations.Add(CreateViolation(
                        RuleId,
                        $"Result set has {extraColumns.Count} extra columns not mapped to entity properties",
                        Severity,
                        rawSql.Location,
                        ShapeProperties("extra", rawSql.TargetTypeName, extraColumns, rawSql.SqlText)));
                }
            }
        }
    }

    /// <summary>
    /// Structured subject of a DG004 finding: the full, ordinal-sorted column list (never truncated), the entity and the
    /// SQL text hash, so two different column sets or two different queries never share a baseline fingerprint.
    /// </summary>
    internal static IReadOnlyDictionary<string, object?> ShapeProperties(string kind, string? entity, IEnumerable<string> columns, string? sqlText)
    {
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["kind"] = kind,
            ["columns"] = string.Join(",", columns.Distinct(StringComparer.Ordinal).OrderBy(column => column, StringComparer.Ordinal)),
        };
        if (!string.IsNullOrWhiteSpace(entity))
        {
            properties["entity"] = entity;
        }

        if (!string.IsNullOrWhiteSpace(sqlText))
        {
            properties["sqlHash"] = ComputeSqlHash(sqlText);
        }

        return properties;
    }

    /// <summary>
    /// Stable 16-hex SHA-256 prefix of SQL text with whitespace runs collapsed, so reformatting a query keeps its
    /// fingerprint while a different query gets a different one.
    /// </summary>
    internal static string ComputeSqlHash(string sqlText)
    {
        var normalized = Regex.Replace(sqlText ?? string.Empty, @"\s+", " ").Trim();
        var digest = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexStringLower(digest)[..16];
    }

    /// <summary>Text of the top-level <c>SELECT</c> list (up to <c>INTO</c>/<c>FROM</c>/<c>;</c>), or null; see <see cref="SqlTextScanner"/>.</summary>
    public static string? ExtractTopLevelSelectClause(string sql) => SqlTextScanner.ExtractTopLevelSelectClause(sql);

    /// <summary>Removes comments and masks string literals (identifiers are kept); see <see cref="SqlTextScanner"/>.</summary>
    public static string StripCommentsAndLiterals(string sql) => SqlTextScanner.StripCommentsAndLiterals(sql);

    /// <summary>True when a <c>/*</c> block comment is never closed outside literals; see <see cref="SqlTextScanner"/>.</summary>
    public static bool HasUnclosedBlockComment(string sql) => SqlTextScanner.HasUnclosedBlockComment(sql);

    /// <summary>Result column names of the top-level select list (empty for <c>SELECT *</c>); see <see cref="SqlTextScanner"/>.</summary>
    public static HashSet<string> ExtractColumnNamesFromSql(string sqlText) => SqlTextScanner.ExtractColumnNamesFromSql(sqlText);
}
