using DataGuard.Core.Rules.TypeCompatibility;
using C = DataGuard.Core.Rules.TypeCompatibility.ClrTypeNames;

namespace DataGuard.PostgreSql.Adapter;

/// <summary>
/// PostgreSQL CLR ↔ type table (Npgsql mappings). Npgsql sends strongly typed parameters and PostgreSQL resolves routine
/// overloads by argument type, so integral widths are matched exactly. Arrays, enums, composites, ranges and other
/// extension types are <see cref="TypeCompatibilityResult.Unknown"/>.
/// </summary>
public sealed class PostgreSqlTypeCompatibility : MappedTypeCompatibility
{
    /// <summary>Gets the shared stateless instance.</summary>
    public static PostgreSqlTypeCompatibility Instance { get; } = new();

    /// <inheritdoc/>
    public override string Provider => "postgresql";

    /// <inheritdoc/>
    protected override TypeCompatibilityResult Evaluate(string clr, DbTypeInfo db) => db.Base switch
    {
        "integer" or "int" or "int4" or "serial" or "serial4" => OneOf(clr, C.Int),
        "bigint" or "int8" or "bigserial" or "serial8" => OneOf(clr, C.Long),
        "smallint" or "int2" or "smallserial" or "serial2" => OneOf(clr, C.Short, C.Byte, C.SByte),
        "boolean" or "bool" => OneOf(clr, C.Bool),
        "numeric" or "decimal" or "money" => OneOf(clr, C.Decimal),
        "double precision" or "float8" => OneOf(clr, C.Double),
        "float" when db.Precision is > 0 and <= 24 => OneOf(clr, C.Float),
        "float" => OneOf(clr, C.Double),
        "real" or "float4" => OneOf(clr, C.Float),
        "text" or "varchar" or "character varying" or "char" or "character" or "bpchar" or "citext" or "name"
            or "json" or "jsonb" or "xml" => Character(clr, db),
        "timestamp" or "timestamp without time zone" => OneOf(clr, C.DateTime),
        "timestamptz" or "timestamp with time zone" => OneOf(clr, C.DateTime, C.DateTimeOffset),
        "date" => OneOf(clr, C.DateOnly, C.DateTime),
        "time" or "time without time zone" => OneOf(clr, C.TimeOnly, C.TimeSpan),
        "timetz" or "time with time zone" => OneOf(clr, C.DateTimeOffset),
        "interval" => OneOf(clr, C.TimeSpan),
        "uuid" => OneOf(clr, C.Guid),
        "bytea" => OneOf(clr, C.ByteArray),
        _ => TypeCompatibilityResult.Unknown,
    };
}
