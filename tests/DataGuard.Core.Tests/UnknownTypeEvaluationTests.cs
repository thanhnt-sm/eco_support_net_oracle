using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules;
using DataGuard.Core.Rules.TypeCompatibility;
using DataGuard.Core.Validation;
using DataGuard.SqlServer.Adapter;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

public class UnknownTypeEvaluationTests
{
    [Theory]
    [InlineData(false, "Custom.Value", "int", true, false)]
    [InlineData(false, "int", "custom_type", true, false)]
    [InlineData(true, "Custom.Value", "int", true, false)]
    [InlineData(true, "int", "custom_type", true, false)]
    [InlineData(false, "int", "int", false, false)]
    [InlineData(true, "int", "int", false, false)]
    [InlineData(false, "Guid", "int", false, true)]
    [InlineData(true, "Guid", "int", false, true)]
    public async Task ParameterTypes_UnknownIsUnevaluated_WithoutInventingMismatch(
        bool catalog, string clrType, string dbType, bool unknown, bool mismatch)
    {
        var parameter = new ParameterDescriptor("@value", dbType, ParameterDirection.Input, null, null, null, false, 1, ClrType: clrType);
        var call = new RawSqlDescriptor("call:test", "EXEC dbo.CheckValue @value = @value", new[] { parameter }, Array.Empty<ColumnDescriptor>());
        var procedure = new StoredProcedureDescriptor("procedure:test", "CheckValue", "dbo", "", new[] { parameter with { ClrType = null } }, Array.Empty<ColumnDescriptor>(), false);
        ContractDescriptor[] contracts = catalog ? new ContractDescriptor[] { call, procedure } : new ContractDescriptor[] { call };
        var rule = new ParameterTypeMatchRule("sqlserver", SqlServerTypeCompatibility.Instance);

        var findings = await rule.ValidateAsync(call, contracts);

        findings.Should().HaveCount(mismatch ? 1 : 0);
        var unevaluated = rule.DrainUnevaluatedContracts();
        unevaluated.Should().HaveCount(unknown ? 1 : 0);
        if (unknown)
        {
            unevaluated[0].RuleId.Should().Be("DG002");
            unevaluated[0].ContractId.Should().Be(call.Id);
            unevaluated[0].Reason.Should().Contain("@value");
        }
    }

    [Theory]
    [InlineData("Custom.Value", "int", true, false)]
    [InlineData("int", "custom_type", true, false)]
    [InlineData("int", "int", false, false)]
    [InlineData("Guid", "int", false, true)]
    public async Task LiveTypes_UnknownIsUnevaluated_WithoutInventingMismatch(
        string clrType, string dbType, bool unknown, bool mismatch)
    {
        TypeCompatibilityRegistry.Register(SqlServerTypeCompatibility.Instance);
        var property = new PropertyDescriptor("Value", clrType, null, null, false, null, false, false);
        var call = new RawSqlDescriptor("query:test", "SELECT Value FROM Fixture", Array.Empty<ParameterDescriptor>(), Array.Empty<ColumnDescriptor>(), ExpectedProperties: new[] { property });
        var rule = new LiveSqlShapeValidationRule(schemaProvider: new SyntheticSchemaProvider(dbType));

        var findings = await rule.ValidateAsync(call, new[] { call });

        findings.Should().HaveCount(mismatch ? 1 : 0);
        var unevaluated = rule.DrainUnevaluatedContracts();
        unevaluated.Should().HaveCount(unknown ? 1 : 0);
        if (unknown)
        {
            unevaluated[0].RuleId.Should().Be("DG020");
            unevaluated[0].ContractId.Should().Be(call.Id);
            unevaluated[0].Reason.Should().Contain("Value");
        }
    }

    [Fact]
    public async Task Engine_PreservesUnknownStatusAlongsideIndependentMismatch()
    {
        var unknown = new ParameterDescriptor("@unknown", "int", ParameterDirection.Input, null, null, null, false, 1, ClrType: "Custom.Value");
        var mismatch = unknown with { Name = "@mismatch", ClrType = "Guid", OrdinalPosition = 2 };
        var call = new RawSqlDescriptor("call:mixed", "EXEC dbo.CheckValue @unknown, @mismatch", new[] { unknown, mismatch }, Array.Empty<ColumnDescriptor>());
        var rule = new ParameterTypeMatchRule("sqlserver", SqlServerTypeCompatibility.Instance);

        var result = await new ConcurrentValidationEngine(maxDegreeOfParallelism: 2)
            .ValidateDetailedAsync(new ContractDescriptor[] { call }, new IContractRule[] { rule });

        result.IsIncomplete.Should().BeFalse("unevaluated contracts use a separate status channel from rule execution failures");
        result.Violations.Should().ContainSingle().Which.Message.Should().Contain("@mismatch");
        var entry = result.UnevaluatedContracts.Should().ContainSingle().Subject;
        entry.ContractId.Should().Be(call.Id);
        entry.RuleId.Should().Be("DG002");
        entry.Reason.Should().Contain("@unknown");
        rule.DrainUnevaluatedContracts().Should().BeEmpty("the engine drains status into its result");
    }

    [Fact]
    public async Task UnregisteredProvider_ReportsUnknownWithoutBorrowingAnotherTypeTable()
    {
        const string provider = "synthetic-unknown-reconciliation";
        var parameter = new ParameterDescriptor("@value", "int", ParameterDirection.Input, null, null, null, false, 1, ClrType: "int");
        var property = new PropertyDescriptor("Value", "int", null, null, false, null, false, false);
        var call = new RawSqlDescriptor("query:unregistered", "SELECT Value FROM Fixture", new[] { parameter }, Array.Empty<ColumnDescriptor>(), ExpectedProperties: new[] { property });
        var parameterRule = new ParameterTypeMatchRule(provider);
        var liveRule = new LiveSqlShapeValidationRule(provider: provider, schemaProvider: new SyntheticSchemaProvider("int"));

        var result = await new ConcurrentValidationEngine(maxDegreeOfParallelism: 2)
            .ValidateDetailedAsync(new ContractDescriptor[] { call }, new IContractRule[] { parameterRule, liveRule });

        result.Violations.Should().BeEmpty();
        result.UnevaluatedContracts.Select(entry => entry.RuleId).Should().Equal("DG002", "DG020");
        result.UnevaluatedContracts.Should().OnlyContain(entry => entry.ContractId == call.Id);
    }

    private sealed class SyntheticSchemaProvider(string dbType) : ILiveQuerySchemaProvider
    {
        public Task<LiveSchemaResult> DescribeResultSetAsync(string sqlText, CancellationToken cancellationToken) =>
            Task.FromResult(LiveSchemaResult.FromColumns(new[] { new ColumnDescriptor("Value", dbType, null, null, null, false, null) }));
    }
}
