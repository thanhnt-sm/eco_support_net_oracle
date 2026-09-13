using DataGuard.Core.Rules;
using FluentAssertions;
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
    public void WithDependency_RegistersDependency()
    {
        var graph = new RuleDependencyGraph();
        graph.WithDependency("DG101");
        graph.WithDependency("CUSTOM001", "DG101");

        var validation = graph.Validate();
        validation.IsValid.Should().BeTrue();
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
        ruleIds.Should().Contain("DG015");  // PhantomIdentifierRule
    }

    [Fact]
    public void CreateDefault_ExecutionOrder_HasEightRules()
    {
        var order = BuiltInRuleDependencies.CreateDefault().GetExecutionOrder();
        order.Length.Should().Be(8);
    }
}
