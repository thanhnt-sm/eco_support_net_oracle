using System.CommandLine;
using System.Text.Json;
using static DataGuard.Cli.Services.ConfigLoader;

namespace DataGuard.Cli.Commands;

/// <summary><c>config show|validate</c>: inspect the configuration file.</summary>
internal static class ConfigCommands
{
    public static Command Create(CommonOptions options)
    {
        var configOption = options.ConfigOption;

        var configCommand = new Command("config", "Manage DataGuard configuration");
        var configShowCommand = new Command("show", "Show current configuration")
        {
            configOption,
        };

        configShowCommand.SetAction(
            (ParseResult result) =>
            {
                var configPath = result.GetValue(configOption);
                if (TryLoadConfig(configPath) is not { } config)
                {
                    return;
                }

                // Never print secrets: redact connection string and vault/key material.
                var redacted = config with
                {
                    ConnectionString = string.IsNullOrEmpty(config.ConnectionString) ? null : "***redacted***"
                };
                var json = JsonSerializer.Serialize(redacted, new JsonSerializerOptions { WriteIndented = true });
                Console.WriteLine(json);
                Console.WriteLine("# Secrets are redacted. Use environment DATAGUARD_CONNECTION_STRING instead of --connection.");
            });

        var configValidateCommand = new Command("validate", "Validate configuration file")
        {
            configOption,
        };

        configValidateCommand.SetAction(
            (ParseResult result) =>
            {
                var configPath = result.GetValue(configOption);
                try
                {
                    if (TryLoadConfig(configPath) is not { } config)
                    {
                        return;
                    }

                    Console.WriteLine("Configuration is valid");
                    Console.WriteLine($"  GroundTruthMode: {config.GroundTruthMode}");
                    Console.WriteLine($"  NamingConvention: {config.NamingConvention}");
                    Console.WriteLine($"  EnableBaseline: {config.EnableBaseline}");
                    Console.WriteLine($"  DefaultSchema: {config.DefaultSchema ?? "not set"}");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Configuration invalid: {ex.Message}");
                    Environment.ExitCode = 1;
                }
            });

        configCommand.Add(configShowCommand);
        configCommand.Add(configValidateCommand);

        return configCommand;
    }
}
