using C = DataGuard.Core.Rules.TypeCompatibility.ClrTypeNames;

namespace DataGuard.Core.Rules.TypeCompatibility;

/// <summary>
/// SQL Server CLR ↔ type table (SqlClient mappings). Lives in Core until the SQL Server adapter split (plan 4.1).
/// </summary>
public sealed class SqlServerTypeCompatibility : MappedTypeCompatibility
{
    /// <summary>Gets the shared stateless instance.</summary>
    public static SqlServerTypeCompatibility Instance { get; } = new();

    /// <inheritdoc/>
    public override string Provider => "sqlserver";

    /// <inheritdoc/>
    protected override TypeCompatibilityResult Evaluate(string clr, DbTypeInfo db) => db.Base switch
    {
        "int" => OneOf(clr, C.Int),
        "bigint" => OneOf(clr, C.Long),
        "smallint" => OneOf(clr, C.Short),
        "tinyint" => OneOf(clr, C.Byte),
        "bit" => OneOf(clr, C.Bool),
        "decimal" or "numeric" or "money" or "smallmoney" => OneOf(clr, C.Decimal),
        "float" when db.Precision is > 0 and <= 24 => OneOf(clr, C.Float, C.Double),
        "float" => OneOf(clr, C.Double),
        "real" => OneOf(clr, C.Float),
        "nvarchar" or "varchar" or "nchar" or "char" or "text" or "ntext" or "sysname" or "xml" => Character(clr, db),
        "datetime" or "datetime2" or "smalldatetime" => OneOf(clr, C.DateTime),
        "date" => OneOf(clr, C.DateOnly, C.DateTime),
        "time" => OneOf(clr, C.TimeOnly, C.TimeSpan),
        "datetimeoffset" => OneOf(clr, C.DateTimeOffset),
        "uniqueidentifier" => OneOf(clr, C.Guid),
        "binary" => Binary(clr, db),
        "varbinary" or "image" or "timestamp" or "rowversion" => OneOf(clr, C.ByteArray),
        _ => TypeCompatibilityResult.Unknown,
    };
}
