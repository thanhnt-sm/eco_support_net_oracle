# Tương Tác Thành Phần / Component Interaction / Tương Tác Thành Phần

## Tổng Quan Tương Tác / Interaction Overview / Tổng Quan Tương Tác

```mermaid
graph TB
    subgraph "External / Bên Ngoài"
        Dev[Developer / Lập Trình Viên]
        CI[CI/CD Pipeline]
        DB[(Database)]
        Vault[Secret Vault]
        IDE[IDE / Editor]
    end

    subgraph "CLI Layer / Lớp CLI"
        CLI[DataGuard.Cli]
        Hook[PreCommitHookInstaller]
    end

    subgraph "Core Engine / Động Cơ Cốt Lõi"
        Pipeline[ValidationPipeline]
        Graph[RuleDependencyGraph]
        Engine[ConcurrentValidationEngine]
        Emitter[DiagnosticEmitter]
        Baseline[BaselineManager v2]
        Telemetry[TelemetryCollector]
    end

    subgraph "Sources / Nguồn"
        EfSrc[EfModelSource]
        SPSrc[SqlServerStoredProcedureParser]
        RawSrc[RawSqlParser]
        OraSrc[OracleAdapter]
    end

    subgraph "Rules / Quy Tắc"
        R1[ParameterCountRule]
        R2[ParameterTypeMatchRule]
        R3[ParameterDirectionRule]
        R4[ColumnShapeMatchRule]
        R5[NullableMismatchRule]
        R6[NamingConventionRule]
        R7[LengthExceedsColumnRule]
        R8[ByteLengthOverflowRiskRule]
        R9[InferredSizeFallbackRule]
        R10[OracleDialectRules]
    end

    subgraph "Adapters / Bộ Điều Hợp"
        SqlAdap[SqlServer.Adapter]
        OraAdap[Oracle.Adapter]
    end

    subgraph "Security / Bảo Mật"
        CredMgr[CredentialManager]
        ZTCred[ZeroTrustCredentialProvider]
        Audit[FileAuditLogger]
        SCV[SupplyChainVerifier]
    end

    subgraph "Infrastructure / Hạ Tầng"
        Baseline[BaselineManager v2]
        Telemetry[TelemetryCollector]
        Health[HealthChecks]
        PluginMgr[RulePluginManager]
        AutoDetect[AutoDetectionEngine]
    end

    subgraph "Analyzers / Bộ Phân Tích"
        Gen[UnvalidatedSqlCallGenerator]
        Diag[ContractValidationAnalyzer]
        Fixes[CodeFixProviders x12]
    end

    subgraph "Output / Đầu Ra"
        SARIF[SarifSink]
        Console[ConsoleSink]
        MD[MarkdownSink]
    end

    Dev --> CLI
    CI --> CLI
    IDE --> Gen
    Gen --> Fixes
    Fixes --> IDE
    CLI --> Pipeline
    CI --> Pipeline
    Pipeline --> Graph
    Pipeline --> Engine
    Pipeline --> EfSrc
    Pipeline --> SPSrc
    Pipeline --> RawSrc
    Pipeline --> OraSrc
    EfSrc --> Contracts
    SPSrc --> Contracts
    RawSrc --> Contracts
    OraSrc --> Contracts
    Graph --> Engine
    Engine --> Rules
    Rules --> Violations
    Violations --> Baseline
    Baseline --> Emitter
    Emitter --> SARIF
    Emitter --> Console
    Emitter --> MD
    OraSrc --> OraAdap
    SPSrc --> SqlAdap
    Pipeline --> CredMgr
    Pipeline --> ZTCred
    Pipeline --> Audit
    Pipeline --> SCV
    Pipeline --> Telemetry
    Pipeline --> Health
    Pipeline --> PluginMgr
    Pipeline --> AutoDetect
    CredMgr --> Vault
    ZTCred --> Vault
    SPSrc --> DB
    OraSrc --> DB
    OraSrc --> Audit
    SPSrc --> Audit
    Health --> DB
    Hook --> CLI
    Hook --> Dev
```

---

## Tương Tác Chi Tiết / Detailed Interactions / Tương Tác Chi Tiết

### 1. CLI → ValidationPipeline

