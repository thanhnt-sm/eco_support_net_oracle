using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules;
using DataGuard.Core.Rules.Sql;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

/// <summary>
/// DG015 (<see cref="PhantomTableRule"/>) and DG016 (<see cref="PhantomColumnRule"/>): C3 key normalization and the
/// H10 false-positive family. Every negative case runs both rules and must produce no finding.
/// </summary>
public class PhantomRulesTests
{
    private static ColumnDescriptor Col(string name, bool nullable = false) => new(name, "NUMBER", null, null, null, nullable, null);

    /// <summary>Oracle-style bare keys plus a SQL Server-style <c>dbo.Invoices</c> key (as SqlServerParsers emits).</summary>
    private static DatabaseSchemaDescriptor Schema() => new(
        "schema:1",
        new List<DatabaseTableDescriptor>
        {
            new("CUSTOMERS", new[] { Col("ID"), Col("NAME"), Col("EMAIL") }),
            new("ORDERS", new[] { Col("ID"), Col("CUSTOMER_ID"), Col("TOTAL"), Col("ORDER_DATE") }),
            new("dbo.Invoices", new[] { Col("Id"), Col("OrderId"), Col("Amount") }),
        },
        "CHAR");

    private static RawSqlDescriptor Sql(string text) =>
        new("raw:1", text, Array.Empty<ParameterDescriptor>(), Array.Empty<ColumnDescriptor>());

    private static async Task<List<ContractViolation>> RunAsync(IContractRule rule, string sql, DatabaseSchemaDescriptor? schema = null)
    {
        var violations = (await rule.ValidateAsync(Sql(sql), new ContractDescriptor[] { schema ?? Schema() })).ToList();
        violations.Where(v => v.RuleId != rule.RuleId).Should().BeEmpty("every violation must carry its own rule's id");
        return violations;
    }

    private static async Task<List<ContractViolation>> RunBothAsync(string sql, DatabaseSchemaDescriptor? schema = null)
    {
        var all = await RunAsync(new PhantomTableRule(), sql, schema);
        all.AddRange(await RunAsync(new PhantomColumnRule(), sql, schema));
        return all;
    }

    // ---- Rule identity ----
    [Fact]
    public void Rules_HaveDistinctIds()
    {
        new PhantomTableRule().RuleId.Should().Be("DG015");
        new PhantomColumnRule().RuleId.Should().Be("DG016");
        new RawSqlParseStatusRule().RuleId.Should().Be("DG019");
    }

    // ---- Positive cases ----
    [Fact]
    public async Task SchemaQualifiedPhantomTable_ReportsDG015()
    {
        var violations = await RunAsync(new PhantomTableRule(), "SELECT g.Id FROM dbo.Ghosts g");

        var violation = violations.Should().ContainSingle().Subject;
        violation.Message.Should().Be("Table 'DBO.GHOSTS' does not exist in database");
        violation.Properties!["table"].Should().Be("DBO.GHOSTS");
    }

    [Fact]
    public async Task BarePhantomTable_ReportsDG015()
    {
        var violations = await RunAsync(new PhantomTableRule(), "SELECT * FROM ORDERS_SUMMARY WHERE CUSTOMER_ID = :custId");

        violations.Should().ContainSingle().Which.Message.Should().Contain("'ORDERS_SUMMARY'");
    }

    [Fact]
    public async Task PhantomTable_DoesNotEmitColumnFindingsForIt()
    {
        var violations = await RunAsync(new PhantomColumnRule(), "SELECT g.Anything, Whatever FROM Ghosts g");

        violations.Should().BeEmpty();
    }

    [Fact]
    public async Task QualifiedPhantomColumn_ReportsDG016()
    {
        var violations = await RunAsync(new PhantomColumnRule(), "SELECT c.ID, c.ADDRESS FROM CUSTOMERS c");

        var violation = violations.Should().ContainSingle().Subject;
        violation.Message.Should().Be("Column 'ADDRESS' does not exist in table 'CUSTOMERS'");
        violation.Properties!["column"].Should().Be("ADDRESS");
        violation.Properties!["table"].Should().Be("CUSTOMERS");
    }

