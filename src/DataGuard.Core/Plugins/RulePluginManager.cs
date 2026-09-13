using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Composition.Convention;
using System.Composition.Hosting;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
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
    private readonly List<System.Runtime.Loader.AssemblyLoadContext> _pluginContexts = new();
    private readonly List<PluginAdmissionResult> _admissions = new();

    public RulePluginManager(
        string? pluginDirectory = null,
        ILogger<RulePluginManager>? logger = null,
        PluginTrustPolicy? trustPolicy = null,
        IPluginProvenanceVerifier? provenanceVerifier = null,
        IEnumerable<string>? reservedRuleIds = null)
    {
        _logger = logger;

        var config = new ContainerConfiguration()
            .WithDefaultConventions(new ConventionBuilder());

        // Scan directory for assemblies. Only an explicitly provided plugin directory is
        // scanned: the default location is user-writable and must never auto-load code.
        var dir = pluginDirectory;
        var policy = trustPolicy ?? new PluginTrustPolicy();
        if (dir != null && Directory.Exists(dir))
        {
            foreach (var admission in PluginAdmission.VerifyAll(
                Directory.GetFiles(dir, "*.dll"), policy, provenanceVerifier, reservedRuleIds))
            {
                var assemblyFile = admission.AssemblyPath;
                try
                {
                    _admissions.Add(admission);
                    if (!admission.Accepted)
                    {
                        _logger?.LogWarning("Rejected plugin assembly {AssemblyFile}: {Reason}", assemblyFile, admission.Reason);
                        continue;
                    }

                    // Bind the exact managed bytes hashed during admission. A path can
                    // be replaced after admission, so do not resolve it again at load time.
                    if (admission.VerifiedAssemblyBytes is null)
                    {
                        var rejected = admission with { Accepted = false, Reason = "Plugin did not retain verified assembly bytes." };
                        _admissions[_admissions.Count - 1] = rejected;
                        _logger?.LogWarning("Rejected plugin assembly {AssemblyFile}: {Reason}", assemblyFile, rejected.Reason);
                        continue;
                    }

                    // Load into an isolated collectible context so plugins can be
                    // unloaded. It is lifecycle/type-resolution isolation, not a sandbox.
                    var alc = new VerifiedPluginLoadContext(
                        $"DataGuard.Plugin:{Path.GetFileName(assemblyFile)}",
                        admission.VerifiedManagedDependencies ?? Array.Empty<VerifiedPluginDependency>(),
                        admission.VerifiedHostAssemblyIdentities ?? PluginAdmission.GetHostAssemblyIdentities(policy));
                    _pluginContexts.Add(alc);
                    using var verifiedStream = new MemoryStream(admission.VerifiedAssemblyBytes, writable: false);
                    var assembly = alc.LoadFromStream(verifiedStream);
                    NativeLibrary.SetDllImportResolver(assembly, static (_, _, _) =>
                        throw new DllNotFoundException("Native dependencies are not admitted for DataGuard plugins."));
                    config = config.WithAssembly(assembly);
                }
                catch (Exception ex)
                {
                    var rejected = admission with
                    {
                        Accepted = false,
                        Reason = $"Plugin load failed: {ex.Message}",
                        VerifiedAssemblyBytes = null,
                        VerifiedManagedDependencies = null,
                    };
                    _admissions[_admissions.Count - 1] = rejected;
                    _logger?.LogWarning(ex, "Skipping plugin assembly {AssemblyFile}: {Message}", assemblyFile, ex.Message);
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

        _logger?.LogInformation(
            "Loaded {Count} rule plugins from {Directory}",
            _rulePlugins.Length, pluginDirectory ?? GetDefaultPluginDirectory());
    }

    /// <summary>Gets admission results without loading rejected plugin code.</summary>
    public IReadOnlyList<PluginAdmissionResult> GetAdmissions() => _admissions
        .Select(admission => admission with
        {
            VerifiedAssemblyBytes = admission.VerifiedAssemblyBytes?.ToArray(),
            VerifiedManagedDependencies = admission.VerifiedManagedDependencies?
                .Select(dependency => dependency with { AssemblyBytes = dependency.AssemblyBytes.ToArray() })
                .ToArray(),
            VerifiedHostAssemblyIdentities = admission.VerifiedHostAssemblyIdentities is null
                ? null
                : new HashSet<string>(admission.VerifiedHostAssemblyIdentities, StringComparer.Ordinal),
        })
        .ToArray();

    /// <summary>
    /// Gets all available rules including built-in and plugin rules.
    /// </summary>
    /// <returns></returns>
    public ImmutableArray<IContractRule> GetAllRules(ImmutableArray<IContractRule> builtInRules)
    {
        var builtInRuleIds = builtInRules
            .Select(rule => rule.RuleId)
            .ToHashSet(StringComparer.Ordinal);
        var pluginRules = _rulePlugins
            .Where(p => IsCompatible(p.Metadata))
            .GroupBy(plugin => plugin.Metadata.RuleId, StringComparer.Ordinal)
            .Select(group => group.OrderBy(plugin => plugin.Metadata.Name, StringComparer.Ordinal).First())
            .Where(plugin => !builtInRuleIds.Contains(plugin.Metadata.RuleId))
            .Select(p => p.Value)
            .ToImmutableArray();

        return builtInRules.AddRange(pluginRules);
    }

    /// <summary>
    /// Gets a specific rule by ID (checks both built-in and plugins).
    /// </summary>
    /// <returns></returns>
    public IContractRule? GetRuleById(string ruleId, ImmutableArray<IContractRule> builtInRules)
    {
        var builtIn = builtInRules.FirstOrDefault(r => r.RuleId == ruleId);
        if (builtIn != null)
        {
            return builtIn;
        }

        return _rulePlugins
            .Where(p => p.Metadata.RuleId == ruleId && IsCompatible(p.Metadata))
            .Select(p => p.Value)
            .FirstOrDefault();
    }

    /// <summary>
    /// Gets all rule metadata for discovery/UI purposes.
    /// </summary>
    /// <returns></returns>
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
        // Check version compatibility (lenient parse: malformed metadata must not crash).
        var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
        if (currentVersion == null)
        {
            return false;
        }

        if (!Version.TryParse(metadata.MinDataGuardVersion ?? "", out var minVersion))
        {
            minVersion = new Version(1, 0, 0);
        }

        return currentVersion >= minVersion;
    }

    private sealed class VerifiedPluginLoadContext : System.Runtime.Loader.AssemblyLoadContext
    {
        private readonly IReadOnlyDictionary<string, byte[]> dependencies;

        private readonly IReadOnlySet<string> hostAssemblyIdentities;

        public VerifiedPluginLoadContext(
            string name,
            IEnumerable<VerifiedPluginDependency> verifiedDependencies,
            IReadOnlySet<string> hostAssemblyIdentities)
            : base(name, isCollectible: true)
        {
            dependencies = verifiedDependencies.ToDictionary(
                dependency => dependency.AssemblyName,
                dependency => dependency.AssemblyBytes.ToArray(),
                StringComparer.Ordinal);
            this.hostAssemblyIdentities = new HashSet<string>(hostAssemblyIdentities, StringComparer.Ordinal);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (!dependencies.TryGetValue(assemblyName.FullName ?? string.Empty, out var bytes))
            {
                if (hostAssemblyIdentities.Contains(assemblyName.FullName ?? string.Empty))
                {
                    return null;
                }

                throw new FileLoadException($"Plugin dependency '{assemblyName.FullName}' is not admitted.");
            }

            using var stream = new MemoryStream(bytes, writable: false);
            return LoadFromStream(stream);
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            throw new DllNotFoundException("Native dependencies are not admitted for DataGuard plugins.");
        }
    }

    public void Dispose()
    {
        _container?.Dispose();

        // Release plugin assemblies: drop container/export references first, then
        // unload each collectible context so plugin code cannot linger in the host.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        foreach (var context in _pluginContexts)
        {
            try
            {
                context.Unload();
            }
            catch (Exception)
            {
                // Best-effort unload; a plugin may still be referenced by a caller.
            }
        }
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
    public ExportRuleAttribute(string ruleId)
        : base(typeof(IContractRule))
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
[ExportRule(
    "CUSTOM001",
    Name = "Custom Naming Convention",
    Description = "Enforces custom naming convention for specific schemas",
    Category = "Naming",
    DefaultSeverity = "Warning",
    MinDataGuardVersion = "1.0.0",
    Author = "DataGuard Team",
    Tags = new[] { "naming", "custom" })]
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
