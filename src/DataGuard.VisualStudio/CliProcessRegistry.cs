// <copyright file="CliProcessRegistry.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

using System.Diagnostics;

/// <summary>
/// Single-run guard for CLI processes: at most one command runs per package instance, cancellation
/// is recorded against the exact process it stopped, and Dispose can detach the live process.
/// </summary>
internal sealed class CliProcessRegistry
{
    private readonly object gate = new();
    private Process? activeProcess;
    private Process? cancelledProcess;
    private bool commandReserved;

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

    /// <summary>Starts the process and registers it atomically so Cancel cannot observe a half-started run.</summary>
    public void StartAndRegister(Process process)
    {
        lock (this.gate)
        {
            process.Start();
            this.activeProcess = process;
            this.cancelledProcess = null;
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

    public bool WasCancelled(Process process)
    {
        lock (this.gate)
        {
            return ReferenceEquals(this.cancelledProcess, process);
        }
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
