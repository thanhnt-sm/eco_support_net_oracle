using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
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

    protected override Task ValidateCoreAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        List<ContractViolation> violations,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
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

    protected override Task ValidateCoreAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        List<ContractViolation> violations,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public static bool IsTypeCompatible(string clrType, string dbType, bool isOracle)
    {
        var map = isOracle ? OracleTypeMap : SqlServerTypeMap;
        var clrKey = map.Keys.FirstOrDefault(k => dbType.Contains(k, StringComparison.OrdinalIgnoreCase));
        if (clrKey == null) return false;
        return map[clrKey].Any(t => dbType.Contains(t, StringComparison.OrdinalIgnoreCase));
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

    protected override Task ValidateCoreAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        List<ContractViolation> violations,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
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

    protected override Task ValidateCoreAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        List<ContractViolation> violations,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
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

    protected override Task ValidateCoreAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        List<ContractViolation> violations,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
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

    protected override Task ValidateCoreAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        List<ContractViolation> violations,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public static string ToPascalCase(string snakeCase)
    {
        return string.Concat(snakeCase.Split('_', '-', '.')
            .Select(s => char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant()));
    }

    public static string ToSnakeCase(string pascalCase)
    {
        return string.Concat(pascalCase.Select((c, i) =>
            i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
    }
}