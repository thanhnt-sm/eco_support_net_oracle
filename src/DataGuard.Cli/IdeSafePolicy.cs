using DataGuard.Core.Models;
using DataGuard.Core.Validation;

namespace DataGuard.Cli;

/// <summary>
/// IDE-safe execution policy. IDE hosts (Visual Studio, VS Code) run the CLI against repositories the
/// user may not trust. When <c>--ide-safe</c> is set, the CLI must never load assemblies, never open
/// database or secret-manager connections, and never write to repo-chosen file paths — regardless of
/// what <c>.dataguard.yml</c> or the environment requests. The policy is pure so it can be unit tested.
/// </summary>
public static class IdeSafePolicy
{
    /// <summary>The command-line option that enables IDE-safe mode.</summary>
    public const string OptionName = "--ide-safe";

    /// <summary>The <c>validate</c>-only option that keeps a host-supplied <c>DATAGUARD_CONNECTION_STRING</c> under IDE-safe mode.</summary>
    public const string AllowEnvConnectionOptionName = "--allow-env-connection";

    /// <summary>Names the environment variable holding the connection string; under IDE-safe mode only with <see cref="AllowEnvConnectionOptionName"/>.</summary>
    public const string ConnectionEnvOptionName = "--connection-env";

    /// <summary>Allows a <c>ManualAssemblyPath</c> from the configuration file; always rejected under IDE-safe mode.</summary>
    public const string AllowAssemblyFromConfigOptionName = "--allow-assembly-from-config";

    /// <summary>The <c>validate</c> option that loads rule plugin assemblies; always rejected under IDE-safe mode.</summary>
    public const string PluginsDirOptionName = "--plugins-dir";

    /// <summary>Positive acknowledgement hosts require as the first stderr line before publishing any result.</summary>
    public const string ActiveLine = "ide-safe: active";

    /// <summary>Upper bound for <c>MaxViolationQueueSize</c> under IDE-safe mode: the engine's own default.</summary>
    public const int MaxQueueSize = ConcurrentValidationEngine.DefaultMaxViolationQueueSize;

    /// <summary>Upper bound for <c>ValidationTimeoutSeconds</c> under IDE-safe mode.</summary>
    public const int MaxTimeoutSeconds = 900;

    /// <summary>Text of the stderr line acknowledging that the environment credential was kept.</summary>
    public const string KeptEnvironmentConnectionNote = "kept environment connection (--allow-env-connection)";

    /// <summary>
    /// Suppression entry written when the environment credential is kept: the kept credential is used for
    /// ground-truth acquisition only; repository-extracted SQL is never described against it during <c>validate</c>.
    /// </summary>
    public const string LiveShapeRuleDisabledNote = "live SQL shape rule disabled (use verify-shape)";

    /// <summary>Result of applying the policy.</summary>
    /// <param name="Configuration">The sanitized configuration.</param>
    /// <param name="Suppressed">What was stripped or clamped, in report order.</param>
    /// <param name="KeptEnvironmentConnection">True when the host-supplied environment credential survived for ground-truth acquisition.</param>
    /// <param name="RulesConnectionString">
    /// Connection handed to connection-bound rules. Always null under IDE-safe mode (review H1): even a kept credential
    /// never drives the live SQL shape rule over repository-extracted SQL; that is <c>verify-shape</c>, behind a host confirmation.
    /// </param>
    public sealed record Result(
        DataGuardConfiguration Configuration,
        IReadOnlyList<string> Suppressed,
        bool KeptEnvironmentConnection,
        string? RulesConnectionString);

