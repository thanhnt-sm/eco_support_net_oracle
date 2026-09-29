using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DataGuard.VisualStudio;
using FluentAssertions;
using Xunit;

namespace DataGuard.VisualStudio.Tests;

/// <summary>
/// Drives <see cref="CliRunSession"/> against cmd.exe fakes: timeout termination, the AlreadyExited
/// and terminated-after-SARIF publish paths, the ide-safe handshake, old-CLI detection and the
/// off-UI-thread process start. Windows only (cmd.exe, taskkill).
/// </summary>
public class CliRunSessionLiveTests : IDisposable
{
    private const string SummaryLine = "{\"Kind\":\"Summary\",\"Phase\":\"Validation complete\",\"Data\":{\"ErrorCount\":0,\"WarningCount\":0}}";
    private readonly string tempDir = Path.Combine(Path.GetTempPath(), "dg_live_" + Guid.NewGuid().ToString("N"));
    private readonly List<string> output = new();

    public CliRunSessionLiveTests()
    {
        Directory.CreateDirectory(this.tempDir);
    }

    public void Dispose()
    {
        TempDirectoryCleaner.SafeDeleteDirectory(this.tempDir);
    }

    [Fact]
    public async Task Timeout_WithoutSarif_TerminatesTreeAndDiscards()
    {
        var script = this.WriteScript(">&2 echo ide-safe: active", "ping -n 8 127.0.0.1 >nul", "exit /b 0");
        var session = this.CreateSession();
        using var process = CreateCmdProcess(script);

        var outcome = await session.RunAsync("validate", process, timeoutSeconds: 1, this.SarifPath);

        outcome.ProceedToPublish.Should().BeFalse();
        process.HasExited.Should().BeTrue("taskkill /T /F must end the cmd.exe tree");
        this.output.Should().Contain(line => line.Contains("timed out after 1 seconds and its process tree was terminated"));
    }

    [Fact]
    public async Task Timeout_WhenProcessExitsDuringKill_AlreadyExitedPathPublishes()
    {
        var script = this.WriteScript(
            ">&2 echo ide-safe: active",
            "> \"" + this.SarifPath + "\" echo {\"runs\":[]}",
            ">&2 echo " + SummaryLine,
            "ping -n 3 127.0.0.1 >nul",
            "exit /b 0");
        var session = this.CreateSession();

        // Simulates taskkill losing the race: by the time the kill is attempted the CLI has exited on its own.
        session.ProcessStopper = p =>
        {
            p.WaitForExit();
            return ProcessTerminator.ClassifyAfterKillAttempt(killSucceeded: false, hasExited: p.HasExited);
        };
        using var process = CreateCmdProcess(script);

        var outcome = await session.RunAsync("validate", process, timeoutSeconds: 1, this.SarifPath);

        outcome.ProceedToPublish.Should().BeTrue();
        outcome.ExitCode.Should().Be(0);
        outcome.Progress.IdeSafeAcknowledged.Should().BeTrue();
        outcome.Progress.HasSummary.Should().BeTrue();
        PublishGate.Decide(outcome.Progress, outcome.ExitCode).Should().Be(PublishVerdict.Publish);
        this.output.Should().Contain(line => line.Contains("completed before termination was requested"));
    }

    [Fact]
    public async Task Timeout_TerminatedAfterSarifWasWritten_StillPublishes()
    {
        var script = this.WriteScript(
            ">&2 echo ide-safe: active",
            "> \"" + this.SarifPath + "\" echo {\"runs\":[]}",
            ">&2 echo " + SummaryLine,
            "ping -n 8 127.0.0.1 >nul",
            "exit /b 0");
        var session = this.CreateSession();
        using var process = CreateCmdProcess(script);

        var outcome = await session.RunAsync("validate", process, timeoutSeconds: 1, this.SarifPath);

        outcome.ProceedToPublish.Should().BeTrue("the CLI had written its SARIF and reported a normal exit code (" + outcome.ExitCode + ") when terminated");
        ExitCodeExplainer.IsNormalCliExitCode(outcome.ExitCode).Should().BeTrue("taskkill leaves exit code " + outcome.ExitCode);
        outcome.Progress.HasSummary.Should().BeTrue();
        this.output.Should().Contain(line => line.Contains("process tree was terminated"));
    }

