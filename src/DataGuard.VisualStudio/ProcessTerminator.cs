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
/// without retaining potentially sensitive output.
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

            var taskkillSucceeded = TryTaskKill(process.Id);
            if (taskkillSucceeded || process.HasExited)
            {
                return ProcessStopOutcome.Terminated;
            }

            // Fallback: force kill the process directly if taskkill failed or was unavailable.
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    try
                    {
                        process.WaitForExit(1000);
                    }
                    catch
                    {
                    }
                }

                return ProcessStopOutcome.Terminated;
            }
            catch
            {
                try
                {
                    return process.HasExited ? ProcessStopOutcome.Terminated : ProcessStopOutcome.Failed;
                }
                catch
                {
                    return ProcessStopOutcome.Failed;
                }
            }
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
