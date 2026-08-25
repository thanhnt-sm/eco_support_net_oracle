# State Machine & Status Flow

## 1. Validation Lifecycle State Machine

```mermaid
stateDiagram-v2
    [*] --> Idle: CLI invoked
    
    Idle --> LoadingConfig: dataguard validate
    LoadingConfig --> ConfigLoaded: .dataguard.yml parsed
    LoadingConfig --> ConfigError: Invalid/missing config
    
    ConfigLoaded --> DetectingProvider: Auto-detect enabled
    ConfigLoaded --> ResolvingCredentials: Provider specified
    
    DetectingProvider --> ResolvingCredentials: Provider detected
    DetectingProvider --> UnknownProvider: No EF Core/Dapper found
    
    ResolvingCredentials --> ExtractingContracts: Credentials resolved
    ResolvingCredentials --> CredentialError: No credentials found
    
    ExtractingContracts --> Validating: Contracts extracted
    ExtractingContracts --> ExtractionError: DB connection failed
    
    Validating --> EmittingResults: All rules evaluated
    Validating --> ValidationTimeout: Timeout exceeded (300s default)
    
    EmittingResults --> Complete: Output written
    
    Complete --> [*]: Exit code 0 (no errors)
    Complete --> [*]: Exit code 1 (violations found)
    
    ConfigError --> [*]: Exit code 2
    UnknownProvider --> [*]: Exit code 2
    CredentialError --> [*]: Exit code 1
    ExtractionError --> [*]: Exit code 1
    ValidationTimeout --> [*]: Exit code 1
```

## 2. Baseline Lifecycle State Machine

```mermaid
stateDiagram-v2
    [*] --> NoBaseline: Fresh project
    
    NoBaseline --> CreatingBaseline: dataguard baseline
    CreatingBaseline --> BaselineActive: .dataguard-baseline.json written
    
    BaselineActive --> Validating: dataguard validate
    Validating --> FilteringBaseline: Load baseline
    FilteringBaseline --> ReportingNew: Diff current vs baseline
    ReportingNew --> BaselineActive: Show only new violations
    
    BaselineActive --> Drifting: Schema changes detected
    Drifting --> UpdatingBaseline: dataguard baseline (re-freeze)
    UpdatingBaseline --> BaselineActive: New baseline written
    
    BaselineActive --> Migrating: dataguard migrate
    Migrating --> BaselineActive: v1 → v2 converted
    
    BaselineActive --> Deleting: User deletes file
    Deleting --> NoBaseline: Back to fresh state
```

## 3. Snapshot Lifecycle State Machine

```mermaid
stateDiagram-v2
    [*] --> NoSnapshot: No snapshot file
    
    NoSnapshot --> CreatingSnapshot: dataguard snapshot refresh
    CreatingSnapshot --> SnapshotActive: .dataguard-snapshot.json written
    
    SnapshotActive --> OfflineValidation: dataguard validate --offline
    OfflineValidation --> SnapshotActive: Validation complete
    
    SnapshotActive --> DriftCheck: dataguard snapshot diff
    DriftCheck --> NoDrift: Schema matches
    DriftCheck --> DriftDetected: Schema differs
    
    NoDrift --> SnapshotActive: Continue
    DriftDetected --> SnapshotActive: --fail-on-drift → exit 1
    DriftDetected --> RefreshingSnapshot: dataguard snapshot refresh
    RefreshingSnapshot --> SnapshotActive: Updated
    
    SnapshotActive --> Showing: dataguard snapshot show
    Showing --> SnapshotActive: Display info
```

## 4. Assessment Report State Machine

```mermaid
stateDiagram-v2
    [*] --> Requested: dataguard assess
    
    Requested --> Discovering: InventoryPack.DiscoverProjects()
    Discovering --> NoProjects: 0 projects found
    Discovering --> Assessing: Projects found
    
    Assessing --> InventoryPass: TFM analysis
    InventoryPass --> DependencyPass: Lock file check
    DependencyPass --> BuildCiPass: SDK/CI check
    BuildCiPass --> SecretsPass: Secret scan
    SecretsPass --> Aggregating: All passes complete
    
    Aggregating --> ReportReady: AssessmentReport built
    
    ReportReady --> JsonOutput: --format json
    ReportReady --> SarifOutput: --format sarif
    ReportReady --> TextOutput: --format text (default)
    
    JsonOutput --> [*]: Exit 0 or 1
    SarifOutput --> [*]: Exit 0 or 1
    TextOutput --> [*]: Exit 0 or 1
    NoProjects --> [*]: Exit 1
```

## 5. Plugin Lifecycle State Machine

```mermaid
stateDiagram-v2
    [*] --> Discovering: RulePluginManager created
    
    Discovering --> Loading: MEF composition
    Loading --> Validating: Assembly loaded
    Validating --> Ready: Metadata valid
    Validating --> Rejected: Version mismatch / invalid
    
    Ready --> Executing: Validation request
    Executing --> Ready: Violations returned
    
    Ready --> Unloading: Dispose
    Unloading --> [*]: AssemblyLoadContext unloaded
    
    Rejected --> [*]: Plugin skipped
```

## 6. Credential Lifecycle State Machine

```mermaid
stateDiagram-v2
    [*] --> Resolving: GetDatabaseConnectionAsync()
    
    Resolving --> FromEnv: CONNECTION_STRING set
    Resolving --> FromVault: KeyVault/SecretsManager configured
    Resolving --> FromConfig: AllowPlaintextConfigFallback=true
    Resolving --> Failed: No source available
    
    FromEnv --> HandleCreated: CredentialHandle created
    FromVault --> HandleCreated
    FromConfig --> HandleCreated
    
    HandleCreated --> Active: Value accessible
    Active --> Rotating: 30-day warning
    Rotating --> Active: New credential fetched
    
    Active --> Disposed: IDisposable.Dispose()
    Disposed --> [*]: Value cleared from memory
    
    Failed --> [*]: CredentialError
```

## 7. CI Pipeline State Machine

```mermaid
stateDiagram-v2
    [*] --> Triggered: Push/PR to main/develop
    
    Triggered --> Building: Build job starts
    Triggered --> SecurityScan: Security job starts
    Triggered --> SbomGen: SBOM job starts
    Triggered --> CodeQlRun: CodeQL job starts
    
    Building --> BuildPassed: All tests pass, coverage ≥60%
    Building --> BuildFailed: Tests fail or coverage <60%
    
    SecurityScan --> SecurityClean: No vulns, no secrets
    SecurityScan --> SecurityFailed: Vuln or secret found
    
    SbomGen --> SbomReady: SBOM generated
    CodeQlRun --> CodeQlClean: No issues
    
    BuildPassed --> DockerSmoke: docker-smoke job
    DockerSmoke --> DockerPassed: Image builds, --help works
    DockerSmoke --> DockerFailed: Build or smoke fails
    
    BuildPassed & SecurityClean & SbomReady & CodeQlClean & DockerPassed --> AllGreen: All jobs pass
    BuildFailed & SecurityFailed & DockerFailed --> Failed: At least one fails
    
    AllGreen --> [*]: ✅ CI green
    Failed --> [*]: ❌ CI red
```
