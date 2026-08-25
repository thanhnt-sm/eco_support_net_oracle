# Sequence Diagrams

## 1. Full Validation Flow (CLI → Database → Output)

```mermaid
sequenceDiagram
    actor User
    participant CLI as DataGuard.Cli
    participant Config as .dataguard.yml
    participant ZTCP as ZeroTrustCredentialProvider
    participant Adapter as DB Adapter<br/>(Oracle/SqlServer/MySql/Pg)
    participant DB as Database
    participant Sources as Contract Sources
    participant Engine as ConcurrentValidationEngine
    participant Rules as Rules Engine
    participant Emitter as DiagnosticEmitter
    participant Output as Output (Console/SARIF/Evidence)

    User->>CLI: dataguard validate --provider oracle
    CLI->>Config: LoadConfig()
    Config-->>CLI: DataGuardConfiguration
    
    CLI->>ZTCP: GetDatabaseConnectionAsync()
    ZTCP->>ZTCP: Resolve from env/vault
    ZTCP-->>CLI: CredentialHandle
    
    CLI->>Adapter: ExtractContractsAsync()
    Adapter->>DB: Query ALL_ARGUMENTS / sys.parameters
    DB-->>Adapter: Parameter metadata
    Adapter->>DB: Query ALL_TAB_COLUMNS / sys.columns
    DB-->>Adapter: Column metadata
    Adapter-->>CLI: ContractDescriptor[]
    
    CLI->>Sources: EfModelSource.ExtractContractsAsync()
    Sources-->>CLI: EntityDescriptor[]
    
    CLI->>Rules: GetRulesForProvider("oracle")
    Rules-->>CLI: IContractRule[]
    
    CLI->>Engine: ValidateAsync(contracts, rules)
    
    loop For each (rule, contract) in parallel
        Engine->>Rules: rule.ValidateAsync(contract, allContracts)
        Rules-->>Engine: ContractViolation[]
    end
    
    Engine-->>CLI: ContractViolation[] (sorted)
    
    CLI->>Emitter: EmitAsync(violations)
    Emitter->>Output: Write SARIF / Console / Evidence
    Output-->>User: Validation results
    
    CLI-->>User: Exit code (0/1/2)
```

## 2. IDE Analysis Flow (VS Code → Analyzer → Quick Fix)

```mermaid
sequenceDiagram
    actor Developer
    participant VSCode as VS Code
    participant Ext as DataGuard Extension
    participant CLI as dataguard CLI
    participant Analyzer as Roslyn Analyzer
    participant CodeFix as Code Fix Provider
    participant Diag as DiagnosticCollection

    Developer->>VSCode: Open .cs file
    VSCode->>Analyzer: Incremental generator triggers
    Analyzer->>Analyzer: Scan syntax tree for SQL calls
    Analyzer-->>VSCode: Diagnostic DG001 (squiggly)
    
    Developer->>VSCode: Hover on squiggly
    VSCode-->>Developer: "Parameter count mismatch: expected 3, got 2"
    
    Developer->>VSCode: Ctrl+. (Quick Fix)
    VSCode->>CodeFix: RegisterCodeFixesAsync()
    CodeFix-->>VSCode: "Add missing parameter" / "Add [SkipContractCheck]"
    Developer->>VSCode: Select fix
    CodeFix->>VSCode: Apply code change
    
    Developer->>VSCode: Run full validation (Ctrl+Shift+P → DataGuard)
    VSCode->>Ext: runValidation()
    Ext->>CLI: spawn("dataguard", ["validate", "--format", "sarif"])
    CLI-->>Ext: SARIF output
    Ext->>Diag: Load SARIF diagnostics
    Diag-->>VSCode: Show in Problems panel
    VSCode-->>Developer: All violations listed
```

## 3. CI Pipeline Flow

```mermaid
sequenceDiagram
    actor Dev as Developer
    participant Git as Git
    participant GH as GitHub Actions
    participant Build as Build Job
    participant Security as Security Scan
    participant SBOM as SBOM Generation
    participant Docker as Docker Smoke
    participant CodeQL as CodeQL Analysis

    Dev->>Git: git push / PR
    Git->>GH: Trigger CI workflow
    
    par Build and Test
        GH->>Build: dotnet restore --locked
        Build->>Build: dotnet build -c Release
        Build->>Build: dotnet format --verify-no-changes
        Build->>Build: dotnet test (with coverage)
        Build->>Build: Coverage gate (≥60%)
    and Security Scan
        GH->>Security: NuGet vulnerability check
        GH->>Security: TruffleHog secret scan
    and SBOM
        GH->>SBOM: Generate SBOM (Microsoft.Sbom.DotNetTool)
    and CodeQL
        GH->>CodeQL: C# analysis with custom queries
    end
    
    Build-->>GH: ✅ Pass / ❌ Fail
    Security-->>GH: ✅ Clean / ❌ Vulnerability found
    SBOM-->>GH: ✅ SBOM generated
    CodeQL-->>GH: ✅ No issues
    
    GH->>Docker: Build Docker image
    Docker->>Docker: Smoke test (--help)
    Docker-->>GH: ✅ Image works
    
    GH-->>Dev: CI status (green/red)
```

