// <copyright file="CliRunTimeoutHandler.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

// VSTHRD003: exitTask/drains are created by CliRunSession inside the caller's JoinableTask (see the
// note in CliRunSession.cs); awaiting them here is safe only within that same JoinableTask context.
#pragma warning disable VSTHRD003

/// <summary>
/// Handles a CLI run that exceeded its timeout: terminates the process tree, reports the outcome,
/// and makes sure redirected streams are released so the command slot can be reused.
/// </summary>
internal static class CliRunTimeoutHandler
{
    private static readonly TimeSpan FailedTerminationGrace = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan DrainGrace = TimeSpan.FromSeconds(5);

    /// <summary>Returns the termination outcome; the caller publishes results only for <see cref="ProcessStopOutcome.AlreadyExited"/>.</summary>
    internal static async Task<ProcessStopOutcome> HandleAsync(
        Process process,
        Task exitTask,
        Task drains,
        string command,
        int timeoutSeconds,
        Func<string, Task> writeOutput,
        Func<string, Task> setStatus)
    {
        var termination = ProcessTerminator.StopProcess(process);
        await setStatus("DataGuard: Timed out");
        await writeOutput(DescribeTimeout(command, timeoutSeconds, termination));

        switch (termination)
        {
            case ProcessStopOutcome.Failed:
                await ReleaseAfterFailedTerminationAsync(process, exitTask, drains);
                break;
            case ProcessStopOutcome.AlreadyExited:
                await AwaitDrainsAsync(drains);
                break;
            default:
                if (await Task.WhenAny(drains, Task.Delay(DrainGrace)) == drains)
                {
                    await AwaitDrainsAsync(drains);
                }
                else
                {
                    ProcessTerminator.CloseStreams(process);
                }

                break;
        }

        return termination;
    }

    internal static string DescribeTimeout(string command, int timeoutSeconds, ProcessStopOutcome termination)
    {
        switch (termination)
        {
            case ProcessStopOutcome.Terminated:
                return "[DataGuard] " + command + " timed out after " + timeoutSeconds + " seconds and its process tree was terminated.\r\n";
            case ProcessStopOutcome.AlreadyExited:
                return "[DataGuard] " + command + " exceeded " + timeoutSeconds + " seconds but completed before termination was requested.\r\n";
            default:
                return "[DataGuard] " + command + " timed out, but its process tree could not be terminated. Stop it manually.\r\n";
        }
    }

    internal static bool ShouldForceReleaseFailedTerminationReservation(bool exitCompleted, bool drainsCompleted) =>
        !exitCompleted || !drainsCompleted;

    private static async Task ReleaseAfterFailedTerminationAsync(Process process, Task exitTask, Task drains)
    {
        bool exitCompleted;
        bool drainsCompleted;
        using (var cleanupCts = new CancellationTokenSource())
        {
            var cleanupTimeout = Task.Delay(FailedTerminationGrace, cleanupCts.Token);
            exitCompleted = await Task.WhenAny(exitTask, cleanupTimeout) == exitTask;
            drainsCompleted = exitCompleted && await Task.WhenAny(drains, cleanupTimeout) == drains;
            cleanupCts.Cancel();
        }

        if (ShouldForceReleaseFailedTerminationReservation(exitCompleted, drainsCompleted))
        {
            ProcessTerminator.CloseStreams(process);
            DataGuardLogger.LogWarning("Timed-out command did not exit or drain within 120 seconds after termination failed; releasing the command reservation.");
        }
        else
        {
            await AwaitDrainsAsync(drains);
        }
    }

    private static async Task AwaitDrainsAsync(Task drains)
    {
        try
        {
            await drains;
        }
        catch (Exception ex)
        {
            DataGuardLogger.LogWarning("Timed-out command stream drain failed: " + DataGuardLogger.Redact(ex.Message));
        }
    }
}
