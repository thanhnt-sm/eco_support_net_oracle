// <copyright file="ErrorListPresenter.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using Task = System.Threading.Tasks.Task;

/// <summary>
/// Turns validated SARIF diagnostics into Error List tasks. Navigation uses
/// <see cref="TaskProvider.Navigate(TaskListItem, Guid)"/>, which opens the document and positions the
/// caret at the task's line/column without any TextManager.Interop dependency.
/// </summary>
internal sealed class ErrorListPresenter
{
    private readonly ErrorListProvider provider;
    private readonly JoinableTaskFactory joinableTaskFactory;
    private readonly Func<string, Task> writeOutput;

    public ErrorListPresenter(ErrorListProvider provider, JoinableTaskFactory joinableTaskFactory, Func<string, Task> writeOutput)
    {
        this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
        this.joinableTaskFactory = joinableTaskFactory ?? throw new ArgumentNullException(nameof(joinableTaskFactory));
        this.writeOutput = writeOutput ?? throw new ArgumentNullException(nameof(writeOutput));
    }

    /// <summary>
    /// Clears DataGuard's Error List items. Called when a run starts (after consent) so that a run that
    /// times out, is cancelled or is discarded never leaves stale diagnostics behind. Main thread only.
    /// </summary>
    public void Clear()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        this.provider.Tasks.Clear();
    }

    /// <summary>Replaces the Error List content with the given diagnostics. Must be called on the main thread.</summary>
    public int Publish(SarifLoadResult result)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        this.provider.SuspendRefresh();
        try
        {
            this.provider.Tasks.Clear();
            foreach (var diagnostic in result.Diagnostics)
            {
                this.provider.Tasks.Add(this.CreateTask(diagnostic));
            }
        }
        finally
        {
            this.provider.ResumeRefresh();
        }

        if (result.Diagnostics.Count > 0)
        {
            this.provider.Show();
        }

        return result.Diagnostics.Count;
    }

    internal static TaskErrorCategory MapLevel(string? level)
    {
        switch (level)
        {
            case "error":
                return TaskErrorCategory.Error;
            case "warning":
                return TaskErrorCategory.Warning;
            default:
                return TaskErrorCategory.Message;
        }
    }

    private ErrorTask CreateTask(SarifDiagnostic diagnostic)
    {
        var task = new ErrorTask
        {
            Category = TaskCategory.BuildCompile,
            Column = diagnostic.Column,
            Document = diagnostic.Document,
            ErrorCategory = MapLevel(diagnostic.Level),
            Line = diagnostic.Line,
            Text = diagnostic.Message,
        };
        task.Navigate += (sender, e) =>
        {
            this.joinableTaskFactory.RunAsync(async () =>
            {
                await this.joinableTaskFactory.SwitchToMainThreadAsync();
                if (File.Exists(task.Document))
                {
                    if (!this.provider.Navigate(task, VSConstants.LOGVIEWID_Code))
                    {
                        await this.writeOutput($"[DataGuard] Cannot navigate to '{task.Document}' line {task.Line + 1}.\r\n");
                    }
                }
                else
                {
                    await this.writeOutput($"[DataGuard] Cannot navigate: file not found '{task.Document}'.\r\n");
                }
            }).FileAndForget("DataGuard/NavigateTask");
        };
        return task;
    }
}
