using System;
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
/// They verify that the analyzer, its code fixes and the two assemblies the analyzer loads at
/// runtime (DataGuard.Contracts, DataGuard.SqlClassification) are present in the built VSIX
/// container and declared as analyzer assets in vsixmanifest.
/// Without a VSIX the tests return (local runs, the test job builds with CreateVsixContainer=false);
/// with <c>DATAGUARD_VSIX_EXPECTED=1</c> (the packaging job) a missing VSIX is a failure, so the
/// gate cannot silently pass because nothing was packaged.
/// </summary>
public class VsixAnalyzerPackagingTests
{
    /// <summary>Environment variable the packaging job sets so a missing VSIX fails instead of returning.</summary>
    internal const string VsixExpectedVariable = "DATAGUARD_VSIX_EXPECTED";

    /// <summary>Lowest VS 2022 version whose Roslyn (4.12) loads the bundled analyzer.</summary>
    internal const string MinimumVisualStudioVersion = "17.12";

    /// <summary>Every assembly the VSIX must carry for the in-editor analyzer to load (manifest + container).</summary>
    internal static readonly string[] RequiredAnalyzerAssemblies =
    {
        "DataGuard.Analyzers.dll",
        "DataGuard.CodeFixes.dll",
        "DataGuard.Contracts.dll",
        "DataGuard.SqlClassification.dll",
    };

    private static readonly XNamespace VsixNamespace = "http://schemas.microsoft.com/developer/vsx-schema/2011";

    private static string? FindVsixPath()
    {
        var binDirs = new[]
        {
            Path.Combine(TestPaths.RepoRoot, "src", "DataGuard.VisualStudio", "bin", "Debug"),
            Path.Combine(TestPaths.RepoRoot, "src", "DataGuard.VisualStudio", "bin", "Release"),
        };
        foreach (var dir in binDirs)
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }

            // The VSSDK writes the container under bin/<Configuration>/<TargetFramework>/, so search recursively (as CI does).
            var vsix = Directory.GetFiles(dir, "*.vsix", SearchOption.AllDirectories).FirstOrDefault();
            if (vsix != null)
            {
                return vsix;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the VSIX path, or null when none was built and the run is not expected to have one.
    /// With <c>DATAGUARD_VSIX_EXPECTED=1</c> a missing VSIX fails the test.
    /// </summary>
    internal static string? RequireVsixPath()
    {
        var vsixPath = FindVsixPath();
        if (vsixPath == null && IsVsixExpected(Environment.GetEnvironmentVariable(VsixExpectedVariable)))
        {
            throw new Xunit.Sdk.XunitException(
                $"{VsixExpectedVariable}=1 but no *.vsix was found under src/DataGuard.VisualStudio/bin/{{Debug,Release}} "
                + $"(repo root {TestPaths.RepoRoot}); the packaging job must build with CreateVsixContainer=true before running these tests.");
        }

        return vsixPath;
    }

    /// <summary>Returns whether the variable value demands a VSIX (<c>1</c> or <c>true</c>, case-insensitive).</summary>
    internal static bool IsVsixExpected(string? value)
        => string.Equals(value?.Trim(), "1", StringComparison.Ordinal)
           || string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase);

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("0", false)]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    public void IsVsixExpected_ParsesTheGateVariable(string? value, bool expected)
    {
        IsVsixExpected(value).Should().Be(expected);
    }

    [Fact]
    public void VsixPackage_WhenBuilt_ContainsAnalyzersDll()
    {
        var vsixPath = RequireVsixPath();
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
        var vsixPath = RequireVsixPath();
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
    public void VsixPackage_WhenBuilt_ContainsAnalyzerRuntimeDependencies()
    {
        var vsixPath = RequireVsixPath();
        if (vsixPath == null)
        {
            return;
        }

        using var zip = ZipFile.OpenRead(vsixPath);
        var entries = zip.Entries.Select(e => e.Name).ToList();
        foreach (var assembly in RequiredAnalyzerAssemblies)
        {
            entries.Should().Contain(
                assembly,
                "the analyzer references DataGuard.Contracts and DataGuard.SqlClassification at load time; without {0} VS cannot load it",
                assembly);
        }
    }

    [Fact]
    public void VsixManifest_WhenBuilt_DeclaresAnalyzerAsset()
    {
        var vsixPath = RequireVsixPath();
        if (vsixPath == null)
        {
            return;
        }

        using var zip = ZipFile.OpenRead(vsixPath);
        var manifestEntry = zip.GetEntry("extension.vsixmanifest");
        manifestEntry.Should().NotBeNull("VSIX must contain extension.vsixmanifest");

        using var stream = manifestEntry!.Open();
        var manifest = XDocument.Load(stream);

        var analyzerAssetPaths = AnalyzerAssetPaths(manifest);
        analyzerAssetPaths.Should().BeEquivalentTo(
            RequiredAnalyzerAssemblies,
            "vsixmanifest must declare every bundled analyzer assembly as a Microsoft.VisualStudio.Analyzer asset");
    }

    [Fact]
    public void SourceManifest_DeclaresFourAnalyzerAssets_AndVs2022_17_12Floor()
    {
        // Source-level check (no VSIX needed): the committed manifest declares the four analyzer assemblies and
        // installs only on VS 2022 >= 17.12, the first release whose Roslyn (4.12) loads the bundled analyzer.
        var manifestPath = Path.Combine(TestPaths.RepoRoot, "src", "DataGuard.VisualStudio", "source.extension.vsixmanifest");
        var manifest = XDocument.Load(manifestPath);

        AnalyzerAssetPaths(manifest).Should().BeEquivalentTo(RequiredAnalyzerAssemblies);

        var installationTargets = manifest.Descendants(VsixNamespace + "InstallationTarget").ToList();
        installationTargets.Should().NotBeEmpty();
        foreach (var target in installationTargets)
        {
            ((string?)target.Attribute("Version")).Should().StartWith(
                "[" + MinimumVisualStudioVersion + ",",
                "VS 2022 below 17.12 ships Roslyn < 4.12 and refuses the analyzer (CS9057); the install floor must say so");
        }

        var prerequisite = manifest.Descendants(VsixNamespace + "Prerequisite")
            .Single(p => (string?)p.Attribute("Id") == "Microsoft.VisualStudio.Component.CoreEditor");
        ((string?)prerequisite.Attribute("Version")).Should().StartWith("[" + MinimumVisualStudioVersion + ",");
    }

    [Fact]
    public void VsixPackage_WhenBuilt_ContainsLegalNotices()
    {
        var vsixPath = RequireVsixPath();
        if (vsixPath == null)
        {
            return;
        }

        using var zip = ZipFile.OpenRead(vsixPath);
        var entries = zip.Entries.Select(e => e.Name).ToList();
        entries.Should().Contain("LICENSE.txt", "VSIX must bundle LICENSE.txt (GPL-3.0-only)");
        entries.Should().Contain("THIRD-PARTY-NOTICES.md", "VSIX must bundle THIRD-PARTY-NOTICES.md");
        entries.Should().Contain("ADDITIONAL-PERMISSIONS.md", "VSIX must bundle ADDITIONAL-PERMISSIONS.md");
    }

    private static System.Collections.Generic.List<string> AnalyzerAssetPaths(XDocument manifest)
        => manifest
            .Descendants(VsixNamespace + "Asset")
            .Where(a => (string?)a.Attribute("Type") == "Microsoft.VisualStudio.Analyzer")
            .Select(a => (string?)a.Attribute("Path") ?? string.Empty)
            .ToList();
}
