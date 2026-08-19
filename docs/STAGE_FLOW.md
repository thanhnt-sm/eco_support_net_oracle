# Luồng Giai Đoạn & Trạng Thái / Stage & Status Flow / Luồng Giai Đoạn & Trạng Thái

## Tổng Quan Vòng Đời / Lifecycle Overview / Tổng Quan Vòng Đời

```mermaid
stateDiagram-v2
    [*] --> Init: dataguard init
    Init --> Config: .dataguard.yml created
    Config --> Baseline: dataguard baseline (legacy)
    Config --> Ready: Ready for CI
    Baseline --> Ready: Baseline committed
    Ready --> Validate: PR Created / Push
    Validate --> Analyzing: Extract Contracts
    Analyzing --> Validating: Run Rules
    Validating --> Filtering: Apply Baseline
    Filtering --> Reporting: Emit SARIF/Console
    Reporting --> Passed: No new violations
    Reporting --> Failed: New violations found
    Passed --> Merge: PR Approved
    Failed --> Fix: Developer fixes code
    Fix --> Validate: Re-run validation
    Merge --> Deploy: Production Deploy
    Deploy --> SnapshotRefresh: dataguard snapshot refresh
    SnapshotRefresh --> Ready: Schema updated
```

---

## Trạng Thái Pipeline CI / CI Pipeline States / Trạng Thái Pipeline CI

```mermaid
stateDiagram-v2
    [*] --> Queued: Push/PR Triggered
    Queued --> Running: Agent Available
    Running --> Restoring: dotnet restore
    Restoring --> Building: dotnet build
    Building --> BuildingAnalyzers: dotnet build /p:RunAnalyzers
    BuildingAnalyzers --> Testing: dotnet test
    Testing --> SecurityScan: Security Scan
    SecurityScan --> GeneratingSBOM: Generate SBOM
    GeneratingSBOM --> UploadingArtifacts: Upload Test/SBOM
    UploadingArtifacts --> Completed: All Jobs Done
    Completed --> Success: All Green
    Completed --> Failure: Any Red
    Failure --> Failed: Red Build
    Success --> ReleaseReady: Tag Push
    ReleaseReady --> Publishing: Publish NuGet
    Publishing --> Published: On NuGet.org
    Published --> ReleaseCreated: GitHub Release
    ReleaseCreated --> DockerPushed: GHCR Image
    DockerPushed --> Done: Release Complete
```

---

## Trạng Thái Xác Thực / Validation States / Trạng Thái Xác Thực

```mermaid
stateDiagram-v2
    [*] --> Idle: Waiting for Trigger
    Idle --> Extracting: ValidateAsync Called
    Extracting --> ExtractingEF: EF Model Extraction
    ExtractingEF --> ExtractingSP: SP Extraction
    ExtractingSP --> ExtractingRaw: Raw SQL Extraction
    ExtractingRaw --> Validating: All Contracts Ready
    Validating --> OrderingRules: RuleDependencyGraph
    OrderingRules --> Executing: ConcurrentValidationEngine
    Executing --> RunningRules: Parallel Rule Execution
    RunningRules --> CollectingViolations: Aggregate Results
    CollectingViolations --> FilteringBaseline: BaselineManager
    FilteringBaseline --> Emitting: DiagnosticEmitter
    Emitting --> EmittingSARIF: SarifSink
    EmittingSARIF --> EmittingConsole: ConsoleSink
    EmittingConsole --> Complete: ValidationResult
    Complete --> [*]
```

---

## Trạng Thái Baseline / Baseline States / Trạng Thái Baseline

```mermaid
stateDiagram-v2
    [*] --> NoBaseline: Fresh Repo
    NoBaseline --> Creating: dataguard baseline
    Creating --> BaselineActive: File Created
    BaselineActive --> Validating: dataguard validate
    Validating --> Filtering: FilterNewViolations
    Filtering --> NoNewViolations: Clean
    Filtering --> NewViolationsFound: Drift Detected
    NewViolationsFound --> Fixing: Developer Fixes
    Fixing --> Validating: Re-run
    Validating --> Clean: All Fixed
    BaselineActive --> SchemaChange: DBA Changes Proc
    SchemaChange --> Refreshing: dataguard snapshot refresh
    Refreshing --> BaselineUpdated: New Snapshot
    BaselineUpdated --> Validating: Next CI Run
    BaselineActive --> ManualOverride: [ExpectedColumn] Attributes
    ManualOverride --> Validating: Manual Mode
```

---

## Trạng Thái CLI / CLI Command States / Trạng Thái CLI

