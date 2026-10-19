using System.IO;
using System.Text;
using System.Threading.Tasks;
using DataGuard.VisualStudio;
using FluentAssertions;
using Xunit;

namespace DataGuard.VisualStudio.Tests;

/// <summary>
/// Run-state reporting: the CLI's ground-truth signals (Summary counts, <c>GroundTruth</c> kind, the
/// "no ground truth" warning and the other policy lines) must reach the Output pane, the explanation
/// and the status bar, so a syntactic-only run is never reported as "[OK] No issues found.".
/// </summary>
public class RunStateReportingTests
{
    private const string SummaryLine = "{\"Kind\":\"Summary\",\"Phase\":\"Validation complete\",\"Data\":{\"ErrorCount\":2,\"WarningCount\":1}}";
    private const string RichSummaryLine = "{\"Kind\":\"Summary\",\"Phase\":\"Validation complete\",\"Data\":{\"ErrorCount\":2,\"WarningCount\":1,\"ViolationCount\":3,\"UnavailableRuleCount\":1,\"UnevaluatedContractCount\":3,\"AcquisitionDiagnosticCount\":0,\"GroundTruth\":\"entities\"}}";
    private const string GroundTruthWarningLine = "Warning: no ground truth (snapshot, connection, manual assembly or EF model) was loaded; only syntactic rules ran";
    private const string GroundTruthUnevaluatedLine = "UNEVALUATED: no ground truth (snapshot, connection, manual assembly or EF model) was loaded; only syntactic rules ran";
    private const string SpoofLine = "[DG1290] C:\\x\\Warning: no ground truth.csproj: SQL literal skipped";