```mermaid
sequenceDiagram
    participant User as Lập Trình Viên
    participant CLI as DataGuard.Cli
    participant Config as LoadConfig
    participant Pipeline as ValidationPipeline
    participant Engine as ConcurrentValidationEngine
    participant Baseline as BaselineManager
    participant Emitter as DiagnosticEmitter

    User->>CLI: dataguard validate --connection "..." --format sarif
    CLI->>Config: Load .dataguard.yml
    Config-->>CLI: DataGuardConfiguration
    CLI->>Pipeline: new ValidationPipeline(config)
    Pipeline->>Engine: new ConcurrentValidationEngine(config, rules)
    Pipeline->>Baseline: new BaselineManager(baselinePath)
    Pipeline->>Emitter: new DiagnosticEmitter()
    Pipeline->>Emitter: AddSarifSink(FileSarifSink)
    Pipeline->>Emitter: AddDiagnosticSink(ConsoleSink)
    
    par Extraction
        Pipeline->>EfSrc: ExtractContractsAsync()
        Pipeline->>SPSrc: ExtractContractsAsync()
        Pipeline->>RawSrc: ExtractContractsAsync()
        Pipeline->>OraSrc: ExtractContractsAsync()
    end
    
    EfSrc-->>Pipeline: EntityDescriptor[]
    SPSrc-->>Pipeline: StoredProcedureDescriptor[]
    RawSrc-->>Pipeline: RawSqlDescriptor[]
    OraSrc-->>Pipeline: OracleDescriptor[]
    
    Pipeline->>Engine: ValidateAllAsync(contracts)
    Engine->>Engine: Partitioner + SemaphoreSlim
    Engine->>Rules: Parallel ValidateAsync
    Rules-->>Engine: ContractViolation[]
    Engine-->>Pipeline: All Violations
    
    Pipeline->>Baseline: FilterNewViolations(violations)
    Baseline-->>Pipeline: New Violations Only
    
    Pipeline->>Emitter: EmitAsync(filteredViolations)
    Emitter->>SarifSink: WriteAsync(SarifLog)
    Emitter->>ConsoleSink: WriteAsync(violations)
    Emitter-->>CLI: void
    CLI->>User: ExitCode 0=Pass, 1=Fail
```

### 2. Analyzers Tương Tác IDE / IDE Analyzer Interaction

```mermaid
sequenceDiagram
    participant IDE as IDE/Editor
    participant Gen as UnvalidatedSqlCallGenerator
    participant Syntax as SyntaxProvider
    participant Semantic as SemanticModel
    participant Fixes as CodeFixProviders
    participant User as Developer

    IDE->>Gen: Initialize(IncrementalGeneratorInitializationContext)
    Gen->>Syntax: CreateSyntaxProvider(predicate, transform)
    Syntax-->>Gen: SqlCallSite[]
    Gen->>Gen: RegisterSourceOutput(EmitDiagnostics)
    
    loop Mỗi Keystroke/Change
        IDE->>Syntax: IsPotentialSqlCall(node)
        Syntax-->>Gen: true/false
        Gen->>Syntax: ExtractSqlCallSite(ctx)
        Syntax-->>Gen: SqlCallSite?
        Gen->>IDE: ReportDiagnostic(UnvalidatedSqlCall)
    end
    
    IDE->>User: Show Squiggle + Lightbulb
    User->>IDE: Click Lightbulb
    IDE->>Fixes: RegisterCodeFixesAsync(context)
    Fixes->>IDE: Register Code Actions
    User->>IDE: Chọn "Add [SkipContractCheck] attribute"
    IDE->>Fixes: AddSkipContractCheckAttributeAsync(doc, root, ct)
    Fixes->>Fixes: DocumentEditor.CreateAsync
    Fixes->>Fixes: generator.AddAttribute
    Fixes-->>IDE: Updated Document
    IDE->>Gen: Re-analyze (Incremental)
    Gen->>IDE: Diagnostic Cleared
```

### 3. Oracle Adapter Tương Tác Database

