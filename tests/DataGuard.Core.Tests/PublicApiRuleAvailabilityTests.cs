using System.Collections.Immutable;
using DataGuard.Cli;
using DataGuard.Core.Abstractions;
using DataGuard.Core.Models;
using DataGuard.Core.Plugins;
using DataGuard.Core.Validation;
using FluentAssertions;
using Xunit;

namespace DataGuard.Core.Tests;

public class PublicApiRuleAvailabilityTests
{
    public static IEnumerable<object[]> ProviderModes()
    {
        foreach (var provider in new[] { "oracle", "postgres", "postgresql", "sqlserver", "mysql" })
        {
            foreach (var concurrent in new[] { false, true })
            {
                foreach (var failOnUnavailable in new[] { false, true })
                {
                    yield return new object[] { provider, concurrent, failOnUnavailable };
                }
            }
        }
    }

    private static DataGuardConfiguration Config(bool concurrent = false, bool failOnUnavailable = false) => new()
    {
        EnableSmartDefaults = false,
        EnableAuditLogging = false,
        EnableConcurrentValidation = concurrent,
        FailOnUnavailableRules = failOnUnavailable,
    };

    [Theory]
    [MemberData(nameof(ProviderModes))]
    public async Task CatalogPipeline_ReportsCoverageWithoutInventingViolations(string provider, bool concurrent, bool failOnUnavailable)
    {
        using var pipeline = ProviderRuleCatalog.CreatePipeline(provider, Config(concurrent, failOnUnavailable));
        var expected = ProviderRuleCatalog.Get(provider);

        // Empty input still requests the provider's declared rule inventory.
        var first = await pipeline.ValidateAsync(Array.Empty<ContractDescriptor>());
        var second = await pipeline.ValidateAsync(Array.Empty<ContractDescriptor>());
        foreach (var result in new[] { first, second })
        {
            result.RuleOutcomes.Select(o => o.RuleId).Should().BeEquivalentTo(expected.Select(r => r.Rule.RuleId));
            result.RuleOutcomes.Select(o => o.RuleId).Should().OnlyHaveUniqueItems();
            result.Violations.Should().BeEmpty();
            result.TotalViolations.Should().Be(0);
            result.UnevaluatedContracts.Should().BeEmpty();
            var unavailable = expected.Where(r => r.Availability == RuleAvailability.Unavailable).ToList();
            result.ExecutionStatus.Should().Be(unavailable.Count > 0 ? ValidationExecutionStatus.Incomplete : ValidationExecutionStatus.Complete);
            result.IsClean.Should().Be(unavailable.Count == 0);
            foreach (var registration in unavailable)
            {
                var outcome = result.RuleOutcomes.Should().ContainSingle(o => o.RuleId == registration.Rule.RuleId).Subject;
                outcome.State.Should().Be(RuleExecutionState.Unavailable);
                outcome.PrerequisiteReason.Should().Be(registration.PrerequisiteReason);
                outcome.Violations.Should().BeEmpty();
                pipeline.Rules.Should().NotContain(r => r.RuleId == outcome.RuleId);
            }
            result.RuleOutcomes.Where(o => pipeline.Rules.Any(r => r.RuleId == o.RuleId))
                .Should().OnlyContain(o => o.State == RuleExecutionState.Evaluated);
        }
    }