    [Fact]
    public async Task QualifiedPhantomColumn_InWhereClauseOfJoin_ReportsDG016()
    {
        var violations = await RunAsync(
            new PhantomColumnRule(),
            "SELECT o.ID, c.NAME FROM ORDERS o JOIN CUSTOMERS c ON o.CUSTOMER_ID = c.ID WHERE o.STATUS = 'X'");

        violations.Should().ContainSingle().Which.Message.Should().Be("Column 'STATUS' does not exist in table 'ORDERS'");
    }

    [Fact]
    public async Task QualifiedPhantomColumn_OnSchemaQualifiedCatalogKey_ReportsDG016()
    {
        var violations = await RunAsync(new PhantomColumnRule(), "SELECT i.Id, i.Discount FROM dbo.Invoices i");

        violations.Should().ContainSingle().Which.Message.Should().Be("Column 'DISCOUNT' does not exist in table 'DBO.INVOICES'");
    }

    [Fact]
    public async Task UnqualifiedPhantomColumn_ReportsDG016()
    {
        var violations = await RunAsync(new PhantomColumnRule(), "select id,\n  address\nfrom customers where id = :id");

        violations.Should().ContainSingle().Which.Message.Should().Be("Column 'ADDRESS' does not exist in table 'CUSTOMERS'");
    }

    [Fact]
    public async Task UnqualifiedPhantomColumn_WithJoin_ChecksUnionOfTables()
    {
        var violations = await RunAsync(
            new PhantomColumnRule(),
            "SELECT NAME, TOTAL, ADDRESS FROM CUSTOMERS c JOIN ORDERS o ON c.ID = o.CUSTOMER_ID");

        var violation = violations.Should().ContainSingle().Subject;
        violation.Properties!["column"].Should().Be("ADDRESS");
        violation.Properties!["table"].Should().Be("CUSTOMERS, ORDERS");
    }

    [Fact]
    public async Task PhantomColumn_DuplicateReferences_ReportedOnce()
    {
        var violations = await RunAsync(new PhantomColumnRule(), "SELECT c.GHOST FROM CUSTOMERS c WHERE c.GHOST = 1 ORDER BY c.GHOST");

        violations.Should().ContainSingle();
    }

    // ---- C3: catalog keyed by schema.name ----
    [Theory]
    [InlineData("SELECT i.Id, i.Amount FROM dbo.Invoices i")]
    [InlineData("SELECT i.Id, i.Amount FROM [dbo].[Invoices] i")]
    [InlineData("SELECT \"i\".\"Id\" FROM \"dbo\".\"Invoices\" \"i\"")]
    [InlineData("SELECT Id, Amount FROM Invoices")]
    [InlineData("SELECT invoices.amount FROM INVOICES")]
    [InlineData("SELECT i.Id FROM sales.Invoices i")]
    public async Task SchemaKeyedCatalog_ExistingTable_NotFlagged(string sql)
    {
        (await RunBothAsync(sql)).Should().BeEmpty();
    }

