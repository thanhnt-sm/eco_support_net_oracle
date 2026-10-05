using DataGuard.Cli;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules;
using DataGuard.Core.Rules.Sql;
using DataGuard.SqlServer.Adapter;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

/// <summary>
/// <see cref="TSqlPhantomAnalyzer"/>: ScriptDOM scope resolution behind DG015/DG016 for the <c>sqlserver</c> provider
/// (plan Phase 3.8). Catalog keys are SQL Server-shaped (<c>dbo.Orders</c>) except one non-dbo schema.
/// </summary>
public class TSqlPhantomAnalyzerTests
{
    private static readonly DatabaseSchemaDescriptor Catalog = new(
        "schema:tsql",
        new List<DatabaseTableDescriptor>
        {
            new("dbo.Orders", new[] { Col("Id"), Col("CustomerId"), Col("Total"), Col("Status"), Col("Code"), Col("OrderDate") }),
            new("dbo.Customers", new[] { Col("Id"), Col("Name"), Col("Email") }),
            new("dbo.Staging", new[] { Col("Id"), Col("Total") }),
            new("sales.Invoices", new[] { Col("Id"), Col("OrderId"), Col("Amount") }),
        },
        "CHAR");

    private static ColumnDescriptor Col(string name) => new(name, "int", null, null, null, false, null);

    private static PhantomAnalysis Analyze(string sql) => new TSqlPhantomAnalyzer().Analyze(sql, SchemaTableIndex.For(Catalog));

    private static void ShouldBeClean(string sql)
    {
        var analysis = Analyze(sql);
        analysis.ParseFailed.Should().BeFalse(analysis.ParseError);
        analysis.PhantomTables.Should().BeEmpty();
        analysis.PhantomColumns.Should().BeEmpty();
    }

    private static RawSqlDescriptor Sql(string text) =>
        new("raw:tsql", text, Array.Empty<ParameterDescriptor>(), Array.Empty<ColumnDescriptor>());

    // ---- Positive cases ----
    [Fact]
    public void BarePhantomTable_IsReported()
    {
        var analysis = Analyze("SELECT Id FROM Ordres");

        analysis.PhantomTables.Should().Equal(new PhantomTableRef("ORDRES"));
        analysis.PhantomColumns.Should().BeEmpty("columns of a phantom table are not checked");
    }

    [Fact]
    public void SchemaQualifiedPhantomTable_IsReported()
    {
        var analysis = Analyze("SELECT o.Id, o.Anything FROM dbo.Ordres o");

        analysis.PhantomTables.Should().Equal(new PhantomTableRef("DBO.ORDRES"));
        analysis.PhantomColumns.Should().BeEmpty();
    }

    [Fact]
    public void QualifiedPhantomColumn_IsReportedAgainstAliasTable()
    {
        var analysis = Analyze("SELECT o.Id, o.Discount FROM dbo.Orders o");

        analysis.PhantomTables.Should().BeEmpty();
        analysis.PhantomColumns.Should().Equal(new PhantomColumnRef("DISCOUNT", "DBO.ORDERS"));
    }

    [Fact]
    public void SchemaTableColumnReference_IsResolvedAgainstTheUnaliasedTable()
    {
        Analyze("SELECT dbo.Orders.Total, dbo.Orders.Nope FROM dbo.Orders")
            .PhantomColumns.Should().Equal(new PhantomColumnRef("NOPE", "DBO.ORDERS"));
    }

    [Fact]
    public void UnqualifiedPhantomColumn_SingleTable_IsReported()
    {
        Analyze("SELECT Id, Discount FROM dbo.Orders WHERE Status = 1")
            .PhantomColumns.Should().Equal(new PhantomColumnRef("DISCOUNT", "DBO.ORDERS"));
    }

    [Fact]
    public void UnqualifiedColumn_SeveralTables_IsCheckedAgainstTheUnion()
    {
        var analysis = Analyze(
            "SELECT Name, Total, Discount FROM dbo.Orders o JOIN dbo.Customers c ON c.Id = o.CustomerId");

        analysis.PhantomColumns.Should().Equal(new PhantomColumnRef("DISCOUNT", "DBO.ORDERS, DBO.CUSTOMERS"));
    }

