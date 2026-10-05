using System.Reflection;
using DataGuard.Core.Models;
using DataGuard.Core.Security;
using DataGuard.Core.Security.SecretStores;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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

    /// <summary>Environment variable that carries the operator/CI connection string.</summary>
    public const string ConnectionEnvironmentVariable = "DATAGUARD_CONNECTION_STRING";

    /// <summary>One-time stderr warning for <c>--connection</c>.</summary>
    public const string CommandLineConnectionWarning =
        "warning: a connection string on the command line is visible to process listings; prefer --connection-env";

    /// <summary>Warning written when a plaintext <c>ConnectionString</c> in the configuration file is ignored.</summary>
    public const string IgnoredPlaintextConnectionWarning =
        "warning: ignoring the plaintext ConnectionString key in the configuration file (AllowPlaintextConfigFallback is false); "
        + "use --connection-env, DATAGUARD_CONNECTION_STRING or a secret store, or set AllowPlaintextConfigFallback: true";

    /// <summary>Warning written when a plaintext <c>ConnectionString</c> in the configuration file is used.</summary>
    public const string PlaintextConnectionInUseWarning =
        "warning: using the plaintext ConnectionString key from the configuration file (AllowPlaintextConfigFallback: true); "
        + "prefer --connection-env or a secret store outside the repository";

    /// <summary>Provider precedence only: <c>--provider</c>, then <c>DefaultProvider</c>, then <c>sqlserver</c>.</summary>
    /// <returns>The (not yet normalized) provider name.</returns>
    public static string ResolveProvider(DataGuardConfiguration configuration, string? commandLineProvider)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return !string.IsNullOrWhiteSpace(commandLineProvider)
            ? commandLineProvider
            : !string.IsNullOrWhiteSpace(configuration.DefaultProvider)
                ? configuration.DefaultProvider
                : "sqlserver";
    }

    /// <summary>
    /// Resolves the connection string for a database-backed command (red-team D1). Order:
    /// <c>--connection</c> (with <see cref="CommandLineConnectionWarning"/>) → <c>--connection-env NAME</c> →
    /// <c>DATAGUARD_CONNECTION_STRING</c> → <paramref name="secureSource"/> (the credential provider: secret stores, then
    /// the encrypted credential file) → the configuration file's <c>ConnectionString</c>, only when
    /// <see cref="DataGuardConfiguration.AllowPlaintextConfigFallback"/> is true (otherwise it is ignored with
    /// <see cref="IgnoredPlaintextConnectionWarning"/>). Never throws for an absent connection; the result says why.
    /// </summary>
    /// <param name="configuration">Configuration loaded from the file.</param>
    /// <param name="request">Command-line inputs and mode.</param>
    /// <param name="getEnvironmentVariable">Environment accessor (tests inject one).</param>
    /// <param name="secureSource">Credential-provider lookup, or null to skip it (offline, IDE-safe).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resolution, with warnings to print and an error (exit 2) when the request is invalid.</returns>
    public static async Task<CliConnectionResolution> ResolveConnectionAsync(
        DataGuardConfiguration configuration,
        CliConnectionRequest request,
        Func<string, string?> getEnvironmentVariable,
        Func<CancellationToken, Task<string?>>? secureSource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        var warnings = new List<string>();

        if (!string.IsNullOrWhiteSpace(request.CommandLineConnection))
        {
            warnings.Add(CommandLineConnectionWarning);
            return new CliConnectionResolution(request.CommandLineConnection, "--connection", warnings);
        }

        if (request.ConnectionEnvironmentVariable is { } variableName)
        {
            if (string.IsNullOrWhiteSpace(variableName))
            {
                return CliConnectionResolution.Failed("--connection-env requires an environment variable name.");
            }

            var value = getEnvironmentVariable(variableName.Trim());
            return string.IsNullOrWhiteSpace(value)
                ? CliConnectionResolution.Failed($"--connection-env {variableName.Trim()}: the environment variable is not set or empty.")
                : new CliConnectionResolution(value, "--connection-env " + variableName.Trim(), warnings);
        }

        var environmentConnection = getEnvironmentVariable(ConnectionEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(environmentConnection))
        {
            return new CliConnectionResolution(environmentConnection, ConnectionEnvironmentVariable, warnings);
        }

        if (request.IdeSafe)
        {
            // IDE-safe keeps the merged value only so IdeSafePolicy can report and strip it; it is never used.
            return new CliConnectionResolution(configuration.ConnectionString, "configuration file", warnings);
        }

        if (secureSource is not null)
        {
            var secured = await secureSource(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(secured))
            {
                return new CliConnectionResolution(secured, "credential provider", warnings);
            }
        }

        if (!string.IsNullOrWhiteSpace(configuration.ConnectionString))
        {
            if (configuration.AllowPlaintextConfigFallback)
            {
                if (request.WarnAboutPlaintext)
                {
                    warnings.Add(PlaintextConnectionInUseWarning);
                }

                return new CliConnectionResolution(configuration.ConnectionString, "configuration file", warnings);
            }

            if (request.WarnAboutPlaintext)
            {
                warnings.Add(IgnoredPlaintextConnectionWarning);
            }
        }

        return new CliConnectionResolution(null, "none", warnings);
    }

    /// <summary>
    /// Builds the credential-provider lookup the CLI uses after the command line and environment: a
    /// <see cref="ZeroTrustCredentialProvider"/> with <paramref name="secretStores"/> registered (AWS Secrets Manager),
    /// the encrypted credential file, and a <see cref="FileAuditLogger"/> (keyed by <c>DATAGUARD_AUDIT_KEY</c> or
    /// <c>AuditKeyFile</c>) shared with the <see cref="CredentialManager"/> so both write one hash chain. Returns null
    /// (nothing constructed, nothing audited) when no secret store is configured, no <c>DATAGUARD_DATABASECONNECTION</c>
    /// variable is set and no credential file exists. Secret-store failures are written to <paramref name="warnings"/>.
    /// </summary>
    /// <returns>The lookup, or null when there is no secure source to consult.</returns>
    public static Func<CancellationToken, Task<string?>>? CreateCredentialProviderSource(
        DataGuardConfiguration configuration,
        IEnumerable<ISecretStore> secretStores,
        TextWriter warnings,
        Func<string, string?> getEnvironmentVariable,
        string? credentialStorePath = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(secretStores);
        ArgumentNullException.ThrowIfNull(warnings);
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        var stores = secretStores.ToList();
        var storePath = credentialStorePath ?? CredentialManager.DefaultCredentialStorePath;
        var hasSource = stores.Any(store => store.IsConfigured(configuration))
            || !string.IsNullOrEmpty(configuration.KeyVaultUri)
            || !string.IsNullOrEmpty(configuration.VaultAddress)
            || !string.IsNullOrEmpty(configuration.AwsRegion)
            || !string.IsNullOrEmpty(getEnvironmentVariable("DATAGUARD_DATABASECONNECTION"))
            || File.Exists(storePath);
        if (!hasSource)
        {
            return null;
        }

        return async cancellationToken =>
        {
            // The provider must never fall back to the YAML value on its own: the CLI applies that rule itself.
            var secureConfiguration = configuration with { ConnectionString = null };
            IAuditLogger auditLogger = secureConfiguration.EnableAuditLogging
                ? FileAuditLogger.Create(secureConfiguration, getEnvironmentVariable)
                : new NullAuditLogger();
            var credentialManager = new CredentialManager(secureConfiguration, credentialStorePath: storePath, auditLogger: auditLogger);
            var provider = new ZeroTrustCredentialProvider(
                new ConfigurationBuilder().Build(),
                secureConfiguration,
                credentialManager,
                auditLogger,
                stores,
                new WarningLogger<ZeroTrustCredentialProvider>(warnings));
            using var handle = await provider.TryGetCredentialAsync("DatabaseConnection", CredentialType.DatabaseConnection, cancellationToken)
                .ConfigureAwait(false);
            return handle?.GetString();
        };
    }

    /// <summary>Writes Warning-and-above log events as one <c>warning: ...</c> line; everything else is dropped.</summary>
    private sealed class WarningLogger<T> : ILogger<T>
    {
        private readonly TextWriter _writer;

        public WarningLogger(TextWriter writer) => _writer = writer;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            _writer.WriteLine((logLevel >= LogLevel.Error ? "error: " : "warning: ") + formatter(state, exception));
        }
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