    [Theory]
    [InlineData("oracle", false)]
    [InlineData("oracle", true)]
    [InlineData("postgresql", false)]
    [InlineData("postgresql", true)]
    public async Task Baseline_DoesNotHideUnavailableCoverage(string provider, bool concurrent)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dg-api-availability-{Guid.NewGuid():N}.json");
        try
        {
            using var pipeline = ProviderRuleCatalog.CreatePipeline(provider, Config(concurrent)).WithBaselineFile(path);
            await pipeline.CreateBaselineAsync(Array.Empty<ContractViolation>());
            var result = await pipeline.ValidateAsync(Array.Empty<ContractDescriptor>());
            result.ExecutionStatus.Should().Be(ValidationExecutionStatus.Incomplete);
            result.IsClean.Should().BeFalse();
            result.RuleOutcomes.Should().ContainSingle(o => o.State == RuleExecutionState.Unavailable);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(RuleExecutionState.Unavailable)]
    [InlineData(RuleExecutionState.Failed)]
    public void Result_IsCleanCannotContradictIncompleteRuleCoverage(RuleExecutionState state)
    {
        var result = new ValidationResult(0, 0, 0, 0, 0, ImmutableArray<ContractViolation>.Empty, TimeSpan.Zero, "1.0")
        {
            RuleOutcomes = new[] { new RuleExecutionOutcome("CUSTOM1", state, Array.Empty<ContractViolation>()) },
        };
        result.IsClean.Should().BeFalse();
    }

