using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

public class HealthHostBindingTests
{
    [Fact]
    public void HealthHostOptions_RejectsRefreshIntervalAtOrAboveSnapshotAge()
    {
        var options = new HealthHostOptions { MaximumSnapshotAgeSeconds = 30, RefreshIntervalSeconds = 30 };

        var act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>().WithMessage("*less than MaximumSnapshotAgeSeconds*");
    }

    [Fact]
    public void HealthHostOptions_AcceptsRefreshIntervalBelowSnapshotAge()
    {
        var options = new HealthHostOptions { MaximumSnapshotAgeSeconds = 30, RefreshIntervalSeconds = 10 };

        options.Validate();
    }

    [Theory]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("http://localhost:8080;http://[::1]:8081")]
    public void IsLoopbackOnly_AcceptsLoopbackUrls(string urls) =>
        HealthHostBinding.IsLoopbackOnly(urls).Should().BeTrue();

    [Theory]
    [InlineData("http://0.0.0.0:8080")]
    [InlineData("http://192.0.2.10:8080")]
    [InlineData("https://example.test")]
    [InlineData("not-a-url")]
    [InlineData("")]
    [InlineData("ftp://localhost:8080")]
    public void IsLoopbackOnly_RejectsRemoteAndMalformedUrls(string urls) =>
        HealthHostBinding.IsLoopbackOnly(urls).Should().BeFalse();
}
