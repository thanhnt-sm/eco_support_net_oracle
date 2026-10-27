// <copyright file="OutputPaneWriter.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Task = System.Threading.Tasks.Task;

/// <summary>Writes to the "StackAccord (Beta)" Output pane and the status bar; mirrors every line to the log file.</summary>
internal sealed class OutputPaneWriter
{
    private static readonly Guid OutputPaneGuid = new("b85dce85-998f-4f6a-a4fd-c2b6867d0c2a");

    private readonly JoinableTaskFactory joinableTaskFactory;
    private readonly Func<Type, Task<object?>> getServiceAsync;

    public OutputPaneWriter(JoinableTaskFactory joinableTaskFactory, Func<Type, Task<object?>> getServiceAsync)
    {
        this.joinableTaskFactory = joinableTaskFactory ?? throw new ArgumentNullException(nameof(joinableTaskFactory));
        this.getServiceAsync = getServiceAsync ?? throw new ArgumentNullException(nameof(getServiceAsync));
    }

    public async Task ActivateAsync()
    {
        await this.joinableTaskFactory.SwitchToMainThreadAsync();
        var pane = await this.GetPaneAsync();
        pane?.Activate();
    }

    public async Task WriteAsync(string text)
    {
        text = DataGuardLogger.Redact(text) + "\r\n";
        DataGuardLogger.LogInfo(text.TrimEnd('\r', '\n'));
        await this.joinableTaskFactory.SwitchToMainThreadAsync();
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

        lines = lines.Select(DataGuardLogger.Redact).ToArray();
        DataGuardLogger.LogInfoBatch(lines);
        await this.joinableTaskFactory.SwitchToMainThreadAsync();
        var pane = await this.GetPaneAsync();
        pane?.OutputStringThreadSafe(OutputBlockFormatter.RenderLines(lines));
    }

    public async Task SetStatusAsync(string text)
    {
        try
        {
            await this.joinableTaskFactory.SwitchToMainThreadAsync();
            var statusBar = await this.getServiceAsync(typeof(SVsStatusbar)) as IVsStatusbar;
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
            command == "validate" ? new[] { "Rules:  " + enabledRuleCount + " enabled, " + disabledRuleCount + " disabled" } : Array.Empty<string>());
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
            Array.Empty<string>());
    }

    private async Task WriteCommandBannerAsync(string title, string description, string solutionDirectory, string configPath, string mode, string errorsLine, IReadOnlyList<string> extraLines)
    {
        await this.ActivateAsync();
        await this.WriteLinesAsync(OutputBlockFormatter.Banner(title, description, solutionDirectory, configPath, mode, errorsLine, extraLines));
    }

    public Task WriteResultBlockAsync(ProgressReadResult progress) =>
        this.WriteLinesAsync(OutputBlockFormatter.Result(progress));

    private async Task<IVsOutputWindowPane?> GetPaneAsync()
    {
        await this.joinableTaskFactory.SwitchToMainThreadAsync();
        var outputWindow = await this.getServiceAsync(typeof(SVsOutputWindow)) as IVsOutputWindow;
        if (outputWindow == null)
        {
            return null;
        }

        var paneGuid = OutputPaneGuid;
        outputWindow.CreatePane(ref paneGuid, "StackAccord (Beta)", 1, 1);
        return ErrorHandler.Succeeded(outputWindow.GetPane(ref paneGuid, out var pane)) ? pane : null;
    }
}
