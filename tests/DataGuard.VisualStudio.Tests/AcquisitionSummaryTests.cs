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
        Assert.Equal("StackAccord: incomplete (0 errors, " + warningCount + " warnings; acquisition diagnostics 2; unevaluated contracts 3)", ExitCodeExplainer.DescribeStatus(outcome.Progress));
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

    [Theory]
    [InlineData(1, 0)]
    [InlineData(0, 2)]
    [InlineData(1, 2)]
    public void ReportedCoverageGaps_ExitZeroIsIncompleteRatherThanClean(int unavailable, int unevaluated)
    {
        var outcome = CreateOutcome();
        outcome.Progress.AcquisitionDiagnosticCount = 0;
        outcome.Progress.UnavailableRuleCount = unavailable;
        outcome.Progress.UnevaluatedContractCount = unevaluated;

        var explanation = ExitCodeExplainer.Explain("validate", outcome, sarifExists: true);

        Assert.StartsWith("[WARN] Validation incomplete.", explanation);
        Assert.DoesNotContain("[OK]", explanation);
        Assert.DoesNotContain("No issues found", explanation);
        if (unavailable > 0)
        {
            Assert.Contains("1 rule(s) unavailable", explanation);
            Assert.Contains("not evaluated", explanation);
        }

        if (unevaluated > 0)
        {
            Assert.Contains("2 contract(s) could not be evaluated", explanation);
            Assert.Contains("UNEVALUATED", explanation);
        }

        Assert.StartsWith("StackAccord: incomplete (0 errors, 0 warnings", ExitCodeExplainer.DescribeStatus(outcome.Progress));
    }

    [Theory]
    [InlineData(0, false, "[ERROR] The CLI reported a summary but failed to write results")]
    [InlineData(1, false, "[ERROR] The CLI reported a summary but failed to write results")]
    [InlineData(1, true, "[WARN] Validation found errors.")]
    [InlineData(2, true, "[ERROR] Invalid arguments or configuration.")]
    [InlineData(3, true, "[WARN] Validation incomplete:")]
    [InlineData(4, true, "[ERROR] The CLI refused to write results")]
    [InlineData(130, true, "[CANCELLED] Cancelled by user.")]
    public void CoverageGaps_PreserveExitPrecedence(int exitCode, bool sarifExists, string expected)
    {
        var outcome = CreateOutcome();
        outcome.ExitCode = exitCode;
        outcome.Progress.UnavailableRuleCount = 1;
        outcome.Progress.UnevaluatedContractCount = 2;

        Assert.StartsWith(expected, ExitCodeExplainer.Explain("validate", outcome, sarifExists));
    }

    [Fact]
    public void CoverageGaps_PreserveTimeoutMissingSummaryAndMissingGroundTruth()
    {
        var outcome = CreateOutcome();
        outcome.Progress.UnavailableRuleCount = 1;
        outcome.Progress.UnevaluatedContractCount = 2;
        outcome.TerminatedAtTimeout = true;
        Assert.Equal(ExitCodeExplainer.TerminatedAtTimeoutExplanation, ExitCodeExplainer.Explain("validate", outcome, sarifExists: true));

        outcome.TerminatedAtTimeout = false;
        outcome.ExitCode = 1;
        outcome.Progress.HasSummary = false;
        Assert.StartsWith("[ERROR] The CLI failed before producing a validation summary.", ExitCodeExplainer.Explain("validate", outcome, sarifExists: false));
        Assert.Equal("StackAccord: Result summary unavailable", ExitCodeExplainer.DescribeStatus(outcome.Progress));

        outcome.ExitCode = 0;
        outcome.Progress.HasSummary = true;
        outcome.Progress.GroundTruthMissing = true;
        Assert.StartsWith("[WARN] Ran syntactic rules only", ExitCodeExplainer.Explain("validate", outcome, sarifExists: true));
        Assert.Contains("1 rule(s) unavailable", ExitCodeExplainer.Explain("validate", outcome, sarifExists: true));
        Assert.StartsWith("StackAccord: syntactic only", ExitCodeExplainer.DescribeStatus(outcome.Progress));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void ZeroOrAbsentCoverageCounts_PreserveCleanResult(int? count)
    {
        var outcome = CreateOutcome();
        outcome.Progress.AcquisitionDiagnosticCount = count;
        outcome.Progress.UnavailableRuleCount = count;
        outcome.Progress.UnevaluatedContractCount = count;

        Assert.Equal("[OK] No issues found.", ExitCodeExplainer.Explain("validate", outcome, sarifExists: true));
        Assert.Equal("StackAccord: 0 errors, 0 warnings", ExitCodeExplainer.DescribeStatus(outcome.Progress));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public void UnfinalizedCounts_DoNotBecomeCoverageWarnings(int exitCode, bool sarifExists)
    {
        // Exercise the helper's defensive input contract. The production caller suppresses an
        // exit-zero explanation without a summary; this test does not certify that run as clean.
        var outcome = new CliRunOutcome
        {
            ExitCode = exitCode,
            Progress = new ProgressReadResult { HasSummary = false },
        };
        var withoutCounts = ExitCodeExplainer.Explain("validate", outcome, sarifExists);

        outcome.Progress.UnavailableRuleCount = 7;
        outcome.Progress.UnevaluatedContractCount = 11;
        outcome.Progress.AcquisitionDiagnosticCount = 13;

        Assert.Equal(withoutCounts, ExitCodeExplainer.Explain("validate", outcome, sarifExists));
        Assert.Null(ExitCodeExplainer.DescribeCoverage(outcome.Progress));
        Assert.Equal("StackAccord: Result summary unavailable", ExitCodeExplainer.DescribeStatus(outcome.Progress));
    }

    [Fact]
    public void DescribeCoverage_ReportsExplicitGapsWithoutInferringAllRulesWereEvaluated()
    {
        var progress = CreateOutcome().Progress;
        progress.UnavailableRuleCount = 1;
        progress.UnevaluatedContractCount = 3;

        Assert.Equal(
            "Coverage: incomplete; unavailable rules 1; unevaluated contracts 3; acquisition diagnostics 2. Review Rule ... not evaluated, UNEVALUATED and ACQUISITION lines above.",
            ExitCodeExplainer.DescribeCoverage(progress));

        progress.HasSummary = false;
        Assert.Null(ExitCodeExplainer.DescribeCoverage(progress));
        progress.HasSummary = true;
        progress.UnavailableRuleCount = 0;
        progress.UnevaluatedContractCount = 0;
        progress.AcquisitionDiagnosticCount = 0;
        Assert.Null(ExitCodeExplainer.DescribeCoverage(progress));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void ExitThree_ExplainsIncompleteWithoutClaimingPublication(bool hasSummary, bool sarifExists)
    {
        var outcome = CreateOutcome();
        outcome.ExitCode = 3;
        outcome.Progress.HasSummary = hasSummary;

        var explanation = ExitCodeExplainer.Explain("validate", outcome, sarifExists);

        Assert.StartsWith("[WARN] Validation incomplete:", explanation);
        Assert.DoesNotContain("preserved", explanation);
        Assert.DoesNotContain("were kept", explanation);
        Assert.DoesNotContain("published", explanation);
        Assert.DoesNotContain("loaded into Error List", explanation);
        Assert.DoesNotContain("replaced", explanation);
        Assert.Equal(3, outcome.ExitCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExitThree_PublicationGateStillRequiresHandshake(bool acknowledged)
    {
        var progress = new ProgressReadResult
        {
            HasSummary = true,
            IdeSafeAcknowledged = acknowledged,
            UnavailableRuleCount = 1,
        };

        Assert.Equal(acknowledged ? PublishVerdict.Publish : PublishVerdict.HandshakeMissing, PublishGate.Decide(progress, 3));
    }

    [Theory]
    [InlineData(null, null, null, null)]
    [InlineData(0, 0, 0, null)]
    [InlineData(1, null, null, "; unavailable rules 1")]
    [InlineData(null, 2, null, "; unevaluated contracts 2")]
    [InlineData(null, null, 3, "; acquisition diagnostics 3")]
    [InlineData(1, 2, 3, "; unavailable rules 1; unevaluated contracts 2; acquisition diagnostics 3")]
    public void DescribeCoverage_ReportsOnlyPositiveCounts(int? unavailable, int? unevaluated, int? acquisition, string? expectedGaps)
    {
        var progress = new ProgressReadResult
        {
            HasSummary = true,
            UnavailableRuleCount = unavailable,
            UnevaluatedContractCount = unevaluated,
            AcquisitionDiagnosticCount = acquisition,
        };

        Assert.Equal(
            expectedGaps == null ? null : "Coverage: incomplete" + expectedGaps + ". Review Rule ... not evaluated, UNEVALUATED and ACQUISITION lines above.",
            ExitCodeExplainer.DescribeCoverage(progress));
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