```mermaid
sequenceDiagram
    participant Pipeline
    participant OraSrc as OracleAdapter
    participant Reader as AllArgumentsReader
    participant NLS as NlsSessionReader
    participant DB as Oracle DB
    participant Audit as FileAuditLogger

    Pipeline->>OraSrc: new OracleAdapter(config)
    OraSrc->>Reader: new AllArgumentsReader(connStr, config)
    OraSrc->>NLS: new NlsSessionReader(connStr)
    
    Pipeline->>Reader: GetParametersAsync(owner, pkg, proc)
    Reader->>DB: SELECT FROM all_arguments
    Note right of Reader: Audit Log: GetParameters<br/>owner, package, procedure
    Reader->>Audit: LogDatabaseOperation("GetParameters", "Oracle", hash, details, success)
    DB-->>Reader: Parameters + Overload Info
    Reader-->>Pipeline: ParameterDescriptor[]
    
    Pipeline->>NLS: GetLengthSemanticsAsync()
    NLS->>DB: SELECT FROM nls_session_parameters
    DB-->>NLS: CHAR/BYTE
    NLS-->>Pipeline: LengthSemantics
    
    Pipeline->>Audit: LogDatabaseOperation("GetLengthSemantics", ...)
```

### 4. Baseline v2 Tương Tác File System

```mermaid
sequenceDiagram
    participant Pipeline
    participant BM as BaselineManager v2
    participant Cache as MemoryCache + FileCache
    participant File as .dataguard-baseline.json
    participant Hash as SHA256

    Pipeline->>BM: CreateBaselineAsync(violations, "1.0", "Snapshot")
    BM->>Hash: ComputeSchemaHash(violations)
    Hash-->>BM: schemaHash (64-bit)
    BM->>Cache: Check Memory Cache
    alt Cache Hit
        Cache-->>BM: Cached Hash
    else Cache Miss
        BM->>Cache: Store in Memory + File Cache
    end
    BM->>BM: GetDatabaseVersion()
    BM->>File: SaveAsync (Memory-Mapped >1MB)
    File-->>BM: Success
    BM-->>Pipeline: BaselineFile v2

    Note over Pipeline,File: Load Baseline
    Pipeline->>BM: LoadAsync()
    BM->>File: Check File Exists
    alt File > 1MB
        BM->>File: Memory-Mapped Read
    else File <= 1MB
        BM->>File: File.ReadAllTextAsync
    end
    File-->>BM: JSON Content
    BM->>BM: Deserialize BaselineFile v2
    alt Legacy v1
        BM->>BM: Migrate to v2
    end
    BM-->>Pipeline: BaselineFile?
```

### 5. Baseline Filter Tương Tác

```mermaid
graph TD
    A[Current Violations] --> B[BaselineManager.FilterNewViolations]
    B --> C{Baseline Exists?}
    C -->|No| D[Return All Violations]
    C -->|Yes| E[Build Baseline Keys Set]
    E --> F[For Each Current Violation]
    F --> G{Key In Baseline Set?}
    G -->|Yes| H[Skip - In Baseline]
    G -->|No| I[Include - New Violation]
    H --> F
    I --> F
    F --> J[Return New Violations Only]
```

**Key Generation / Tạo Key**:
```csharp
// BaselineViolation Key
$"{RuleId}|{Message}|{Severity}|{FilePath}|{StartLine}"

// ContractViolation Key  
$"{RuleId}|{Message}|{Severity}|{SourceTree.FilePath}|{StartLine}"
```

---

## Ma Trận Tương Tác / Interaction Matrix / Ma Trận Tương Tác

