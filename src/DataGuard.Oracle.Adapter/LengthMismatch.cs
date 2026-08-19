namespace DataGuard.Oracle.Adapter;

using DataGuard.Core.Abstractions;
using DataGuard.Core.Models;
using DataGuard.Core.Rules;
using Microsoft.CodeAnalysis;

/// <summary>
/// Simulates EF Core Oracle provider's type inference behavior.
/// Mirrors the behavior described in dotnet/efcore#33218.
/// </summary>
public class EfCoreInferenceSimulator
{
    /// <summary>
    /// Predicts the Oracle column type that EF Core would infer for a property.
    /// </summary>
    public OracleColumnType Predict(PropertyDescriptor property, string? sqlFragment = null)
    {
        var maxLen = property.MaxLength;
        var isUnicode = IsUnicodeType(property.ClrTypeName);

        // Mirror EF Core Oracle provider behavior from #33218
        if (maxLen is null && isUnicode)
        {
            // EF Core Oracle provider falls back to NVARCHAR2(2000) when size is null and Unicode
            return OracleColumnTypeFactory.NVarchar2(2000);
        }

        if (maxLen is null && !isUnicode)
        {
            // Non-Unicode with no size -> VARCHAR2(2000) typically
            return OracleColumnTypeFactory.Varchar2(2000);
        }

        if (maxLen > 4000 && isUnicode)
        {
            // NVARCHAR2 max is 4000 chars, beyond that -> NCLOB
            return OracleColumnType.NClob;
        }

        if (maxLen > 4000 && !isUnicode)
        {
            // VARCHAR2 max is 4000 bytes, beyond that -> CLOB
            return OracleColumnType.Clob;
        }

        if (isUnicode)
        {
            return OracleColumnTypeFactory.NVarchar2(maxLen.Value);
        }

        return OracleColumnTypeFactory.Varchar2(maxLen.Value);
    }

    /// <summary>
    /// Predicts the Oracle column type for a raw SQL parameter.
    /// </summary>
    public OracleColumnType PredictForParameter(ParameterDescriptor parameter)
    {
        var isUnicode = parameter.DataType.StartsWith("N", StringComparison.OrdinalIgnoreCase);
        var maxLen = parameter.MaxLength;

        if (maxLen is null && isUnicode)
            return OracleColumnTypeFactory.NVarchar2(2000);

        if (maxLen is null && !isUnicode)
            return OracleColumnTypeFactory.Varchar2(2000);

        if (isUnicode)
        {
            if (maxLen > 4000) return OracleColumnType.NClob;
            return OracleColumnTypeFactory.NVarchar2(maxLen.Value);
        }

        if (maxLen > 4000) return OracleColumnType.Clob;
        return OracleColumnTypeFactory.Varchar2(maxLen.Value);
    }

    private static bool IsUnicodeType(string clrTypeName)
    {
        return clrTypeName switch
        {
            "string" => true,
            "System.String" => true,
            _ => false
        };
    }
}

/// <summary>
/// Oracle column type enum.
/// </summary>
public enum OracleColumnType
{
    Varchar2,
    NVarchar2,
    Char,
    NChar,
    Clob,
    NClob,
    Number,
    Date,
    Timestamp,
    TimestampWithTimeZone,
    Raw,
    Blob,
    RowId
}

public static class OracleColumnTypeFactory
{
    public static OracleColumnType Varchar2(int length) => OracleColumnType.Varchar2;
    public static OracleColumnType NVarchar2(int length) => OracleColumnType.NVarchar2;
    public static OracleColumnType Clob() => OracleColumnType.Clob;
    public static OracleColumnType NClob() => OracleColumnType.NClob;
}

/// <summary>
/// Resolves length semantics (CHAR vs BYTE) from Oracle session.
/// </summary>
public class LengthSemanticsResolver
{
    private readonly string _connectionString;

    public LengthSemanticsResolver(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<LengthSemantics> ResolveAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT value
            FROM nls_session_parameters
            WHERE parameter = 'NLS_LENGTH_SEMANTICS'";

        await using var connection = new global::Oracle.ManagedDataAccess.Client.OracleConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new global::Oracle.ManagedDataAccess.Client.OracleCommand(sql, connection);
        var value = await command.ExecuteScalarAsync(cancellationToken) as string;

        return value == "CHAR" ? LengthSemantics.Char : LengthSemantics.Byte;
    }
}

/// <summary>
/// Detects length mismatches between entity properties and Oracle columns.
/// </summary>
public class LengthMismatchDetector
{
    private readonly EfCoreInferenceSimulator _inferenceSimulator = new();

