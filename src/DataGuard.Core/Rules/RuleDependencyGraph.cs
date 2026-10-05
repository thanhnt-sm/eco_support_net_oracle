using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using DataGuard.Core.Abstractions;

namespace DataGuard.Core.Rules;

/// <summary>
/// Rule dependency graph for optimal execution order.
/// Rules with no dependencies run first; dependent rules run after their dependencies.
/// Uses topological sorting; every iteration is in <see cref="StringComparer.Ordinal"/> order so plans are deterministic.
/// </summary>
public sealed class RuleDependencyGraph
{
    private readonly Dictionary<string, RuleNode> _nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SortedSet<string>> _dependencies = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SortedSet<string>> _dependents = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers a rule with its dependencies. Registering the same rule instance again is idempotent (new dependencies
    /// are added); a dependency placeholder with the same ID is upgraded to the rule.
    /// </summary>
    /// <exception cref="InvalidOperationException">A different rule instance is already registered under the same ID.</exception>
    public void RegisterRule(IContractRule rule, IEnumerable<string>? dependsOn = null)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var ruleId = rule.RuleId;
        if (string.IsNullOrWhiteSpace(ruleId))
        {
            throw new ArgumentException($"Rule {rule.GetType().FullName} has no rule ID.", nameof(rule));
        }

        if (_nodes.TryGetValue(ruleId, out var existing) && existing.Rule is not null && !ReferenceEquals(existing.Rule, rule))
        {
            throw new InvalidOperationException(
                $"Rule ID '{ruleId}' is already registered by {existing.Rule.GetType().FullName}; {rule.GetType().FullName} cannot reuse it.");
        }

        EnsureNode(ruleId);
        _nodes[ruleId] = new RuleNode(rule);
        AddDependencies(ruleId, dependsOn);
    }