| Component A | Component B | Interaction Type / Loại Tương Tác | Direction / Hướng | Protocol / Giao Thức |
|------|------|------|--------|--------|
| CLI | ValidationPipeline | Method Call / Gọi Phương Thức | CLI → Pipeline | In-process / Trong tiến trình |
| Pipeline | EfModelSource | Async Method | Pipeline → Source | `Task<IReadOnlyList<ContractDescriptor>>` |
| Pipeline | SqlServerAdapter | Async Method | Pipeline → Adapter | `Task<IReadOnlyList<ContractDescriptor>>` |
| Pipeline | OracleAdapter | Async Method | Pipeline → Adapter | `Task<IReadOnlyList<ContractDescriptor>>` |
| Pipeline | RuleDependencyGraph | Method Call | Pipeline → Graph | `ImmutableArray<IContractRule>` |
| Pipeline | ConcurrentValidationEngine | Async Method | Pipeline → Engine | `Task<IReadOnlyList<ContractViolation>>` |
| Engine | Rules | Async Method | Engine → Rule | `Task<IReadOnlyList<ContractViolation>>` |
| Rules | Violations | Return Value | Rule → Engine | `IReadOnlyList<ContractViolation>` |
| Engine | Pipeline | Return Value | Engine → Pipeline | `IReadOnlyList<ContractViolation>` |
| Pipeline | BaselineManager | Async Method | Pipeline → Baseline | `Task<BaselineFile>` / `FilterNewViolations` |
| Pipeline | DiagnosticEmitter | Async Method | Pipeline → Emitter | `Task EmitAsync` |
| Emitter | SarifSink | Async Method | Emitter → Sink | `Task WriteAsync(SarifLog)` |
| Emitter | ConsoleSink | Async Method | Emitter → Sink | `Task WriteAsync(violations)` |
| Pipeline | CredentialManager | Async Method | Pipeline → CredMgr | `Task<string> GetConnectionStringAsync` |
| CredMgr | Vault | External Call | CredMgr → Vault | KeyVault/AWS/Vault API |
| Pipeline | ZeroTrustCredentialProvider | Async Method | Pipeline → ZTProvider | `Task<CredentialHandle>` |
| ZTProvider | AuditLogger | Async Method | ZTProvider → Audit | `Task LogCredentialAccessAsync` |
| Pipeline | SupplyChainVerifier | Async Method | Pipeline → SCV | `Task<SupplyChainVerificationResult>` |
| SCV | Assembly | Reflection | SCV → Assembly | `Assembly.GetReferencedAssemblies()` |
| Pipeline | TelemetryCollector | Method Call | Pipeline → Telemetry | `IncrementCounter`, `RecordHistogram` |
| Telemetry | Meter | .NET Metrics | Telemetry → Meter | `IMeter` API |
| Pipeline | HealthChecks | Method Call | Pipeline → Health | `Task<HealthCheckResult>` |
| Health | DB | DB Query | Health → DB | `SELECT 1` / Version Query |
| Pipeline | AutoDetectionEngine | Async Method | Pipeline → AutoDetect | `Task<DataGuardConfiguration>` |
| AutoDetect | File System | File IO | AutoDetect → FS | `Directory.GetFiles`, `File.ReadAllText` |
| AutoDetect | Roslyn | Syntax Analysis | AutoDetect → Roslyn | `CSharpSyntaxTree.ParseText` |
| Pipeline | RulePluginManager | Constructor | Pipeline → PluginMgr | MEF Container |
| PluginMgr | Plugin Assemblies | MEF | PluginMgr → Plugins | `CompositionHost` |
| Pipeline | AutoDetectionEngine | Async Method | Pipeline → AutoDetect | `Task<DataGuardConfiguration>` |
| Analyzers | IDE | Roslyn API | Analyzer → IDE | `ReportDiagnostic`, `RegisterCodeFixesAsync` |
| CodeFixProviders | IDE | Roslyn API | FixProvider → IDE | `DocumentEditor`, `CodeAction` |
| CLI | PreCommitHookInstaller | Method Call | CLI → HookInstaller | `Task<InstallResult>` |
| HookInstaller | File System | File IO | HookInstaller → FS | `File.WriteAllText`, `chmod +x` |
| HookInstaller | Git | Git Config | HookInstaller → Git | `.git/hooks/pre-commit`, `.husky/pre-commit`, `lefthook.yml` |
| Pipeline | SCV | Startup | Pipeline → SCV | `VerifyAsync` on startup |
| Pipeline | AuditLogger | Throughout | Pipeline → Audit | `LogDatabaseOperation`, `LogCredentialAccess` |

---

## Tương Tác Song Song / Parallel Interactions / Tương Tác Song Song

### ConcurrentValidationEngine

```mermaid
graph TD
    A[ContractDescriptors[]] --> B[Partitioner.Create<br/>Range Partitioning]
    B --> C[Partition 1] --> D[SemaphoreSlim<br/>WaitAsync]
    B --> E[Partition 2] --> D
    B --> F[Partition N] --> D
    D --> G[Task.WhenAll<br/>Parallel.ForEach]
    G --> H[ConcurrentQueue<ContractViolation>]
    H --> I[All Violations]
    
    style B fill:#e8f5e9
    style G fill:#e8f5e9
```

