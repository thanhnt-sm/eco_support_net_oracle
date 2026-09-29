using DataGuard.VisualStudio;
using FluentAssertions;
using Xunit;

namespace DataGuard.VisualStudio.Tests;

/// <summary>
/// Termination classification is pure: taskkill/Kill success is the only "Terminated"; a process that
/// exited on its own while we tried is "AlreadyExited" and its results must still be published.
/// </summary>
public class ProcessTerminatorClassificationTests
{
    [Theory]
    [InlineData(true, true, (int)ProcessStopOutcome.Terminated)]
    [InlineData(true, false, (int)ProcessStopOutcome.Terminated)]
    [InlineData(false, true, (int)ProcessStopOutcome.AlreadyExited)]
    [InlineData(false, false, (int)ProcessStopOutcome.Failed)]
    public void ClassifyAfterKillAttempt_OnlySuccessfulKillIsTerminated(bool killSucceeded, bool hasExited, int expected)
    {
        ProcessTerminator.ClassifyAfterKillAttempt(killSucceeded, hasExited).Should().Be((ProcessStopOutcome)expected);
    }

    [Theory]
    [InlineData((int)ProcessStopOutcome.AlreadyExited, true, false, true)]
    [InlineData((int)ProcessStopOutcome.AlreadyExited, true, true, true)]
    [InlineData((int)ProcessStopOutcome.Terminated, true, true, true)]
    [InlineData((int)ProcessStopOutcome.Terminated, true, false, false)]
    [InlineData((int)ProcessStopOutcome.Terminated, false, true, false)]
    [InlineData((int)ProcessStopOutcome.Failed, false, true, false)]
    [InlineData((int)ProcessStopOutcome.Failed, true, true, false)]
    public void ShouldPublishAfterTimeout_PublishesAlreadyExitedOrTerminatedWithSarif(int termination, bool hasExited, bool sarifExists, bool expected)
    {
        CliRunTimeoutHandler.ShouldPublishAfterTimeout((ProcessStopOutcome)termination, hasExited, sarifExists).Should().Be(expected);
    }

    /// <summary>taskkill /F leaves exit code 1, so the exit code takes no part in the decision or the explanation.</summary>
    [Fact]
    public void TerminatedWithSarif_PublishesAndExplainsTerminationInsteadOfExitCode()
    {
        const int taskkillExitCode = 1;

        CliRunTimeoutHandler.ShouldPublishAfterTimeout(ProcessStopOutcome.Terminated, hasExited: true, sarifExists: true).Should().BeTrue();
        ExitCodeExplainer.Explain("validate", CliRunOutcomes.Create(taskkillExitCode, terminatedAtTimeout: true), sarifExists: true)
            .Should().Be("[WARN] Terminated at the timeout after results were written; the exit code is not meaningful. See Error List.");
    }

    [Fact]
    public void DescribeTimeout_Characterization_NamesEachOutcome()
    {
        CliRunTimeoutHandler.DescribeTimeout("validate", 7, ProcessStopOutcome.Terminated)
            .Should().Be("[DataGuard] validate timed out after 7 seconds and its process tree was terminated.\r\n");
        CliRunTimeoutHandler.DescribeTimeout("assess", 9, ProcessStopOutcome.AlreadyExited)
            .Should().Be("[DataGuard] assess exceeded 9 seconds but completed before termination was requested.\r\n");
        CliRunTimeoutHandler.DescribeTimeout("validate", 7, ProcessStopOutcome.Failed)
            .Should().Contain("could not be terminated. Stop it manually.");
    }
}
