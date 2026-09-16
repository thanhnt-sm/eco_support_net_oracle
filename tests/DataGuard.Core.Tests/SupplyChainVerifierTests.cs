using System;
using System.IO;
using System.Threading.Tasks;
using DataGuard.Core.Security;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

public class SupplyChainVerifierTests
{
    [Fact]
    public async Task VerifyAsync_NoAnchor_FailsClosed()
    {
        var result = await new SupplyChainVerifier().VerifyAsync();

        result.OverallPassed.Should().BeFalse();
        result.Checks.Should().Contain(check => check.Name == "AssemblyIntegrity" && !check.Passed);
        result.Summary.Should().Contain("failed");
    }

    [Fact]
    public async Task VerifyAsync_MissingHashFile_FlagsUnverifiable()
    {
        var missingFile = Path.Combine(Path.GetTempPath(), $"dataguard-missing-{Guid.NewGuid():N}.txt");

        var result = await new SupplyChainVerifier().VerifyAsync(missingFile);

        result.Checks.Should().Contain(check => check.Name == "ExpectedHashMatch" && !check.Passed);
        result.OverallPassed.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyAsync_ReportsDebugAndDependencyChecks()
    {
        var result = await new SupplyChainVerifier().VerifyAsync();

        result.Checks.Should().Contain(check => check.Name == "DebugSymbols");
        result.Checks.Should().Contain(check => check.Name.StartsWith("Dependency_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task VerifyAsync_DoesNotTreatAssemblyNamePrefixesAsTrustEvidence()
    {
        var result = await new SupplyChainVerifier().VerifyAsync();

        result.Checks.Where(check => check.Name.StartsWith("Dependency_", StringComparison.Ordinal))
            .Should().OnlyContain(check => !check.Passed && check.Details.Contains("unverified", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task VerifyAsync_OversizedHashAnchorFailsClosed()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dataguard-hash-{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllTextAsync(path, new string('a', 1_025));
            var result = await new SupplyChainVerifier().VerifyAsync(path);

            result.Checks.Should().Contain(check => check.Name == "ExpectedHashMatch" && !check.Passed && check.Details.Contains("safety limit", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task VerifyAsync_SymbolicLinkHashAnchorFailsClosed()
    {
        var target = Path.Combine(Path.GetTempPath(), $"dataguard-hash-target-{Guid.NewGuid():N}.txt");
        var link = target + ".link";
        try
        {
            await File.WriteAllTextAsync(target, new string('a', 64));
            try
            {
                File.CreateSymbolicLink(link, target);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            {
                return;
            }

            var result = await new SupplyChainVerifier().VerifyAsync(link);

            result.Checks.Should().Contain(check => check.Name == "ExpectedHashMatch" && !check.Passed);
        }
        finally
        {
            if (File.Exists(link))
            {
                File.Delete(link);
            }

            File.Delete(target);
        }
    }

    [Fact]
    public async Task VerifyAsync_SymbolicLinkHashAnchorParentFailsClosed()
    {
        var targetDirectory = Path.Combine(Path.GetTempPath(), $"dataguard-hash-dir-{Guid.NewGuid():N}");
        var linkDirectory = targetDirectory + ".link";
        var target = Path.Combine(targetDirectory, "anchor.txt");
        var link = Path.Combine(linkDirectory, "anchor.txt");
        try
        {
            Directory.CreateDirectory(targetDirectory);
            await File.WriteAllTextAsync(target, new string('a', 64));
            try
            {
                Directory.CreateSymbolicLink(linkDirectory, targetDirectory);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            {
                return;
            }

            var result = await new SupplyChainVerifier().VerifyAsync(link);

            result.Checks.Should().Contain(check => check.Name == "ExpectedHashMatch" && !check.Passed);
        }
        finally
        {
            if (File.Exists(link))
            {
                File.Delete(link);
            }

            if (Directory.Exists(linkDirectory))
            {
                Directory.Delete(linkDirectory, recursive: false);
            }

            File.Delete(target);
            Directory.Delete(targetDirectory, recursive: true);
        }
    }
}
