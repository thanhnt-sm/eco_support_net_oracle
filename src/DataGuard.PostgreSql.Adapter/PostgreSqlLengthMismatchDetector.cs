using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules;
using DataGuard.Core.Rules.Sql;
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
/// Detects length mismatches between entity properties and PostgreSQL columns. PostgreSQL limits
/// <c>character varying(n)</c> / <c>character(n)</c> in characters (never bytes), so no byte-capacity check applies.
/// Each property yields at most one PG003, chosen in this order: entity length over the column length, entity length
/// over the VARCHAR maximum, MaxLength on an unlimited column (Info), no MaxLength over a bounded VARCHAR (Warning).
/// </summary>
public sealed class PostgreSqlLengthMismatchDetector
{
    /// <summary>
    /// PostgreSQL VARCHAR maximum length (10 MB in characters).
    /// See: https://www.postgresql.org/docs/current/datatype-character.html
    /// </summary>
    public const int PgVarcharMaxLength = 10_485_760;

    /// <summary>
    /// Detects all length-related mismatches between an entity and its PostgreSQL columns.
    /// </summary>
    public IEnumerable<ContractViolation> Detect(
        EntityDescriptor entity,
        IReadOnlyList<ColumnDescriptor> columns)
    {
        foreach (var property in entity.Properties)
        {
            var column = FindColumn(property, columns);
            if (column == null)
            {
                continue;
            }

            var violation = DetectProperty(property, column);
            if (violation != null)
            {
                yield return violation;
            }
        }
    }

