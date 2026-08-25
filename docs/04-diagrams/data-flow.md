# Data Flow Diagrams

## 1. High-Level Data Flow Pipeline

```mermaid
flowchart LR
    subgraph Sources ["📥 Contract Sources"]
        EF["EF Core Model<br/>(IModel / ModelSnapshot)"]
        SP["Stored Procedures<br/>(sys.parameters / ALL_ARGUMENTS)"]
        RAW["Raw SQL<br/>(ScriptDOM parsing)"]
        MANUAL["Manual Attributes<br/>([ExpectedColumn] / [ExpectedSpParameter])"]
    end

    subgraph Extraction ["🔍 Extraction Layer"]
        EFS["EfModelSource"]
        SPS["SqlServerStoredProcedureParser"]
        RS["RawSqlParser"]
        MS["ManualContractSource"]
        ORA["OracleReaders<br/>(AllArguments / AllTabColumns)"]
    end

    subgraph Core ["⚙️ Core Engine"]
        DESC["ContractDescriptor<br/>(Entity / SP / RawSql / Schema)"]
        RULES["Rules Engine<br/>(DG001-DG016, MY/PG rules)"]
        CONV["ConcurrentValidationEngine<br/>(Parallel + Backpressure)"]
    end

    subgraph Output ["📤 Output Layer"]
        DIAG["DiagnosticEmitter"]
        SARIF["SARIF 2.1.0"]
        CONSOLE["Console Text"]
        EVIDENCE["Contract Evidence"]
        EXPORT["Contract Export<br/>(JSON + TypeScript DTO)"]
    end

    EF --> EFS
    SP --> SPS
    SP --> ORA
    RAW --> RS
    MANUAL --> MS

    EFS --> DESC
    SPS --> DESC
    RS --> DESC
    MS --> DESC
    ORA --> DESC

    DESC --> RULES
    RULES --> CONV
    CONV --> DIAG

    DIAG --> SARIF
    DIAG --> CONSOLE
    DIAG --> EVIDENCE
    DIAG --> EXPORT
```

## 2. Validation Data Flow Detail

```mermaid
flowchart TD
    A["User runs: dataguard validate"] --> B{"Ground Truth Mode?"}
    
    B -->|"Full (Live DB)"| C["Connect to database"]
    C --> D["Extract schema via adapter<br/>(Oracle/SqlServer/MySql/Pg)"]
    D --> E["Build DatabaseSchemaDescriptor"]
    
    B -->|"Snapshot (Offline)"| F["Load .dataguard-snapshot.json"]
    F --> G["Build DatabaseSchemaDescriptor<br/>from SnapshotTable/Column"]
    
    B -->|"Manual (Attributes)"| H["Load compiled assembly"]
    H --> I["Reflect [ExpectedColumn]<br/>[ExpectedSpParameter]"]
    I --> J["Build EntityDescriptor +<br/>StoredProcedureDescriptor"]
    
    E & G & J --> K["Load .dataguard.yml config"]
    K --> L["Build contract descriptors<br/>(Entity + SP + RawSql)"]
    L --> M["Resolve rules for provider"]
    M --> N["Run ConcurrentValidationEngine"]
    
    N --> O["For each (rule, contract) pair:<br/>Parallel.ForEachAsync"]
    O --> P["Rule.ValidateAsync()"]
    P --> Q{"Violations found?"}
    Q -->|Yes| R["Add to ConcurrentBag"]
    Q -->|No| S["Continue"]
    R --> T{"Backpressure<br/>limit reached?"}
    T -->|Yes| U["Stop adding"]
    T -->|No| S
    S --> O
    U --> V["Sort violations by RuleId + Message"]
    V --> W["Emit via DiagnosticEmitter"]
    
    W --> X["Console output"]
    W --> Y["SARIF file"]
    W --> Z["Evidence artifact"]
    
    X & Y & Z --> AA{"Exit code"}
    AA -->|"0"| AB["No errors"]
    AA -->|"1"| AC["Errors found or drift detected"]
    AA -->|"2"| AD["Config/usage error"]
```

## 3. Source Extraction Data Flow

```mermaid
flowchart LR
    subgraph EFCore ["EF Core Model"]
        IMODEL["IModel<br/>(Runtime)"]
        SNAPSHOT["ModelSnapshot.cs<br/>(Design-time)"]
    end
    
    IMODEL -->|"GetEntityTypes()"| EFS["EfModelSource"]
    SNAPSHOT -->|"Roslyn parse<br/>+ reflection"| EFS
    
    EFS --> ED["EntityDescriptor<br/>(ClrTypeName, TableName, Properties)"]
    EFS --> PD["PropertyDescriptor<br/>(Name, ClrType, ColumnName,<br/>ColumnType, Nullable, MaxLength)"]
    
    subgraph Oracle ["Oracle Sources"]
        AA["ALL_ARGUMENTS<br/>(Parameters)"]
        ATC["ALL_TAB_COLUMNS<br/>(Columns + CHAR/BYTE)"]
        NLS["NLS_SESSION_PARAMETERS<br/>(Length Semantics)"]
        RC["DBMS_SQL<br/>(REF CURSOR describe)"]
    end
    
    AA --> AAR["AllArgumentsReader"]
    ATC --> ATCR["AllTabColumnsReader"]
    NLS --> NLSR["NlsSessionReader"]
    RC --> RCD["RefCursorDescriber"]
    
    AAR --> SPD["StoredProcedureDescriptor<br/>(Parameters, Overload, Sequence)"]
    ATCR --> COLD["ColumnDescriptor<br/>(Name, DataType, MaxLength,<br/>CharUsed, Precision, Scale)"]
    NLSR --> LS["LengthSemantics<br/>(CHAR or BYTE)"]
    RCD --> SPD
    
    subgraph SqlServer ["SQL Server Sources"]
        SSP["sys.parameters"]
        SSC["sys.columns"]
        SCRIPT["ScriptDOM<br/>(TSqlFragmentVisitor)"]
    end
    
    SSP & SSC --> SSRS["SqlServerStoredProcedureParser"]
    SCRIPT --> RSP["RawSqlParser"]
    
    SSRS --> SPD2["StoredProcedureDescriptor"]
    RSP --> RSD["RawSqlDescriptor<br/>(SQL text + extracted params)"]
    
    subgraph Manual ["Manual Source"]
        ASM["Compiled Assembly"]
        ATTR["[ExpectedColumn]<br/>[ExpectedSpParameter]"]
    end
    
    ASM --> MCS["ManualContractSource"]
    ATTR --> MCS
    MCS --> ED2["EntityDescriptor"]
    MCS --> SPD3["StoredProcedureDescriptor"]
```

