// <copyright file="DataGuardPackage.Commands.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

/// <summary>Command execution: trust gate → CLI discovery → ide-safe run → SARIF → Error List.</summary>
public sealed partial class DataGuardPackage
{
    private const string ConfigFileName = ".dataguard.yml";

    /// <summary>Sentinel exit code recorded in the log when the CLI was terminated on timeout.</summary>
    private const int TimedOutExitCode = -1;

    private Task RunValidationAsync(bool fromBuild)
    {
        this.ruleInventory.Clear();
        return this.RunCliAsync("validate", fromBuild);
    }

    private Task RunAssessmentAsync()
    {
        this.ruleInventory.Clear();
        return this.RunCliAsync("assess", fromBuild: false);
    }

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
        if (await this.GetServiceAsync(typeof(SVsSolution)) is not IVsSolution solution)
        {
            await output.WriteAsync("[DataGuard] Visual Studio solution service is unavailable.\r\n");
            return;
        }

        ErrorHandler.ThrowOnFailure(solution.GetSolutionInfo(out var solutionDirectory, out _, out _));
        if (string.IsNullOrWhiteSpace(solutionDirectory))
        {
            await output.WriteAsync("[DataGuard] Open a solution before running " + command + ".\r\n");
            return;
        }

        solutionDirectory = solutionDirectory!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!this.processRegistry.TryReserve())
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

            var configPath = Path.Combine(solutionDirectory, ConfigFileName);
            var configExists = File.Exists(configPath);
            if (!await this.EnsureConsentAsync(solutionDirectory, configPath, configExists, fromBuild))
            {
                this.processRegistry.ReleaseReservation();
                return;
            }

            // Clear policy: DataGuard's Error List items belong to the most recent consented run. They are
            // cleared here, so timeout / cancel / discarded runs never show stale results next to an
            // Output line that says nothing was produced.
            await this.JoinableTaskFactory.SwitchToMainThreadAsync();
            this.errorListPresenter?.Clear();

            var disabledRules = ruleOptions.GetDisabledRuleIds();
            var temporaryDirectory = TempDirectoryCleaner.CreateRunDirectory();
            var sarifPath = Path.Combine(temporaryDirectory, "validation.sarif");
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = cliPath,
                    Arguments = command == "validate"
                        ? CliArgumentBuilder.BuildValidateArguments(configPath, sarifPath, solutionDirectory, disabledRules)
                        : CliArgumentBuilder.BuildAssessArguments(solutionDirectory, sarifPath),
                    WorkingDirectory = solutionDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
                EnableRaisingEvents = true,
            };
            process.StartInfo.EnvironmentVariables["DOTNET_ROLL_FORWARD"] = "LatestMajor";

            try
            {
                await output.WriteCommandBannerAsync(
                    command, solutionDirectory, configPath,
                    command == "validate" ? ruleOptions.GetRuleCatalog().Count(rule => rule.IsEnabled) : 0,
                    command == "validate" ? disabledRules.Count : 0);
                if (!configExists)
                {
                    await output.WriteAsync("[DataGuard] No " + ConfigFileName + " in the solution directory: only source-only rules run and no schema was compared. Run 'dataguard init' to create one.\r\n");
                }

                var session = new CliRunSession(this.processRegistry, this.ruleInventory, output.WriteAsync, output.SetStatusAsync);
                var outcome = await session.RunAsync(command, process, timeoutSeconds);
                if (outcome.Cancelled)
                {
                    DataGuardLogger.LogValidationRun(command, solutionDirectory, configPath, disabledRules, outcome.ExitCode, outcome.ElapsedMs, 0);
                    await output.WriteAsync("[DataGuard] Validation cancelled by user. No diagnostics were produced.\r\n");
                    await output.SetStatusAsync("DataGuard: Cancelled");
                    return;
                }

                if (!outcome.ProceedToPublish)
                {
                    DataGuardLogger.LogValidationRun(command, solutionDirectory, configPath, disabledRules, TimedOutExitCode, outcome.ElapsedMs, 0);
                    return;
                }

                if (outcome.Progress.IdeSafeUnsupported)
                {
                    DataGuardLogger.LogValidationRun(command, solutionDirectory, configPath, disabledRules, outcome.ExitCode, outcome.ElapsedMs, 0);
                    await output.WriteAsync("[DataGuard] The configured CLI does not support " + CliArgumentBuilder.IdeSafeOption + " and was not run in IDE-safe mode; its results were discarded. Update dataguard (dotnet tool update -g DataGuard.Cli) or clear the custom CLI path to use the bundled CLI.\r\n");
                    await output.SetStatusAsync("DataGuard: CLI too old");
                    return;
                }

                var diagnosticCount = await this.PublishSarifAsync(sarifPath, solutionDirectory);
                DataGuardLogger.LogValidationRun(command, solutionDirectory, configPath, disabledRules, outcome.ExitCode, outcome.ElapsedMs, diagnosticCount);
                await this.ReportOutcomeAsync(command, outcome);
            }
            catch (Exception ex)
            {
                await output.WriteAsync("[DataGuard] Failed to run " + command + ": " + DataGuardLogger.Redact(ex.Message) + "\r\n");
            }
            finally
            {
                this.processRegistry.Complete(process);
                process.Dispose();
                TempDirectoryCleaner.SafeDeleteDirectory(temporaryDirectory);
            }
        }
        catch
        {
            this.processRegistry.ReleaseReservation();
            throw;
        }
    }

    private async Task<bool> EnsureConsentAsync(string solutionDirectory, string configPath, bool configExists, bool fromBuild)
    {
        var consentKey = SolutionTrustGate.ComputeConsentKey(solutionDirectory, configPath);
        if (this.trustGate!.IsConsented(consentKey))
        {
            return true;
        }

        if (fromBuild)
        {
            await this.output!.WriteAsync("[DataGuard] Run Validation on Build skipped: this solution (or its " + ConfigFileName + ") has not been approved yet. Run Tools > DataGuard > Run Validation once to approve it.\r\n");
            return false;
        }

        await this.JoinableTaskFactory.SwitchToMainThreadAsync();
        var answer = VsShellUtilities.ShowMessageBox(
            this,
            SolutionTrustGate.BuildPromptText(solutionDirectory, configExists),
            "DataGuard — run validation for this solution?",
            OLEMSGICON.OLEMSGICON_QUERY,
            OLEMSGBUTTON.OLEMSGBUTTON_YESNO,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_SECOND);
        if (answer != (int)VSConstants.MessageBoxResult.IDYES)
        {
            await this.output!.WriteAsync("[DataGuard] Run declined; nothing was executed for this solution.\r\n");
            return false;
        }

        this.trustGate.RecordConsent(consentKey);
        return true;
    }
}
