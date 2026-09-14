# Public API

> Nguồn: `src/DataGuard.Core/PublicApi/PublicApiSurface.cs`

Public API surface cung cấp điểm vào ổn định, có phiên bản cho việc sử dụng DataGuard theo chương trình. Nó bao gồm facade `DataGuardApi` chính, `ValidationPipeline` fluent, các kiểu kết quả, phát hiện drift, và factory thân thiện DI.

## Luồng Sử Dụng API

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

Facade tĩnh — điểm vào chính cho mọi sử dụng theo chương trình.

```csharp
public static class DataGuardApi
{
    public const string Version = "1.0.0";

    public static ValidationPipeline CreatePipeline(DataGuardConfiguration config) { ... }
    public static ValidationPipeline CreatePipeline() { ... }
}
```

### Version

Tuân theo semantic versioning (MAJOR.MINOR.PATCH):
- **MAJOR** — thay đổi API phá vỡ
- **MINOR** — tính năng mới, tương thích ngược
- **PATCH** — sửa lỗi

### Cấu hình mặc định

`CreatePipeline()` khởi đầu từ `DataGuardConfigurationExtensions.Default()` và áp dụng smart defaults. `CreatePipeline(config)` giữ nguyên record của caller và chỉ áp dụng smart defaults khi `config.EnableSmartDefaults` là true. Đặt cờ này thành `false` là opt-out: các giá trị do caller sở hữu được truyền qua mà không tự động đổi provider/schema.

## ValidationPipeline

API fluent để cấu hình và chạy validations. Implement `IDisposable`.

```csharp
public sealed class ValidationPipeline : IDisposable
{
    internal ValidationPipeline(DataGuardConfiguration config) { ... }

    public ValidationPipeline WithRules(params IContractRule[] rules) { ... }
    public ValidationPipeline WithPlugins(string pluginDirectory) { ... }
    public ValidationPipeline WithPlugins(
        string pluginDirectory,
        PluginTrustPolicy? trustPolicy,
        IPluginProvenanceVerifier? provenanceVerifier) { ... }

`WithPlugins(path)` giữ policy strict mặc định và chỉ nhận plugin khi có provenance verifier. Overload ba tham số cho phép host truyền policy và verifier do operator sở hữu; plugin không tự cấp trust root.

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

### Cấu Hình Fluent

```csharp
var pipeline = DataGuardApi.CreatePipeline(config)
    .WithRules(new MyCustomRule(), new AnotherRule())
    .WithPlugins("/path/to/plugins")
    .WithTelemetry(new TelemetryConfig(Enabled: true))
    .WithBaselineFile(".dataguard-baseline.json");
