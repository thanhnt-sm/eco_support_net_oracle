using System.IO;
using System.Linq;
using System.Text;
using DataGuard.VisualStudio;
using FluentAssertions;
using Xunit;

namespace DataGuard.VisualStudio.Tests;

/// <summary>
/// SARIF loading must be robust per result: one malformed entry cannot empty the Error List,
/// paths never escape the solution, and the task count is capped.
/// </summary>
public class SarifErrorListPublisherTests
{
    private static readonly string SolutionDir = Path.Combine(Path.GetTempPath(), "dg_sarif_sln");

    private static string Wrap(params string[] results)
    {
        return "{\"version\":\"2.1.0\",\"runs\":[{\"tool\":{\"driver\":{\"name\":\"DataGuard\"}},\"results\":[" + string.Join(",", results) + "]}]}";
    }

    private static string Result(string uri, string uriBaseId = "%SRCROOT%", string region = "{\"startLine\":5,\"startColumn\":3}", string extra = "")
    {
        return "{\"ruleId\":\"DG004\",\"level\":\"error\",\"message\":{\"text\":\"Column mismatch\"}," +
            "\"locations\":[{\"physicalLocation\":{\"artifactLocation\":{\"uri\":\"" + uri + "\",\"uriBaseId\":\"" + uriBaseId + "\"},\"region\":" + region + "}}]" + extra + "}";
    }

    [Fact]
    public void Load_ValidResult_ProducesOneBasedDiagnosticWithRulePrefix()
    {
        var loaded = SarifErrorListPublisher.Load(Wrap(Result("src/Model.cs")), SolutionDir);

        loaded.Error.Should().BeNull();
        loaded.Diagnostics.Should().ContainSingle();
        var diagnostic = loaded.Diagnostics[0];
        diagnostic.Document.Should().Be(Path.Combine(SolutionDir, "src", "Model.cs"));
        diagnostic.Line.Should().Be(5);
        diagnostic.Column.Should().Be(3);
        diagnostic.Message.Should().Be("[DG004] Column mismatch");
        diagnostic.Level.Should().Be("error");
    }

    [Fact]
    public void Load_MalformedRegion_SkipsOnlyThatResult()
    {
        var malformed = Result("src/A.cs", region: "{\"startLine\":\"five\",\"startColumn\":1.5}");
        var loaded = SarifErrorListPublisher.Load(Wrap(malformed, Result("src/B.cs")), SolutionDir);

        loaded.Diagnostics.Should().HaveCount(2, "non-integer region numbers fall back to line 0 instead of failing");
        loaded.SkippedCount.Should().Be(0);
    }

    [Fact]
    public void Load_ResultWithNonObjectMessageAndMissingLocation_IsSkippedNotFatal()
    {
        var noLocation = "{\"ruleId\":\"DG001\",\"message\":\"plain string\"}";
        var badLocation = "{\"ruleId\":\"DG001\",\"message\":{\"text\":\"x\"},\"locations\":[{\"physicalLocation\":\"nope\"}]}";
        var loaded = SarifErrorListPublisher.Load(Wrap(noLocation, badLocation, Result("src/Ok.cs")), SolutionDir);

        loaded.Diagnostics.Should().ContainSingle();
        loaded.SkippedCount.Should().Be(2);
    }

    [Fact]
    public void Load_PathTraversalAndRootedOutside_AreSkipped()
    {
        var loaded = SarifErrorListPublisher.Load(
            Wrap(Result("../../Windows/System32/drivers/etc/hosts"), Result("C:/Windows/win.ini", uriBaseId: "")),
            SolutionDir);

        loaded.Diagnostics.Should().BeEmpty();
        loaded.SkippedCount.Should().Be(2);
    }

    [Fact]
    public void Load_PercentEncodedRelativeUri_IsDecoded()
    {
        var loaded = SarifErrorListPublisher.Load(Wrap(Result("src/My%20Folder/Model.cs")), SolutionDir);

        loaded.Diagnostics.Should().ContainSingle();
        loaded.Diagnostics[0].Document.Should().Be(Path.Combine(SolutionDir, "src", "My Folder", "Model.cs"));
    }

    [Fact]
    public void Load_MoreThanCap_TruncatesAndCounts()
    {
        var many = Enumerable.Range(0, SarifErrorListPublisher.MaxErrorListTasks + 25).Select(i => Result("src/F" + i + ".cs")).ToArray();
        var loaded = SarifErrorListPublisher.Load(Wrap(many), SolutionDir);

        loaded.Diagnostics.Should().HaveCount(SarifErrorListPublisher.MaxErrorListTasks);
        loaded.TruncatedCount.Should().Be(25);
    }

    [Fact]
    public void Load_InvalidJson_ReportsErrorWithoutThrowing()
    {
        var loaded = SarifErrorListPublisher.Load("{not json", SolutionDir);

        loaded.Error.Should().StartWith("SARIF output could not be parsed");
        loaded.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Load_MessageWithSecret_IsRedacted()
    {
        var secret = "{\"ruleId\":\"DG016\",\"level\":\"warning\",\"message\":{\"text\":\"Server=x;User Id=sa;Password=Hunter2;\"}," +
            "\"locations\":[{\"physicalLocation\":{\"artifactLocation\":{\"uri\":\"src/A.cs\",\"uriBaseId\":\"%SRCROOT%\"},\"region\":{\"startLine\":1,\"startColumn\":1}}}]}";
        var loaded = SarifErrorListPublisher.Load(Wrap(secret), SolutionDir);

        loaded.Diagnostics.Should().ContainSingle();
        loaded.Diagnostics[0].Message.Should().NotContain("Hunter2");
        loaded.Diagnostics[0].Message.Should().Contain("[REDACTED]");
    }

    [Fact]
    public void ResolveSarifArtifactUri_FileUriInsideSolution_IsAccepted()
    {
        var inside = Path.Combine(SolutionDir, "src", "Model.cs");
        var resolved = SarifErrorListPublisher.ResolveSarifArtifactUri(new System.Uri(inside).AbsoluteUri, null, SolutionDir);

        resolved.Should().Be(inside);
    }
}
