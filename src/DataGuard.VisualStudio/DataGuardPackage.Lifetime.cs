// <copyright file="DataGuardPackage.Lifetime.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

/// <summary>Paths that stop a run early: the Cancel command and the solution closing under it.</summary>
public sealed partial class DataGuardPackage
{
    /// <summary>
    /// Raised by <see cref="SolutionLifetimeWatcher"/> on the main thread before the solution closes:
    /// stops the running CLI (its results will be discarded by the publish gate) and clears the Error
    /// List so no diagnostics survive into the next solution.
    /// </summary>
    private void OnBeforeCloseSolution()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var process = this.processRegistry.Active;
        if (process != null)
        {
            var outcome = ProcessTerminator.StopProcess(process);
            this.processRegistry.TryMarkCancelled(process, outcome);
            this.JoinableTaskFactory.RunAsync(() => this.output!.WriteAsync("[DataGuard] The solution is closing; the running DataGuard command was stopped.\r\n"))
                .FileAndForget("DataGuard/SolutionClosing");
        }

        try
        {
            this.errorListPresenter?.Clear();
        }
        catch (Exception ex)
        {
            DataGuardLogger.LogWarning("Could not clear the Error List on solution close: " + DataGuardLogger.Redact(ex.Message));
        }
    }

    private async Task CancelValidationAsync()
    {
        var processToStop = this.processRegistry.Active;
        if (processToStop == null && this.processRegistry.RequestCancelPending())
        {
            await this.output!.WriteAsync("[DataGuard] A run is waiting for consent; it will not start.\r\n");
            return;
        }

        var outcome = ProcessStopOutcome.AlreadyExited;
        if (processToStop != null)
        {
            outcome = await Task.Run(() => ProcessTerminator.StopProcess(processToStop));
            this.processRegistry.TryMarkCancelled(processToStop, outcome);
        }

        switch (outcome)
        {
            case ProcessStopOutcome.Terminated:
                await this.output!.WriteAsync("[DataGuard] DataGuard command cancelled by user. No further diagnostics will be produced.\r\n");
                await this.output.SetStatusAsync("DataGuard: Cancelled");
                break;
            case ProcessStopOutcome.AlreadyExited:
                await this.output!.WriteAsync("[DataGuard] The command already completed; processing diagnostics.\r\n");
                break;
            default:
                await this.output!.WriteAsync("[DataGuard] Cancellation requested, but the process tree could not be terminated. Stop it manually.\r\n");
                break;
        }
    }
}
