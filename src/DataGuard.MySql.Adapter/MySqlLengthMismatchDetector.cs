using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules;
using Microsoft.CodeAnalysis;

namespace DataGuard.MySql.Adapter;

/// <summary>
/// MySQL column type enum — mirrors the MySQL type system for type-safe comparisons.
/// </summary>
public enum MySqlColumnType
{
    TinyInt,
    SmallInt,
    MediumInt,
    Int,
    BigInt,
    Float,
    Double,
    Decimal,
    Char,
    VarChar,
    Binary,
    VarBinary,
    TinyText,
    Text,
    MediumText,
    LongText,
    TinyBlob,
    Blob,
    MediumBlob,
    LongBlob,
    Date,
    DateTime,
    TimeStamp,
    Time,
    Year,
    Enum,
    Set,
    Json,
    Geometry,
}

/// <summary>
/// Factory for creating MySqlColumnType values from MySQL type strings.
/// </summary>
public static class MySqlColumnTypeFactory
{
    public static MySqlColumnType? FromString(string dataType)
    {
        return dataType.ToUpperInvariant().Trim() switch
        {
            "TINYINT" => MySqlColumnType.TinyInt,
            "SMALLINT" => MySqlColumnType.SmallInt,
            "MEDIUMINT" => MySqlColumnType.MediumInt,
            "INT" or "INTEGER" => MySqlColumnType.Int,
            "BIGINT" => MySqlColumnType.BigInt,
            "FLOAT" => MySqlColumnType.Float,
            "DOUBLE" or "DOUBLE PRECISION" or "REAL" => MySqlColumnType.Double,
            "DECIMAL" or "NUMERIC" or "FIXED" => MySqlColumnType.Decimal,
            "CHAR" => MySqlColumnType.Char,
            "VARCHAR" or "CHARACTER VARYING" => MySqlColumnType.VarChar,
            "BINARY" => MySqlColumnType.Binary,
            "VARBINARY" => MySqlColumnType.VarBinary,
            "TINYTEXT" => MySqlColumnType.TinyText,
            "TEXT" => MySqlColumnType.Text,
            "MEDIUMTEXT" => MySqlColumnType.MediumText,
            "LONGTEXT" => MySqlColumnType.LongText,
            "TINYBLOB" => MySqlColumnType.TinyBlob,
            "BLOB" => MySqlColumnType.Blob,
            "MEDIUMBLOB" => MySqlColumnType.MediumBlob,
            "LONGBLOB" => MySqlColumnType.LongBlob,
            "DATE" => MySqlColumnType.Date,
            "DATETIME" => MySqlColumnType.DateTime,
            "TIMESTAMP" => MySqlColumnType.TimeStamp,
            "TIME" => MySqlColumnType.Time,
            "YEAR" => MySqlColumnType.Year,
            "ENUM" => MySqlColumnType.Enum,
            "SET" => MySqlColumnType.Set,
            "JSON" => MySqlColumnType.Json,
            "GEOMETRY" or "POINT" or "LINESTRING" or "POLYGON" => MySqlColumnType.Geometry,
            _ => null
        };
    }
}

/// <summary>
/// Detects length mismatches between entity properties and MySQL columns.
/// Handles UTF-8mb4 (4 bytes per char) vs UTF-8 (3 bytes per char) byte semantics,
/// and detects LONGTEXT/MEDIUMTEXT overflow risks.
/// </summary>
public sealed class MySqlLengthMismatchDetector
{
    /// <summary>
    /// Maximum character lengths for MySQL TEXT family types.
    /// </summary>
    private static readonly Dictionary<string, long> TextTypeMaxChars = new(StringComparer.OrdinalIgnoreCase)
    {
        { "TINYTEXT", 255 },
        { "TEXT", 65_535 },
        { "MEDIUMTEXT", 16_777_215 },
        { "LONGTEXT", 4_294_967_295L },
    };

