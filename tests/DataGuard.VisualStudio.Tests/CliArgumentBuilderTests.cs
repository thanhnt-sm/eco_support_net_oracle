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
    [InlineData("error: unknown option '--ide-safe'", false)]
    [InlineData("[DG1290] C:\\x\\Unrecognized command or argument '--ide-safe'.csproj: skipped", false)]
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
        var reader = new ProgressStreamReader(new RuleInventory(), text => written.Append(text));

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
        ExitCodeExplainer.Explain("validate", 1, hasSummary: true, warningCount: 0, sarifExists: true).Should().Contain("found errors");
        ExitCodeExplainer.Explain("validate", 1, hasSummary: false, warningCount: 0, sarifExists: false).Should().Contain("failed before producing a validation summary");
        ExitCodeExplainer.Explain("validate", 3, hasSummary: false, warningCount: 0, sarifExists: false).Should().Contain("Validation incomplete");
        ExitCodeExplainer.Explain("validate", 0, hasSummary: true, warningCount: 2, sarifExists: true).Should().Contain("with warnings");
        ExitCodeExplainer.Explain("assess", 2, hasSummary: false, warningCount: 0, sarifExists: false).Should().Contain("Invalid arguments for assess");
        ExitCodeExplainer.Explain("assess", 130, hasSummary: false, warningCount: 0, sarifExists: false).Should().Contain("[CANCELLED]");
    }

    [Fact]
    public void ExitCodeExplainer_SummaryWithoutSarif_ReportsWriteFailure()
    {
        ExitCodeExplainer.Explain("validate", 1, hasSummary: true, warningCount: 0, sarifExists: false)
            .Should().Be("[ERROR] The CLI reported a summary but failed to write results (see [DataGuard CLI] lines)");
        ExitCodeExplainer.Explain("validate", 3, hasSummary: false, warningCount: 0, sarifExists: false)
            .Should().Contain("previous Error List items were preserved");
    }

    [Fact]
    public void ExitCodeExplainer_TerminatedAtTimeout_DoesNotInterpretTaskkillExitCode()
    {
        const string expected = "[WARN] Terminated at the timeout after results were written; the exit code is not meaningful. See Error List.";

        // taskkill /F leaves exit code 1; without the flag that reads as "found errors" or "failed before producing a summary".
        ExitCodeExplainer.Explain("validate", 1, hasSummary: true, warningCount: 0, sarifExists: true, terminatedAtTimeout: true).Should().Be(expected);
        ExitCodeExplainer.Explain("validate", 1, hasSummary: false, warningCount: 0, sarifExists: true, terminatedAtTimeout: true).Should().Be(expected);
        ExitCodeExplainer.Explain("assess", 0, hasSummary: true, warningCount: 3, sarifExists: true, terminatedAtTimeout: true).Should().Be(expected);
        ExitCodeExplainer.Explain("validate", 1, hasSummary: true, warningCount: 0, sarifExists: true).Should().Contain("found errors");
    }

    [Fact]
    public void NoDotnetToolGuidance_AnywhereInExtensionSources()
    {
        var repoRoot = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (repoRoot != null && !File.Exists(Path.Combine(repoRoot.FullName, "DataGuard.sln")))
        {
            repoRoot = repoRoot.Parent;
        }

        repoRoot.Should().NotBeNull();
        var sourceDir = Path.Combine(repoRoot!.FullName, "src", "DataGuard.VisualStudio");
        var pattern = new System.Text.RegularExpressions.Regex(@"dotnet tool (install|update)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var offenders = new System.Collections.Generic.List<string>();
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*.*", SearchOption.AllDirectories))
        {
            var relative = file.Substring(sourceDir.Length + 1);
            if (relative.StartsWith("bin", System.StringComparison.OrdinalIgnoreCase) || relative.StartsWith("obj", System.StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if ((file.EndsWith(".cs") || file.EndsWith(".md") || file.EndsWith(".vsct")) && pattern.IsMatch(File.ReadAllText(file)))
            {
                offenders.Add(relative);
            }
        }

        offenders.Should().BeEmpty("the CLI is bundled; NuGet DataGuard.Cli is not published");
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
