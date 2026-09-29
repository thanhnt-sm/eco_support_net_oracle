using DataGuard.Cli;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

/// <summary>
/// Pins the process-wide regex bound the CLI installs at startup (code-review M1). This proves the switch the
/// runtime reads is set, not that <c>Regex</c> honoured it: the test process has already initialised
/// <c>Regex</c> before this runs, so the effective default here is frozen at whatever it was first.
/// </summary>
public class RegexMatchTimeoutStartupTests
{
    [Fact]
    public void Apply_RegistersOneSecondDefaultUnderTheRuntimeKey()
    {
        RegexMatchTimeoutStartup.Apply();

        AppContext.GetData("REGEX_DEFAULT_MATCH_TIMEOUT").Should().Be(TimeSpan.FromSeconds(1));
        RegexMatchTimeoutStartup.Configured.Should().Be(RegexMatchTimeoutStartup.DefaultMatchTimeout);
        RegexMatchTimeoutStartup.DefaultMatchTimeout.Should().Be(TimeSpan.FromSeconds(1));
    }
}
