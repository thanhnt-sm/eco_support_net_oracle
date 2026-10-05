using System.Collections.Concurrent;

namespace DataGuard.Core.Rules.TypeCompatibility;

/// <summary>
/// Provider-neutral table that answers <see cref="TypeCompatibilityResult.Unknown"/> for every pair, so rules built
/// without a provider table report nothing instead of borrowing another dialect's mappings. This is the
/// <see cref="TypeCompatibilityRegistry"/> fallback for every provider whose adapter has not registered a table.
/// </summary>
public sealed class UnknownTypeCompatibility : ITypeCompatibility
{
    private static readonly ConcurrentDictionary<string, UnknownTypeCompatibility> Cache = new(StringComparer.Ordinal);

    /// <summary>Initializes a new instance of the <see cref="UnknownTypeCompatibility"/> class.</summary>
    /// <param name="provider">The provider key this placeholder stands for.</param>
    public UnknownTypeCompatibility(string provider)
    {
        Provider = provider ?? string.Empty;
    }

    /// <inheritdoc/>
    public string Provider { get; }

    /// <summary>Returns the shared instance for <paramref name="provider"/>.</summary>
    /// <param name="provider">Canonical provider key.</param>
    /// <returns>A table that answers <see cref="TypeCompatibilityResult.Unknown"/>.</returns>
    public static UnknownTypeCompatibility For(string provider) =>
        Cache.GetOrAdd(provider ?? string.Empty, key => new UnknownTypeCompatibility(key));

    /// <inheritdoc/>
    public TypeCompatibilityResult Check(string? clrType, string? dbType, int? precision = null, int? scale = null, int? maxLength = null) =>
        TypeCompatibilityResult.Unknown;
}
