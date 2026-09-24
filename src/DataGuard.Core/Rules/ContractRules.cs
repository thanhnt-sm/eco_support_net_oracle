using System.Collections.Immutable;
using System.Text.RegularExpressions;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Models;
using Microsoft.CodeAnalysis;

namespace DataGuard.Core.Rules;

/// <summary>
/// Base class for contract rules.
/// </summary>
public abstract class ContractRuleBase : IContractRule
{
    public abstract string RuleId { get; }

    public abstract string Name { get; }

    public abstract DiagnosticSeverity Severity { get; }

    public abstract string Description { get; }

    public virtual async Task<IReadOnlyList<ContractViolation>> ValidateAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<ContractViolation>();
        await ValidateCoreAsync(contract, allContracts, violations, cancellationToken);
        return violations;
    }

    protected abstract Task ValidateCoreAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        List<ContractViolation> violations,
        CancellationToken cancellationToken);

    protected static ContractViolation CreateViolation(
        string ruleId,
        string message,
        DiagnosticSeverity severity,
        Location? location = null,
        IReadOnlyDictionary<string, object?>? properties = null)
    {
        return new ContractViolation(ruleId, message, severity, location, properties);
    }
}

/// <summary>
/// Rule: Parameter count must match between call site and stored procedure.
/// </summary>
public class ParameterCountRule : ContractRuleBase
{
    public override string RuleId => "DG101"; // engine-only id; DG001 is the IDE UnvalidatedSqlCall id

    public override string Name => "Parameter Count Match";

    public override DiagnosticSeverity Severity => DiagnosticSeverity.Error;

    public override string Description => "Stored procedure parameter count must match call site";

    protected override async Task ValidateCoreAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        List<ContractViolation> violations,
        CancellationToken cancellationToken)
    {
        // Handle RawSqlDescriptor which has SqlText
        if (contract is RawSqlDescriptor sqlDesc)
        {
            var sqlText = sqlDesc.SqlText;

            if (string.IsNullOrEmpty(sqlText))
            {
                return;
            }

            // Count parameters in SQL
            var paramMatches = Regex.Matches(sqlText, @"@\w+");
            var detectedCount = paramMatches.Count;

            // For stored procedures with EXEC prefix, validate
            if (sqlText.Trim().ToLower().StartsWith("exec ") || sqlText.Trim().ToLower().StartsWith("execute "))
            {
                if (detectedCount == 0)
                {
                    violations.Add(CreateViolation(
                        RuleId,
                        "Stored procedure call appears to have no parameters detected",
                        Severity));
                }
            }
        }
    }
}

/// <summary>
/// Rule: Parameter types must match between call site and stored procedure.
/// </summary>
public class ParameterTypeMatchRule : ContractRuleBase
{
    public override string RuleId => "DG002";

    public override string Name => "Parameter Type Match";

    public override DiagnosticSeverity Severity => DiagnosticSeverity.Error;

    public override string Description => "Parameter CLR types must match database types";

    private static readonly ImmutableDictionary<string, string[]> SqlServerTypeMap = ImmutableDictionary<string, string[]>.Empty
        .Add("int", new[] { "int" })
        .Add("long", new[] { "bigint" })
        .Add("short", new[] { "smallint" })
        .Add("byte", new[] { "tinyint" })
        .Add("bool", new[] { "bit" })
        .Add("decimal", new[] { "decimal", "numeric", "money", "smallmoney" })
        .Add("double", new[] { "float" })
        .Add("float", new[] { "real" })
        .Add("string", new[] { "nvarchar", "varchar", "nchar", "char", "ntext", "text" })
        .Add("DateTime", new[] { "datetime", "datetime2", "smalldatetime", "date", "time" })
        .Add("DateTimeOffset", new[] { "datetimeoffset" })
        .Add("Guid", new[] { "uniqueidentifier" })
        .Add("byte[]", new[] { "varbinary", "binary", "image" })
        .Add("TimeSpan", new[] { "time" });

    private static readonly ImmutableDictionary<string, string[]> OracleTypeMap = ImmutableDictionary<string, string[]>.Empty
        .Add("int", new[] { "NUMBER", "INTEGER", "INT" })
        .Add("long", new[] { "NUMBER", "BIGINT" })
        .Add("short", new[] { "NUMBER", "SMALLINT" })
        .Add("byte", new[] { "NUMBER" })
        .Add("bool", new[] { "NUMBER(1)" })
        .Add("decimal", new[] { "NUMBER", "DECIMAL", "NUMERIC" })
        .Add("double", new[] { "BINARY_DOUBLE", "FLOAT" })
        .Add("float", new[] { "BINARY_FLOAT" })
        .Add("string", new[] { "VARCHAR2", "NVARCHAR2", "CHAR", "NCHAR", "CLOB", "NCLOB" })
        .Add("DateTime", new[] { "DATE", "TIMESTAMP", "TIMESTAMP WITH TIME ZONE" })
        .Add("DateTimeOffset", new[] { "TIMESTAMP WITH TIME ZONE" })
        .Add("Guid", new[] { "RAW(16)" })
        .Add("byte[]", new[] { "RAW", "BLOB" });

