using DataGuard.Cli;
using DataGuard.Core.Models;
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
}
