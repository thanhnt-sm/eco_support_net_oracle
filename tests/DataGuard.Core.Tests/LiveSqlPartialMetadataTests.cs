using DataGuard.Core.Abstractions;
using DataGuard.Core.Models;
using DataGuard.Core.Rules;
using DataGuard.Core.Rules.TypeCompatibility;
using DataGuard.Core.Validation;
using DataGuard.SqlServer.Adapter;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DataGuard.Core.Tests;

public class LiveSqlPartialMetadataTests
{
    [Theory]
    [InlineData(false, "")]
    [InlineData(true, "")]
    [InlineData(false, " \t")]
    [InlineData(true, " \t")]
    [InlineData(false, "unknown")]
    [InlineData(true, "unknown")]
    public async Task UnavailableDatabaseType_IsNotClean_AndDoesNotLeakIntoResolvedRun(bool concurrent, string dataType)
    {
        var rule = Rule(sql => new[] { Column("Id", sql.Contains("Resolved", StringComparison.Ordinal) ? "int" : dataType) });
        using var pipeline = DataGuardApi.CreatePipeline(new DataGuardConfiguration
        {
            EnableConcurrentValidation = concurrent,
            EnableSmartDefaults = false,
            EnableTelemetry = false,
            EnableAuditLogging = false,
            EnableBaseline = false,
        }).WithProviderRules(new IContractRule[] { rule });
        var partial = Query();

        foreach (var query in new[] { partial, partial with { Id = "resolved", SqlText = "SELECT Id FROM Resolved" }, partial })
        {
            var result = await pipeline.ValidateAsync(new[] { query });

            result.ExecutionStatus.Should().Be(ValidationExecutionStatus.Complete);
            result.Violations.Should().BeEmpty();
            result.RuleOutcomes.Should().ContainSingle().Which.State.Should().Be(RuleExecutionState.Evaluated);
            if (query.Id == "resolved")
            {
                result.IsClean.Should().BeTrue();
                result.UnevaluatedContracts.Should().BeEmpty();
            }
            else
            {
                result.IsClean.Should().BeFalse("a matched column without a known database type has no type verdict");
                var status = result.UnevaluatedContracts.Should().ContainSingle().Subject;
                status.RuleId.Should().Be("DG020");
                status.ContractId.Should().Be(partial.Id);
                status.Reason.Should().Contain("Cannot determine type compatibility").And.Contain("'Id'");
            }

            rule.DrainUnevaluatedContracts().Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task PartialTypeMetadata_PreservesIndependentFindings_InEitherExecutionOrder(
        bool concurrent, bool shapeFirst, bool missingAge)
    {
        var columns = new List<ColumnDescriptor> { Column("Id", "") };
        if (!missingAge)
        {
            columns.Add(Column("Age", "nvarchar"));
        }

        var live = Rule(_ => columns);
        var shape = new ColumnShapeMatchRule();
        var graph = new RuleDependencyGraph();
        graph.RegisterRule(live, shapeFirst ? new[] { shape.RuleId } : null);
        graph.RegisterRule(shape, shapeFirst ? null : new[] { live.RuleId });
        graph.GetExecutionOrder().Select(rule => rule.RuleId)
            .Should().Equal(shapeFirst ? new[] { "DG004", "DG018" } : new[] { "DG018", "DG004" });
        var query = Query() with
        {
            SqlText = missingAge ? "SELECT Id FROM Customers" : "SELECT Id, Age FROM Customers",
            ExpectedProperties = new[] { Property("Id"), Property("Age") },
        };

        var result = await GraphValidationExecutor.ValidateAsync(graph, new[] { query }, concurrent, 4, 100);

        result.IsIncomplete.Should().BeFalse();
        result.RuleOutcomes.Should().HaveCount(2).And.OnlyContain(outcome => outcome.State == RuleExecutionState.Evaluated);
        result.Violations.Should().HaveCount(missingAge ? 2 : 1).And.OnlyContain(violation => violation.Severity == DiagnosticSeverity.Error);
        var liveFinding = result.Violations.Should().ContainSingle(violation => violation.RuleId == "DG018").Subject;
        liveFinding.Message.Should().Contain("Age").And.Contain(missingAge ? "missing expected column" : "not compatible");
        if (missingAge)
        {
            result.Violations.Should().ContainSingle(violation => violation.RuleId == "DG004")
                .Which.Message.Should().Contain("Age");
        }

        var status = result.UnevaluatedContracts.Should().ContainSingle().Subject;
        status.RuleId.Should().Be("DG020");
        status.ContractId.Should().Be(query.Id);
        status.Reason.Should().Contain("'Id'");
        live.DrainUnevaluatedContracts().Should().BeEmpty();
    }

    private static PropertyDescriptor Property(string name) => new(name, "int", null, null, false, null, false, false);

    private static ColumnDescriptor Column(string name, string type) => new(name, type, null, null, null, false, null);

    private static RawSqlDescriptor Query() => new("partial", "SELECT Id FROM Customers", Array.Empty<ParameterDescriptor>(),
        Array.Empty<ColumnDescriptor>(), ExpectedProperties: new[] { Property("Id") }, TargetTypeName: "Customer");

    private static LiveSqlShapeValidationRule Rule(Func<string, IReadOnlyList<ColumnDescriptor>> columns)
    {
        TypeCompatibilityRegistry.Register(SqlServerTypeCompatibility.Instance);
        return new LiveSqlShapeValidationRule(schemaProvider: new Provider(columns));
    }

    private sealed class Provider(Func<string, IReadOnlyList<ColumnDescriptor>> columns) : ILiveQuerySchemaProvider
    {
        public Task<LiveSchemaResult> DescribeResultSetAsync(string sqlText, CancellationToken cancellationToken) =>
            Task.FromResult(LiveSchemaResult.FromColumns(columns(sqlText)));
    }
}
