# Public API

> Source: `src/DataGuard.Core/PublicApi/PublicApiSurface.cs`

The public API surface provides a stable, versioned entry point for programmatic DataGuard usage. It includes the main `DataGuardApi` facade, the fluent `ValidationPipeline`, result types, drift detection, and a DI-friendly factory.

## API Usage Flow

```mermaid
flowchart TB
    subgraph Entry Points
        DGA[DataGuardApi]
        DGF[DataGuardFactory]
    end

    subgraph Configuration
        CFG[DataGuardConfiguration]
        DGA --> |CreatePipeline| VP[ValidationPipeline]
    end

    subgraph Pipeline Configuration
        VP --> |WithRules| R[IContractRule[]]
        VP --> |WithPlugins| P[Plugin Directory]
        VP --> |WithTelemetry| T[TelemetryConfig]
        VP --> |WithBaseline| B[Baseline Path]
    end

    subgraph Execution
        VP --> |ValidateAsync| VR[ValidationResult]
        VP --> |CreateBaselineAsync| BF[BaselineFile]
        VP --> |LoadBaselineAsync| BF
        VP --> |CheckDriftAsync| DR[DriftReport]
    end

    subgraph DI Factory
        DGF --> |CreateCredentialManager| CM[CredentialManager]
        DGF --> |CreateAuditLogger| AL[IAuditLogger]
        DGF --> |CreateTelemetryCollector| TC[TelemetryCollector]
        DGF --> |CreateRuleGraph| RDG[RuleDependencyGraph]
    end
```

## DataGuardApi

Static facade — the primary entry point for all programmatic usage.

```csharp
public static class DataGuardApi
{
    public const string Version = "1.0.0";

    public static ValidationPipeline CreatePipeline(DataGuardConfiguration config) { ... }
    public static ValidationPipeline CreatePipeline() { ... }
}
```

### Version

Follows semantic versioning (MAJOR.MINOR.PATCH):
- **MAJOR** — breaking API changes
- **MINOR** — new features, backward compatible
- **PATCH** — bug fixes

## ValidationPipeline

Fluent API for configuring and running validations. Implements `IDisposable`.

```csharp
public sealed class ValidationPipeline : IDisposable
{
    internal ValidationPipeline(DataGuardConfiguration config) { ... }

    public ValidationPipeline WithRules(params IContractRule[] rules) { ... }
    public ValidationPipeline WithPlugins(string pluginDirectory) { ... }
    public ValidationPipeline WithTelemetry(TelemetryConfig? config = null) { ... }
    public ValidationPipeline WithBaselineFile(string baselinePath = ".dataguard-baseline.json") { ... }

    public async Task<ValidationResult> ValidateAsync(
        IReadOnlyList<ContractDescriptor> contracts,
        CancellationToken cancellationToken = default) { ... }

    public async Task<BaselineFile> CreateBaselineAsync(
        IReadOnlyList<ContractViolation> violations,
        string schemaVersion = "1.0",
        CancellationToken cancellationToken = default) { ... }

    public async Task<BaselineFile?> LoadBaselineAsync(
        CancellationToken cancellationToken = default) { ... }

    public async Task<DriftReport> CheckDriftAsync(
        IReadOnlyList<ContractViolation> currentViolations,
        CancellationToken cancellationToken = default) { ... }
}
```

### Fluent Configuration

```csharp
var pipeline = DataGuardApi.CreatePipeline(config)
    .WithRules(new MyCustomRule(), new AnotherRule())
    .WithPlugins("/path/to/plugins")
    .WithTelemetry(new TelemetryConfig(Enabled: true))
    .WithBaselineFile(".dataguard-baseline.json");
```

### ValidateAsync

Runs all configured rules against the provided contracts.

```csharp
public async Task<ValidationResult> ValidateAsync(
    IReadOnlyList<ContractDescriptor> contracts,
    CancellationToken cancellationToken = default)
{
    // 1. Get execution order from dependency graph
    var rules = _ruleGraph.GetExecutionOrder();

    // 2. Run rules in dependency order
    foreach (var rule in rules)
        foreach (var contract in contracts)
            allViolations.AddRange(await rule.ValidateAsync(contract, contracts, ct));

    // 3. Apply baseline filtering
    if (_config.EnableBaseline)
        allViolations = baselineManager.FilterNewViolations(allViolations, baseline).ToList();

    // 4. Record telemetry
    _telemetry?.RecordValidationSummary(...);

    return new ValidationResult(...);
}
```

### CreateBaselineAsync

Creates a baseline from current violations for legacy codebase support.

### LoadBaselineAsync

Loads an existing baseline file for drift comparison.

### CheckDriftAsync

Compares current violations against baseline to detect schema drift.

## ValidationResult

Result of a validation run.

```csharp
public sealed record ValidationResult(
    int ContractsValidated,
    int TotalViolations,
    int Errors,
    int Warnings,
    int Infos,
    ImmutableArray<ContractViolation> Violations,
    TimeSpan Duration,
    string SchemaVersion)
{
    public bool HasErrors => Errors > 0;
    public bool HasWarnings => Warnings > 0;
    public bool IsClean => TotalViolations == 0;
    public bool HasViolations => TotalViolations > 0;
    public double ViolationsPerContract => ContractsValidated > 0
        ? (double)TotalViolations / ContractsValidated : 0;
}
```