```mermaid
stateDiagram-v2
    [*] --> CommandParsed: dataguard <command>
    CommandParsed --> Init: init
    CommandParsed --> Validate: validate
    CommandParsed --> Baseline: baseline
    CommandParsed --> Snapshot: snapshot
    CommandParsed --> Config: config
    CommandParsed --> OracleCheck: oracle-check
    CommandParsed --> Version: version
    CommandParsed --> Hook: hook

    Init --> Interactive: --wizard flag
    Init --> AutoDetect: auto-detect provider/EF/Dapper
    Init --> GenerateConfig: Write .dataguard.yml
    Init --> Complete: Config Saved

    Validate --> LoadConfig: Load .dataguard.yml
    LoadConfig --> AutoDetectProvider: Auto-detect
    AutoDetectProvider --> ExtractContracts: All Sources
    ExtractContracts --> RunValidation: ValidationPipeline
    RunValidation --> ApplyBaseline: BaselineManager
    ApplyBaseline --> EmitResults: DiagnosticEmitter
    EmitResults --> ExitCode: 0=Pass, 1=Fail

    Baseline --> LoadConfig: Load .dataguard.yml
    LoadConfig --> RunValidation: Full Validation
    RunValidation --> CreateBaseline: BaselineManager
    CreateBaseline --> SaveFile: .dataguard-baseline.json
    SaveFile --> Complete: Baseline Created

    Snapshot --> Refresh: snapshot refresh
    Refresh --> RunValidation: Full Validation
    RunValidation --> CreateSnapshot: BaselineManager
    CreateSnapshot --> SaveFile: .dataguard-snapshot.json
    SaveFile --> Complete: Snapshot Refreshed

    Snapshot --> Show: snapshot show
    Show --> LoadBaseline: BaselineManager.LoadAsync
    LoadBaseline --> DisplayInfo: Version, Hash, Count
    DisplayInfo --> Complete

    Snapshot --> Diff: snapshot diff
    Diff --> LoadSnapshot: BaselineManager.LoadAsync
    LoadSnapshot --> RunValidation: Current State
    RunValidation --> CompareHashes: SchemaHash Compare
    CompareHashes --> ShowDiff: Display Changes
    ShowDiff --> Complete

    Config --> Show: config show
    Show --> LoadConfig: Deserialize YAML
    LoadConfig --> DisplayJSON: Pretty Print
    DisplayJSON --> Complete

    Config --> Validate: config validate
    Validate --> CheckConfig: Validate Schema
    CheckConfig --> DisplayResult: Valid/Invalid
    DisplayResult --> Complete

    OracleCheck --> LoadConfig: Load .dataguard.yml
    LoadConfig --> RunOracleValidation: Oracle-Specific
    RunOracleValidation --> EmitResults: Oracle Violations
    EmitResults --> ExitCode: 0=Pass, 1=Fail

    Version --> PrintVersion: Assembly Info
    PrintVersion --> Complete

    Hook --> DetectType: Auto-detect Husky/Lefthook/Native
    DetectType --> InstallHook: Write Hook File
    InstallHook --> Complete: Hook Installed
```

---

## Trạng Thái Analyzer / Analyzer States (IDE vs CI)

```mermaid
stateDiagram-v2
    state "IDE Layer (Incremental Generator)" as IDE {
        [*] --> Idle: Editor Open
        Idle --> Analyzing: Keystroke/Change
        Analyzing --> SyntaxCheck: IsPotentialSqlCall
        SyntaxCheck --> MatchFound: SQL Call Detected
        MatchFound --> EmitDiagnostic: UnvalidatedSqlCall
        EmitDiagnostic --> ShowSquiggle: IDE Warning
        ShowSquiggle --> CodeFixAvailable: Lightbulb
        CodeFixAvailable --> UserAction: User Clicks Fix
        UserAction --> ApplyFix: CodeFixProvider
        ApplyFix --> Idle: Fixed/Ignored
    }

    state "CI Layer (DiagnosticAnalyzer)" as CI {
        [*] --> Idle: Build Start
        Idle --> Initialized: Initialize(AnalysisContext)
        Initialized --> RegisterActions: RegisterOperationAction
        RegisterActions --> Waiting: Waiting for Invocations
        Waiting --> Analyzing: InvocationOperation
        Analyzing --> SemanticCheck: GetSymbolInfo
        SemanticCheck --> MatchEF: IsEfCoreFromSqlMethod
        SemanticCheck --> MatchExec: IsExecuteSqlMethod
        SemanticCheck --> MatchDapper: IsDapperQueryMethod
        MatchEF --> AnalyzeEF: AnalyzeEfCoreFromSql
        MatchExec --> AnalyzeExec: AnalyzeExecuteSql
        MatchDapper --> AnalyzeDapper: AnalyzeDapperQuery
        AnalyzeEF --> EmitCI: ParameterMismatch/Unvalidated
        AnalyzeExec --> EmitCI: ParameterMismatch
        AnalyzeDapper --> EmitCI: ParameterMismatch
        EmitCI --> Idle
    }

    state "Code Fix Providers" as Fixes {
        [*] --> FixRegistered: RegisterCodeFixesAsync
        FixRegistered --> UserInvoked: User Clicks Lightbulb
        UserInvoked --> FixApplied: ApplyFix
        FixApplied --> DocumentUpdated: Document Updated
        DocumentUpdated --> Reanalysis: IDE Re-analyzes
        Reanalysis --> DiagnosticCleared: Warning/Error Gone
    }
```

