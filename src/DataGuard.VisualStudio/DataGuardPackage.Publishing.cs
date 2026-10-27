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
        "Reinstall the StackAccord extension (the bundled CLI is missing) or set Tools > Options > StackAccord > General > Custom CLI Executable Path to a dataguard.exe from GitHub Releases (verify its SHA-256).";

    private async Task HandleRunOutcomeAsync(CliRunContext context, CliRunOutcome outcome)
    {
        this.CaptureSupportOutcome(outcome);
        var output = this.output!;
        if (outcome.Cancelled)
        {
            this.LogRun(context, outcome.ExitCode, outcome.ElapsedMs, 0);
            await output.WriteAsync("[StackAccord] " + context.Command + " was stopped before completion. No diagnostics were produced; previous Error List results were kept.\r\n");
            await output.SetStatusAsync("StackAccord: Cancelled");
            return;
        }

        if (!outcome.ProceedToPublish)
        {
            this.LogRun(context, TimedOutExitCode, outcome.ElapsedMs, 0);
            return;
        }

        // Live commands never print the ide-safe handshake and never touch the Error List: their outcome is
        // rendered from the exit code, the "snapshot: wrote" line and verify-shape's summary.json.
        if (context.LiveCommand is { } liveKind)
        {
            await this.ReportLiveOutcomeAsync(context, liveKind, outcome);
            return;
        }

        var verdict = PublishGate.Decide(outcome.Progress, outcome.ExitCode);
        if (verdict != PublishVerdict.Publish)
        {
            this.MarkSupportDiscarded();
            this.LogRun(context, outcome.ExitCode, outcome.ElapsedMs, 0);
            await output.WriteAsync(DescribeDiscard(verdict));
            await output.SetStatusAsync(verdict == PublishVerdict.CliTooOld ? "StackAccord: CLI too old" : "StackAccord: Results discarded");
            return;
        }

        var currentSolution = await this.ReadSolutionInfoAsync();
        var mismatch = SolutionLifetimeWatcher.DescribeSolutionMismatch(context.Command, context.SolutionDirectory, currentSolution?.Directory);
        if (mismatch != null)
        {
            this.MarkSupportDiscarded();
            this.LogRun(context, outcome.ExitCode, outcome.ElapsedMs, 0);
            await output.WriteAsync(mismatch);
            await output.SetStatusAsync("StackAccord: Results discarded");
            return;
        }

        var sarifExists = File.Exists(context.SarifPath);
        var diagnosticCount = sarifExists
            ? await this.PublishSarifAsync(context.SarifPath, context.SolutionDirectory)
            : 0;
        if (!sarifExists)
        {
            await output.WriteAsync("[StackAccord] " + context.Command + " produced no SARIF file; previous Error List results were kept.\r\n");
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
            await output.WriteAsync("[StackAccord] " + loaded.Error + " Previous Error List results were kept.\r\n");
            return 0;
        }

        lock (this.supportSummaryLock)
        {
            var fileIds = new System.Collections.Generic.Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
            var captured = 0;
            foreach (var diagnostic in loaded.Diagnostics)
            {
                if (captured++ > 200)
                {
                    break;
                }

                if (!fileIds.TryGetValue(diagnostic.Document, out var fileId))
                {
                    fileId = fileIds.Count + 1;
                    fileIds.Add(diagnostic.Document, fileId);
                }

                this.supportSummary?.AddDiagnostic(fileId, diagnostic.RuleId, diagnostic.Level, diagnostic.Line, diagnostic.Column);
            }
        }

        await this.JoinableTaskFactory.SwitchToMainThreadAsync();
        if (this.errorListPresenter == null)
        {
            await output.WriteAsync("[StackAccord] Error List provider unavailable; " + loaded.Diagnostics.Count + " diagnostics were not displayed.\r\n");
            return loaded.Diagnostics.Count;
        }

        // Publish clears DataGuard's previous items immediately before adding the new ones.
        var count = this.errorListPresenter.Publish(loaded);
        await output.WriteAsync("[StackAccord] Loaded " + count + " diagnostics into Error List.\r\n");
        if (loaded.SkippedCount > 0)
        {
            await output.WriteAsync("[StackAccord] " + loaded.SkippedCount + " SARIF results were skipped (malformed or outside the solution directory).\r\n");
        }

        if (loaded.TruncatedCount > 0)
        {
            await output.WriteAsync("[StackAccord] " + loaded.TruncatedCount + " more results were not shown; the Error List is capped at " + SarifErrorListPublisher.MaxErrorListTasks + " items per run. Run 'dataguard validate --format sarif' from the CLI for the full report.\r\n");
        }

        return count;
    }

    private async Task ReportOutcomeAsync(string command, CliRunOutcome outcome, bool sarifExists)
    {
        var output = this.output!;
        var progress = outcome.Progress;
        await output.WriteAsync("[StackAccord] " + command + " completed in " + outcome.ElapsedMs + " ms with exit code " + outcome.ExitCode + ".\r\n");
        if (progress.HasSummary || outcome.ExitCode != 0 || outcome.TerminatedAtTimeout)
        {
            await output.WriteAsync("[StackAccord] " + ExitCodeExplainer.Explain(command, outcome, sarifExists) + "\r\n");
        }

        await output.WriteResultBlockAsync(progress);
        await output.SetStatusAsync(ExitCodeExplainer.DescribeStatus(progress));
    }

    private void LogRun(CliRunContext context, int exitCode, long elapsedMs, int diagnosticCount)
    {
        DataGuardLogger.LogValidationRun(context.Command, context.SolutionDirectory, context.ConfigPath, context.DisabledRuleIds, exitCode, elapsedMs, diagnosticCount);
    }

    private static string DescribeDiscard(PublishVerdict verdict)
    {
        if (verdict == PublishVerdict.CliTooOld)
        {
            return "[StackAccord] The configured CLI does not support " + CliArgumentBuilder.IdeSafeOption + " (it is too old) and was not run in IDE-safe mode; its results were discarded. " + ReinstallGuidance + "\r\n";
        }

        return "[StackAccord] CLI did not confirm ide-safe mode; results discarded.\r\n";
    }

    private static string DescribeMissingCli(string? customCliPath)
    {
        if (!string.IsNullOrWhiteSpace(customCliPath))
        {
            return "[StackAccord] Custom CLI path '" + customCliPath!.Trim() + "' was not found or is not an absolute path to an .exe. Check Tools > Options > StackAccord > General > Custom CLI Executable Path.\r\n";
        }

        return "[StackAccord] CLI executable was not found. " + ReinstallGuidance + "\r\n";
    }
}
