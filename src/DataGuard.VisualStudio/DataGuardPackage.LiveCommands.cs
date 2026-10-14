// <copyright file="DataGuardPackage.LiveCommands.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Task = System.Threading.Tasks.Task;

/// <summary>
/// Tools &gt; DataGuard &gt; Refresh Snapshot and Verify SQL Shapes Against Database: the two commands
/// that connect to a database. They run without <c>--ide-safe</c> and without <c>--config</c>; the
/// connection string is read by the CLI from the environment variable named in Tools &gt; Options.
/// Each run needs the solution's live consent (separate from the validate consent) plus a modal that
/// names the target host. Neither command is ever triggered by Run Validation on Build.
/// </summary>
public sealed partial class DataGuardPackage
{
    private Task RefreshSnapshotAsync() => this.RunLiveCommandAsync(LiveCommandKind.RefreshSnapshot);

    private Task VerifyShapesAsync() => this.RunLiveCommandAsync(LiveCommandKind.VerifyShape);

    private async Task RunLiveCommandAsync(LiveCommandKind kind)
    {
        await this.JoinableTaskFactory.SwitchToMainThreadAsync();
        var output = this.output!;
        var command = LiveDatabaseCommands.CommandName(kind);
        var solution = await this.ReadSolutionInfoAsync();
        if (solution == null)
        {
            await output.WriteAsync("[DataGuard] Open a solution before running " + command + ".\r\n");
            return;
        }

        bool reserved;
        lock (this.ruleInventoryLock)
        {
            reserved = RuleInventory.ClearInventoryIfReserved(this.processRegistry, this.ruleInventory);
        }

        if (!reserved)
        {
            await output.WriteAsync("[DataGuard] A DataGuard command is already running for this solution.\r\n");
            return;
        }

        try
        {
            var options = (DataGuardOptionsPage?)this.GetDialogPage(typeof(DataGuardOptionsPage));
            DataGuardLogger.Configure(options?.EnableDetailedLogging ?? true, options?.CustomLogDirectory);
            var live = LiveDatabaseOptions.Validate(options?.ConnectionEnvironmentVariable, options?.LiveProvider, options?.LiveSchema);
            if (live.Error != null)
            {
                await output.WriteAsync("[DataGuard] " + live.Error + "\r\n");
                this.processRegistry.ReleaseReservation();
                return;
            }

            var cliPath = CliLocator.FindCliExecutable(options?.CustomCliPath);
            if (string.IsNullOrEmpty(cliPath))
            {
                await output.WriteAsync(DescribeMissingCli(options?.CustomCliPath));
                this.processRegistry.ReleaseReservation();
                return;
            }

            if (!await this.EnsureLiveConsentAsync(solution, live.VariableName) || !await this.ConfirmLiveTargetAsync(kind, solution, live))
            {
                this.processRegistry.ReleaseReservation();
                return;
            }

            await TaskScheduler.Default;
            var temporaryDirectory = TempDirectoryCleaner.CreateRunDirectory();

            // verify-shape writes summary.json; for snapshot refresh the path never exists, so a timeout kill never publishes.
            var summaryPath = Path.Combine(temporaryDirectory, kind == LiveCommandKind.VerifyShape ? "summary.json" : "no-output.json");
            var context = new CliRunContext(command, solution.Directory, "(not read)", configExists: false, Array.Empty<string>(), 0, temporaryDirectory, summaryPath)
            {
                LiveCommand = kind,
                LiveOptions = live,
            };
            var arguments = kind == LiveCommandKind.RefreshSnapshot
                ? CliArgumentBuilder.BuildSnapshotRefreshArguments(live.VariableName, live.Provider, live.Schema)
                : CliArgumentBuilder.BuildVerifyShapeArguments(solution.Directory, live.VariableName, live.Provider, summaryPath);
            var process = CliArgumentBuilder.CreateProcess(cliPath, arguments, solution.Directory);

            await this.ExecuteRunAsync(context, process, options?.ValidationTimeoutSeconds ?? 300);
        }
        catch
        {
            this.processRegistry.ReleaseReservation();
            throw;
        }
    }

