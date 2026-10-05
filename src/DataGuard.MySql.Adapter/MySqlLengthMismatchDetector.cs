using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules;
using DataGuard.Core.Rules.Sql;
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
/// Character columns are limited in characters (MY004); TEXT family columns are limited in bytes (MY006);
/// the row limit of 65,535 bytes applies to the declared widths of all VARCHAR/CHAR columns (MY005);
/// a bounded VARCHAR/CHAR column behind a string property without MaxLength is MY007.
/// </summary>
public sealed class MySqlLengthMismatchDetector
{
    /// <summary>MySQL's maximum row size in bytes (TEXT/BLOB columns count only their 9–12 byte pointers).</summary>
    public const int MaxRowSizeBytes = 65_535;

    /// <summary>
    /// Maximum byte lengths for MySQL TEXT family types (L + 1..4 length bytes; the limits are bytes, not characters).
    /// </summary>
    private static readonly Dictionary<string, long> TextTypeMaxBytes = new(StringComparer.OrdinalIgnoreCase)
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

    /// <summary>Returns the byte limit of a TEXT family type (TINYTEXT, TEXT, MEDIUMTEXT, LONGTEXT).</summary>
    /// <param name="dataType">Column data type.</param>
    /// <param name="maxBytes">The limit in bytes.</param>
    /// <returns>True for a TEXT family type.</returns>
    internal static bool TryGetTextTypeMaxBytes(string? dataType, out long maxBytes)
    {
        maxBytes = 0;
        return !string.IsNullOrWhiteSpace(dataType) && TextTypeMaxBytes.TryGetValue(dataType.Trim(), out maxBytes);
    }

    /// <summary>
    /// Worst-case bytes one UTF-16 code unit (one <c>char</c> of a .NET string) needs in <paramref name="charset"/>:
    /// utf8mb4/utf8mb3/utf8 = 3 (a supplementary character is 2 units / 4 bytes), ucs2/utf16/utf16le = 2, utf32 = 4,
    /// single-byte sets = 1, other multibyte sets = their maximum character width, unknown = 3.
    /// </summary>
    /// <returns>Bytes per UTF-16 code unit.</returns>
    public static int BytesPerUtf16Unit(string? charset)
    {
        var name = charset?.Trim().ToLowerInvariant() ?? string.Empty;
        return name switch
        {
            "utf8mb4" or "utf8mb3" or "utf8" => 3,
            "ucs2" or "utf16" or "utf16le" => 2,
            "utf32" => 4,
            "gbk" or "big5" or "sjis" or "cp932" or "euckr" or "gb2312" => 2,
            "ujis" or "eucjpms" => 3,
            "gb18030" => 4,
            _ when MySqlDialectChecker.GetBytesPerChar(name) == 1 => 1,
            _ => 3,
        };
    }

