namespace DataGuard.Oracle.Adapter;

using DataGuard.Core.Abstractions;
using DataGuard.Core.Models;
using DataGuard.Core.Rules;
using DataGuard.Core.Rules.Sql;
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
    /// <returns></returns>
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
            return OracleColumnTypeFactory.NVarchar2(maxLen!.Value);
        }

        return OracleColumnTypeFactory.Varchar2(maxLen!.Value);
    }

    /// <summary>
    /// Predicts the Oracle column type for a raw SQL parameter.
    /// </summary>
    /// <returns></returns>
    public OracleColumnType PredictForParameter(ParameterDescriptor parameter)
    {
        var isUnicode = parameter.DataType.StartsWith("N", StringComparison.OrdinalIgnoreCase);
        var maxLen = parameter.MaxLength;

        if (maxLen is null && isUnicode)
        {
            return OracleColumnTypeFactory.NVarchar2(2000);
        }

        if (maxLen is null && !isUnicode)
        {
            return OracleColumnTypeFactory.Varchar2(2000);
        }

        if (isUnicode)
        {
            if (maxLen > 4000)
            {
                return OracleColumnType.NClob;
            }

            return OracleColumnTypeFactory.NVarchar2(maxLen!.Value);
        }

        if (maxLen > 4000)
        {
            return OracleColumnType.Clob;
        }

        return OracleColumnTypeFactory.Varchar2(maxLen!.Value);
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
    RowId,
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
/// Database facts that change how many bytes a .NET string needs in an Oracle column.
/// </summary>
/// <param name="DatabaseCharset">NLS_CHARACTERSET (VARCHAR2/CHAR/CLOB), or null when unknown.</param>
/// <param name="NationalCharset">NLS_NCHAR_CHARACTERSET (NVARCHAR2/NCHAR/NCLOB), or null when unknown.</param>
/// <param name="MaxStringSize">MAX_STRING_SIZE: STANDARD (4000-byte VARCHAR2) or EXTENDED (32767).</param>
public sealed record OracleLengthContext(string? DatabaseCharset, string? NationalCharset, string MaxStringSize = "STANDARD")
{
    /// <summary>Gets the context used when nothing is known (unknown charset, STANDARD limits).</summary>
    public static OracleLengthContext Unknown { get; } = new(null, null);
}

/// <summary>
/// Oracle character-set arithmetic for length checks.
/// </summary>
public static class OracleCharsets
{
    /// <summary>
    /// Worst-case bytes one UTF-16 code unit (one <c>char</c> of a .NET string, the unit EF <c>MaxLength</c> counts) needs in
    /// <paramref name="charset"/>: AL32UTF8/UTF8 = 3 (a BMP character is at most 3 bytes; a supplementary character is
    /// 2 units / 4 bytes), AL16UTF16/UTF16 = 2, single-byte sets (US7*, WE8*, EE8*, ...) = 1, 16-bit multibyte sets = 2
    /// (EUC = 3), GB18030 = 4, unknown = 3. When <paramref name="isUnicode"/> is false (EF <c>IsUnicode(false)</c>, data
    /// is declared single-byte) the cost is the charset's minimum: 2 for UTF-16 sets, otherwise 1.
    /// </summary>
    /// <returns>Bytes per UTF-16 code unit.</returns>
    public static int BytesPerUtf16Unit(string? charset, bool isUnicode)
    {
        var name = Normalize(charset);
        if (IsUtf16(name))
        {
            return 2;
        }

        if (!isUnicode)
        {
            return 1;
        }

        return name switch
        {
            "" => 3,
            "AL32UTF8" or "UTF8" or "UTFE" => 3,
            _ when name.Contains("GB18030", StringComparison.Ordinal) => 4,
            _ when IsSingleByte(name) => 1,
            _ when IsSixteenBit(name) => name.Contains("EUC", StringComparison.Ordinal) ? 3 : 2,
            _ => 3,
        };
    }

    /// <summary>
    /// Maximum bytes of one character in <paramref name="charset"/> (Oracle's own CHAR-semantics accounting): AL32UTF8 = 4,
    /// UTF8 = 3, AL16UTF16 = 2, single-byte = 1, 16-bit sets = 2 (EUC = 3), GB18030 = 4, unknown = 4.
    /// </summary>
    /// <returns>Bytes per character.</returns>
    public static int MaxBytesPerCharacter(string? charset)
    {
        var name = Normalize(charset);
        return name switch
        {
            "" => 4,
            "AL32UTF8" => 4,
            "UTF8" or "UTFE" => 3,
            _ when IsUtf16(name) => 2,
            _ when name.Contains("GB18030", StringComparison.Ordinal) => 4,
            _ when IsSingleByte(name) => 1,
            _ when IsSixteenBit(name) => name.Contains("EUC", StringComparison.Ordinal) ? 3 : 2,
            _ => 4,
        };
    }

    /// <summary>
    /// Byte ceiling of a character column type: CHAR/NCHAR = 2000; VARCHAR2/NVARCHAR2 = 4000, or 32767 when
    /// <paramref name="maxStringSize"/> is EXTENDED; null for LOB/other types.
    /// </summary>
    /// <returns>The byte ceiling, or null.</returns>
    public static int? MaxColumnBytes(string? dataType, string? maxStringSize)
    {
        var extended = string.Equals(maxStringSize?.Trim(), "EXTENDED", StringComparison.OrdinalIgnoreCase);
        return dataType?.Trim().ToUpperInvariant() switch
        {
            "CHAR" or "NCHAR" => 2000,
            "VARCHAR2" or "VARCHAR" or "NVARCHAR2" => extended ? 32767 : 4000,
            _ => null,
        };
    }

    /// <summary>Returns true for national character types (NVARCHAR2/NCHAR/NCLOB).</summary>
    /// <returns>True for national types.</returns>
    public static bool IsNationalType(string? dataType)
        => dataType?.Trim().ToUpperInvariant() is "NVARCHAR2" or "NCHAR" or "NCLOB";

    private static string Normalize(string? charset)
    {
        var name = charset?.Trim().ToUpperInvariant() ?? string.Empty;
        return name == "UNKNOWN" ? string.Empty : name;
    }

    private static bool IsUtf16(string name) => name is "AL16UTF16" or "AL16UTF16LE" or "UTF16";

    private static bool IsSingleByte(string name)
        => System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Z]{1,4}[78][A-Z0-9]*$", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));

    private static bool IsSixteenBit(string name)
        => System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Z]{1,4}16[A-Z0-9]*$", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));
}