    [Theory]
    [InlineData("INSERT INTO dbo.Ordrs (Id) VALUES (1)")]
    [InlineData("UPDATE dbo.Ordrs SET Total = 1 WHERE Id = 1")]
    [InlineData("DELETE FROM dbo.Ordrs WHERE Id = 1")]
    [InlineData("MERGE INTO dbo.Ordrs AS t USING dbo.Staging AS s ON t.Id = s.Id WHEN MATCHED THEN UPDATE SET t.Total = s.Total;")]
    public void DmlTargetPhantom_IsReported(string sql)
    {
        var analysis = Analyze(sql);

        analysis.ParseFailed.Should().BeFalse(analysis.ParseError);
        analysis.PhantomTables.Should().Equal(new PhantomTableRef("DBO.ORDRS"));
        analysis.PhantomColumns.Should().BeEmpty();
    }

    [Theory]
    [InlineData("INSERT INTO dbo.Orders (Id, Discount) VALUES (1, 2)")]
    [InlineData("UPDATE dbo.Orders SET Discount = 1 WHERE Id = 1")]
    [InlineData("DELETE FROM dbo.Orders WHERE Discount > 1")]
    public void DmlTargetPhantomColumn_IsReported(string sql)
    {
        Analyze(sql).PhantomColumns.Should().Equal(new PhantomColumnRef("DISCOUNT", "DBO.ORDERS"));
    }

    [Fact]
    public void ClientPlaceholders_StillParse()
    {
        var analysis = Analyze("SELECT Id FROM Ordres WHERE Id = :id AND Status = ? AND Total > {0}");

        analysis.ParseFailed.Should().BeFalse(analysis.ParseError);
        analysis.PhantomTables.Should().Equal(new PhantomTableRef("ORDRES"));
    }

    [Fact]
    public void TrimFrom_ChecksTheColumnNotATable()
    {
        var analysis = Analyze("SELECT TRIM(LEADING '0' FROM Kode) AS c FROM dbo.Orders");

        analysis.PhantomTables.Should().BeEmpty();
        analysis.PhantomColumns.Should().Equal(new PhantomColumnRef("KODE", "DBO.ORDERS"));
    }

    [Fact]
    public void SubqueryAliasReuse_InnerAliasWins_ForPhantom()
    {
        Analyze("SELECT o.Id FROM dbo.Orders o WHERE EXISTS (SELECT 1 FROM dbo.Customers o WHERE o.Total > 0)")
            .PhantomColumns.Should().Equal(new PhantomColumnRef("TOTAL", "DBO.CUSTOMERS"));
    }

    // ---- Negative cases ----
    [Fact]
    public void ExistingSchemaQualifiedTable_IsClean() =>
        ShouldBeClean("SELECT o.Id, o.Total, CustomerId FROM dbo.Orders o WHERE o.Status = @status");

    [Fact]
    public void UnqualifiedTable_DefaultsToDbo() => ShouldBeClean("SELECT Id, Total FROM Orders");

    [Fact]
    public void NonDboSchema_ResolvesByKeyAndBareName()
    {
        ShouldBeClean("SELECT i.Amount, OrderId FROM sales.Invoices i");
        ShouldBeClean("SELECT Amount FROM Invoices");
    }

    [Fact]
    public void MultipleCtes_AreNotTables() =>
        ShouldBeClean(
            "WITH a AS (SELECT Id, Total FROM dbo.Orders), b (X) AS (SELECT Id FROM a) " +
            "SELECT b.X, a.Total, Whatever FROM b JOIN a ON a.Id = b.X");

    [Fact]
    public void RecursiveCte_IsNotATable() =>
        ShouldBeClean(
            "WITH r AS (SELECT Id, CustomerId AS Parent, 0 AS Depth FROM dbo.Orders " +
            "UNION ALL SELECT o.Id, r.Parent, r.Depth + 1 FROM dbo.Orders o JOIN r ON o.CustomerId = r.Id) " +
            "SELECT Id, Parent, Depth FROM r OPTION (MAXRECURSION 10)");

    [Fact]
    public void TempTables_AreNeverChecked()
    {
        ShouldBeClean("SELECT t.Foo, Bar FROM #work t");
        ShouldBeClean("INSERT INTO #work (Foo) SELECT Id FROM dbo.Orders");
        ShouldBeClean("SELECT Foo FROM ##global");
    }

