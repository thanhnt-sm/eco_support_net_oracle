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
            if (allContracts?.Any(c => c is RawSqlDescriptor) == true)
            {
                var sqlDesc = allContracts.First(c => c is RawSqlDescriptor) as RawSqlDescriptor;
                if (sqlDesc != null)
                {
                    var columnNames = ExtractColumnNamesFromSql(sqlDesc.SqlText);

                    // If no columns could be extracted (SELECT *, expressions only), skip shape comparison.
                    if (columnNames.Count == 0)
                    {
                        return;
                    }

                    // Check for missing required columns
                    var missingColumns = entityDesc.Properties
                        .Where(p => !columnNames.Contains(p.Name, StringComparer.OrdinalIgnoreCase) &&
                                    (string.IsNullOrEmpty(p.ColumnName) ||
                                     !columnNames.Contains(p.ColumnName, StringComparer.OrdinalIgnoreCase)))
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
                    var extraColumns = columnNames.Where(c => !entityPropertyNames.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();

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
    }

    private static HashSet<string> ExtractColumnNamesFromSql(string sqlText)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var selectMatch = Regex.Match(
            sqlText, @"SELECT\s+(.+?)\s+FROM", RegexOptions.IgnoreCase | RegexOptions.Singleline);

        if (!selectMatch.Success)
        {
            return columns;
        }

        var selectClause = selectMatch.Groups[1].Value;
        if (selectClause.Trim() == "*")
        {
            return columns; // SELECT *: column list is unknown, skip shape comparison.
        }

        foreach (var part in selectClause.Split(','))
        {
            var trimmed = part.Trim();
            if (trimmed.Length == 0 || trimmed == "*")
            {
                continue;
            }

            // Skip expressions: function calls, qualified refs, literals, operators.
            if (trimmed.Contains('(') || trimmed.Contains('.') ||
                trimmed.Contains('+') || trimmed.Contains('-') || trimmed.Contains('*') || trimmed.Contains('/'))
            {
                continue;
            }

            var tokens = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                continue;
            }

            // "column AS alias" -> use the alias; otherwise use the last token (handles "column alias").
            var asIndex = Array.FindIndex(tokens, t => t.Equals("AS", StringComparison.OrdinalIgnoreCase));
            var columnName = asIndex >= 0 && asIndex + 1 < tokens.Length
                ? tokens[asIndex + 1]
                : tokens[tokens.Length - 1];

            if (string.IsNullOrEmpty(columnName) || IsSqlKeyword(columnName))
            {
                continue;
            }

            columns.Add(columnName);
        }

        return columns;
    }

    private static bool IsSqlKeyword(string token)
    {
        return token.ToUpperInvariant() is "SELECT" or "FROM" or "WHERE" or "AS" or
            "SUM" or "COUNT" or "MAX" or "MIN" or "AVG" or "DISTINCT" or "CASE" or
            "WHEN" or "THEN" or "ELSE" or "END" or "NULL";
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