**Cấu Hình Song Song / Parallelism Config**:
```csharp
var maxParallelism = config.MaxDegreeOfParallelism > 0 
    ? config.MaxDegreeOfParallelism 
    : Environment.ProcessorCount;

var chunkSize = Math.Max(1, contracts.Count / (maxParallelism * 4));

var semaphore = new SemaphoreSlim(maxParallelism);
var partitions = Partitioner.Create(0, contracts.Count, chunkSize);
```

### RuleDependencyGraph - Thứ Tự Thực Thi

```mermaid
graph TD
    L1[ParameterCountRule] --> L3[ParameterDirectionRule]
    L1 --> L4[ColumnShapeMatchRule]
    L1 --> L6[NamingConventionRule]
    L2[ParameterTypeMatchRule] --> L5[NullableMismatchRule]
    L2 --> L7[LengthExceedsColumnRule]
    L2 --> L9[InferredSizeFallbackRule]
    L7 --> L8[ByteLengthOverflowRiskRule]
    L4 --> L6
    L7 --> L8

    classDef l1 fill:#e3f2fd;
    classDef l2 fill:#e8f5e9;
    classDef l3 fill:#fff3e0;
    classDef l4 fill:#fce4ec;
    classDef l5 fill:#f3e5f5;

    class L1,L2 l1;
    class L3,L4 l2;
    class L5,L6 l3;
    class L7,L9 l4;
    class L8 l5;
```

**Nhóm Song Song / Parallel Groups**:
```
Level 1 (Parallel): [ParameterCountRule, ParameterTypeMatchRule]
Level 2 (Parallel): [ParameterDirectionRule, ColumnShapeMatchRule] 
Level 3 (Parallel): [NullableMismatchRule, NamingConventionRule]
Level 4 (Parallel): [LengthExceedsColumnRule, InferredSizeFallbackRule]
Level 5 (Sequential): [ByteLengthOverflowRiskRule]
Level 6 (Parallel - Independent): [Dialect Rules: DG010-DG014]
```

---

## Tương Tác Bảo Mật / Security Interactions / Tương Tác Bảo Mật

### Zero-Trust Credential Flow

```mermaid
sequenceDiagram
    participant Pipeline
    participant ZT as ZeroTrustCredentialProvider
    participant Env as Environment Variables
    participant KV as Azure Key Vault
    participant AWS as AWS Secrets Manager
    participant Vault as HashiCorp Vault
    participant Local as Local Encrypted Store
    participant Config as Config File
    participant Audit as AuditLogger

    Pipeline->>ZT: GetDatabaseConnectionAsync()
    ZT->>Env: Get DATAGUARD_CONNECTION_STRING
    alt Env Found
        Env-->>ZT: Connection String
        ZT->>Audit: LogCredentialAccess("EnvVar")
        ZT-->>Pipeline: CredentialHandle
    else Env Not Found
        ZT->>KV: Try Key Vault (if KeyVaultUri configured)
        alt KV Found
            KV-->>ZT: Secret Value
            ZT->>Audit: LogCredentialAccess("KeyVault")
            ZT-->>Pipeline: CredentialHandle
        else KV Not Found
            ZT->>AWS: Try AWS Secrets Manager
            alt AWS Found
                AWS-->>ZT: Secret Value
                ZT->>Audit: LogCredentialAccess("AWS")
                ZT-->>Pipeline: CredentialHandle
            else AWS Not Found
                ZT->>Vault: Try HashiCorp Vault
                alt Vault Found
                    Vault-->>ZT: Secret Value
                    ZT->>Audit: LogCredentialAccess("Vault")
                    ZT-->>Pipeline: CredentialHandle
                else Vault Not Found
                    ZT->>Local: Try Local Encrypted Store
                    alt Local Found
                        Local-->>ZT: Encrypted/Plain
                        ZT->>Audit: LogCredentialAccess("LocalStore")
                        ZT-->>Pipeline: CredentialHandle
                    else Local Not Found
                        ZT->>Config: Read from .dataguard.yml
                        alt Config Found
                            Config-->>ZT: Connection String
                            ZT->>Audit: LogCredentialAccess("ConfigFile") + WARNING
                            ZT-->>Pipeline: CredentialHandle
                        else All Failed
                            ZT-->>Pipeline: Exception
                        end
                    end
                end
            end
        end
    end
```

### Credential Rotation Detection