    // ---- H10: false-positive family ----
    [Theory]
    [InlineData("WITH a AS (SELECT ID FROM CUSTOMERS), b AS (SELECT ID, TOTAL FROM ORDERS) SELECT a.ID, b.TOTAL, b.FOO FROM a JOIN b ON a.ID = b.ID")]
    [InlineData("WITH a (X) AS (SELECT ID FROM CUSTOMERS), b AS MATERIALIZED (SELECT ID FROM ORDERS) SELECT X FROM a, b")]
    [InlineData("WITH RECURSIVE tree (ID, LVL) AS (SELECT ID, 1 FROM CUSTOMERS UNION ALL SELECT c.ID, t.LVL + 1 FROM CUSTOMERS c JOIN tree t ON c.ID = t.ID) SELECT ID, LVL FROM tree")]
    [InlineData("SELECT SYSDATE FROM DUAL")]
    [InlineData("SELECT 1 FROM SYS.DUAL")]
    [InlineData("SELECT EXTRACT(YEAR FROM o.ORDER_DATE) AS yr FROM ORDERS o")]
    [InlineData("SELECT EXTRACT(MONTH FROM ORDER_DATE) FROM ORDERS")]
    [InlineData("SELECT TRIM(LEADING '0' FROM c.NAME) AS n FROM CUSTOMERS c")]
    [InlineData("SELECT TRIM(BOTH FROM NAME) FROM CUSTOMERS")]
    [InlineData("SELECT c.ID FROM CUSTOMERS c WHERE c.NAME IS DISTINCT FROM c.EMAIL")]
    [InlineData("SELECT c.ID FROM CUSTOMERS c WHERE c.NAME IS NOT DISTINCT FROM c.EMAIL")]
    [InlineData("SELECT f.Value FROM dbo.fn_Split(@list, ',') f JOIN CUSTOMERS c ON c.ID = f.Value")]
    [InlineData("SELECT g FROM generate_series(1, 10) AS g")]
    [InlineData("SELECT t.COLUMN_VALUE FROM TABLE(pkg.get_ids(:p)) t")]
    [InlineData("SELECT t.Anything, Whatever FROM #Staging t")]
    [InlineData("SELECT Anything FROM ##GlobalStaging")]
    [InlineData("SELECT p.Id, o.TOTAL FROM @ids p JOIN ORDERS o ON o.ID = p.Id")]
    [InlineData("SELECT x.Col FROM OtherDb.dbo.Things x")]
    [InlineData("SELECT x.Col FROM OtherDb..Things x")]
    [InlineData("SELECT r.Col FROM REMOTE_ORDERS@prod_link r")]
    [InlineData("SELECT name, object_id FROM sys.tables")]
    [InlineData("SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES")]
    [InlineData("SELECT relname FROM pg_catalog.pg_class")]
    [InlineData("SELECT ID -- pulled FROM GHOST_TABLE g, g.NOPE\nFROM CUSTOMERS /* JOIN PHANTOM p ON p.X = 1 */")]
    [InlineData("SELECT ID FROM CUSTOMERS WHERE NAME = 'copied FROM GHOST g JOIN x.y'")]
    [InlineData("SELECT ID FROM CUSTOMERS WHERE NAME = N'it''s FROM GHOST'")]
    [InlineData("SELECT COALESCE(NAME, EMAIL) AS DISPLAY_NAME FROM CUSTOMERS")]
    [InlineData("SELECT COALESCE(c.NAME, c.EMAIL) DISPLAY_NAME FROM CUSTOMERS c")]
    [InlineData("SELECT NAME CUSTOMER_NAME, COUNT(*) TOTAL_ORDERS FROM CUSTOMERS GROUP BY NAME")]
    [InlineData("SELECT c.NAME AS LABEL, CASE WHEN c.ID > 1 THEN 'a' ELSE 'b' END BUCKET FROM CUSTOMERS c")]
    [InlineData("SELECT NAME, TOTAL FROM CUSTOMERS c JOIN ORDERS o ON c.ID = o.CUSTOMER_ID")]
    [InlineData("SELECT NAME, TOTAL FROM CUSTOMERS, ORDERS WHERE CUSTOMERS.ID = ORDERS.CUSTOMER_ID")]
    [InlineData("SELECT o.TOTAL FROM ORDERS o WHERE EXISTS (SELECT 1 FROM CUSTOMERS o WHERE o.NAME = 'x')")]
    [InlineData("SELECT o.TOTAL, (SELECT c.NAME FROM CUSTOMERS c WHERE c.ID = o.CUSTOMER_ID) AS CNAME FROM ORDERS o")]
    [InlineData("SELECT d.N FROM (SELECT NAME AS N FROM CUSTOMERS) d")]
    [InlineData("SELECT o.ID FROM ORDERS o WITH (NOLOCK) WHERE o.TOTAL > 0")]
    [InlineData("SELECT DISTINCT TOP 10 NAME FROM CUSTOMERS")]
    [InlineData("SELECT ID FROM CUSTOMERS UNION SELECT TOTAL FROM ORDERS")]
    [InlineData("SELECT c.ID FROM CUSTOMERS c UNION ALL SELECT c.TOTAL FROM ORDERS c")]
    [InlineData("SELECT ROWNUM, NAME FROM CUSTOMERS WHERE ROWNUM <= 10")]
    [InlineData("SELECT seq_orders.NEXTVAL FROM DUAL")]
    [InlineData("SELECT o.*, c.* FROM ORDERS o JOIN CUSTOMERS c ON c.ID = o.CUSTOMER_ID")]
    [InlineData("SELECT ID::text FROM CUSTOMERS")]
    [InlineData("SELECT ID FROM CUSTOMERS WHERE NAME = $tag$ FROM GHOST $tag$")]
    [InlineData("EXEC dbo.GetCustomer @Id")]
    public async Task FalsePositiveFamily_NotFlagged(string sql)
    {
        var violations = await RunBothAsync(sql);

        violations.Should().BeEmpty(string.Join("; ", violations.Select(v => $"{v.RuleId}: {v.Message}")));
    }

