using System.IO;
using DataGuard.VisualStudio;
using FluentAssertions;
using Xunit;

namespace DataGuard.VisualStudio.Tests;

public class DataGuardPackageTests
{
    [Fact]
    public void FormatProgressLine_UnknownValidJson_DoesNotEchoContent()
    {
        var json = "{\"Unexpected\":\"Secret123\"}";
        var output = DataGuardPackage.FormatProgressLine(json, false);

        output.FormattedOutput.Should().NotContain("Secret123");
        output.FormattedOutput.Should().NotContain("Unexpected");
        output.FormattedOutput.Should().Contain("[structured diagnostic redacted]");
    }

    [Theory]
    [InlineData("\"string payload\"")]
    [InlineData("12345")]
    [InlineData("[1, 2, 3]")]
    [InlineData("true")]
    [InlineData("null")]
    public void FormatProgressLine_NonObjectJson_DoesNotThrowAndRedacts(string nonObjectJson)
    {
        var output = DataGuardPackage.FormatProgressLine(nonObjectJson, false);

        output.FormattedOutput.Should().NotBeNull();
        output.FormattedOutput.Should().Contain("[structured diagnostic redacted]");
    }

    [Fact]
    public void FormatProgressLine_DiscardedLine_OutputsDiscardMessage()
    {
        var output = DataGuardPackage.FormatProgressLine("some giant text", true);

        output.FormattedOutput.Should().Contain("exceeded the safe display limit and was discarded");
    }

    [Fact]
    public void TryFormatProgress_IncompleteSummary_ReturnsFalse()
    {
        var json = "{\"Kind\":\"Summary\", \"Phase\":\"Completed\", \"Data\":{}}";

        var success = DataGuardPackage.TryFormatProgress(json, out var formatted, out var errors, out var warnings);

        success.Should().BeFalse();
    }

    [Fact]
    public void TryFormatProgress_AssessmentCompleteSummary_IncludesCriticalCountInErrors()
    {
        var json = "{\"Kind\":\"Summary\", \"Phase\":\"Assessment complete\", \"Data\":{\"ErrorCount\":2, \"WarningCount\":1, \"CriticalCount\":5}}";

        var success = DataGuardPackage.TryFormatProgress(json, out var formatted, out var errors, out var warnings);

        success.Should().BeTrue();
        errors.Should().Be(7);
        formatted.Should().Contain("7 errors");
    }

    [Fact]
    public void DecideCancellationSuppression_WhenCancelledAndDrained_ReturnsTrue()
    {
        var suppress = DataGuardPackage.DecideCancellationSuppression(cancellationRequested: true, streamsDrained: true);
        suppress.Should().BeTrue();
    }

    [Fact]
    public void DecideCancellationSuppression_WhenCancelledButNotDrained_Throws()
    {
        System.Action act = () => DataGuardPackage.DecideCancellationSuppression(cancellationRequested: true, streamsDrained: false);
        act.Should().Throw<System.InvalidOperationException>().WithMessage("*drained*");
    }

    [Fact]
    public void ShouldRecordCancellation_RequiresLiveOwnedTermination()
    {
        DataGuardPackage.ShouldRecordCancellation(DataGuardPackage.ProcessStopOutcome.Terminated, true).Should().BeTrue();
        DataGuardPackage.ShouldRecordCancellation(DataGuardPackage.ProcessStopOutcome.Terminated, false).Should().BeFalse();
        DataGuardPackage.ShouldRecordCancellation(DataGuardPackage.ProcessStopOutcome.AlreadyExited, true).Should().BeFalse();
        DataGuardPackage.ShouldRecordCancellation(DataGuardPackage.ProcessStopOutcome.Failed, true).Should().BeFalse();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public void ShouldForceReleaseFailedTerminationReservation_WhenExitOrDrainsRemainIncomplete_ReturnsTrue(bool exitCompleted, bool drainsCompleted)
    {
        DataGuardPackage.ShouldForceReleaseFailedTerminationReservation(exitCompleted, drainsCompleted).Should().BeTrue();
    }

    [Fact]
    public void ShouldForceReleaseFailedTerminationReservation_WhenExitAndDrainsComplete_ReturnsFalse()
    {
        DataGuardPackage.ShouldForceReleaseFailedTerminationReservation(exitCompleted: true, drainsCompleted: true).Should().BeFalse();
    }

    [Fact]
    public void AppendProgressChar_WhenExceeds16KB_SetsDiscardedLine()
    {
        var sb = new System.Text.StringBuilder();
        bool discarded = false;

        for (int i = 0; i < 16 * 1024; i++)
        {
            DataGuardPackage.AppendProgressChar('a', sb, ref discarded);
        }
        discarded.Should().BeFalse();

        DataGuardPackage.AppendProgressChar('b', sb, ref discarded);
        discarded.Should().BeTrue();
    }

    [Fact]
    public void Quote_WithTrailingBackslash_DoublesBackslash()
    {
        var input = @"D:\path\to\solution\";
        var quoted = DataGuardPackage.Quote(input);
        quoted.Should().Be("\"D:\\path\\to\\solution\\\\\"");

        var inputWithoutTrailing = @"D:\path\to\solution";
        DataGuardPackage.Quote(inputWithoutTrailing).Should().Be("\"D:\\path\\to\\solution\"");
    }

    [Fact]
    public void ResolveSarifArtifactUri_RelativeUriWithSrcRoot_ReturnsAbsolutePath()
    {
        var solutionDir = @"D:\repo";
        var resolved = DataGuardPackage.ResolveSarifArtifactUri("src/Model.cs", "%SRCROOT%", solutionDir);
        resolved.Should().Be(Path.GetFullPath(Path.Combine(solutionDir, "src", "Model.cs")));
    }

    [Fact]
    public void ResolveSarifArtifactUri_WithoutSrcRoot_ReturnsNull()
    {
        var solutionDir = @"D:\repo";
        var resolved = DataGuardPackage.ResolveSarifArtifactUri("src/Model.cs", null, solutionDir);
        resolved.Should().BeNull();
    }

    [Fact]
    public void ResolveSarifArtifactUri_RootedUri_ReturnsOriginal()
    {
        var rooted = @"C:\abs\file.cs";
        var resolved = DataGuardPackage.ResolveSarifArtifactUri(rooted, null, @"D:\repo");
        resolved.Should().Be(rooted);
    }

    [Fact]
    public void FindCliExecutable_WhenBundledCliExists_PrioritizesBundledOverPath()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "dg_vsix_test_" + System.Guid.NewGuid().ToString("N"));
        var cliDir = Path.Combine(tempDir, "cli");
        Directory.CreateDirectory(cliDir);
        var fakeCli = Path.Combine(cliDir, "dataguard.exe");
        File.WriteAllText(fakeCli, "dummy");
        try
        {
            var resolved = DataGuardLogger.FindCliExecutable(null, tempDir);
            resolved.Should().Be(fakeCli);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void FindCliExecutable_WhenCustomCliPathIsInvalid_ReturnsEmptyString()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "dg_vsix_test_" + System.Guid.NewGuid().ToString("N"));
        var cliDir = Path.Combine(tempDir, "cli");
        Directory.CreateDirectory(cliDir);
        var fakeCli = Path.Combine(cliDir, "dataguard.exe");
        File.WriteAllText(fakeCli, "dummy");
        try
        {
            var resolved = DataGuardLogger.FindCliExecutable(@"C:\nonexistent\path\dataguard.exe", tempDir);
            resolved.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
