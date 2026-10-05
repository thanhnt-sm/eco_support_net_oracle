using DataGuard.Core.Abstractions;
using DataGuard.Core.Rules;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DataGuard.Core.Tests;

public class RuleDependencyGraphTests
{
    [Fact]
    public void CreateDefault_ExecutionOrder_IsDeterministic()
    {
        var first = BuiltInRuleDependencies.CreateDefault().GetExecutionOrder();
        var second = BuiltInRuleDependencies.CreateDefault().GetExecutionOrder();

        first.Select(r => r.RuleId).Should().Equal(second.Select(r => r.RuleId));
        first.Should().NotBeEmpty();
    }

    [Fact]
    public void CreateDefault_ParallelGroups_ResolvesWithoutThrowing()
    {
        var groups = BuiltInRuleDependencies.CreateDefault().GetParallelGroups();

        var allRuleIds = groups.SelectMany(g => g).Select(r => r.RuleId).ToHashSet();
        allRuleIds.Should().Contain("DG101");
        allRuleIds.Should().Contain("DG015");
    }

    [Fact]
    public void CreateDefault_Validate_HasNoErrors()
    {
        BuiltInRuleDependencies.CreateDefault().Validate().IsValid.Should().BeTrue();
    }

    [Fact]
    public void DependencyOrder_RunsDependencyBeforeDependent()
    {
        var graph = new RuleDependencyGraph();
        graph.AddRule(new NamingConventionRule(), "DG101");
        graph.AddRule(new ParameterCountRule());

        var order = graph.GetExecutionOrder().Select(r => r.RuleId).ToList();
        order.IndexOf("DG101").Should().BeLessThan(order.IndexOf("DG006"));
    }

    [Fact]
    public void UnregisteredDependency_IsValidationErrorAndCannotExecute()
    {
        var graph = new RuleDependencyGraph();
        graph.AddRule(new NamingConventionRule(), "MISSING");

        graph.Validate().IsValid.Should().BeFalse();
        var act = () => graph.GetExecutionOrder();
        act.Should().Throw<InvalidOperationException>().WithMessage("*MISSING*");
    }

    [Fact]
    public void ForwardDependency_IsValidAfterRegistration()
    {
        var graph = new RuleDependencyGraph();
        graph.AddRule(new NamingConventionRule(), "DG101");
        graph.AddRule(new ParameterCountRule());

        graph.Validate().IsValid.Should().BeTrue();
        graph.GetExecutionOrder().Select(rule => rule.RuleId).Should().ContainInOrder("DG101", "DG006");
    }

    [Fact]
    public void ParallelGroups_SeparateDependencyFromDependent()
    {
        var graph = new RuleDependencyGraph();
        graph.AddRule(new NamingConventionRule(), "DG101");
        graph.AddRule(new ParameterCountRule());

        var groups = graph.GetParallelGroups();

        groups.Should().HaveCount(2);
        groups[0].Select(rule => rule.RuleId).Should().ContainSingle().Which.Should().Be("DG101");
        groups[1].Select(rule => rule.RuleId).Should().ContainSingle().Which.Should().Be("DG006");
    }

