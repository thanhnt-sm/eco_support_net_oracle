# Plugin Architecture

> Source: `src/DataGuard.Core/Plugins/RulePluginManager.cs`

DataGuard's plugin system allows external assemblies to extend the validation rules engine. It uses MEF 2 (Managed Extensibility Framework) for discovery and loading, with `AssemblyLoadContext` isolation for safe plugin unloading.

## Plugin Loading Flow

```mermaid
flowchart TB
    subgraph Plugin Discovery
        DIR[Plugin Directory]
        DLL[*.dll files]
        DIR --> DLL
    end

    subgraph Isolation
        ALC[AssemblyLoadContext<br/>isCollectible: true]
        DLL --> ALC
    end

    subgraph MEF 2
        CC[ContainerConfiguration]
        CH[CompositionHost]
        ALC --> CC
        CC --> CH
    end

    subgraph Export Discovery
        EXP[IContractRule exports]
        META[ExportRuleAttribute metadata]
        CH --> EXP
        EXP --> META
    end

    subgraph Rule Registration
        RPM[RulePluginManager]
        ALL[GetAllRules]
        GID[GetRuleById]
        RPM --> ALL
        RPM --> GID
    end

    META --> RPM
```

## RulePluginManager

Central manager for plugin discovery, loading, and rule resolution.

```csharp
public sealed class RulePluginManager : IDisposable
{
    private readonly CompositionHost _container;
    private readonly ImmutableArray<Lazy<IContractRule, IRuleMetadata>> _rulePlugins;
    private readonly List<AssemblyLoadContext> _pluginContexts = new();

    public RulePluginManager(
        string? pluginDirectory = null,
        ILogger<RulePluginManager>? logger = null) { ... }

    public ImmutableArray<IContractRule> GetAllRules(
        ImmutableArray<IContractRule> builtInRules) { ... }

    public IContractRule? GetRuleById(
        string ruleId,
        ImmutableArray<IContractRule> builtInRules) { ... }

    public ImmutableArray<IRuleMetadata> GetRuleMetadata() { ... }
}
```

### Plugin Directory

Only an explicitly provided plugin directory is scanned. The default location (`%APPDATA%/DataGuard/Plugins`) is user-writable and must never auto-load code.

```csharp
var dir = pluginDirectory;
if (dir != null && Directory.Exists(dir))
{
    foreach (var assemblyFile in Directory.GetFiles(dir, "*.dll"))
    {
        var alc = new AssemblyLoadContext(
            $"DataGuard.Plugin:{Path.GetFileName(assemblyFile)}",
            isCollectible: true);
        var assembly = alc.LoadFromAssemblyPath(assemblyFile);
        config = config.WithAssembly(assembly);
    }
}
```

### AssemblyLoadContext Isolation

Each plugin assembly is loaded into a separate collectible `AssemblyLoadContext`:

| Property | Value | Purpose |
|----------|-------|---------|
| `Name` | `DataGuard.Plugin:{filename}` | Identifies the context |
| `isCollectible` | `true` | Allows unloading |

**Isolation benefits:**
- Plugin types don't interfere with host type resolution
- Plugins can be unloaded (memory reclaimed)
- Plugin failures don't crash the host

## ExportRuleAttribute

Metadata attribute for rule plugins. Combines MEF's `ExportAttribute` with rule-specific metadata.

```csharp
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
```

### Example Plugin

```csharp
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
    public DiagnosticSeverity Severity => DiagnosticSeverity.Warning;
    public string Description => "Enforces custom naming convention for specific schemas";

    public async Task<IReadOnlyList<ContractViolation>> ValidateAsync(
        ContractDescriptor contract,
        IReadOnlyList<ContractDescriptor> allContracts,
        CancellationToken cancellationToken = default)
    {
        var violations = new List<ContractViolation>();

        if (contract is StoredProcedureDescriptor sp &&
            sp.Schema.StartsWith("LEGACY_", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var param in sp.Parameters)
            {
                if (!param.Name.StartsWith("P_", StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add(new ContractViolation(
                        RuleId: "CUSTOM001",
                        Message: $"Parameter '{param.Name}' should start with 'P_'",
                        Severity: DiagnosticSeverity.Warning));
                }
            }
        }

        return await Task.FromResult(violations);
    }
}
```

## IExternalToolPlugin

Interface for integrating external analysis tools (SonarQube, custom linters).

```csharp
public interface IExternalToolPlugin
{
    string ToolName { get; }
    string Version { get; }

    Task<PluginAnalysisResult> AnalyzeAsync(
        IReadOnlyList<ContractDescriptor> contracts,
        CancellationToken cancellationToken = default);
}
```

### PluginAnalysisResult

```csharp
public sealed record PluginAnalysisResult(
    string ToolName,
    IReadOnlyList<ContractViolation> Violations,
    IReadOnlyList<PluginMetric> Metrics,
    TimeSpan Duration);
```

### PluginMetric

```csharp
public sealed record PluginMetric(
    string Name,
    double Value,
    string Unit,
    string Description);
```

## Version Compatibility

Plugins declare minimum DataGuard version via `MinDataGuardVersion`:

```csharp
private bool IsCompatible(IRuleMetadata metadata)
{
    var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
    if (!Version.TryParse(metadata.MinDataGuardVersion ?? "", out var minVersion))
        minVersion = new Version(1, 0, 0);
    return currentVersion >= minVersion;
}
```

Incompatible plugins are silently excluded from `GetAllRules()`.

## Plugin Lifecycle

```mermaid
stateDiagram-v2
    [*] --> Discovered: Scan plugin directory
    Discovered --> Loaded: Load into AssemblyLoadContext
    Loaded --> Registered: MEF composition
    Registered --> Active: GetAllRules() includes plugin
    Active --> Unloaded: Dispose() called
    Unloaded --> [*]: AssemblyLoadContext.Unload()
```

## Disposal

```csharp
public void Dispose()
{
    _container?.Dispose();
    GC.Collect();
    GC.WaitForPendingFinalizers();
    foreach (var context in _pluginContexts)
    {
        try { context.Unload(); }
        catch (Exception) { /* Best-effort */ }
    }
}
```

Disposal sequence:
1. Dispose MEF container (releases exports)
2. Force GC to release references
3. Unload each `AssemblyLoadContext`

## Creating a Plugin

### 1. Create a Class Library

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="DataGuard.Core" Version="1.0.0" />
  </ItemGroup>
</Project>
```

### 2. Implement IContractRule

```csharp
using DataGuard.Core.Abstractions;
using DataGuard.Core.Plugins;

[ExportRule("CUSTOM001",
    Name = "My Custom Rule",
    Description = "Validates custom business logic",
    Category = "Business",
    DefaultSeverity = "Warning")]
public class MyCustomRule : IContractRule
{
    // Implementation
}
```

### 3. Deploy

Copy the compiled DLL to the plugin directory:
```bash
cp bin/Release/net9.0/MyPlugin.dll ~/.local/share/DataGuard/Plugins/
```

### 4. Verify

```bash
dataguard validate --list-rules
# Should show CUSTOM001: My Custom Rule
```
