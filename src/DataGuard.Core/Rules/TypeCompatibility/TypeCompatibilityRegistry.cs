using System.Collections.Concurrent;

namespace DataGuard.Core.Rules.TypeCompatibility;

/// <summary>
/// Process-wide lookup of provider type tables for Core code that only knows a provider key
/// (<see cref="LiveSqlShapeValidationRule"/>, <see cref="ParameterTypeMatchRule.IsTypeCompatible"/>, rules built without
/// an injected table). <c>ProviderRuleCatalog</c> registers the adapter table for the provider it builds rules for.
/// Core references no provider driver, so an unregistered provider falls back to <see cref="UnknownTypeCompatibility"/>
/// (no findings; borrowing another provider's table produced false findings, red-team H6/A1). This applies to Oracle too:
/// library callers that use the registry (for example <see cref="ParameterTypeMatchRule.IsTypeCompatible"/> with
/// <c>isOracle: true</c>) register <c>OracleTypeCompatibility.Instance</c> from the Oracle adapter first.
/// </summary>
public static class TypeCompatibilityRegistry
{
    private static readonly ConcurrentDictionary<string, ITypeCompatibility> Tables = new(StringComparer.Ordinal);

    /// <summary>Registers (or replaces) the table for <see cref="ITypeCompatibility.Provider"/>.</summary>
    /// <param name="table">The provider table.</param>
    public static void Register(ITypeCompatibility table)
    {
        ArgumentNullException.ThrowIfNull(table);
        Tables[NormalizeProvider(table.Provider)] = table;
    }

    /// <summary>Returns the registered table for <paramref name="provider"/>, or <see cref="UnknownTypeCompatibility"/>.</summary>
    /// <param name="provider">Provider key (sqlserver, oracle, postgresql/postgres, mysql); null means sqlserver.</param>
    /// <returns>A table; never null.</returns>
    public static ITypeCompatibility Resolve(string? provider)
    {
        var key = NormalizeProvider(provider);
        if (Tables.TryGetValue(key, out var table))
        {
            return table;
        }

        return UnknownTypeCompatibility.For(key);
    }

    /// <summary>Maps provider spellings to the canonical key.</summary>
    /// <param name="provider">Provider key or null.</param>
    /// <returns>sqlserver, oracle, postgresql, mysql, or the lower-cased input.</returns>
    public static string NormalizeProvider(string? provider)
    {
        var key = provider?.Trim().ToLowerInvariant() ?? string.Empty;
        return key switch
        {
            "" or "mssql" or "sql server" => "sqlserver",
            "postgres" or "pg" or "npgsql" => "postgresql",
            _ => key,
        };
    }
}