    [Fact]
    public void TableVariables_AreNeverChecked()
    {
        ShouldBeClean("SELECT t.Foo, o.Total FROM @tvp t JOIN dbo.Orders o ON o.Id = t.Id");
        ShouldBeClean("INSERT INTO @ids (Foo) SELECT Id FROM dbo.Orders");
    }

    [Fact]
    public void CrossDatabaseAndLinkedServerNames_AreNeverChecked()
    {
        ShouldBeClean("SELECT x.Foo, Bar FROM OtherDb.dbo.T x");
        ShouldBeClean("SELECT x.Foo FROM Srv.OtherDb.dbo.T x");
        ShouldBeClean("SELECT o.Id, x.Foo FROM dbo.Orders o JOIN OtherDb..T x ON x.Id = o.Id");
    }

    [Fact]
    public void SystemObjects_AreNeverChecked()
    {
        ShouldBeClean("SELECT name, object_id FROM sys.objects WHERE type = 'U'");
        ShouldBeClean("SELECT c.COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS c");
        ShouldBeClean("SELECT name FROM sysobjects");
    }

    [Fact]
    public void TableValuedFunctions_AreScopeSourcesNotTables()
    {
        ShouldBeClean("SELECT f.Foo, Bar FROM dbo.fn_X(@id) f");
        ShouldBeClean("SELECT s.value FROM STRING_SPLIT(@csv, ',') s");
        ShouldBeClean("SELECT o.Id, f.Foo FROM dbo.Orders o CROSS APPLY dbo.fn_Lines(o.Id) f");
    }

    [Fact]
    public void OpenJsonAndOpenRowset_AreScopeSourcesNotTables()
    {
        ShouldBeClean("SELECT j.v, w FROM OPENJSON(@json) WITH (v int '$.v', w nvarchar(10) '$.w') AS j");
        ShouldBeClean("SELECT r.BulkColumn FROM OPENROWSET(BULK 'c:\\f.json', SINGLE_CLOB) AS r");
    }

    [Fact]
    public void DerivedTablesAndValues_AreScopeSourcesNotTables()
    {
        ShouldBeClean("SELECT d.Total2, Total2 FROM (SELECT Total AS Total2 FROM dbo.Orders) d");
        ShouldBeClean("SELECT v.n FROM (VALUES (1), (2)) v(n)");
    }

    [Fact]
    public void OutputAliases_AreNotColumnReferences()
    {
        ShouldBeClean("SELECT COALESCE(Total, 0) AS Amount FROM dbo.Orders ORDER BY Amount");
        ShouldBeClean("SELECT Id OrderId, Total Amount FROM dbo.Orders ORDER BY OrderId");
        ShouldBeClean("SELECT Label = Status FROM dbo.Orders");
    }

    [Fact]
    public void SelectStar_IsNotAColumnReference() => ShouldBeClean("SELECT *, o.* FROM dbo.Orders o");

    [Fact]
    public void TrimLeadingFrom_IsNotATableReference() =>
        ShouldBeClean("SELECT TRIM(LEADING '0' FROM Code) AS c FROM dbo.Orders");

    [Fact]
    public void DatePartArguments_AreNotColumns() =>
        ShouldBeClean("SELECT DATEADD(day, 1, OrderDate), DATEDIFF(month, OrderDate, GETDATE()) FROM dbo.Orders");

    [Fact]
    public void SubqueryAliasReuse_InnerAliasWins() =>
        ShouldBeClean("SELECT o.Total FROM dbo.Orders o WHERE EXISTS (SELECT 1 FROM dbo.Customers o WHERE o.Email = 'x')");

    [Fact]
    public void CorrelatedUnqualifiedOuterColumn_IsResolvedThroughTheScopeChain() =>
        ShouldBeClean("SELECT Id FROM dbo.Customers WHERE EXISTS (SELECT 1 FROM dbo.Orders WHERE Total > 0 AND Email IS NOT NULL)");

    [Fact]
    public void TableHints_AreIgnored() =>
        ShouldBeClean(
            "SELECT o.Id, c.Name FROM dbo.Orders o WITH (NOLOCK) " +
            "JOIN dbo.Customers c WITH (NOLOCK, INDEX(0)) ON c.Id = o.CustomerId");

