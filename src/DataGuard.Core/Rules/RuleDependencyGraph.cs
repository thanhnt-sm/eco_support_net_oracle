using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using DataGuard.Core.Abstractions;
using Microsoft.CodeAnalysis;

namespace DataGuard.Core.Rules;

/// <summary>
/// Rule dependency graph for optimal execution order.
/// Rules with no dependencies run first; dependent rules run after their dependencies.
/// Uses topological sorting for optimal execution order.
/// </summary>
public sealed class RuleDependencyGraph
{
    private readonly Dictionary<string, RuleNode> _nodes = new();
    private readonly Dictionary<string, HashSet<string>> _dependencies = new();
    private readonly Dictionary<string, HashSet<string>> _dependents = new();

    /// <summary>
    /// Registers a rule with its dependencies.
    /// </summary>
    public void RegisterRule(IContractRule rule, IEnumerable<string>? dependsOn = null)
    {
        var ruleId = rule.RuleId;
        
        if (!_nodes.ContainsKey(ruleId))
        {
            _nodes[ruleId] = new RuleNode(rule);
            _dependencies[ruleId] = new HashSet<string>();
            _dependents[ruleId] = new HashSet<string>();
        }
        else if (_nodes[ruleId].Rule == null)
        {
            // The node existed as a dependency placeholder - upgrade it to the real rule.
            _nodes[ruleId] = new RuleNode(rule);
        }

        if (dependsOn != null)
        {
            foreach (var depId in dependsOn)
            {
                if (!_nodes.ContainsKey(depId))
                {
                    // Register placeholder for dependency
                    _nodes[depId] = new RuleNode(null);
                    _dependencies[depId] = new HashSet<string>();
                    _dependents[depId] = new HashSet<string>();
                }

                _dependencies[ruleId].Add(depId);
                _dependents[depId].Add(ruleId);
            }
        }
    }

    /// <summary>
    /// Gets the optimal execution order using topological sort.
    /// Rules with no dependencies come first; rules that depend on others come later.
    /// </summary>
    public ImmutableArray<IContractRule> GetExecutionOrder()
    {
        var visited = new HashSet<string>();
        var visiting = new HashSet<string>();
        var result = new List<IContractRule>();

        foreach (var nodeId in _nodes.Keys.OrderBy(id => id, StringComparer.Ordinal))
        {
            if (!visited.Contains(nodeId))
            {
                Visit(nodeId, visited, visiting, result);
            }
        }

        return result.ToImmutableArray();
    }

    private void Visit(string nodeId, HashSet<string> visited, HashSet<string> visiting, List<IContractRule> result)
    {
        if (visiting.Contains(nodeId))
        {
            throw new InvalidOperationException($"Circular dependency detected involving rule: {nodeId}");
        }

        if (visited.Contains(nodeId))
            return;

        visiting.Add(nodeId);

        // Visit dependencies first
        if (_dependencies.TryGetValue(nodeId, out var deps))
        {
            foreach (var depId in deps)
            {
                Visit(depId, visited, visiting, result);
            }
        }

        visiting.Remove(nodeId);
        visited.Add(nodeId);

        if (_nodes[nodeId].Rule != null)
        {
            result.Add(_nodes[nodeId].Rule);
        }
    }

    /// <summary>
    /// Gets parallelizable groups of rules (rules that can run concurrently).
    /// </summary>
    public ImmutableArray<ImmutableArray<IContractRule>> GetParallelGroups()
    {
        var levels = new List<List<IContractRule>>();
        var remaining = new HashSet<string>(_nodes.Keys);
        var completed = new HashSet<string>();

        while (remaining.Count > 0)
        {
            var currentLevel = new List<IContractRule>();
            var completedThisRound = new List<string>();

            foreach (var nodeId in remaining.OrderBy(id => id, StringComparer.Ordinal).ToList())
            {
                var deps = _dependencies.GetValueOrDefault(nodeId, new HashSet<string>());
                if (deps.IsSubsetOf(completed))
                {
                    if (_nodes[nodeId].Rule != null)
                    {
                        currentLevel.Add(_nodes[nodeId].Rule);
                    }
                    completed.Add(nodeId);
                    completedThisRound.Add(nodeId);
                }
            }

            // Remove every completed node (including dependency placeholders) from
            // the remaining set; otherwise placeholders spin forever.
            remaining.RemoveWhere(completed.Contains);

            if (currentLevel.Count == 0)
            {
                if (completedThisRound.Count == 0)
                {
                    // Circular dependency or missing dependency
                    var stuck = remaining.Except(completed).ToList();
                    throw new InvalidOperationException($"Cannot resolve dependencies for rules: {string.Join(", ", stuck)}");
                }
                // Only placeholders were resolved this round - continue with the next level.
                continue;
            }

            levels.Add(currentLevel);
        }

        return levels.Select(l => l.ToImmutableArray()).ToImmutableArray();
    }

    /// <summary>
    /// Validates the dependency graph for circular dependencies and missing dependencies.
    /// </summary>
    public ValidationResult Validate()
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // Check for circular dependencies
        try
        {
            GetExecutionOrder();
        }
        catch (InvalidOperationException ex)
        {
            errors.Add($"Circular dependency: {ex.Message}");
        }