    protected override async Task ValidateCoreAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        List<ContractViolation> violations,
        CancellationToken cancellationToken)
    {
        // Handle RawSqlDescriptor which has Parameters with DataType
        if (contract is RawSqlDescriptor sqlDesc)
        {
            var isOracle = sqlDesc.Parameters?.Any(p => p.DataType?.Contains("NUMBER", StringComparison.OrdinalIgnoreCase) == true) == true;

            foreach (var param in sqlDesc.Parameters ?? Array.Empty<ParameterDescriptor>())
            {
                // Only check when a real CLR type source is available (attribute or Roslyn call site).
                // Without one, checking would fabricate violations from inferred types.
                if (string.IsNullOrEmpty(param.ClrType))
                {
                    continue;
                }

                if (!IsTypeCompatible(param.ClrType, param.DataType, isOracle))
                {
                    violations.Add(CreateViolation(
                        RuleId,
                        $"Parameter '{param.Name}' has CLR type '{param.ClrType}' but database type '{param.DataType}' is not compatible",
                        Severity));
                }
            }
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Splits a database type string into normalized tokens: parentheses contents,
    /// whitespace and commas are separators, so "nvarchar(50)" → ["nvarchar", "50"].
    /// </summary>
    private static IEnumerable<string> TokenizeDbType(string dbType)
    {
        return dbType.Split(new[] { '(', ')', ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public static bool IsTypeCompatible(string clrType, string dbType, bool isOracle)
    {
        if (string.IsNullOrWhiteSpace(clrType) || string.IsNullOrWhiteSpace(dbType))
        {
            return false;
        }

        var map = isOracle ? OracleTypeMap : SqlServerTypeMap;
        if (!map.TryGetValue(clrType, out var compatibleDbTypes))
        {
            return false;
        }

        // Exact matching only - never substring ("POINT" must not match "int",
        // "CHART" must not match "char"). Two exact forms: a map entry equals one
        // type token ("NUMBER" in "NUMBER(10)"), or the whole entry equals the
        // whole db type with whitespace collapsed ("NUMBER(1)", "RAW(16)",
        // "TIMESTAMP WITH TIME ZONE").
        var tokens = TokenizeDbType(dbType).ToList();
        var full = CollapseWhitespace(dbType);
        return compatibleDbTypes.Any(t =>
            tokens.Contains(t, StringComparer.OrdinalIgnoreCase) ||
            string.Equals(CollapseWhitespace(t), full, StringComparison.OrdinalIgnoreCase));
    }

    private static string CollapseWhitespace(string value) =>
        string.Concat(value.Where(c => !char.IsWhiteSpace(c)));
}

/// <summary>
/// Rule: Parameter direction must match (IN/OUT/INOUT ↔ in/out/ref).
/// </summary>
public class ParameterDirectionRule : ContractRuleBase
{
    public override string RuleId => "DG003";

    public override string Name => "Parameter Direction Match";

    public override DiagnosticSeverity Severity => DiagnosticSeverity.Error;

    public override string Description => "Parameter direction must match call site (in/out/ref)";

    protected override async Task ValidateCoreAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        List<ContractViolation> violations,
        CancellationToken cancellationToken)
    {
        // Handle RawSqlDescriptor which has Parameters with Direction
        if (contract is RawSqlDescriptor sqlDesc)
        {
            foreach (var param in sqlDesc.Parameters ?? Array.Empty<ParameterDescriptor>())
            {
                // Only check when call-site direction is known; without a call site
                // the rule cannot decide and must not flag unconditionally.
                if (param.CallSiteDirection is null)
                {
                    continue;
                }

                // Flag only when the SP requires out/ref but the call site is input-only.
                var requiresOutAtCallSite = param.Direction is ParameterDirection.Output
                    or ParameterDirection.InputOutput
                    or ParameterDirection.ReturnValue;
                var callSiteIsInputOnly = param.CallSiteDirection == ParameterDirection.Input;

                if (requiresOutAtCallSite && callSiteIsInputOnly)
                {
                    violations.Add(CreateViolation(
                        RuleId,
                        $"Parameter '{param.Name}' is {param.Direction} but call site passes it as {param.CallSiteDirection} (out/ref required)",
                        Severity));
                }
            }
        }
    }
}

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

                    if (missingColumns.Count > 0)
                    {
                        violations.Add(CreateViolation(
                            RuleId,
                            $"Result set is missing required columns: {string.Join(", ", missingColumns.Take(5))}",
                            Severity));
                    }

                    // Check for extra columns not mapped to entity
                    var extraColumns = columnNames.Where(c => !entityPropertyNames.Contains(c)).ToList();

                    if (extraColumns.Count > 0 && extraColumns.Count > entityPropertyNames.Count / 2)
                    {
                        violations.Add(CreateViolation(
                            RuleId,
                            $"Result set has {extraColumns.Count} extra columns not mapped to entity properties",
                            Severity));
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
                        rawSql.Location));
                }

                var extraColumns = columnNames.Where(c => !expectedPropertyNames.Contains(c)).ToList();
                if (extraColumns.Count > 0 && extraColumns.Count > expectedPropertyNames.Count / 2)
                {
                    violations.Add(CreateViolation(
                        RuleId,
                        $"Result set has {extraColumns.Count} extra columns not mapped to entity properties",
                        Severity,
                        rawSql.Location));
                }
            }
        }
    }

    private static readonly char[] LineEndings = { '\r', '\n', '\u0085', '\u2028', '\u2029' };

    public static string? ExtractTopLevelSelectClause(string sql)
    {
        var depth = 0;
        var inSelect = false;
        var selectStartIndex = -1;
        var intoStartIndex = -1;
        char inQuote = '\0';
        for (var i = 0; i < sql.Length; i++)
        {
            var ch = sql[i];

            if (inQuote != '\0')
            {
                if (inQuote == '[')
                {
                    if (ch == ']')
                    {
                        if (i + 1 < sql.Length && sql[i + 1] == ']')
                        {
                            i++;
                        }
                        else
                        {
                            inQuote = '\0';
                        }
                    }
                }
                else if (inQuote == '\'')
                {
                    if (ch == '\'')
                    {
                        if (i + 1 < sql.Length && sql[i + 1] == '\'')
                        {
                            i++;
                        }
                        else
                        {
                            inQuote = '\0';
                        }
                    }
                }
                else if (inQuote == '"')
                {
                    if (ch == '"')
                    {
                        if (i + 1 < sql.Length && sql[i + 1] == '"')
                        {
                            i++;
                        }
                        else
                        {
                            inQuote = '\0';
                        }
                    }
                }
                else if (inQuote == '`')
                {
                    if (ch == '`')
                    {
                        if (i + 1 < sql.Length && sql[i + 1] == '`')
                        {
                            i++;
                        }
                        else
                        {
                            inQuote = '\0';
                        }
                    }
                }
                else if (ch == inQuote)
                {
                    inQuote = '\0';
                }
                continue;
            }

            if (ch == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                var nextNewline = sql.IndexOfAny(LineEndings, i + 2);
                if (nextNewline == -1)
                {
                    break;
                }

                i = nextNewline;
                continue;
            }
            if (ch == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                var commentDepth = 1;
                var j = i + 2;
                while (j < sql.Length && commentDepth > 0)
                {
                    if (sql[j] == '/' && j + 1 < sql.Length && sql[j + 1] == '*')
                    {
                        commentDepth++;
                        j += 2;
                    }
                    else if (sql[j] == '*' && j + 1 < sql.Length && sql[j + 1] == '/')
                    {
                        commentDepth--;
                        j += 2;
                    }
                    else
                    {
                        j++;
                    }
                }

                if (commentDepth > 0)
                {
                    break;
                }

                i = j - 1;
                continue;
            }

            if ((ch == 'q' || ch == 'Q') && i + 2 < sql.Length && sql[i + 1] == '\'')
            {
                var openDelim = sql[i + 2];
                var closeDelim = openDelim switch
                {
                    '[' => ']',
                    '{' => '}',
                    '(' => ')',
                    '<' => '>',
                    _ => openDelim
                };
                var target = closeDelim + "'";
                var end = sql.IndexOf(target, i + 3, StringComparison.Ordinal);
                if (end >= 0)
                {
                    i = end + 1;
                    continue;
                }
            }
            if (ch == '"' || ch == '`' || ch == '[' || ch == '\'')
            {
                inQuote = ch;
                continue;
            }

            if (ch == '(')
            {
                depth++;
                continue;
            }
            if (ch == ')')
            {
                if (depth > 0)
                {
                    depth--;
                }
                continue;
            }

            if (depth == 0)
            {
                if (!inSelect)
                {
                    if ((i == 0 || (!char.IsLetterOrDigit(sql[i - 1]) && sql[i - 1] != '_')) &&
                        i + 6 <= sql.Length &&
                        string.Equals(sql.Substring(i, 6), "SELECT", StringComparison.OrdinalIgnoreCase) &&
                        (i + 6 == sql.Length || (!char.IsLetterOrDigit(sql[i + 6]) && sql[i + 6] != '_')))
                    {
                        inSelect = true;
                        i += 6;
                        while (i < sql.Length && char.IsWhiteSpace(sql[i]))
                        {
                            i++;
                        }
                        selectStartIndex = i;
                        i--; // loop will increment
                    }
                }
                else
                {
                    if (intoStartIndex == -1 &&
                        (i == 0 || (!char.IsLetterOrDigit(sql[i - 1]) && sql[i - 1] != '_')) &&
                        i + 4 <= sql.Length &&
                        string.Equals(sql.Substring(i, 4), "INTO", StringComparison.OrdinalIgnoreCase) &&
                        (i + 4 == sql.Length || (!char.IsLetterOrDigit(sql[i + 4]) && sql[i + 4] != '_')))
                    {
                        intoStartIndex = i;
                    }

                    if ((i == 0 || (!char.IsLetterOrDigit(sql[i - 1]) && sql[i - 1] != '_')) &&
                        i + 4 <= sql.Length &&
                        string.Equals(sql.Substring(i, 4), "FROM", StringComparison.OrdinalIgnoreCase) &&
                        (i + 4 == sql.Length || (!char.IsLetterOrDigit(sql[i + 4]) && sql[i + 4] != '_')))
                    {
                        var endIndex = intoStartIndex >= 0 ? intoStartIndex : i;
                        return sql.Substring(selectStartIndex, endIndex - selectStartIndex).Trim();
                    }
                    if (ch == ';')
                    {
                        var endIndex = intoStartIndex >= 0 ? intoStartIndex : i;
                        return sql.Substring(selectStartIndex, endIndex - selectStartIndex).Trim();
                    }
                }
            }
        }

        if (inSelect && selectStartIndex >= 0 && selectStartIndex <= sql.Length)
        {
            var endIndex = intoStartIndex >= 0 ? intoStartIndex : sql.Length;
            return sql.Substring(selectStartIndex, endIndex - selectStartIndex).Trim();
        }

        return null;
    }

    public static string StripCommentsAndLiterals(string sql)
    {
        if (string.IsNullOrEmpty(sql))
        {
            return string.Empty;
        }
        var sb = new System.Text.StringBuilder(sql.Length);
        var inQuote = '\0';
        for (var i = 0; i < sql.Length; i++)
        {
            var ch = sql[i];
            if (inQuote != '\0')
            {
                if (ch == inQuote)
                {
                    if (inQuote == ']' && i + 1 < sql.Length && sql[i + 1] == ']')
                    {
                        sb.Append("]]");
                        i++; // skip escaped bracket ]]
                    }
                    else if (inQuote == '`' && i + 1 < sql.Length && sql[i + 1] == '`')
                    {
                        sb.Append("``");
                        i++; // skip escaped backtick ``
                    }
                    else if (inQuote == '"' && i + 1 < sql.Length && sql[i + 1] == '"')
                    {
                        sb.Append("\"\"");
                        i++; // skip escaped double-quote ""
                    }
                    else if (inQuote != ']' && inQuote != '`' && inQuote != '"' && i + 1 < sql.Length && sql[i + 1] == inQuote)
                    {
                        i++; // skip escaped quote
                    }
                    else
                    {
                        if (inQuote == ']' || inQuote == '`' || inQuote == '"')
                        {
                            sb.Append(inQuote);
                        }
                        else
                        {
                            sb.Append("''");
                        }
                        inQuote = '\0';
                    }
                }
                else if (inQuote == ']' || inQuote == '`' || inQuote == '"')
                {
                    sb.Append(ch); // preserve characters inside [bracket identifier], `backtick identifier`, and "quoted identifier"
                }
                continue;
            }

            if (ch == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                var nl = sql.IndexOfAny(LineEndings, i + 2);
                if (nl < 0)
                {
                    break;
                }
                i = nl;
                sb.Append('\n');
                continue;
            }

            if (ch == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                var commentDepth = 1;
                var j = i + 2;
                while (j < sql.Length && commentDepth > 0)
                {
                    if (sql[j] == '/' && j + 1 < sql.Length && sql[j + 1] == '*')
                    {
                        commentDepth++;
                        j += 2;
                    }
                    else if (sql[j] == '*' && j + 1 < sql.Length && sql[j + 1] == '/')
                    {
                        commentDepth--;
                        j += 2;
                    }
                    else
                    {
                        j++;
                    }
                }

                if (commentDepth > 0)
                {
                    break;
                }

                i = j - 1;
                sb.Append(' ');
                continue;
            }

            if (ch == '$')
            {
                var m = System.Text.RegularExpressions.Regex.Match(sql.Substring(i), @"^\$([A-Za-z0-9_]*)\$");
                if (m.Success)
                {
                    var tag = m.Value;
                    var end = sql.IndexOf(tag, i + tag.Length, StringComparison.Ordinal);
                    if (end >= 0)
                    {
                        i = end + tag.Length - 1;
                        sb.Append("''");
                        continue;
                    }
                }
            }
            if ((ch == 'q' || ch == 'Q') && i + 2 < sql.Length && sql[i + 1] == '\'')
            {
                var openDelim = sql[i + 2];
                var closeDelim = openDelim switch
                {
                    '[' => ']',
                    '{' => '}',
                    '(' => ')',
                    '<' => '>',
                    _ => openDelim
                };
                var target = closeDelim + "'";
                var end = sql.IndexOf(target, i + 3, StringComparison.Ordinal);
                if (end >= 0)
                {
                    i = end + 1;
                    sb.Append("''");
                    continue;
                }
            }

            if (ch == '\'')
            {
                inQuote = '\'';
                continue;
            }

            if (ch == '"')
            {
                inQuote = '"';
                sb.Append('"');
                continue;
            }

            if (ch == '[')
            {
                inQuote = ']';
                sb.Append('[');
                continue;
            }

            if (ch == '`')
            {
                inQuote = '`';
                sb.Append('`');
                continue;
            }

            sb.Append(ch);
        }
        return sb.ToString();
    }

    public static bool HasUnclosedBlockComment(string sql)
    {
        if (string.IsNullOrEmpty(sql))
        {
            return false;
        }

        var inQuote = '\0';
        for (var i = 0; i < sql.Length; i++)
        {
            var ch = sql[i];
            if (inQuote != '\0')
            {
                if (ch == inQuote)
                {
                    if (inQuote == ']' && i + 1 < sql.Length && sql[i + 1] == ']')
                    {
                        i++;
                    }
                    else if (inQuote != ']' && inQuote != '`' && inQuote != '"' && i + 1 < sql.Length && sql[i + 1] == inQuote)
                    {
                        i++;
                    }
                    else
                    {
                        inQuote = '\0';
                    }
                }
                continue;
            }

            if ((ch == 'q' || ch == 'Q') && i + 2 < sql.Length && sql[i + 1] == '\'')
            {
                var openDelim = sql[i + 2];
                var closeDelim = openDelim switch
                {
                    '[' => ']',
                    '{' => '}',
                    '(' => ')',
                    '<' => '>',
                    _ => openDelim
                };
                var target = closeDelim + "'";
                var end = sql.IndexOf(target, i + 3, StringComparison.Ordinal);
                if (end >= 0)
                {
                    i = end + 1;
                    continue;
                }
                else
                {
                    break;
                }
            }

            if (ch == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                var nl = sql.IndexOfAny(LineEndings, i + 2);
                if (nl < 0)
                {
                    break;
                }
                i = nl;
                continue;
            }

            if (ch == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                var commentDepth = 1;
                var j = i + 2;
                while (j < sql.Length && commentDepth > 0)
                {
                    if (sql[j] == '/' && j + 1 < sql.Length && sql[j + 1] == '*')
                    {
                        commentDepth++;
                        j += 2;
                    }
                    else if (sql[j] == '*' && j + 1 < sql.Length && sql[j + 1] == '/')
                    {
                        commentDepth--;
                        j += 2;
                    }
                    else
                    {
                        j++;
                    }
                }

                if (commentDepth > 0)
                {
                    return true;
                }

                i = j - 1;
                continue;
            }

            if (ch == '$')
            {
                var m = System.Text.RegularExpressions.Regex.Match(sql.Substring(i), @"^\$([A-Za-z0-9_]*)\$");
                if (m.Success)
                {
                    var tag = m.Value;
                    var end = sql.IndexOf(tag, i + tag.Length, StringComparison.Ordinal);
                    if (end >= 0)
                    {
                        i = end + tag.Length - 1;
                        continue;
                    }
                }
            }

            if (ch == '\'' || ch == '"' || ch == '`')
            {
                inQuote = ch;
                continue;
            }

            if (ch == '[')
            {
                inQuote = ']';
                continue;
            }
        }

        return false;
    }

    public static HashSet<string> ExtractColumnNamesFromSql(string sqlText)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(sqlText))
        {
            return columns;
        }

        if (SelectStarUsageRule.ContainsSelectStar(sqlText))
        {
            return columns; // SELECT *: column list is unknown, skip shape comparison.
        }

        var selectClause = ExtractTopLevelSelectClause(sqlText);
        if (selectClause == null)
        {
            var maskedSql = DataGuard.Core.Sources.ProjectCSharpSqlSource.MaskSqlStringLiterals(sqlText);
            selectClause = ExtractTopLevelSelectClause(maskedSql);
            if (selectClause == null)
            {
                return columns;
            }
        }
        foreach (var clause in SplitTopLevelClauses(selectClause))
        {
            var trimmed = Regex.Replace(clause, @"--[^\r\n]*", " ");
            trimmed = Regex.Replace(trimmed, @"/\*[\s\S]*?\*/", " ").Trim();
            if (trimmed.Length == 0 || trimmed == "*" || trimmed.EndsWith(".*", StringComparison.Ordinal))
            {
                continue;
            }

            string? rawColumn = null;
            var aliasAssignMatch = Regex.Match(trimmed, @"^\s*((?:\[(?:[^\]]|\]\])+\]|""[^""]+""|`[^`]+`|[A-Za-z0-9_]+))\s*=\s*(?!=)");
            var asMatch = Regex.Match(trimmed, @"\bAS\s+((?:'[^']+'|""[^""]+""|\[(?:[^\]]|\]\])+\]|`[^`]+`|[A-Za-z0-9_]+))\s*$", RegexOptions.IgnoreCase);
            if (aliasAssignMatch.Success)
            {
                rawColumn = aliasAssignMatch.Groups[1].Value;
            }
            else if (asMatch.Success)
            {
                rawColumn = asMatch.Groups[1].Value;
            }
            else if (trimmed.EndsWith("]"))
            {
                var openBracket = -1;
                for (var i = trimmed.Length - 1; i >= 0; i--)
                {
                    if (trimmed[i] == ']' && i > 0 && trimmed[i - 1] == ']')
                    {
                        i--; // skip escaped ]]
                        continue;
                    }
                    if (trimmed[i] == '[')
                    {
                        openBracket = i;
                        break;
                    }
                }

                if (openBracket > 0)
                {
                    var prefix = trimmed.Substring(0, openBracket).TrimEnd();
                    if (!string.IsNullOrEmpty(prefix) && (prefix.EndsWith("+") || prefix.EndsWith("-") || prefix.EndsWith("*") || prefix.EndsWith("/") || prefix.EndsWith("%")))
                    {
                        rawColumn = trimmed;
                    }
                    else
                    {
                        rawColumn = trimmed.Substring(openBracket);
                    }
                }
                else
                {
                    rawColumn = trimmed;
                }
            }
            else if (trimmed.Length >= 2 && trimmed.EndsWith("\""))
            {
                var openQuote = -1;
                for (var i = trimmed.Length - 2; i >= 0; i--)
                {
                    if (trimmed[i] == '"' && i > 0 && trimmed[i - 1] == '"')
                    {
                        i--; // skip escaped ""
                        continue;
                    }
                    if (trimmed[i] == '"')
                    {
                        openQuote = i;
                        break;
                    }
                }
                if (openQuote >= 0)
                {
                    rawColumn = trimmed.Substring(openQuote);
                }
            }
            else if (trimmed.Length >= 2 && trimmed.EndsWith("`"))
            {
                var openBacktick = -1;
                for (var i = trimmed.Length - 2; i >= 0; i--)
                {
                    if (trimmed[i] == '`' && i > 0 && trimmed[i - 1] == '`')
                    {
                        i--; // skip escaped ``
                        continue;
                    }
                    if (trimmed[i] == '`')
                    {
                        openBacktick = i;
                        break;
                    }
                }
                if (openBacktick >= 0)
                {
                    rawColumn = trimmed.Substring(openBacktick);
                }
            }
            else if (trimmed.Contains('('))
            {
                var match = Regex.Match(trimmed, @"(?:\)|END)\s+((?:'[^']+'|""[^""]+""|\[(?:[^\]]|\]\])+\]|`[^`]+`|[A-Za-z0-9_]+))\s*$", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    rawColumn = match.Groups[1].Value;
                }
                else
                {
                    var parenIndex = trimmed.IndexOf('(');
                    var funcName = parenIndex > 0 ? trimmed.Substring(0, parenIndex).Trim() : trimmed;
                    var lastDot = funcName.LastIndexOf('.');
                    if (lastDot >= 0 && lastDot < funcName.Length - 1)
                    {
                        funcName = funcName.Substring(lastDot + 1).Trim();
                    }
                    rawColumn = IsValidIdentifier(funcName)
                        ? funcName
                        : trimmed;
                }
            }
            else
            {
                var tokens = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 0)
                {
                    continue;
                }

                if (trimmed.Contains('+') || trimmed.Contains('-') || trimmed.Contains('*') || trimmed.Contains('/') || trimmed.Contains('%'))
                {
                    var prevToken = tokens.Length >= 2 ? tokens[tokens.Length - 2] : null;
                    var lastToken = tokens[tokens.Length - 1];
                    var prevEndsWithOp = prevToken != null && (prevToken is "+" or "-" or "*" or "/" or "%" ||
                                         prevToken.EndsWith('+') || prevToken.EndsWith('-') || prevToken.EndsWith('*') || prevToken.EndsWith('/') || prevToken.EndsWith('%'));
                    if (tokens.Length >= 2 &&
                        !prevEndsWithOp &&
                        !string.IsNullOrEmpty(lastToken) &&
                        (char.IsLetter(lastToken[0]) || lastToken[0] == '_') &&
                        lastToken.All(c => char.IsLetterOrDigit(c) || c == '_'))
                    {
                        rawColumn = lastToken;
                    }
                    else
                    {
                        rawColumn = trimmed;
                    }
                }
                else
                {
                    rawColumn = tokens[tokens.Length - 1];
                }
            }
            if (string.IsNullOrEmpty(rawColumn))
            {
                continue;
            }

            string columnName;
            if (rawColumn.StartsWith("[") && rawColumn.EndsWith("]"))
            {
                var lastDot = -1;
                var inB = false;
                for (var ci = 0; ci < rawColumn.Length; ci++)
                {
                    if (rawColumn[ci] == '[')
                    {
                        inB = true;
                    }
                    else if (rawColumn[ci] == ']')
                    {
                        if (ci + 1 < rawColumn.Length && rawColumn[ci + 1] == ']')
                        {
                            ci++;
                        }
                        else
                        {
                            inB = false;
                        }
                    }
                    else if (rawColumn[ci] == '.' && !inB)
                    {
                        lastDot = ci;
                    }
                }

                var part = lastDot >= 0 ? rawColumn.Substring(lastDot + 1) : rawColumn;
                columnName = UnwrapIdentifier(part);
            }
            else
            {
                var dotIndex = rawColumn.LastIndexOf('.');
                var part = dotIndex >= 0 ? rawColumn.Substring(dotIndex + 1) : rawColumn;
                columnName = UnwrapIdentifier(part);
            }
            if (string.IsNullOrEmpty(columnName) || columnName == "*" || IsSqlKeyword(columnName))
            {
                continue;
            }

            columns.Add(columnName);
        }
        return columns;
    }
    private static bool IsValidIdentifier(string s)
    {
        if (string.IsNullOrEmpty(s) || (!char.IsLetter(s[0]) && s[0] != '_'))
        {
            return false;
        }

        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (!char.IsLetterOrDigit(c) && c != '_')
            {
                return false;
            }
        }

        return true;
    }
    private static string UnwrapIdentifier(string part)
    {
        if (IsSingleQuotedIdentifier(part, '[', ']'))
        {
            return part.Substring(1, part.Length - 2).Replace("]]", "]");
        }
        if (IsSingleQuotedIdentifier(part, '"', '"'))
        {
            return part.Substring(1, part.Length - 2).Replace("\"\"", "\"");
        }
        if (IsSingleQuotedIdentifier(part, '`', '`'))
        {
            return part.Substring(1, part.Length - 2).Replace("``", "`");
        }
        if (IsSingleQuotedIdentifier(part, '\'', '\''))
        {
            return part.Substring(1, part.Length - 2).Replace("''", "'");
        }

        return part;
    }

    private static bool IsSingleQuotedIdentifier(string part, char openChar, char closeChar)
    {
        if (string.IsNullOrEmpty(part) || part.Length < 2 || part[0] != openChar || part[part.Length - 1] != closeChar)
        {
            return false;
        }

        for (var k = 1; k < part.Length; k++)
        {
            if (part[k] == closeChar)
            {
                if (k + 1 < part.Length && part[k + 1] == closeChar)
                {
                    k++; // skip escaped delimiter, e.g. ]] or '' or ""
                    continue;
                }

                return k == part.Length - 1;
            }
        }

        return false;
    }

    internal static List<string> SplitTopLevelClauses(string input)
    {
        var list = new List<string>();
        var depth = 0;
        var start = 0;
        char inQuote = '\0';

        for (var i = 0; i < input.Length; i++)
        {
            var ch = input[i];

            if (inQuote != '\0')
            {
                if (inQuote == '[')
                {
                    if (ch == ']')
                    {
                        if (i + 1 < input.Length && input[i + 1] == ']')
                        {
                            i++;
                        }
                        else
                        {
                            inQuote = '\0';
                        }
                    }
                }
                else if (inQuote == '\'')
                {
                    if (ch == '\'')
                    {
                        if (i + 1 < input.Length && input[i + 1] == '\'')
                        {
                            i++;
                        }
                        else
                        {
                            inQuote = '\0';
                        }
                    }
                }
                else if (inQuote == '"')
                {
                    if (ch == '"')
                    {
                        if (i + 1 < input.Length && input[i + 1] == '"')
                        {
                            i++;
                        }
                        else
                        {
                            inQuote = '\0';
                        }
                    }
                }
                else if (ch == inQuote)
                {
                    inQuote = '\0';
                }
                continue;
            }

            if (ch == '-' && i + 1 < input.Length && input[i + 1] == '-')
            {
                var nextNewline = input.IndexOfAny(LineEndings, i + 2);
                if (nextNewline == -1)
                {
                    break;
                }
                i = nextNewline;
                continue;
            }
            if (ch == '/' && i + 1 < input.Length && input[i + 1] == '*')
            {
                var closeComment = input.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (closeComment == -1)
                {
                    break;
                }

                i = closeComment + 1;
                continue;
            }

            if ((ch == 'q' || ch == 'Q') && i + 2 < input.Length && input[i + 1] == '\'')
            {
                var openDelim = input[i + 2];
                var closeDelim = openDelim switch
                {
                    '[' => ']',
                    '{' => '}',
                    '(' => ')',
                    '<' => '>',
                    _ => openDelim
                };
                var target = closeDelim + "'";
                var end = input.IndexOf(target, i + 3, StringComparison.Ordinal);
                if (end >= 0)
                {
                    i = end + 1;
                    continue;
                }
            }
            if (ch == '"' || ch == '`' || ch == '[' || ch == '\'')
            {
                inQuote = ch;
                continue;
            }

            if (ch == '(')
            {
                depth++;
            }
            else if (ch == ')')
            {
                if (depth > 0)
                {
                    depth--;
                }
            }
            else if (ch == ',' && depth == 0)
            {
                list.Add(input.Substring(start, i - start).Trim());
                start = i + 1;
            }
        }

        if (start < input.Length)
        {
            list.Add(input.Substring(start).Trim());
        }

        return list;
    }

    internal static List<string> SplitTopLevelSetBranches(string input)
    {
        var list = new List<string>();
        var depth = 0;
        var start = 0;
        char inQuote = '\0';

        for (var i = 0; i < input.Length; i++)
        {
            var ch = input[i];

            if (inQuote != '\0')
            {
                if (inQuote == '[')
                {
                    if (ch == ']')
                    {
                        if (i + 1 < input.Length && input[i + 1] == ']')
                        {
                            i++;
                        }
                        else
                        {
                            inQuote = '\0';
                        }
                    }
                }
                else if (inQuote == '\'')
                {
                    if (ch == '\'')
                    {
                        if (i + 1 < input.Length && input[i + 1] == '\'')
                        {
                            i++;
                        }
                        else
                        {
                            inQuote = '\0';
                        }
                    }
                }
                else if (inQuote == '"')
                {
                    if (ch == '"')
                    {
                        if (i + 1 < input.Length && input[i + 1] == '"')
                        {
                            i++;
                        }
                        else
                        {
                            inQuote = '\0';
                        }
                    }
                }
                else if (inQuote == '`')
                {
                    if (ch == '`')
                    {
                        if (i + 1 < input.Length && input[i + 1] == '`')
                        {
                            i++;
                        }
                        else
                        {
                            inQuote = '\0';
                        }
                    }
                }
                continue;
            }

            if (ch == '-' && i + 1 < input.Length && input[i + 1] == '-')
            {
                var nextNewline = input.IndexOfAny(LineEndings, i + 2);
                if (nextNewline == -1)
                {
                    break;
                }
                i = nextNewline;
                continue;
            }
            if (ch == '/' && i + 1 < input.Length && input[i + 1] == '*')
            {
                var closeComment = input.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (closeComment == -1)
                {
                    break;
                }

                i = closeComment + 1;
                continue;
            }

            if ((ch == 'q' || ch == 'Q') && i + 2 < input.Length && input[i + 1] == '\'')
            {
                var openDelim = input[i + 2];
                var closeDelim = openDelim switch
                {
                    '[' => ']',
                    '{' => '}',
                    '(' => ')',
                    '<' => '>',
                    _ => openDelim
                };
                var target = closeDelim + "'";
                var end = input.IndexOf(target, i + 3, StringComparison.Ordinal);
                if (end >= 0)
                {
                    i = end + 1;
                    continue;
                }
            }
            if (ch == '[' || ch == '\'' || ch == '"' || ch == '`')
            {
                inQuote = ch;
                continue;
            }

            if (ch == '(')
            {
                depth++;
                continue;
            }
            else if (ch == ')')
            {
                if (depth > 0)
                {
                    depth--;
                }
                continue;
            }
            else if (depth == 0)
            {
                var isWordStart = i == 0 || (!char.IsLetterOrDigit(input[i - 1]) && input[i - 1] != '_');
                if (isWordStart)
                {
                    int matchLen = 0;
                    if (i + 5 <= input.Length && string.Equals(input.Substring(i, 5), "UNION", StringComparison.OrdinalIgnoreCase))
                    {
                        matchLen = 5;
                        var after = i + 5;
                        while (after < input.Length && char.IsWhiteSpace(input[after]))
                        {
                            after++;
                        }
                        if (after + 3 <= input.Length && string.Equals(input.Substring(after, 3), "ALL", StringComparison.OrdinalIgnoreCase) &&
                            (after + 3 == input.Length || (!char.IsLetterOrDigit(input[after + 3]) && input[after + 3] != '_')))
                        {
                            matchLen = (after + 3) - i;
                        }
                    }
                    else if (i + 9 <= input.Length && string.Equals(input.Substring(i, 9), "INTERSECT", StringComparison.OrdinalIgnoreCase))
                    {
                        matchLen = 9;
                    }
                    else if (i + 6 <= input.Length && string.Equals(input.Substring(i, 6), "EXCEPT", StringComparison.OrdinalIgnoreCase))
                    {
                        matchLen = 6;
                    }

                    if (matchLen > 0 && (i + matchLen == input.Length || (!char.IsLetterOrDigit(input[i + matchLen]) && input[i + matchLen] != '_')))
                    {
                        var branch = input.Substring(start, i - start).Trim();
                        if (!string.IsNullOrEmpty(branch))
                        {
                            list.Add(branch);
                        }
                        start = i + matchLen;
                        i = start - 1;
                    }
                }
            }
        }

        if (start < input.Length)
        {
            var branch = input.Substring(start).Trim();
            if (!string.IsNullOrEmpty(branch))
            {
                list.Add(branch);
            }
        }

        return list;
    }

    private static bool IsSqlKeyword(string token)
    {
        return token.ToUpperInvariant() is "SELECT" or "FROM" or "WHERE" or "AS" or
            "DISTINCT" or "CASE" or "WHEN" or "THEN" or "ELSE" or "END" or "NULL";
    }
}

