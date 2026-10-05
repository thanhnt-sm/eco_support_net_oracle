using DataGuard.Cli;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules;
using DataGuard.Core.Rules.Sql;
using DataGuard.SqlServer.Adapter;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

/// <summary>
/// DG019 receives T-SQL parse failures for <c>sqlserver</c> through the injected <see cref="ISqlStatementParser"/>
/// (red-team A1/R33: the ScriptDOM parser lives in the SQL Server adapter, not in Core).
/// </summary>
public class RawSqlParseStatusRuleTests
{
    private const string InvalidTSql = "SELECT Id, Name FROM dbo.Orders WHERE";
    private const string ValidTSql = "SELECT o.Id, o.Total AS Amount FROM dbo.Orders o WHERE o.CustomerId = @customerId";

    [Fact]
    public async Task ProviderCatalog_SqlServer_InvalidTSql_ReportsDg019()
    {
        var violations = await RunCatalogRuleAsync("sqlserver", Raw(InvalidTSql));

        var finding = violations.Should().ContainSingle().Subject;
        finding.RuleId.Should().Be("DG019");
        finding.Severity.Should().Be(Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        finding.Message.Should().StartWith("Raw SQL could not be parsed: Line 1");
    }

    [Fact]
    public async Task ProviderCatalog_SqlServer_ValidTSql_ReportsNothing()
    {
        (await RunCatalogRuleAsync("sqlserver", Raw(ValidTSql))).Should().BeEmpty();
    }

    [Theory]
    [InlineData("oracle")]
    [InlineData("postgresql")]
    [InlineData("mysql")]
    public async Task ProviderCatalog_OtherProviders_HaveNoParser_AndReportNothing(string provider)
    {
        (await RunCatalogRuleAsync(provider, Raw(InvalidTSql))).Should().BeEmpty();
    }

    [Theory]
    [InlineData("SELECT Id FROM dbo.Orders WHERE Id = :id")]
    [InlineData("SELECT Id FROM dbo.Orders WHERE Id = ?")]
    [InlineData("SELECT Id FROM dbo.Orders WHERE Id = {0}")]
    public async Task SqlServerParser_AcceptsClientPlaceholders(string sql)
    {
        (await new RawSqlParseStatusRule(TSqlStatementParser.Instance).ValidateAsync(Raw(sql), Array.Empty<ContractDescriptor>()))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task SqlServerParser_SkipsStoredProcedureCallsAndOtherProviderHints()
    {
        var rule = new RawSqlParseStatusRule(TSqlStatementParser.Instance);
        var procedureCall = Raw("dbo.usp_GetOrders") with { IsStoredProcedure = true, ProcedureName = "usp_GetOrders" };
        var oracleHinted = Raw("SELECT * FROM orders WHERE ROWNUM <= 10 CONNECT BY PRIOR id = parent_id") with { ConnectionProviderHint = "oracle" };

        (await rule.ValidateAsync(procedureCall, Array.Empty<ContractDescriptor>())).Should().BeEmpty();
        (await rule.ValidateAsync(oracleHinted, Array.Empty<ContractDescriptor>())).Should().BeEmpty();
        (await rule.ValidateAsync(Raw(InvalidTSql) with { ConnectionProviderHint = "sqlserver" }, Array.Empty<ContractDescriptor>()))
            .Should().ContainSingle().Which.RuleId.Should().Be("DG019");
    }

    [Fact]
    public async Task WithoutParser_OnlyAcquisitionStatusIsReported()
    {
        var acquisitionInvalid = Raw("SELECT FROM") with { ParseStatus = RawSqlParseStatus.Invalid, ParseError = "Incorrect syntax near 'FROM'." };

        (await new RawSqlParseStatusRule().ValidateAsync(Raw(InvalidTSql), Array.Empty<ContractDescriptor>())).Should().BeEmpty();
        (await new RawSqlParseStatusRule(NoOpSqlStatementParser.Instance).ValidateAsync(Raw(InvalidTSql), Array.Empty<ContractDescriptor>())).Should().BeEmpty();
        (await new RawSqlParseStatusRule().ValidateAsync(acquisitionInvalid, Array.Empty<ContractDescriptor>()))
            .Should().ContainSingle().Which.Message.Should().Contain("Incorrect syntax near 'FROM'.");
    }

    [Fact]
    public void TSqlStatementParser_ReturnsDeclaredParametersAndSelectColumns()
    {
        var procedure = TSqlStatementParser.Instance.Parse("CREATE PROCEDURE dbo.p @Id int, @Name nvarchar(50) AS SELECT 1");
        procedure.IsValid.Should().BeTrue();
        procedure.Parameters.Select(p => (p.Name, p.DataType, p.MaxLength)).Should().Equal(("@Id", "int", (int?)null), ("@Name", "nvarchar(50)", (int?)50));

        var query = TSqlStatementParser.Instance.Parse(ValidTSql);
        query.IsValid.Should().BeTrue();
        query.Error.Should().BeNull();
        query.Columns.Select(c => c.Name).Should().Equal("Id", "Amount");

        var invalid = TSqlStatementParser.Instance.Parse(InvalidTSql);
        invalid.IsValid.Should().BeFalse();
        invalid.Error.Should().NotBeNullOrWhiteSpace();
        NoOpSqlStatementParser.Instance.Parse(InvalidTSql).IsValid.Should().BeTrue();
    }

    private static RawSqlDescriptor Raw(string sql) =>
        new("raw:" + sql.GetHashCode(StringComparison.Ordinal), sql, Array.Empty<ParameterDescriptor>(), Array.Empty<ColumnDescriptor>());

    private static async Task<IReadOnlyList<ContractViolation>> RunCatalogRuleAsync(string provider, RawSqlDescriptor sql)
    {
        var rule = ProviderRuleCatalog.Get(provider).Single(r => r.Rule.RuleId == "DG019").Rule;
        return await rule.ValidateAsync(sql, new ContractDescriptor[] { sql });
    }
}
