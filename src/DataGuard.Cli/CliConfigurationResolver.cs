using DataGuard.Core.Models;

namespace DataGuard.Cli;

/// <summary>
/// Resolves the shared configuration precedence used by database-backed CLI commands.
/// </summary>
public static class CliConfigurationResolver
{
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
}