    [Fact]
    public void BracketedNames_AreUnquoted()
    {
        ShouldBeClean("SELECT [o].[Total], [Status] FROM [dbo].[Orders] AS [o]");
        Analyze("SELECT [x].[Id] FROM [dbo].[Ordres] [x]").PhantomTables.Should().Equal(new PhantomTableRef("DBO.ORDRES"));
    }

    [Fact]
    public void MergeWithUsing_ResolvesTargetAliasAndSource() =>
        ShouldBeClean(
            "MERGE INTO dbo.Orders AS t USING dbo.Staging AS s ON t.Id = s.Id " +
            "WHEN MATCHED THEN UPDATE SET t.Total = s.Total " +
            "WHEN NOT MATCHED THEN INSERT (Id, Total) VALUES (s.Id, s.Total) " +
            "OUTPUT $action, inserted.Id;");

    [Fact]
    public void InsertSelect_ChecksTargetColumnsAndSourceSeparately()
    {
        ShouldBeClean("INSERT INTO dbo.Orders (Id, Total) SELECT Id, Total FROM dbo.Staging");
        Analyze("INSERT INTO dbo.Orders (Id, Total) SELECT Id, Status FROM dbo.Staging")
            .PhantomColumns.Should().Equal(new PhantomColumnRef("STATUS", "DBO.STAGING"));
    }

    [Fact]
    public void UpdateFromAlias_TargetIsTheFromSource() =>
        ShouldBeClean(
            "UPDATE o SET o.Total = s.Total, Status = 2 OUTPUT inserted.Id, deleted.Total " +
            "FROM dbo.Orders o JOIN dbo.Staging s ON s.Id = o.Id");

    [Fact]
    public void UnionOrderBy_NamesOutputColumns() =>
        ShouldBeClean("SELECT Id AS Key1 FROM dbo.Orders UNION ALL SELECT Id FROM dbo.Customers ORDER BY Key1");

    [Theory]
    [InlineData("SELECT TOP (@n) Id FROM dbo.Orders ORDER BY Id")]
    [InlineData("SELECT Id, ROW_NUMBER() OVER (PARTITION BY CustomerId ORDER BY OrderDate DESC) rn FROM dbo.Orders")]
    [InlineData("SELECT CAST(Total AS decimal(10, 2)), IIF(Status = 1, 'a', 'b'), JSON_VALUE(Code, '$.a') FROM dbo.Orders")]
    [InlineData("SELECT STRING_AGG(Code, ',') WITHIN GROUP (ORDER BY Code) FROM dbo.Orders")]
    [InlineData("SELECT CustomerId, COUNT(DISTINCT Id) FROM dbo.Orders GROUP BY CustomerId HAVING COUNT(*) > 1")]
    [InlineData("DECLARE @t TABLE (Foo int); INSERT @t SELECT Id FROM dbo.Orders; SELECT Foo FROM @t;")]
    [InlineData("SELECT Id, CASE WHEN Total > 0 THEN 1 END AS Flag FROM dbo.Orders")]
    [InlineData("SELECT o.Id FROM dbo.Orders o WHERE o.Id IN (SELECT OrderId FROM sales.Invoices)")]
    [InlineData("SELECT x.Id, a.Amount FROM dbo.Orders AS x CROSS APPLY (SELECT TOP 1 i.Amount FROM sales.Invoices i WHERE i.OrderId = x.Id ORDER BY i.Amount DESC) a")]
    [InlineData("SELECT p.[1], p.[2] FROM (SELECT Status, Total FROM dbo.Orders) s PIVOT (SUM(Total) FOR Status IN ([1], [2])) p")]
    [InlineData("SELECT Id FROM dbo.Orders o ORDER BY o.Total OFFSET 10 ROWS FETCH NEXT 5 ROWS ONLY")]
    [InlineData("WITH XMLNAMESPACES ('urn:x' AS ns) SELECT Id FROM dbo.Orders")]
    [InlineData("SELECT Id INTO #snapshot FROM dbo.Orders")]
    [InlineData("UPDATE TOP (10) dbo.Orders SET Total += 1")]
    [InlineData("SELECT o.Id FROM dbo.Orders o INNER HASH JOIN dbo.Customers c ON c.Id = o.CustomerId")]
    [InlineData("SELECT o.Id FROM dbo.Orders o WHERE o.Id = (SELECT MAX(Id) FROM dbo.Orders)")]
    [InlineData("SELECT dbo.Orders.Total FROM dbo.Orders")]
    [InlineData("EXEC dbo.usp_Report @from = @d")]
    [InlineData("SELECT Id FROM dbo.Orders WHERE OrderDate >= DATEADD(dd, -7, SYSUTCDATETIME()) AND Code LIKE N'A%'")]
    public void CommonTSqlShapes_AreClean(string sql) => ShouldBeClean(sql);