    /// <summary>
    /// Candidate column names for a property in lookup order: the EF column name, the property name, and the property
    /// name in snake_case (<c>CustomerId</c> ⇒ <c>customer_id</c>, the EFCore.NamingConventions / Npgsql convention).
    /// </summary>
    /// <returns>Distinct candidates.</returns>
    public static IReadOnlyList<string> CandidateColumnNames(PropertyDescriptor property)
    {
        ArgumentNullException.ThrowIfNull(property);
        var candidates = new List<string>(3);
        if (!string.IsNullOrEmpty(property.ColumnName))
        {
            candidates.Add(property.ColumnName);
        }

        candidates.Add(property.Name);
        candidates.Add(ToSnakeCase(property.Name));
        return candidates.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>Converts PascalCase to lower snake_case treating acronyms as one word (<c>CustomerID</c> ⇒ <c>customer_id</c>).</summary>
    /// <returns>The snake-case name.</returns>
    public static string ToSnakeCase(string name)
    {
        var builder = new System.Text.StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (i > 0 && char.IsUpper(c))
            {
                var previous = name[i - 1];
                var nextIsLower = i + 1 < name.Length && char.IsLower(name[i + 1]);
                if (char.IsLower(previous) || char.IsDigit(previous) || (char.IsUpper(previous) && nextIsLower))
                {
                    builder.Append('_');
                }
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    private static ColumnDescriptor? FindColumn(PropertyDescriptor property, IReadOnlyList<ColumnDescriptor> columns)
    {
        foreach (var candidate in CandidateColumnNames(property))
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

    private static ContractViolation? DetectProperty(PropertyDescriptor property, ColumnDescriptor column)
    {
        var pgType = PostgreSqlColumnTypeFactory.Resolve(column.DataType);

        // 1. Direct length mismatch: entity MaxLength > column character_maximum_length.
        //    Only applies to types that have a meaningful length constraint.
        if (property.MaxLength.HasValue && column.MaxLength.HasValue
            && !PostgreSqlColumnTypeFactory.IsUnlimitedType(pgType)
            && property.MaxLength.Value > column.MaxLength.Value)
        {
            return new ContractViolation(
                "PG003",
                $"Entity property '{property.Name}' MaxLength={property.MaxLength.Value} " +
                $"exceeds PostgreSQL column '{column.Name}' ({column.DataType}) length={column.MaxLength.Value}",
                DiagnosticSeverity.Error,
                null,
                new Dictionary<string, object?>
                {
                    { "property", property.Name },
                    { "column", column.Name },
                    { "entityMaxLength", property.MaxLength.Value },
                    { "columnMaxLength", column.MaxLength.Value },
                    { "columnType", column.DataType },
                });
        }

        // 2. VARCHAR exceeds PostgreSQL maximum (10485760).
        if (property.MaxLength.HasValue
            && pgType == PostgreSqlColumnType.VarChar
            && property.MaxLength.Value > PgVarcharMaxLength)
        {
            return new ContractViolation(
                "PG003",
                $"Entity property '{property.Name}' MaxLength={property.MaxLength.Value} " +
                $"exceeds PostgreSQL VARCHAR maximum of {PgVarcharMaxLength:N0} characters",
                DiagnosticSeverity.Error,
                null,
                new Dictionary<string, object?>
                {
                    { "property", property.Name },
                    { "column", column.Name },
                    { "entityMaxLength", property.MaxLength.Value },
                    { "pgVarcharMax", PgVarcharMaxLength },
                });
        }

        // 3. TEXT/JSONB type mismatch: entity has MaxLength but column is unlimited type.
        //    PostgreSQL TEXT/JSONB columns have no length limit, but the entity
        //    constrains MaxLength — this is a design mismatch (entity is more
        //    restrictive than DB, which is safe but may indicate confusion).
        if (property.MaxLength.HasValue
            && PostgreSqlColumnTypeFactory.IsUnlimitedType(pgType))
        {
            return new ContractViolation(
                "PG003",
                $"Entity property '{property.Name}' has MaxLength={property.MaxLength.Value} " +
                $"but PostgreSQL column '{column.Name}' is {column.DataType} (unlimited length). " +
                $"The MaxLength constraint is enforced only at the application level, not by the database.",
                DiagnosticSeverity.Info,
                null,
                new Dictionary<string, object?>
                {
                    { "property", property.Name },
                    { "column", column.Name },
                    { "entityMaxLength", property.MaxLength.Value },
                    { "columnType", column.DataType },
                    { "columnIsUnlimited", true },
                });
        }

        // 4. No MaxLength on entity but column is VARCHAR(n) — entity could write
        //    arbitrarily long strings that exceed the column limit.
        if (!property.MaxLength.HasValue
            && pgType == PostgreSqlColumnType.VarChar
            && column.MaxLength.HasValue)
        {
            return new ContractViolation(
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
                    { "column", column.Name },
                    { "columnType", column.DataType },
                    { "columnMaxLength", column.MaxLength.Value },
                    { "inferredType", "character varying" },
                });
        }

        return null;
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

        var table = FindTable(schema.Tables, entity.TableName);
        if (table == null)
        {
            return Array.Empty<ContractViolation>();
        }

        return new PostgreSqlLengthMismatchDetector().Detect(entity, table.Columns)
            .Where(v => v.RuleId == ruleId)
            .ToList();
    }

    /// <summary>
    /// Resolves an EF table name (<c>table</c> or <c>schema.table</c>, optionally quoted) against catalog tables. PostgreSQL
    /// names are case-sensitive, so an exact (ordinal) match wins; otherwise names are compared through
    /// <see cref="SchemaObjectName.Canonical(string?, string?)"/>. A schema on both sides must agree; ambiguity resolves to nothing.
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
                string.Equals(item.Schema, wanted.Schema, StringComparison.Ordinal) ||
                string.Equals(SchemaObjectName.Canonical("postgresql", item.Schema), SchemaObjectName.Canonical("postgresql", wanted.Schema), StringComparison.Ordinal))
            .ToList();

        var exact = candidates.Where(item => string.Equals(item.Name, wanted.Name, StringComparison.Ordinal)).ToList();
        var matches = exact.Count > 0
            ? exact
            : candidates.Where(item => string.Equals(
                SchemaObjectName.Canonical("postgresql", item.Name),
                SchemaObjectName.Canonical("postgresql", wanted.Name),
                StringComparison.Ordinal)).ToList();

        return matches.Count == 1 ? matches[0].Table : null;
    }
}