    [Fact]
    public void CircularDependency_Throws()
    {
        var graph = new RuleDependencyGraph();
        graph.AddRule(new ParameterCountRule(), "DG002");
        graph.AddRule(new ParameterTypeMatchRule(), "DG101");

        var act = () => graph.GetParallelGroups();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void WithDependency_FluentApi_ReturnsGraph()
    {
        var graph = new RuleDependencyGraph();
        var result = graph.WithDependency("CUSTOM001", "DG101");

        result.Should().BeSameAs(graph);
    }

    [Fact]
    public void WithDependency_RegistersPlaceholdersThatNeverRunAsRules()
    {
        var graph = new RuleDependencyGraph();
        graph.WithDependency("CUSTOM001", "DG101");

        // Placeholders are not no-op rules: the graph is invalid and cannot produce a plan until both are implemented.
        graph.Validate().Errors.Should().Contain(error => error.Contains("CUSTOM001")).And.Contain(error => error.Contains("DG101"));
        var act = () => graph.GetExecutionOrder();
        act.Should().Throw<InvalidOperationException>().WithMessage("*CUSTOM001*");
    }

    [Fact]
    public void WithDependency_PlaceholdersAreUpgradedByRealRules()
    {
        var graph = new RuleDependencyGraph();
        graph.WithDependency("CUSTOM001", "DG101");
        graph.AddRule(new StubRule("CUSTOM001"));
        graph.AddRule(new ParameterCountRule());

        graph.Validate().IsValid.Should().BeTrue();
        graph.GetExecutionOrder().Select(rule => rule.RuleId).Should().Equal("DG101", "CUSTOM001");
        graph.GetExecutionOrder().Should().NotContain(rule => rule.GetType().Name == "DummyRule");
    }

    [Fact]
    public void RegisterRule_DifferentInstanceWithSameId_Throws()
    {
        var graph = new RuleDependencyGraph();
        graph.AddRule(new ParameterCountRule());

        var act = () => graph.AddRule(new ParameterCountRule());

        act.Should().Throw<InvalidOperationException>().WithMessage("*DG101*already registered*");
    }

    [Fact]
    public void RegisterRule_SameInstance_IsIdempotentAndKeepsNewDependencies()
    {
        var graph = new RuleDependencyGraph();
        var naming = new NamingConventionRule();
        graph.AddRule(new ParameterCountRule());
        graph.AddRule(naming);
        graph.AddRule(naming, "DG101");

        graph.GetExecutionOrder().Should().HaveCount(2);
        graph.GetTransitiveDependencies("DG006").Should().Equal("DG101");
    }

    [Fact]
    public void Dependencies_IterateInOrdinalOrder_RegardlessOfRegistrationOrder()
    {
        string[] Plan(params string[] dependencyOrder)
        {
            var graph = new RuleDependencyGraph();
            foreach (var id in new[] { "b", "B", "a", "A" })
            {
                graph.AddRule(new StubRule(id));
            }

            graph.AddRule(new StubRule("Z"), dependencyOrder);
            return graph.GetExecutionOrder().Select(rule => rule.RuleId).ToArray();
        }

        var expected = new[] { "A", "B", "a", "b", "Z" };
        Plan("b", "a", "B", "A").Should().Equal(expected);
        Plan("A", "B", "a", "b").Should().Equal(expected);
    }

    [Fact]
    public void TransitiveQueries_ReturnOrdinalOrder()
    {
        var graph = new RuleDependencyGraph();
        graph.AddRule(new StubRule("b"));
        graph.AddRule(new StubRule("A"));
        graph.AddRule(new StubRule("C"), "b", "A");

        graph.GetTransitiveDependencies("C").Should().Equal("A", "b");
        graph.GetTransitiveDependents("b").Should().Equal("C");
    }

    [Fact]
    public void BuiltInCoreRuleIds_MatchProviderRuleCatalogCoreRuleIds()
    {
        // Core rules are the IDs every provider registers; BuiltInRuleDependencies must stay in sync with the CLI catalog.
        var providers = new[] { "sqlserver", "oracle", "postgresql", "mysql" };
        var catalogCore = providers
            .Select(provider => DataGuard.Cli.ProviderRuleCatalog.Get(provider).Select(registration => registration.Rule.RuleId).ToHashSet(StringComparer.Ordinal))
            .Aggregate((left, right) => left.Intersect(right, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal));
        catalogCore.Remove("DG010"); // dialect rule shared by three providers, not a core rule

        BuiltInRuleDependencies.CreateDefaultRules().Select(rule => rule.RuleId)
            .Should().BeEquivalentTo(catalogCore);
        BuiltInRuleDependencies.CreateDefault().GetExecutionOrder().Select(rule => rule.RuleId)
            .Should().BeEquivalentTo(catalogCore);
    }

    [Fact]
    public void Create_DropsEdgesToRulesThatAreNotComposed()
    {
        // --skip-rules DG101: DG003/DG004/DG006 lose that edge instead of leaving an unresolved placeholder.
        var graph = BuiltInRuleDependencies.Create(BuiltInRuleDependencies.CreateDefaultRules().Where(rule => rule.RuleId != "DG101"));

        graph.Validate().IsValid.Should().BeTrue();
        graph.GetExecutionOrder().Select(rule => rule.RuleId).Should().NotContain("DG101").And.Contain("DG003");
    }

    [Fact]
    public void Create_DuplicateRuleIds_Throws()
    {
        var act = () => BuiltInRuleDependencies.Create(new IContractRule[] { new StubRule("X1"), new StubRule("X1") });

        act.Should().Throw<InvalidOperationException>().WithMessage("*X1*");
    }

    [Fact]
    public void AddRule_FluentApi_ReturnsGraph()
    {
        var graph = new RuleDependencyGraph();
        var result = graph.AddRule(new ParameterCountRule());

        result.Should().BeSameAs(graph);
    }

    [Fact]
    public void CreateDefault_ContainsAllBuiltInRules()
    {
        var graph = BuiltInRuleDependencies.CreateDefault();
        var order = graph.GetExecutionOrder();
        var ruleIds = order.Select(r => r.RuleId).ToHashSet();

        ruleIds.Should().Contain("DG101");  // ParameterCountRule
        ruleIds.Should().Contain("DG002");  // ParameterTypeMatchRule
        ruleIds.Should().Contain("DG003");  // ParameterDirectionRule
        ruleIds.Should().Contain("DG004");  // ColumnShapeMatchRule
        ruleIds.Should().Contain("DG005");  // NullableMismatchRule
        ruleIds.Should().Contain("DG006");  // NamingConventionRule
        ruleIds.Should().Contain("DG015");  // PhantomTableRule
        ruleIds.Should().Contain("DG016");  // PhantomColumnRule
        ruleIds.Should().Contain("DG019");  // RawSqlParseStatusRule
        ruleIds.Should().Contain("DG017");  // SelectStarUsageRule
    }

    [Fact]
    public void CreateDefault_ExecutionOrder_HasElevenRules()
    {
        var order = BuiltInRuleDependencies.CreateDefault().GetExecutionOrder();
        order.Length.Should().Be(11);
        order.Select(r => r.RuleId).Should().OnlyHaveUniqueItems().And.Contain("DG018");
    }

    private sealed class StubRule : IContractRule
    {
        public StubRule(string ruleId) => RuleId = ruleId;

        public string RuleId { get; }

        public string Name => RuleId;

        public DiagnosticSeverity Severity => DiagnosticSeverity.Warning;

        public string Description => "test stub";

        public Task<IReadOnlyList<ContractViolation>> ValidateAsync(
            ContractDescriptor contract,
            IReadOnlyList<ContractDescriptor> allContracts,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ContractViolation>>(Array.Empty<ContractViolation>());
    }
}