        // Check for missing dependencies
        foreach (var kvp in _dependencies)
        {
            foreach (var depId in kvp.Value)
            {
                if (!_nodes.ContainsKey(depId))
                {
                    warnings.Add($"Rule '{kvp.Key}' depends on missing rule '{depId}'");
                }
            }
        }

        // Check for orphaned rules (no dependents, not depended upon)
        var allDepIds = _dependencies.Values.SelectMany(d => d).ToHashSet();
        var allDependentIds = _dependents.Values.SelectMany(d => d).ToHashSet();
        
        foreach (var nodeId in _nodes.Keys)
        {
            if (!allDepIds.Contains(nodeId) && !allDependentIds.Contains(nodeId))
            {
                warnings.Add($"Rule '{nodeId}' is isolated (no dependencies or dependents)");
            }
        }

        return new ValidationResult(errors.ToImmutableArray(), warnings.ToImmutableArray());
    }

    /// <summary>
    /// Gets all rules that depend on the given rule (transitive).
    /// </summary>
    public ImmutableArray<string> GetTransitiveDependents(string ruleId)
    {
        var result = new HashSet<string>();
        var queue = new Queue<string>();
        queue.Enqueue(ruleId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (_dependents.TryGetValue(current, out var dependents))
            {
                foreach (var dep in dependents)
                {
                    if (result.Add(dep))
                    {
                        queue.Enqueue(dep);
                    }
                }
            }
        }

        return result.ToImmutableArray();
    }

    /// <summary>
    /// Gets all rules that the given rule depends on (transitive).
    /// </summary>
    public ImmutableArray<string> GetTransitiveDependencies(string ruleId)
    {
        var result = new HashSet<string>();
        var queue = new Queue<string>();
        queue.Enqueue(ruleId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (_dependencies.TryGetValue(current, out var deps))
            {
                foreach (var dep in deps)
                {
                    if (result.Add(dep))
                    {
                        queue.Enqueue(dep);
                    }
                }
            }
        }

        return result.ToImmutableArray();
    }
}

/// <summary>
/// Node in the dependency graph.
/// </summary>
internal sealed class RuleNode
{
    public IContractRule? Rule { get; }

    public RuleNode(IContractRule? rule)
    {
        Rule = rule;
    }
}

/// <summary>
/// Validation result for the dependency graph.
/// </summary>
public sealed record ValidationResult(
    ImmutableArray<string> Errors,
    ImmutableArray<string> Warnings)
{
    public bool IsValid => Errors.Length == 0;
}

/// <summary>
/// Extension methods for building rule dependency graphs fluently.
/// </summary>
public static class RuleDependencyGraphExtensions
{
    public static RuleDependencyGraph WithDependency(this RuleDependencyGraph graph, string ruleId, params string[] dependsOn)
    {
        var rule = new DummyRule(ruleId); // Placeholder
        graph.RegisterRule(rule, dependsOn);
        return graph;
    }

    public static RuleDependencyGraph AddRule(this RuleDependencyGraph graph, IContractRule rule, params string[] dependsOn)
    {
        graph.RegisterRule(rule, dependsOn);
        return graph;
    }
}

/// <summary>
/// Dummy rule for registering dependencies without actual implementation.
/// </summary>
internal sealed class DummyRule : IContractRule
{
    public string RuleId { get; set; }
    public string Name => RuleId;
    public DiagnosticSeverity Severity => Microsoft.CodeAnalysis.DiagnosticSeverity.Warning;
    public string Description => "Dependency placeholder";

    public DummyRule(string ruleId)
    {
        RuleId = ruleId;
    }

    public Task<IReadOnlyList<ContractViolation>> ValidateAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ContractViolation>>(Array.Empty<ContractViolation>());
}

/// <summary>
/// Pre-configured dependency graph for DataGuard built-in rules.
/// </summary>
public static class BuiltInRuleDependencies
{
    public static RuleDependencyGraph CreateDefault()
    {
        var graph = new RuleDependencyGraph();

        // Register all built-in rules with their dependencies
        // Order: Parameter checks -> Column checks -> Type checks -> Naming -> Nullable

        // Level 1: Basic parameter checks (no dependencies)
        graph.AddRule(new ParameterCountRule());
        graph.AddRule(new ParameterTypeMatchRule());

        // Level 2: Parameter direction (depends on parameter existence)
        graph.AddRule(new ParameterDirectionRule(), "DG101");

        // Level 3: Column shape (depends on parameter existence)
        graph.AddRule(new ColumnShapeMatchRule(), "DG101");

        // Level 4: Nullable and type matching (depends on parameter type info)
        graph.AddRule(new NullableMismatchRule(), "DG002");

        // Level 5: Naming convention (depends on parameter/column names)
        graph.AddRule(new NamingConventionRule(), "DG101", "DG004");

        // Level 6: Phantom identifiers (schema ground truth)
        graph.AddRule(new PhantomIdentifierRule());
        return graph;
    }
}