    /// <summary>
    /// Detects all length-related mismatches between an entity and its MySQL columns.
    /// Yields violations for:
    /// 1. MY004: entity MaxLength (characters) &gt; column CHARACTER_MAXIMUM_LENGTH.
    /// 2. MY006: entity MaxLength × bytes per UTF-16 unit &gt; TEXT family byte limit, or byte[] MaxLength &gt; BLOB limit.
    /// 3. MY007: string property without MaxLength (Pomelo maps it to <c>longtext</c>) over a bounded VARCHAR/CHAR column.
    /// 4. MY005: the row the entity implies (each VARCHAR/CHAR column at the entity's MaxLength, else the column's own
    ///    length, × the charset's maximum character width, + length bytes) exceeds 65,535 bytes. Index-prefix limits
    ///    (3072/767 bytes) need index metadata that the schema descriptor does not carry, so they are not checked.
    /// </summary>
    public IEnumerable<ContractViolation> Detect(
        EntityDescriptor entity,
        IReadOnlyList<ColumnDescriptor> columns)
    {
        var mapped = new Dictionary<ColumnDescriptor, PropertyDescriptor>(ReferenceEqualityComparer.Instance);
        foreach (var property in entity.Properties)
        {
            var column = FindColumn(property, columns);
            if (column == null)
            {
                continue;
            }

            mapped.TryAdd(column, property);
            var dataType = column.DataType.ToUpperInvariant();
            var charset = ResolveCharset(column);
            var isString = IsStringType(property.ClrTypeName);

            // 1. Direct length mismatch: entity MaxLength (chars) > column char length.
            //    MySQL INFORMATION_SCHEMA.COLUMNS.CHARACTER_MAXIMUM_LENGTH is in characters.
            if (property.MaxLength.HasValue && column.MaxLength.HasValue && !TextTypeMaxBytes.ContainsKey(dataType)
                && property.MaxLength.Value > column.MaxLength.Value)
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
                        { "column", column.Name },
                        { "entityMaxLength", property.MaxLength.Value },
                        { "columnMaxLength", column.MaxLength.Value },
                        { "columnType", dataType },
                    });
            }

            // 2. TEXT family byte overflow: the limits are bytes, a .NET char costs up to BytesPerUtf16Unit bytes.
            if (property.MaxLength.HasValue && TextTypeMaxBytes.TryGetValue(dataType, out var dbMaxBytes))
            {
                var bytesPerUnit = isString ? BytesPerUtf16Unit(charset) : 1;
                var entityMaxBytes = (long)property.MaxLength.Value * bytesPerUnit;
                if (entityMaxBytes > dbMaxBytes)
                {
                    yield return new ContractViolation(
                        "MY006",
                        $"Entity property '{property.Name}' MaxLength={property.MaxLength.Value} may need {entityMaxBytes} bytes " +
                        $"({bytesPerUnit} per character in {charset}) but MySQL {dataType} holds at most {dbMaxBytes} bytes. " +
                        $"Consider a larger TEXT type or a smaller MaxLength.",
                        DiagnosticSeverity.Warning,
                        null,
                        new Dictionary<string, object?>
                        {
                            { "property", property.Name },
                            { "column", column.Name },
                            { "entityMaxLength", property.MaxLength.Value },
                            { "entityMaxBytes", entityMaxBytes },
                            { "dbMaxBytes", dbMaxBytes },
                            { "charSet", charset },
                            { "dbType", dataType },
                        });
                }
            }

            // 3. BLOB type overflow risk for byte[] properties.
            if (property.MaxLength.HasValue &&
                IsBinaryType(property.ClrTypeName) &&
                BlobTypeMaxBytes.TryGetValue(dataType, out var blobMaxBytes) &&
                property.MaxLength.Value > blobMaxBytes)
            {
                yield return new ContractViolation(
                    "MY006",
                    $"Entity property '{property.Name}' MaxLength={property.MaxLength.Value} bytes " +
                    $"exceeds MySQL {dataType} maximum of {blobMaxBytes} bytes.",
                    DiagnosticSeverity.Warning,
                    null,
                    new Dictionary<string, object?>
                    {
                        { "property", property.Name },
                        { "column", column.Name },
                        { "entityMaxLength", property.MaxLength.Value },
                        { "dbMaxBytes", blobMaxBytes },
                        { "dbType", dataType },
                    });
            }

            // 4. Pomelo maps a string without MaxLength to longtext: the model believes the value is unbounded while the
            //    database column is VARCHAR(n)/CHAR(n), so values longer than n fail (strict mode) or are truncated.
            if (!property.MaxLength.HasValue && isString && dataType is ("VARCHAR" or "CHAR") && column.MaxLength.HasValue)
            {
                yield return new ContractViolation(
                    "MY007",
                    $"String property '{property.Name}' has no MaxLength (EF Core/Pomelo maps it to longtext) " +
                    $"but MySQL column '{column.Name}' is {dataType}({column.MaxLength.Value}); values longer than " +
                    $"{column.MaxLength.Value} characters fail or are truncated at runtime. Set MaxLength({column.MaxLength.Value}).",
                    DiagnosticSeverity.Warning,
                    null,
                    new Dictionary<string, object?>
                    {
                        { "property", property.Name },
                        { "column", column.Name },
                        { "inferredType", "longtext" },
                        { "dbColumnType", $"{dataType}({column.MaxLength.Value})" },
                        { "columnMaxLength", column.MaxLength.Value },
                    });
            }
        }

        var rowSize = ImpliedRowSize(columns, mapped, out var contributors);
        if (rowSize > MaxRowSizeBytes)
        {
            yield return new ContractViolation(
                "MY005",
                $"Row size risk: entity '{entity.Name}' implies {rowSize} bytes of VARCHAR/CHAR data in table " +
                $"'{entity.TableName}', exceeding MySQL's {MaxRowSizeBytes}-byte row limit; the CREATE/ALTER TABLE " +
                $"from this model fails (ERROR 1118). Use TEXT for the widest columns or reduce MaxLength.",
                DiagnosticSeverity.Warning,
                null,
                new Dictionary<string, object?>
                {
                    { "entity", entity.Name },
                    { "table", entity.TableName },
                    { "rowBytes", rowSize },
                    { "rowLimitBytes", MaxRowSizeBytes },
                    { "columns", contributors },
                });
        }
    }

    /// <summary>
    /// Bytes the VARCHAR/CHAR columns occupy in a row when each is declared at the mapped property's MaxLength (or its own
    /// length when unmapped): width × maximum character width of its charset, plus 1–2 length bytes for VARCHAR.
    /// </summary>
    private static long ImpliedRowSize(
        IReadOnlyList<ColumnDescriptor> columns,
        IReadOnlyDictionary<ColumnDescriptor, PropertyDescriptor> mapped,
        out IReadOnlyList<string> contributors)
    {
        long total = 0;
        var names = new List<string>();
        foreach (var column in columns)
        {
            var dataType = column.DataType.ToUpperInvariant();
            if (dataType is not ("VARCHAR" or "CHAR"))
            {
                continue;
            }

            var width = mapped.TryGetValue(column, out var property) && property.MaxLength.HasValue
                ? property.MaxLength.Value
                : column.MaxLength;
            if (width is null or <= 0)
            {
                continue;
            }

            var bytes = (long)width.Value * MySqlDialectChecker.GetBytesPerChar(ResolveCharset(column));
            if (dataType == "VARCHAR")
            {
                bytes += bytes > 255 ? 2 : 1;
            }

            total += bytes;
            names.Add(column.Name);
        }

        contributors = names;
        return total;
    }

    private static ColumnDescriptor? FindColumn(PropertyDescriptor property, IReadOnlyList<ColumnDescriptor> columns)
    {
        var candidates = new List<string>(3);
        if (!string.IsNullOrEmpty(property.ColumnName))
        {
            candidates.Add(property.ColumnName);
        }

        candidates.Add(property.Name);
        candidates.Add(ToSnakeCase(property.Name));
        foreach (var candidate in candidates)
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

    private static string ToSnakeCase(string name)
    {
        var builder = new System.Text.StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (i > 0 && char.IsUpper(c) && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1]) ||
                (char.IsUpper(name[i - 1]) && i + 1 < name.Length && char.IsLower(name[i + 1]))))
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Column charset: <see cref="ColumnDescriptor.Charset"/>; snapshots written before it existed carried the charset in
    /// <see cref="ColumnDescriptor.CharUsed"/>, which is honored when it is not an Oracle B/C marker; default utf8mb4.
    /// </summary>
    internal static string ResolveCharset(ColumnDescriptor column)
    {
        if (!string.IsNullOrWhiteSpace(column.Charset))
        {
            return column.Charset;
        }

        return column.CharUsed is { Length: > 1 } legacy ? legacy : "utf8mb4";
    }

    internal static bool IsStringType(string? clrTypeName)
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
/// Rule MY005: row size implied by the entity exceeds MySQL's 65,535-byte limit.
/// </summary>
public class MySqlUtf8mb4ByteOverflowRule : ContractRuleBase
{
    public override string RuleId => "MY005";

