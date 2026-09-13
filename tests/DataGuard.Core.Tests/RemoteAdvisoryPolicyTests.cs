using DataGuard.Core.Assessment;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

public class RemoteAdvisoryPolicyTests
{
    [Fact]
    public void FilterApproved_RequiresDoubleOptInAndNeverIncludesUnknownPackages()
    {
        var coordinates = new[]
        {
            new PackageCoordinate("Public.Package", "1.0.0"),
            new PackageCoordinate("Private.Package", "2.0.0"),
        };
        var disabled = new RemoteAdvisoryPolicy
        {
            AllowRemoteLookups = true,
            AllowNetwork = false,
            ApprovedPublicPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Public.Package" },
        };
        var enabled = disabled with { AllowNetwork = true };

        disabled.FilterApproved(coordinates).Should().BeEmpty();
        enabled.FilterApproved(coordinates).Should().ContainSingle().Which.Should().Be(new PackageCoordinate("Public.Package", "1.0.0"));
    }

    [Fact]
    public void FilterApproved_ZeroPackageCapDisablesEgress()
    {
        var policy = new RemoteAdvisoryPolicy
        {
            AllowRemoteLookups = true,
            AllowNetwork = true,
            MaximumPackagesPerRequest = 0,
            ApprovedPublicPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Public.Package" },
        };

        policy.FilterApproved(new[] { new PackageCoordinate("Public.Package", "1.0.0") }).Should().BeEmpty();
    }

    [Fact]
    public void FilterApproved_ZeroPageCapDisablesEgress()
    {
        var policy = new RemoteAdvisoryPolicy
        {
            AllowRemoteLookups = true,
            AllowNetwork = true,
            MaximumPagesPerPackage = 0,
            ApprovedPublicPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Public.Package" },
        };

        policy.FilterApproved(new[] { new PackageCoordinate("Public.Package", "1.0.0") }).Should().BeEmpty();
    }

    [Fact]
    public void FilterApproved_ZeroResponseByteCapDisablesEgress()
    {
        var policy = new RemoteAdvisoryPolicy
        {
            AllowRemoteLookups = true,
            AllowNetwork = true,
            MaximumResponseBytes = 0,
            ApprovedPublicPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Public.Package" },
        };

        policy.FilterApproved(new[] { new PackageCoordinate("Public.Package", "1.0.0") }).Should().BeEmpty();
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(10, 0)]
    [InlineData(10, -1)]
    public void FilterApproved_NonPositiveDetailCapOrTimeoutDisablesEgress(int maximumAdvisoryDetails, int timeoutSeconds)
    {
        var policy = new RemoteAdvisoryPolicy
        {
            AllowRemoteLookups = true,
            AllowNetwork = true,
            MaximumAdvisoryDetails = maximumAdvisoryDetails,
            RequestTimeout = TimeSpan.FromSeconds(timeoutSeconds),
            ApprovedPublicPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Public.Package" },
        };

        policy.FilterApproved(new[] { new PackageCoordinate("Public.Package", "1.0.0") }).Should().BeEmpty();
    }

    [Fact]
    public void FilterApproved_SortsAndCapsSanitizedCoordinates()
    {
        var policy = new RemoteAdvisoryPolicy
        {
            AllowRemoteLookups = true,
            AllowNetwork = true,
            MaximumPackagesPerRequest = 1,
            ApprovedPublicPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A", "B" },
        };

        policy.FilterApproved(new[] { new PackageCoordinate("B", "1"), new PackageCoordinate("A", "2") })
            .Should().ContainSingle().Which.Should().Be(new PackageCoordinate("A", "2"));
    }
}
