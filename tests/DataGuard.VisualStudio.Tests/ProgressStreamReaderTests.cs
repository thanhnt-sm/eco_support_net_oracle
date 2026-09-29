using System.IO;
using System.Text;
using System.Threading.Tasks;
using DataGuard.VisualStudio;
using FluentAssertions;
using Xunit;

namespace DataGuard.VisualStudio.Tests;

/// <summary>
/// stderr parsing: progress events, the ide-safe handshake, old-CLI detection and the publish verdict.
/// </summary>
public class ProgressStreamReaderTests
{
    private const string PhaseStartedLine = "{\"Kind\":\"PhaseStarted\",\"Phase\":\"Discovering contracts\",\"Detail\":\"src\"}";
    private const string SummaryLine = "{\"Kind\":\"Summary\",\"Phase\":\"Validation complete\",\"Data\":{\"ErrorCount\":2,\"WarningCount\":1}}";
    private const string RejectionLine = "Unrecognized command or argument '--ide-safe'.";
    private const string SpoofLine = "[DG1290] C:\\x\\Unrecognized command or argument '--ide-safe'.csproj: SQL literal skipped";

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

    [Fact]
    public async Task Characterization_PhaseStartedAndSummary_AreFormattedAndCounted()
    {
        var (result, output) = await ReadAsync(PhaseStartedLine + "\n" + SummaryLine + "\n");

        result.HasSummary.Should().BeTrue();
        result.ErrorCount.Should().Be(2);
        result.WarningCount.Should().Be(1);
        result.IdeSafeUnsupported.Should().BeFalse();
        output.Should().Contain("▶ Discovering contracts — src");
        output.Should().Contain("✔ Validation complete: 2 errors, 1 warnings");
    }

    [Fact]
    public async Task Characterization_RejectionLineAtStart_FlagsIdeSafeUnsupported()
    {
        var (result, output) = await ReadAsync(RejectionLine + "\n");

        result.IdeSafeUnsupported.Should().BeTrue();
        result.HasSummary.Should().BeFalse();
        output.Should().Contain("[DataGuard CLI] " + RejectionLine);
    }

    [Fact]
    public async Task SpoofedFileNameContainingRejectionText_DoesNotFlagOldCli()
    {
        var (result, _) = await ReadAsync(PhaseStartedLine + "\n" + SpoofLine + "\n" + SummaryLine + "\n");

        result.IdeSafeUnsupported.Should().BeFalse();
        result.HasSummary.Should().BeTrue();
        result.SawAnyProgressEvent.Should().BeTrue();
    }

    [Fact]
    public async Task IdeSafeActiveAsFirstLine_SetsAcknowledged()
    {
        var (result, output) = await ReadAsync("\nide-safe: active\r\n" + SummaryLine + "\n");

        result.IdeSafeAcknowledged.Should().BeTrue();
        output.Should().Contain("[DataGuard CLI] ide-safe: active");
    }

    [Theory]
    [InlineData("ide-safe: suppressed ConnectionString\nide-safe: active\n")]
    [InlineData("ide-safe: active (v2)\n")]
    [InlineData(" ide-safe: active\n")]
    public async Task IdeSafeActiveNotExactFirstLine_IsNotAcknowledged(string stderr)
    {
        var (result, _) = await ReadAsync(stderr + SummaryLine + "\n");

        result.IdeSafeAcknowledged.Should().BeFalse();
    }

    [Fact]
    public async Task Verdict_WithoutAcknowledgement_IsHandshakeMissing_NotPublish()
    {
        var (result, _) = await ReadAsync(PhaseStartedLine + "\n" + SummaryLine + "\n");

        PublishGate.Decide(result, 0).Should().Be(PublishVerdict.HandshakeMissing);
        PublishGate.Decide(result, 1).Should().Be(PublishVerdict.HandshakeMissing);
    }

    [Fact]
    public async Task Verdict_OldCliStream_IsCliTooOld()
    {
        var (result, _) = await ReadAsync(RejectionLine + "\n");

        result.SawAnyProgressEvent.Should().BeFalse();
        PublishGate.Decide(result, 1).Should().Be(PublishVerdict.CliTooOld);
        PublishGate.Decide(result, 2).Should().Be(PublishVerdict.HandshakeMissing, "System.CommandLine exits 1, anything else is not the old-CLI signature");
    }

    [Fact]
    public async Task Verdict_RejectionLineButProgressEvents_IsHandshakeMissing()
    {
        var (result, _) = await ReadAsync(RejectionLine + "\n" + SummaryLine + "\n");

        result.IdeSafeUnsupported.Should().BeTrue();
        PublishGate.Decide(result, 1).Should().Be(PublishVerdict.HandshakeMissing);
    }

    [Fact]
    public async Task Verdict_Acknowledged_IsPublish()
    {
        var (result, _) = await ReadAsync("ide-safe: active\n" + SummaryLine + "\n");

        PublishGate.Decide(result, 1).Should().Be(PublishVerdict.Publish);
    }

    [Fact]
    public async Task BaselineApplied_RendersAsWarningLine_NotRedacted()
    {
        var line = "{\"Kind\":\"BaselineApplied\",\"Phase\":\"Validating rules\",\"Detail\":\".dataguard.baseline.json\",\"Data\":{\"SuppressedCount\":3}}";

        var (result, output) = await ReadAsync(line + "\n");

        output.Should().Contain("[WARN] baseline: 3");
        output.Should().Contain(".dataguard.baseline.json");
        output.Should().NotContain("[structured diagnostic redacted]");
        result.SawAnyProgressEvent.Should().BeTrue();
    }

    [Fact]
    public async Task Characterization_IdeSafeAndBaselineStderrLines_AreEchoedNeverDropped()
    {
        var (_, output) = await ReadAsync("ide-safe: active\nide-safe: suppressed ConnectionString from config (database access disabled)\nbaseline: 2 violations suppressed by .dataguard.baseline.json\n");

        output.Should().Contain("ide-safe: suppressed ConnectionString from config (database access disabled)");
        output.Should().Contain("baseline: 2 violations suppressed by .dataguard.baseline.json");
    }
}
