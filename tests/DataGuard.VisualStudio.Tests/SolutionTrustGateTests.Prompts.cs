using DataGuard.VisualStudio;
using FluentAssertions;
using Xunit;

namespace DataGuard.VisualStudio.Tests;

/// <summary>Shared prompt assertions exercised by both Windows and cross-platform CI.</summary>
public partial class SolutionTrustGateTests
{
    [Fact]
    public void BuildPromptText_NamesSolutionAndIdeSafeGuarantees()
    {
        var text = SolutionTrustGate.BuildPromptText(@"D:\repo\solution", configExists: true);

        text.Should().Contain(@"D:\repo\solution");
        text.Should().Contain("--ide-safe");
        text.Should().Contain("no assemblies are loaded");
        text.Should().Contain(".dataguard.yml");
        text.Should().EndWith("Run StackAccord for this solution?");
    }
}