/// <summary>
/// Rule: Nullability must match between database and entity.
/// </summary>
public class NullableMismatchRule : ContractRuleBase
{
    public override string RuleId => "DG005";

    public override string Name => "Nullable Match";

    public override DiagnosticSeverity Severity => DiagnosticSeverity.Warning;

    public override string Description => "Database NOT NULL columns should match non-nullable entity properties";

    protected override async Task ValidateCoreAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        List<ContractViolation> violations,
        CancellationToken cancellationToken)
    {
        // Handle EntityDescriptor: compare property nullability against ground-truth schema columns.
        if (contract is EntityDescriptor entityDesc)
        {
            var schema = allContracts.OfType<DatabaseSchemaDescriptor>().FirstOrDefault();
            if (schema == null)
            {
                return;
            }

            var columnNullability = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var table in schema.Tables)
            {
                foreach (var column in table.Columns)
                {
                    columnNullability[column.Name] = column.IsNullable;
                }
            }

            foreach (var prop in entityDesc.Properties)
            {
                var hasRequired = prop.Annotations?.Any(a => a.Key == "Required") == true;
                var columnName = prop.ColumnName;
                if (string.IsNullOrEmpty(columnName))
                {
                    continue;
                }

                if (!columnNullability.TryGetValue(columnName, out var columnIsNullable))
                {
                    continue;
                }

                if (hasRequired && columnIsNullable)
                {
                    violations.Add(CreateViolation(
                        RuleId,
                        $"Property '{prop.Name}' is required but database column '{columnName}' allows NULL",
                        Severity));
                }
                else if (!hasRequired && !columnIsNullable)
                {
                    violations.Add(CreateViolation(
                        RuleId,
                        $"Property '{prop.Name}' is nullable but database column '{columnName}' is NOT NULL",
                        Severity));
                }
            }
        }
    }
}

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

