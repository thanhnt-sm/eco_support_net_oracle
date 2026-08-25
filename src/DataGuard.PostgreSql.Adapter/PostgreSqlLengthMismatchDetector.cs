using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules;
using Microsoft.CodeAnalysis;

namespace DataGuard.PostgreSql.Adapter;

/// <summary>
/// PostgreSQL column type enum for type-aware length checking.
/// </summary>
public enum PostgreSqlColumnType
{
    /// <summary>character varying(n) — max 10485760 chars.</summary>
    VarChar,

    /// <summary>char(n) — fixed-length, padded.</summary>
    Char,

    /// <summary>text — unlimited length.</summary>
    Text,

    /// <summary>json — unlimited length.</summary>
    Json,

    /// <summary>jsonb — binary JSON, unlimited length.</summary>
    Jsonb,

    /// <summary>bytea — binary data, unlimited.</summary>
    Bytea,

    /// <summary>uuid — 16 bytes fixed.</summary>
    Uuid,

    /// <summary>integer / int4 — 4 bytes.</summary>
    Integer,

    /// <summary>bigint / int8 — 8 bytes.</summary>
    BigInt,

    /// <summary>smallint / int2 — 2 bytes.</summary>
    SmallInt,

    /// <summary>numeric / decimal — variable precision.</summary>
    Numeric,

    /// <summary>real / float4 — 4 bytes.</summary>
    Real,

    /// <summary>double precision / float8 — 8 bytes.</summary>
    DoublePrecision,

    /// <summary>date — 4 bytes.</summary>
    Date,

    /// <summary>timestamp — 8 bytes.</summary>
    Timestamp,

    /// <summary>timestamptz — 8 bytes.</summary>
    TimestampTz,

    /// <summary>time — 8 bytes.</summary>
    Time,

    /// <summary>interval — 16 bytes.</summary>
    Interval,

    /// <summary>boolean — 1 byte.</summary>
    Boolean,

    /// <summary>Other / unknown type.</summary>
    Other,
}

/// <summary>
/// Factory for PostgreSQL column type resolution.
/// </summary>
public static class PostgreSqlColumnTypeFactory
{
    /// <summary>
    /// Resolves a PostgreSQL type name string to a PostgreSqlColumnType enum.
    /// </summary>
    public static PostgreSqlColumnType Resolve(string dataType)
    {
        return dataType.ToLowerInvariant().Trim() switch
        {
            "character varying" or "varchar" => PostgreSqlColumnType.VarChar,
            "character" or "char" => PostgreSqlColumnType.Char,
            "text" => PostgreSqlColumnType.Text,
            "json" => PostgreSqlColumnType.Json,
            "jsonb" => PostgreSqlColumnType.Jsonb,
            "bytea" => PostgreSqlColumnType.Bytea,
            "uuid" => PostgreSqlColumnType.Uuid,
            "integer" or "int" or "int4" or "serial" => PostgreSqlColumnType.Integer,
            "bigint" or "int8" or "bigserial" => PostgreSqlColumnType.BigInt,
            "smallint" or "int2" or "smallserial" => PostgreSqlColumnType.SmallInt,
            "numeric" or "decimal" => PostgreSqlColumnType.Numeric,
            "real" or "float4" => PostgreSqlColumnType.Real,
            "double precision" or "float8" => PostgreSqlColumnType.DoublePrecision,
            "date" => PostgreSqlColumnType.Date,
            "timestamp without time zone" or "timestamp" => PostgreSqlColumnType.Timestamp,
            "timestamp with time zone" or "timestamptz" => PostgreSqlColumnType.TimestampTz,
            "time without time zone" or "time" => PostgreSqlColumnType.Time,
            "interval" => PostgreSqlColumnType.Interval,
            "boolean" or "bool" => PostgreSqlColumnType.Boolean,
            _ => PostgreSqlColumnType.Other,
        };
    }

    /// <summary>
    /// Returns true if the type has unlimited/varlen storage (no meaningful MaxLength).
    /// </summary>
    public static bool IsUnlimitedType(PostgreSqlColumnType type)
    {
        return type is PostgreSqlColumnType.Text
            or PostgreSqlColumnType.Json
            or PostgreSqlColumnType.Jsonb
            or PostgreSqlColumnType.Bytea;
    }

