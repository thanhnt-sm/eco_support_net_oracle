using System;
using System.Linq;
using DataGuard.Cli;
using DataGuard.Core.Rules;
using DataGuard.Oracle.Adapter;
using DataGuard.PostgreSql.Adapter;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

public class LiveQuerySchemaProviderTests
{
    [Fact]
    public void OracleLiveQuerySchemaProvider_Constructor_RejectsNull()
    {
        var act = () => new OracleLiveQuerySchemaProvider(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void PostgreSqlLiveQuerySchemaProvider_Constructor_RejectsNull()
    {
        var act = () => new PostgreSqlLiveQuerySchemaProvider(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ProviderRuleCatalog_WiresOracleLiveQueryProvider_WhenConnectionSupplied()
    {
        var rules = ProviderRuleCatalog.Get("oracle", "Data Source=test;User Id=u;Password=p;");
        var liveRuleReg = rules.FirstOrDefault(r => r.Rule is LiveSqlShapeValidationRule);

        liveRuleReg.Should().NotBeNull();
    }

    [Fact]
    public void ProviderRuleCatalog_WiresPostgreSqlLiveQueryProvider_WhenConnectionSupplied()
    {
        var rules = ProviderRuleCatalog.Get("postgresql", "Host=localhost;Database=test;");
        var liveRuleReg = rules.FirstOrDefault(r => r.Rule is LiveSqlShapeValidationRule);
        liveRuleReg.Should().NotBeNull();
        liveRuleReg!.Availability.Should().Be(RuleAvailability.Ready);
    }

    [Fact]
    public async Task QuerySchemaProvider_StackedQueriesRejected_ButLiteralsWithSemicolonAllowed()
    {
        var oracle = new OracleLiveQuerySchemaProvider("Data Source=test;User Id=u;Password=p;");
        var stacked = await oracle.DescribeResultSetAsync("SELECT 1 FROM dual; DROP TABLE users;", default);
        stacked.Should().BeEmpty();

        var pg = new PostgreSqlLiveQuerySchemaProvider("Host=localhost;Database=test;");
        var pgStacked = await pg.DescribeResultSetAsync("SELECT 1; DROP TABLE users;", default);
        pgStacked.Should().BeEmpty();

        // Semicolon inside literal enters try/catch and returns empty because no live DB is reachable,
        // instead of early reject.
        var withLiteral = await oracle.DescribeResultSetAsync("SELECT 'hello;world' FROM dual", default);
        withLiteral.Should().BeEmpty();
    }
}
