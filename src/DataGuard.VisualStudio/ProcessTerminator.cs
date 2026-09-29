// <copyright file="ProcessTerminator.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

/// <summary>Outcome of a request to stop a CLI process tree.</summary>
internal enum ProcessStopOutcome
{
    Terminated,
    AlreadyExited,
    Failed,
}

/// <summary>
/// Stops CLI process trees (taskkill first, Process.Kill fallback) and drains redirected streams
/// without retaining potentially sensitive output. Only a kill that actually succeeded is reported as
/// <see cref="ProcessStopOutcome.Terminated"/>; a process that finished on its own in the meantime is
/// <see cref="ProcessStopOutcome.AlreadyExited"/> so its results are still published.
/// </summary>
internal static class ProcessTerminator
{
    internal static ProcessStopOutcome StopProcess(Process process)
    {
        try
        {
            if (process.HasExited)
            {
                return ProcessStopOutcome.AlreadyExited;
            }

            if (TryTaskKill(process.Id))
            {
                return ProcessStopOutcome.Terminated;
            }

            // taskkill failed or was unavailable: the process may simply have exited while it ran.
            if (process.HasExited)
            {
                return ProcessStopOutcome.AlreadyExited;
            }

            return KillDirectly(process);
        }
        catch (ObjectDisposedException)
        {
            return ProcessStopOutcome.AlreadyExited;
        }
        catch (InvalidOperationException)
        {
            return ProcessStopOutcome.AlreadyExited;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return ProcessStopOutcome.Failed;
        }
    }

    /// <summary>
    /// Pure classification after a kill attempt: a successful kill is Terminated; otherwise a process
    /// that has exited did so on its own (AlreadyExited); anything else is Failed.
    /// </summary>
    internal static ProcessStopOutcome ClassifyAfterKillAttempt(bool killSucceeded, bool hasExited)
    {
        if (killSucceeded)
        {
            return ProcessStopOutcome.Terminated;
        }

        return hasExited ? ProcessStopOutcome.AlreadyExited : ProcessStopOutcome.Failed;
    }

    private static ProcessStopOutcome KillDirectly(Process process)
    {
        var killSucceeded = false;
        try
        {
            process.Kill();
            killSucceeded = true;
            try
            {
                process.WaitForExit(1000);
            }
            catch (SystemException)
            {
                // Exit-wait failures do not change the classification: the kill itself succeeded.
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception || ex is NotSupportedException)
        {
            // Kill() throws InvalidOperationException when the process already exited; classified below.
        }

        bool hasExited;
        try
        {
            hasExited = process.HasExited;
        }
        catch (Exception ex) when (ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception)
        {
            hasExited = false;
        }

        return ClassifyAfterKillAttempt(killSucceeded, hasExited);
    }

    private static bool TryTaskKill(int processId)
    {
        try
        {
            using (var killer = Process.Start(new ProcessStartInfo
            {
                FileName = "taskkill",
                Arguments = "/pid " + processId + " /T /F",
                UseShellExecute = false,
                CreateNoWindow = true,
            }))
            {
                return killer != null && killer.WaitForExit(5000) && killer.ExitCode == 0;
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    /// <summary>Reads a redirected stream to completion, discarding its content.</summary>
    internal static async Task DrainAsync(StreamReader reader)
    {
        var buffer = new char[4096];
        try
        {
            while (await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false) > 0)
            {
                // Drain without retaining potentially sensitive CLI output.
            }
        }
        catch (Exception ex) when (ex is ObjectDisposedException || ex is IOException || ex is OperationCanceledException)
        {
            // Stream was closed or process cancelled/terminated; drain completes cleanly.
        }
    }

    /// <summary>Closes both redirected streams, ignoring failures.</summary>
    internal static void CloseStreams(Process process)
    {
        try
        {
            process.StandardOutput.Close();
            process.StandardError.Close();
        }
        catch
        {
        }
    }
}
