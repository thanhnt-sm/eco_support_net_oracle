// <copyright file="OutputPaneWriter.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

/// <summary>Writes to the "DataGuard" Output pane and the status bar; mirrors every line to the log file.</summary>
internal sealed class OutputPaneWriter
{
    private const string Separator = "========================================================================\r\n";
    private static readonly Guid OutputPaneGuid = new("b85dce85-998f-4f6a-a4fd-c2b6867d0c2a");

    private readonly AsyncPackage package;

    public OutputPaneWriter(AsyncPackage package)
    {
        this.package = package ?? throw new ArgumentNullException(nameof(package));
    }

    public async Task ActivateAsync()
    {
        await this.package.JoinableTaskFactory.SwitchToMainThreadAsync();
        var pane = await this.GetPaneAsync();
        pane?.Activate();
    }

    public async Task WriteAsync(string text)
    {
        text = DataGuardLogger.Redact(text) + "\r\n";
        DataGuardLogger.LogInfo(text.TrimEnd('\r', '\n'));
        await this.package.JoinableTaskFactory.SwitchToMainThreadAsync();
        var pane = await this.GetPaneAsync();
        pane?.OutputStringThreadSafe(text);
    }

    /// <summary>Writes a batch of lines with a single main-thread hop (used by <see cref="ProgressPump"/>).</summary>
    public async Task WriteLinesAsync(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
        {
            return;
        }

        lines = lines.Select(line => DataGuardLogger.Redact(line) + "\r\n").ToArray();
        DataGuardLogger.LogInfoBatch(lines.Select(line => line.TrimEnd('\r', '\n')));
        await this.package.JoinableTaskFactory.SwitchToMainThreadAsync();
        var pane = await this.GetPaneAsync();
        pane?.OutputStringThreadSafe(string.Concat(lines));
    }

    public async Task SetStatusAsync(string text)
    {
        try
        {
            await this.package.JoinableTaskFactory.SwitchToMainThreadAsync();
            var statusBar = await this.package.GetServiceAsync(typeof(SVsStatusbar)) as IVsStatusbar;
            statusBar?.SetText(DataGuardLogger.Redact(text));
        }
        catch
        {
            // Output Window feedback remains available when the status bar is unavailable.
        }
    }

    public async Task WriteCommandBannerAsync(string command, string solutionDirectory, string configPath, int enabledRuleCount, int disabledRuleCount)
    {
        var title = command == "validate" ? "Run Validation" : "Assess Workspace";
        var description = command == "validate"
            ? "Validates C# and database contracts (parameters, result shapes, types, naming, and SQL dialect)."
            : "Assesses workspace configuration, dependencies, and environment readiness.";
        await this.WriteCommandBannerAsync(
            title,
            description,
            solutionDirectory,
            configPath,
            "ide-safe (no assembly loading, no database or network access)",
            "previous results are kept until new ones load",
            command == "validate" ? "Rules:  " + enabledRuleCount + " enabled, " + disabledRuleCount + " disabled\r\n" : string.Empty);
    }

    /// <summary>Banner for Refresh Snapshot / Verify SQL Shapes: not ide-safe, no .dataguard.yml, Error List untouched.</summary>
    public Task WriteLiveCommandBannerAsync(LiveCommandKind kind, string solutionDirectory, string variableName)
    {
        var title = kind == LiveCommandKind.RefreshSnapshot ? "Refresh Snapshot" : "Verify SQL Shapes Against Database";
        var description = kind == LiveCommandKind.RefreshSnapshot
            ? "Reads the live database schema and stored procedures and writes " + LiveDatabaseCommands.SnapshotFileName + " into the solution directory."
            : "Describes every read SQL query of the solution against the live database and reports shape mismatches.";
        return this.WriteCommandBannerAsync(
            title,
            description,
            solutionDirectory,
            "(not read)",
            LiveDatabaseCommands.DescribeMode(variableName),
            "the Error List is not changed by this command",
            string.Empty);
    }

    private async Task WriteCommandBannerAsync(string title, string description, string solutionDirectory, string configPath, string mode, string errorsLine, string extraLines)
    {
        await this.ActivateAsync();
        await this.WriteAsync(
            Separator +
            "DataGuard — " + title + "\r\n" +
            Separator +
            "What:   " + description + "\r\n" +
            "Scope:  " + solutionDirectory + "\r\n" +
            "Config: " + configPath + "\r\n" +
            "Mode:   " + mode + "\r\n" +
            "Errors: " + errorsLine + "\r\n" +
            extraLines +
            Separator);
    }

    public async Task WriteResultBlockAsync(ProgressReadResult progress)
    {
        var resultText = progress.HasSummary
            ? "Result: " + progress.ErrorCount + " errors, " + progress.WarningCount + " warnings\r\n"
            : "Result: No final validation summary was produced.\r\n";
        var groundTruth = ExitCodeExplainer.DescribeGroundTruth(progress);
        await this.WriteAsync(
            Separator +
            resultText +
            (groundTruth == null ? string.Empty : groundTruth + "\r\n") +
            "Action: Open Error List to see details and jump to source locations.\r\n" +
            "Docs:   Tools -> Options -> DataGuard -> Validation Rules\r\n" +
            Separator);
    }

    private async Task<IVsOutputWindowPane?> GetPaneAsync()
    {
        await this.package.JoinableTaskFactory.SwitchToMainThreadAsync();
        var outputWindow = await this.package.GetServiceAsync(typeof(SVsOutputWindow)) as IVsOutputWindow;
        if (outputWindow == null)
        {
            return null;
        }

        var paneGuid = OutputPaneGuid;
        outputWindow.CreatePane(ref paneGuid, "DataGuard", 1, 1);
        return ErrorHandler.Succeeded(outputWindow.GetPane(ref paneGuid, out var pane)) ? pane : null;
    }
}
