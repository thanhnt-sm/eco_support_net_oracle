// <copyright file="CliRunSession.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Threading;

// VSTHRD003: the exit/parse tasks awaited here are started by this session inside the same
// JoinableTask as the caller (DataGuardPackage runs every command via JoinableTaskFactory.RunAsync).
// The Output flush switches to the main thread once per batch, which is deadlock-free only because of
// that shared JoinableTask context. Do not start these tasks outside a JoinableTask.
#pragma warning disable VSTHRD003

/// <summary>
/// Runs one already-configured CLI process: starts it off the UI thread under the registry, drains
/// stdout, parses stderr on a background thread (Output text goes through a <see cref="ProgressPump"/>),
/// enforces the timeout, and decides whether results may be published.
/// </summary>
internal sealed class CliRunSession
{
    private static readonly TimeSpan PostExitDrainGrace = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan FlushGrace = TimeSpan.FromSeconds(5);

    private readonly CliProcessRegistry registry;
    private readonly RuleInventory inventory;
    private readonly Func<string, Task> writeOutput;
    private readonly Func<IReadOnlyList<string>, Task> flushOutput;
    private readonly Func<string, Task> setStatus;

    public CliRunSession(
        CliProcessRegistry registry,
        RuleInventory inventory,
        Func<string, Task> writeOutput,
        Func<IReadOnlyList<string>, Task> flushOutput,
        Func<string, Task> setStatus)
    {
        this.registry = registry;
        this.inventory = inventory;
        this.writeOutput = writeOutput;
        this.flushOutput = flushOutput;
        this.setStatus = setStatus;
    }

    /// <summary>Starts the process; injected so tests can assert it runs off the UI thread.</summary>
    public Action<Process> ProcessStarter { get; set; } = process => process.Start();

    /// <summary>Stops the process tree on timeout; injected so tests can drive the AlreadyExited path.</summary>
    public Func<Process, ProcessStopOutcome> ProcessStopper { get; set; } = ProcessTerminator.StopProcess;

    public async Task<CliRunOutcome> RunAsync(string command, Process process, int timeoutSeconds, string sarifPath)
    {
        var outcome = new CliRunOutcome();
        var stopwatch = Stopwatch.StartNew();

        // Always yield: Process.Start must never run on the caller's (UI) context, whatever thread called us.
        await TaskScheduler.Default.SwitchTo(alwaysYield: true);
        this.registry.StartAndRegister(process, this.ProcessStarter);
        await this.setStatus(command == "validate" ? "DataGuard: Validating..." : "DataGuard: Assessing...");

        var pump = new ProgressPump(this.flushOutput);
        var stdoutDrainTask = ProcessTerminator.DrainAsync(process.StandardOutput);
        var stderrReadTask = Task.Run(() => this.ParseStderrAsync(process, pump));
        var exitTask = Task.Run(() => process.WaitForExit());
        var drains = Task.WhenAll(stdoutDrainTask, stderrReadTask);

        if (!await WaitForExitOrTimeoutAsync(exitTask, timeoutSeconds) && !exitTask.IsCompleted)
        {
            var termination = await CliRunTimeoutHandler.HandleAsync(process, exitTask, drains, command, timeoutSeconds, this.ProcessStopper, this.writeOutput, this.setStatus);
            if (!CliRunTimeoutHandler.ShouldPublishAfterTimeout(termination, HasExited(process), File.Exists(sarifPath)))
            {
                await FlushPumpAsync(pump);
                return outcome;
            }

            outcome.TerminatedAtTimeout = termination == ProcessStopOutcome.Terminated;
        }

        await FinishDrainsAsync(process, drains);
        stopwatch.Stop();
        outcome.ElapsedMs = stopwatch.ElapsedMilliseconds;
        outcome.Progress = stderrReadTask.Status == TaskStatus.RanToCompletion ? await stderrReadTask : new ProgressReadResult();
        await FlushPumpAsync(pump);

        var wasCancelled = this.registry.WasCancelled(process);
        bool shouldSuppress;
        try
        {
            shouldSuppress = DecideCancellationSuppression(wasCancelled, stdoutDrainTask.IsCompleted && stderrReadTask.IsCompleted);
        }
        catch (InvalidOperationException)
        {
            await this.writeOutput("[DataGuard] Warning: Process output streams could not be completely drained upon cancellation.\r\n");
            shouldSuppress = wasCancelled;
        }

        if (shouldSuppress)
        {
            outcome.Cancelled = true;
            outcome.ExitCode = ExitCodeExplainer.CancelledExitCode;
            return outcome;
        }

        outcome.ExitCode = process.ExitCode;
        outcome.ProceedToPublish = true;
        return outcome;
    }

    internal static bool DecideCancellationSuppression(bool cancellationRequested, bool streamsDrained)
    {
        if (cancellationRequested && !streamsDrained)
        {
            throw new InvalidOperationException("Streams must be drained before suppressing publication.");
        }

        return cancellationRequested;
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (Exception ex) when (ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static async Task<bool> WaitForExitOrTimeoutAsync(Task exitTask, int timeoutSeconds)
    {
        using (var timeoutCts = new CancellationTokenSource())
        {
            var delayTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), timeoutCts.Token);
            var completed = await Task.WhenAny(exitTask, delayTask);
            timeoutCts.Cancel();
            return completed == exitTask;
        }
    }

    /// <summary>Waits for parse completion, bounded by process exit + 3 s; the UI flush is awaited separately.</summary>
    private static async Task FinishDrainsAsync(Process process, Task drains)
    {
        if (await Task.WhenAny(drains, Task.Delay(PostExitDrainGrace)) == drains)
        {
            return;
        }

        ProcessTerminator.CloseStreams(process);
        await Task.WhenAny(drains, Task.Delay(500));
        _ = drains.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

    private static async Task FlushPumpAsync(ProgressPump pump)
    {
        pump.Complete();
        if (await Task.WhenAny(pump.FlushCompletion, Task.Delay(FlushGrace)) != pump.FlushCompletion)
        {
            DataGuardLogger.LogWarning("Output pane flush did not complete within 5 seconds; continuing with the run outcome.");
        }
    }

    private async Task<ProgressReadResult> ParseStderrAsync(Process process, ProgressPump pump)
    {
        try
        {
            return await new ProgressStreamReader(this.inventory, pump.Enqueue).ReadAsync(process.StandardError).ConfigureAwait(false);
        }
        finally
        {
            pump.Complete();
        }
    }
}