    private static async Task<(ProgressReadResult Result, string Output)> ReadAsync(string stderr)
    {
        var written = new StringBuilder();
        var reader = new ProgressStreamReader(new RuleInventory(), text =>
        {
            lock (written)
            {
                written.Append(text);
            }
        });
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(stderr));
        using var streamReader = new StreamReader(stream);
        var result = await reader.ReadAsync(streamReader);
        return (result, written.ToString());
    }

    // ---- pins (behaviour that must survive) ----
    [Fact]
    public async Task Pin_SummaryWithCountsOnly_FormatsCountsAndNewFieldsStayAbsent()
    {
        var (result, output) = await ReadAsync("ide-safe: active\n" + SummaryLine + "\n");

        result.HasSummary.Should().BeTrue();
        result.ErrorCount.Should().Be(2);
        result.WarningCount.Should().Be(1);
        result.UnevaluatedContractCount.Should().BeNull();
        result.UnavailableRuleCount.Should().BeNull();
        result.AcquisitionDiagnosticCount.Should().BeNull();
        result.GroundTruth.Should().BeNull();
        result.GroundTruthMissing.Should().BeFalse();
        output.Should().Contain("✔ Validation complete: 2 errors, 1 warnings\r\n");
    }

    [Fact]
    public void Pin_ExplainValidateExitZeroWithSummary_IsOk_WithoutTheFlag()
    {
        ExitCodeExplainer.Explain("validate", CliRunOutcomes.Create(0, hasSummary: true), sarifExists: true).Should().Be("[OK] No issues found.");
    }

    [Fact]
    public void Pin_ExplainValidateExitOneWithoutSummary_IsCrash_EvenWithTheFlag()
    {
        ExitCodeExplainer.Explain("validate", CliRunOutcomes.Create(1), sarifExists: false).Should().Contain("failed before producing a validation summary");
        ExitCodeExplainer.Explain("validate", CliRunOutcomes.Create(1, groundTruthMissing: true), sarifExists: false).Should().Contain("failed before producing a validation summary");
    }

    [Fact]
    public void Pin_ExplainAssessExitFour_StaysToolErrors()
    {
        ExitCodeExplainer.Explain("assess", CliRunOutcomes.Create(4), sarifExists: true).Should().Contain("tool errors");
    }

    [Fact]
    public void Pin_RedactMasksConnectionMetadataAndPassword()
    {
        DataGuardLogger.Redact("Server=x;User Id=sa;Password=Hunter2;Encrypt=true").Should().Be("Server=[REDACTED];User Id=[REDACTED];Password=[REDACTED];Encrypt=true");
    }

    [Fact]
    public async Task Pin_PublishGateStillRequiresHandshake()
    {
        var (result, _) = await ReadAsync(GroundTruthWarningLine + "\n" + SummaryLine + "\n");

        result.IdeSafeAcknowledged.Should().BeFalse();
        PublishGate.Decide(result, 0).Should().Be(PublishVerdict.HandshakeMissing);
    }

    // ---- Summary counts and ground-truth kind ----
    [Fact]
    public async Task Summary_WithPolicyCountsAndGroundTruth_IsCapturedAndRendered()
    {
        var (result, output) = await ReadAsync("ide-safe: active\n" + RichSummaryLine + "\n");

        result.HasSummary.Should().BeTrue();
        result.UnevaluatedContractCount.Should().Be(3);
        result.UnavailableRuleCount.Should().Be(1);
        result.AcquisitionDiagnosticCount.Should().Be(0);
        result.GroundTruth.Should().Be("entities");
        output.Should().Contain("✔ Validation complete: 2 errors, 1 warnings; ground truth: entities; unevaluated 3; unavailable rules 1\r\n");
    }

    [Fact]
    public async Task Summary_ZeroPolicyCounts_RenderNoSegments()
    {
        var line = "{\"Kind\":\"Summary\",\"Phase\":\"Validation complete\",\"Data\":{\"ErrorCount\":0,\"WarningCount\":0,\"UnavailableRuleCount\":0,\"UnevaluatedContractCount\":0,\"GroundTruth\":\"schema+entities\"}}";

        var (result, output) = await ReadAsync("ide-safe: active\n" + line + "\n");

        result.UnevaluatedContractCount.Should().Be(0);
        result.GroundTruth.Should().Be("schema+entities");
        output.Should().Contain("✔ Validation complete: 0 errors, 0 warnings; ground truth: schema+entities\r\n");
    }

    [Fact]
    public void Summary_GroundTruthIsRedacted_AndNonStringIgnored()
    {
        ProgressLineParser.TryFormatProgress(
            "{\"Kind\":\"Summary\",\"Phase\":\"Validation complete\",\"Data\":{\"ErrorCount\":0,\"WarningCount\":0,\"GroundTruth\":7}}",
            out _, out _, out _, out _, out var details).Should().BeTrue();
        details.GroundTruth.Should().BeNull();
    }

    // ---- policy lines ----
    [Fact]
    public async Task GroundTruthWarningLine_SetsFlag_AndRendersAsWarn()
    {
        var (result, output) = await ReadAsync("ide-safe: active\n" + GroundTruthWarningLine + "\n" + SummaryLine + "\n");

        result.GroundTruthMissing.Should().BeTrue();
        output.Should().Contain("[DataGuard] [WARN] " + GroundTruthWarningLine + "\r\n");
        output.Should().NotContain("[DataGuard CLI] Warning:");
    }

    [Fact]
    public async Task GroundTruthUnevaluatedLine_SetsFlag()
    {
        var (result, _) = await ReadAsync("ide-safe: active\n" + GroundTruthUnevaluatedLine + "\n" + SummaryLine + "\n");

        result.GroundTruthMissing.Should().BeTrue();
    }

    [Theory]
    [InlineData("UNEVALUATED: 3 contract(s) could not be evaluated")]
    [InlineData("ACQUISITION: src/Snapshot.cs: EF model not used: no entities")]
    [InlineData("Rule DG012 not evaluated: provider mismatch")]
    [InlineData("Warning: unknown configuration keys: Foo")]
    public async Task PolicyLines_RenderAsWarn(string line)
    {
        var (result, output) = await ReadAsync("ide-safe: active\n" + line + "\n");

        output.Should().Contain("[DataGuard] [WARN] " + line + "\r\n");
        result.GroundTruthMissing.Should().BeFalse();
    }

    [Theory]
    [InlineData("  DG020 Foo: reason")]
    [InlineData("ide-safe: suppressed ConnectionString from config (database access disabled)")]
    [InlineData("baseline: 2 violations suppressed by .dataguard.baseline.json")]
    public async Task OtherRawLines_StayPlainCliEcho(string line)
    {
        var (_, output) = await ReadAsync("ide-safe: active\n" + line + "\n");

        output.Should().Contain("[DataGuard CLI] " + line + "\r\n");
    }

    [Fact]
    public async Task SpoofedFileNameContainingWarningText_DoesNotSetFlag()
    {
        var (result, output) = await ReadAsync("ide-safe: active\n" + SpoofLine + "\n" + SummaryLine + "\n");

        result.GroundTruthMissing.Should().BeFalse();
        output.Should().Contain("[DataGuard CLI] " + SpoofLine);
    }

    [Fact]
    public async Task PolicyLineWithCredential_IsRedacted()
    {
        var (_, output) = await ReadAsync("ide-safe: active\nUNEVALUATED: contract acquisition failed: Login failed for Password=Hunter2\n");

        output.Should().Contain("[DataGuard] [WARN] UNEVALUATED: contract acquisition failed: Login failed for Password=[REDACTED]");
        output.Should().NotContain("Hunter2");
    }

    [Fact]
    public void IsPolicyLine_MatchesOnlyLineStart()
    {
        ProgressLineParser.IsPolicyLine("Warning: x").Should().BeTrue();
        ProgressLineParser.IsPolicyLine("UNEVALUATED: x").Should().BeTrue();
        ProgressLineParser.IsPolicyLine("ACQUISITION: x").Should().BeTrue();
        ProgressLineParser.IsPolicyLine("Rule DG012 not evaluated: x").Should().BeTrue();
        ProgressLineParser.IsPolicyLine(" Warning: x").Should().BeFalse();
        ProgressLineParser.IsPolicyLine("[DG1290] Warning: x").Should().BeFalse();
        ProgressLineParser.IsPolicyLine("Rule not evaluated: x").Should().BeFalse();
        ProgressLineParser.IsPolicyLine(string.Empty).Should().BeFalse();
    }

    // ---- explanations ----
    [Fact]
    public void Explain_SyntacticOnlyExitZero_WarnsInsteadOfOk()
    {
        var text = ExitCodeExplainer.Explain("validate", CliRunOutcomes.Create(0, hasSummary: true, groundTruthMissing: true), sarifExists: true);

        text.Should().StartWith("[WARN] Ran syntactic rules only");
        text.Should().Contain("database contract was NOT compared");
        text.Should().Contain("Refresh Snapshot");
        text.Should().NotContain("[OK]");
    }

    [Fact]
    public void Explain_SyntacticOnlyExitOne_AppendsToFoundErrors()
    {
        var text = ExitCodeExplainer.Explain("validate", CliRunOutcomes.Create(1, hasSummary: true, groundTruthMissing: true), sarifExists: true);

        text.Should().StartWith("[WARN] Validation found errors. See Error List.");
        text.Should().EndWith("Ground truth was not loaded (syntactic only).");
    }

    [Fact]
    public void Explain_SyntacticOnlyWithWarnings_StillWarnsSyntacticOnly()
    {
        ExitCodeExplainer.Explain("validate", CliRunOutcomes.Create(0, hasSummary: true, warningCount: 2, groundTruthMissing: true), sarifExists: true)
            .Should().StartWith("[WARN] Ran syntactic rules only");
    }

    [Fact]
    public void Explain_SummaryWithoutSarif_WinsOverTheFlag()
    {
        ExitCodeExplainer.Explain("validate", CliRunOutcomes.Create(0, hasSummary: true, groundTruthMissing: true), sarifExists: false)
            .Should().Contain("failed to write results");
    }

    [Fact]
    public void Explain_UnevaluatedContracts_AppendsUnevaluatedText_NotSyntactic()
    {
        var text = ExitCodeExplainer.Explain("validate", CliRunOutcomes.Create(0, hasSummary: true, unevaluatedContractCount: 2), sarifExists: true);

        text.Should().NotContain("syntactic");
        text.Should().Contain("[WARN] 2 contract(s) could not be evaluated against the loaded ground truth (see UNEVALUATED lines).");

        ExitCodeExplainer.Explain("validate", CliRunOutcomes.Create(1, hasSummary: true, unevaluatedContractCount: 1), sarifExists: true)
            .Should().Contain("found errors").And.Contain("1 contract(s) could not be evaluated");
    }

    [Fact]
    public void Explain_ValidateExitFour_IsRefusedOutputPath()
    {
        ExitCodeExplainer.Explain("validate", CliRunOutcomes.Create(4), sarifExists: false)
            .Should().Be("[ERROR] The CLI refused to write results to the output path (see [DataGuard CLI] lines above).");
    }

    [Fact]
    public void Explain_TimeoutAndCancel_UnchangedByTheFlag()
    {
        ExitCodeExplainer.Explain("validate", CliRunOutcomes.Create(1, hasSummary: true, terminatedAtTimeout: true, groundTruthMissing: true), sarifExists: true)
            .Should().Be(ExitCodeExplainer.TerminatedAtTimeoutExplanation);
        ExitCodeExplainer.Explain("validate", CliRunOutcomes.Create(130, groundTruthMissing: true), sarifExists: false)
            .Should().Contain("[CANCELLED]");
    }

    // ---- status bar and result block ----
    [Fact]
    public void DescribeStatus_SyntacticOnly_OnlyWithSummaryAndFlag()
    {
        ExitCodeExplainer.DescribeStatus(new ProgressReadResult { HasSummary = true, GroundTruthMissing = true }).Should().Be("DataGuard: syntactic only (0 errors, 0 warnings)");
        ExitCodeExplainer.DescribeStatus(new ProgressReadResult { HasSummary = true, ErrorCount = 2, WarningCount = 1 }).Should().Be("DataGuard: 2 errors, 1 warnings");
        ExitCodeExplainer.DescribeStatus(new ProgressReadResult { HasSummary = false, GroundTruthMissing = true }).Should().Be("DataGuard: Result summary unavailable");
    }

    [Fact]
    public void DescribeGroundTruth_KindOrMissingOrNull()
    {
        ExitCodeExplainer.DescribeGroundTruth(new ProgressReadResult { HasSummary = true, GroundTruth = "schema" }).Should().Be("Ground truth: schema");
        ExitCodeExplainer.DescribeGroundTruth(new ProgressReadResult { HasSummary = true, GroundTruthMissing = true }).Should().Be("Ground truth: not loaded (syntactic rules only)");
        ExitCodeExplainer.DescribeGroundTruth(new ProgressReadResult { HasSummary = true, GroundTruth = "none", GroundTruthMissing = true }).Should().Be("Ground truth: not loaded (syntactic rules only)");
        ExitCodeExplainer.DescribeGroundTruth(new ProgressReadResult { HasSummary = true }).Should().BeNull();
    }

    // ---- redaction forms ----
    [Theory]
    [InlineData("Password=\"a b;c\";Server=x", "Password=[REDACTED];Server=[REDACTED]")]
    [InlineData("Password={p;w};Server=x", "Password=[REDACTED];Server=[REDACTED]")]
    [InlineData("Password='q r';Server=x", "Password=[REDACTED];Server=[REDACTED]")]
    [InlineData("postgresql://u:p@h/db", "[REDACTED]")]
    [InlineData("Data Source=scott/tiger@orcl", "Data Source=[REDACTED]")]
    [InlineData("Server=x;User Id=sa;Password=Hunter2", "Server=[REDACTED];User Id=[REDACTED];Password=[REDACTED]")]
    [InlineData("https://host/path", "[REDACTED]")]
    [InlineData("src/Repo.cs:12 duplicated", "src/Repo.cs:12 duplicated")]
    public void Redact_CoversQuotedBracedUriAndEzConnectForms(string input, string expected)
    {
        DataGuardLogger.Redact(input).Should().Be(expected);
    }
}
