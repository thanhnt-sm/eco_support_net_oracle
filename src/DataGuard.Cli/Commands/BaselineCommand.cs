using System.CommandLine;
using DataGuard.Core.Baseline;
using DataGuard.Core.Models;
using static DataGuard.Cli.Services.ConfigLoader;
using static DataGuard.Cli.Services.DatabaseVersionReader;
using static DataGuard.Cli.Services.SnapshotGuard;
using static DataGuard.Cli.Services.ValidationRunner;

namespace DataGuard.Cli.Commands;

/// <summary><c>baseline</c>: create a baseline from current violations.</summary>
internal static class BaselineCommand
{
    public static Command Create(CommonOptions options)
    {
        var connectionOption = options.ConnectionOption;
        var configOption = options.ConfigOption;
        var outputOption = options.OutputOption;
        var verboseOption = options.VerboseOption;
        var providerOption = options.ProviderOption;
        var schemaOption = options.SchemaOption;
        var packageOption = options.PackageOption;
        var connectionEnvOption = options.ConnectionEnvOption;
        var allowAssemblyFromConfigOption = options.AllowAssemblyFromConfigOption;

        var baselineCommand = new Command("baseline", "Create baseline from current violations")
        {
            connectionOption, connectionEnvOption, configOption, outputOption, verboseOption, providerOption, schemaOption, packageOption, allowAssemblyFromConfigOption,
        };

        baselineCommand.SetAction(
            async (ParseResult result, System.Threading.CancellationToken ct) =>
            {
                var configPath = result.GetValue(configOption);
                var output = result.GetValue(outputOption);
                var verbose = result.GetValue(verboseOption);
                var schema = result.GetValue(schemaOption);
                var package = result.GetValue(packageOption);
                if (await ResolveCommandConfigurationAsync(configPath, result.GetValue(connectionOption), result.GetValue(connectionEnvOption), result.GetValue(providerOption), ct) is not { } resolved)
                {
                    return;
                }

                var config = resolved.Configuration;
                var provider = resolved.Provider;
                config = config with
                {
                    DefaultSchema = schema ?? config.DefaultSchema,
                    DefaultPackage = package ?? config.DefaultPackage,

                    // The baseline being (re)written must see every current finding, not only those the old baseline lets through.
                    EnableBaseline = false,
                };

                if (RefuseAssemblyFromConfig(config, commandLineAssemblyPath: null, result.GetValue(allowAssemblyFromConfigOption)))
                {
                    return;
                }

                try
                {
                    var outputPath = output ?? config.BaselineFilePath ?? ".dataguard-baseline.json";
                    if (string.IsNullOrWhiteSpace(config.ConnectionString) && config.GroundTruthMode != GroundTruthMode.Manual)
                    {
                        // Same offline source validate uses, so baseline fingerprints are computed over the same findings.
                        config = config with { GroundTruthMode = GroundTruthMode.Snapshot };
                        if (string.IsNullOrWhiteSpace(config.SnapshotFilePath)
                            && CliConfigurationResolver.FindDefaultSnapshot(configPath, Directory.GetCurrentDirectory()) is { } defaultSnapshot)
                        {
                            config = config with { SnapshotFilePath = defaultSnapshot };
                        }
                    }

                    var (violations, unevaluated) = await RunValidationAsync(config, provider, verbose, ct);

                    // Unevaluated contracts have no verdict: they are listed, never persisted as baseline findings.
                    WriteUnevaluated(unevaluated, " and are not part of the baseline");

                    var baselineManager = new BaselineManager(outputPath);
                    var previous = File.Exists(outputPath) ? await baselineManager.LoadAsync(ct) : null;

                    var dbVersion = await GetDatabaseVersionAsync(config, provider, ct);
                    var schemaHash = ComputeViolationHash(violations);

                    var baseline = await baselineManager.CreateBaselineAsync(
                        violations,
                        GetSchemaVersion(),
                        config.GroundTruthMode.ToString(),
                        dbVersion,
                        schemaHash,
                        provider: provider,
                        cancellationToken: ct);

                    Console.WriteLine($"Baseline created with {violations.Count} violations ({baseline.Violations.Count} fingerprints) at {outputPath}");
                    if (previous is not null && BaselineManager.CountLegacyEntries(previous) is > 0 and var upgraded)
                    {
                        Console.WriteLine($"Upgraded {upgraded} legacy entries to fingerprint {BaselineManager.FingerprintKey}");
                    }

                    Console.WriteLine($"Database version: {dbVersion}");
                    Console.WriteLine($"Schema hash: {schemaHash}");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Baseline creation failed: {ex.Message}");
                    if (verbose)
                    {
                        Console.Error.WriteLine(ex.StackTrace ?? "(no stack trace)");
                    }

                    Environment.ExitCode = 1;
                }
            });

        return baselineCommand;
    }
}
