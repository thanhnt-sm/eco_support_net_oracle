// <copyright file="DataGuardPackage.Commands.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using Task = System.Threading.Tasks.Task;

/// <summary>Command execution: run slot → CLI discovery → trust gate → ide-safe run → outcome handling.</summary>
public sealed partial class DataGuardPackage
{
    private const string ConfigFileName = ".dataguard.yml";

    /// <summary>Sentinel exit code recorded in the log when the CLI was terminated on timeout.</summary>
    private const int TimedOutExitCode = -1;

    private Task RunValidationAsync(bool fromBuild) => this.RunCliAsync("validate", fromBuild);

    private Task RunAssessmentAsync() => this.RunCliAsync("assess", fromBuild: false);

    private async Task RunValidationOnBuildAsync()
    {
        await this.JoinableTaskFactory.SwitchToMainThreadAsync();
        var options = (DataGuardOptionsPage?)this.GetDialogPage(typeof(DataGuardOptionsPage));
        if (options != null && options.RunValidationOnBuild)
        {
            await this.RunValidationAsync(fromBuild: true);
        }
    }

    private async Task RunCliAsync(string command, bool fromBuild)
    {
        await this.JoinableTaskFactory.SwitchToMainThreadAsync();
        var output = this.output!;
        var solution = await this.ReadSolutionInfoAsync();
        if (solution == null)
        {
            await output.WriteAsync("[DataGuard] Open a solution before running " + command + ".\r\n");
            return;
        }

        // Reserve first, clear second: a command issued during another run must not wipe its inventory.
        if (!RuleInventory.ClearInventoryIfReserved(this.processRegistry, this.ruleInventory))
        {
            await output.WriteAsync("[DataGuard] A DataGuard command is already running for this solution.\r\n");
            return;
        }

        try
        {
            var options = (DataGuardOptionsPage?)this.GetDialogPage(typeof(DataGuardOptionsPage));
            var ruleOptions = (DataGuardRulesOptionsPage)this.GetDialogPage(typeof(DataGuardRulesOptionsPage));
            var timeoutSeconds = command == "validate" ? (options?.ValidationTimeoutSeconds ?? 300) : (options?.AssessmentTimeoutSeconds ?? 60);
            DataGuardLogger.Configure(options?.EnableDetailedLogging ?? true, options?.CustomLogDirectory);

            var cliPath = CliLocator.FindCliExecutable(options?.CustomCliPath);
            if (string.IsNullOrEmpty(cliPath))
            {
                await output.WriteAsync(DescribeMissingCli(options?.CustomCliPath));
                this.processRegistry.ReleaseReservation();
                return;
            }

            var configPath = Path.Combine(solution.Directory, ConfigFileName);
            var configExists = File.Exists(configPath);
            if (!await this.EnsureConsentAsync(solution, configPath, configExists, fromBuild))
            {
                this.processRegistry.ReleaseReservation();
                return;
            }

            var disabledRules = command == "validate" ? ruleOptions.GetDisabledRuleIds() : Array.Empty<string>();
            var enabledRuleCount = command == "validate" ? ruleOptions.GetRuleCatalog().Count(rule => rule.IsEnabled) : 0;

            // The temp directory and the process are created off the UI thread; the session starts the process there too.
            await TaskScheduler.Default;
            var temporaryDirectory = TempDirectoryCleaner.CreateRunDirectory();
            var sarifPath = Path.Combine(temporaryDirectory, "validation.sarif");
            var context = new CliRunContext(command, solution.Directory, configPath, configExists, disabledRules, enabledRuleCount, temporaryDirectory, sarifPath);
            var arguments = command == "validate"
                ? CliArgumentBuilder.BuildValidateArguments(configPath, sarifPath, solution.Directory, disabledRules)
                : CliArgumentBuilder.BuildAssessArguments(solution.Directory, sarifPath);
            var process = CliArgumentBuilder.CreateProcess(cliPath, arguments, solution.Directory);

            await this.ExecuteRunAsync(context, process, timeoutSeconds);
        }
        catch
        {
            this.processRegistry.ReleaseReservation();
            throw;
        }
    }

    /// <summary>Owns the process for its whole life: banner → session → outcome → slot release and temp cleanup.</summary>
    private async Task ExecuteRunAsync(CliRunContext context, Process process, int timeoutSeconds)
    {
        var output = this.output!;
        try
        {
            // Last check before the process exists: a Cancel that landed after consent must still win.
            if (!this.processRegistry.ShouldStartAfterConsent())
            {
                await output.WriteAsync("[DataGuard] Cancel was requested before " + context.Command + " started; nothing was executed.\r\n");
                return;
            }

            await output.WriteCommandBannerAsync(context.Command, context.SolutionDirectory, context.ConfigPath, context.EnabledRuleCount, context.DisabledRuleIds.Count);
            if (!context.ConfigExists)
            {
                await output.WriteAsync("[DataGuard] No " + ConfigFileName + " in the solution directory: only source-only rules run and no schema was compared. Run 'dataguard init' to create one.\r\n");
            }

            var session = new CliRunSession(this.processRegistry, this.ruleInventory, output.WriteAsync, output.WriteLinesAsync, output.SetStatusAsync);
            var outcome = await session.RunAsync(context.Command, process, timeoutSeconds, context.SarifPath);
            await this.HandleRunOutcomeAsync(context, outcome);
        }
        catch (Exception ex)
        {
            await output.WriteAsync("[DataGuard] Failed to run " + context.Command + ": " + DataGuardLogger.Redact(ex.Message) + "\r\n");
        }
        finally
        {
            this.processRegistry.Complete(process);
            process.Dispose();
            TempDirectoryCleaner.SafeDeleteDirectory(context.TemporaryDirectory);
        }
    }
}