```mermaid
sequenceDiagram
    participant Pipeline
    participant CM as CredentialManager
    participant Store as Credential Store
    participant Audit as AuditLogger

    Pipeline->>CM: GetConnectionStringAsync()
    CM->>Store: LoadFromCredentialStoreAsync()
    Store-->>CM: Stored CredentialData
    CM->>CM: Compare Stored vs Current
    alt Different (Rotated)
        CM->>Audit: LogCredentialRotationDetected(oldHash, newHash)
        CM->>Pipeline: Warning + Connection String
    else Same
        CM->>Pipeline: Connection String
    end
```

---

## Tương Tác Supply Chain / Supply Chain Interactions

```mermaid
sequenceDiagram
    participant Pipeline
    participant SCV as SupplyChainVerifier
    participant Assembly as Current Assembly
    participant Deps as Referenced Assemblies
    participant File as Assembly File

    Pipeline->>SCV: VerifyAsync()
    SCV->>Assembly: Get Assembly Location
    SCV->>Assembly: ComputeAssemblyHashAsync()
    SCV->>Deps: GetReferencedAssemblies()
    loop Mỗi Dependency
        SCV->>SCV: IsTrustedDependency(name)
    end
    SCV->>File: Check File Exists + Size + Modified Time
    SCV->>Assembly: GetPublicKey().Length > 0
    SCV-->>Pipeline: SupplyChainVerificationResult
```

---

## Tương Tác Plugin / Plugin Interactions

```mermaid
sequenceDiagram
    participant Pipeline
    participant PM as RulePluginManager
    participant MEF as MEF Container
    participant Plugins as Plugin Assemblies
    participant BuiltIn as Built-in Rules

    Pipeline->>PM: new RulePluginManager(pluginDir)
    PM->>MEF: ContainerConfiguration + WithAssembliesInDirectory
    MEF->>Plugins: Load Assemblies
    Plugins-->>MEF: Export<IContractRule, IRuleMetadata>
    MEF-->>PM: ImmutableArray<Lazy<IContractRule, IRuleMetadata>>
    PM->>PM: Filter Compatible (Version Check)
    PM->>Pipeline: GetAllRules(builtInRules)
    PM->>Pipeline: builtInRules + pluginRules
```

---

## Tương Tác Telemetry / Telemetry Interactions

```mermaid
graph TD
    A[ValidationPipeline] --> B[TelemetryCollector]
    B --> C[Meter: DataGuard.Core]
    C --> D[Counter: rules.executions]
    C --> E[Histogram: rule.duration]
    C --> F[Counter: validations.total]
    C --> G[Histogram: validation.duration]
    C --> H[Counter: cache.hit/miss]
    C --> I[Counter: baseline.created]
    C --> J[Counter: schema.hash.computed]
    C --> K[Counter: database.connection]
    C --> L[Histogram: database.query.duration]
    
    B --> M[TimedOperation: MeasureOperation]
    M --> N[Stopwatch.StartNew]
    N --> O[Dispose -> RecordHistogram]
    
    B --> P[TelemetryEvent Queue]
    P --> Q[FlushTimer: 30s]
    Q --> R[Export to OpenTelemetry/Prometheus]
```

---

## Tương Tác Health Check / Health Check Interactions

```mermaid
sequenceDiagram
    participant K8s as Kubernetes/Load Balancer
    participant HC as DataGuardHealthCheck
    participant CredMgr as CredentialManager
    participant BM as BaselineManager
    participant SCV as SupplyChainVerifier
    participant Disk as DiskSpace
    participant Mem as GC Memory

    K8s->>HC: GET /health/live
    HC-->>K8s: Healthy {uptime, version}

    K8s->>HC: GET /health/ready
    HC->>CredMgr: GetConnectionStringAsync()
    CredMgr-->>HC: Connection String / Error
    HC->>BM: LoadAsync()
    BM-->>HC: BaselineFile / null
    HC->>SCV: VerifyAsync()
    SCV-->>HC: SupplyChainVerificationResult
    HC->>Disk: DriveInfo.AvailableFreeSpace
    HC->>Mem: GC.GetTotalMemory(false)
    HC-->>K8s: Healthy/Degraded/Unhealthy + Details
```

---

## Tương Tác CI/CD / CI/CD Interactions