```

### ValidateAsync

Chạy tất cả rules đã cấu hình với các contracts được cung cấp.

```csharp
public async Task<ValidationResult> ValidateAsync(
    IReadOnlyList<ContractDescriptor> contracts,
    CancellationToken cancellationToken = default)
{
    // 1. Lấy thứ tự thực thi từ đồ thị phụ thuộc
    var rules = _ruleGraph.GetExecutionOrder();

    // 2. Chạy rules theo thứ tự phụ thuộc
    foreach (var rule in rules)
        foreach (var contract in contracts)
            allViolations.AddRange(await rule.ValidateAsync(contract, contracts, ct));

    // 3. Áp dụng lọc baseline
    if (_config.EnableBaseline)
        allViolations = baselineManager.FilterNewViolations(allViolations, baseline).ToList();

    // 4. Ghi nhận telemetry
    _telemetry?.RecordValidationSummary(...);

    return new ValidationResult(...);
}
```

## ValidationResult

Kết quả chi tiết của cả đường concurrent lẫn sequential có thêm `RuleOutcomes`.
Rule là `Evaluated` vì nó đã thực sự chạy, kể cả khi không tạo violation.
`Unavailable` và `Failed` làm requested validation incomplete. `Skipped` ghi nhận
rule không chạy; chẳng hạn sequential validation ghi lý do violation cap sau khi
các finding giữ lại đã đầy dung lượng. Khi đó `ExecutionStatus` bao quanh là
incomplete. Cách tạo positional và các thành viên kết quả legacy vẫn không đổi.

Kết quả của một lần chạy validation.

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

`ExecutionStatus` và `DroppedViolationCount` là các thuộc tính kết quả được thêm theo hướng tương thích. Kết quả chỉ clean khi không có violation hiển thị và execution complete; lọc baseline không thể che một run incomplete.

| Thuộc tính | Kiểu | Mô tả |
|------------|------|-------|
| `ContractsValidated` | `int` | Số contracts đã kiểm tra |
| `TotalViolations` | `int` | Tổng violations tìm thấy |
| `Errors` | `int` | Số lượng mức Error |
| `Warnings` | `int` | Số lượng mức Warning |
| `Infos` | `int` | Số lượng mức Info |
| `Violations` | `ImmutableArray<ContractViolation>` | Tất cả violations |
| `Duration` | `TimeSpan` | Thời gian validation |
| `HasErrors` | `bool` | True nếu có errors |
| `IsClean` | `bool` | True nếu không có violations |
| `ViolationsPerContract` | `double` | Chỉ số mật độ violations |

## DriftReport

Báo cáo từ phát hiện violation drift so với baseline. Overload nhận
`DatabaseSchemaDescriptor` dùng cho structural schema drift và trả về
`DriftEvaluationStatus` tường minh; input thiếu, hỏng, không hỗ trợ hoặc chưa
đánh giá không thể bị hiểu thành kết quả clean.

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

`Status` có thể là `Complete`, `Missing`, `Corrupt`, `UnsupportedVersion`,
`Unevaluated` hoặc `Failed`.
Mặc định là `Missing`; các đường chạy đã đánh giá sẽ gán tường minh `Complete`.

| Thuộc tính | Kiểu | Mô tả |
|------------|------|-------|
| `HasBaseline` | `bool` | Baseline có tồn tại không |
| `DriftDetected` | `bool` | Drift có được phát hiện không |
| `NewViolations` | `ImmutableArray<ContractViolation>` | Violations mới không có trong baseline |
| `BaselineVersion` | `string` | Phiên bản schema baseline |
| `BaselineHash` | `string` | Hash schema baseline |
| `CurrentHash` | `string` | Hash schema hiện tại |
| `Message` | `string` | Thông điệp dễ đọc |

## DataGuardFactory

Factory thân thiện DI để tạo các thành phần DataGuard.

```csharp
public static class DataGuardFactory
{
    public static CredentialManager CreateCredentialManager(DataGuardConfiguration config) { ... }
    public static IAuditLogger CreateAuditLogger(DataGuardConfiguration config) { ... }
    public static TelemetryCollector? CreateTelemetryCollector(TelemetryConfig config) { ... }
    public static RuleDependencyGraph CreateRuleGraph() { ... }
}
```

### Các Phương Thức Factory

| Phương thức | Trả về | Mô tả |
|-------------|--------|-------|
| `CreateCredentialManager` | `CredentialManager` | Quản lý vòng đời credential |
| `CreateAuditLogger` | `IAuditLogger` | File hoặc null audit logger dựa trên config |
| `CreateTelemetryCollector` | `TelemetryCollector?` | Null nếu telemetry tắt |
| `CreateRuleGraph` | `RuleDependencyGraph` | Cấu hình sẵn với built-in rules |

## Ví Dụ Sử Dụng Đầy Đủ

```csharp
// 1. Cấu hình
var config = new DataGuardConfiguration
{
    ConnectionString = Environment.GetEnvironmentVariable("DATAGUARD_CONNECTION_STRING"),
    GroundTruthMode = GroundTruthMode.Snapshot,
    EnableBaseline = true,
    EnableTelemetry = true,
};

// 2. Tạo pipeline
using var pipeline = DataGuardApi.CreatePipeline(config)
    .WithRules(new MyCustomRule())
    .WithPlugins("/opt/dataguard/plugins")
    .WithTelemetry(new TelemetryConfig(
        Enabled: true)
    {
        FileSinkDirectory = "/var/lib/dataguard/observability/archive",
        RemoteExportEnabled = false,
    })
    .WithBaselineFile(".dataguard-baseline.json");

// 3. Trích xuất contracts
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

// 5. Kiểm tra drift
var drift = await pipeline.CheckDriftAsync(result.Violations.ToList());
if (drift.HasDrift)
    Console.WriteLine($"⚠ Drift detected: {drift.NewViolationCount} new violations");

// 6. Tạo/cập nhật baseline
if (result.HasViolations)
    await pipeline.CreateBaselineAsync(result.Violations.ToList(), "1.0");
```

## Đảm Bảo Ổn Định API

| Đảm bảo | Mô tả |
|----------|-------|
| **Semantic versioning** | Thay đổi phá vỡ chỉ trong phiên bản MAJOR |
| **Interface contracts** | `IContractSource`, `IContractRule` không bao giờ thay đổi chữ ký |
| **Record bất biến** | Tất cả kiểu kết quả là records bất biến |
| **Nullable annotations** | Đầy đủ nullable reference type annotations |
| **CancellationToken** | Tất cả async methods chấp nhận hủy |
