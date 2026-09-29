using System.IO;
using System.Text;
using System.Threading.Tasks;
using DataGuard.VisualStudio;
using FluentAssertions;
using Xunit;

namespace DataGuard.VisualStudio.Tests;

/// <summary>Every IDE-launched command must carry --ide-safe, and a CLI that rejects it must be detected.</summary>
public class CliArgumentBuilderTests
{
    [Fact]
    public void BuildValidateArguments_AlwaysIncludesIdeSafeAndSkipRules()
    {
        var args = CliArgumentBuilder.BuildValidateArguments(@"D:\s\.dataguard.yml", @"C:\t\v.sarif", @"D:\s", new[] { "DG002", "bad id", "DG017" });

        args.Should().StartWith("validate --config \"D:\\s\\.dataguard.yml\" --format sarif --output \"C:\\t\\v.sarif\" --project \"D:\\s\" --progress --ide-safe");
        args.Should().EndWith(" --skip-rules \"DG002,DG017\"");
        args.Should().NotContain("bad id");
    }

    [Fact]
    public void BuildAssessArguments_AlwaysIncludesIdeSafe()
    {
        CliArgumentBuilder.BuildAssessArguments(@"D:\s", @"C:\t\v.sarif")
            .Should().Be("assess --workspace \"D:\\s\" --format sarif --output \"C:\\t\\v.sarif\" --progress --ide-safe");
    }

    [Theory]
    [InlineData("Unrecognized command or argument '--ide-safe'.", true)]
    [InlineData("error: unknown option '--ide-safe'", true)]
    [InlineData("ide-safe: suppressed ManualAssemblyPath", false)]
    [InlineData("Unrecognized command or argument '--foo'.", false)]
    [InlineData("Unrecognized command or argument '--foo'. (hint: --ide-safe is supported)", false)]
    [InlineData("", false)]
    public void IsIdeSafeUnsupportedMessage_DetectsOnlyRejectionOfTheFlag(string line, bool expected)
    {
        CliArgumentBuilder.IsIdeSafeUnsupportedMessage(line).Should().Be(expected);
    }

    [Fact]
    public async Task ProgressStreamReader_FlagsIdeSafeRejection_AndStillCollectsSummary()
    {
        var stderr = "Unrecognized command or argument '--ide-safe'.\n" +
            "{\"Kind\":\"Summary\",\"Phase\":\"Validation complete\",\"Data\":{\"ErrorCount\":1,\"WarningCount\":2}}\n";
        var written = new StringBuilder();
        var reader = new ProgressStreamReader(new RuleInventory(), text =>
        {
            written.Append(text);
            return Task.CompletedTask;
        });

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(stderr));
        using var streamReader = new StreamReader(stream);
        var result = await reader.ReadAsync(streamReader);

        result.IdeSafeUnsupported.Should().BeTrue();
        result.HasSummary.Should().BeTrue();
        result.ErrorCount.Should().Be(1);
        written.ToString().Should().Contain("[DataGuard CLI] Unrecognized command or argument '--ide-safe'.");
    }

    [Theory]
    [InlineData(@"C:\tools\dataguard.exe", true)]
    [InlineData(@"\\server\share\dataguard.exe", true)]
    [InlineData(@"\tools\dataguard.exe", false)]
    [InlineData(@"C:tools\dataguard.exe", false)]
    [InlineData(@"\\?\C:\tools\dataguard.exe", false)]
    [InlineData(@"tools\dataguard.exe", false)]
    public void CliLocator_IsFullyQualified_RejectsDriveRelativeAndRootRelativePaths(string path, bool expected)
    {
        CliLocator.IsFullyQualified(path).Should().Be(expected);
    }

    [Fact]
    public void ExitCodeExplainer_DistinguishesCrashFromFindings()
    {
        ExitCodeExplainer.Explain("validate", 1, hasSummary: true, warningCount: 0).Should().Contain("found errors");
        ExitCodeExplainer.Explain("validate", 1, hasSummary: false, warningCount: 0).Should().Contain("failed before producing a validation summary");
        ExitCodeExplainer.Explain("validate", 3, hasSummary: false, warningCount: 0).Should().Contain("Validation incomplete");
        ExitCodeExplainer.Explain("validate", 0, hasSummary: true, warningCount: 2).Should().Contain("with warnings");
        ExitCodeExplainer.Explain("assess", 2, hasSummary: false, warningCount: 0).Should().Contain("Invalid arguments for assess");
        ExitCodeExplainer.Explain("assess", 130, hasSummary: false, warningCount: 0).Should().Contain("[CANCELLED]");
    }

    [Fact]
    public void ExtensionVersion_ReadsIdentityVersionFromManifest_AndFallsBackOtherwise()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "dg_ver_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var manifest = Path.Combine(tempDir, "extension.vsixmanifest");
        try
        {
            File.WriteAllText(manifest, "<?xml version=\"1.0\"?><PackageManifest Version=\"2.0.0\" xmlns=\"http://schemas.microsoft.com/developer/vsx-schema/2011\"><Metadata><Identity Id=\"x\" Version=\"9.8.7\" Language=\"en-US\" Publisher=\"p\" /></Metadata></PackageManifest>");
            ExtensionVersion.ReadManifestVersion(manifest).Should().Be("9.8.7");
            ExtensionVersion.ReadManifestVersion(Path.Combine(tempDir, "missing.vsixmanifest")).Should().BeNull();
            ExtensionVersion.Fallback.Should().MatchRegex(@"^\d+\.\d+\.\d+$");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ExtensionVersion_FallbackMatchesSourceManifest()
    {
        var repoRoot = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (repoRoot != null && !File.Exists(Path.Combine(repoRoot.FullName, "DataGuard.sln")))
        {
            repoRoot = repoRoot.Parent;
        }

        repoRoot.Should().NotBeNull();
        var manifestPath = Path.Combine(repoRoot!.FullName, "src", "DataGuard.VisualStudio", "source.extension.vsixmanifest");
        ExtensionVersion.ReadManifestVersion(manifestPath).Should().Be(ExtensionVersion.Fallback);
    }

    [Fact]
    public void Redact_CoversConnectionStringAndJwtForms()
    {
        DataGuardLogger.Redact("Server=x;User Id=sa;Password=Hunter2;Encrypt=true").Should().Be("Server=x;User Id=sa;Password=[REDACTED];Encrypt=true");
        DataGuardLogger.Redact("Authorization: Bearer abc.def").Should().NotContain("abc.def");

        // JWT-shaped fixture assembled at runtime (three base64url segments) so no token literal exists in source.
        var signature = new string('Q', 43);
        var jwtShaped = string.Join(".", "eyJhbGciOiJIUzI1NiJ9", "eyJzdWIiOiIxMjM0NTY3ODkwIn0", signature);
        DataGuardLogger.Redact("token " + jwtShaped).Should().NotContain(signature);
        DataGuardLogger.Redact("plain text without secrets").Should().Be("plain text without secrets");
    }
}