    [Fact]
    public void ParseFailure_ReportsParseFailedAndNoReferences()
    {
        var analysis = Analyze("SELECT o.Id FROM dbo.Ordres o WHERE");

        analysis.ParseFailed.Should().BeTrue();
        analysis.ParseError.Should().NotBeNullOrWhiteSpace();
        analysis.PhantomTables.Should().BeEmpty();
        analysis.PhantomColumns.Should().BeEmpty();
    }

    [Fact]
    public void PlaceholdersInsideLiteralsAndComments_AreUntouched()
    {
        ShouldBeClean(
            "SELECT Id FROM dbo.Orders WHERE Code = ':a ? {0}' AND Status = :status /* :c ? {1} */ -- {2} ?\n" +
            "AND Total > ? AND OrderDate > {0} AND [Code] <> N'x::y'");
    }

    // ---- Rule level ----
    [Fact]
    public async Task PhantomTableRule_WithTSqlAnalyzer_UsesSchemaQualifiedCatalogKeys()
    {
        var rule = new PhantomTableRule(new TSqlPhantomAnalyzer());
        var schema = new DatabaseSchemaDescriptor(
            "schema:dbo",
            new List<DatabaseTableDescriptor> { new("dbo.Orders", new[] { Col("Id"), Col("Total") }) },
            "CHAR");

        var clean = await rule.ValidateAsync(Sql("SELECT o.Id FROM dbo.Orders o"), new ContractDescriptor[] { schema });
        clean.Should().BeEmpty();

        var violations = (await rule.ValidateAsync(Sql("SELECT o.Id FROM dbo.Ordres o"), new ContractDescriptor[] { schema })).ToList();
        var violation = violations.Should().ContainSingle().Subject;
        violation.RuleId.Should().Be("DG015");
        violation.Properties!["table"].Should().Be("DBO.ORDRES");
    }

    [Fact]
    public async Task PhantomColumnRule_WithTSqlAnalyzer_ReportsColumnAndTable()
    {
        var rule = new PhantomColumnRule(new TSqlPhantomAnalyzer());

        var violation = (await rule.ValidateAsync(Sql("SELECT o.Discount FROM dbo.Orders o"), new ContractDescriptor[] { Catalog }))
            .Should().ContainSingle().Subject;
        violation.RuleId.Should().Be("DG016");
        violation.Properties!["column"].Should().Be("DISCOUNT");
        violation.Properties!["table"].Should().Be("DBO.ORDERS");
    }

    [Fact]
    public async Task PhantomRules_WithTSqlAnalyzer_EmitNothingWhenTheSqlDoesNotParse()
    {
        var sql = Sql("SELECT g.Id, g.Nope FROM dbo.Ghosts g WHERE");

        (await new PhantomTableRule(new TSqlPhantomAnalyzer()).ValidateAsync(sql, new ContractDescriptor[] { Catalog })).Should().BeEmpty();
        (await new PhantomColumnRule(new TSqlPhantomAnalyzer()).ValidateAsync(sql, new ContractDescriptor[] { Catalog })).Should().BeEmpty();
    }

    [Fact]
    public async Task ProviderRuleCatalog_UsesTheTSqlAnalyzerOnlyForSqlServer()
    {
        // The tokenizer still reports the phantom in an unparsable statement; the T-SQL analyzer defers to DG019.
        static async Task<int> Dg015CountAsync(string provider)
        {
            var rule = ProviderRuleCatalog.Get(provider).Single(r => r.Rule.RuleId == "DG015").Rule;
            var sql = new RawSqlDescriptor($"raw:{provider}", "SELECT g.Id FROM dbo.Ghosts g WHERE", Array.Empty<ParameterDescriptor>(), Array.Empty<ColumnDescriptor>());
            return (await rule.ValidateAsync(sql, new ContractDescriptor[] { Catalog })).Count();
        }

        (await Dg015CountAsync("sqlserver")).Should().Be(0);
        (await Dg015CountAsync("oracle")).Should().Be(1);
    }
}
