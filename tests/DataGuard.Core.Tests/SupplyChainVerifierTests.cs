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
}