/// <summary>Rule: raw SQL must parse before semantic validation can be complete.</summary>
public sealed class RawSqlParseStatusRule : ContractRuleBase
{
    public override string RuleId => "DG016";
    public override string Name => "Raw SQL Parse Status";
    public override DiagnosticSeverity Severity => DiagnosticSeverity.Error;
    public override string Description => "Raw SQL must parse successfully before validation";

    protected override Task ValidateCoreAsync(ContractDescriptor contract, IReadOnlyList<ContractDescriptor> allContracts, List<ContractViolation> violations, CancellationToken cancellationToken)
    {
        if (contract is RawSqlDescriptor { ParseStatus: RawSqlParseStatus.Invalid } rawSql)
        {
            violations.Add(CreateViolation(RuleId, $"Raw SQL could not be parsed: {rawSql.ParseError ?? "unknown parse error"}", Severity, contract.Location));
        }

        return Task.CompletedTask;
    }
}

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
                violations.Add(CreateViolation(
                    RuleId,
                    "Avoid SELECT *; specify explicit columns to reduce bandwidth and enable shape validation.",
                    Severity,
                    contract.Location));
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

        var stripped = ColumnShapeMatchRule.StripCommentsAndLiterals(sqlText);
        var setBranches = ColumnShapeMatchRule.SplitTopLevelSetBranches(sqlText);
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

        var selectClause = ColumnShapeMatchRule.ExtractTopLevelSelectClause(sqlText);
        if (string.IsNullOrWhiteSpace(selectClause))
        {
            return Regex.IsMatch(stripped, @"\bSELECT\s+(DISTINCT\s+|ALL\s+)?(?:(?:\w+|\[[^\]]+\]|""[^""]+""|`[^`]+`)\.)*\*", RegexOptions.IgnoreCase);
        }

        var items = ColumnShapeMatchRule.SplitTopLevelClauses(selectClause);
        foreach (var item in items)
        {
            var trimmed = ColumnShapeMatchRule.StripCommentsAndLiterals(item).Trim();
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
