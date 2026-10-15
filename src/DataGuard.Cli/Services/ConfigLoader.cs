using DataGuard.Core.Models;
using Microsoft.CodeAnalysis;
using DataGuard.Cli.Security;
using DataGuard.Core.Security.SecretStores;

namespace DataGuard.Cli.Services;

/// <summary>Loads, binds and resolves <c>.dataguard.yml</c> plus the command-line provider and connection.</summary>
internal static class ConfigLoader
{
    // Loads --config. No path: defaults. An explicit path that does not exist, an unparsable file, or unknown keys under
    // StrictConfig write one stderr line, set exit 2 and return null (red-team C4: never a silent default).
    // allowMissingConfig: IDE hosts always pass the conventional workspace path, so under --ide-safe a missing file warns.
    internal static DataGuardConfiguration? TryLoadConfig(string? configPath, bool allowMissingConfig = false)
    {
        if (string.IsNullOrEmpty(configPath))
        {
            return new DataGuardConfiguration();
        }

        if (!File.Exists(configPath))
        {
            if (allowMissingConfig)
            {
                Console.Error.WriteLine($"Warning: configuration file not found: {configPath}; using defaults.");
                return new DataGuardConfiguration();
            }

            Console.Error.WriteLine($"Configuration file not found: {configPath}");
            Environment.ExitCode = 2;
            return null;
        }

        DataGuardConfiguration config;
        IReadOnlyList<string> unknownKeys;
        try
        {
            var yaml = File.ReadAllText(configPath);
            config = DeserializeConfig(yaml);
            unknownKeys = CliConfigurationResolver.FindUnknownTopLevelKeys(yaml);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException or IOException or UnauthorizedAccessException or YamlDotNet.Core.YamlException)
        {
            Console.Error.WriteLine($"Configuration invalid: {configPath}: {ex.Message}");
            Environment.ExitCode = 2;
            return null;
        }

        if (unknownKeys.Count > 0)
        {
            // Keys come from the repository's YAML; a quoted key holding a line break must stay on this one line.
            var echoedKeys = OutputSinks.EscapeControlCharacters(string.Join(", ", unknownKeys));
            if (config.StrictConfig)
            {
                Console.Error.WriteLine($"Error: unknown configuration keys: {echoedKeys} (StrictConfig: true)");
                Environment.ExitCode = 2;
                return null;
            }

            Console.Error.WriteLine($"Warning: unknown configuration keys: {echoedKeys}");
        }

        return config;
    }

    // Writes the allowed-values error and sets exit 2 when provider is not on the whitelist.
    internal static string? TryNormalizeProviderOrFail(string? provider, string source)
    {
        if (CliConfigurationResolver.TryNormalizeProvider(provider, out var normalized))
        {
            return normalized;
        }

        Console.Error.WriteLine(CliConfigurationResolver.FormatUnsupportedProvider(provider, source));
        Environment.ExitCode = 2;
        return null;
    }

