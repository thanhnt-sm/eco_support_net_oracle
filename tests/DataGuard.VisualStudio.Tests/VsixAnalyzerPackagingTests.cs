using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace DataGuard.VisualStudio.Tests;

/// <summary>
/// VSIX packaging integration tests for Roslyn analyzer bundling (Step 4).
/// These tests operate on the VSIX artifact directly and do NOT require VS SDK.
/// They verify that DataGuard.Analyzers.dll and DataGuard.CodeFixes.dll are
/// present in the built VSIX container and declared in vsixmanifest.
/// Tests skip gracefully when VSIX artifact is absent (e.g. CI builds without VSIX step).
/// </summary>
public class VsixAnalyzerPackagingTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private static string? FindVsixPath()
    {
        var binDirs = new[]
        {
            Path.Combine(RepoRoot, "src", "DataGuard.VisualStudio", "bin", "Debug"),
            Path.Combine(RepoRoot, "src", "DataGuard.VisualStudio", "bin", "Release"),
        };
        foreach (var dir in binDirs)
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }

            var vsix = Directory.GetFiles(dir, "*.vsix", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (vsix != null)
            {
                return vsix;
            }
        }

        return null;
    }

    [Fact]
    public void VsixPackage_WhenBuilt_ContainsAnalyzersDll()
    {
        var vsixPath = FindVsixPath();
        if (vsixPath == null)
        {
            return; // VSIX not built in this test run — skip gracefully.
        }

        using var zip = ZipFile.OpenRead(vsixPath);
        var entries = zip.Entries.Select(e => e.Name).ToList();
        entries.Should().Contain(
            "DataGuard.Analyzers.dll",
            "VSIX must bundle DataGuard.Analyzers.dll for in-editor Roslyn squiggles");
    }

    [Fact]
    public void VsixPackage_WhenBuilt_ContainsCodeFixesDll()
    {
        var vsixPath = FindVsixPath();
        if (vsixPath == null)
        {
            return;
        }

        using var zip = ZipFile.OpenRead(vsixPath);
        var entries = zip.Entries.Select(e => e.Name).ToList();
        entries.Should().Contain(
            "DataGuard.CodeFixes.dll",
            "VSIX must bundle DataGuard.CodeFixes.dll for lightbulb code fixes");
    }

    [Fact]
    public void VsixManifest_WhenBuilt_DeclaresAnalyzerAsset()
    {
        var vsixPath = FindVsixPath();
        if (vsixPath == null)
        {
            return;
        }

        using var zip = ZipFile.OpenRead(vsixPath);
        var manifestEntry = zip.GetEntry("extension.vsixmanifest");
        manifestEntry.Should().NotBeNull("VSIX must contain extension.vsixmanifest");

        using var stream = manifestEntry!.Open();
        var manifest = XDocument.Load(stream);
        XNamespace ns = "http://schemas.microsoft.com/developer/vsx-schema/2011";

        var analyzerAssets = manifest
            .Descendants(ns + "Asset")
            .Where(a => (string?)a.Attribute("Type") == "Microsoft.VisualStudio.Analyzer")
            .ToList();

        analyzerAssets.Should().HaveCountGreaterOrEqualTo(
            1,
            "vsixmanifest must declare at least one Microsoft.VisualStudio.Analyzer asset");
    }

    [Fact]
    public void ObjAnalyzersDirectory_AfterBuildAnalyzersTarget_ContainsAnalyzersDll()
    {
        // Verifies the MSBuild BuildAnalyzers target output — works without full VSIX build.
        var objAnalyzersDir = Path.Combine(
            RepoRoot, "src", "DataGuard.VisualStudio", "obj", "analyzers");

        if (!Directory.Exists(objAnalyzersDir))
        {
            return; // BuildAnalyzers target hasn't run yet — skip.
        }

        var analyzersDll = Path.Combine(objAnalyzersDir, "DataGuard.Analyzers.dll");
        File.Exists(analyzersDll).Should().BeTrue(
            $"BuildAnalyzers MSBuild target must output DataGuard.Analyzers.dll to {objAnalyzersDir}");
    }

    [Fact]
    public void ObjAnalyzersDirectory_AfterBuildAnalyzersTarget_ContainsCodeFixesDll()
    {
        var objAnalyzersDir = Path.Combine(
            RepoRoot, "src", "DataGuard.VisualStudio", "obj", "analyzers");

        if (!Directory.Exists(objAnalyzersDir))
        {
            return;
        }

        var codeFixesDll = Path.Combine(objAnalyzersDir, "DataGuard.CodeFixes.dll");
        File.Exists(codeFixesDll).Should().BeTrue(
            $"BuildAnalyzers MSBuild target must output DataGuard.CodeFixes.dll to {objAnalyzersDir}");
    }
}