---

## Trạng Thái CI/CD Pipeline / CI/CD Pipeline States

```mermaid
graph TD
    subgraph "Build Stage"
        B1[Restore] --> B2[Build]
        B2 --> B3[Build + Analyzers]
        B3 --> B4[Test]
    end

    subgraph "Security Stage"
        S1[Secrets Scan] --> S2[OWASP Audit]
        S2 --> S3[CodeQL]
    end

    subgraph "SBOM Stage"
        SB1[Generate SBOM] --> SB2[Upload Artifact]
    end

    subgraph "Release Stage"
        R1[Sign Packages] --> R2[Publish NuGet]
        R2 --> R3[Create Release]
        R3 --> R4[Docker Push]
    end

    B4 --> S1
    B4 --> SB1
    S3 --> R1
```

---

## Ma Trạng Thái / State Transition Matrix / Ma Trạng Thái

| Current State / Trạng Thái Hiện Tại | Trigger / Kích Hoạt | Next State / Trạng Thái Tiếp Theo | Condition / Điều Kiện |
|------|--------|---------|--------|
| Idle | Push/PR | Queued | GitHub Action Triggered |
| Queued | Agent Free | Running | Runner Available |
| Restoring | Restore Done | Building | `dotnet restore` success |
| Building | Build Done | BuildingAnalyzers | `dotnet build` success |
| BuildingAnalyzers | Analyzers Done | Testing | `dotnet build /p:RunAnalyzers` success |
| Testing | Tests Pass | SecurityScan | All Tests Pass |
| Testing | Tests Fail | Failed | Any Test Fail |
| SecurityScan | Scan Pass | GeneratingSBOM | No High/Critical |
| SecurityScan | Scan Fail | Failed | High/Critical Found |
| GeneratingSBOM | SBOM Done | UploadingArtifacts | SBOM Generated |
| UploadingArtifacts | Upload Done | Completed | Artifacts Uploaded |
| Completed | All Green | Success | All Jobs Green |
| Completed | Any Red | Failed | Any Job Red |
| Success | Tag Pushed | ReleaseReady | Git Tag `v*` |
| ReleaseReady | Publish Done | Published | NuGet Push Success |
| Published | Release Created | ReleaseCreated | GitHub Release Created |
| ReleaseCreated | Docker Push Done | DockerPushed | GHCR Push Success |
| DockerPushed | All Done | Done | All Release Steps Done |

---

## Trạng Thái Xác Thực Chi Tiết / Validation Sub-States

```mermaid
stateDiagram-v2
    state "Extraction Phase" as Extract {
        [*] --> EFModel: EfModelSource
        EFModel --> StoredProc: StoredProcedureParser
        StoredProc --> RawSQL: RawSqlParser
        RawSQL --> ContractsReady: All Contracts
    }

    state "Validation Phase" as Validate {
        ContractsReady --> RuleOrdering: RuleDependencyGraph
        RuleOrdering --> ParallelExecution: ConcurrentValidationEngine
        ParallelExecution --> RuleExecution: Partitioner + Semaphore
        RuleExecution --> RuleLoop: For Each Rule
        RuleLoop --> ValidateCall: rule.ValidateAsync
        ValidateCall --> CollectViolations: Add to Queue
        RuleLoop --> NextRule: Until All Rules
        CollectViolations --> AllRulesDone
    }

    state "Post-Processing" as PostProcess {
        AllRulesDone --> BaselineFilter: BaselineManager
        BaselineFilter --> FilterNew: FilterNewViolations
        FilterNew --> EmitResults: DiagnosticEmitter
        EmitResults --> SARIF: SarifSink
        EmitResults --> Console: ConsoleSink
        EmitResults --> Markdown: MarkdownSink
        EmitResults --> Complete: ValidationResult
    }
```

