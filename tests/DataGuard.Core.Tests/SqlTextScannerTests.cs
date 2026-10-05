using System.Diagnostics;
using System.Text;
using DataGuard.Core.Rules;
using DataGuard.Core.Rules.Sql;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

/// <summary>
/// <see cref="SqlTextScanner"/> (split out of <see cref="ColumnShapeMatchRule"/>, red-team rec 20): dollar-quote tags,
/// keyword probes and the public forwarders, plus a performance guard for the former per-character
/// <c>Substring(i)</c> scan, which was quadratic in the number of <c>$</c> characters.
/// </summary>
public class SqlTextScannerTests
{
    [Theory]
    [InlineData("$$ body $$", 0, 2)]
    [InlineData("$fn$ body $fn$", 0, 4)]
    [InlineData("$a_1$", 0, 5)]
    [InlineData("x = $1", 4, 0)]
    [InlineData("$1, $2", 0, 0)]
    [InlineData("$", 0, 0)]
    [InlineData("$é$", 0, 0)]
    [InlineData("abc", 1, 0)]
    public void DollarTagLength_RecognizesOnlyCompleteAsciiTags(string sql, int index, int expected)
    {
        SqlTextScanner.DollarTagLength(sql, index).Should().Be(expected);
    }

    [Fact]
    public void StripCommentsAndLiterals_MasksDollarQuotedBodiesButKeepsPlaceholders()
    {
        SqlTextScanner.StripCommentsAndLiterals("SELECT $tag$ it's -- not a comment $tag$ AS v, $1 FROM t")
            .Should().Be("SELECT '' AS v, $1 FROM t");
    }

    [Fact]
    public void HasUnclosedBlockComment_IgnoresMarkersInsideDollarQuotes()
    {
        SqlTextScanner.HasUnclosedBlockComment("SELECT $$ /* not a comment $$ FROM t").Should().BeFalse();
        SqlTextScanner.HasUnclosedBlockComment("SELECT $1 FROM t /* open").Should().BeTrue();
    }

    [Fact]
    public void KeywordProbes_AreCaseInsensitiveAndWordBounded()
    {
        SqlTextScanner.ExtractTopLevelSelectClause("select a, b into #t from x").Should().Be("a, b");
        SqlTextScanner.ExtractTopLevelSelectClause("SELECT fromage FROM cheese").Should().Be("fromage");
        SqlTextScanner.SplitTopLevelSetBranches("SELECT a FROM t union all SELECT b FROM u Except SELECT c FROM v")
            .Should().Equal("SELECT a FROM t", "SELECT b FROM u", "SELECT c FROM v");
        SqlTextScanner.SplitTopLevelSetBranches("SELECT unionized FROM t").Should().ContainSingle();
    }

    [Fact]
    public void ColumnShapeMatchRule_PublicHelpersForwardToScanner()
    {
        const string sql = "SELECT [Id], u.Name AS FullName /* c */ FROM dbo.Users u";

        ColumnShapeMatchRule.ExtractTopLevelSelectClause(sql).Should().Be(SqlTextScanner.ExtractTopLevelSelectClause(sql));
        ColumnShapeMatchRule.StripCommentsAndLiterals(sql).Should().Be(SqlTextScanner.StripCommentsAndLiterals(sql));
        ColumnShapeMatchRule.HasUnclosedBlockComment(sql).Should().BeFalse();
        ColumnShapeMatchRule.ExtractColumnNamesFromSql(sql).Should().BeEquivalentTo(new[] { "Id", "FullName" });
    }

    [Fact]
    public void Scanning_LargeLiteralWithManyPlaceholders_IsLinear()
    {
        // 256 KB of SQL with 5 000 "$n" placeholders: each '$' used to copy the rest of the string (Substring(i)).
        var builder = new StringBuilder("SELECT id, name FROM t WHERE ");
        for (var i = 1; i <= 5000; i++)
        {
            builder.Append(i == 1 ? string.Empty : " OR ").Append("c = $").Append(i);
        }

        builder.Append(" -- ").Append('x', (256 * 1024) - builder.Length);
        var sql = builder.ToString();
        sql.Length.Should().Be(256 * 1024);

        var stopwatch = Stopwatch.StartNew();
        var stripped = SqlTextScanner.StripCommentsAndLiterals(sql);
        var unclosed = SqlTextScanner.HasUnclosedBlockComment(sql);
        var columns = SqlTextScanner.ExtractColumnNamesFromSql(sql);
        var selectStar = SelectStarUsageRule.ContainsSelectStar(sql);
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
        stripped.Should().Contain("c = $5000").And.NotContain("xxx");
        unclosed.Should().BeFalse();
        columns.Should().BeEquivalentTo(new[] { "id", "name" });
        selectStar.Should().BeFalse();
    }
}
