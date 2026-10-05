using DataGuard.Cli;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Models;
using DataGuard.Core.Rules;
using DataGuard.Core.Validation;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

public class ProviderRuleCatalogTests
{
    [Fact]
    public void RuleExecutionOutcome_OnlyUnavailableAndFailedMakeCoverageIncomplete()
    {
        new RuleExecutionOutcome("DG001", RuleExecutionState.Evaluated, Array.Empty<DataGuard.Core.Abstractions.ContractViolation>()).MakesValidationIncomplete.Should().BeFalse();
        new RuleExecutionOutcome("DG002", RuleExecutionState.Skipped, Array.Empty<DataGuard.Core.Abstractions.ContractViolation>()).MakesValidationIncomplete.Should().BeFalse();
        new RuleExecutionOutcome("PG004", RuleExecutionState.Unavailable, Array.Empty<DataGuard.Core.Abstractions.ContractViolation>(), "Requires analyzer context").MakesValidationIncomplete.Should().BeTrue();
        new RuleExecutionOutcome("DG003", RuleExecutionState.Failed, Array.Empty<DataGuard.Core.Abstractions.ContractViolation>(), FailureReason: "rule failure").MakesValidationIncomplete.Should().BeTrue();
    }

    [Fact]
    public void MySql_ContainsEachDocumentedRuleOnce()
    {
        var registrations = ProviderRuleCatalog.Get("mysql");

        registrations.Select(registration => registration.Rule.RuleId)
            .Should().Contain(new[] { "MY001", "MY002", "MY003", "MY004", "MY005", "MY006", "MY007" });
        registrations.Select(registration => registration.Rule.RuleId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void PostgreSql_ReportsAnalyzerOnlyRuleAsUnavailable()
    {
        var registrations = ProviderRuleCatalog.Get("postgresql");

        registrations.Select(registration => registration.Rule.RuleId)
            .Should().Contain(new[] { "PG001", "PG002", "PG003", "PG004", "PG005" });
        var pg004 = registrations.Should().ContainSingle(registration => registration.Rule.RuleId == "PG004").Subject;
        pg004.Availability.Should().Be(RuleAvailability.Unavailable);
        pg004.PrerequisiteReason.Should().Contain("analyzer");
        var outcome = pg004.CreateUnavailableOutcome();
        outcome.State.Should().Be(RuleExecutionState.Unavailable);
        outcome.PrerequisiteReason.Should().Contain("analyzer");
        outcome.MakesValidationIncomplete.Should().BeTrue();
    }

    [Fact]
    public void Get_WithConnection_RegistersLiveShapeRuleBoundToConnection()
    {
        var live = ProviderRuleCatalog.Get("sqlserver", "Server=127.0.0.1,1;Connect Timeout=1")
            .Select(registration => registration.Rule).OfType<LiveSqlShapeValidationRule>().Single();

        live.HasLiveConnection.Should().BeTrue();
    }

    [Fact]
    public void Get_WithoutConnection_RegistersConnectionlessLiveShapeRule()
    {
        // The catalog has one switch: a null connection registers the offline variant (IDE-safe hands it null, see IdeSafePolicyTests).
        var registrations = ProviderRuleCatalog.Get("sqlserver", connectionString: null);

        var live = registrations.Select(registration => registration.Rule).OfType<LiveSqlShapeValidationRule>().Single();
        live.HasLiveConnection.Should().BeFalse();
        registrations.Select(registration => registration.Rule.RuleId).Should().Contain("DG018");
    }

    [Fact]
    public void CliConfigurationResolver_CommandLineValuesTakePrecedence()
    {
        var input = new DataGuardConfiguration { ConnectionString = "config", DefaultProvider = "oracle" };

        var resolved = CliConfigurationResolver.Resolve(input, "command-line", "mysql", "environment");

        resolved.Configuration.ConnectionString.Should().Be("command-line");
        resolved.Provider.Should().Be("mysql");
        input.ConnectionString.Should().Be("config");
    }

    [Fact]
    public void CliConfigurationResolver_EnvironmentAndConfigDefaultsAreUsedInOrder()
    {
        var input = new DataGuardConfiguration { ConnectionString = "config", DefaultProvider = "postgresql" };

        var environment = CliConfigurationResolver.Resolve(input, null, null, "environment");
        var config = CliConfigurationResolver.Resolve(input, null, null, null);
        var fallback = CliConfigurationResolver.Resolve(new DataGuardConfiguration(), null, null, null);

        environment.Configuration.ConnectionString.Should().Be("environment");
        environment.Provider.Should().Be("postgresql");
        config.Configuration.ConnectionString.Should().Be("config");
        config.Provider.Should().Be("postgresql");
        fallback.Configuration.ConnectionString.Should().BeNull();
        fallback.Provider.Should().Be("sqlserver");
    }
    [Theory]
    [InlineData("sqlserver")]
    [InlineData("oracle")]
    [InlineData("mysql")]
    [InlineData("postgresql")]
    public void Get_RuleIdsAreUniquePerProvider_AndCoreIdsAreTitled(string provider)
    {
        var ids = ProviderRuleCatalog.Get(provider).Select(registration => registration.Rule.RuleId).ToList();

        ids.Should().OnlyHaveUniqueItems();
        ids.Should().Contain(new[] { "DG015", "DG016", "DG019" });
        ids.Where(id => id.StartsWith("DG", StringComparison.Ordinal))
            .Should().OnlyContain(id => ProviderRuleCatalog.RuleTitles.ContainsKey(id));
    }

    [Fact]
    public void RuleTitles_Dg016IsPhantomColumn_Dg019IsParseError()
    {
        ProviderRuleCatalog.RuleTitles["DG015"].Should().Be("Phantom Table Reference");
        ProviderRuleCatalog.RuleTitles["DG016"].Should().Be("Phantom Column Reference");
        ProviderRuleCatalog.RuleTitles["DG019"].Should().Be("Raw SQL Parse Error");
    }

    [Fact]
    public async Task Get_SampleRules_EmitViolationsCarryingTheirOwnRuleId()
    {
        // H11: --skip-rules is exact only if every violation's RuleId equals the emitting rule's RuleId.
        var schema = new DatabaseSchemaDescriptor(
            "schema:1",
            new[]
            {
                new DatabaseTableDescriptor("CUSTOMERS", new[]
                {
                    new ColumnDescriptor("ID", "NUMBER", null, null, null, false, null),
                    new ColumnDescriptor("NAME", "VARCHAR2", 100, null, null, false, null),
                }),
            },
            "CHAR");
        static RawSqlDescriptor Raw(string id, string sql) => new(id, sql, Array.Empty<ParameterDescriptor>(), Array.Empty<ColumnDescriptor>());
        var contracts = new List<ContractDescriptor>
        {
            schema,
            Raw("raw:table", "SELECT ID FROM GHOST"),
            Raw("raw:column", "SELECT c.ID, c.ADDRESS FROM CUSTOMERS c"),
            Raw("raw:star", "SELECT * FROM CUSTOMERS"),
            Raw("raw:exec", "EXEC dbo.GetCustomer"),
            Raw("raw:invalid", "SELECT FROM") with { ParseStatus = RawSqlParseStatus.Invalid, ParseError = "Incorrect syntax" },
            new EntityDescriptor("entity:c", "Customer", "Customer", "CUSTOMERS", new[]
            {
                new PropertyDescriptor("Name", "string", "NAME", null, IsNullable: true),
            }),
        };
        var sample = new[] { "DG005", "DG013", "DG015", "DG016", "DG017", "DG019" };
        var rules = ProviderRuleCatalog.Get("oracle").Select(registration => registration.Rule)
            .Where(rule => sample.Contains(rule.RuleId)).ToList();
        rules.Select(rule => rule.RuleId).Should().BeEquivalentTo(sample);

        foreach (var rule in rules)
        {
            var violations = new List<ContractViolation>();
            foreach (var contract in contracts)
            {
                violations.AddRange(await rule.ValidateAsync(contract, contracts));
            }

            violations.Should().NotBeEmpty(rule.RuleId);
            violations.Should().OnlyContain(violation => violation.RuleId == rule.RuleId, rule.RuleId);
        }
    }
}
