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
            if (config.StrictConfig)
            {
                Console.Error.WriteLine($"Error: unknown configuration keys: {string.Join(", ", unknownKeys)} (StrictConfig: true)");
                Environment.ExitCode = 2;
                return null;
            }

            Console.Error.WriteLine($"Warning: unknown configuration keys: {string.Join(", ", unknownKeys)}");
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

        // Typed binding via YamlDotNet: handles comments, quotes, lists and nested blocks, and preserves every
        // configuration field. An empty document keeps the defaults.
        try
        {
            return TypedConfigDeserializer.Deserialize<ConfigDocument?>(yaml)?.ToConfiguration() ?? config;
        }
        catch (YamlDotNet.Core.YamlException)
        {
            // Last resort for files the typed binding rejects (e.g. a scalar where a block is expected): map the
            // top-level scalars below, which reports an invalid value with the offending key's parse error.
        }

        var stream = new YamlDotNet.RepresentationModel.YamlStream();
        stream.Load(new StringReader(yaml));
        var root = stream.Documents.FirstOrDefault()?.RootNode as YamlDotNet.RepresentationModel.YamlMappingNode;
        if (root == null)
        {
            return config;
        }

        foreach (var entry in root.Children)
        {
            // Nested Plugins block: only AllowUnsignedLocal is mapped (the typed path above maps the whole record).
            if (entry.Key is YamlDotNet.RepresentationModel.YamlScalarNode { Value: "Plugins" } &&
                entry.Value is YamlDotNet.RepresentationModel.YamlMappingNode pluginsNode)
            {
                var allowUnsignedLocal = pluginsNode.Children
                    .Where(child => child.Key is YamlDotNet.RepresentationModel.YamlScalarNode { Value: "AllowUnsignedLocal" })
                    .Select(child => child.Value)
                    .OfType<YamlDotNet.RepresentationModel.YamlScalarNode>()
                    .Select(value => bool.Parse(value.Value ?? string.Empty))
                    .LastOrDefault();
                config = config with { Plugins = new PluginConfiguration { AllowUnsignedLocal = allowUnsignedLocal } };
                continue;
            }

            if (entry.Key is not YamlDotNet.RepresentationModel.YamlScalarNode keyNode ||
                entry.Value is not YamlDotNet.RepresentationModel.YamlScalarNode valueNode)
            {
                continue;
            }

            var key = keyNode.Value ?? "";
            var value = valueNode.Value ?? "";

            bool B() => bool.Parse(value);
            int I() => int.Parse(value);

            config = key switch
            {
                "GroundTruthMode" => config with { GroundTruthMode = Enum.Parse<GroundTruthMode>(value) },
                "NamingConvention" => config with { NamingConvention = Enum.Parse<NamingConvention>(value) },
                "EnableBaseline" => config with { EnableBaseline = B() },
                "DefaultSchema" => config with { DefaultSchema = value },
                "DefaultPackage" => config with { DefaultPackage = value },
                "DefaultProvider" => config with { DefaultProvider = value },
                "SnapshotFilePath" => config with { SnapshotFilePath = value },
                "BaselineFilePath" => config with { BaselineFilePath = value },
                "ConnectionString" => config with { ConnectionString = value },
                "EnableConcurrentValidation" => config with { EnableConcurrentValidation = B() },
                "MaxDegreeOfParallelism" => config with { MaxDegreeOfParallelism = I() },
                "MaxViolationQueueSize" => config with { MaxViolationQueueSize = I() },
                "ValidationTimeoutSeconds" => config with { ValidationTimeoutSeconds = I() },
                "EnableCredentialRotationDetection" => config with { EnableCredentialRotationDetection = B() },
                "CredentialRotationWarningDays" => config with { CredentialRotationWarningDays = I() },
                "EncryptConnectionStringAtRest" => config with { EncryptConnectionStringAtRest = B() },
                "KeyVaultUri" => config with { KeyVaultUri = value },
                "AwsRegion" => config with { AwsRegion = value },
                "VaultAddress" => config with { VaultAddress = value },
                "EnableAuditLogging" => config with { EnableAuditLogging = B() },
                "AuditLogPath" => config with { AuditLogPath = value },
                "AllowPlaintextConfigFallback" => config with { AllowPlaintextConfigFallback = B() },
                "ManualAssemblyPath" => config with { ManualAssemblyPath = value },
                "AutoDetectProvider" => config with { AutoDetectProvider = B() },
                "AutoDetectEFContext" => config with { AutoDetectEFContext = B() },
                "AutoDetectDapper" => config with { AutoDetectDapper = B() },
                "EnableSmartDefaults" => config with { EnableSmartDefaults = B() },
                "EnableTelemetry" => config with { EnableTelemetry = B() },
                "TelemetryFileDirectory" => config with { TelemetryFileDirectory = value },
                "TelemetryServiceName" => config with { TelemetryServiceName = value },
                "TelemetryServiceVersion" => config with { TelemetryServiceVersion = value },
                "IncludeTelemetryEventDetails" => config with { IncludeTelemetryEventDetails = B() },
                "FailOnUnavailableRules" => config with { FailOnUnavailableRules = B() },
                "StrictConfig" => config with { StrictConfig = B() },
                "SnapshotMaxAgeDays" => config with { SnapshotMaxAgeDays = I() },
                "StrictProcedureContracts" => config with { StrictProcedureContracts = B() },
                "AuditKeyFile" => config with { AuditKeyFile = value },
                "RequireEncryptedCredentialStore" => config with { RequireEncryptedCredentialStore = B() },
                _ => config
            };
        }

        return config;
    }

    internal static string SerializeConfig(DataGuardConfiguration config)
    {
        // Full round-trip via YamlDotNet: serializes every configuration field,
        // including nested Oracle/SqlServer blocks and excluded lists.
        var serializer = new YamlDotNet.Serialization.SerializerBuilder()
            .WithIndentedSequences()
            .Build();
        return serializer.Serialize(config);
    }
}