    /// <summary>Live consent is keyed separately from the validate consent; granting one never grants the other.</summary>
    private async Task<bool> EnsureLiveConsentAsync(SolutionInfo solution, string variableName)
    {
        await TaskScheduler.Default;
        var consentKey = SolutionTrustGate.ComputeLiveConsentKey(solution.Directory, solution.FilePath);

        await this.JoinableTaskFactory.SwitchToMainThreadAsync();
        if (this.trustGate!.IsConsented(consentKey))
        {
            return true;
        }

        var answer = VsShellUtilities.ShowMessageBox(
            this,
            SolutionTrustGate.BuildLivePromptText(solution.Directory, variableName),
            "DataGuard — allow database commands for this solution?",
            OLEMSGICON.OLEMSGICON_QUERY,
            OLEMSGBUTTON.OLEMSGBUTTON_YESNO,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_SECOND);
        if (answer != (int)VSConstants.MessageBoxResult.IDYES)
        {
            await this.output!.WriteAsync("[DataGuard] Database command declined; nothing was executed for this solution.\r\n");
            return false;
        }

        if (!this.processRegistry.ShouldStartAfterConsent())
        {
            await this.output!.WriteAsync("[DataGuard] Cancel was requested while waiting for consent; the run was not started and consent was not recorded.\r\n");
            return false;
        }

        this.trustGate.RecordConsent(consentKey);
        return true;
    }

    /// <summary>
    /// Reads the variable's value only here, in-process and unlogged, to show the target host (unmasked,
    /// so production and development hosts can be told apart). Default button is No.
    /// </summary>
    private async Task<bool> ConfirmLiveTargetAsync(LiveCommandKind kind, SolutionInfo solution, LiveDatabaseOptions live)
    {
        var host = ConnectionHostExtractor.Extract(Environment.GetEnvironmentVariable(live.VariableName));

        await this.JoinableTaskFactory.SwitchToMainThreadAsync();
        var answer = VsShellUtilities.ShowMessageBox(
            this,
            LiveDatabaseCommands.BuildConfirmationText(kind, host, live.VariableName, live.Provider, solution.Directory),
            "DataGuard — connect to the database?",
            OLEMSGICON.OLEMSGICON_WARNING,
            OLEMSGBUTTON.OLEMSGBUTTON_YESNO,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_SECOND);
        if (answer != (int)VSConstants.MessageBoxResult.IDYES)
        {
            await this.output!.WriteAsync("[DataGuard] " + LiveDatabaseCommands.CommandName(kind) + " was not confirmed; nothing was executed.\r\n");
            return false;
        }

        return true;
    }

    /// <summary>Renders the live outcome from exit code, the <c>snapshot: wrote</c> line and summary.json; the Error List is untouched.</summary>
    private async Task ReportLiveOutcomeAsync(CliRunContext context, LiveCommandKind kind, CliRunOutcome outcome)
    {
        var output = this.output!;
        if (outcome.TerminatedAtTimeout)
        {
            this.LogRun(context, TimedOutExitCode, outcome.ElapsedMs, 0);
            await output.WriteAsync("[DataGuard] " + context.Command + " was terminated at the timeout; its results were not used. A partial " + LiveDatabaseCommands.SnapshotFileName + ".*.tmp file may remain in the solution directory.\r\n");
            await output.SetStatusAsync("DataGuard: Timed out");
            return;
        }

        SnapshotEvidence? evidence = null;
        VerifyShapeSummary? summary = null;
        if (kind == LiveCommandKind.RefreshSnapshot)
        {
            evidence = LiveCommandOutcome.ResolveSnapshotEvidence(context.SolutionDirectory, outcome.Progress.SnapshotWrittenPath, context.StartedUtc);
        }
        else if (File.Exists(context.SarifPath))
        {
            try
            {
                string json;
                using (var reader = new StreamReader(context.SarifPath))
                {
                    json = await reader.ReadToEndAsync().ConfigureAwait(false);
                }

                summary = VerifyShapeSummary.TryParse(json, context.SolutionDirectory);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                await output.WriteAsync("[DataGuard] summary.json could not be read: " + DataGuardLogger.Redact(ex.Message) + "\r\n");
            }
        }

        this.LogRun(context, outcome.ExitCode, outcome.ElapsedMs, 0);
        await output.WriteAsync("[DataGuard] " + context.Command + " completed in " + outcome.ElapsedMs + " ms with exit code " + outcome.ExitCode + ".\r\n");
        var lines = LiveCommandOutcome.Describe(kind, outcome.ExitCode, evidence, summary, context.LiveOptions.VariableName);
        foreach (var line in lines)
        {
            await output.WriteAsync("[DataGuard] " + DataGuardLogger.Redact(line) + "\r\n");
        }

        await output.SetStatusAsync("DataGuard: " + lines[0]);
    }
}
