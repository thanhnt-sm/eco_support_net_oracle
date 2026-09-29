// <copyright file="CliProcessRegistry.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

using System;
using System.Diagnostics;
using System.Threading.Tasks;

/// <summary>
/// Single-run guard for CLI processes: at most one command runs per package instance, cancellation
/// is recorded against the exact process it stopped, a Cancel issued while the run is still waiting
/// for consent aborts it, and Dispose can detach the live process.
/// </summary>
internal sealed class CliProcessRegistry
{
    private readonly object gate = new();
    private Process? activeProcess;
    private Process? cancelledProcess;
    private bool commandReserved;
    private bool cancelPending;

    /// <summary>True from a successful <see cref="TryReserve"/> until the run completes or is released.</summary>
    public bool IsReserved
    {
        get
        {
            lock (this.gate)
            {
                return this.commandReserved;
            }
        }
    }

    public Process? Active
    {
        get
        {
            lock (this.gate)
            {
                return this.activeProcess;
            }
        }
    }

    /// <summary>Reserves the slot for a new command; false when a command is already running or reserved.</summary>
    public bool TryReserve()
    {
        lock (this.gate)
        {
            if (this.activeProcess != null || this.commandReserved)
            {
                return false;
            }

            this.commandReserved = true;

            // The only reset needed: a Cancel can be recorded only while a reservation is held.
            this.cancelPending = false;
            return true;
        }
    }

    public void ReleaseReservation()
    {
        lock (this.gate)
        {
            this.commandReserved = false;
        }
    }

    /// <summary>Records a Cancel for a reserved run that has no process yet; false when nothing is waiting.</summary>
    public bool RequestCancelPending()
    {
        lock (this.gate)
        {
            if (!this.commandReserved || this.activeProcess != null)
            {
                return false;
            }

            this.cancelPending = true;
            return true;
        }
    }

    /// <summary>False when a Cancel arrived while the run was waiting for consent; the run must not start.</summary>
    public bool ShouldStartAfterConsent()
    {
        lock (this.gate)
        {
            return !this.cancelPending;
        }
    }

    /// <summary>
    /// Starts the process through <paramref name="start"/> (injected so tests can assert the calling
    /// thread) and registers it atomically so Cancel cannot observe a half-started run.
    /// </summary>
    public void StartAndRegister(Process process, Action<Process> start)
    {
        lock (this.gate)
        {
            start(process);
            this.activeProcess = process;
            this.cancelledProcess = null;
        }
    }

    public bool WasCancelled(Process process)
    {
        lock (this.gate)
        {
            return ReferenceEquals(this.cancelledProcess, process);
        }
    }

    /// <summary>
    /// Stops the process on the thread pool through <paramref name="stop"/> and records the cancellation
    /// once the stop has finished. Cancel awaits the result; the solution-close handler must not (that
    /// would block the UI thread on taskkill for up to 6 s) and relies on the publish gate's solution
    /// re-check to discard a result that arrives late.
    /// </summary>
    public Task<ProcessStopOutcome> StopAndMarkCancelledAsync(Process process, Func<Process, ProcessStopOutcome> stop)
    {
        return Task.Run(() =>
        {
            var outcome = stop(process);
            this.TryMarkCancelled(process, outcome);
            return outcome;
        });
    }

    /// <summary>Records a cancellation only when the stop actually terminated the still-active process.</summary>
    public bool TryMarkCancelled(Process process, ProcessStopOutcome outcome)
    {
        lock (this.gate)
        {
            if (ShouldRecordCancellation(outcome, ReferenceEquals(this.activeProcess, process)))
            {
                this.cancelledProcess = process;
                return true;
            }

            return false;
        }
    }

    /// <summary>Clears the run's registration and releases the command slot.</summary>
    public void Complete(Process? process)
    {
        lock (this.gate)
        {
            if (process != null)
            {
                if (ReferenceEquals(this.activeProcess, process))
                {
                    this.activeProcess = null;
                }

                if (ReferenceEquals(this.cancelledProcess, process))
                {
                    this.cancelledProcess = null;
                }
            }

            this.commandReserved = false;
        }
    }

    /// <summary>Removes and returns the live process (used by Dispose to stop it).</summary>
    public Process? DetachActive()
    {
        lock (this.gate)
        {
            var process = this.activeProcess;
            this.activeProcess = null;
            return process;
        }
    }

    internal static bool ShouldRecordCancellation(ProcessStopOutcome outcome, bool ownsActiveProcess) =>
        outcome == ProcessStopOutcome.Terminated && ownsActiveProcess;
}
