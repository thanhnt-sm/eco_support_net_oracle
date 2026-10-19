namespace DataGuard.Core.Tests;

using System.Text;
using DataGuard.VisualStudio;
using Xunit;

public sealed class SupportSummaryTests
{
    [Fact]
    public void PreviewIncludesOnlyValidatedMetadataAndNumericOutcome()
    {
        var summary = new SupportSummary("0.4.0", new string('a', 40), "18.10.2", 2, 123);
        summary.AddDiagnostic(1, "DG019", "warning", 7, 4);
        var preview = summary.Preview();
        Assert.Contains("extension=0.4.0", preview);
        Assert.Contains("host=18.10.2", preview);
        Assert.Contains("exit=2", preview);
        Assert.Contains("durationMs=123", preview);
        Assert.Contains("provider=unknown", preview);
        Assert.Contains("file-1 DG019 warning 7:4", preview);
    }

    [Theory]
    [InlineData("Password=secret\r\nforged=true")]
    [InlineData("/home/alice/project/secret.sql")]
    [InlineData("Bearer secret")]
    [InlineData("SELECT 'secret'")]
    [InlineData("0.4.0+secret")]
    public void RejectsUntrustedStringsAtCaptureAndExport(string injected)
    {
        var summary = new SupportSummary(injected, injected, injected);
        summary.AddDiagnostic(1, injected, injected, -1, -2);
        var preview = summary.Preview();
        Assert.DoesNotContain(injected, preview);
        Assert.Contains("extension=unknown", preview);
        Assert.Contains("file-1 unknown unknown 0:0", preview);
    }

    [Fact]
    public void DefaultNonPublishingOutcomeIsTimeoutWithUnknownDurationNotSuccess()
    {
        var summary = new SupportSummary(null, null, null);
        summary.CaptureOutcome(false, false, false, 0, 0, 124);
        Assert.Contains("outcome=timed-out", summary.Preview());
        Assert.Contains("exit=124", summary.Preview());
        Assert.Contains("durationMs=unknown", summary.Preview());
        summary.CaptureOutcome(true, false, false, 0, 12, 124);
        Assert.Contains("outcome=cancelled", summary.Preview());
        Assert.Contains("durationMs=12", summary.Preview());
    }

    [Theory]
    [InlineData("cancelled")]
    [InlineData("timed-out")]
    [InlineData("completed")]
    [InlineData("discarded")]
    public void CapturesOutcomeAndFreezesPreviouslyGeneratedPreview(string disposition)
    {
        var summary = new SupportSummary(null, null, null, 124, 42, disposition);
        var preview = summary.Preview();
        summary.MarkDiscarded();
        Assert.Contains("outcome=" + disposition, preview);
        Assert.Contains("outcome=discarded", summary.Preview());
        Assert.Contains("exit=124", preview);
    }

    [Fact]
    public void CapsDiagnosticsAndKeepsPreviewSnapshotStable()
    {
        var summary = new SupportSummary(null, null, null);
        for (var i = 0; i < 1000; i++)
        {
            summary.AddDiagnostic(i, "DG019", "error", int.MaxValue, int.MaxValue);
        }

        var preview = summary.Preview();
        Assert.Equal(200, preview.Split('\n').Count(line => line.StartsWith("file-", StringComparison.Ordinal)));
        Assert.Contains("diagnosticsTruncated=true", preview);
        Assert.True(Encoding.UTF8.GetByteCount(preview) <= 65536);
        Assert.Equal(preview, summary.Preview());
    }
}