    /// <summary>
    /// Declares that <paramref name="ruleId"/> depends on <paramref name="dependsOn"/> without registering an
    /// implementation. Every ID stays an unresolved placeholder until a rule with that ID is registered; a graph with
    /// placeholders fails <see cref="Validate"/> and cannot produce an execution plan.
    /// </summary>
    public void RegisterDependencies(string ruleId, IEnumerable<string>? dependsOn = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);
        EnsureNode(ruleId);
        AddDependencies(ruleId, dependsOn);
    }

    /// <summary>
    /// Gets the optimal execution order using topological sort.
    /// Rules with no dependencies come first; rules that depend on others come later.
    /// </summary>
    /// <returns>The registered rules in dependency order.</returns>
    public ImmutableArray<IContractRule> GetExecutionOrder()
    {
        EnsureAllDependenciesImplemented();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
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

    private void EnsureNode(string ruleId)
    {
        if (!_nodes.ContainsKey(ruleId))
        {
            _nodes[ruleId] = new RuleNode(null);
            _dependencies[ruleId] = new SortedSet<string>(StringComparer.Ordinal);
            _dependents[ruleId] = new SortedSet<string>(StringComparer.Ordinal);
        }
    }

    private void AddDependencies(string ruleId, IEnumerable<string>? dependsOn)
    {
        if (dependsOn == null)
        {
            return;
        }

        foreach (var depId in dependsOn)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(depId);
            EnsureNode(depId);
            _dependencies[ruleId].Add(depId);
            _dependents[depId].Add(ruleId);
        }
    }

    private void Visit(string nodeId, HashSet<string> visited, HashSet<string> visiting, List<IContractRule> result)
    {
        if (visiting.Contains(nodeId))
        {
            throw new InvalidOperationException($"Circular dependency detected involving rule: {nodeId}");
        }

        if (visited.Contains(nodeId))
        {
            return;
        }

        visiting.Add(nodeId);

        // Visit dependencies first; SortedSet iterates in ordinal order, so the plan is deterministic.
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
            result.Add(_nodes[nodeId].Rule!);
        }
    }

    /// <summary>
    /// Gets parallelizable groups of rules (rules that can run concurrently), each group in ordinal rule ID order.
    /// </summary>
    /// <returns>Levels of rules; every rule's dependencies are in an earlier level.</returns>
    public ImmutableArray<ImmutableArray<IContractRule>> GetParallelGroups()
    {
        EnsureAllDependenciesImplemented();
        var levels = new List<List<IContractRule>>();
        var remaining = new HashSet<string>(_nodes.Keys, StringComparer.Ordinal);
        var completed = new HashSet<string>(StringComparer.Ordinal);

        while (remaining.Count > 0)
        {
            var currentLevel = new List<IContractRule>();
            var completedThisRound = new List<string>();
            var completedBeforeRound = completed.ToHashSet(StringComparer.Ordinal);

            foreach (var nodeId in remaining.OrderBy(id => id, StringComparer.Ordinal).ToList())
            {
                if (_dependencies[nodeId].IsSubsetOf(completedBeforeRound))
                {
                    if (_nodes[nodeId].Rule != null)
                    {
                        currentLevel.Add(_nodes[nodeId].Rule!);
                    }

                    completed.Add(nodeId);
                    completedThisRound.Add(nodeId);
                }
            }

            remaining.RemoveWhere(completed.Contains);

            if (currentLevel.Count == 0)
            {
                if (completedThisRound.Count == 0)
                {
                    // Circular dependency
                    var stuck = remaining.Except(completed).OrderBy(id => id, StringComparer.Ordinal).ToList();
                    throw new InvalidOperationException($"Cannot resolve dependencies for rules: {string.Join(", ", stuck)}");
                }

                continue;
            }

            levels.Add(currentLevel);
        }

        return levels.Select(l => l.ToImmutableArray()).ToImmutableArray();
    }

    /// <summary>
    /// Validates the dependency graph for circular dependencies and missing dependencies.
    /// </summary>
    /// <returns>Errors (unresolved placeholders, cycles) and warnings (isolated rules).</returns>
    public ValidationResult Validate()
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        var unresolved = _nodes.Where(entry => entry.Value.Rule == null)
            .Select(entry => entry.Key)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        foreach (var nodeId in unresolved)
        {
            errors.Add($"Rule dependency '{nodeId}' has no registered implementation");
        }

        // Check for circular dependencies
        if (unresolved.Length == 0)
        {
            try
            {
                GetExecutionOrder();
            }
            catch (InvalidOperationException ex)
            {
                errors.Add($"Circular dependency: {ex.Message}");
            }
        }

        // Check for orphaned rules (no dependents, not depended upon)
        var allDepIds = _dependencies.Values.SelectMany(d => d).ToHashSet(StringComparer.Ordinal);
        var allDependentIds = _dependents.Values.SelectMany(d => d).ToHashSet(StringComparer.Ordinal);

        foreach (var nodeId in _nodes.Keys.OrderBy(id => id, StringComparer.Ordinal))
        {
            if (!allDepIds.Contains(nodeId) && !allDependentIds.Contains(nodeId))
            {
                warnings.Add($"Rule '{nodeId}' is isolated (no dependencies or dependents)");
            }
        }

        return new ValidationResult(errors.ToImmutableArray(), warnings.ToImmutableArray());
    }

    private void EnsureAllDependenciesImplemented()
    {
        var missing = _nodes.Where(entry => entry.Value.Rule == null)
            .Select(entry => entry.Key)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException($"Rule dependencies have no registered implementation: {string.Join(", ", missing)}");
        }
    }

    /// <summary>
    /// Gets all rules that depend on the given rule (transitive), in ordinal order.
    /// </summary>
    /// <returns>Dependent rule IDs.</returns>
    public ImmutableArray<string> GetTransitiveDependents(string ruleId) => Transitive(ruleId, _dependents);

    /// <summary>
    /// Gets all rules that the given rule depends on (transitive), in ordinal order.
    /// </summary>
    /// <returns>Dependency rule IDs.</returns>
    public ImmutableArray<string> GetTransitiveDependencies(string ruleId) => Transitive(ruleId, _dependencies);

    private static ImmutableArray<string> Transitive(string ruleId, Dictionary<string, SortedSet<string>> edges)
    {
        var result = new SortedSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(ruleId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (edges.TryGetValue(current, out var next))
            {
                foreach (var id in next)
                {
                    if (result.Add(id))
                    {
                        queue.Enqueue(id);
                    }
                }
            }
        }

        return result.ToImmutableArray();
    }
}

