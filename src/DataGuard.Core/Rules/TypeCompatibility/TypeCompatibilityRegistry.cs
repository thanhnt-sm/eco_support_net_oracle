using System.Collections.Concurrent;

namespace DataGuard.Core.Rules.TypeCompatibility;

/// <summary>
/// Process-wide lookup of provider type tables for Core code that only knows a provider key
/// (<see cref="LiveSqlShapeValidationRule"/>, <see cref="ParameterTypeMatchRule.IsTypeCompatible"/>, rules built without
/// an injected table). <c>ProviderRuleCatalog</c> registers the adapter table for the provider it builds rules for.
/// Unregistered providers fall back to the SQL Server table (sqlserver), the pre-3.1 Oracle map (oracle) or a table that
/// answers <see cref="TypeCompatibilityResult.Unknown"/> for everything (postgresql, mysql: borrowing the SQL Server table
/// produced false findings, red-team H6).
/// </summary>
public static class TypeCompatibilityRegistry
{
    private static readonly ConcurrentDictionary<string, ITypeCompatibility> Tables = new(StringComparer.Ordinal);
    private static readonly ITypeCompatibility UnknownPostgreSql = new UnknownTypeCompatibility("postgresql");
    private static readonly ITypeCompatibility UnknownMySql = new UnknownTypeCompatibility("mysql");

    /// <summary>Registers (or replaces) the table for <see cref="ITypeCompatibility.Provider"/>.</summary>
    /// <param name="table">The provider table.</param>
    public static void Register(ITypeCompatibility table)
    {
        ArgumentNullException.ThrowIfNull(table);
        Tables[NormalizeProvider(table.Provider)] = table;
    }

    /// <summary>Returns the registered table for <paramref name="provider"/>, or the built-in fallback.</summary>
    /// <param name="provider">Provider key (sqlserver, oracle, postgresql/postgres, mysql) or null for SQL Server.</param>
    /// <returns>A table; never null.</returns>
    public static ITypeCompatibility Resolve(string? provider)
    {
        var key = NormalizeProvider(provider);
        if (Tables.TryGetValue(key, out var table))
        {
            return table;
        }

        return key switch
        {
            "oracle" => LegacyOracleTypeCompatibility.Instance,
            "postgresql" => UnknownPostgreSql,
            "mysql" => UnknownMySql,
            _ => SqlServerTypeCompatibility.Instance,
        };
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

    private sealed class UnknownTypeCompatibility(string provider) : ITypeCompatibility
    {
        public string Provider { get; } = provider;

        public TypeCompatibilityResult Check(string? clrType, string? dbType, int? precision = null, int? scale = null, int? maxLength = null) =>
            TypeCompatibilityResult.Unknown;
    }
}

/// <summary>
/// The pre-3.1 Oracle map, kept only as the fallback when the Oracle adapter table has not been registered
/// (library callers and rules constructed without a provider). Remove when the rules move into the adapters (plan 4.1).
/// </summary>
internal sealed class LegacyOracleTypeCompatibility : ITypeCompatibility
{
    private static readonly Dictionary<string, string[]> Map = new(StringComparer.Ordinal)
    {
        [ClrTypeNames.Int] = new[] { "NUMBER", "INTEGER", "INT" },
        [ClrTypeNames.Long] = new[] { "NUMBER", "BIGINT" },
        [ClrTypeNames.Short] = new[] { "NUMBER", "SMALLINT" },
        [ClrTypeNames.Byte] = new[] { "NUMBER" },
        [ClrTypeNames.Bool] = new[] { "NUMBER(1)" },
        [ClrTypeNames.Decimal] = new[] { "NUMBER", "DECIMAL", "NUMERIC" },
        [ClrTypeNames.Double] = new[] { "BINARY_DOUBLE", "FLOAT" },
        [ClrTypeNames.Float] = new[] { "BINARY_FLOAT" },
        [ClrTypeNames.String] = new[] { "VARCHAR2", "NVARCHAR2", "CHAR", "NCHAR", "CLOB", "NCLOB" },
        [ClrTypeNames.DateTime] = new[] { "DATE", "TIMESTAMP", "TIMESTAMP WITH TIME ZONE" },
        [ClrTypeNames.DateTimeOffset] = new[] { "TIMESTAMP WITH TIME ZONE" },
        [ClrTypeNames.Guid] = new[] { "RAW(16)" },
        [ClrTypeNames.ByteArray] = new[] { "RAW", "BLOB" },
    };

    public static LegacyOracleTypeCompatibility Instance { get; } = new();

    public string Provider => "oracle";

    public TypeCompatibilityResult Check(string? clrType, string? dbType, int? precision = null, int? scale = null, int? maxLength = null)
    {
        var clr = ClrTypeNames.Normalize(clrType);
        if (clr is null || string.IsNullOrWhiteSpace(dbType) || !Map.TryGetValue(clr, out var accepted))
        {
            return TypeCompatibilityResult.Unknown;
        }

        // Exact token or whole-type equality only (never substring: "POINT" must not match "INT").
        var tokens = dbType.Split(new[] { '(', ')', ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var full = string.Concat(dbType.Where(c => !char.IsWhiteSpace(c)));
        var match = accepted.Any(t =>
            tokens.Contains(t, StringComparer.OrdinalIgnoreCase) ||
            string.Equals(string.Concat(t.Where(c => !char.IsWhiteSpace(c))), full, StringComparison.OrdinalIgnoreCase));
        return match ? TypeCompatibilityResult.Compatible : TypeCompatibilityResult.Incompatible;
    }
}
