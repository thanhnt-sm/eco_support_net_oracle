using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Reporting;
using DataGuard.Core.Rules;
using DataGuard.Core.Validation;
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

        public Task<LiveSchemaResult> DescribeResultSetAsync(string sqlText, CancellationToken cancellationToken)
        {
            return Task.FromResult(LiveSchemaResult.FromColumns(_handler(sqlText)));
        }
    }

    private sealed class FixedResultProvider : ILiveQuerySchemaProvider
    {
        private readonly LiveSchemaResult _result;

        public FixedResultProvider(LiveSchemaResult result)
        {
            _result = result;
        }

        public Task<LiveSchemaResult> DescribeResultSetAsync(string sqlText, CancellationToken cancellationToken) => Task.FromResult(_result);
    }

    private sealed class FailingLiveQuerySchemaProvider : ILiveQuerySchemaProvider
    {
        private readonly Exception _exception;

        public FailingLiveQuerySchemaProvider(Exception exception)
        {
            _exception = exception;
        }

        public Task<LiveSchemaResult> DescribeResultSetAsync(string sqlText, CancellationToken cancellationToken)
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

    private static RawSqlDescriptor TypedQuery(string id = "raw:1", string sql = "SELECT Id FROM Customers") => new(
        Id: id,
        SqlText: sql,
        Parameters: Array.Empty<ParameterDescriptor>(),
        ResultColumns: Array.Empty<ColumnDescriptor>(),
        ExpectedProperties: new List<PropertyDescriptor> { new("Id", "int", null, null, false, null, true, false) },
        TargetTypeName: "Customer");

    [Fact]
    public async Task ValidateAsync_DescribeThrows_RecordsUnevaluatedInsteadOfDG020Warning()
    {
        var failingProvider = new FailingLiveQuerySchemaProvider(
            new InvalidOperationException("The metadata could not be determined because statement '#temp' in procedure or batch is not supported. Password=hunter2"));
        var rule = new LiveSqlShapeValidationRule(schemaProvider: failingProvider);
        var rawSql = TypedQuery(sql: "SELECT * INTO #temp FROM Customers; SELECT * FROM #temp;");

        var violations = await rule.ValidateAsync(rawSql, new ContractDescriptor[] { rawSql });

        violations.Should().BeEmpty("an undescribed shape is not a finding (red-team H2)");
        var entry = rule.DrainUnevaluatedContracts().Should().ContainSingle().Subject;
        entry.RuleId.Should().Be(LiveSqlShapeValidationRule.UndeterminedShapeRuleId);
        entry.ContractId.Should().Be("raw:1");
        entry.Status.Should().Be(ContractEvaluationStatus.Unevaluated);
        entry.Reason.Should().Contain("Cannot determine result set shape for query").And.Contain("#temp");
        entry.Reason.Should().NotContain("hunter2");
        rule.DrainUnevaluatedContracts().Should().BeEmpty("draining clears the collector");
    }

    [Theory]
    [InlineData(LiveSchemaStatus.Failed, "describe failed")]
    [InlineData(LiveSchemaStatus.Unsupported, "describe not supported")]
    public async Task ValidateAsync_NotDescribed_IgnoresNonLiveColumnsAndRecordsUnevaluated(LiveSchemaStatus status, string expectedKind)
    {
        // Even when a provider attaches syntactic hint columns that would produce a mismatch, they are never compared.
        var hint = new List<ColumnDescriptor> { new("Other", "VARCHAR2", null, null, null, true, "C") };
        var result = status == LiveSchemaStatus.Failed
            ? LiveSchemaResult.Fail("ORA-12541: TNS:no listener", hint)
            : LiveSchemaResult.NotSupported("Statement is not a read-only query; not described live.", hint);
        var rule = new LiveSqlShapeValidationRule(schemaProvider: new FixedResultProvider(result));
        var rawSql = TypedQuery();

        var violations = await rule.ValidateAsync(rawSql, new ContractDescriptor[] { rawSql });

        violations.Should().BeEmpty();
        rule.DrainUnevaluatedContracts().Should().ContainSingle()
            .Which.Reason.Should().Contain(expectedKind);
    }

    [Fact]
    public async Task ValidateAsync_ConnectionWithoutDescriberForProvider_RecordsUnevaluated()
    {
        var rule = new LiveSqlShapeValidationRule("Server=db;Database=x;", "db2");
        var rawSql = TypedQuery();

        var violations = await rule.ValidateAsync(rawSql, new ContractDescriptor[] { rawSql });

        violations.Should().BeEmpty();
        rule.DrainUnevaluatedContracts().Should().ContainSingle()
            .Which.Reason.Should().Contain("No live query schema provider").And.Contain("db2");
    }

    [Fact]
    public async Task ValidateAsync_NoConnectionAndNoProvider_IsNotUnevaluated()
    {
        // The offline registration is not a live check at all; it must not invent unevaluated entries.
        var rule = new LiveSqlShapeValidationRule();
        var rawSql = TypedQuery();

        await rule.ValidateAsync(rawSql, new ContractDescriptor[] { rawSql });

        rule.DrainUnevaluatedContracts().Should().BeEmpty();
    }

    [Fact]
    public async Task ConcurrentEngine_DrainsUnevaluatedIntoResultWithoutMarkingIncomplete()
    {
        var rule = new LiveSqlShapeValidationRule(schemaProvider: new FixedResultProvider(LiveSchemaResult.Fail("connection refused")));
        var contracts = new ContractDescriptor[] { TypedQuery("raw:b"), TypedQuery("raw:a") };

        var result = await new ConcurrentValidationEngine(maxDegreeOfParallelism: 4)
            .ValidateDetailedAsync(contracts, new IContractRule[] { rule });

        result.IsIncomplete.Should().BeFalse("unevaluated contracts are a separate channel from rule failures");
        result.Violations.Should().BeEmpty();
        result.UnevaluatedContracts.Select(entry => entry.ContractId).Should().Equal("raw:a", "raw:b");
        result.UnevaluatedContracts.Should().OnlyContain(entry => entry.RuleId == "DG020");
        rule.DrainUnevaluatedContracts().Should().BeEmpty("the engine already drained the rule");

        // The violations-only entry point does not throw for unevaluated contracts.
        var violations = await new ConcurrentValidationEngine(2).ValidateAsync(contracts, new IContractRule[] { rule });
        violations.Should().BeEmpty();
    }

    [Fact]
    public async Task GraphExecutor_AggregatesUnevaluatedAcrossLevels()
    {
        var rule = new LiveSqlShapeValidationRule(schemaProvider: new FixedResultProvider(LiveSchemaResult.Fail("timeout")));
        var graph = new RuleDependencyGraph().AddRule(rule);

        var result = await GraphValidationExecutor.ValidateAsync(graph, new ContractDescriptor[] { TypedQuery() }, 2, 100);

        result.UnevaluatedContracts.Should().ContainSingle().Which.ContractId.Should().Be("raw:1");
    }

    [Fact]
    public async Task SequentialPath_DrainFromCollectsEachRuleOnce()
    {
        var rule = new LiveSqlShapeValidationRule(schemaProvider: new FixedResultProvider(LiveSchemaResult.Fail("timeout")));
        var contract = TypedQuery();

        // Mirrors the CLI fallback path (EnableConcurrentValidation: false): rules run one contract at a time, then drain.
        await rule.ValidateAsync(contract, new ContractDescriptor[] { contract });
        var drained = UnevaluatedContracts.DrainFrom(new IContractRule[] { rule, rule, new ParameterCountRule() });

        drained.Should().ContainSingle().Which.ContractId.Should().Be("raw:1");
    }

    [Fact]
    public async Task DescribeColumnsOrThrowAsync_ThrowsForNotDescribedAndReturnsDescribedColumns()
    {
        var failed = new FixedResultProvider(LiveSchemaResult.Fail("boom"));
        var act = async () => await failed.DescribeColumnsOrThrowAsync("SELECT 1", CancellationToken.None);
        (await act.Should().ThrowAsync<LiveSchemaUnavailableException>()).Which.Result.Status.Should().Be(LiveSchemaStatus.Failed);

        var described = new FixedResultProvider(LiveSchemaResult.FromColumns(new List<ColumnDescriptor> { new("Id", "int", null, null, null, false, null) }));
        (await described.DescribeColumnsOrThrowAsync("SELECT 1", CancellationToken.None)).Should().ContainSingle();
    }

    [Fact]
    public async Task SqlServerProvider_UnreachableServer_ReturnsFailedInsteadOfThrowing()
    {
        var provider = new SqlServerLiveQuerySchemaProvider("Server=127.0.0.1,1;Database=x;User Id=u;Password=secret-pw;Connect Timeout=1;Encrypt=False");

        var result = await provider.DescribeResultSetAsync("SELECT Id FROM Customers", CancellationToken.None);

        result.Status.Should().Be(LiveSchemaStatus.Failed);
        result.Columns.Should().BeEmpty();
        result.Error.Should().NotBeNullOrWhiteSpace().And.NotContain("secret-pw");
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