## 4. Security Data Flow

```mermaid
flowchart TD
    subgraph CredentialSources ["🔐 Credential Sources"]
        ENV["Environment Variables<br/>(CONNECTION_STRING)"]
        KV["Azure Key Vault"]
        AWS["AWS Secrets Manager"]
        VAULT["HashiCorp Vault"]
    end
    
    ENV & KV & AWS & VAULT --> ZTCP["ZeroTrustCredentialProvider"]
    ZTCP --> CH["CredentialHandle<br/>(IDisposable, value cleared on dispose)"]
    
    CH --> CM["CredentialManager"]
    CM --> ENC["Encrypt at rest<br/>(AES-256-GCM)"]
    CM --> ROT["Rotation detection<br/>(30-day warning)"]
    CM --> AUDIT["Audit logging"]
    
    AUDIT --> FAL["FileAuditLogger<br/>(SHA256 hash chain)"]
    FAL --> LOGFILE["audit-log.jsonl"]
    
    subgraph SupplyChain ["🔗 Supply Chain"]
        SCV["SupplyChainVerifier"]
        HASH["Assembly hash<br/>(SHA-256)"]
        DEP["Dependency check<br/>(trusted list)"]
        TAMPER["Tampering detection"]
    end
    
    SCV --> HASH & DEP & TAMPER
    SCV --> RESULT["SupplyChainVerificationResult"]
```

## 5. Baseline & Drift Data Flow

```mermaid
flowchart LR
    subgraph Create ["Create Baseline"]
        VAL1["Run validation"] --> BF["BaselineFile v2<br/>(Version, CreatedAt,<br/>SchemaVersion, GroundTruthMode,<br/>DatabaseVersion, SchemaHash,<br/>Violations, Schema)"]
        BF --> WRITE[".dataguard-baseline.json"]
    end
    
    subgraph Drift ["Drift Detection"]
        SNAP[".dataguard-snapshot.json"] --> LOAD["Load snapshot"]
        DB["Live database"] --> EXTRACT["Extract current schema"]
        LOAD --> COMPARE["Compare schema hash"]
        EXTRACT --> COMPARE
        COMPARE --> DRIFT{"Hash match?"}
        DRIFT -->|Yes| OK["No drift"]
        DRIFT -->|No| REPORT["DriftReport<br/>(NewViolations, BaselineHash,<br/>CurrentHash)"]
    end
    
    subgraph Migrate ["Migration"]
        V1["BaselineFile v1"] --> MIG["BaselineManager.MigrateAsync()"]
        MIG --> V2["BaselineFile v2<br/>(+ DatabaseVersion, SchemaHash)"]
    end
```

## 6. Assessment Data Flow

```mermaid
flowchart TD
    REQ["AssessmentRequest<br/>(WorkspaceRoot, ProjectFilters)"] --> INV["InventoryPack.DiscoverProjects()"]
    INV --> PROJ["List of .csproj files"]
    
    PROJ --> PIR["ProjectInventoryReader.Read()<br/>(TFM, SDK-style, PackageRefs, ProjectRefs)"]
    PIR --> FACTS["ProjectFacts"]
    
    FACTS --> INVASS["InventoryPack.Assess()<br/>(TFM support status via LegacySupportTable)"]
    FACTS --> DHP["DependencyHealthPack.Assess()<br/>(lock file consistency)"]
    
    WS["Workspace root"] --> BCI["BuildCiPack.Assess()<br/>(global.json SDK pinning,<br/>CI matrix coverage)"]
    WS --> SEC["SecretsPack.AssessFile()<br/>(secret-like values in .config/.yml)"]
    WS --> SEC2["SecretsPack.AssessMachinePaths()<br/>(absolute paths in config)"]
    
    INVASS & DHP & BCI & SEC & SEC2 --> FINDINGS["AssessmentFinding[]"]
    FINDINGS --> REPORT["AssessmentReport<br/>(Summary, Findings, Errors)"]
    
    REPORT --> JSON["JSON output"]
    REPORT --> SARIF2["SARIF output"]
    
    FINDINGS --> UP["UpgradePlanner.Plan()"]
    UP --> STEPS["UpgradeStep[]<br/>(ordered leaf-first,<br/>blocking findings,<br/>validation commands)"]
```
