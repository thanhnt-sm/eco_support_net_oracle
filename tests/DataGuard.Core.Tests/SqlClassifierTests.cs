using DataGuard.SqlClassification;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

public sealed class SqlClassifierTests
{
    [Fact]
    public void Classify_RecognizesKeywordsWithWordBoundariesAndStableOffsets()
    {
        const string source = "var value = \"SELECT * FROM Customers\"; var body = \"BEGIN TRANSACTION\"; var label = \"SELECTED\";";

        var result = SqlClassifier.Classify(source, new Uri("file:///tmp/Customer.cs"), "7");

        result.Select(item => item.Kind).Should().BeEquivalentTo(["BEGIN", "SELECT"]);
        result[0].Start.Should().Be(source.IndexOf("SELECT", StringComparison.Ordinal));
        result[0].DocumentUri.AbsoluteUri.Should().Be("file:///tmp/Customer.cs");
        result[0].Version.Should().Be("7");
    }

    [Fact]
    public void Classify_OversizedInput_ReturnsNoResults()
    {
        var result = SqlClassifier.Classify(new string('x', 65_537), new Uri("file:///tmp/large.cs"), "1");

        result.Should().BeEmpty();
    }
}
