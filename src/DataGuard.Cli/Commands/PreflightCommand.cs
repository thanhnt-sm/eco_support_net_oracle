using System.CommandLine;
using DataGuard.Core.Models;
using static DataGuard.Cli.Services.ConfigLoader;
using static DataGuard.Cli.Services.ContractAcquisition;

namespace DataGuard.Cli.Commands;

/// <summary><c>preflight</c>: acquire approved metadata and write a bounded offline manifest.</summary>
internal static class PreflightCommand
{
    public static Command Create(CommonOptions options)
    {
        var connectionOption = options.ConnectionOption;
        var configOption = options.ConfigOption;
        var outputOption = options.OutputOption;
        var providerOption = options.ProviderOption;
        var connectionEnvOption = options.ConnectionEnvOption;

        var preflightTargetOption = new Option<string>("--target") { Description = "Bounded operator-owned target identifier for the offline manifest" };
        var preflightCommand = new Command("preflight", "Acquire approved metadata and write a bounded offline manifest")
        {
            connectionOption, connectionEnvOption, configOption, outputOption, providerOption, preflightTargetOption,
        };
        preflightCommand.SetAction(async (ParseResult result, CancellationToken ct) =>
        {
            var output = result.GetValue(outputOption);
            var target = result.GetValue(preflightTargetOption);
            if (string.IsNullOrWhiteSpace(output) || string.IsNullOrWhiteSpace(target))
            {
                Console.Error.WriteLine("preflight requires --target and --output.");
                Environment.ExitCode = 2;
                return;
            }

            if (await ResolveCommandConfigurationAsync(result.GetValue(configOption), result.GetValue(connectionOption), result.GetValue(connectionEnvOption), result.GetValue(providerOption), ct) is not { } resolved)
            {
                return;
            }

            var provider = resolved.Provider;

            if (string.IsNullOrWhiteSpace(resolved.Configuration.ConnectionString))
            {
                Console.Error.WriteLine("preflight requires an explicit --connection or configured connection string.");
                Environment.ExitCode = 2;
                return;
            }

            try
            {
                var config = resolved.Configuration with { GroundTruthMode = GroundTruthMode.Full };
                var acquisition = await AcquireContractsAsync(config, provider, ct);
                if (acquisition.Status != ContractAcquisitionStatus.Complete)
                {
                    Console.Error.WriteLine($"UNEVALUATED: preflight acquisition {acquisition.Status.ToString().ToLowerInvariant()}: {acquisition.Message}");
                    Environment.ExitCode = 3;
                    return;
                }

                await OfflineManifestWriter.WriteAsync(output, target.Trim(), provider, acquisition.Contracts, ct);
                Console.WriteLine($"Offline manifest written to {output} ({acquisition.Contracts.Count} contracts).");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                Console.Error.WriteLine("Preflight cancelled.");
                Environment.ExitCode = 130;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Preflight failed: {ex.Message}");
                Environment.ExitCode = 1;
            }
        });

        return preflightCommand;
    }
}