---

## Ma Trạng Thái Lệnh CLI / CLI Command State Matrix

| Command / Lệnh | Sub-Commands / Sub-lệnh | Flags / Cờ | Exit Codes / Mã Thoát |
|----------|----------------|---------|-------------|
| `validate` | - | `--connection`, `--config`, `--output`, `--format`, `--offline`, `--verbose`, `--provider`, `--schema`, `--package` | 0=Pass, 1=Fail |
| `baseline` | - | `--connection`, `--config`, `--output`, `--verbose`, `--provider`, `--schema`, `--package` | 0=Success, 1=Error |
| `snapshot` | `refresh`, `show`, `diff` | `--connection`, `--config`, `--verbose`, `--provider`, `--schema`, `--package` | 0=Success, 1=Error |
| `init` | - | `--output`, `--provider` | 0=Success, 1=Error |
| `config` | `show`, `validate` | `--config` | 0=Valid, 1=Invalid |
| `oracle-check` | - | `--connection`, `--config`, `--output`, `--format`, `--verbose`, `--schema`, `--package` | 0=Pass, 1=Fail |
| `version` | - | - | 0=Success |
| `hook` | `install`, `uninstall`, `status` | `--repo-root`, `--type`, `--force` | 0=Success, 1=Error |

---

## Trạng Thái Health Check / Health Check States

```mermaid
stateDiagram-v2
    [*] --> LivenessCheck: GET /health/live
    LivenessCheck --> Healthy: Process Running
    LivenessCheck --> Unhealthy: Process Dead

    [*] --> ReadinessCheck: GET /health/ready
    ReadinessCheck --> CredentialsCheck: Check Credentials
    CredentialsCheck --> BaselineCheck: Check Baseline
    BaselineCheck --> SupplyChainCheck: Check Supply Chain
    SupplyChainCheck --> DiskCheck: Check Disk Space
    DiskCheck --> MemoryCheck: Check Memory Pressure
    MemoryCheck --> Ready: All Healthy
    MemoryCheck --> Degraded: Some Degraded
    MemoryCheck --> Unhealthy: Any Unhealthy

    [*] --> StartupCheck: GET /health/startup
    StartupCheck --> Starting: Uptime < 30s
    StartupCheck --> Started: Uptime >= 30s
```

---

## Ma Trạng Thái Health Check / Health Check State Matrix

| Check / Kiểm Tra | Healthy / Khỏe | Degraded / Giảm Sức | Unhealthy / Bệnh | Unknown / Không Xác Định |
|--------|----------|----------------|-----------|----------------|
| Liveness / Sống | Process Running | - | Process Dead | - |
| Credentials / Thông Tin Đăng Nhập | Connection String Available | - | Missing/Invalid | Error Getting |
| Baseline / Cơ Sở | Loaded Successfully | File Missing | Corrupt/Unreadable | Error Reading |
| Supply Chain / Cung Ứng | All Checks Pass | 1+ Warnings | Any Failure | Error Running |
| Disk Space / Đĩa | > 10% Free | 5-10% Free | < 5% Free | Error Checking |
| Memory / Bộ Nhớ | < 500MB | 500MB - 1GB | > 1GB | Error Reading |
| Startup / Khởi Động | Uptime >= 30s | - | Uptime < 30s | - |

---

## Bảng Chuyển Trạng Thái CLI / CLI Command Transition Table

| Current Command / Lệnh Hiện Tại | Next Valid Commands / Lệnh Tiếp Theo Hợp Lệ | Notes / Ghi Chú |
|----------|----------------|--------|
| `init` | `validate`, `baseline`, `config show`, `hook install` | Creates `.dataguard.yml` |
| `validate` | `baseline`, `snapshot refresh`, `config show` | Requires config + connection |
| `baseline` | `validate`, `snapshot refresh`, `snapshot diff` | Creates/updates baseline file |
| `snapshot refresh` | `validate`, `snapshot diff`, `snapshot show` | Updates snapshot file |
| `snapshot show` | `snapshot diff`, `validate` | Read-only |
| `snapshot diff` | `snapshot refresh`, `validate` | Shows drift |
| `config show` | `config validate`, `validate` | Read-only |
| `config validate` | `validate`, `init` | Validates current config |
| `oracle-check` | `validate`, `snapshot refresh` | Oracle-specific rules |
| `hook install` | `validate`, `hook status` | Installs pre-commit hook |
| `hook uninstall` | `hook install`, `hook status` | Removes hooks |
| `hook status` | `hook install`, `hook uninstall` | Read-only status |

---

*Generated from DataGuard source code. Last updated: 2025-01-19*