using DataGuard.SqlClassification;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

public sealed class SqlClassifierTests
{
    private static readonly Uri Document = new("file:///tmp/query.sql");

    [Fact]
    public void ClassifyCSharp_RecognizesKeywordsWithWordBoundariesAndStableOffsets()
    {
        const string source = "var value = \"SELECT * FROM Customers\"; var body = \"BEGIN TRANSACTION\"; var label = \"SELECTED\";";

        var result = SqlClassifier.ClassifyCSharp(source, new Uri("file:///tmp/Customer.cs"), "7");

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

    [Theory]
    [InlineData("EXECUTE dbo.ArchiveOrders @id", "EXECUTE", "Stored procedure call 'dbo.ArchiveOrders'.")]
    [InlineData("EXEC [dbo].[ArchiveOrders]", "EXEC", "Stored procedure call 'dbo.ArchiveOrders'.")]
    [InlineData("CALL archive_orders(1)", "CALL", "Stored procedure call 'archive_orders'.")]
    [InlineData("EXECUTE IMMEDIATE v_sql", "EXECUTE", "Dynamic SQL execution: the statement text is not visible to static validation.")]
    public void Classify_RecognizesProcedureCalls(string sql, string kind, string detail)
    {
        var result = SqlClassifier.Classify(sql, Document, "1");

        result.Should().ContainSingle().Which.Should().Match<SqlClassification.SqlClassification>(
            item => item.Kind == kind && item.Start == 0 && item.Length == kind.Length && item.DetailMessage == detail);
    }

    [Theory]
    [InlineData("CREATE TABLE dbo.Orders (Id int)", "CREATE", "dbo.Orders", "SQL DDL statement: CREATE TABLE 'dbo.Orders'.")]
    [InlineData("DROP TABLE IF EXISTS [dbo].[Orders]", "DROP", "dbo.Orders", "SQL DDL statement: DROP TABLE 'dbo.Orders'.")]
    [InlineData("ALTER VIEW v_orders AS SELECT 1 AS x", "ALTER", null, "SQL DDL statement: ALTER VIEW 'v_orders'.")]
    [InlineData("TRUNCATE TABLE Staging", "TRUNCATE", "Staging", "SQL DDL statement: TRUNCATE TABLE 'Staging'.")]
    [InlineData("CREATE OR REPLACE PACKAGE BODY pkg_orders AS END;", "CREATE", null, "SQL DDL statement: CREATE PACKAGE 'pkg_orders'.")]
    public void Classify_RecognizesDdl(string sql, string kind, string? table, string detail)
    {
        var result = SqlClassifier.Classify(sql, Document, "1");

        var ddl = result.Should().Contain(item => item.Kind == kind).Which;
        ddl.Table.Should().Be(table);
        ddl.DetailMessage.Should().Be(detail);
    }

    [Theory]
    [InlineData("SELECT * FROM Orders", "Orders")]
    [InlineData("SELECT TOP 10 * FROM Orders", "Orders")]
    [InlineData("SELECT TOP (5) PERCENT * FROM Orders", "Orders")]
    [InlineData("SELECT DISTINCT * FROM dbo.Orders", "dbo.Orders")]
    [InlineData("SELECT o.* FROM [dbo].[Orders] o", "dbo.Orders")]
    [InlineData("SELECT Id, o.* FROM \"sales\".\"Orders\" o", "sales.Orders")]
    [InlineData("SELECT\n    *\nFROM\n    Orders\nWHERE Id = 1", "Orders")]
    public void Classify_DetectsSelectStarShapesAndQualifiedTables(string sql, string table)
    {
        var select = SqlClassifier.Classify(sql, Document, "1").Single(item => item.Kind == "SELECT");

        select.IsSelectStar.Should().BeTrue();
        select.Table.Should().Be(table);
        select.DetailMessage.Should().Contain($"'{table}'").And.Contain("DG017");
    }

    [Theory]
    [InlineData("SELECT COUNT(*) FROM Orders")]
    [InlineData("SELECT Id /* SELECT * */ FROM Orders")]
    [InlineData("SELECT Id FROM Orders -- SELECT * FROM Orders")]
    [InlineData("SELECT Id FROM Orders WHERE Note = 'SELECT * FROM x'")]
    [InlineData("SELECT a * b FROM Orders")]
    public void Classify_DoesNotReportStarInExpressionsCommentsOrStrings(string sql)
    {
        SqlClassifier.Classify(sql, Document, "1").Should().NotContain(item => item.IsSelectStar);
        SqlClassifier.Classify(sql, Document, "1").Should().ContainSingle(item => item.Kind == "SELECT");
    }

    [Fact]
    public void Classify_ReadsSchemaQualifiedWriteTargets()
    {
        var result = SqlClassifier.Classify(
            "INSERT INTO [dbo].[Audit] (Id) VALUES (1); UPDATE dbo.Orders SET X = 1; DELETE FROM sales.Lines; MERGE INTO dbo.Target t USING s ON 1 = 1 WHEN MATCHED THEN UPDATE SET x = 1;",
            Document,
            "1");

        result.Single(item => item.Kind == "INSERT").Table.Should().Be("dbo.Audit");
        result.First(item => item.Kind == "UPDATE").Table.Should().Be("dbo.Orders");
        result.Single(item => item.Kind == "DELETE").Table.Should().Be("sales.Lines");
        result.Single(item => item.Kind == "MERGE").Table.Should().Be("dbo.Target");
        result.Last(item => item.Kind == "UPDATE").Table.Should().BeNull("MERGE ... THEN UPDATE SET has no table");
    }

    [Fact]
    public void Classify_IgnoresTableHintsThatAreNotCommonTableExpressions()
    {
        SqlClassifier.Classify("SELECT Id FROM Orders WITH (NOLOCK)", Document, "1").Should().NotContain(item => item.Kind == "WITH");
        SqlClassifier.Classify("WITH recent AS (SELECT Id FROM Orders) SELECT Id FROM recent", Document, "1")
            .Select(item => item.Kind).Should().Equal("WITH", "SELECT", "SELECT");
    }

    [Fact]
    public void ClassifyCSharp_OnlyClassifiesSqlStringLiterals()
    {
        const string source = """
            // SELECT * FROM Commented
            /* DELETE FROM Commented */
            var names = people.Select(p => p.Name).Where(n => n != null);
            var label = "Please select a file";
            var sql = "SELECT * FROM Customers";
            """;

        var result = SqlClassifier.ClassifyCSharp(source, new Uri("file:///tmp/Repo.cs"), "3");

        var select = result.Should().ContainSingle().Which;
        select.Kind.Should().Be("SELECT");
        select.IsSelectStar.Should().BeTrue();
        select.Table.Should().Be("Customers");
        select.Start.Should().Be(source.IndexOf("SELECT * FROM Customers", StringComparison.Ordinal));
        select.Version.Should().Be("3");
    }

    [Fact]
    public void ClassifyCSharp_HandlesVerbatimInterpolatedAndRawLiteralsWithSourceOffsets()
    {
        const string source = """"
            var a = @"EXECUTE dbo.Archive";
            var b = $"DELETE FROM Orders WHERE Id = {id}";
            var c = """
                SELECT
                    *
                FROM [dbo].[Lines]
                """;
            var d = $$"""UPDATE Items SET Note = '{{note}}'""";
            var e = "EXEC \"quoted\".proc";
            """";

        var result = SqlClassifier.ClassifyCSharp(source, new Uri("file:///tmp/Repo.cs"), "1");

        result.Select(item => item.Kind).Should().Equal("EXECUTE", "DELETE", "SELECT", "UPDATE", "EXEC");
        foreach (var item in result)
        {
            source.Substring(item.Start, item.Length).Should().BeEquivalentTo(item.Kind);
        }

        result.Single(item => item.Kind == "SELECT").Should().Match<SqlClassification.SqlClassification>(
            item => item.IsSelectStar && item.Table == "dbo.Lines");
        result.Single(item => item.Kind == "EXEC").DetailMessage.Should().Be("Stored procedure call 'quoted.proc'.");
    }

    [Theory]
    [InlineData("  SELECT 1", true)]
    [InlineData("-- note\nEXECUTE dbo.P", true)]
    [InlineData("(SELECT 1)", true)]
    [InlineData("call proc()", true)]
    [InlineData("selected products", false)]
    [InlineData("Please select a file", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void LooksLikeSql_RequiresALeadingStatementKeyword(string? text, bool expected)
    {
        SqlClassifier.LooksLikeSql(text).Should().Be(expected);
    }
}
