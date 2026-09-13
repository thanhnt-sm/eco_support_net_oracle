using DataGuard.Core.Assessment;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

public class DependencyHealthScoreTests
{
    [Fact]
    public void Calculate_IncompleteCoverage_NeverEmitsOptimisticNumericScore()
    {
        var result = DependencyHealthScoreCalculator.Calculate(
        [
            new DependencyScoreInput("Public.Package", "1.0.0", true, true, 0, true, true),
            new DependencyScoreInput("Uncovered.Package", "2.0.0", true, false, 0, true, true),
        ]);

        result.State.Should().Be(DependencyScoreState.Partial);
        result.Score.Should().BeNull();
        result.CoveredPackages.Should().Be(1);
        result.EligiblePackages.Should().Be(2);
    }

    [Fact]
    public void Calculate_NegativeAdvisoryCountCannotIncreaseScore()
    {
        var result = DependencyHealthScoreCalculator.Calculate(
        [
            new DependencyScoreInput("Public.Package", "1.0.0", true, true, -10, true, true),
        ]);

        result.State.Should().Be(DependencyScoreState.Complete);
        result.Score.Should().Be(100);
    }

    [Fact]
    public void Calculate_CompleteCoverage_IsDeterministicAndAppliesPublishedCaps()
    {
        var inputs = new[]
        {
            new DependencyScoreInput("B", "1", true, true, 4, true, true),
            new DependencyScoreInput("A", "1", true, true, 1, false, false),
        };

        var first = DependencyHealthScoreCalculator.Calculate(inputs);
        var second = DependencyHealthScoreCalculator.Calculate(inputs.Reverse());

        first.Should().BeEquivalentTo(second);
        first.State.Should().Be(DependencyScoreState.Partial);
        first.Score.Should().BeNull();
    }

    [Fact]
    public void Calculate_UnsupportedFrameworkOrLockMismatch_IsPartialWithoutNumericScore()
    {
        var result = DependencyHealthScoreCalculator.Calculate(
        [
            new DependencyScoreInput("Public.Package", "1.0.0", true, true, 0, false, true),
        ]);

        result.State.Should().Be(DependencyScoreState.Partial);
        result.Score.Should().BeNull();
    }

    [Fact]
    public void Calculate_UnresolvedInventoryReason_IsPartialWithoutNumericScore()
    {
        var result = DependencyHealthScoreCalculator.Calculate(
            [new DependencyScoreInput("Public.Package", "1.0.0", true, true, 0, true, true)],
            ["Lock file has an unresolved dependency entry."]);

        result.State.Should().Be(DependencyScoreState.Partial);
        result.Score.Should().BeNull();
        result.Reasons.Should().ContainSingle().Which.Should().Contain("unresolved");
    }
}
