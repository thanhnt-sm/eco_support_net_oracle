using DataGuard.VisualStudio;
using FluentAssertions;
using Xunit;

namespace DataGuard.VisualStudio.Tests;

/// <summary>
/// A run's results belong to the solution that was open when it started; if the solution changed
/// (or closed) while the CLI ran, the results are discarded with an Output line and never published.
/// </summary>
public class SolutionLifetimeWatcherTests
{
    [Theory]
    [InlineData(@"D:\repo\app", @"D:\repo\app")]
    [InlineData(@"D:\repo\app", @"d:\REPO\App\")]
    [InlineData(@"D:\repo\app\", @"D:\repo\app")]
    public void DescribeSolutionMismatch_SameDirectory_ReturnsNull(string captured, string current)
    {
        SolutionLifetimeWatcher.DescribeSolutionMismatch("validate", captured, current).Should().BeNull();
    }

    [Theory]
    [InlineData(@"D:\repo\app", @"D:\repo\other")]
    [InlineData(@"D:\repo\app", "")]
    [InlineData(@"D:\repo\app", null)]
    public void DescribeSolutionMismatch_DifferentOrClosed_ReturnsDiscardMessage(string captured, string? current)
    {
        var message = SolutionLifetimeWatcher.DescribeSolutionMismatch("validate", captured, current);

        message.Should().NotBeNull();
        message.Should().Contain("validate");
        message.Should().Contain("discarded");
        message.Should().EndWith("\r\n");
    }
}