    /// <summary>
    /// Returns a configuration with every code-loading, outbound-connection and repo-chosen write path removed.
    /// With <paramref name="allowEnvConnection"/> and an environment credential present, that credential is the
    /// only connection kept (config-file values are always stripped) and a non-Manual ground-truth mode survives
    /// so the connection is actually usable; assembly loading stays disabled in every case.
    /// </summary>
    /// <param name="configuration">Configuration resolved from file, command line and environment.</param>
    /// <param name="environmentConnectionPresent">True when <c>DATAGUARD_CONNECTION_STRING</c> was set.</param>
    /// <param name="allowEnvConnection">True when the host passed <c>--allow-env-connection</c>: keep the environment credential only.</param>
    /// <param name="environmentConnection">Raw <c>DATAGUARD_CONNECTION_STRING</c> value; when kept it replaces any merged value explicitly.</param>
    public static Result Apply(
        DataGuardConfiguration configuration,
        bool environmentConnectionPresent,
        bool allowEnvConnection = false,
        string? environmentConnection = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var suppressed = new List<string>();
        var config = configuration;
        var keepEnvConnection = allowEnvConnection && environmentConnectionPresent;

        // Manual mode is meaningless without assembly loading; Full only survives when a connection is kept.
        if (config.GroundTruthMode == GroundTruthMode.Manual || (!keepEnvConnection && config.GroundTruthMode != GroundTruthMode.Snapshot))
        {
            suppressed.Add($"GroundTruthMode={config.GroundTruthMode} (forced to Snapshot)");
            config = config with { GroundTruthMode = GroundTruthMode.Snapshot };
        }

        if (!string.IsNullOrEmpty(config.ManualAssemblyPath))
        {
            suppressed.Add("ManualAssemblyPath (assembly loading disabled)");
            config = config with { ManualAssemblyPath = null };
        }

        if (keepEnvConnection)
        {
            suppressed.Add(LiveShapeRuleDisabledNote);
            if (!string.IsNullOrWhiteSpace(environmentConnection))
            {
                config = config with { ConnectionString = environmentConnection };
            }
        }
        else if (!string.IsNullOrEmpty(config.ConnectionString))
        {
            suppressed.Add(environmentConnectionPresent
                ? "connection string from DATAGUARD_CONNECTION_STRING/config (database access disabled)"
                : "ConnectionString from config (database access disabled)");
            config = config with { ConnectionString = null };
        }

        if (!string.IsNullOrEmpty(config.KeyVaultUri) || !string.IsNullOrEmpty(config.AwsRegion) || !string.IsNullOrEmpty(config.VaultAddress))
        {
            suppressed.Add("secret-manager settings (KeyVaultUri/AwsRegion/VaultAddress)");
            config = config with { KeyVaultUri = null, AwsRegion = null, VaultAddress = null };
        }

        if (!string.IsNullOrEmpty(config.AuditLogPath))
        {
            suppressed.Add("AuditLogPath (repo-chosen write path)");
            config = config with { AuditLogPath = null };
        }

        if (config.EnableTelemetry || !string.IsNullOrEmpty(config.TelemetryFileDirectory))
        {
            suppressed.Add("telemetry output (EnableTelemetry/TelemetryFileDirectory)");
            config = config with { EnableTelemetry = false, TelemetryFileDirectory = null };
        }

        // EncryptConnectionStringAtRest (default true) is left as is: under IDE-safe mode the CLI never builds the
        // credential provider or CredentialManager, so no DPAPI/Keychain/Secret Service call can happen for a repo config.
        return new Result(ClampResourceBounds(config, suppressed), suppressed, keepEnvConnection, RulesConnectionString: null);
    }

    /// <summary>Bounds repo-controlled parallelism, queue and timeout values so a hostile config cannot exhaust the host.</summary>
    private static DataGuardConfiguration ClampResourceBounds(DataGuardConfiguration config, List<string> suppressed)
    {
        var processorCount = Environment.ProcessorCount;
        if (config.MaxDegreeOfParallelism > processorCount)
        {
            suppressed.Add($"MaxDegreeOfParallelism={config.MaxDegreeOfParallelism} (clamped to {processorCount})");
            config = config with { MaxDegreeOfParallelism = processorCount };
        }

        if (config.MaxViolationQueueSize > MaxQueueSize)
        {
            suppressed.Add($"MaxViolationQueueSize={config.MaxViolationQueueSize} (clamped to {MaxQueueSize})");
            config = config with { MaxViolationQueueSize = MaxQueueSize };
        }

        if (config.ValidationTimeoutSeconds > MaxTimeoutSeconds)
        {
            suppressed.Add($"ValidationTimeoutSeconds={config.ValidationTimeoutSeconds} (clamped to {MaxTimeoutSeconds})");
            config = config with { ValidationTimeoutSeconds = MaxTimeoutSeconds };
        }

        return config;
    }