    /// <summary>
    /// Maximum byte lengths for MySQL BLOB family types.
    /// </summary>
    private static readonly Dictionary<string, long> BlobTypeMaxBytes = new(StringComparer.OrdinalIgnoreCase)
    {
        { "TINYBLOB", 255 },
        { "BLOB", 65_535 },
        { "MEDIUMBLOB", 16_777_215 },
        { "LONGBLOB", 4_294_967_295L },
    };

    /// <summary>
    /// Detects all length-related mismatches between an entity and its MySQL columns.
    /// Yields violations for:
    /// 1. Direct length mismatch (entity MaxLength > column char length)
    /// 2. UTF-8mb4 byte overflow risk (entity MaxLength × 4 bytes > column byte capacity)
    /// 3. TEXT type overflow risk (entity MaxLength > TEXT family max chars)
    /// </summary>
    public IEnumerable<ContractViolation> Detect(
        EntityDescriptor entity,
        IReadOnlyList<ColumnDescriptor> columns)
    {
        foreach (var property in entity.Properties)
        {
            var column = columns.FirstOrDefault(c =>
                string.Equals(c.Name, property.ColumnName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.Name, property.Name, StringComparison.OrdinalIgnoreCase));

            if (column == null)
            {
                continue;
            }

            var dataType = column.DataType.ToUpperInvariant();

            // 1. Direct length mismatch: entity MaxLength (chars) > column char length.
            //    MySQL INFORMATION_SCHEMA.COLUMNS.CHARACTER_MAXIMUM_LENGTH is in characters.
            if (property.MaxLength.HasValue && column.MaxLength.HasValue)
            {
                if (property.MaxLength.Value > column.MaxLength.Value)
                {
                    yield return new ContractViolation(
                        "MY004",
                        $"Entity property '{property.Name}' MaxLength={property.MaxLength.Value} " +
                        $"exceeds column '{column.Name}' ({dataType}) max length={column.MaxLength.Value}",
                        DiagnosticSeverity.Error,
                        null,
                        new Dictionary<string, object?>
                        {
                            { "property", property.Name },
                            { "entityMaxLength", property.MaxLength.Value },
                            { "columnMaxLength", column.MaxLength.Value },
                            { "columnType", dataType },
                        });
                }
            }

            // 2. UTF-8mb4 byte overflow risk.
            //    MySQL's utf8mb4 uses up to 4 bytes per character. VARCHAR(N) stores
            //    N characters but the byte size depends on the charset. If the entity
            //    stores Unicode strings, the actual byte consumption may exceed what
            //    the column can hold when the charset is utf8mb4.
            if (property.MaxLength.HasValue && IsStringType(property.ClrTypeName))
            {
                var charSetName = column.CharUsed ?? "utf8mb4";
                var bytesPerChar = MySqlDialectChecker.GetBytesPerChar(charSetName);

                // For VARCHAR/CHAR, MySQL limits the byte size to 65535 per column
                if (dataType is "VARCHAR" or "CHAR" && column.MaxLength.HasValue)
                {
                    var maxBytesForColumn = (long)column.MaxLength.Value * bytesPerChar;
                    if (maxBytesForColumn > 65_535)
                    {
                        yield return new ContractViolation(
                            "MY005",
                            $"UTF-8mb4 byte overflow risk: column '{column.Name}' ({dataType}({column.MaxLength.Value})) " +
                            $"requires {maxBytesForColumn} bytes with {charSetName} charset, " +
                            $"exceeding MySQL's 65535-byte column limit. " +
                            $"Consider using TEXT type or reducing column length.",
                            DiagnosticSeverity.Warning,
                            null,
                            new Dictionary<string, object?>
                            {
                                { "column", column.Name },
                                { "columnType", dataType },
                                { "declaredCharLength", column.MaxLength.Value },
                                { "bytesPerChar", bytesPerChar },
                                { "totalBytes", maxBytesForColumn },
                                { "charSet", charSetName },
                            });
                    }
                }

                // Check if entity MaxLength × bytesPerChar exceeds the byte capacity
                // of the column's declared type (for VARCHAR/CHAR columns)
                if (dataType is "VARCHAR" or "CHAR" && column.MaxLength.HasValue)
                {
                    var entityMaxBytes = (long)property.MaxLength.Value * bytesPerChar;
                    var columnMaxBytes = (long)column.MaxLength.Value * bytesPerChar;

                    if (entityMaxBytes > 65_535 && entityMaxBytes > columnMaxBytes)
                    {
                        yield return new ContractViolation(
                            "MY005",
                            $"Byte overflow risk: property '{property.Name}' MaxLength={property.MaxLength.Value} " +
                            $"may consume up to {entityMaxBytes} bytes with {charSetName} charset, " +
                            $"exceeding column '{column.Name}' byte capacity of {columnMaxBytes}",
                            DiagnosticSeverity.Warning,
                            null,
                            new Dictionary<string, object?>
                            {
                                { "property", property.Name },
                                { "entityMaxLength", property.MaxLength.Value },
                                { "entityMaxBytes", entityMaxBytes },
                                { "columnMaxBytes", columnMaxBytes },
                                { "charSet", charSetName },
                            });
                    }
                }
            }

            // 3. TEXT type overflow risk: entity MaxLength > TEXT family max chars.
            //    When a column uses TEXT/MEDIUMTEXT/LONGTEXT, the entity should not
            //    declare a MaxLength that exceeds the type's character capacity.
            if (property.MaxLength.HasValue && TextTypeMaxChars.TryGetValue(dataType, out var dbMaxChars))
            {
                if (property.MaxLength.Value > dbMaxChars)
                {
                    yield return new ContractViolation(
                        "MY006",
                        $"Entity property '{property.Name}' MaxLength={property.MaxLength.Value} " +
                        $"exceeds MySQL {dataType} maximum of {dbMaxChars} characters. " +
                        $"Consider upgrading to a larger TEXT type or removing the MaxLength constraint.",
                        DiagnosticSeverity.Warning,
                        null,
                        new Dictionary<string, object?>
                        {
                            { "property", property.Name },
                            { "entityMaxLength", property.MaxLength.Value },
                            { "dbMaxChars", dbMaxChars },
                            { "dbType", dataType },
                        });
                }
            }

            // 4. BLOB type overflow risk for byte[] properties.
            if (property.MaxLength.HasValue &&
                IsBinaryType(property.ClrTypeName) &&
                BlobTypeMaxBytes.TryGetValue(dataType, out var dbMaxBytes))
            {
                if (property.MaxLength.Value > dbMaxBytes)
                {
                    yield return new ContractViolation(
                        "MY006",
                        $"Entity property '{property.Name}' MaxLength={property.MaxLength.Value} bytes " +
                        $"exceeds MySQL {dataType} maximum of {dbMaxBytes} bytes.",
                        DiagnosticSeverity.Warning,
                        null,
                        new Dictionary<string, object?>
                        {
                            { "property", property.Name },
                            { "entityMaxLength", property.MaxLength.Value },
                            { "dbMaxBytes", dbMaxBytes },
                            { "dbType", dataType },
                        });
                }
            }

            // 5. Inferred type fallback risk: string property with no MaxLength
            //    EF Core MySQL provider defaults to VARCHAR(255) when no MaxLength is set.
            //    If the column is TEXT/MEDIUMTEXT/LONGTEXT, values > 255 chars will fail.
            if (!property.MaxLength.HasValue && IsStringType(property.ClrTypeName))
            {
                if (TextTypeMaxChars.ContainsKey(dataType))
                {
                    yield return new ContractViolation(
                        "MY007",
                        $"EF Core will infer VARCHAR(255) for property '{property.Name}' " +
                        $"(no MaxLength set) but MySQL column '{column.Name}' is {dataType}. " +
                        $"If values exceed 255 characters, data truncation or errors will occur at runtime. " +
                        $"Consider setting explicit MaxLength or using a matching column type.",
                        DiagnosticSeverity.Warning,
                        null,
                        new Dictionary<string, object?>
                        {
                            { "property", property.Name },
                            { "inferredType", "VARCHAR(255)" },
                            { "dbColumnType", dataType },
                        });
                }
            }
        }
    }

