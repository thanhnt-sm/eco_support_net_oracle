using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DataGuard.Core.Tests;

public class PhantomIdentifierRuleTests
{
    private static DatabaseSchemaDescriptor CreateSchema()
    {
        var tables = new List<DatabaseTableDescriptor>
        {
            new ("CUSTOMERS", new List<ColumnDescriptor>
            {
                new ("ID", "NUMBER", null, null, 22, false, null),
                new ("NAME", "VARCHAR2", 100, null, null, true, null),
            }),
            new ("ORDERS", new List<ColumnDescriptor>
            {
                new ("ID", "NUMBER", null, null, 22, false, null),
                new ("CUSTOMER_ID", "NUMBER", null, null, 22, false, null),
                new ("TOTAL", "NUMBER", null, null, 22, false, null),
            }),
        };

        return new DatabaseSchemaDescriptor("schema:1", tables, "CHAR");
    }

    [Fact]
    public async Task ValidateAsync_PhantomTable_ReportsDG015()
    {
        var rule = new PhantomIdentifierRule();
        var rawSql = new RawSqlDescriptor("raw:1", "SELECT * FROM NON_EXISTENT_TABLE", Array.Empty<ParameterDescriptor>(), Array.Empty<ColumnDescriptor>());
        var schema = CreateSchema();

        var violations = await rule.ValidateAsync(rawSql, new ContractDescriptor[] { schema });

        violations.Should().ContainSingle(v => v.RuleId == "DG015");
    }

    [Fact]
    public async Task ValidateAsync_PhantomColumn_ReportsDG016()
    {
        var rule = new PhantomIdentifierRule();
        var rawSql = new RawSqlDescriptor("raw:1", "SELECT c.NON_EXISTENT_COL FROM CUSTOMERS c", Array.Empty<ParameterDescriptor>(), Array.Empty<ColumnDescriptor>());
        var schema = CreateSchema();

        var violations = await rule.ValidateAsync(rawSql, new ContractDescriptor[] { schema });

        violations.Should().Contain(v => v.RuleId == "DG016");
    }

    [Fact]
    public async Task ValidateAsync_CteTable_NotReportedAsPhantom()
    {
        var rule = new PhantomIdentifierRule();
        var rawSql = new RawSqlDescriptor("raw:1", "WITH TempCte AS (SELECT ID FROM CUSTOMERS) SELECT * FROM TempCte", Array.Empty<ParameterDescriptor>(), Array.Empty<ColumnDescriptor>());
        var schema = CreateSchema();

        var violations = await rule.ValidateAsync(rawSql, new ContractDescriptor[] { schema });

        violations.Where(v => v.RuleId == "DG015").Should().BeEmpty();
    }
}
