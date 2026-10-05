using DataGuard.Core.Rules.TypeCompatibility;
using C = DataGuard.Core.Rules.TypeCompatibility.ClrTypeNames;

namespace DataGuard.MySql.Adapter;

/// <summary>
/// MySQL CLR ↔ type table (MySqlConnector / Pomelo mappings). <c>tinyint(1)</c>, <c>bit(1)</c> and <c>bool</c> map to
/// <c>bool</c>; a bare <c>tinyint</c> (MySQL 8 no longer reports the display width) leaves <c>bool</c> unknown;
/// <c>char(36)</c> and <c>binary(16)</c> map to <c>Guid</c>. Signed and unsigned integers of the same width are accepted.
/// </summary>
public sealed class MySqlTypeCompatibility : MappedTypeCompatibility
{
    /// <summary>Gets the shared stateless instance.</summary>
    public static MySqlTypeCompatibility Instance { get; } = new();

    /// <inheritdoc/>
    public override string Provider => "mysql";

    /// <inheritdoc/>
    protected override TypeCompatibilityResult Evaluate(string clr, DbTypeInfo db) => db.Base switch
    {
        "int" or "integer" or "mediumint" => OneOf(clr, C.Int, C.UInt),
        "bigint" => OneOf(clr, C.Long, C.ULong),
        "smallint" => OneOf(clr, C.Short, C.UShort),
        "tinyint" => TinyInt(clr, db),
        "bit" when db.Length is null or 1 => OneOf(clr, C.Bool, C.ULong),
        "bit" => OneOf(clr, C.ULong),
        "bool" or "boolean" => OneOf(clr, C.Bool),
        "decimal" or "numeric" or "dec" or "fixed" => OneOf(clr, C.Decimal),
        "double" or "double precision" or "real" => OneOf(clr, C.Double),
        "float" when db.Precision is > 24 => OneOf(clr, C.Double),
        "float" => OneOf(clr, C.Float, C.Double),
        "varchar" or "char" or "text" or "tinytext" or "mediumtext" or "longtext" or "nvarchar" or "nchar"
            or "enum" or "set" or "json" => Character(clr, db),
        "datetime" or "timestamp" => OneOf(clr, C.DateTime),
        "date" => OneOf(clr, C.DateOnly, C.DateTime),
        "time" => OneOf(clr, C.TimeSpan, C.TimeOnly),
        "year" => OneOf(clr, C.Int, C.Short),
        "binary" => Binary(clr, db),
        "varbinary" or "blob" or "tinyblob" or "mediumblob" or "longblob" => OneOf(clr, C.ByteArray),
        _ => TypeCompatibilityResult.Unknown,
    };

    private static TypeCompatibilityResult TinyInt(string clr, DbTypeInfo db)
    {
        if (clr == C.Bool)
        {
            return db.Length switch
            {
                1 => TypeCompatibilityResult.Compatible,
                null => TypeCompatibilityResult.Unknown,
                _ => TypeCompatibilityResult.Incompatible,
            };
        }

        return OneOf(clr, C.Byte, C.SByte);
    }
}