    // Null after writing the reason to stderr with exit 2: missing/invalid --config, a provider (from --provider or the
    // config DefaultProvider) outside the whitelist, or an invalid credential request (--connection-env naming an unset
    // variable, an unusable audit key). The returned provider is normalized (lower-case, postgres => postgresql) and the
    // returned configuration carries the connection resolved by CliConfigurationResolver.ResolveConnectionAsync (red-team D1).
    internal static async Task<(DataGuardConfiguration Configuration, string Provider)?> ResolveCommandConfigurationAsync(
        string? configPath,
        string? commandLineConnection,
        string? connectionEnvironmentVariable,
        string? commandLineProvider,
        CancellationToken cancellationToken,
        bool allowMissingConfig = false,
        bool ideSafe = false,
        bool offline = false,
        bool warnAboutPlaintext = true)
    {
        var config = TryLoadConfig(configPath, allowMissingConfig);
        if (config is null)
        {
            return null;
        }

        var source = !string.IsNullOrWhiteSpace(commandLineProvider) ? "--provider" : "config DefaultProvider";
        var provider = TryNormalizeProviderOrFail(CliConfigurationResolver.ResolveProvider(config, commandLineProvider), source);
        if (provider is null)
        {
            return null;
        }

        CliConnectionResolution connection;
        try
        {
            // Secret stores and the encrypted credential file are never consulted offline or under --ide-safe. The audit
            // logger (HMAC-keyed by DATAGUARD_AUDIT_KEY or AuditKeyFile) is built inside the provider source and shared
            // with the CredentialManager, so one chain records every credential event.
            var secureSource = ideSafe || offline
                ? null
                : CliConfigurationResolver.CreateCredentialProviderSource(
                    config,
                    new ISecretStore[] { new AwsSecretsManagerSecretStore() },
                    Console.Error,
                    Environment.GetEnvironmentVariable);
            connection = await CliConfigurationResolver.ResolveConnectionAsync(
                config,
                new CliConnectionRequest(commandLineConnection, connectionEnvironmentVariable, ideSafe, warnAboutPlaintext),
                Environment.GetEnvironmentVariable,
                secureSource,
                cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine($"Credential resolution failed: {ex.Message}");
            Environment.ExitCode = 2;
            return null;
        }

        foreach (var warning in connection.Warnings)
        {
            Console.Error.WriteLine(warning);
        }

        if (connection.Error is not null)
        {
            Console.Error.WriteLine(connection.Error);
            Environment.ExitCode = 2;
            return null;
        }

        return (config with { ConnectionString = connection.ConnectionString }, provider);
    }

    // Manual mode from a repository's YAML (ManualAssemblyPath) reads a repository-chosen assembly; it needs
    // --allow-assembly-from-config. --assembly on the command line is the operator's own choice and needs no flag.
    // Writes the reason, sets exit 2 and returns true when refused.
    internal static bool RefuseAssemblyFromConfig(DataGuardConfiguration config, string? commandLineAssemblyPath, bool allowAssemblyFromConfig)
    {
        if (config.GroundTruthMode != GroundTruthMode.Manual
            || string.IsNullOrWhiteSpace(config.ManualAssemblyPath)
            || allowAssemblyFromConfig
            || (!string.IsNullOrWhiteSpace(commandLineAssemblyPath)
                && string.Equals(config.ManualAssemblyPath, commandLineAssemblyPath, StringComparison.Ordinal)))
        {
            return false;
        }

        Console.Error.WriteLine(
            "ManualAssemblyPath is set in the configuration file; pass --allow-assembly-from-config to read that assembly, "
            + "or name it on the command line with --offline --assembly <path>.");
        Environment.ExitCode = 2;
        return true;
    }

    // Binds every key, nested Oracle/SqlServer/Plugins blocks and the Excluded* lists through ConfigDocument; unknown
    // keys are ignored here (TryLoadConfig warns about them, or fails under StrictConfig).
    private static readonly YamlDotNet.Serialization.IDeserializer TypedConfigDeserializer =
        new YamlDotNet.Serialization.DeserializerBuilder()
            .IgnoreUnmatchedProperties()
            .Build();

    internal static DataGuardConfiguration DeserializeConfig(string yaml)
    {
        var config = new DataGuardConfiguration
        {
            ExcludedProcedures = Array.Empty<string>(),
            ExcludedEntities = Array.Empty<string>(),
        };

        var stream = new YamlDotNet.RepresentationModel.YamlStream();
        stream.Load(new StringReader(yaml));
        var root = stream.Documents.FirstOrDefault()?.RootNode;
        if (root is YamlDotNet.RepresentationModel.YamlMappingNode mapping)
        {
            foreach (var key in new[] { "Oracle", "SqlServer", "Plugins" })
            {
                if (mapping.Children.TryGetValue(new YamlDotNet.RepresentationModel.YamlScalarNode(key), out var value)
                    && value is not YamlDotNet.RepresentationModel.YamlMappingNode
                    && value is not YamlDotNet.RepresentationModel.YamlScalarNode { Value: null or "" or "~" or "null" or "Null" or "NULL" })
                {
                    throw new FormatException($"{key} must be a mapping block or null, not a boolean/scalar."
                        + (key == "Plugins" ? string.Empty : " Use DefaultProvider to select the database provider."));
                }
            }
        }

        // Never recover from a binding failure by silently dropping nested settings or exclusion lists.
        try
        {
            return TypedConfigDeserializer.Deserialize<ConfigDocument?>(yaml)?.ToConfiguration() ?? config;
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            throw new FormatException($"Invalid configuration value or shape at line {ex.Start.Line}, column {ex.Start.Column}; check scalar types, lists and nested mappings.", ex);
        }
    }

    internal static string SerializeConfig(DataGuardConfiguration config)
    {
        return ConfigurationYamlWriter.Serialize(config);
    }
}
