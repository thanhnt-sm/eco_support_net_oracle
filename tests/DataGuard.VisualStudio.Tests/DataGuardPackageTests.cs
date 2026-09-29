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
        var output = ProgressLineParser.FormatProgressLine(json, false);

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
        var output = ProgressLineParser.FormatProgressLine(nonObjectJson, false);

        output.FormattedOutput.Should().NotBeNull();
        output.FormattedOutput.Should().Contain("[structured diagnostic redacted]");
    }

    [Fact]
    public void FormatProgressLine_DiscardedLine_OutputsDiscardMessage()
    {
        var output = ProgressLineParser.FormatProgressLine("some giant text", true);

        output.FormattedOutput.Should().Contain("exceeded the safe display limit and was discarded");
    }

    [Fact]
    public void TryFormatProgress_IncompleteSummary_ReturnsFalse()
    {
        var json = "{\"Kind\":\"Summary\", \"Phase\":\"Completed\", \"Data\":{}}";

        var success = ProgressLineParser.TryFormatProgress(json, out var formatted, out var errors, out var warnings);

        success.Should().BeFalse();
    }

    [Fact]
    public void TryFormatProgress_AssessmentCompleteSummary_IncludesCriticalCountInErrors()
    {
        var json = "{\"Kind\":\"Summary\", \"Phase\":\"Assessment complete\", \"Data\":{\"ErrorCount\":2, \"WarningCount\":1, \"CriticalCount\":5}}";

        var success = ProgressLineParser.TryFormatProgress(json, out var formatted, out var errors, out var warnings);

        success.Should().BeTrue();
        errors.Should().Be(7);
        formatted.Should().Contain("7 errors");
    }

    [Fact]
    public void DecideCancellationSuppression_WhenCancelledAndDrained_ReturnsTrue()
    {
        var suppress = CliRunSession.DecideCancellationSuppression(cancellationRequested: true, streamsDrained: true);
        suppress.Should().BeTrue();
    }

    [Fact]
    public void DecideCancellationSuppression_WhenCancelledButNotDrained_Throws()
    {
        System.Action act = () => CliRunSession.DecideCancellationSuppression(cancellationRequested: true, streamsDrained: false);
        act.Should().Throw<System.InvalidOperationException>().WithMessage("*drained*");
    }

    [Fact]
    public void ShouldRecordCancellation_RequiresLiveOwnedTermination()
    {
        CliProcessRegistry.ShouldRecordCancellation(ProcessStopOutcome.Terminated, true).Should().BeTrue();
        CliProcessRegistry.ShouldRecordCancellation(ProcessStopOutcome.Terminated, false).Should().BeFalse();
        CliProcessRegistry.ShouldRecordCancellation(ProcessStopOutcome.AlreadyExited, true).Should().BeFalse();
        CliProcessRegistry.ShouldRecordCancellation(ProcessStopOutcome.Failed, true).Should().BeFalse();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public void ShouldForceReleaseFailedTerminationReservation_WhenExitOrDrainsRemainIncomplete_ReturnsTrue(bool exitCompleted, bool drainsCompleted)
    {
        CliRunTimeoutHandler.ShouldForceReleaseFailedTerminationReservation(exitCompleted, drainsCompleted).Should().BeTrue();
    }

    [Fact]
    public void ShouldForceReleaseFailedTerminationReservation_WhenExitAndDrainsComplete_ReturnsFalse()
    {
        CliRunTimeoutHandler.ShouldForceReleaseFailedTerminationReservation(exitCompleted: true, drainsCompleted: true).Should().BeFalse();
    }

    [Fact]
    public void AppendProgressChar_WhenExceeds16KB_SetsDiscardedLine()
    {
        var sb = new System.Text.StringBuilder();
        bool discarded = false;

        for (int i = 0; i < 16 * 1024; i++)
        {
            ProgressLineParser.AppendProgressChar('a', sb, ref discarded);
        }
        discarded.Should().BeFalse();

        ProgressLineParser.AppendProgressChar('b', sb, ref discarded);
        discarded.Should().BeTrue();
    }

    [Fact]
    public void Quote_WithTrailingBackslash_DoublesBackslash()
    {
        var input = @"D:\path\to\solution\";
        var quoted = CliArgumentBuilder.Quote(input);
        quoted.Should().Be("\"D:\\path\\to\\solution\\\\\"");

        var inputWithoutTrailing = @"D:\path\to\solution";
        CliArgumentBuilder.Quote(inputWithoutTrailing).Should().Be("\"D:\\path\\to\\solution\"");
    }

    [Fact]
    public void Quote_WithNullOrEmpty_ReturnsEmptyQuotes()
    {
        CliArgumentBuilder.Quote(null!).Should().Be("\"\"");
        CliArgumentBuilder.Quote(string.Empty).Should().Be("\"\"");
    }

    [Fact]
    public void Quote_WithBackslashPrecedingDoubleQuote_EscapesProperly()
    {
        var input = @"dir\""test";
        var quoted = CliArgumentBuilder.Quote(input);
        quoted.Should().Be("\"dir\\\\\\\"test\"");
    }
    [Fact]
    public void ResolveSarifArtifactUri_RelativeUriWithSrcRoot_ReturnsAbsolutePath()
    {
        var solutionDir = @"D:\repo";
        var resolved = SarifErrorListPublisher.ResolveSarifArtifactUri("src/Model.cs", "%SRCROOT%", solutionDir);
        resolved.Should().Be(Path.GetFullPath(Path.Combine(solutionDir, "src", "Model.cs")));
    }

    [Fact]
    public void ResolveSarifArtifactUri_WithoutSrcRoot_ReturnsNull()
    {
        var solutionDir = @"D:\repo";
        var resolved = SarifErrorListPublisher.ResolveSarifArtifactUri("src/Model.cs", null, solutionDir);
        resolved.Should().BeNull();
    }

    [Fact]
    public void ResolveSarifArtifactUri_RootedUriOutsideSolution_ReturnsNull()
    {
        var rootedOutside = @"C:\Windows\System32\cmd.exe";
        var resolved = SarifErrorListPublisher.ResolveSarifArtifactUri(rootedOutside, null, @"D:\repo");
        resolved.Should().BeNull();
    }

    [Fact]
    public void ResolveSarifArtifactUri_RootedUriInsideSolution_ReturnsCanonicalPath()
    {
        var solutionDir = @"D:\repo";
        var rootedInside = @"D:\repo\src\File.cs";
        var resolved = SarifErrorListPublisher.ResolveSarifArtifactUri(rootedInside, null, solutionDir);
        resolved.Should().Be(Path.GetFullPath(rootedInside));
    }

    [Fact]
    public void ResolveSarifArtifactUri_PathTraversalWithSrcRoot_ReturnsNull()
    {
        var solutionDir = @"D:\repo";
        var resolved = SarifErrorListPublisher.ResolveSarifArtifactUri(@"../../etc/passwd", "%SRCROOT%", solutionDir);
        resolved.Should().BeNull();
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
            var resolved = CliLocator.FindCliExecutable(null, tempDir);
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
            var resolved = CliLocator.FindCliExecutable(@"C:\nonexistent\path\dataguard.exe", tempDir);
            resolved.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void FindCliExecutable_WhenCustomCliPathIsRelative_NeverResolvesAgainstSolutionDirectory()
    {
        // A repository could ship tools\dataguard.exe; a relative user setting must not pick it up.
        var tempDir = Path.Combine(Path.GetTempPath(), "dg_vsix_rel_test_" + System.Guid.NewGuid().ToString("N"));
        var toolsDir = Path.Combine(tempDir, "tools");
        Directory.CreateDirectory(toolsDir);
        var fakeCli = Path.Combine(toolsDir, "dataguard.exe");
        File.WriteAllText(fakeCli, "dummy");
        try
        {
            var resolved = CliLocator.FindCliExecutable(@".\tools\dataguard.exe", null, solutionDirectory: tempDir);
            resolved.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void SafeDeleteDirectory_WithReadOnlyDirectoryAndFiles_DeletesSuccessfullyWithoutThrowing()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "dg_safe_del_test_" + System.Guid.NewGuid().ToString("N"));
        var subDir = Path.Combine(tempDir, "sub");
        Directory.CreateDirectory(subDir);
        var testFile = Path.Combine(subDir, "locked.txt");
        File.WriteAllText(testFile, "content");
        File.SetAttributes(testFile, FileAttributes.ReadOnly);
        new DirectoryInfo(subDir).Attributes |= FileAttributes.ReadOnly;

        TempDirectoryCleaner.SafeDeleteDirectory(tempDir);

        Directory.Exists(tempDir).Should().BeFalse();
    }

    [Fact]
    public void SafeDeleteDirectory_WhenDirectoryDoesNotExist_DoesNotThrow()
    {
        var nonExistent = Path.Combine(Path.GetTempPath(), "dg_nonexistent_" + System.Guid.NewGuid().ToString("N"));
        System.Action act = () => TempDirectoryCleaner.SafeDeleteDirectory(nonExistent);
        act.Should().NotThrow();
    }

    [Fact]
    public void SafeDeleteDirectory_WithHiddenAndReadOnlyAttributes_CleansUpSuccessfully()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "dg_safe_del_hidden_" + System.Guid.NewGuid().ToString("N"));
        var subDir = Path.Combine(tempDir, "sub_hidden");
        Directory.CreateDirectory(subDir);
        var testFile = Path.Combine(subDir, "hidden.txt");
        File.WriteAllText(testFile, "hidden-content");
        File.SetAttributes(testFile, FileAttributes.Hidden | FileAttributes.ReadOnly);
        var dirInfo = new DirectoryInfo(subDir);
        dirInfo.Attributes |= FileAttributes.Hidden | FileAttributes.ReadOnly;

        TempDirectoryCleaner.SafeDeleteDirectory(tempDir);

        Directory.Exists(tempDir).Should().BeFalse();
    }

    [Fact]
    public void FindCliExecutable_WhenExtensionDirectoryIsNull_DoesNotThrowAndReturnsString()
    {
        // Tests the production path where extensionDirectory is null, ensuring Assembly.Location/CodeBase resolution does not throw ArgumentException on .NET Framework 4.7.2
        System.Action act = () =>
        {
            var resolved = CliLocator.FindCliExecutable(null, extensionDirectory: null);
            resolved.Should().NotBeNull();
        };
        act.Should().NotThrow();
    }

    [Fact]
    public void Quote_WithEmbeddedQuotesAndSpaces_SanitizesAndEnclosesCorrectly()
    {
        var path = @"C:\Program Files\DataGuard\test\""malicious\"".log";
        var sanitized = path.Replace("\"", string.Empty);
        var quoted = CliArgumentBuilder.Quote(sanitized);
        quoted.Should().StartWith("\"");
        quoted.Should().EndWith("\"");
        quoted.Should().NotContain("\"malicious\"");
    }

    [Fact]
    public void Configure_WithUncPath_RejectsRemoteShareAndDoesNotUseAsLogFile()
    {
        var initialLogPath = DataGuardLogger.LogFilePath;
        DataGuardLogger.Configure(true, @"\\malicious-smb-server\share\logs");
        DataGuardLogger.LogFilePath.Should().NotStartWith(@"\\malicious-smb-server");
    }

    [Fact]
    public void FormatProgressLine_ContractDiscovered_SuppressesOutput()
    {
        var json = "{\"Kind\":\"ContractDiscovered\",\"Phase\":\"Contract Discovery\",\"Detail\":\"Found SQL in Repositories/UserDao.cs:42\"}";
        var output = ProgressLineParser.FormatProgressLine(json, false);

        output.FormattedOutput.Should().BeNull();
    }

    [Fact]
    public void FormatProgressLine_RuleExecutedWithZeroViolations_SuppressesOutput()
    {
        var json = "{\"Kind\":\"RuleExecuted\",\"Phase\":\"Validation\",\"Detail\":\"DG101\",\"Data\":{\"ContractCount\":1,\"ViolationCount\":0}}";
        var output = ProgressLineParser.FormatProgressLine(json, false);

        output.FormattedOutput.Should().BeNull();
    }

    [Fact]
    public void FormatProgressLine_RuleExecutedWithViolations_PreservesOutput()
    {
        var json = "{\"Kind\":\"RuleExecuted\",\"Phase\":\"Validation\",\"Detail\":\"DG101\",\"Data\":{\"RuleId\":\"DG101\",\"RuleTitle\":\"Parameter Count Match\",\"ContractCount\":1,\"ViolationCount\":3}}";
        var output = ProgressLineParser.FormatProgressLine(json, false);

        output.FormattedOutput.Should().NotBeNull();
        output.FormattedOutput.Should().Contain("DG101 (Parameter Count Match):");
        output.FormattedOutput.Should().Contain("3 violations");
    }

    [Fact]
    public void FormatProgressLine_RuleExecutedWithoutTitle_FallsBackToRuleId()
    {
        var json = "{\"Kind\":\"RuleExecuted\",\"Phase\":\"Validation\",\"Detail\":\"DG101\",\"Data\":{\"RuleId\":\"DG101\",\"ContractCount\":1,\"ViolationCount\":3}}";
        var output = ProgressLineParser.FormatProgressLine(json, false);

        output.FormattedOutput.Should().NotBeNull();
        output.FormattedOutput.Should().Contain("DG101: 1 contracts checked → 3 violations");
    }

    [Fact]
    public void FormatProgressLine_PhaseStartedAndCompleted_PreservesOutput()
    {
        var startedJson = "{\"Kind\":\"PhaseStarted\",\"Phase\":\"Rule Evaluation\",\"Detail\":\"Running active rules\"}";
        var startedOutput = ProgressLineParser.FormatProgressLine(startedJson, false);
        startedOutput.FormattedOutput.Should().NotBeNull();
        startedOutput.FormattedOutput.Should().Contain("▶ Rule Evaluation — Running active rules");

        var completedJson = "{\"Kind\":\"PhaseCompleted\",\"Phase\":\"Contract Discovery\",\"Data\":{\"ContractCount\":2886}}";
        var completedOutput = ProgressLineParser.FormatProgressLine(completedJson, false);
        completedOutput.FormattedOutput.Should().NotBeNull();
        completedOutput.FormattedOutput.Should().Contain("✔ Contract Discovery: 2886 contracts");
    }

    [Fact]
    public void FormatProgressLine_Summary_PreservesOutputAndCounts()
    {
        var summaryJson = "{\"Kind\":\"Summary\",\"Phase\":\"Validation complete\",\"Data\":{\"ErrorCount\":2,\"WarningCount\":5}}";
        var output = ProgressLineParser.FormatProgressLine(summaryJson, false);

        output.FormattedOutput.Should().NotBeNull();
        output.FormattedOutput.Should().Contain("✔ Validation complete: 2 errors, 5 warnings");
        output.ErrorCount.Should().Be(2);
        output.WarningCount.Should().Be(5);
    }

    [Theory]
    [InlineData(300, 300)]
    [InlineData(5, 5)]
    [InlineData(900, 900)]
    [InlineData(0, 5)]
    [InlineData(-100, 5)]
    [InlineData(1000, 900)]
    public void ValidationTimeoutSeconds_ClampsToSafeRange(int input, int expected)
    {
        var options = (DataGuardOptionsPage)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(DataGuardOptionsPage));
        options.ValidationTimeoutSeconds = input;
        options.ValidationTimeoutSeconds.Should().Be(expected);
    }

    [Theory]
    [InlineData(60, 60)]
    [InlineData(5, 5)]
    [InlineData(900, 900)]
    [InlineData(0, 5)]
    [InlineData(-50, 5)]
    [InlineData(1500, 900)]
    public void AssessmentTimeoutSeconds_ClampsToSafeRange(int input, int expected)
    {
        var options = (DataGuardOptionsPage)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(DataGuardOptionsPage));
        options.AssessmentTimeoutSeconds = input;
        options.AssessmentTimeoutSeconds.Should().Be(expected);
    }
}
