// <copyright file="DataGuardPackage.Publishing.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

using System.IO;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;

/// <summary>
/// Result handling: cancelled/timed-out runs, the ide-safe publish verdict, the solution re-check, and
/// SARIF → Error List. The Error List is only replaced when a new SARIF loads; otherwise previous
/// results are kept.
/// </summary>
public sealed partial class DataGuardPackage
{
    private const string ReinstallGuidance =
        "Reinstall the DataGuard extension (the bundled CLI is missing) or set Tools > Options > DataGuard > General > Custom CLI Executable Path to a dataguard.exe from GitHub Releases (verify its SHA-256).";

    private async Task HandleRunOutcomeAsync(CliRunContext context, CliRunOutcome outcome)
    {
        var output = this.output!;
        if (outcome.Cancelled)
        {
            this.LogRun(context, outcome.ExitCode, outcome.ElapsedMs, 0);
            await output.WriteAsync("[DataGuard] " + context.Command + " was stopped before completion. No diagnostics were produced; previous Error List results were kept.\r\n");
            await output.SetStatusAsync("DataGuard: Cancelled");
            return;
        }

        if (!outcome.ProceedToPublish)
        {
            this.LogRun(context, TimedOutExitCode, outcome.ElapsedMs, 0);
            return;
        }

        var verdict = PublishGate.Decide(outcome.Progress, outcome.ExitCode);
        if (verdict != PublishVerdict.Publish)
        {
            this.LogRun(context, outcome.ExitCode, outcome.ElapsedMs, 0);
            await output.WriteAsync(DescribeDiscard(verdict));
            await output.SetStatusAsync(verdict == PublishVerdict.CliTooOld ? "DataGuard: CLI too old" : "DataGuard: Results discarded");
            return;
        }

        var currentSolution = await this.ReadSolutionInfoAsync();
        var mismatch = SolutionLifetimeWatcher.DescribeSolutionMismatch(context.Command, context.SolutionDirectory, currentSolution?.Directory);
        if (mismatch != null)
        {
            this.LogRun(context, outcome.ExitCode, outcome.ElapsedMs, 0);
            await output.WriteAsync(mismatch);
            await output.SetStatusAsync("DataGuard: Results discarded");
            return;
        }

        var sarifExists = File.Exists(context.SarifPath);
        var diagnosticCount = sarifExists
            ? await this.PublishSarifAsync(context.SarifPath, context.SolutionDirectory)
            : 0;
        if (!sarifExists)
        {
            await output.WriteAsync("[DataGuard] " + context.Command + " produced no SARIF file; previous Error List results were kept.\r\n");
        }

        this.LogRun(context, outcome.TerminatedAtTimeout ? TimedOutExitCode : outcome.ExitCode, outcome.ElapsedMs, diagnosticCount);
        await this.ReportOutcomeAsync(context.Command, outcome, sarifExists);
    }

    /// <summary>Loads the SARIF and replaces the Error List; an unreadable file keeps the previous items.</summary>
    private async Task<int> PublishSarifAsync(string sarifPath, string solutionDirectory)
    {
        var output = this.output!;
        string sarifJson;
        using (var reader = new StreamReader(sarifPath))
        {
            sarifJson = await reader.ReadToEndAsync().ConfigureAwait(false);
        }

        var loaded = SarifErrorListPublisher.Load(sarifJson, solutionDirectory);
        if (loaded.Error != null)
        {
            await output.WriteAsync("[DataGuard] " + loaded.Error + " Previous Error List results were kept.\r\n");
            return 0;
        }

        await this.JoinableTaskFactory.SwitchToMainThreadAsync();
        if (this.errorListPresenter == null)
        {
            await output.WriteAsync("[DataGuard] Error List provider unavailable; " + loaded.Diagnostics.Count + " diagnostics were not displayed.\r\n");
            return loaded.Diagnostics.Count;
        }

        // Publish clears DataGuard's previous items immediately before adding the new ones.
        var count = this.errorListPresenter.Publish(loaded);
        await output.WriteAsync("[DataGuard] Loaded " + count + " diagnostics into Error List.\r\n");
        if (loaded.SkippedCount > 0)
        {
            await output.WriteAsync("[DataGuard] " + loaded.SkippedCount + " SARIF results were skipped (malformed or outside the solution directory).\r\n");
        }

        if (loaded.TruncatedCount > 0)
        {
            await output.WriteAsync("[DataGuard] " + loaded.TruncatedCount + " more results were not shown; the Error List is capped at " + SarifErrorListPublisher.MaxErrorListTasks + " items per run. Run 'dataguard validate --format sarif' from the CLI for the full report.\r\n");
        }

        return count;
    }

    private async Task ReportOutcomeAsync(string command, CliRunOutcome outcome, bool sarifExists)
    {
        var output = this.output!;
        var progress = outcome.Progress;
        await output.WriteAsync("[DataGuard] " + command + " completed in " + outcome.ElapsedMs + " ms with exit code " + outcome.ExitCode + ".\r\n");
        if (progress.HasSummary || outcome.ExitCode != 0 || outcome.TerminatedAtTimeout)
        {
            await output.WriteAsync("[DataGuard] " + ExitCodeExplainer.Explain(command, outcome.ExitCode, progress.HasSummary, progress.WarningCount, sarifExists, outcome.TerminatedAtTimeout) + "\r\n");
        }

        await output.WriteResultBlockAsync(progress);
        await output.SetStatusAsync(progress.HasSummary
            ? "DataGuard: " + progress.ErrorCount + " errors, " + progress.WarningCount + " warnings"
            : "DataGuard: Result summary unavailable");
    }

    private void LogRun(CliRunContext context, int exitCode, long elapsedMs, int diagnosticCount)
    {
        DataGuardLogger.LogValidationRun(context.Command, context.SolutionDirectory, context.ConfigPath, context.DisabledRuleIds, exitCode, elapsedMs, diagnosticCount);
    }

    private static string DescribeDiscard(PublishVerdict verdict)
    {
        if (verdict == PublishVerdict.CliTooOld)
        {
            return "[DataGuard] The configured CLI does not support " + CliArgumentBuilder.IdeSafeOption + " (it is too old) and was not run in IDE-safe mode; its results were discarded. " + ReinstallGuidance + "\r\n";
        }

        return "[DataGuard] CLI did not confirm ide-safe mode; results discarded.\r\n";
    }

    private static string DescribeMissingCli(string? customCliPath)
    {
        if (!string.IsNullOrWhiteSpace(customCliPath))
        {
            return "[DataGuard] Custom CLI path '" + customCliPath!.Trim() + "' was not found or is not an absolute path to an .exe. Check Tools > Options > DataGuard > General > Custom CLI Executable Path.\r\n";
        }

        return "[DataGuard] CLI executable was not found. " + ReinstallGuidance + "\r\n";
    }
}