## 4. Baseline Lifecycle Sequence

```mermaid
sequenceDiagram
    actor User
    participant CLI as DataGuard.Cli
    participant BM as BaselineManager
    participant Engine as Validation Engine
    participant DB as Database
    participant FS as File System

    User->>CLI: dataguard baseline
    CLI->>Engine: Run full validation
    Engine->>DB: Extract all contracts
    DB-->>Engine: ContractDescriptor[]
    Engine->>Engine: Validate all rules
    Engine-->>CLI: ContractViolation[]
    
    CLI->>BM: CreateBaselineAsync(violations)
    BM->>DB: GetDatabaseVersionAsync()
    DB-->>BM: version string
    BM->>BM: ComputeSchemaHash(violations)
    BM->>BM: Build BaselineFile v2
    BM->>FS: Write .dataguard-baseline.json
    FS-->>BM: Written
    BM-->>CLI: BaselineInfo
    CLI-->>User: "Baseline created: 15 violations frozen"
    
    Note over User,FS: Later: dataguard validate (with baseline)
    User->>CLI: dataguard validate
    CLI->>BM: LoadBaselineAsync()
    BM->>FS: Read .dataguard-baseline.json
    FS-->>BM: BaselineFile
    CLI->>Engine: Validate
    Engine-->>CLI: Current violations
    CLI->>BM: Diff(current, baseline)
    BM-->>CLI: New violations only
    CLI-->>User: "3 new violations (15 baselined)"
```

## 5. Snapshot Drift Detection Sequence

```mermaid
sequenceDiagram
    actor User
    participant CLI as DataGuard.Cli
    participant BM as BaselineManager
    participant Adapter as DB Adapter
    participant DB as Database
    participant FS as File System

    User->>CLI: dataguard snapshot diff --fail-on-drift
    CLI->>FS: Load .dataguard-snapshot.json
    FS-->>CLI: SnapshotTable[]
    
    CLI->>Adapter: Extract current schema
    Adapter->>DB: Query ALL_TAB_COLUMNS / sys.columns
    DB-->>Adapter: Current columns
    Adapter-->>CLI: Current schema
    
    CLI->>BM: Compare(snapshot, current)
    BM->>BM: Hash comparison
    
    alt Drift detected
        BM-->>CLI: DriftReport (HasDrift=true)
        CLI-->>User: "DRIFT: CUSTOMERS.EMAIL changed VARCHAR2(100)→VARCHAR2(255)"
        CLI-->>User: Exit code 1
    else No drift
        BM-->>CLI: DriftReport (HasDrift=false)
        CLI-->>User: "No schema drift detected"
        CLI-->>User: Exit code 0
    end
```

## 6. Plugin Loading Sequence

```mermaid
sequenceDiagram
    participant PM as RulePluginManager
    participant MEF as MEF Container
    participant ASM as Plugin Assembly
    participant ALD as AssemblyLoadContext
    participant Rule as Custom Rule

    PM->>ALD: Create isolated context
    ALD->>ASM: Load plugin assembly
    ASM-->>MEF: ExportRuleAttribute types
    MEF->>MEF: Discover [ExportRule] exports
    MEF-->>PM: Lazy<IContractRule, RulePluginMetadata>[]
    
    PM->>PM: Validate metadata (MinDataGuardVersion)
    PM->>PM: Check tags and categories
    
    Note over PM,Rule: On validation request
    PM->>Rule: Create instance
    Rule->>Rule: ValidateCoreAsync()
    Rule-->>PM: ContractViolation[]
    
    PM->>ALD: Unload context (cleanup)
```

## 7. Credential Resolution Sequence

```mermaid
sequenceDiagram
    participant CLI as DataGuard.Cli
    participant ZTCP as ZeroTrustCredentialProvider
    participant ENV as Environment Variables
    participant KV as Azure Key Vault
    participant AWS as AWS Secrets Manager
    participant VAULT as HashiCorp Vault
    participant CM as CredentialManager
    participant AUDIT as FileAuditLogger

    CLI->>ZTCP: GetDatabaseConnectionAsync()
    
    alt CONNECTION_STRING env var
        ZTCP->>ENV: Read CONNECTION_STRING
        ENV-->>ZTCP: Connection string
    else KeyVaultUri configured
        ZTCP->>KV: GetSecret("connection-string")
        KV-->>ZTCP: Secret value
    else AwsRegion configured
        ZTCP->>AWS: GetSecretValue("dataguard/connection")
        AWS-->>ZTCP: Secret value
    else VaultAddress configured
        ZTCP->>VAULT: Read("secret/data/dataguard")
        VAULT-->>ZTCP: Secret value
    end
    
    ZTCP->>ZTCP: Create CredentialHandle
    ZTCP-->>CLI: CredentialHandle (value never logged)
    
    CLI->>CM: StoreCredential(handle)
    CM->>CM: Encrypt at rest (AES-256-GCM)
    CM->>CM: Check rotation (30-day warning)
    CM->>AUDIT: LogCredentialAccessAsync()
    AUDIT->>AUDIT: Append to hash chain
```