    public override string Name => "Row Size Overflow Risk";

    public override DiagnosticSeverity Severity => DiagnosticSeverity.Warning;

    public override string Description => "VARCHAR/CHAR widths implied by the entity exceed MySQL's 65535-byte row limit (charset-aware)";

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
/// Rule MY007: unbounded string property over a bounded VARCHAR/CHAR column.
/// </summary>
public class MySqlInferredSizeFallbackRule : ContractRuleBase
{
    public override string RuleId => "MY007";

    public override string Name => "Inferred Size Fallback Risk";

    public override DiagnosticSeverity Severity => DiagnosticSeverity.Warning;

    public override string Description => "String property without MaxLength (Pomelo maps it to longtext) is stored in a bounded VARCHAR/CHAR column";

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

        var table = FindTable(schema.Tables, entity.TableName);
        if (table == null)
        {
            return Array.Empty<ContractViolation>();
        }

        return new MySqlLengthMismatchDetector().Detect(entity, table.Columns)
            .Where(v => v.RuleId == ruleId)
            .ToList();
    }

    /// <summary>
    /// Resolves an EF table name (<c>table</c> or <c>schema.table</c>, optionally quoted) against catalog tables. An exact
    /// (ordinal) match wins, which keeps <c>Orders</c> and <c>orders</c> apart under <c>lower_case_table_names = 0</c>;
    /// otherwise names compare through <see cref="SchemaObjectName.Canonical(string?, string?)"/>. A schema written on both
    /// sides must agree; ambiguity resolves to nothing.
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
                string.Equals(SchemaObjectName.Canonical("mysql", item.Schema), SchemaObjectName.Canonical("mysql", wanted.Schema), StringComparison.Ordinal))
            .ToList();

        var exact = candidates.Where(item => string.Equals(item.Name, wanted.Name, StringComparison.Ordinal)).ToList();
        var matches = exact.Count > 0
            ? exact
            : candidates.Where(item => string.Equals(
                SchemaObjectName.Canonical("mysql", item.Name),
                SchemaObjectName.Canonical("mysql", wanted.Name),
                StringComparison.Ordinal)).ToList();

        return matches.Count == 1 ? matches[0].Table : null;
    }
}