    [Fact]
    public async Task HandshakeMissing_ResultsAreDiscarded()
    {
        var script = this.WriteScript(">&2 echo " + SummaryLine, "exit /b 0");
        var session = this.CreateSession();
        using var process = CreateCmdProcess(script);

        var outcome = await session.RunAsync("validate", process, timeoutSeconds: 30, this.SarifPath);

        outcome.ProceedToPublish.Should().BeTrue("the session itself only reports; the package applies the verdict");
        outcome.Progress.IdeSafeAcknowledged.Should().BeFalse();
        outcome.Progress.HasSummary.Should().BeTrue();
        PublishGate.Decide(outcome.Progress, outcome.ExitCode).Should().Be(PublishVerdict.HandshakeMissing);
    }

    [Fact]
    public async Task OldCli_RejectionLineAndExitOne_IsCliTooOld()
    {
        var script = this.WriteScript(">&2 echo Unrecognized command or argument '--ide-safe'.", "exit /b 1");
        var session = this.CreateSession();
        using var process = CreateCmdProcess(script);

        var outcome = await session.RunAsync("validate", process, timeoutSeconds: 30, this.SarifPath);

        outcome.ExitCode.Should().Be(1);
        outcome.Progress.SawAnyProgressEvent.Should().BeFalse();
        PublishGate.Decide(outcome.Progress, outcome.ExitCode).Should().Be(PublishVerdict.CliTooOld);
        this.output.Should().Contain(line => line.Contains("Unrecognized command or argument '--ide-safe'"));
    }

    [Fact]
    public async Task ProcessStarter_RunsOffTheCallingSynchronizationContext()
    {
        var script = this.WriteScript(">&2 echo ide-safe: active", "exit /b 0");
        var session = this.CreateSession();
        var marker = new SynchronizationContext();
        SynchronizationContext? observed = marker;
        var observedThreadPool = false;
        session.ProcessStarter = p =>
        {
            observed = SynchronizationContext.Current;
            observedThreadPool = Thread.CurrentThread.IsThreadPoolThread;
            p.Start();
        };
        using var process = CreateCmdProcess(script);

        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(marker);
        try
        {
            await session.RunAsync("validate", process, timeoutSeconds: 30, this.SarifPath);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        observed.Should().NotBeSameAs(marker, "Process.Start must not run on the caller's (UI) context");
        observedThreadPool.Should().BeTrue();
    }

    private string SarifPath => Path.Combine(this.tempDir, "validation.sarif");

    private CliRunSession CreateSession()
    {
        var registry = new CliProcessRegistry();
        registry.TryReserve().Should().BeTrue();
        return new CliRunSession(
            registry,
            new RuleInventory(),
            text =>
            {
                lock (this.output)
                {
                    this.output.Add(text);
                }

                return Task.CompletedTask;
            },
            batch =>
            {
                lock (this.output)
                {
                    this.output.AddRange(batch);
                }

                return Task.CompletedTask;
            },
            _ => Task.CompletedTask);
    }

    private string WriteScript(params string[] lines)
    {
        var path = Path.Combine(this.tempDir, "fake-" + Guid.NewGuid().ToString("N") + ".cmd");
        File.WriteAllText(path, "@echo off\r\n" + string.Join("\r\n", lines) + "\r\n");
        return path;
    }

    private static Process CreateCmdProcess(string scriptPath)
    {
        var comSpec = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
        return CliArgumentBuilder.CreateProcess(comSpec, "/c " + CliArgumentBuilder.Quote(scriptPath), Path.GetDirectoryName(scriptPath)!);
    }
}