| Property | Type | Description |
|----------|------|-------------|
| `ContractsValidated` | `int` | Number of contracts validated |
| `TotalViolations` | `int` | Total violations found |
| `Errors` | `int` | Error-severity count |
| `Warnings` | `int` | Warning-severity count |
| `Infos` | `int` | Info-severity count |
| `Violations` | `ImmutableArray<ContractViolation>` | All violations |
| `Duration` | `TimeSpan` | Validation duration |
| `SchemaVersion` | `string` | Schema version |
| `HasErrors` | `bool` | True if any errors |
| `IsClean` | `bool` | True if zero violations |
| `ViolationsPerContract` | `double` | Violation density metric |

## DriftReport

Report from drift detection against baseline.

```csharp
public sealed record DriftReport(
    bool HasBaseline,
    bool DriftDetected,
    ImmutableArray<ContractViolation> NewViolations = default,
    string BaselineVersion = "",
    string BaselineHash = "",
    string CurrentHash = "",
    string Message = "")
{
    public bool HasDrift => DriftDetected;
    public int NewViolationCount => NewViolations.Length;
}
```

| Property | Type | Description |
|----------|------|-------------|
| `HasBaseline` | `bool` | Whether a baseline exists |
| `DriftDetected` | `bool` | Whether drift was detected |
| `NewViolations` | `ImmutableArray<ContractViolation>` | New violations not in baseline |
| `BaselineVersion` | `string` | Baseline schema version |
| `BaselineHash` | `string` | Baseline schema hash |
| `CurrentHash` | `string` | Current schema hash |
| `Message` | `string` | Human-readable message |

## DataGuardFactory

DI-friendly factory for creating DataGuard components.

```csharp
public static class DataGuardFactory
{
    public static CredentialManager CreateCredentialManager(DataGuardConfiguration config) { ... }
    public static IAuditLogger CreateAuditLogger(DataGuardConfiguration config) { ... }
    public static TelemetryCollector? CreateTelemetryCollector(TelemetryConfig config) { ... }
    public static RuleDependencyGraph CreateRuleGraph() { ... }
}
```

### Factory Methods

| Method | Returns | Description |
|--------|---------|-------------|
| `CreateCredentialManager` | `CredentialManager` | Credential lifecycle management |
| `CreateAuditLogger` | `IAuditLogger` | File or null audit logger based on config |
| `CreateTelemetryCollector` | `TelemetryCollector?` | Null if telemetry disabled |
| `CreateRuleGraph` | `RuleDependencyGraph` | Pre-configured with built-in rules |

## ValidationPipelineExtensions

Extension methods for fluent configuration.

```csharp
public static class ValidationPipelineExtensions
{
    public static ValidationPipeline WithBaseline(
        this ValidationPipeline pipeline,
        string baselinePath = ".dataguard-baseline.json") { ... }
}
```

## Complete Usage Example

```csharp
// 1. Configure
var config = new DataGuardConfiguration
{
    ConnectionString = Environment.GetEnvironmentVariable("DATAGUARD_CONNECTION_STRING"),
    GroundTruthMode = GroundTruthMode.Snapshot,
    EnableBaseline = true,
    EnableTelemetry = true,
};

// 2. Create pipeline
using var pipeline = DataGuardApi.CreatePipeline(config)
    .WithRules(new MyCustomRule())
    .WithPlugins("/opt/dataguard/plugins")
    .WithTelemetry(new TelemetryConfig(
        Enabled: true,
        ExportEndpoint: "https://otel.example.com/v1/logs"))
    .WithBaselineFile(".dataguard-baseline.json");

// 3. Extract contracts
var efSource = new EfModelSource(dbContext, config);
var spSource = new SqlServerStoredProcedureParser(connectionString, config);
var contracts = new List<ContractDescriptor>();
contracts.AddRange(await efSource.ExtractContractsAsync());
contracts.AddRange(await spSource.ExtractContractsAsync());

// 4. Validate
var result = await pipeline.ValidateAsync(contracts);

if (result.HasErrors)
{
    Console.WriteLine($"❌ {result.Errors} errors found");
    foreach (var violation in result.Violations.Where(v => v.Severity == DiagnosticSeverity.Error))
        Console.WriteLine($"  [{violation.RuleId}] {violation.Message}");
}
else if (result.IsClean)
{
    Console.WriteLine("✅ All contracts valid");
}

// 5. Check drift
var drift = await pipeline.CheckDriftAsync(result.Violations.ToList());
if (drift.HasDrift)
    Console.WriteLine($"⚠ Drift detected: {drift.NewViolationCount} new violations");

// 6. Create/update baseline
if (result.HasViolations)
    await pipeline.CreateBaselineAsync(result.Violations.ToList(), "1.0");
```

## API Stability Guarantees

| Guarantee | Description |
|-----------|-------------|
| **Semantic versioning** | Breaking changes only in MAJOR versions |
| **Interface contracts** | `IContractSource`, `IContractRule` never change signatures |
| **Record immutability** | All result types are immutable records |
| **Nullable annotations** | Full nullable reference type annotations |
| **CancellationToken** | All async methods accept cancellation |