/// <summary>
/// Detects length mismatches between entity properties and Oracle columns.
/// </summary>
public class LengthMismatchDetector
{
    private readonly EfCoreInferenceSimulator _inferenceSimulator = new();

    /// <summary>Detects mismatches without database charset facts (unknown charset ⇒ 3 bytes per UTF-16 unit).</summary>
    /// <returns>The violations.</returns>
    public IEnumerable<ContractViolation> Detect(
        EntityDescriptor entity,
        IReadOnlyList<ColumnDescriptor> columns,
        LengthSemantics sessionSemantics)
        => Detect(entity, columns, sessionSemantics, OracleLengthContext.Unknown);

    /// <summary>
    /// Detects DG007 (chars over CHAR_LENGTH), DG008 (worst-case bytes over the column's byte capacity) and DG009
    /// (EF NVARCHAR2(2000) fallback over a LOB). The byte capacity is DATA_LENGTH for BYTE-semantics columns and
    /// <c>min(CHAR_LENGTH × maxBytesPerChar(charset), 2000|4000|32767)</c> for CHAR-semantics columns; the entity's worst
    /// case is <c>MaxLength × BytesPerUtf16Unit(charset, IsUnicode)</c>.
    /// </summary>
    /// <returns>The violations.</returns>
    public IEnumerable<ContractViolation> Detect(
        EntityDescriptor entity,
        IReadOnlyList<ColumnDescriptor> columns,
        LengthSemantics sessionSemantics,
        OracleLengthContext? context)
    {
        context ??= OracleLengthContext.Unknown;
        foreach (var property in entity.Properties)
        {
            var column = FindColumn(property, columns);
            if (column == null)
            {
                continue;
            }

            // 1. Direct length mismatch: entity MaxLength (chars) > column char length.
            //    ColumnDescriptor.MaxLength holds DATA_LENGTH (bytes); CharLength holds CHAR_LENGTH (chars).
            //    Compare chars against chars, falling back to byte length only for BYTE-semantics columns.
            var columnCharLength = column.CharLength ?? column.MaxLength;
            if (property.MaxLength.HasValue && columnCharLength.HasValue)
            {
                if (property.MaxLength.Value > columnCharLength.Value)
                {
                    yield return new ContractViolation(
                        "DG007",
                        $"Entity property '{property.Name}' MaxLength={property.MaxLength.Value} " +
                        $"exceeds column '{column.Name}' length={columnCharLength.Value}",
                        DiagnosticSeverity.Error,
                        null,
                        new Dictionary<string, object?>
                        {
                            { "property", property.Name },
                            { "column", column.Name },
                            { "entityMaxLength", property.MaxLength.Value },
                            { "columnMaxLength", columnCharLength.Value },
                        });
                }
            }

            // 2. Byte capacity overflow risk. Prefer the authoritative per-column char_used (B=BYTE, C=CHAR); fall
            //    back to the session NLS length semantics only when the column is silent.
            if (property.MaxLength.HasValue &&
                TryGetColumnByteCapacity(column, sessionSemantics, context, out var columnMaxBytes, out var byteSemantics))
            {
                var charset = ResolveCharset(column, context);
                var isString = IsUnicodeType(property.ClrTypeName);
                var bytesPerUnit = isString ? OracleCharsets.BytesPerUtf16Unit(charset, IsUnicode(property)) : 1;
                var entityMaxBytes = (long)property.MaxLength.Value * bytesPerUnit;

                if (entityMaxBytes > columnMaxBytes)
                {
                    yield return new ContractViolation(
                        "DG008",
                        $"Byte overflow risk: property '{property.Name}' may exceed column '{column.Name}' " +
                        $"byte capacity in {(byteSemantics ? "BYTE" : "CHAR")} semantics: up to {entityMaxBytes} bytes " +
                        $"({property.MaxLength.Value} × {bytesPerUnit} for {charset ?? "unknown charset"}) > {columnMaxBytes} bytes",
                        DiagnosticSeverity.Warning,
                        null,
                        new Dictionary<string, object?>
                        {
                            { "property", property.Name },
                            { "column", column.Name },
                            { "entityMaxBytes", entityMaxBytes },
                            { "columnMaxBytes", columnMaxBytes },
                            { "bytesPerUnit", bytesPerUnit },
                            { "charset", charset },
                            { "semantics", byteSemantics ? "Byte" : "Char" },
                        });
                }
            }

            // 3. Inferred NVARCHAR2(2000) fallback risk (mirrors dotnet/efcore#33218)
            if (!property.MaxLength.HasValue && IsUnicodeType(property.ClrTypeName))
            {
                if (string.Equals(column.DataType, "CLOB", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(column.DataType, "NCLOB", StringComparison.OrdinalIgnoreCase))
                {
                    yield return new ContractViolation(
                        "DG009",
                        $"EF Core will infer NVARCHAR2(2000) for property '{property.Name}' " +
                        $"(no MaxLength set, Unicode=true) but Oracle column '{column.Name}' is {column.DataType}. " +
                        $"If values exceed 2000 characters, ORA-12899 'value too large for column' will occur at runtime. " +
                        $"Consider setting explicit MaxLength or using NCLOB column type.",
                        DiagnosticSeverity.Warning,
                        null,
                        new Dictionary<string, object?>
                        {
                            { "property", property.Name },
                            { "column", column.Name },
                            { "inferredType", "NVARCHAR2(2000)" },
                            { "dbColumnType", column.DataType },
                            { "referencedIssue", "dotnet/efcore#33218" },
                        });
                }
            }
        }
    }

    /// <summary>
    /// Candidate Oracle column names for a property, in lookup order: the EF column name, the property name upper-cased
    /// (<c>CustomerID</c> ⇒ <c>CUSTOMERID</c>) and the property name in UPPER_SNAKE_CASE (<c>CustomerID</c> ⇒ <c>CUSTOMER_ID</c>).
    /// </summary>
    /// <returns>Distinct candidate names.</returns>
    public static IReadOnlyList<string> ToOracleColumnNames(PropertyDescriptor property)
    {
        ArgumentNullException.ThrowIfNull(property);
        var candidates = new List<string>(3);
        if (!string.IsNullOrEmpty(property.ColumnName))
        {
            candidates.Add(property.ColumnName);
        }

        candidates.Add(property.Name.ToUpperInvariant());
        candidates.Add(ToUpperSnakeCase(property.Name));
        return candidates.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Converts PascalCase to UPPER_SNAKE_CASE treating acronyms as one word: <c>FirstName</c> ⇒ <c>FIRST_NAME</c>,
    /// <c>CustomerID</c> ⇒ <c>CUSTOMER_ID</c>, <c>HTMLBody</c> ⇒ <c>HTML_BODY</c>.
    /// </summary>
    /// <returns>The snake-case name.</returns>
    public static string ToUpperSnakeCase(string propertyName)
    {
        var builder = new System.Text.StringBuilder(propertyName.Length + 4);
        for (var i = 0; i < propertyName.Length; i++)
        {
            var c = propertyName[i];
            if (i > 0 && char.IsUpper(c))
            {
                var previous = propertyName[i - 1];
                var nextIsLower = i + 1 < propertyName.Length && char.IsLower(propertyName[i + 1]);
                if (char.IsLower(previous) || char.IsDigit(previous) || (char.IsUpper(previous) && nextIsLower))
                {
                    builder.Append('_');
                }
            }

            builder.Append(char.ToUpperInvariant(c));
        }

        return builder.ToString();
    }

    private static ColumnDescriptor? FindColumn(PropertyDescriptor property, IReadOnlyList<ColumnDescriptor> columns)
    {
        foreach (var candidate in ToOracleColumnNames(property))
        {
            var column = columns.FirstOrDefault(c => string.Equals(c.Name, candidate, StringComparison.Ordinal))
                ?? columns.FirstOrDefault(c => string.Equals(c.Name, candidate, StringComparison.OrdinalIgnoreCase));
            if (column != null)
            {
                return column;
            }
        }

        return null;
    }

    private static bool TryGetColumnByteCapacity(
        ColumnDescriptor column,
        LengthSemantics sessionSemantics,
        OracleLengthContext context,
        out long capacity,
        out bool byteSemantics)
    {
        capacity = 0;
        byteSemantics = column.CharUsed == "B"
            || string.Equals(column.CharUsed, "BYTE", StringComparison.OrdinalIgnoreCase)
            || (string.IsNullOrEmpty(column.CharUsed) && sessionSemantics == LengthSemantics.Byte);
        if (byteSemantics)
        {
            if (!column.MaxLength.HasValue)
            {
                return false;
            }

            capacity = column.MaxLength.Value;
            return true;
        }

        // CHAR semantics: CHAR_LENGTH characters, but never more than the type's byte ceiling.
        var typeCeiling = OracleCharsets.MaxColumnBytes(column.DataType, context.MaxStringSize);
        if (!column.CharLength.HasValue || column.CharLength.Value <= 0 || typeCeiling is null)
        {
            return false;
        }

        var charset = ResolveCharset(column, context);
        capacity = Math.Min((long)column.CharLength.Value * OracleCharsets.MaxBytesPerCharacter(charset), typeCeiling.Value);
        return true;
    }

    private static string? ResolveCharset(ColumnDescriptor column, OracleLengthContext context)
    {
        if (!string.IsNullOrWhiteSpace(column.Charset))
        {
            return column.Charset;
        }

        return OracleCharsets.IsNationalType(column.DataType) ? context.NationalCharset : context.DatabaseCharset;
    }

    /// <summary>
    /// Reads the EF unicode facet from <c>Annotations["IsUnicode"]</c> (emitted by <c>EfModelSource</c>) or EF's own
    /// <c>Unicode</c> annotation; absent means Unicode (EF's default for <c>string</c>).
    /// </summary>
    private static bool IsUnicode(PropertyDescriptor property)
    {
        if (property.Annotations is null)
        {
            return true;
        }

        foreach (var key in new[] { "IsUnicode", "Unicode" })
        {
            if (property.Annotations.TryGetValue(key, out var value) && value is not null)
            {
                return value switch
                {
                    bool flag => flag,
                    string text when bool.TryParse(text, out var parsed) => parsed,
                    _ => true,
                };
            }
        }

        return true;
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
    Byte,
}

/// <summary>
/// Rule: Length mismatch between entity and Oracle column.
/// </summary>
public class LengthExceedsColumnRule : ContractRuleBase
{
    public override string RuleId => "DG007";

    public override string Name => "Entity Length Exceeds Column Length";

    public override DiagnosticSeverity Severity => DiagnosticSeverity.Error;

    public override string Description => "Entity property MaxLength exceeds Oracle column MaxLength";

    protected override Task ValidateCoreAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        List<ContractViolation> violations,
        CancellationToken cancellationToken)
    {
        violations.AddRange(LengthMismatchRuleHelper.Detect(contract, allContracts, RuleId));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Rule: Byte-length overflow risk in BYTE semantics.
/// </summary>
public class ByteLengthOverflowRiskRule : ContractRuleBase
{
    public override string RuleId => "DG008";

    public override string Name => "Byte Length Overflow Risk";

    public override DiagnosticSeverity Severity => DiagnosticSeverity.Warning;

    public override string Description => "Entity property may exceed Oracle column byte capacity in BYTE semantics";

    protected override Task ValidateCoreAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        List<ContractViolation> violations,
        CancellationToken cancellationToken)
    {
        violations.AddRange(LengthMismatchRuleHelper.Detect(contract, allContracts, RuleId));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Rule: Inferred size fallback risk (NVARCHAR2(2000) fallback).
/// </summary>
public class InferredSizeFallbackRule : ContractRuleBase
{
    public override string RuleId => "DG009";

    public override string Name => "Inferred Size Fallback Risk";

    public override DiagnosticSeverity Severity => DiagnosticSeverity.Warning;

    public override string Description => "EF Core infers NVARCHAR2(2000) which may cause ORA-12899";

    protected override Task ValidateCoreAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        List<ContractViolation> violations,
        CancellationToken cancellationToken)
    {
        violations.AddRange(LengthMismatchRuleHelper.Detect(contract, allContracts, RuleId));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Shared detection logic for the three length-mismatch rules.
/// </summary>
internal static class LengthMismatchRuleHelper
{
    public static IReadOnlyList<ContractViolation> Detect(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        string ruleId)
    {
        if (contract is not EntityDescriptor entity || string.IsNullOrEmpty(entity.TableName))
        {
            return Array.Empty<ContractViolation>();
        }

        var schema = allContracts.OfType<DatabaseSchemaDescriptor>().FirstOrDefault();
        if (schema == null)
        {
            return Array.Empty<ContractViolation>();
        }

        var table = FindTable(schema.Tables, entity.TableName);
        if (table == null)
        {
            return Array.Empty<ContractViolation>();
        }

        var semantics = string.Equals(schema.LengthSemantics, "BYTE", StringComparison.OrdinalIgnoreCase)
            ? LengthSemantics.Byte : LengthSemantics.Char;
        var context = schema is OracleDatabaseSchemaDescriptor oracle
            ? new OracleLengthContext(oracle.DatabaseCharset, oracle.NationalCharset, oracle.MaxStringSize)
            : OracleLengthContext.Unknown;

        return new LengthMismatchDetector().Detect(entity, table.Columns, semantics, context)
            .Where(v => v.RuleId == ruleId)
            .ToList();
    }

    /// <summary>
    /// Resolves an EF table name (<c>TABLE</c>, <c>SCHEMA.TABLE</c>, optionally quoted) against catalog tables whose name
    /// may itself be bare or schema-qualified and whose <see cref="DatabaseTableDescriptor.Schema"/> may be set. Exact
    /// (ordinal) name matches win over canonical (upper-folded) matches; a schema written on either side must agree; an
    /// ambiguous bare name resolves to nothing.
    /// </summary>
    /// <returns>The single matching table, or null.</returns>
    internal static DatabaseTableDescriptor? FindTable(IReadOnlyList<DatabaseTableDescriptor> tables, string entityTableName)
    {
        var wanted = SchemaObjectName.Parse(entityTableName);
        if (wanted.Name.Length == 0)
        {
            return null;
        }

        var candidates = tables
            .Select(table => (Table: table, Parts: SchemaObjectName.Parse(table.Name)))
            .Select(item => (item.Table, Name: item.Parts.Name, Schema: item.Table.Schema ?? item.Parts.Schema))
            .Where(item => wanted.Schema is null || item.Schema is null ||
                string.Equals(SchemaObjectName.Canonical("oracle", item.Schema), SchemaObjectName.Canonical("oracle", wanted.Schema), StringComparison.Ordinal))
            .ToList();

        var exact = candidates.Where(item => string.Equals(item.Name, wanted.Name, StringComparison.Ordinal)).ToList();
        var matches = exact.Count > 0
            ? exact
            : candidates.Where(item => string.Equals(
                SchemaObjectName.Canonical("oracle", item.Name),
                SchemaObjectName.Canonical("oracle", wanted.Name),
                StringComparison.Ordinal)).ToList();

        if (matches.Count > 1 && wanted.Schema is not null)
        {
            matches = matches.Where(item => item.Schema is not null).ToList();
        }

        return matches.Count == 1 ? matches[0].Table : null;
    }
}