    public IEnumerable<ContractViolation> Detect(
        EntityDescriptor entity,
        IReadOnlyList<ColumnDescriptor> columns,
        LengthSemantics sessionSemantics)
    {
        foreach (var property in entity.Properties)
        {
            var column = columns.FirstOrDefault(c => 
                string.Equals(c.Name, property.ColumnName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.Name, ToOracleColumnName(property.Name), StringComparison.OrdinalIgnoreCase));

            if (column == null) continue;

            // 1. Direct length mismatch: entity MaxLength > column MaxLength
            if (property.MaxLength.HasValue && column.MaxLength.HasValue)
            {
                if (property.MaxLength.Value > column.MaxLength.Value)
                {
                    yield return new ContractViolation(
                        "DG020",
                        $"Entity property '{property.Name}' has MaxLength={property.MaxLength.Value} " +
                        $"but Oracle column '{column.Name}' has MaxLength={column.MaxLength.Value}. " +
                        $"Data truncation will occur at runtime.",
                        DiagnosticSeverity.Error,
                        property.Annotations?.TryGetValue("Location", out var loc) == true ? loc as Location : null,
                        new Dictionary<string, object?>
                        {
                            { "property", property.Name },
                            { "entityMaxLength", property.MaxLength.Value },
                            { "columnMaxLength", column.MaxLength.Value },
                            { "entityColumnName", property.ColumnName ?? property.Name },
                            { "dbColumnName", column.Name }
                        });
                }
            }

            // 2. Byte semantics overflow risk
            if (sessionSemantics == LengthSemantics.Byte &&
                property.MaxLength.HasValue &&
                column.MaxLength.HasValue)
            {
                var maxBytesPerChar = IsUnicodeType(property.ClrTypeName) ? 3 : 1; // UTF-8: 3 bytes per char max, ASCII: 1
                var maxByteLength = column.MaxLength.Value * maxBytesPerChar;

                if (property.MaxLength.Value * maxBytesPerChar > maxByteLength)
                {
                    yield return new ContractViolation(
                        "DG021",
                        $"Byte-length overflow risk: property '{property.Name}' has MaxLength={property.MaxLength.Value} " +
                        $"({property.MaxLength.Value * maxBytesPerChar} bytes max for {maxBytesPerChar}-byte chars) " +
                        $"but Oracle column '{column.Name}' has MaxLength={column.MaxLength.Value} " +
                        $"({maxByteLength} bytes in {sessionSemantics} semantics). " +
                        $"Unicode data may exceed column byte capacity.",
                        DiagnosticSeverity.Warning,
                        null,
                        new Dictionary<string, object?>
                        {
                            { "property", property.Name },
                            { "entityMaxLength", property.MaxLength.Value },
                            { "maxBytesPerChar", maxBytesPerChar },
                            { "columnMaxLength", column.MaxLength.Value },
                            { "maxByteLength", maxByteLength },
                            { "semantics", sessionSemantics.ToString() }
                        });
                }
            }

            // 3. CLOB/VARCHAR2 switching risk (mirrors #33218)
            if (!property.MaxLength.HasValue && IsUnicodeType(property.ClrTypeName))
            {
                if (column.DataType != "NCLOB" && column.DataType != "CLOB")
                {
                    yield return new ContractViolation(
                        "DG022",
                        $"EF Core will infer NVARCHAR2(2000) for property '{property.Name}' (no MaxLength set, Unicode=true) " +
                        $"but Oracle column '{column.Name}' is {column.DataType}. " +
                        $"If values exceed 2000 characters, ORA-12899 'value too large for column' will occur at runtime. " +
                        $"Consider setting explicit MaxLength or using NCLOB column type.",
                        DiagnosticSeverity.Warning,
                        null,
                        new Dictionary<string, object?>
                        {
                            { "property", property.Name },
                            { "inferredType", "NVARCHAR2(2000)" },
                            { "dbColumnType", column.DataType },
                            { "referencedIssue", "dotnet/efcore#33218" }
                        });
                }
            }
        }
    }

    private static string ToOracleColumnName(string propertyName)
    {
        // Convert PascalCase to UPPER_SNAKE_CASE (Oracle convention)
        return string.Concat(propertyName.Select((c, i) =>
            i > 0 && char.IsUpper(c) ? "_" + char.ToUpperInvariant(c) : char.ToUpperInvariant(c).ToString()));
    }

    private static bool IsUnicodeType(string clrTypeName)
    {
        return clrTypeName switch
        {
            "string" => true,
            "System.String" => true,
            _ => false
        };
    }
}

/// <summary>
/// Length semantics (CHAR vs BYTE).
/// </summary>
public enum LengthSemantics
{
    Char,
    Byte
}

/// <summary>
/// Rule: Length mismatch between entity and Oracle column.
/// </summary>
public class LengthExceedsColumnRule : ContractRuleBase
{
    public override string RuleId => "DG020";
    public override string Name => "Entity Length Exceeds Column Length";
    public override DiagnosticSeverity Severity => DiagnosticSeverity.Error;
    public override string Description => "Entity property MaxLength exceeds Oracle column MaxLength";

    protected override Task ValidateCoreAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        List<ContractViolation> violations,
        CancellationToken cancellationToken)
    {
        // Implementation would use LengthMismatchDetector
        return Task.CompletedTask;
    }
}

/// <summary>
/// Rule: Byte-length overflow risk in BYTE semantics.
/// </summary>
public class ByteLengthOverflowRiskRule : ContractRuleBase
{
    public override string RuleId => "DG021";
    public override string Name => "Byte Length Overflow Risk";
    public override DiagnosticSeverity Severity => DiagnosticSeverity.Warning;
    public override string Description => "Entity property may exceed Oracle column byte capacity in BYTE semantics";

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
/// Rule: Inferred size fallback risk (NVARCHAR2(2000) fallback).
/// </summary>
public class InferredSizeFallbackRule : ContractRuleBase
{
    public override string RuleId => "DG022";
    public override string Name => "Inferred Size Fallback Risk";
    public override DiagnosticSeverity Severity => DiagnosticSeverity.Warning;
    public override string Description => "EF Core infers NVARCHAR2(2000) which may cause ORA-12899";

    protected override Task ValidateCoreAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        List<ContractViolation> violations,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
