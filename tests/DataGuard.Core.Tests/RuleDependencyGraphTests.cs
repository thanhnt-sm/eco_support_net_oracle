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
    public void CircularDependency_Throws()
    {
        var graph = new RuleDependencyGraph();
        graph.AddRule(new ParameterCountRule(), "DG002");
        graph.AddRule(new ParameterTypeMatchRule(), "DG101");

        var act = () => graph.GetParallelGroups();
        act.Should().Throw<InvalidOperationException>();
    }
}