/// <summary>
/// Node in the dependency graph; <see cref="Rule"/> is null for a declared-but-unimplemented dependency.
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
    /// <summary>
    /// Declares dependency edges for <paramref name="ruleId"/> without an implementation. The ID stays an unresolved
    /// placeholder (never a no-op rule) until a rule with that ID is registered.
    /// </summary>
    /// <returns>The same graph.</returns>
    public static RuleDependencyGraph WithDependency(this RuleDependencyGraph graph, string ruleId, params string[] dependsOn)
    {
        ArgumentNullException.ThrowIfNull(graph);
        graph.RegisterDependencies(ruleId, dependsOn);
        return graph;
    }

    /// <summary>Registers <paramref name="rule"/> with its dependencies.</summary>
    /// <returns>The same graph.</returns>
    public static RuleDependencyGraph AddRule(this RuleDependencyGraph graph, IContractRule rule, params string[] dependsOn)
    {
        ArgumentNullException.ThrowIfNull(graph);
        graph.RegisterRule(rule, dependsOn);
        return graph;
    }
}

/// <summary>
/// Dependency edges between DataGuard built-in rules and the default (provider-neutral) core rule set. The CLI's
/// <c>ProviderRuleCatalog</c> registers the same core rule IDs (configured per provider) and composes them with
/// <see cref="Create"/>; a test keeps both sets in sync.
/// </summary>
public static class BuiltInRuleDependencies
{
    /// <summary>
    /// Built-in dependency edges by rule ID. An edge is applied only when both rules are part of the composed set, so
    /// skipping a rule (<c>--skip-rules</c>) never leaves an unresolved placeholder behind.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Edges { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            // Parameter direction and column shape depend on parameter existence (DG101).
            ["DG003"] = new[] { "DG101" },
            ["DG004"] = new[] { "DG101" },

            // Nullable matching depends on parameter type info (DG002).
            ["DG005"] = new[] { "DG002" },

            // Naming convention depends on parameter and column names.
            ["DG006"] = new[] { "DG004", "DG101" },
        };

    /// <summary>Creates the provider-neutral core rules (unconfigured, connectionless) in registration order.</summary>
    /// <returns>A fresh instance of every core rule.</returns>
    public static IReadOnlyList<IContractRule> CreateDefaultRules() => new IContractRule[]
    {
        new ParameterCountRule(),
        new ParameterTypeMatchRule(),
        new ParameterDirectionRule(),
        new ColumnShapeMatchRule(),
        new NullableMismatchRule(),
        new NamingConventionRule(),

        // Phantom identifiers (schema ground truth): DG015 table, DG016 column; DG019 parse status.
        new PhantomTableRule(),
        new PhantomColumnRule(),
        new RawSqlParseStatusRule(),
        new SelectStarUsageRule(),

        // DG018 without a connection is the offline variant (no describe); the CLI binds it to the connection.
        new LiveSqlShapeValidationRule(),
    };

    /// <summary>Creates the default graph: <see cref="CreateDefaultRules"/> composed with <see cref="Edges"/>.</summary>
    /// <returns>A new graph.</returns>
    public static RuleDependencyGraph CreateDefault() => Create(CreateDefaultRules());

    /// <summary>
    /// Composes <paramref name="rules"/> (for example the CLI provider catalog plus plugins) into one graph, applying
    /// <see cref="Edges"/> between rules that are present. Duplicate rule IDs throw (see <see cref="RuleDependencyGraph.RegisterRule"/>).
    /// </summary>
    /// <returns>A new graph.</returns>
    public static RuleDependencyGraph Create(IEnumerable<IContractRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var list = rules.ToList();
        var present = list.Select(rule => rule.RuleId).ToHashSet(StringComparer.Ordinal);
        var graph = new RuleDependencyGraph();
        foreach (var rule in list)
        {
            var dependsOn = Edges.TryGetValue(rule.RuleId, out var edges)
                ? edges.Where(present.Contains).ToArray()
                : Array.Empty<string>();
            graph.RegisterRule(rule, dependsOn);
        }

        return graph;
    }
}
