using System.Reflection;
using DataGuard.Core.Models;
using YamlDotNet.RepresentationModel;

namespace DataGuard.Cli;

/// <summary>
/// Resolves the shared configuration precedence used by database-backed CLI commands.
/// </summary>
public static class CliConfigurationResolver
{
    /// <summary>File name of the committed schema snapshot, shared by <c>validate</c> and the <c>snapshot</c> commands.</summary>
    public const string DefaultSnapshotFileName = ".dataguard-snapshot.json";

    /// <summary>Provider names accepted by <c>--provider</c> and the <c>DefaultProvider</c> config key (case-insensitive).</summary>
    public static readonly IReadOnlyList<string> SupportedProviders = new[] { "sqlserver", "oracle", "mysql", "postgresql", "postgres" };

    private static readonly HashSet<string> KnownConfigurationKeys = typeof(DataGuardConfiguration)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Select(property => property.Name)
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Applies command-line and environment values without changing the caller's configuration object.
    /// </summary>
    public static (DataGuardConfiguration Configuration, string Provider) Resolve(
        DataGuardConfiguration configuration,
        string? commandLineConnection,
        string? commandLineProvider,
        string? environmentConnection)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connection = !string.IsNullOrWhiteSpace(commandLineConnection)
            ? commandLineConnection
            : !string.IsNullOrWhiteSpace(environmentConnection)
                ? environmentConnection
                : configuration.ConnectionString;
        var provider = !string.IsNullOrWhiteSpace(commandLineProvider)
            ? commandLineProvider
            : !string.IsNullOrWhiteSpace(configuration.DefaultProvider)
                ? configuration.DefaultProvider
                : "sqlserver";

        return (configuration with { ConnectionString = connection }, provider);
    }

    /// <summary>
    /// Normalizes a provider name against <see cref="SupportedProviders"/>: trimmed, lower-case, and <c>postgres</c>
    /// folded to <c>postgresql</c>. Returns false for any other value, so a typo can never fall back to core rules only.
    /// </summary>
    public static bool TryNormalizeProvider(string? provider, out string normalized)
    {
        normalized = provider?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!SupportedProviders.Contains(normalized, StringComparer.Ordinal))
        {
            return false;
        }

        if (normalized == "postgres")
        {
            normalized = "postgresql";
        }

        return true;
    }

    /// <summary>Single-line error for an unsupported provider value that names every allowed value.</summary>
    public static string FormatUnsupportedProvider(string? provider, string source) =>
        $"Unsupported provider '{provider}' ({source}). Allowed values: {string.Join(", ", SupportedProviders)}.";

    /// <summary>
    /// Returns the top-level keys of a YAML configuration document that do not name a
    /// <see cref="DataGuardConfiguration"/> property (ordinal match, document order, no duplicates).
    /// </summary>
    public static IReadOnlyList<string> FindUnknownTopLevelKeys(string yaml)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        if (stream.Documents.FirstOrDefault()?.RootNode is not YamlMappingNode root)
        {
            return Array.Empty<string>();
        }

        return root.Children.Keys
            .OfType<YamlScalarNode>()
            .Select(key => key.Value ?? string.Empty)
            .Where(key => !KnownConfigurationKeys.Contains(key))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Finds the default committed snapshot: <see cref="DefaultSnapshotFileName"/> next to the config file when one is
    /// given, then in <paramref name="currentDirectory"/> (where <c>snapshot refresh</c> writes it by default).
    /// Returns null when neither exists.
    /// </summary>
    public static string? FindDefaultSnapshot(string? configPath, string currentDirectory)
    {
        if (!string.IsNullOrWhiteSpace(configPath))
        {
            var configDirectory = Path.GetDirectoryName(Path.GetFullPath(configPath));
            if (!string.IsNullOrEmpty(configDirectory))
            {
                var nextToConfig = Path.Combine(configDirectory, DefaultSnapshotFileName);
                if (File.Exists(nextToConfig))
                {
                    return nextToConfig;
                }
            }
        }

        var inCurrentDirectory = Path.Combine(currentDirectory, DefaultSnapshotFileName);
        return File.Exists(inCurrentDirectory) ? inCurrentDirectory : null;
    }
}
