using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Composition.Convention;
using System.Composition.Hosting;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DataGuard.Core.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace DataGuard.Core.Plugins;

/// <summary>
/// Metadata for rule plugins in MEF 2.
/// </summary>
public class RulePluginMetadata : IRuleMetadata
{
    public string RuleId { get; }
    public string Name { get; }
    public string Description { get; }
    public string Category { get; }
    public string DefaultSeverity { get; }
    public string MinDataGuardVersion { get; }
    public string Author { get; }
    public string[] Tags { get; }

    public RulePluginMetadata(IDictionary<string, object> metadata)
    {
        RuleId = metadata?.TryGetValue("RuleId", out var id) == true ? id?.ToString() ?? "" : "";
        Name = metadata?.TryGetValue("Name", out var name) == true ? name?.ToString() ?? "" : "";
        Description = metadata?.TryGetValue("Description", out var desc) == true ? desc?.ToString() ?? "" : "";
        Category = metadata?.TryGetValue("Category", out var cat) == true ? cat?.ToString() ?? "Custom" : "Custom";
        DefaultSeverity = metadata?.TryGetValue("DefaultSeverity", out var sev) == true ? sev?.ToString() ?? "Warning" : "Warning";
        MinDataGuardVersion = metadata?.TryGetValue("MinDataGuardVersion", out var ver) == true ? ver?.ToString() ?? "1.0.0" : "1.0.0";
        Author = metadata?.TryGetValue("Author", out var auth) == true ? auth?.ToString() ?? "" : "";
        Tags = metadata?.TryGetValue("Tags", out var tags) == true && tags is string[] tagArray ? tagArray : Array.Empty<string>();
    }
}

/// <summary>
/// Plugin architecture for custom rules - allows external assemblies to extend DataGuard.
/// Uses MEF (Managed Extensibility Framework) for discovery and loading.
/// </summary>
public sealed class RulePluginManager : IDisposable
{
    private readonly CompositionHost _container;
    private readonly ILogger<RulePluginManager>? _logger;
    private readonly ImmutableArray<Lazy<IContractRule, IRuleMetadata>> _rulePlugins;

    public RulePluginManager(
        string? pluginDirectory = null,
        ILogger<RulePluginManager>? logger = null)
    {
        _logger = logger;
        
        var config = new ContainerConfiguration()
            .WithDefaultConventions(new ConventionBuilder());

        // Scan directory for assemblies
        var dir = pluginDirectory ?? GetDefaultPluginDirectory();
        if (Directory.Exists(dir))
        {
            foreach (var assemblyFile in Directory.GetFiles(dir, "*.dll"))
            {
                try
                {
                    var assembly = Assembly.LoadFrom(assemblyFile);
                    config = config.WithAssembly(assembly);
                }
                catch
                {
                    // Ignore load errors
                }
            }
        }

        _container = config.CreateContainer();
        
        // GetExports<T, TMetadata> doesn't exist in MEF 2, use GetExports<T>() 
        // and create Lazy with metadata manually
        _rulePlugins = _container.GetExports<IContractRule>()
            .Select(e => new Lazy<IContractRule, IRuleMetadata>(
                () => e,
                new RulePluginMetadata(e.GetType().GetCustomAttributes<ExportMetadataAttribute>().ToDictionary(a => a.Name, a => a.Value))))
            .ToImmutableArray();
        
        _logger?.LogInformation("Loaded {Count} rule plugins from {Directory}", 
            _rulePlugins.Length, pluginDirectory ?? GetDefaultPluginDirectory());
    }

    /// <summary>
    /// Gets all available rules including built-in and plugin rules.
    /// </summary>
    public ImmutableArray<IContractRule> GetAllRules(ImmutableArray<IContractRule> builtInRules)
    {
        var pluginRules = _rulePlugins
            .Where(p => IsCompatible(p.Metadata))
            .Select(p => p.Value)
            .ToImmutableArray();

        return builtInRules.AddRange(pluginRules);
    }

