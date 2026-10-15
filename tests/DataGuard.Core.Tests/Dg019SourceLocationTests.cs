using DataGuard.Cli;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Reporting;
using DataGuard.Core.Sources;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

public sealed class Dg019SourceLocationTests
{
    [Fact]
    public void SegmentBoundaries_UseContainingExpressionOrNoLocation()
    {
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("class Example {}");
        var location = tree.GetRoot().GetLocation();
        var segment = new SqlSourceSegment(10, 4, location, false);
        segment.Resolve(9).Should().BeNull();
        segment.Resolve(14).Should().BeNull();
        segment.Resolve(10).Should().BeSameAs(location);
        segment.Resolve(13).Should().BeSameAs(location);
        new SqlSourceSegment(10, 4, Microsoft.CodeAnalysis.Location.None, true)
            .Resolve(11).Should().BeSameAs(Microsoft.CodeAnalysis.Location.None);
    }

    [Theory]
    [InlineData(false, "\n")]
    [InlineData(true, "\n")]
    [InlineData(false, "\r\n")]
    [InlineData(true, "\r\n")]
    public async Task CustomWrapper_AliasError_NavigatesToSqlToken(bool update, string newline)
    {
        var expression = update
            ? """
              $@"
              UPDATE SAMPLE_RECORD rec SET rec.UPDATED_BY = {model.UpdatedBy}, rec.UPDATED_AT = {model.UpdatedAt}, rec .AMOUNT = {model.Amount}
              WHERE rec.ID = {model.Id}
              "
              """
            : """
              @"
                  delete from SAMPLE_CONTACT contact where contact.ORGANIZATION_ID = " + id
              """;
        var code = """
            class DataContext { public void ExecuteNonQuery(string sql) {} }
            class Repository
            {
                void Run(DataContext context, dynamic model, int? id)
                {
                    string sql = EXPRESSION;
                    context.ExecuteNonQuery(sql);
                }
            }
            """.Replace("EXPRESSION", expression).Replace("\n", newline);
        var directory = Path.Combine(Path.GetTempPath(), "dg019-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "Repository.cs"), code);
            var contracts = await new ProjectCSharpSqlSource(directory).ExtractContractsAsync();
            var raw = contracts.OfType<RawSqlDescriptor>().Should().ContainSingle().Subject;
            raw.ConnectionProviderHint.Should().BeNull();
            raw.SqlText.Should().StartWith(newline);
            raw.SqlText.Should().Contain(update ? "@UpdatedBy" : "@id");
            var rule = ProviderRuleCatalog.Get("sqlserver").Single(r => r.Rule.RuleId == "DG019").Rule;
            var violation = (await rule.ValidateAsync(raw, contracts)).Should().ContainSingle().Subject;
            var expected = code.IndexOf(update ? "rec SET" : "contact where", StringComparison.Ordinal);
            violation.Location!.SourceSpan.Start.Should().Be(expected);
            violation.Message.Should().Contain("sqlserver").And.Contain("SQL-relative");
            var sarif = DiagnosticEmitter.CreateSarifLog(new[] { violation }, directory);
            var region = sarif.Runs.Single().Results.Single().Locations.Single().PhysicalLocation.Region;
            var position = violation.Location.GetLineSpan().StartLinePosition;
            region.StartLine.Should().Be(position.Line + 1);
            region.StartColumn.Should().Be(position.Character + 1);
            var oracle = ProviderRuleCatalog.Get("oracle").Single(r => r.Rule.RuleId == "DG019").Rule;
            (await oracle.ValidateAsync(raw, contracts)).Should().BeEmpty("Oracle SQL must not be checked with the T-SQL grammar");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
    [Theory]
    [InlineData("$@\"SELECT {id} FROM SAMPLE_RECORD WHERE )\"", true)]
    [InlineData("$@\"SELECT {\"ID\"} FROM SAMPLE_RECORD WHERE )\"", true)]
    [InlineData("(@\"SELECT ID FROM SAMPLE_RECORD WHERE )\")", true)]
    [InlineData("@\"SELECT \" + id + @\" FROM SAMPLE_RECORD WHERE )\"", true)]
    [InlineData("\"SELECT \" + id + \" FROM SAMPLE_RECORD WHERE )\"", true)]
    [InlineData("\"\\nSELECT ID FROM SAMPLE_RECORD WHERE )\"", false)]
    [InlineData("@\"SELECT \"\"ID\"\" FROM SAMPLE_RECORD WHERE )\"", false)]
    [InlineData("\"\"\"SELECT ID FROM SAMPLE_RECORD WHERE )\"\"\"", false)]
    public async Task ErrorAfterDynamicValue_UsesOriginalSourceOrContainingToken(string expression, bool exact)
    {
        var code = "class DataContext { public void ExecuteNonQuery(string sql) {} }\n"
            + "class Repository { void Run(DataContext context, int? id) { string sql = " + expression
            + "; context.ExecuteNonQuery(sql); } }";
        var directory = Path.Combine(Path.GetTempPath(), "dg019-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "Repository.cs"), code);
            var contracts = await new ProjectCSharpSqlSource(directory).ExtractContractsAsync();
            var raw = contracts.OfType<RawSqlDescriptor>().Should().ContainSingle().Subject;
            var rule = ProviderRuleCatalog.Get("sqlserver").Single(r => r.Rule.RuleId == "DG019").Rule;
            var violation = (await rule.ValidateAsync(raw, contracts)).Should().ContainSingle().Subject;
            var expected = exact ? code.IndexOf("WHERE )", StringComparison.Ordinal) + 6 : code.IndexOf(expression, StringComparison.Ordinal);
            violation.Location!.SourceSpan.Start.Should().Be(expected);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
