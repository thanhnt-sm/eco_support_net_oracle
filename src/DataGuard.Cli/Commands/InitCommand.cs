using System.CommandLine;
using DataGuard.Core.AutoDetection;
using DataGuard.Core.Models;
using static DataGuard.Cli.Services.ConfigLoader;
using static DataGuard.Cli.Services.OutputSinks;

namespace DataGuard.Cli.Commands;

/// <summary><c>init</c>: write a starter configuration or run the interactive wizard.</summary>
internal static class InitCommand
{
    public static Command Create()
    {
        var initOutputOption = new Option<string>("--output");
        initOutputOption.Description = "Output config file path";
        initOutputOption.DefaultValueFactory = (_) => ".dataguard.yml";
        var initProviderOption = new Option<string>("--provider");
        initProviderOption.Description = "Default provider: sqlserver, oracle";
        initProviderOption.DefaultValueFactory = (_) => "sqlserver";
        var initWizardOption = new Option<bool>("--wizard");
        initWizardOption.Description = "Run the interactive setup wizard";
        var initCommand = new Command("init", "Initialize DataGuard configuration")
        {
            initOutputOption, initProviderOption, initWizardOption,
        };

        initCommand.SetAction(
            async (ParseResult result, System.Threading.CancellationToken ct) =>
            {
                var output = result.GetValue(initOutputOption);
                if (TryNormalizeProviderOrFail(result.GetValue(initProviderOption), "--provider") is not { } provider)
                {
                    return;
                }
                if (result.GetValue(initWizardOption))
                {
                    var configPath = Path.GetFullPath(output!);
                    if (!IsSafeWritablePath(configPath))
                    {
                        Console.Error.WriteLine("Refusing to write configuration through a symbolic link or invalid path.");
                        return;
                    }

                    await InteractiveConfigBuilder.RunWizardAsync(
                        Directory.GetCurrentDirectory(),
                        new SystemConsole(),
                        configPath,
                        ct);
                    return;
                }

                var config = new DataGuardConfiguration
                {
                    GroundTruthMode = GroundTruthMode.Snapshot,
                    SnapshotFilePath = ".dataguard-snapshot.json",
                    BaselineFilePath = ".dataguard-baseline.json",
                    NamingConvention = NamingConvention.SnakeCaseToPascalCase,
                    EnableBaseline = true,
                    DefaultProvider = provider,
                };

                var yaml = SerializeConfig(config);
                if (!IsSafeWritablePath(output!))
                {
                    Console.Error.WriteLine("Refusing to write configuration through a symbolic link or invalid path.");
                    return;
                }

                await File.WriteAllTextAsync(output!, yaml);
                Console.WriteLine($"Configuration written to {output}");
                Console.WriteLine($"Default provider: {provider}");
            });

        return initCommand;
    }
}