    [Fact]
    public async Task PhantomInsideComment_IsMasked_ButRealPhantomStillReported()
    {
        var violations = await RunAsync(new PhantomTableRule(), "SELECT ID /* FROM NOT_A_TABLE */ FROM GHOST");

        violations.Should().ContainSingle().Which.Message.Should().Contain("'GHOST'");
    }

    [Fact]
    public async Task CteWithSameAliasAsMissingTable_OnlyMissingTableReported()
    {
        var violations = await RunAsync(
            new PhantomTableRule(),
            "WITH a AS (SELECT ID FROM CUSTOMERS), b AS (SELECT ID FROM MISSING_T) SELECT a.ID FROM a JOIN b ON a.ID = b.ID");

        violations.Should().ContainSingle().Which.Message.Should().Contain("'MISSING_T'");
    }

    [Fact]
    public async Task NoSchema_NoFindings()
    {
        var sql = Sql("SELECT x FROM GHOST");

        (await new PhantomTableRule().ValidateAsync(sql, new ContractDescriptor[] { sql })).Should().BeEmpty();
        (await new PhantomColumnRule().ValidateAsync(sql, new ContractDescriptor[] { sql })).Should().BeEmpty();
    }

    // ---- SchemaObjectName ----
    [Theory]
    [InlineData("Orders", null, null, "Orders")]
    [InlineData("dbo.Orders", null, "dbo", "Orders")]
    [InlineData("[dbo].[Order Details]", null, "dbo", "Order Details")]
    [InlineData("\"HR\".\"EMPLOYEES\"", null, "HR", "EMPLOYEES")]
    [InlineData("`shop`.`orders`", null, "shop", "orders")]
    [InlineData("Sales.dbo.Orders", "Sales", "dbo", "Orders")]
    [InlineData("Sales..Orders", "Sales", null, "Orders")]
    [InlineData("[a.b].[c]", null, "a.b", "c")]
    public void SchemaObjectName_Parse_HandlesQuotingAndParts(string raw, string? database, string? schema, string name)
    {
        var parts = SchemaObjectName.Parse(raw);

        parts.Database.Should().Be(database);
        parts.Schema.Should().Be(schema);
        parts.Name.Should().Be(name);
    }

    [Fact]
    public void SchemaObjectName_Canonical_FoldsCaseAndQuotes()
    {
        SchemaObjectName.Canonical("[Orders]").Should().Be("ORDERS");
        SchemaObjectName.Canonical("oracle", "\"orders\"").Should().Be("ORDERS");
        SchemaObjectName.Key(null, "dbo", "Orders").Should().Be("DBO.ORDERS");
        SchemaObjectName.Key(null, null, "Orders").Should().Be("ORDERS");
    }
}
