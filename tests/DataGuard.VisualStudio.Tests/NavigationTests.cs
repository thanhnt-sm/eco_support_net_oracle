using System.IO;
using DataGuard.VisualStudio;
using FluentAssertions;
using Xunit;

namespace DataGuard.VisualStudio.Tests;

public class NavigationTests
{
    [Fact]
    public void TryFormatProgress_RuleExecutedWithViolations_FormatsCorrectly()
    {
        var json = "{\"Kind\":\"RuleExecuted\", \"Phase\":\"Validation\", \"Detail\":\"Oracle-DG001\", \"Data\":{\"RuleId\":\"Oracle-DG001\", \"RuleTitle\":\"Avoid SELECT *\", \"ContractCount\":10, \"ViolationCount\":3}}";

        var success = ProgressLineParser.TryFormatProgress(json, out var formatted, out var errors, out var warnings);

        success.Should().BeTrue();
        formatted.Should().NotBeNull();
        formatted.Should().Contain("3 violations");
        formatted.Should().Contain("Oracle-DG001");
        formatted.Should().Contain("Avoid SELECT *");
        formatted.Should().Contain("10 contracts checked");
    }

    [Fact]
    public void Navigate_SarifOneBased_LineColumnConversion()
    {
        // SARIF positions are 1-based (startLine=5, startColumn=3).
        // Visual Studio ErrorTask Line and Column are 0-based (line=4, column=2).
        var position = SarifErrorListPublisher.ConvertSarifPosition(5, 3);

        position.Line.Should().Be(4);
        position.Column.Should().Be(2);

        // Edge case: zero or negative should clamp to 0
        var clamped = SarifErrorListPublisher.ConvertSarifPosition(0, 0);
        clamped.Line.Should().Be(0);
        clamped.Column.Should().Be(0);
    }

    [Fact(Skip = "VS SDK integration — requires experimental instance (ErrorListProvider.Navigate needs a live shell)")]
    public void Navigate_WhenFileMissing_WritesOutputMessage()
    {
        // Documented integration test (ErrorListPresenter.CreateTask):
        // When an ErrorTask with a non-existent Document path has its Navigate event raised,
        // it checks File.Exists(task.Document). When false, it must write
        // "[DataGuard] Cannot navigate: file not found '{task.Document}'.\r\n"
        // to the DataGuard output pane; when true it calls ErrorListProvider.Navigate(task, LOGVIEWID_Code),
        // which opens the document and positions the caret at task.Line/task.Column.
    }
}
