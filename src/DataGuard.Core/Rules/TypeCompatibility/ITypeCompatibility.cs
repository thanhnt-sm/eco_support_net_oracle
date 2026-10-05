namespace DataGuard.Core.Rules.TypeCompatibility;

/// <summary>Outcome of comparing a CLR type with a database type.</summary>
public enum TypeCompatibilityResult
{
    /// <summary>Either side is not understood by the provider table; rules must not report a finding.</summary>
    Unknown,

    /// <summary>The CLR type is a valid mapping for the database type.</summary>
    Compatible,

    /// <summary>Both types are known and the CLR type is not a valid mapping for the database type.</summary>
    Incompatible,
}

/// <summary>
/// Provider-specific CLR ↔ database type compatibility table. Implementations live with their provider
/// (SQL Server in Core until the adapter split, Oracle/PostgreSQL/MySQL in their adapters) and are injected into the
/// parameter rules by <c>ProviderRuleCatalog</c>.
/// </summary>
public interface ITypeCompatibility
{
    /// <summary>Gets the provider key this table describes (sqlserver, oracle, postgresql, mysql).</summary>
    string Provider { get; }

    /// <summary>Compares <paramref name="clrType"/> with <paramref name="dbType"/>.</summary>
    /// <param name="clrType">CLR type in any spelling <see cref="ClrTypeNames.Normalize"/> accepts.</param>
    /// <param name="dbType">Database type as reported by the catalog, with or without length/precision arguments.</param>
    /// <param name="precision">Numeric precision when the catalog reports it separately.</param>
    /// <param name="scale">Numeric scale when the catalog reports it separately.</param>
    /// <param name="maxLength">Length when the catalog reports it separately.</param>
    /// <returns><see cref="TypeCompatibilityResult.Unknown"/> whenever either type is not understood.</returns>
    TypeCompatibilityResult Check(string? clrType, string? dbType, int? precision = null, int? scale = null, int? maxLength = null);
}
