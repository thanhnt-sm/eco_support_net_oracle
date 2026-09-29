using DataGuard.Core.Models;

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

    /// <summary>Result of applying the policy: the sanitized configuration and what was suppressed.</summary>
    public sealed record Result(DataGuardConfiguration Configuration, IReadOnlyList<string> Suppressed);

    /// <summary>
    /// Returns a configuration with every code-loading, outbound-connection and repo-chosen write path removed.
    /// </summary>
    /// <param name="configuration">Configuration resolved from file, command line and environment.</param>
    /// <param name="environmentConnectionPresent">True when <c>DATAGUARD_CONNECTION_STRING</c> was set (for the message only).</param>
    public static Result Apply(DataGuardConfiguration configuration, bool environmentConnectionPresent)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var suppressed = new List<string>();
        var config = configuration;

        if (config.GroundTruthMode != GroundTruthMode.Snapshot)
        {
            suppressed.Add($"GroundTruthMode={config.GroundTruthMode} (forced to Snapshot)");
            config = config with { GroundTruthMode = GroundTruthMode.Snapshot };
        }

        if (!string.IsNullOrEmpty(config.ManualAssemblyPath))
        {
            suppressed.Add("ManualAssemblyPath (assembly loading disabled)");
            config = config with { ManualAssemblyPath = null };
        }

        if (!string.IsNullOrEmpty(config.ConnectionString))
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

        if (config.EncryptConnectionStringAtRest)
        {
            // Nothing to encrypt once the connection string is gone; avoid touching DPAPI/keychain.
            config = config with { EncryptConnectionStringAtRest = false };
        }

        return new Result(config, suppressed);
    }

    /// <summary>Returns the first <c>validate</c> option that is incompatible with IDE-safe mode, or null.</summary>
    public static string? FirstRejectedValidateOption(
        string? commandLineConnection,
        bool offline,
        string? assemblyPath,
        string? efSnapshotPath,
        string? efProjectPath,
        string? efContextName)
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

        return null;
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

    /// <summary>Single stderr line describing what IDE-safe mode suppressed.</summary>
    public static string FormatSuppressionLine(IReadOnlyList<string> suppressed)
    {
        ArgumentNullException.ThrowIfNull(suppressed);
        return "ide-safe: suppressed " + string.Join("; ", suppressed);
    }

    /// <summary>Single stderr line explaining why an option was rejected (exit code 2).</summary>
    public static string FormatRejectionLine(string option)
    {
        return $"{option} is not allowed with {OptionName}: IDE-safe mode forbids assembly loading, database and network access.";
    }
}
