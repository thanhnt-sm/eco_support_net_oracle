using DataGuard.Core.Rules.TypeCompatibility;
using C = DataGuard.Core.Rules.TypeCompatibility.ClrTypeNames;

namespace DataGuard.Oracle.Adapter;

/// <summary>
/// Oracle CLR ↔ type table (ODP.NET / EF Core Oracle mappings). <c>NUMBER</c> is precision/scale aware: unconstrained
/// <c>NUMBER</c> (what <c>ALL_ARGUMENTS</c> reports for PL/SQL parameters) accepts every numeric type and <c>bool</c>;
/// <c>NUMBER(1)</c> accepts <c>bool</c>; <c>NUMBER(p,0)</c> accepts integral types (range is a length concern, not a type
/// mismatch: EF maps <c>int</c> to <c>NUMBER(10)</c>); <c>NUMBER(p,s&gt;0)</c> rejects integral types and <c>bool</c>.
/// <c>REF CURSOR</c>, object and collection types are <see cref="TypeCompatibilityResult.Unknown"/>.
/// </summary>
public sealed class OracleTypeCompatibility : MappedTypeCompatibility
{
    private static readonly string[] NumbersAndBool = C.Numbers.Append(C.Bool).ToArray();
    private static readonly string[] IntegersAndDecimal = C.Integers.Append(C.Decimal).ToArray();
    private static readonly string[] Fractional = { C.Decimal, C.Double, C.Float };

    /// <summary>Gets the shared stateless instance.</summary>
    public static OracleTypeCompatibility Instance { get; } = new();

    /// <inheritdoc/>
    public override string Provider => "oracle";

    /// <inheritdoc/>
    protected override TypeCompatibilityResult Evaluate(string clr, DbTypeInfo db) => db.Base switch
    {
        "number" or "decimal" or "numeric" or "dec" => Number(clr, db),
        "integer" or "int" or "smallint" or "pls_integer" or "binary_integer" or "natural" or "naturaln"
            or "positive" or "positiven" or "signtype" or "simple_integer" => OneOf(clr, IntegersAndDecimal),
        "float" or "double precision" or "real" => OneOf(clr, Fractional),
        "binary_double" => OneOf(clr, C.Double),
        "binary_float" => OneOf(clr, C.Float, C.Double),
        "boolean" or "pl/sql boolean" => OneOf(clr, C.Bool),
        "varchar2" or "nvarchar2" or "char" or "nchar" or "varchar" or "clob" or "nclob" or "long"
            or "rowid" or "urowid" or "xmltype" or "json" => Character(clr, db),
        "date" => OneOf(clr, C.DateTime, C.DateOnly),
        "timestamp" => OneOf(clr, C.DateTime),
        "timestamp with time zone" or "timestamp with local time zone" => OneOf(clr, C.DateTime, C.DateTimeOffset),
        "interval day to second" => OneOf(clr, C.TimeSpan),
        "raw" => Binary(clr, db),
        "blob" or "long raw" or "bfile" => OneOf(clr, C.ByteArray),
        _ => TypeCompatibilityResult.Unknown,
    };

    private static TypeCompatibilityResult Number(string clr, DbTypeInfo db)
    {
        if (db.Scale is > 0)
        {
            return OneOf(clr, Fractional);
        }

        if (db.Precision is null && db.Scale is null)
        {
            return OneOf(clr, NumbersAndBool);
        }

        return db.Precision == 1 ? OneOf(clr, NumbersAndBool) : OneOf(clr, C.Numbers);
    }
}