    /// <summary>
    /// Returns the first <c>validate</c> option that is incompatible with IDE-safe mode, or null. A non-empty
    /// <see cref="PluginsDirOptionName"/> value is rejected too: plugin loading is code loading.
    /// </summary>
    public static string? FirstRejectedValidateOption(
        string? commandLineConnection,
        bool offline,
        string? assemblyPath,
        string? efSnapshotPath,
        string? efProjectPath,
        string? efContextName,
        string? pluginsDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(commandLineConnection))
        {
            return "--connection";
        }

        if (offline)
        {
            return "--offline";
        }

        if (!string.IsNullOrWhiteSpace(assemblyPath))
        {
            return "--assembly";
        }

        if (!string.IsNullOrWhiteSpace(efSnapshotPath))
        {
            return "--ef-snapshot";
        }

        if (!string.IsNullOrWhiteSpace(efProjectPath))
        {
            return "--ef-project";
        }

        if (!string.IsNullOrWhiteSpace(efContextName))
        {
            return "--ef-context";
        }

        if (!string.IsNullOrWhiteSpace(pluginsDirectory))
        {
            return PluginsDirOptionName;
        }

        return null;
    }

    /// <summary>
    /// Returns the first <c>validate</c> option that is incompatible with IDE-safe mode, or null, including the credential
    /// and assembly options: <c>--connection-env</c> follows the <c>--allow-env-connection</c> rule (accepted only together
    /// with it; the named variable then plays the role of <c>DATAGUARD_CONNECTION_STRING</c>), and
    /// <c>--allow-assembly-from-config</c> is always rejected because IDE-safe mode never loads repository assemblies.
    /// </summary>
    public static string? FirstRejectedValidateOption(
        string? commandLineConnection,
        bool offline,
        string? assemblyPath,
        string? efSnapshotPath,
        string? efProjectPath,
        string? efContextName,
        string? connectionEnvironmentVariable,
        bool allowEnvConnection,
        bool allowAssemblyFromConfig,
        string? pluginsDirectory = null)
    {
        var rejected = FirstRejectedValidateOption(commandLineConnection, offline, assemblyPath, efSnapshotPath, efProjectPath, efContextName, pluginsDirectory);
        if (rejected is not null)
        {
            return rejected;
        }

        if (!string.IsNullOrWhiteSpace(connectionEnvironmentVariable) && !allowEnvConnection)
        {
            return ConnectionEnvOptionName;
        }

        return allowAssemblyFromConfig ? AllowAssemblyFromConfigOptionName : null;
    }

    /// <summary>Returns the first <c>assess</c> option that is incompatible with IDE-safe mode, or null.</summary>
    public static string? FirstRejectedAssessOption(bool allowNetwork, string? remoteAdvisoriesProvider)
    {
        if (allowNetwork)
        {
            return "--allow-network";
        }

        if (!string.IsNullOrWhiteSpace(remoteAdvisoriesProvider))
        {
            return "--remote-advisories";
        }

        return null;
    }

    /// <summary>Single stderr line describing what IDE-safe mode suppressed; the kept-connection note has its own line.</summary>
    public static string FormatSuppressionLine(IReadOnlyList<string> suppressed)
    {
        ArgumentNullException.ThrowIfNull(suppressed);
        return "ide-safe: suppressed " + string.Join("; ", suppressed);
    }

    /// <summary>Single stderr line acknowledging that the host-supplied environment credential was kept.</summary>
    public static string FormatKeptConnectionLine() => "ide-safe: " + KeptEnvironmentConnectionNote;

    /// <summary>Writes the suppression and kept-connection lines to <paramref name="writer"/> in a fixed order.</summary>
    public static void WriteReport(TextWriter writer, Result result)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(result);
        if (result.Suppressed.Count > 0)
        {
            writer.WriteLine(FormatSuppressionLine(result.Suppressed));
        }

        if (result.KeptEnvironmentConnection)
        {
            writer.WriteLine(FormatKeptConnectionLine());
        }
    }

    /// <summary>Single stderr line explaining why an option was rejected (exit code 2).</summary>
    public static string FormatRejectionLine(string option)
    {
        if (option == ConnectionEnvOptionName)
        {
            return $"{option} is not allowed with {OptionName} unless {AllowEnvConnectionOptionName} is also given: IDE-safe mode forbids database access by default.";
        }

        return $"{option} is not allowed with {OptionName}: IDE-safe mode forbids assembly loading, database and network access.";
    }
}