    private static bool IsStringType(string? clrTypeName)
    {
        return clrTypeName switch
        {
            "string" => true,
            "System.String" => true,
            _ => false
        };
    }

    private static bool IsBinaryType(string? clrTypeName)
    {
        return clrTypeName switch
        {
            "byte[]" => true,
            "System.Byte[]" => true,
            _ => false
        };
    }
}

/// <summary>
/// Rule MY004: Entity MaxLength exceeds MySQL column character length.
/// </summary>
public class MySqlLengthExceedsColumnRule : ContractRuleBase
{
    public override string RuleId => "MY004";

    public override string Name => "Entity Length Exceeds MySQL Column Length";

    public override DiagnosticSeverity Severity => DiagnosticSeverity.Error;

    public override string Description => "Entity property MaxLength exceeds MySQL column CHARACTER_MAXIMUM_LENGTH";

    protected override Task ValidateCoreAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        List<ContractViolation> violations,
        CancellationToken cancellationToken)
    {
        violations.AddRange(MySqlLengthMismatchRuleHelper.Detect(contract, allContracts, RuleId));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Rule MY005: UTF-8mb4 byte overflow risk.
/// </summary>
public class MySqlUtf8mb4ByteOverflowRule : ContractRuleBase
{
    public override string RuleId => "MY005";

    public override string Name => "UTF-8mb4 Byte Overflow Risk";

    public override DiagnosticSeverity Severity => DiagnosticSeverity.Warning;

    public override string Description => "Column may exceed MySQL 65535-byte row limit with utf8mb4 charset";

    protected override Task ValidateCoreAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        List<ContractViolation> violations,
        CancellationToken cancellationToken)
    {
        violations.AddRange(MySqlLengthMismatchRuleHelper.Detect(contract, allContracts, RuleId));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Rule MY006: TEXT/BLOB type overflow risk.
/// </summary>
public class MySqlTextOverflowRule : ContractRuleBase
{
    public override string RuleId => "MY006";

    public override string Name => "TEXT/BLOB Type Overflow Risk";

    public override DiagnosticSeverity Severity => DiagnosticSeverity.Warning;

    public override string Description => "Entity MaxLength exceeds MySQL TEXT/BLOB family type maximum";

    protected override Task ValidateCoreAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        List<ContractViolation> violations,
        CancellationToken cancellationToken)
    {
        violations.AddRange(MySqlLengthMismatchRuleHelper.Detect(contract, allContracts, RuleId));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Rule MY007: Inferred VARCHAR(255) fallback risk.
/// </summary>
public class MySqlInferredSizeFallbackRule : ContractRuleBase
{
    public override string RuleId => "MY007";

    public override string Name => "Inferred Size Fallback Risk";

    public override DiagnosticSeverity Severity => DiagnosticSeverity.Warning;

    public override string Description => "EF Core infers VARCHAR(255) which may cause truncation with TEXT columns";

    protected override Task ValidateCoreAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        List<ContractViolation> violations,
        CancellationToken cancellationToken)
    {
        violations.AddRange(MySqlLengthMismatchRuleHelper.Detect(contract, allContracts, RuleId));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Shared detection logic for MySQL length-mismatch rules.
/// Follows the same pattern as Oracle's LengthMismatchRuleHelper.
/// </summary>
internal static class MySqlLengthMismatchRuleHelper
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

        var table = schema.Tables.FirstOrDefault(t =>
            string.Equals(t.Name, entity.TableName, StringComparison.OrdinalIgnoreCase));
        if (table == null)
        {
            return Array.Empty<ContractViolation>();
        }

        return new MySqlLengthMismatchDetector().Detect(entity, table.Columns)
            .Where(v => v.RuleId == ruleId)
            .ToList();
    }
}