    /// <summary>
    /// Returns true if the type is a string type that supports character_length.
    /// </summary>
    public static bool IsStringType(PostgreSqlColumnType type)
    {
        return type is PostgreSqlColumnType.VarChar
            or PostgreSqlColumnType.Char
            or PostgreSqlColumnType.Text;
    }
}

/// <summary>
/// Detects length mismatches between entity properties and PostgreSQL columns.
/// Handles UTF-8 encoding (up to 4 bytes per character) and TEXT/JSONB type mismatches.
/// </summary>
public sealed class PostgreSqlLengthMismatchDetector
{
    /// <summary>
    /// PostgreSQL VARCHAR maximum length (10 MB in characters).
    /// See: https://www.postgresql.org/docs/current/datatype-character.html
    /// </summary>
    public const int PgVarcharMaxLength = 10_485_760;

    /// <summary>
    /// Maximum bytes per character in UTF-8 encoding (supplementary plane chars).
    /// </summary>
    private const int MaxUtf8BytesPerChar = 4;

    /// <summary>
    /// Detects all length-related mismatches between an entity and its PostgreSQL columns.
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

            var pgType = PostgreSqlColumnTypeFactory.Resolve(column.DataType);

            // 1. Direct length mismatch: entity MaxLength > column character_maximum_length.
            //    Only applies to types that have a meaningful length constraint.
            if (property.MaxLength.HasValue && column.MaxLength.HasValue
                && !PostgreSqlColumnTypeFactory.IsUnlimitedType(pgType))
            {
                if (property.MaxLength.Value > column.MaxLength.Value)
                {
                    yield return new ContractViolation(
                        "PG003",
                        $"Entity property '{property.Name}' MaxLength={property.MaxLength.Value} " +
                        $"exceeds PostgreSQL column '{column.Name}' ({column.DataType}) length={column.MaxLength.Value}",
                        DiagnosticSeverity.Error,
                        null,
                        new Dictionary<string, object?>
                        {
                            { "property", property.Name },
                            { "entityMaxLength", property.MaxLength.Value },
                            { "columnMaxLength", column.MaxLength.Value },
                            { "columnType", column.DataType },
                        });
                }
            }

            // 2. VARCHAR exceeds PostgreSQL maximum (10485760).
            if (property.MaxLength.HasValue
                && pgType == PostgreSqlColumnType.VarChar
                && property.MaxLength.Value > PgVarcharMaxLength)
            {
                yield return new ContractViolation(
                    "PG003",
                    $"Entity property '{property.Name}' MaxLength={property.MaxLength.Value} " +
                    $"exceeds PostgreSQL VARCHAR maximum of {PgVarcharMaxLength:N0} characters",
                    DiagnosticSeverity.Error,
                    null,
                    new Dictionary<string, object?>
                    {
                        { "property", property.Name },
                        { "entityMaxLength", property.MaxLength.Value },
                        { "pgVarcharMax", PgVarcharMaxLength },
                    });
            }

            // 3. UTF-8 byte-length overflow risk.
            //    PostgreSQL stores data in the database encoding (usually UTF-8).
            //    A VARCHAR(n) allows n characters, but each character can be up to 4 bytes.
            //    If the column has a byte-length limit (not character limit), entity
            //    MaxLength * 4 could exceed it.
            if (property.MaxLength.HasValue && column.MaxLength.HasValue
                && IsUnicodeType(property.ClrTypeName)
                && PostgreSqlColumnTypeFactory.IsStringType(pgType))
            {
                var entityMaxBytes = property.MaxLength.Value * MaxUtf8BytesPerChar;

                // PostgreSQL character_maximum_length is in characters, not bytes.
                // But if the column was defined with a byte limit (e.g. via raw DDL),
                // the effective byte capacity is char_length * 4 for UTF-8.
                // We flag when entity MaxLength > column char length (already caught above)
                // AND when the entity could produce more bytes than the column can store.
                if (column.MaxLength.Value > 0 && property.MaxLength.Value > column.MaxLength.Value)
                {
                    var columnMaxBytes = column.MaxLength.Value * MaxUtf8BytesPerChar;
                    yield return new ContractViolation(
                        "PG003",
                        $"UTF-8 overflow risk: property '{property.Name}' MaxLength={property.MaxLength.Value} " +
                        $"may produce up to {entityMaxBytes:N0} bytes but column '{column.Name}' " +
                        $"allows {column.MaxLength.Value} chars (~{columnMaxBytes:N0} bytes max)",
                        DiagnosticSeverity.Warning,
                        null,
                        new Dictionary<string, object?>
                        {
                            { "property", property.Name },
                            { "entityMaxBytes", entityMaxBytes },
                            { "columnMaxChars", column.MaxLength.Value },
                            { "columnMaxBytes", columnMaxBytes },
                            { "encoding", "UTF-8" },
                        });
                }
            }

