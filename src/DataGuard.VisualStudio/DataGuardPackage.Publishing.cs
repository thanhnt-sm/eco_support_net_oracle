// <copyright file="DataGuardPackage.Publishing.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

using System.IO;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;

/// <summary>Result publishing: SARIF file → validated diagnostics → Error List and Output pane summary.</summary>
public sealed partial class DataGuardPackage
{
    private async Task<int> PublishSarifAsync(string sarifPath, string solutionDirectory)
    {
        var output = this.output!;
        if (!File.Exists(sarifPath))
        {
            await output.WriteAsync("[DataGuard] Validation produced no SARIF diagnostics.\r\n");
            return 0;
        }

        string sarifJson;
        using (var reader = new StreamReader(sarifPath))
        {
            sarifJson = await reader.ReadToEndAsync().ConfigureAwait(false);
        }

        var loaded = SarifErrorListPublisher.Load(sarifJson, solutionDirectory);
        if (loaded.Error != null)
        {
            await output.WriteAsync("[DataGuard] " + loaded.Error + "\r\n");
        }

        await this.JoinableTaskFactory.SwitchToMainThreadAsync();
        if (this.errorListPresenter == null)
        {
            await output.WriteAsync("[DataGuard] Error List provider unavailable; " + loaded.Diagnostics.Count + " diagnostics were not displayed.\r\n");
            return loaded.Diagnostics.Count;
        }

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

    private async Task ReportOutcomeAsync(string command, CliRunOutcome outcome)
    {
        var output = this.output!;
        var progress = outcome.Progress;
        await output.WriteAsync("[DataGuard] " + command + " completed in " + outcome.ElapsedMs + " ms with exit code " + outcome.ExitCode + ".\r\n");
        if (progress.HasSummary || outcome.ExitCode != 0)
        {
            await output.WriteAsync("[DataGuard] " + ExitCodeExplainer.Explain(command, outcome.ExitCode, progress.HasSummary, progress.WarningCount) + "\r\n");
        }

        await output.WriteResultBlockAsync(progress);
        await output.SetStatusAsync(outcome.ExitCode == ExitCodeExplainer.CancelledExitCode
            ? "DataGuard: Cancelled"
            : progress.HasSummary
                ? "DataGuard: " + progress.ErrorCount + " errors, " + progress.WarningCount + " warnings"
                : "DataGuard: Result summary unavailable");
    }

    private static string DescribeMissingCli(string? customCliPath)
    {
        if (!string.IsNullOrWhiteSpace(customCliPath))
        {
            return "[DataGuard] Custom CLI path '" + customCliPath!.Trim() + "' was not found or is not an absolute path to an .exe. Check Tools > Options > DataGuard > General > Custom CLI Executable Path.\r\n";
        }

        return "[DataGuard] CLI executable was not found. The bundled cli\\dataguard.exe is missing from this installation; reinstall the extension, or install the CLI with 'dotnet tool install -g DataGuard.Cli' and restart Visual Studio, or set Tools > Options > DataGuard > General > Custom CLI Executable Path.\r\n";
    }
}