    private static RuleExecutionOutcome Unavailable(string id = "OFFLINE1") =>
        new(id, RuleExecutionState.Unavailable, Array.Empty<ContractViolation>(), "Requires analyzer context.");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Recomposition_ReplacesMetadataAndKeepsCustomRules(bool concurrent)
    {
        var custom = new TestRule("CUSTOM1");
        using var pipeline = ProviderRuleCatalog.CreatePipeline("oracle", Config(concurrent)).WithRules(custom);
        pipeline.WithProviderRules(new[] { new TestRule("READY1") }, new[] { Unavailable("PG004") });
        var replaced = await pipeline.ValidateAsync(Array.Empty<ContractDescriptor>());
        replaced.RuleOutcomes.Select(o => o.RuleId).Should().BeEquivalentTo("CUSTOM1", "READY1", "PG004");
        replaced.ExecutionStatus.Should().Be(ValidationExecutionStatus.Incomplete);

        // The original overload explicitly selects a new executable set and removes old declarations.
        pipeline.WithProviderRules(new[] { new TestRule("READY2") });
        var complete = await pipeline.ValidateAsync(Array.Empty<ContractDescriptor>());
        complete.RuleOutcomes.Select(o => o.RuleId).Should().BeEquivalentTo("CUSTOM1", "READY2");
        complete.IsClean.Should().BeTrue();
        pipeline.Rules.Should().Contain(custom);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitRuleExclusion_DoesNotBecomeUnavailable(bool concurrent)
    {
        using var pipeline = DataGuardApi.CreatePipeline(Config(concurrent))
            .WithProviderRules(ProviderRuleCatalog.GetReadyRules("oracle").Where(r => r.RuleId != "DG101"));
        var result = await pipeline.ValidateAsync(Array.Empty<ContractDescriptor>());
        result.RuleOutcomes.Should().NotContain(o => o.RuleId == "DG101" || o.RuleId == "DG012");
        result.ExecutionStatus.Should().Be(ValidationExecutionStatus.Complete);
        result.IsClean.Should().BeTrue();
    }

    [Fact]
    public async Task Composition_CopiesCallerCollections_AndUsesOrdinalIds()
    {
        var rules = new List<IContractRule> { new TestRule("offline1") };
        var findings = new List<ContractViolation>();
        var declarations = new List<RuleExecutionOutcome> { Unavailable() with { Violations = findings } };
        using var pipeline = DataGuardApi.CreatePipeline(Config()).WithProviderRules(rules, declarations);
        rules.Clear();
        declarations.Clear();
        findings.Add(new ContractViolation("OFFLINE1", "later mutation", Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var result = await pipeline.ValidateAsync(Array.Empty<ContractDescriptor>());
        result.RuleOutcomes.Select(o => o.RuleId).Should().Equal("OFFLINE1", "offline1");
        result.RuleOutcomes.Should().OnlyContain(o => o.Violations.Count == 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedExecution_IsMergedWithDeclaredCoverage(bool concurrent)
    {
        using var pipeline = DataGuardApi.CreatePipeline(Config(concurrent))
            .WithProviderRules(new[] { new TestRule("FAIL1", throwOnValidation: true), new TestRule("READY1") }, new[] { Unavailable() });
        var contracts = new ContractDescriptor[] { new EntityDescriptor("entity:1", "E", "E", "T", Array.Empty<PropertyDescriptor>()) };
        var result = await pipeline.ValidateAsync(contracts);
        result.RuleOutcomes.Should().ContainSingle(o => o.RuleId == "FAIL1" && o.State == RuleExecutionState.Failed);
        result.RuleOutcomes.Should().ContainSingle(o => o.RuleId == "READY1" && o.State == RuleExecutionState.Evaluated);
        result.RuleOutcomes.Should().ContainSingle(o => o.RuleId == "OFFLINE1" && o.State == RuleExecutionState.Unavailable);
        result.IsClean.Should().BeFalse();
        result.ExecutionStatus.Should().Be(ValidationExecutionStatus.Incomplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CustomRules_ExecuteAlongsideDeclaredCoverage_AndBaselineFiltersOnlyFindings(bool concurrent)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dg-api-findings-{Guid.NewGuid():N}.json");
        try
        {
            using var pipeline = DataGuardApi.CreatePipeline(Config(concurrent))
                .WithProviderRules(Array.Empty<IContractRule>(), new[] { Unavailable() })
                .WithRules(new TestRule("CUSTOM1", emitViolation: true)).WithBaselineFile(path);
            var contracts = new ContractDescriptor[] { new EntityDescriptor("entity:1", "E", "E", "T", Array.Empty<PropertyDescriptor>()) };
            var first = await pipeline.ValidateAsync(contracts);
            first.Violations.Should().ContainSingle().Which.RuleId.Should().Be("CUSTOM1");
            await pipeline.CreateBaselineAsync(first.Violations);
            var second = await pipeline.ValidateAsync(contracts);
            second.Violations.Should().BeEmpty();
            second.ExecutionStatus.Should().Be(ValidationExecutionStatus.Incomplete);
            second.IsClean.Should().BeFalse();
            second.RuleOutcomes.Should().ContainSingle(o => o.RuleId == "OFFLINE1" && o.State == RuleExecutionState.Unavailable);
            second.RuleOutcomes.Should().ContainSingle(o => o.RuleId == "CUSTOM1" && o.State == RuleExecutionState.Evaluated);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task DuplicateDeclarationsAndExecutableCollisions_DoNotReplaceExistingComposition()
    {
        using var pipeline = DataGuardApi.CreatePipeline(Config())
            .WithProviderRules(new[] { new TestRule("READY1") }, new[] { Unavailable() })
            .WithRules(new TestRule("CUSTOM1"));
        Action duplicate = () => pipeline.WithProviderRules(Array.Empty<IContractRule>(), new[] { Unavailable(), Unavailable() });
        Action readyCollision = () => pipeline.WithProviderRules(new[] { new TestRule("OFFLINE1") }, new[] { Unavailable() });
        Action customCollision = () => pipeline.WithProviderRules(Array.Empty<IContractRule>(), new[] { Unavailable("CUSTOM1") });
        Action laterCustomCollision = () => pipeline.WithRules(new TestRule("OFFLINE1"));
        Action executableDuplicate = () => pipeline.WithProviderRules(new[] { new TestRule("SAME"), new TestRule("SAME") });
        foreach (var action in new[] { duplicate, readyCollision, customCollision, laterCustomCollision, executableDuplicate })
        {
            action.Should().Throw<InvalidOperationException>();
            var result = await pipeline.ValidateAsync(Array.Empty<ContractDescriptor>());
            result.RuleOutcomes.Select(o => o.RuleId).Should().BeEquivalentTo("OFFLINE1", "READY1", "CUSTOM1");
        }
    }

    public static IEnumerable<object[]> InvalidDeclarations()
    {
        yield return new object[] { new RuleExecutionOutcome[] { null! } };
        yield return new object[] { new[] { Unavailable("") } };
        yield return new object[] { new[] { Unavailable(" ") } };
        yield return new object[] { new[] { Unavailable() with { PrerequisiteReason = null } } };
        yield return new object[] { new[] { Unavailable() with { PrerequisiteReason = " " } } };
        yield return new object[] { new[] { Unavailable() with { Violations = null! } } };
        yield return new object[] { new[] { Unavailable() with { FailureReason = "not an execution failure" } } };
        yield return new object[] { new[] { Unavailable() with { Violations = new[] { new ContractViolation("OFFLINE1", "fake", Microsoft.CodeAnalysis.DiagnosticSeverity.Error) } } } };
        foreach (var state in new[] { RuleExecutionState.Evaluated, RuleExecutionState.Skipped, RuleExecutionState.Failed, (RuleExecutionState)99 })
        {
            yield return new object[] { new[] { Unavailable() with { State = state } } };
        }
    }

    [Theory]
    [MemberData(nameof(InvalidDeclarations))]
    public void InvalidMetadata_IsRejectedWithoutReplacingProvider(RuleExecutionOutcome[] outcomes)
    {
        using var pipeline = DataGuardApi.CreatePipeline(Config()).WithProviderRules(new[] { new TestRule("ORIGINAL") });
        Action compose = () => pipeline.WithProviderRules(Array.Empty<IContractRule>(), outcomes);
        compose.Should().Throw<ArgumentException>();
        pipeline.Rules.Should().ContainSingle().Which.RuleId.Should().Be("ORIGINAL");
    }

    [Fact]
    public void NullInputs_AreRejected()
    {
        using var pipeline = DataGuardApi.CreatePipeline(Config());
        Action nullRules = () => pipeline.WithProviderRules(null!, new[] { Unavailable() });
        Action nullOutcomes = () => pipeline.WithProviderRules(Array.Empty<IContractRule>(), null!);
        nullRules.Should().Throw<ArgumentNullException>();
        nullOutcomes.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Plugins_CannotClaimDeclaredId_AndRemainReservedDuringRecomposition()
    {
        var dir = Directory.CreateTempSubdirectory("dg-availability-plugin-").FullName;
        try
        {
            TestPluginBuilder.WritePlugin(dir, "OFFLINE1");
            using var pipeline = DataGuardApi.CreatePipeline(Config())
                .WithProviderRules(Array.Empty<IContractRule>(), new[] { Unavailable() })
                .WithPlugins(dir, new PluginTrustPolicy { RequireSignedProvenance = false }, null);
            pipeline.Rules.Should().BeEmpty();
            (await pipeline.ValidateAsync(Array.Empty<ContractDescriptor>())).RuleOutcomes.Should().ContainSingle().Which.State.Should().Be(RuleExecutionState.Unavailable);

            using var loaded = DataGuardApi.CreatePipeline(Config())
                .WithProviderRules(Array.Empty<IContractRule>())
                .WithPlugins(dir, new PluginTrustPolicy { RequireSignedProvenance = false }, null);
            Action replace = () => loaded.WithProviderRules(Array.Empty<IContractRule>(), new[] { Unavailable() });
            replace.Should().Throw<InvalidOperationException>();
            loaded.Rules.Should().ContainSingle().Which.RuleId.Should().Be("OFFLINE1");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private sealed class TestRule(string id, bool emitViolation = false, bool throwOnValidation = false) : IContractRule
    {
        public string RuleId => id;
        public string Name => id;
        public string Description => "Availability test rule";
        public Microsoft.CodeAnalysis.DiagnosticSeverity Severity => Microsoft.CodeAnalysis.DiagnosticSeverity.Warning;

        public Task<IReadOnlyList<ContractViolation>> ValidateAsync(ContractDescriptor contract, IReadOnlyList<ContractDescriptor> allContracts, CancellationToken cancellationToken = default) =>
            throwOnValidation ? throw new InvalidOperationException("test execution failure") :
                Task.FromResult<IReadOnlyList<ContractViolation>>(emitViolation ? new[] { new ContractViolation(id, "custom finding", Severity) } : Array.Empty<ContractViolation>());
    }
}