            // 4. TEXT/JSONB type mismatch: entity has MaxLength but column is unlimited type.
            //    PostgreSQL TEXT/JSONB columns have no length limit, but the entity
            //    constrains MaxLength — this is a design mismatch (entity is more
            //    restrictive than DB, which is safe but may indicate confusion).
            if (property.MaxLength.HasValue
                && PostgreSqlColumnTypeFactory.IsUnlimitedType(pgType))
            {
                yield return new ContractViolation(
                    "PG003",
                    $"Entity property '{property.Name}' has MaxLength={property.MaxLength.Value} " +
                    $"but PostgreSQL column '{column.Name}' is {column.DataType} (unlimited length). " +
                    $"The MaxLength constraint is enforced only at the application level, not by the database.",
                    DiagnosticSeverity.Info,
                    null,
                    new Dictionary<string, object?>
                    {
                        { "property", property.Name },
                        { "entityMaxLength", property.MaxLength.Value },
                        { "columnType", column.DataType },
                        { "columnIsUnlimited", true },
                    });
            }

            // 5. No MaxLength on entity but column is VARCHAR(n) — entity could write
            //    arbitrarily long strings that exceed the column limit.
            if (!property.MaxLength.HasValue
                && pgType == PostgreSqlColumnType.VarChar
                && column.MaxLength.HasValue)
            {
                yield return new ContractViolation(
                    "PG003",
                    $"Entity property '{property.Name}' has no MaxLength but PostgreSQL column " +
                    $"'{column.Name}' is VARCHAR({column.MaxLength.Value}). " +
                    $"EF Core Npgsql will infer character varying (unlimited) — values exceeding " +
                    $"{column.MaxLength.Value} characters will cause a runtime error.",
                    DiagnosticSeverity.Warning,
                    null,
                    new Dictionary<string, object?>
                    {
                        { "property", property.Name },
                        { "columnType", column.DataType },
                        { "columnMaxLength", column.MaxLength.Value },
                        { "inferredType", "character varying" },
                    });
            }
        }
    }

    private static bool IsUnicodeType(string? clrTypeName)
    {
        return clrTypeName switch
        {
            "string" => true,
            "System.String" => true,
            _ => false,
        };
    }
}

// ── ContractRuleBase rules ─────────────────────────────────────────────────

/// <summary>
/// Rule PG003: Entity MaxLength exceeds PostgreSQL column length.
/// </summary>
public class PostgreSqlLengthExceedsColumnRule : ContractRuleBase
{
    public override string RuleId => "PG003";

    public override string Name => "Entity Length Exceeds PostgreSQL Column Length";

    public override DiagnosticSeverity Severity => DiagnosticSeverity.Error;

    public override string Description => "Entity property MaxLength exceeds PostgreSQL column length";

    protected override Task ValidateCoreAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        List<ContractViolation> violations,
        CancellationToken cancellationToken)
    {
        violations.AddRange(PostgreSqlLengthMismatchRuleHelper.Detect(contract, allContracts, "PG003"));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Shared detection logic for PostgreSQL length-mismatch rules.
/// Mirrors the Oracle LengthMismatchRuleHelper pattern.
/// </summary>
internal static class PostgreSqlLengthMismatchRuleHelper
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

        return new PostgreSqlLengthMismatchDetector().Detect(entity, table.Columns)
            .Where(v => v.RuleId == ruleId)
            .ToList();
    }
}
