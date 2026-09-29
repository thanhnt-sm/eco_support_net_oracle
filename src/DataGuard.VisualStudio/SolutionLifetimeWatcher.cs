// <copyright file="SolutionLifetimeWatcher.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

/// <summary>
/// Follows the solution lifetime: owns the <see cref="IVsSolutionEvents"/> advise cookie and calls the
/// package back before the solution closes so a running CLI is stopped and stale Error List items are
/// cleared. The "did the solution change under the run" decision and the non-blocking stop request
/// are pure helpers.
/// </summary>
internal sealed class SolutionLifetimeWatcher : IVsSolutionEvents, IDisposable
{
    private readonly IVsSolution solution;
    private readonly Action onBeforeCloseSolution;
    private uint cookie;

    public SolutionLifetimeWatcher(IVsSolution solution, Action onBeforeCloseSolution)
    {
        this.solution = solution ?? throw new ArgumentNullException(nameof(solution));
        this.onBeforeCloseSolution = onBeforeCloseSolution ?? throw new ArgumentNullException(nameof(onBeforeCloseSolution));
    }

    /// <summary>Subscribes to solution events; failures are logged and leave the watcher inert.</summary>
    public void Advise()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            ErrorHandler.ThrowOnFailure(this.solution.AdviseSolutionEvents(this, out this.cookie));
        }
        catch (Exception ex)
        {
            this.cookie = 0;
            DataGuardLogger.LogWarning("Could not subscribe to solution events; runs will not follow solution close: " + DataGuardLogger.Redact(ex.Message));
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (this.cookie == 0)
        {
            return;
        }

#pragma warning disable VSTHRD108 // Thread affinity checks should be unconditional
#pragma warning disable VSTHRD010 // Invoke single-threaded types on Main thread
        try
        {
            this.solution.UnadviseSolutionEvents(this.cookie);
        }
        catch (Exception ex)
        {
            DataGuardLogger.LogWarning("Could not unsubscribe from solution events: " + DataGuardLogger.Redact(ex.Message));
        }
#pragma warning restore VSTHRD010
#pragma warning restore VSTHRD108

        this.cookie = 0;
    }

    /// <summary>
    /// Null when the captured solution directory is still the current one; otherwise the Output line
    /// explaining that the run's results were discarded because the solution changed or closed.
    /// </summary>
    internal static string? DescribeSolutionMismatch(string command, string capturedDirectory, string? currentDirectory)
    {
        if (IsSameSolutionDirectory(capturedDirectory, currentDirectory))
        {
            return null;
        }

        return "[DataGuard] The solution changed or closed while " + command + " was running; its results were discarded.\r\n";
    }

    /// <summary>
    /// Stops the run on the thread pool and records the cancellation once the stop has finished. The
    /// returned task is not meant to be awaited by the solution-close handler (that would block the UI
    /// thread on taskkill for up to 6 s); a result that arrives late is discarded by the publish gate's
    /// solution re-check.
    /// </summary>
    internal static Task<ProcessStopOutcome> RequestStopAsync(
        Process process,
        Func<Process, ProcessStopOutcome> stop,
        Func<Process, ProcessStopOutcome, bool> markCancelled)
    {
        return Task.Run(() =>
        {
            var outcome = stop(process);
            markCancelled(process, outcome);
            return outcome;
        });
    }

    internal static bool IsSameSolutionDirectory(string capturedDirectory, string? currentDirectory)
    {
        if (string.IsNullOrWhiteSpace(currentDirectory))
        {
            return false;
        }

        return string.Equals(NormalizeDirectory(capturedDirectory), NormalizeDirectory(currentDirectory!), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDirectory(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    /// <inheritdoc />
    public int OnBeforeCloseSolution(object pUnkReserved)
    {
        try
        {
            this.onBeforeCloseSolution();
        }
        catch (Exception ex)
        {
            DataGuardLogger.LogWarning("Solution-close handler failed: " + DataGuardLogger.Redact(ex.Message));
        }

        return VSConstants.S_OK;
    }

    /// <inheritdoc />
    public int OnAfterOpenProject(IVsHierarchy pHierarchy, int fAdded) => VSConstants.S_OK;

    /// <inheritdoc />
    public int OnQueryCloseProject(IVsHierarchy pHierarchy, int fRemoving, ref int pfCancel) => VSConstants.S_OK;

    /// <inheritdoc />
    public int OnBeforeCloseProject(IVsHierarchy pHierarchy, int fRemoved) => VSConstants.S_OK;

    /// <inheritdoc />
    public int OnAfterLoadProject(IVsHierarchy pStubHierarchy, IVsHierarchy pRealHierarchy) => VSConstants.S_OK;

    /// <inheritdoc />
    public int OnQueryUnloadProject(IVsHierarchy pRealHierarchy, ref int pfCancel) => VSConstants.S_OK;

    /// <inheritdoc />
    public int OnBeforeUnloadProject(IVsHierarchy pRealHierarchy, IVsHierarchy pStubHierarchy) => VSConstants.S_OK;

    /// <inheritdoc />
    public int OnAfterOpenSolution(object pUnkReserved, int fNewSolution) => VSConstants.S_OK;

    /// <inheritdoc />
    public int OnQueryCloseSolution(object pUnkReserved, ref int pfCancel) => VSConstants.S_OK;

    /// <inheritdoc />
    public int OnAfterCloseSolution(object pUnkReserved) => VSConstants.S_OK;
}