/// <summary>Command-line inputs for <see cref="CliConfigurationResolver.ResolveConnectionAsync"/>.</summary>
/// <param name="CommandLineConnection"><c>--connection</c> value (deprecated: visible to process listings).</param>
/// <param name="ConnectionEnvironmentVariable"><c>--connection-env</c> value: the NAME of the variable holding the connection string.</param>
/// <param name="IdeSafe">IDE-safe mode: no credential provider, the config value is left for <see cref="IdeSafePolicy"/> to strip.</param>
/// <param name="WarnAboutPlaintext">False when the command will not use a connection (bare <c>--offline</c>), to avoid noise.</param>
public sealed record CliConnectionRequest(
    string? CommandLineConnection,
    string? ConnectionEnvironmentVariable,
    bool IdeSafe = false,
    bool WarnAboutPlaintext = true);

/// <summary>Result of <see cref="CliConfigurationResolver.ResolveConnectionAsync"/>.</summary>
/// <param name="ConnectionString">The resolved connection string, or null.</param>
/// <param name="Source">Where it came from (never the value): <c>--connection</c>, <c>--connection-env NAME</c>, ...</param>
/// <param name="Warnings">Lines to write to stderr.</param>
/// <param name="Error">Set when the request is invalid (exit 2).</param>
public sealed record CliConnectionResolution(
    string? ConnectionString,
    string Source,
    IReadOnlyList<string> Warnings,
    string? Error = null)
{
    internal static CliConnectionResolution Failed(string error) => new(null, "none", Array.Empty<string>(), error);
}
