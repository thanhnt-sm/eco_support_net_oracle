using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Reporting;
using DataGuard.Core.Rules;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DataGuard.Core.Tests;

public class LiveSqlShapeValidationTests
{
    private sealed class MockLiveQuerySchemaProvider : ILiveQuerySchemaProvider
    {
        private readonly Func<string, IReadOnlyList<ColumnDescriptor>> _handler;

        public MockLiveQuerySchemaProvider(Func<string, IReadOnlyList<ColumnDescriptor>> handler)
        {
            _handler = handler;
        }

        public Task<IReadOnlyList<ColumnDescriptor>> DescribeResultSetAsync(string sqlText, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(sqlText));
        }
    }

    private sealed class FailingLiveQuerySchemaProvider : ILiveQuerySchemaProvider
    {
        private readonly Exception _exception;

        public FailingLiveQuerySchemaProvider(Exception exception)
        {
            _exception = exception;
        }

        public Task<IReadOnlyList<ColumnDescriptor>> DescribeResultSetAsync(string sqlText, CancellationToken cancellationToken)
        {
            throw _exception;
        }
    }

    [Fact]
    public async Task ValidateAsync_MatchingColumns_NoViolations()
    {
        var mockProvider = new MockLiveQuerySchemaProvider(_ => new List<ColumnDescriptor>
        {
            new ("Id", "int", null, null, null, false, null),
            new ("Name", "nvarchar", 100, null, null, true, null),
            new ("customer_email", "nvarchar", 200, null, null, true, null),
        });

        var rule = new LiveSqlShapeValidationRule(schemaProvider: mockProvider);

        var expectedProps = new List<PropertyDescriptor>
        {
            new ("Id", "int", null, null, false, null, true, false),
            new ("Name", "string", null, null, true, 100, false, false),
            new ("Email", "string", "customer_email", null, true, 200, false, false),
        };

        var rawSql = new RawSqlDescriptor(
            Id: "raw:1",
            SqlText: "SELECT Id, Name, customer_email FROM Customers",
            Parameters: Array.Empty<ParameterDescriptor>(),
            ResultColumns: Array.Empty<ColumnDescriptor>(),
            ExpectedProperties: expectedProps,
            TargetTypeName: "Customer");

        var violations = await rule.ValidateAsync(rawSql, new ContractDescriptor[] { rawSql });

        violations.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_MissingRequiredColumn_ReportsDG004()
    {
        // DB only returns Id and Name, but C# class expects Id, Name, Age
        var mockProvider = new MockLiveQuerySchemaProvider(_ => new List<ColumnDescriptor>
        {
            new ("Id", "int", null, null, null, false, null),
            new ("Name", "nvarchar", 100, null, null, true, null),
        });

        var rule = new LiveSqlShapeValidationRule(schemaProvider: mockProvider);

        var expectedProps = new List<PropertyDescriptor>
        {
            new ("Id", "int", null, null, false, null, true, false),
            new ("Name", "string", null, null, true, 100, false, false),
            new ("Age", "int", null, null, false, null, false, false),
        };

        var rawSql = new RawSqlDescriptor(
            Id: "raw:1",
            SqlText: "SELECT Id, Name FROM Customers",
            Parameters: Array.Empty<ParameterDescriptor>(),
            ResultColumns: Array.Empty<ColumnDescriptor>(),
            ExpectedProperties: expectedProps,
            TargetTypeName: "Customer");

        var violations = await rule.ValidateAsync(rawSql, new ContractDescriptor[] { rawSql });

        violations.Should().ContainSingle(v => v.RuleId == "DG018");
        var violation = violations.Single();
        violation.Severity.Should().Be(DiagnosticSeverity.Error);
        violation.Message.Should().Contain("missing expected column 'Age'");
        violation.Message.Should().Contain("Customer");
    }

    [Fact]
    public async Task ValidateAsync_TypeMismatch_ReportsDG004()
    {
        // DB returns Id as nvarchar, but C# property is int
        var mockProvider = new MockLiveQuerySchemaProvider(_ => new List<ColumnDescriptor>
        {
            new ("Id", "nvarchar", 50, null, null, false, null),
        });

        var rule = new LiveSqlShapeValidationRule(schemaProvider: mockProvider);

        var expectedProps = new List<PropertyDescriptor>
        {
            new ("Id", "int", null, null, false, null, true, false),
        };

        var rawSql = new RawSqlDescriptor(
            Id: "raw:1",
            SqlText: "SELECT Id FROM Customers",
            Parameters: Array.Empty<ParameterDescriptor>(),
            ResultColumns: Array.Empty<ColumnDescriptor>(),
            ExpectedProperties: expectedProps,
            TargetTypeName: "Customer");

        var violations = await rule.ValidateAsync(rawSql, new ContractDescriptor[] { rawSql });

        violations.Should().ContainSingle(v => v.RuleId == "DG018");
        var violation = violations.Single();
        violation.Severity.Should().Be(DiagnosticSeverity.Error);
        violation.Message.Should().Contain("database type 'nvarchar' which is not compatible with C# property 'Id' of type 'int'");
    }

    [Fact]
    public async Task ValidateAsync_ExtraColumns_ReportsWarning()
    {
        // DB returns extra unmapped columns
        var mockProvider = new MockLiveQuerySchemaProvider(_ => new List<ColumnDescriptor>
        {
            new ("Id", "int", null, null, null, false, null),
            new ("Col1", "nvarchar", null, null, null, true, null),
            new ("Col2", "nvarchar", null, null, null, true, null),
            new ("Col3", "nvarchar", null, null, null, true, null),
        });

        var rule = new LiveSqlShapeValidationRule(schemaProvider: mockProvider);

        var expectedProps = new List<PropertyDescriptor>
        {
            new ("Id", "int", null, null, false, null, true, false),
        };

        var rawSql = new RawSqlDescriptor(
            Id: "raw:1",
            SqlText: "SELECT Id, Col1, Col2, Col3 FROM Customers",
            Parameters: Array.Empty<ParameterDescriptor>(),
            ResultColumns: Array.Empty<ColumnDescriptor>(),
            ExpectedProperties: expectedProps,
            TargetTypeName: "Customer");

        var violations = await rule.ValidateAsync(rawSql, new ContractDescriptor[] { rawSql });

        violations.Should().ContainSingle(v => v.RuleId == "DG018" && v.Severity == DiagnosticSeverity.Warning);
        violations.Single().Message.Should().Contain("extra column(s)");
    }

    [Fact]
    public async Task ValidateAsync_TempTableOrDescribeFailure_ReportsDG020Warning()
    {
        var failingProvider = new FailingLiveQuerySchemaProvider(
            new InvalidOperationException("The metadata could not be determined because statement '#temp' in procedure or batch is not supported."));

        var rule = new LiveSqlShapeValidationRule(schemaProvider: failingProvider);

        var expectedProps = new List<PropertyDescriptor>
        {
            new ("Id", "int", null, null, false, null, true, false),
        };

        var rawSql = new RawSqlDescriptor(
            Id: "raw:1",
            SqlText: "SELECT * INTO #temp FROM Customers; SELECT * FROM #temp;",
            Parameters: Array.Empty<ParameterDescriptor>(),
            ResultColumns: Array.Empty<ColumnDescriptor>(),
            ExpectedProperties: expectedProps,
            TargetTypeName: "Customer");

        var violations = await rule.ValidateAsync(rawSql, new ContractDescriptor[] { rawSql });

        violations.Should().ContainSingle(v => v.RuleId == "DG020");
        var violation = violations.Single();
        violation.Severity.Should().Be(DiagnosticSeverity.Warning);
        violation.Message.Should().Contain("Cannot determine result set shape for query");
        violation.Message.Should().Contain("#temp");
    }

    [Fact]
    public async Task ValidateAsync_EmitsProgressEvent()
    {
        var mockProvider = new MockLiveQuerySchemaProvider(_ => new List<ColumnDescriptor>
        {
            new ("Id", "int", null, null, null, false, null),
        });

        using var stringWriter = new StringWriter();
        var progress = new ProgressEmitter(stringWriter, enabled: true);

        var rule = new LiveSqlShapeValidationRule(schemaProvider: mockProvider, progress: progress);

        var expectedProps = new List<PropertyDescriptor>
        {
            new ("Id", "int", null, null, false, null, true, false),
        };

        var rawSql = new RawSqlDescriptor(
            Id: "raw:1",
            SqlText: "SELECT Id FROM Customers",
            Parameters: Array.Empty<ParameterDescriptor>(),
            ResultColumns: Array.Empty<ColumnDescriptor>(),
            ExpectedProperties: expectedProps,
            TargetTypeName: "Customer");

        await rule.ValidateAsync(rawSql, new ContractDescriptor[] { rawSql });

        var output = stringWriter.ToString();
        output.Should().Contain("RuleExecuted");
        output.Should().Contain("Validating query shape against DB schema");
    }
}