```mermaid
graph TD
    subgraph "GitHub Actions"
        GH1[on push/tag] --> GH2[Checkout]
        GH2 --> GH3[Setup .NET 9.0]
        GH3 --> GH4[Restore]
        GH4 --> GH5[Build]
        GH5 --> GH6[Build + Analyzers]
        GH6 --> GH7[Test]
        GH7 --> GH8[Security Scan]
        GH8 --> GH9[Generate SBOM]
        GH9 --> GH10[Pack]
        GH10 --> GH11[Upload Artifacts]
    end

    subgraph "Release"
        R1[Tag Push] --> R2[Sign Packages cosign]
        R2 --> R3[Generate SBOM]
        R3 --> R4[Publish NuGet]
        R4 --> R5[Create GH Release]
        R5 --> R6[Upload Attestations]
        R6 --> R7[Docker Build/Push GHCR]
    end

    GH11 --> R1
```

---

## Tóm Tắt Ma Trận / Interaction Summary Matrix

| Layer / Lớp | Components / Thành Phần | Primary Interactions / Tương Tác Chính |
|------|------|------|
| **CLI** | `DataGuard.Cli`, `PreCommitHookInstaller` | → Pipeline, HookInstaller, Config, Hooks |
| **Core Pipeline** | `ValidationPipeline`, `RuleDependencyGraph`, `ConcurrentValidationEngine`, `DiagnosticEmitter` | ← CLI, CI, Analyzers → Sources, Rules, Baseline, Emitter, Security, Telemetry, Health, Plugins, AutoDetect |
| **Sources** | `EfModelSource`, `SqlServerStoredProcedureParser`, `RawSqlParser`, `OracleAdapter` | → Pipeline (Contracts), → DB (Queries), → AuditLogger |
| **Adapters** | `SqlServer.Adapter`, `Oracle.Adapter` | ← Sources, → DB, → AuditLogger |
| **Rules** | 6 Built-in + Dialect + Plugins | ← Engine (ValidateAsync) → Violations |
| **Engine** | `ConcurrentValidationEngine` | ← Pipeline (ValidateAllAsync) → Partitioner, Semaphore, Rules |
| **Baseline** | `BaselineManager v2` | ← Pipeline (FilterNewViolations, CreateBaseline) → File, Cache, Hash |
| **Emitter** | `DiagnosticEmitter`, `SarifSink`, `ConsoleSink`, `StreamingSarifSink` | ← Pipeline (EmitAsync) → SARIF/Console/Markdown |
| **Security** | `CredentialManager`, `ZeroTrustCredentialProvider`, `FileAuditLogger`, `SupplyChainVerifier` | ← Pipeline, CredMgr, ZTProvider → Vault, Audit, Assembly, File |
| **Infrastructure** | `TelemetryCollector`, `HealthChecks`, `RulePluginManager`, `AutoDetectionEngine` | ← Pipeline → Meter, HealthChecks, MEF, FileSystem, Roslyn |
| **Analyzers** | `UnvalidatedSqlCallGenerator`, `ContractValidationAnalyzer`, `CodeFixProviders` | ↔ IDE (Syntax, Semantic, CodeFixes), ↔ Fixes |
| **CI/CD** | `ci.yml`, `release.yml` | GitHub Actions → .NET, Cosign, SBOM, NuGet, Docker, GHCR |

---

## Tóm Tắt Tương Tác Quan Trọng / Key Interaction Summary

| Interaction | Pattern / Mô Hình | Performance / Hiệu Năng | Reliability / Độ Tin Cậy |
|------|--------|------------|-----------|
| Pipeline → Sources | Async Parallel | O(sources) | Retry logic in sources |
| Engine → Rules | Parallel + Semaphore | O(contracts * rules / cores) | CancellationToken support |
| Pipeline → Baseline | Sync + Async | O(violations) lookup | Atomic write, memory-mapped |
| Pipeline → Emitter | Sequential Sinks | O(violations) serialize | All sinks must succeed |
| Pipeline → Security | Async Priority Chain | O(1) env var, O(net) vault | Fallback chain, audit log |
| Analyzers → IDE | Incremental Generator | O(changed files) | Roslyn incremental |
| CI → Pipeline | Sequential Jobs | ~5-10 min total | Retry on transient failures |
| Release → NuGet | Signed + Attested | ~2 min | Sigstore keyless, SBOM |

---

*Generated from DataGuard source code. Last updated: 2025-01-19*