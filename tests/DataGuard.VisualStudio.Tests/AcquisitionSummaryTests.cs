using DataGuard.VisualStudio;
using Xunit;

namespace DataGuard.VisualStudio.Tests;

/// <summary>Shared with the cross-platform test project; exercises real IDE models and formatter.</summary>
public sealed class AcquisitionSummaryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void LoadedGroundTruthWithAcquisitionGaps_WarnsEvenWithoutRuleWarnings(int warningCount)
    {
        var outcome = CreateOutcome();
        outcome.Progress.WarningCount = warningCount;
        outcome.Progress.UnevaluatedContractCount = 3;

        var explanation = ExitCodeExplainer.Explain("validate", outcome, sarifExists: true);

        Assert.StartsWith("[WARN] Validation incomplete: 2 source acquisition diagnostic(s)", explanation);
        Assert.Contains("(see ACQUISITION lines).", explanation);
        Assert.Contains("3 contract(s) could not be evaluated", explanation);
        Assert.DoesNotContain("[OK]", explanation);
        Assert.DoesNotContain("No issues found", explanation);
        Assert.Equal("StackAccord: 0 errors, " + warningCount + " warnings; acquisition diagnostics 2", ExitCodeExplainer.DescribeStatus(outcome.Progress));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void AbsentOrZeroAcquisitionCount_PreservesCleanResult(int? count)
    {
        var outcome = CreateOutcome();
        outcome.Progress.AcquisitionDiagnosticCount = count;

        Assert.Equal("[OK] No issues found.", ExitCodeExplainer.Explain("validate", outcome, sarifExists: true));
        Assert.Equal("StackAccord: 0 errors, 0 warnings", ExitCodeExplainer.DescribeStatus(outcome.Progress));
    }

    [Fact]
    public void SyntacticOnlyWithAcquisitionGaps_PreservesGroundTruthNoticeAndShowsStatusCount()
    {
        var outcome = CreateOutcome();
        outcome.Progress.GroundTruthMissing = true;

        Assert.StartsWith("[WARN] Ran syntactic rules only", ExitCodeExplainer.Explain("validate", outcome, sarifExists: true));
        Assert.Equal("StackAccord: syntactic only (0 errors, 0 warnings; acquisition diagnostics 2)", ExitCodeExplainer.DescribeStatus(outcome.Progress));
    }

    [Theory]
    [InlineData(0, false, "[ERROR] The CLI reported a summary but failed to write results")]
    [InlineData(1, false, "[ERROR] The CLI reported a summary but failed to write results")]
    [InlineData(1, true, "[WARN] Validation found errors.")]
    [InlineData(2, true, "[ERROR] Invalid arguments or configuration.")]
    [InlineData(3, true, "[WARN] Validation incomplete:")]
    [InlineData(4, true, "[ERROR] The CLI refused to write results")]
    [InlineData(130, true, "[CANCELLED] Cancelled by user.")]
    public void AcquisitionGaps_DoNotOverrideExistingExitPrecedence(int exitCode, bool sarifExists, string expected)
    {
        var outcome = CreateOutcome();
        outcome.ExitCode = exitCode;

        Assert.StartsWith(expected, ExitCodeExplainer.Explain("validate", outcome, sarifExists));
    }

    [Fact]
    public void TimeoutAndMissingSummary_PreserveExistingPrecedence()
    {
        var outcome = CreateOutcome();
        outcome.TerminatedAtTimeout = true;
        Assert.Equal(ExitCodeExplainer.TerminatedAtTimeoutExplanation, ExitCodeExplainer.Explain("validate", outcome, sarifExists: true));

        outcome.TerminatedAtTimeout = false;
        outcome.ExitCode = 1;
        outcome.Progress.HasSummary = false;
        Assert.StartsWith("[ERROR] The CLI failed before producing a validation summary.", ExitCodeExplainer.Explain("validate", outcome, sarifExists: false));
        Assert.Equal("StackAccord: Result summary unavailable", ExitCodeExplainer.DescribeStatus(outcome.Progress));
    }

    private static CliRunOutcome CreateOutcome() => new()
    {
        ExitCode = 0,
        Progress = new ProgressReadResult
        {
            HasSummary = true,
            AcquisitionDiagnosticCount = 2,
            GroundTruth = "schema+entities",
        },
    };
}
