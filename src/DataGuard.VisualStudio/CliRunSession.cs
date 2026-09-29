// <copyright file="CliRunSession.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

// VSTHRD003: the exit/drain tasks awaited here are started by this session inside the same
// JoinableTask as the caller (DataGuardPackage runs every command via JoinableTaskFactory.RunAsync).
// The stderr reader does switch to the main thread per line, which is deadlock-free only because of
// that shared JoinableTask context. Do not start these tasks outside a JoinableTask.
#pragma warning disable VSTHRD003

/// <summary>What one CLI run produced, as far as the package needs to know.</summary>
internal sealed class CliRunOutcome
{
    /// <summary>False when the run timed out or was cancelled; nothing should be published.</summary>
    public bool ProceedToPublish { get; set; }

    public bool Cancelled { get; set; }

    public int ExitCode { get; set; }

    public long ElapsedMs { get; set; }

    public ProgressReadResult Progress { get; set; } = new();
}

/// <summary>
/// Runs one already-configured CLI process: starts it under the registry, drains stdout, parses
/// stderr progress, enforces the timeout, and decides whether results may be published.
/// </summary>
internal sealed class CliRunSession
{
    private static readonly TimeSpan PostExitDrainGrace = TimeSpan.FromSeconds(3);

    private readonly CliProcessRegistry registry;
    private readonly RuleInventory inventory;
    private readonly Func<string, Task> writeOutput;
    private readonly Func<string, Task> setStatus;

    public CliRunSession(CliProcessRegistry registry, RuleInventory inventory, Func<string, Task> writeOutput, Func<string, Task> setStatus)
    {
        this.registry = registry;
        this.inventory = inventory;
        this.writeOutput = writeOutput;
        this.setStatus = setStatus;
    }

    public async Task<CliRunOutcome> RunAsync(string command, Process process, int timeoutSeconds)
    {
        var outcome = new CliRunOutcome();
        var stopwatch = Stopwatch.StartNew();
        this.registry.StartAndRegister(process);
        await this.setStatus(command == "validate" ? "DataGuard: Validating..." : "DataGuard: Assessing...");

        var stdoutDrainTask = ProcessTerminator.DrainAsync(process.StandardOutput);
        var stderrReadTask = new ProgressStreamReader(this.inventory, this.writeOutput).ReadAsync(process.StandardError);
        var exitTask = Task.Run(() => process.WaitForExit());
        var drains = Task.WhenAll(stdoutDrainTask, stderrReadTask);

        if (!await WaitForExitOrTimeoutAsync(exitTask, timeoutSeconds) && !exitTask.IsCompleted)
        {
            var termination = await CliRunTimeoutHandler.HandleAsync(process, exitTask, drains, command, timeoutSeconds, this.writeOutput, this.setStatus);
            if (termination != ProcessStopOutcome.AlreadyExited)
            {
                return outcome;
            }
        }

        await FinishDrainsAsync(process, drains);
        stopwatch.Stop();
        outcome.ElapsedMs = stopwatch.ElapsedMilliseconds;
        outcome.Progress = stderrReadTask.IsCompleted ? await stderrReadTask : new ProgressReadResult();

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
}
