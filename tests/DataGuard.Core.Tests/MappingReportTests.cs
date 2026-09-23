using System;
using System.Collections.Generic;
using System.Linq;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Reporting;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

public class MappingReportTests
{
    [Fact]
    public void IsNameMatch_MatchesExactAndSnakeCase()
    {
        MappingTraceEngine.IsNameMatch("CUSTOMER_ID", "CustomerId").Should().BeTrue();
        MappingTraceEngine.IsNameMatch("email", "Email").Should().BeTrue();
        MappingTraceEngine.IsNameMatch("phone_number", "PhoneNumber").Should().BeTrue();
        MappingTraceEngine.IsNameMatch("Id", "Other").Should().BeFalse();
    }

    [Fact]
    public void Trace_MatchesColumnsAndDetectsDiscrepancies()
    {
        var rawSql = new RawSqlDescriptor(
            Id: "q1",
            SqlText: "SELECT CUSTOMER_ID, FULL_NAME, EMAIL, EXTRA_COL FROM CUSTOMERS",
            Parameters: Array.Empty<ParameterDescriptor>(),
            ResultColumns: Array.Empty<ColumnDescriptor>(),
            ExpectedProperties: new List<PropertyDescriptor>
            {
                new("CustomerId", "int", null, null, false, null, false, false),
                new("FullName", "string", null, null, false, null, false, false),
                new("Email", "string", null, null, true, null, false, false),
                new("PhoneNo", "string", null, null, true, null, false, false),
            },
            TargetTypeName: "Customer",
            OperationType: SqlOperationType.Read,
            ReferencedTables: new[] { "CUSTOMERS" });

        var evidence = MappingTraceEngine.Trace(rawSql);

        evidence.TargetTypeName.Should().Be("Customer");
        evidence.SqlColumns.Should().BeEquivalentTo(new[] { "CUSTOMER_ID", "FULL_NAME", "EMAIL", "EXTRA_COL" });
        evidence.UnmappedColumns.Should().Contain("EXTRA_COL");
        evidence.UnmappedProperties.Should().Contain("PhoneNo");

        var matchedPairs = evidence.Mappings.Where(m => m.IsMatched).ToList();
        matchedPairs.Should().HaveCount(3);
        matchedPairs.Should().Contain(m => m.ColumnName == "CUSTOMER_ID" && m.PropertyName == "CustomerId");
    }

    [Fact]
    public void ScanSummary_SerializesToJsonSafely()
    {
        var summary = new ScanSummary(
            FilesScanned: 10,
            QueriesFound: 2,
            ConnectionsFound: 1,
            ViolationsCount: 1,
            Connections: new List<Sources.ConnectionInfo>
            {
                new("Default", "sqlserver", "Server=localhost;Password=***"),
            },
            Mappings: Array.Empty<MappingEvidence>());

        var json = summary.ToJson();
        json.Should().Contain("\"filesScanned\": 10");
        json.Should().Contain("\"queriesFound\": 2");
        json.Should().Contain("Password=***");
    }
}
