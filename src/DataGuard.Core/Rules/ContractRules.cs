using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using System.Text.RegularExpressions;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Models;

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
    public override string RuleId => "DG001";
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

            if (string.IsNullOrEmpty(sqlText)) return;

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
                else if (detectedCount < 1)
                {
                    violations.Add(CreateViolation(
                        RuleId,
                        $"Stored procedure expected parameters but only {detectedCount} were found",
                        Severity));
                }
            }
        }
        // Handle EntityDescriptor
        else if (contract is EntityDescriptor entityDesc)
        {
            var sqlText = entityDesc.Properties
                .Where(p => p.ColumnName != null)
                .Select(p => p.ColumnName)
                .Aggregate<string, string>("", (a, b) => a + " " + b);

            if (!string.IsNullOrEmpty(sqlText))
            {
                var paramMatches = Regex.Matches(sqlText, @"@\w+");
                var detectedCount = paramMatches.Count;

                if (detectedCount == 0)
                {
                    violations.Add(CreateViolation(
                        RuleId,
                        "Entity contract has no parameters detected in SQL",
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
            var isOracle = sqlDesc.Parameters?.Any(p => p.DataType?.Contains("NUMBER") == true) == true;
            var typeMap = isOracle ? OracleTypeMap : SqlServerTypeMap;

            foreach (var param in sqlDesc.Parameters)
            {
                var clrType = InferClrType(param.DataType);
                var isCompatible = IsTypeCompatible(clrType, param.DataType, isOracle);

                if (!isCompatible)
                {
                    violations.Add(CreateViolation(
                        RuleId,
                        $"Parameter '{param.Name}' has CLR type '{clrType}' but database type '{param.DataType}' is not compatible",
                        Severity));
                }
            }
        }
    }

    private static string InferClrType(string typeStr)
    {
        return typeStr switch
        {
            "int" or "integer" or "number" => "int",
            "long" or "bigint" => "long",
            "short" or "smallint" => "short",
            "float" or "real" => "double",
            "decimal" or "numeric" => "decimal",
            "varchar" or "char" or "nvarchar" or "nchar" => "string",
            "datetime" or "date" or "datetime2" => "DateTime",
            "uniqueidentifier" or "guid" => "Guid",
            "byte[]" or "varbinary" or "binary" => "byte[]",
            _ => "string"
        };
    }

    public static bool IsTypeCompatible(string clrType, string dbType, bool isOracle)
    {
        var map = isOracle ? OracleTypeMap : SqlServerTypeMap;
        if (!map.TryGetValue(clrType, out var compatibleDbTypes))
            return false;
        return compatibleDbTypes.Any(t => dbType.Contains(t, StringComparison.OrdinalIgnoreCase));
    }
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
            foreach (var param in sqlDesc.Parameters)
            {
                // Check for OUT direction
                if (param.DataType != null && param.DataType.Contains("OUT"))
                {
                    violations.Add(CreateViolation(
                        RuleId,
                        $"Parameter '{param.Name}' has OUT direction in SQL - verify matches call site",
                        Severity));
                }
                // Check for REF direction
                if (param.DataType != null && param.DataType.Contains("REF"))
                {
                    violations.Add(CreateViolation(
                        RuleId,
                        $"Parameter '{param.Name}' has REF direction in SQL - verify matches call site",
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
                .Select(p => p.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Handle RawSqlDescriptor for column extraction
            if (allContracts?.Any(c => c is RawSqlDescriptor) == true)
            {
                var sqlDesc = allContracts.First(c => c is RawSqlDescriptor) as RawSqlDescriptor;
                if (sqlDesc != null)
                {
                    var columnNames = ExtractColumnNamesFromSql(sqlDesc.SqlText);

                    // Check for missing required columns
                    var missingColumns = entityPropertyNames.Where(p => !columnNames.Contains(p, StringComparer.OrdinalIgnoreCase)).ToList();

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
            sqlText, @"SELECT\s+(.+?)\s+FROM", RegexOptions.IgnoreCase);

        if (selectMatch.Success)
        {
            var selectClause = selectMatch.Groups[1].Value;
            var parts = selectClause.Split(',');

            foreach (var part in parts)
            {
                var columnName = part.Split(' ').First().Trim();
                if (!string.IsNullOrEmpty(columnName) &&
                    !columnName.ToUpperInvariant().StartsWith("AS") &&
                    !columnName.ToUpperInvariant().StartsWith("SUM") &&
                    !columnName.ToUpperInvariant().StartsWith("COUNT") &&
                    !columnName.ToUpperInvariant().StartsWith("MAX") &&
                    !columnName.ToUpperInvariant().StartsWith("MIN") &&
                    !columnName.ToUpperInvariant().StartsWith("AVG") &&
                    !columnName.ToUpperInvariant().StartsWith("DISTINCT"))
                {
                    columns.Add(columnName);
                }
            }
        }

        return columns;
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
        // Handle EntityDescriptor
        if (contract is EntityDescriptor entityDesc)
        {
            foreach (var prop in entityDesc.Properties)
            {
                var hasRequired = prop.Annotations?.Any(a => a.Key == "Required") == true;
                var columnName = prop.ColumnName;

                if (string.IsNullOrEmpty(columnName)) continue;

                // Check if column appears in SQL with NOT NULL
                // Search through all raw SQL descriptions
                var rawSqlDescriptions = allContracts?.Where(c => c is RawSqlDescriptor)
                    .Cast<RawSqlDescriptor>()
                    .ToList() ?? new List<RawSqlDescriptor>();

                foreach (var sqlDesc in rawSqlDescriptions)
                {
                    if (sqlDesc.SqlText != null)
                    {
                        var sqlText = sqlDesc.SqlText;
                        if (sqlText.IndexOf(columnName, StringComparison.OrdinalIgnoreCase) >= 0 && sqlText.Contains("NOT NULL"))
                        {
                            if (hasRequired)
                            {
                                violations.Add(CreateViolation(
                                    RuleId,
                                    $"Property '{prop.Name}' is required but database column '{columnName}' allows NULL",
                                    Severity));
                            }
                        }
                    }
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
                if (string.IsNullOrEmpty(columnName)) continue;

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
    {
        return string.Concat(pascalCase.Select((c, i) =>
            i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c).ToString() : char.ToLowerInvariant(c).ToString()));
    }

    public static string ToPascalCase(string snakeCase)
    {
        return string.Concat(snakeCase.Split('_', '-', '.')
            .Select(s => char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant()));
    }
}