    /// <summary>
    /// Gets a specific rule by ID (checks both built-in and plugins).
    /// </summary>
    public IContractRule? GetRuleById(string ruleId, ImmutableArray<IContractRule> builtInRules)
    {
        var builtIn = builtInRules.FirstOrDefault(r => r.RuleId == ruleId);
        if (builtIn != null) return builtIn;

        return _rulePlugins
            .Where(p => p.Metadata.RuleId == ruleId && IsCompatible(p.Metadata))
            .Select(p => p.Value)
            .FirstOrDefault();
    }

    /// <summary>
    /// Gets all rule metadata for discovery/UI purposes.
    /// </summary>
    public ImmutableArray<IRuleMetadata> GetRuleMetadata()
    {
        return _rulePlugins.Select(p => p.Metadata).ToImmutableArray();
    }

    private static string GetDefaultPluginDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DataGuard",
            "Plugins");
    }

    private bool IsCompatible(IRuleMetadata metadata)
    {
        // Check version compatibility
        var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
        var minVersion = new Version(metadata.MinDataGuardVersion ?? "1.0.0");
        
        return currentVersion >= minVersion;
    }

    public void Dispose()
    {
        _container?.Dispose();
    }
}

/// <summary>
/// Metadata for rule plugins.
/// </summary>
public interface IRuleMetadata
{
    string RuleId { get; }
    string Name { get; }
    string Description { get; }
    string Category { get; }
    string DefaultSeverity { get; }
    string MinDataGuardVersion { get; }
    string Author { get; }
    string[] Tags { get; }
}

/// <summary>
/// Export attribute for rule plugins.
/// </summary>
[MetadataAttribute]
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class ExportRuleAttribute : ExportAttribute, IRuleMetadata
{
    public ExportRuleAttribute(string ruleId) : base(typeof(IContractRule))
    {
        RuleId = ruleId;
    }

    public string RuleId { get; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "Custom";
    public string DefaultSeverity { get; set; } = "Warning";
    public string MinDataGuardVersion { get; set; } = "1.0.0";
    public string Author { get; set; } = "";
    public string[] Tags { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Example custom rule plugin.
/// </summary>
[ExportRule("CUSTOM001",
    Name = "Custom Naming Convention",
    Description = "Enforces custom naming convention for specific schemas",
    Category = "Naming",
    DefaultSeverity = "Warning",
    MinDataGuardVersion = "1.0.0",
    Author = "DataGuard Team",
    Tags = new[] { "naming", "custom" }
)]
public sealed class CustomNamingConventionRule : IContractRule
{
    public string RuleId => "CUSTOM001";
    public string Name => "Custom Naming Convention";
    public Microsoft.CodeAnalysis.DiagnosticSeverity Severity => Microsoft.CodeAnalysis.DiagnosticSeverity.Warning;
    public string Description => "Enforces custom naming convention for specific schemas";

    public async Task<IReadOnlyList<ContractViolation>> ValidateAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<ContractViolation>();
        
        // Example: Check for specific naming pattern in Oracle schemas
        if (contract is StoredProcedureDescriptor sp && sp.Schema.StartsWith("LEGACY_", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var param in sp.Parameters)
            {
                if (!param.Name.StartsWith("P_", StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add(new ContractViolation(
                        RuleId: "CUSTOM001",
                        Message: $"Parameter '{param.Name}' in legacy schema procedure '{sp.Name}' should start with 'P_'",
                        Severity: DiagnosticSeverity.Warning));
                }
            }
        }

        return await Task.FromResult(violations);
    }
}

/// <summary>
/// Plugin for integrating with external tools (e.g., SonarQube, custom linters).
/// </summary>
public interface IExternalToolPlugin
{
    string ToolName { get; }
    string Version { get; }
    Task<PluginAnalysisResult> AnalyzeAsync(
        IReadOnlyList<ContractDescriptor> contracts,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result from external tool plugin.
/// </summary>
public sealed record PluginAnalysisResult(
    string ToolName,
    IReadOnlyList<ContractViolation> Violations,
    IReadOnlyList<PluginMetric> Metrics,
    TimeSpan Duration);

/// <summary>
/// Metric from external tool.
/// </summary>
public sealed record PluginMetric(
    string Name,
    double Value,
    string Unit,
    string Description);