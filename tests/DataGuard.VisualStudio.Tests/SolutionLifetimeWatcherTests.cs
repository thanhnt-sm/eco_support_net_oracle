using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using DataGuard.VisualStudio;
using FluentAssertions;
using Xunit;

namespace DataGuard.VisualStudio.Tests;

/// <summary>
/// A run's results belong to the solution that was open when it started; if the solution changed
/// (or closed) while the CLI ran, the results are discarded with an Output line and never published.
/// Stopping the run on solution close must not block the caller (the UI thread) on taskkill.
/// </summary>
public class SolutionLifetimeWatcherTests
{
    [Fact]
    public async Task RequestStopAsync_ReturnsBeforeTheStopCompletes_ThenRecordsCancellation()
    {
        var registry = new CliProcessRegistry();
        registry.TryReserve().Should().BeTrue();
        using var process = new Process();
        registry.StartAndRegister(process, _ => { });
        using var stopMayFinish = new ManualResetEventSlim(false);
        var stopper = new Func<Process, ProcessStopOutcome>(_ =>
        {
            stopMayFinish.Wait(TimeSpan.FromSeconds(5));
            return ProcessStopOutcome.Terminated;
        });

        var stopTask = SolutionLifetimeWatcher.RequestStopAsync(process, stopper, registry.TryMarkCancelled);

        stopTask.IsCompleted.Should().BeFalse("the caller must not wait for taskkill; the publish gate discards late results");
        registry.WasCancelled(process).Should().BeFalse();

        stopMayFinish.Set();
        (await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(10)))).Should().BeSameAs(stopTask);
        (await stopTask).Should().Be(ProcessStopOutcome.Terminated);
        registry.WasCancelled(process).Should().BeTrue();
    }

    [Fact]
    public async Task RequestStopAsync_AlreadyExited_DoesNotRecordCancellation()
    {
        var registry = new CliProcessRegistry();
        registry.TryReserve().Should().BeTrue();
        using var process = new Process();
        registry.StartAndRegister(process, _ => { });

        var outcome = await SolutionLifetimeWatcher.RequestStopAsync(process, _ => ProcessStopOutcome.AlreadyExited, registry.TryMarkCancelled);

        outcome.Should().Be(ProcessStopOutcome.AlreadyExited);
        registry.WasCancelled(process).Should().BeFalse("results of a run that finished on its own are still published");
    }

    [Theory]
    [InlineData(@"D:\repo\app", @"D:\repo\app")]
    [InlineData(@"D:\repo\app", @"d:\REPO\App\")]
    [InlineData(@"D:\repo\app\", @"D:\repo\app")]
    public void DescribeSolutionMismatch_SameDirectory_ReturnsNull(string captured, string current)
    {
        SolutionLifetimeWatcher.DescribeSolutionMismatch("validate", captured, current).Should().BeNull();
    }

    [Theory]
    [InlineData(@"D:\repo\app", @"D:\repo\other")]
    [InlineData(@"D:\repo\app", "")]
    [InlineData(@"D:\repo\app", null)]
    public void DescribeSolutionMismatch_DifferentOrClosed_ReturnsDiscardMessage(string captured, string? current)
    {
        var message = SolutionLifetimeWatcher.DescribeSolutionMismatch("validate", captured, current);

        message.Should().NotBeNull();
        message.Should().Contain("validate");
        message.Should().Contain("discarded");
        message.Should().EndWith("\r\n");
    }
}
