// <copyright file="ErrorListPresenter.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Threading;
using Task = System.Threading.Tasks.Task;

/// <summary>
/// Turns validated SARIF diagnostics into Error List tasks. Navigation uses
/// <c>VsShellUtilities.OpenDocument</c> and <c>IVsTextView.SetCaretPos</c>
/// with 0-based buffer coordinates matching <c>ErrorTask.Line</c> and <c>ErrorTask.Column</c>.
/// </summary>
internal sealed class ErrorListPresenter
{
    private readonly ErrorListProvider? provider;
    private readonly JoinableTaskFactory? joinableTaskFactory;
    private readonly Func<string, Task>? writeOutput;
    private readonly IServiceProvider? serviceProvider;
    private readonly IVsSolution? solution;
    private readonly Func<string, IVsHierarchy?>? hierarchyResolver;

    public ErrorListPresenter(
        ErrorListProvider provider,
        JoinableTaskFactory joinableTaskFactory,
        Func<string, Task> writeOutput,
        IServiceProvider? serviceProvider = null,
        IVsSolution? solution = null,
        Func<string, IVsHierarchy?>? hierarchyResolver = null)
    {
        this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
        this.joinableTaskFactory = joinableTaskFactory ?? throw new ArgumentNullException(nameof(joinableTaskFactory));
        this.writeOutput = writeOutput ?? throw new ArgumentNullException(nameof(writeOutput));
        this.serviceProvider = serviceProvider;
        this.solution = solution;
        this.hierarchyResolver = hierarchyResolver;
    }

    internal ErrorListPresenter(
        Func<string, IVsHierarchy?> hierarchyResolver,
        IServiceProvider? serviceProvider = null,
        JoinableTaskFactory? joinableTaskFactory = null)
    {
        this.hierarchyResolver = hierarchyResolver ?? throw new ArgumentNullException(nameof(hierarchyResolver));
        this.serviceProvider = serviceProvider;
        this.joinableTaskFactory = joinableTaskFactory;
    }

    /// <summary>
    /// Clears DataGuard's Error List items. Called only when the solution closes; a run keeps the
    /// previous results until <see cref="Publish"/> replaces them with a new SARIF. Main thread only.
    /// </summary>
    public void Clear()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        this.provider?.Tasks.Clear();
    }

    /// <summary>Replaces the Error List content with the given diagnostics. Must be called on the main thread.</summary>
    public int Publish(SarifLoadResult result)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (this.provider == null)
        {
            return 0;
        }

        this.provider.SuspendRefresh();
        try
        {
            this.provider.Tasks.Clear();
            var hierarchyCache = new Dictionary<string, IVsHierarchy?>(StringComparer.OrdinalIgnoreCase);
            foreach (var diagnostic in result.Diagnostics)
            {
                this.provider.Tasks.Add(this.CreateTask(diagnostic, hierarchyCache));
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

    internal ErrorTask CreateTask(SarifDiagnostic diagnostic, Dictionary<string, IVsHierarchy?> hierarchyCache)
    {
        var task = new ErrorTask
        {
            Category = TaskCategory.BuildCompile,
            Column = diagnostic.Column,
            Document = diagnostic.Document,
            ErrorCategory = MapLevel(diagnostic.Level),
            Line = diagnostic.Line,
            Text = diagnostic.Message,
            HierarchyItem = this.ResolveHierarchy(diagnostic.Document, hierarchyCache),
        };
        task.Navigate += (sender, e) =>
        {
            if (this.joinableTaskFactory == null)
            {
                return;
            }

            this.joinableTaskFactory.RunAsync(async () =>
            {
                await this.joinableTaskFactory.SwitchToMainThreadAsync();
                if (!File.Exists(task.Document))
                {
                    if (this.writeOutput != null)
                    {
                        await this.writeOutput($"[DataGuard] Cannot navigate: file not found '{task.Document}'.\r\n");
                    }

                    return;
                }

                if (this.serviceProvider != null)
                {
                    VsShellUtilities.OpenDocument(
                        this.serviceProvider,
                        task.Document,
                        VSConstants.LOGVIEWID_Code,
                        out _,
                        out _,
                        out IVsWindowFrame frame,
                        out IVsTextView view);

                    frame?.Show();
                    if (view != null)
                    {
                        // Convert 1-based ErrorTask to 0-based buffer position
                        var zeroBasedLine = Math.Max(0, task.Line - 1);
                        var zeroBasedCol = Math.Max(0, task.Column - 1);
                        view.SetCaretPos(zeroBasedLine, zeroBasedCol);
                        view.CenterLines(zeroBasedLine, 1);
                    }
                }
                else if (this.provider != null)
                {
                    if (!this.provider.Navigate(task, VSConstants.LOGVIEWID_Code))
                    {
                        if (this.writeOutput != null)
                        {
                            await this.writeOutput($"[DataGuard] Cannot navigate to '{task.Document}' line {task.Line}.\r\n");
                        }
                    }
                }
            }).FileAndForget("DataGuard/NavigateTask");
        };
        return task;
    }

    internal IVsHierarchy? ResolveHierarchy(string documentPath, Dictionary<string, IVsHierarchy?> cache)
    {
        if (string.IsNullOrEmpty(documentPath))
        {
            return null;
        }

        if (cache.TryGetValue(documentPath, out var cachedHierarchy))
        {
            return cachedHierarchy;
        }

        IVsHierarchy? hierarchy = null;
        if (this.hierarchyResolver != null)
        {
            hierarchy = this.hierarchyResolver(documentPath);
        }
        else if (this.serviceProvider != null)
        {
            try
            {
#pragma warning disable VSTHRD108 // Thread affinity checks should be unconditional
#pragma warning disable VSTHRD010 // Invoke single-threaded types on Main thread
                ThreadHelper.ThrowIfNotOnUIThread();
                var proj = VsShellUtilities.GetProject(this.serviceProvider, documentPath);
                hierarchy = proj as IVsHierarchy;
#pragma warning restore VSTHRD010
#pragma warning restore VSTHRD108
            }
            catch (Exception)
            {
                // Non-fatal: document does not belong to a loaded project in the solution
                hierarchy = null;
            }
        }

        cache[documentPath] = hierarchy;
        return hierarchy;
    }
}
