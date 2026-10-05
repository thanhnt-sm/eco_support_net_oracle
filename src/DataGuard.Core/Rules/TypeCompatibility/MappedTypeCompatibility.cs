namespace DataGuard.Core.Rules.TypeCompatibility;

/// <summary>
/// Base for table-driven provider type tables: normalizes the CLR spelling and parses the database type, then asks the
/// provider which canonical CLR keys the database type accepts.
/// </summary>
public abstract class MappedTypeCompatibility : ITypeCompatibility
{
    /// <inheritdoc/>
    public abstract string Provider { get; }

    /// <inheritdoc/>
    public TypeCompatibilityResult Check(string? clrType, string? dbType, int? precision = null, int? scale = null, int? maxLength = null)
    {
        var clr = ClrTypeNames.Normalize(clrType);
        var db = DbTypeInfo.Parse(dbType, precision, scale, maxLength);
        if (clr is null || db is null || db.IsArray || db.Base.Length == 0)
        {
            return TypeCompatibilityResult.Unknown;
        }

        return Evaluate(clr, db);
    }

    /// <summary>Decides compatibility for a canonical CLR key and a parsed database type.</summary>
    /// <param name="clr">Canonical CLR key (see <see cref="ClrTypeNames"/>).</param>
    /// <param name="db">Parsed database type.</param>
    /// <returns>The outcome; <see cref="TypeCompatibilityResult.Unknown"/> for database types the table does not know.</returns>
    protected abstract TypeCompatibilityResult Evaluate(string clr, DbTypeInfo db);

    /// <summary>Returns <see cref="TypeCompatibilityResult.Compatible"/> when <paramref name="clr"/> is one of <paramref name="accepted"/>.</summary>
    /// <param name="clr">Canonical CLR key.</param>
    /// <param name="accepted">Accepted canonical CLR keys.</param>
    /// <returns>Compatible or Incompatible.</returns>
    protected static TypeCompatibilityResult OneOf(string clr, params string[] accepted) =>
        accepted.Contains(clr, StringComparer.Ordinal) ? TypeCompatibilityResult.Compatible : TypeCompatibilityResult.Incompatible;

    /// <summary>Returns <see cref="TypeCompatibilityResult.Compatible"/> when <paramref name="clr"/> is one of <paramref name="accepted"/>.</summary>
    /// <param name="clr">Canonical CLR key.</param>
    /// <param name="accepted">Accepted canonical CLR keys.</param>
    /// <returns>Compatible or Incompatible.</returns>
    protected static TypeCompatibilityResult OneOf(string clr, IEnumerable<string> accepted) =>
        accepted.Contains(clr, StringComparer.Ordinal) ? TypeCompatibilityResult.Compatible : TypeCompatibilityResult.Incompatible;

    /// <summary>
    /// Character types: <c>string</c>/<c>char</c> are compatible; <c>Guid</c> is compatible when the length is 32, 36 or 38
    /// characters and unknown otherwise (catalogs report character lengths in bytes or characters depending on the provider
    /// and charset, so a GUID-in-text column is never reported as incompatible).
    /// </summary>
    /// <param name="clr">Canonical CLR key.</param>
    /// <param name="db">Parsed database type.</param>
    /// <returns>The outcome.</returns>
    protected static TypeCompatibilityResult Character(string clr, DbTypeInfo db)
    {
        if (clr == ClrTypeNames.Guid)
        {
            return db.Length is 32 or 36 or 38 ? TypeCompatibilityResult.Compatible : TypeCompatibilityResult.Unknown;
        }

        return OneOf(clr, ClrTypeNames.String, ClrTypeNames.Char);
    }

    /// <summary>
    /// Fixed binary types: <c>byte[]</c> is compatible; <c>Guid</c> is compatible at length 16, unknown when the length is
    /// not reported, and incompatible otherwise.
    /// </summary>
    /// <param name="clr">Canonical CLR key.</param>
    /// <param name="db">Parsed database type.</param>
    /// <returns>The outcome.</returns>
    protected static TypeCompatibilityResult Binary(string clr, DbTypeInfo db)
    {
        if (clr == ClrTypeNames.Guid)
        {
            return db.Length switch
            {
                null when !db.IsMax => TypeCompatibilityResult.Unknown,
                16 => TypeCompatibilityResult.Compatible,
                _ => TypeCompatibilityResult.Incompatible,
            };
        }

        return OneOf(clr, ClrTypeNames.ByteArray);
    }
}
