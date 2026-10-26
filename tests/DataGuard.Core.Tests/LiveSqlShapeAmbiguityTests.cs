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

public class LiveSqlShapeAmbiguityTests
{
    [Theory]
    [InlineData("Id", "Id", "int", "nvarchar", null)]
    [InlineData("Id", "Id", "nvarchar", "int", null)]
    [InlineData("Id", "id", "int", "nvarchar", null)]
    [InlineData("id", "Id", "nvarchar", "int", null)]
    [InlineData("i_d", "i-d", "int", "nvarchar", null)]
    [InlineData("i-d", "i_d", "nvarchar", "int", null)]
    [InlineData("mapped", "MAPPED", "int", "nvarchar", "mapped")]
    [InlineData("MAPPED", "mapped", "nvarchar", "int", "mapped")]
    [InlineData("Id", "Id", "int", "int", null)]
    [InlineData("Id", "Id", "int", "nvarchar", "absent")]
    public async Task SelectedTierAmbiguity_IsDG020WithoutFabricatedMismatch(
        string firstName, string secondName, string firstType, string secondType, string? mapping)
    {
        var columns = new[] { Column(firstName, firstType), Column(secondName, secondType) };
        columns.Should().OnlyContain(column => column.ColumnId == 0);
        var rule = Rule(_ => columns);
        var query = Query(mapping: mapping);

        (await rule.ValidateAsync(query, new[] { query })).Should().BeEmpty();

        var status = rule.DrainUnevaluatedContracts().Should().ContainSingle().Subject;
        status.RuleId.Should().Be("DG020");
        status.ContractId.Should().Be(query.Id);
        status.Status.Should().Be(ContractEvaluationStatus.Unevaluated);
        status.Reason.Should().Contain("ambiguous").And.Contain("Id");
        rule.DrainUnevaluatedContracts().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AmbiguousExplicitMapping_DoesNotFallBackToUniqueProperty(bool reverse)
    {
        var columns = new[] { Column("mapped"), Column("MAPPED", "nvarchar"), Column("Id") };
        var rule = Rule(_ => reverse ? columns.Reverse().ToArray() : columns);
        var query = Query(mapping: "mapped");

        var violations = await rule.ValidateAsync(query, new[] { query });

        violations.Should().NotContain(violation => violation.Severity == DiagnosticSeverity.Error);
        rule.DrainUnevaluatedContracts().Should().ContainSingle().Which.RuleId.Should().Be("DG020");
    }

    [Theory]
    [InlineData("Id", "i_d", null)]
    [InlineData("i_d", "Id", null)]
    [InlineData("Id", "i_d", "absent")]
    [InlineData("ID", "i_d", null)]
    [InlineData("mapped", "Id", "mapped")]
    [InlineData("Id", "mapped", "mapped")]
    public async Task UniqueHigherTier_WinsAndOtherListEntryRemainsExtra(string first, string second, string? mapping)
    {
        var chosenName = mapping == "mapped" ? "mapped" : "Id";
        var rule = Rule(_ => new[]
        {
            Column(first, string.Equals(first, chosenName, StringComparison.OrdinalIgnoreCase) ? "int" : "nvarchar"),
            Column(second, string.Equals(second, chosenName, StringComparison.OrdinalIgnoreCase) ? "int" : "nvarchar"),
        });
        var query = Query(mapping: mapping);

        var violations = await rule.ValidateAsync(query, new[] { query });

        var extra = violations.Should().ContainSingle().Subject;
        extra.RuleId.Should().Be("DG018");
        extra.Severity.Should().Be(DiagnosticSeverity.Warning);
        extra.Message.Should().Contain("1 extra column(s)");
        rule.DrainUnevaluatedContracts().Should().BeEmpty();
    }

    [Fact]
    public async Task UniqueExplicitMapping_WinsOverAmbiguousPropertyAndNormalizedTiers()
    {
        var rule = Rule(_ => new[] { Column("mapped"), Column("Id", "nvarchar"), Column("id", "nvarchar"), Column("i_d", "nvarchar") });
        var query = Query(mapping: "mapped");

        var violations = await rule.ValidateAsync(query, new[] { query });

        violations.Should().ContainSingle().Which.Message.Should().Contain("3 extra column(s)");
        violations.Should().OnlyContain(violation => violation.Severity == DiagnosticSeverity.Warning);
        rule.DrainUnevaluatedContracts().Should().BeEmpty();
    }

    [Theory]
    [InlineData("ID", null)]
    [InlineData("i_d", null)]
    [InlineData("Id", "absent")]
    [InlineData("i_d", "absent")]
    public async Task UniquePropertyOrNormalizedFallback_PreservesExistingPolicy(string name, string? mapping)
    {
        var rule = Rule(_ => new[] { Column(name) });
        var query = Query(mapping: mapping);

        (await rule.ValidateAsync(query, new[] { query })).Should().BeEmpty();
        rule.DrainUnevaluatedContracts().Should().BeEmpty();
    }

    [Theory]
    [InlineData("Other", "int", null, "missing expected column 'Id'")]
    [InlineData("mapped_name", "int", "mapped-name", "missing expected column 'mapped-name'")]
    [InlineData("Id", "nvarchar", null, "not compatible")]
    public async Task UnambiguousMissingAndTypeControls_StillReportDG018(
        string name, string dataType, string? mapping, string message)
    {
        var rule = Rule(_ => new[] { Column(name, dataType) });
        var query = Query(mapping: mapping);

        var violations = await rule.ValidateAsync(query, new[] { query });

        violations.Should().ContainSingle(violation => violation.RuleId == "DG018" && violation.Severity == DiagnosticSeverity.Error)
            .Which.Message.Should().Contain(message);
        rule.DrainUnevaluatedContracts().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Ambiguity_PreservesIndependentMissingOrTypeFinding(bool missing, bool reverseProperties)
    {
        var columns = new List<ColumnDescriptor> { Column("Id"), Column("Id", "nvarchar") };
        if (!missing)
        {
            columns.Add(Column("Age", "nvarchar"));
        }

        var rule = Rule(_ => columns);
        var query = Query();
        var properties = query.ExpectedProperties!.Append(new PropertyDescriptor("Age", "int", null, null, false, null, false, false)).ToArray();
        query = query with { ExpectedProperties = reverseProperties ? properties.Reverse().ToArray() : properties };

        var violations = await rule.ValidateAsync(query, new[] { query });

        var finding = violations.Should().ContainSingle().Subject;
        finding.RuleId.Should().Be("DG018");
        finding.Severity.Should().Be(DiagnosticSeverity.Error);
        finding.Message.Should().Contain("Age").And.Contain(missing ? "missing expected column" : "not compatible");
        rule.DrainUnevaluatedContracts().Should().ContainSingle().Which.Reason.Should().Contain("'Id'");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pipeline_SequentialAndConcurrentRuns_KeepAmbiguityAndCleanStatusCurrent(bool concurrent)
    {
        var rule = Rule(sql => sql == "SELECT unique"
            ? new[] { Column("Id") }
            : new[] { Column("Id"), Column("Id", "nvarchar") });
        using var pipeline = DataGuardApi.CreatePipeline(new DataGuardConfiguration
        {
            EnableConcurrentValidation = concurrent,
            EnableSmartDefaults = false,
            EnableTelemetry = false,
            EnableAuditLogging = false,
            EnableBaseline = false,
        }).WithProviderRules(new IContractRule[] { rule });

        foreach (var prefix in new[] { "first", "again" })
        {
            var ambiguous = await pipeline.ValidateAsync(new[] { Query(prefix + ":b"), Query(prefix + ":a") });
            ambiguous.ExecutionStatus.Should().Be(ValidationExecutionStatus.Complete);
            ambiguous.TotalViolations.Should().Be(0);
            ambiguous.IsClean.Should().BeFalse();
            ambiguous.UnevaluatedContracts.Select(entry => (entry.RuleId, entry.ContractId))
                .Should().Equal(("DG020", prefix + ":a"), ("DG020", prefix + ":b"));
            ambiguous.RuleOutcomes.Should().ContainSingle().Which.State.Should().Be(RuleExecutionState.Evaluated);

            var clean = await pipeline.ValidateAsync(new[] { Query(prefix + ":clean", sql: "SELECT unique") });
            clean.ExecutionStatus.Should().Be(ValidationExecutionStatus.Complete);
            clean.IsClean.Should().BeTrue();
            clean.UnevaluatedContracts.Should().BeEmpty();
            rule.DrainUnevaluatedContracts().Should().BeEmpty();
            ambiguous.IsClean.Should().BeFalse("a later clean run must not mutate the earlier result");
        }
    }

    private static ColumnDescriptor Column(string name, string type = "int") => new(name, type, null, null, null, false, null);

    private static RawSqlDescriptor Query(string id = "raw:ambiguous", string? mapping = null, string sql = "SELECT duplicate") => new(
        id, sql, Array.Empty<ParameterDescriptor>(), Array.Empty<ColumnDescriptor>(),
        ExpectedProperties: new[] { new PropertyDescriptor("Id", "int", mapping, null, false, null, false, false) },
        TargetTypeName: "Customer");

    private static LiveSqlShapeValidationRule Rule(Func<string, IReadOnlyList<ColumnDescriptor>> columns)
    {
        TypeCompatibilityRegistry.Register(SqlServerTypeCompatibility.Instance);
        return new LiveSqlShapeValidationRule(schemaProvider: new InjectedProvider(columns));
    }

    private sealed class InjectedProvider(Func<string, IReadOnlyList<ColumnDescriptor>> columns) : ILiveQuerySchemaProvider
    {
        public async Task<LiveSchemaResult> DescribeResultSetAsync(string sqlText, CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            return LiveSchemaResult.FromColumns(columns(sqlText));
        }
    }
}
