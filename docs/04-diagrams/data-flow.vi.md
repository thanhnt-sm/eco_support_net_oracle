# Sơ Đồ Luồng Dữ Liệu

## 1. Đường Ống Luồng Dữ Liệu Tổng Quan

```mermaid
flowchart LR
    subgraph Sources ["📥 Nguồn Contract"]
        EF["EF Core Model<br/>(IModel / ModelSnapshot)"]
        SP["Stored Procedures<br/>(sys.parameters / ALL_ARGUMENTS)"]
        RAW["Raw SQL<br/>(ScriptDOM parsing)"]
        MANUAL["Attributes thủ công<br/>([ExpectedColumn] / [ExpectedSpParameter])"]
    end

    subgraph Extraction ["🔍 Lớp Trích Xuất"]
        EFS["EfModelSource"]
        SPS["SqlServerStoredProcedureParser"]
        RS["RawSqlParser"]
        MS["ManualContractSource"]
        ORA["OracleReaders<br/>(AllArguments / AllTabColumns)"]
    end

    subgraph Core ["⚙️ Engine Chính"]
        DESC["ContractDescriptor<br/>(Entity / SP / RawSql / Schema)"]
        RULES["Rules Engine<br/>(DG001-DG016, MY/PG rules)"]
        CONV["ConcurrentValidationEngine<br/>(Song song + Backpressure)"]
    end

    subgraph Output ["📤 Lớp Đầu Ra"]
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

## 2. Chi Tiết Luồng Dữ Liệu Validation

```mermaid
flowchart TD
    A["Người dùng chạy: dataguard validate"] --> B{"Chế độ Ground Truth?"}
    
    B -->|"Full (DB trực tiếp)"| C["Kết nối database"]
    C --> D["Trích xuất schema qua adapter<br/>(Oracle/SqlServer/MySql/Pg)"]
    D --> E["Tạo DatabaseSchemaDescriptor"]
    
    B -->|"Snapshot (Offline)"| F["Tải .dataguard-snapshot.json"]
    F --> G["Tạo DatabaseSchemaDescriptor<br/>từ SnapshotTable/Column"]
    
    B -->|"Manual (Attributes)"| H["Tải assembly đã compile"]
    H --> I["Reflect [ExpectedColumn]<br/>[ExpectedSpParameter]"]
    I --> J["Tạo EntityDescriptor +<br/>StoredProcedureDescriptor"]
    
    E & G & J --> K["Tải cấu hình .dataguard.yml"]
    K --> L["Tạo contract descriptors<br/>(Entity + SP + RawSql)"]
    L --> M["Resolve rules cho provider"]
    M --> N["Chạy ConcurrentValidationEngine"]
    
    N --> O["Với mỗi cặp (rule, contract):<br/>Parallel.ForEachAsync"]
    O --> P["Rule.ValidateAsync()"]
    P --> Q{"Có violations?"}
    Q -->|Có| R["Thêm vào ConcurrentBag"]
    Q -->|Không| S["Tiếp tục"]
    R --> T{"Đạt giới hạn<br/>backpressure?"}
    T -->|Có| U["Dừng thêm"]
    T -->|Không| S
    S --> O
    U --> V["Sắp xếp violations theo RuleId + Message"]
    V --> W["Phát qua DiagnosticEmitter"]
    
    W --> X["Console output"]
    W --> Y["SARIF file"]
    W --> Z["Evidence artifact"]
    
    X & Y & Z --> AA{"Exit code"}
    AA -->|"0"| AB["Không có lỗi"]
    AA -->|"1"| AC["Phát hiện lỗi hoặc drift"]
    AA -->|"2"| AD["Lỗi cấu hình/cách dùng"]
```

## 3. Luồng Dữ Liệu Trích Xuất Nguồn

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
    
    subgraph Oracle ["Nguồn Oracle"]
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
    NLSR --> LS["LengthSemantics<br/>(CHAR hoặc BYTE)"]
    RCD --> SPD
    
    subgraph SqlServer ["Nguồn SQL Server"]
        SSP["sys.parameters"]
        SSC["sys.columns"]
        SCRIPT["ScriptDOM<br/>(TSqlFragmentVisitor)"]
    end
    
    SSP & SSC --> SSRS["SqlServerStoredProcedureParser"]
    SCRIPT --> RSP["RawSqlParser"]
    
    SSRS --> SPD2["StoredProcedureDescriptor"]
    RSP --> RSD["RawSqlDescriptor<br/>(SQL text + params trích xuất)"]
    
    subgraph Manual ["Nguồn Thủ Công"]
        ASM["Assembly đã compile"]
        ATTR["[ExpectedColumn]<br/>[ExpectedSpParameter]"]
    end
    
    ASM --> MCS["ManualContractSource"]
    ATTR --> MCS
    MCS --> ED2["EntityDescriptor"]
    MCS --> SPD3["StoredProcedureDescriptor"]
```

## 4. Luồng Dữ Liệu Bảo Mật

```mermaid
flowchart TD
    subgraph CredentialSources ["🔐 Nguồn Credentials"]
        ENV["Biến môi trường<br/>(CONNECTION_STRING)"]
        KV["Azure Key Vault"]
        AWS["AWS Secrets Manager"]
        VAULT["HashiCorp Vault"]
    end
    
    ENV & KV & AWS & VAULT --> ZTCP["ZeroTrustCredentialProvider"]
    ZTCP --> CH["CredentialHandle<br/>(IDisposable, giá trị xóa khi dispose)"]
    
    CH --> CM["CredentialManager"]
    CM --> ENC["Mã hóa khi lưu trữ<br/>(AES-256-GCM)"]
    CM --> ROT["Phát hiện rotation<br/>(cảnh báo 30 ngày)"]
    CM --> AUDIT["Ghi audit log"]
    
    AUDIT --> FAL["FileAuditLogger<br/>(chuỗi hash SHA256)"]
    FAL --> LOGFILE["audit-log.jsonl"]
    
    subgraph SupplyChain ["🔗 Chuỗi Cung Ứng"]
        SCV["SupplyChainVerifier"]
        HASH["Hash assembly<br/>(SHA-256)"]
        DEP["Kiểm tra dependencies<br/>(danh sách đáng tin)"]
        TAMPER["Phát hiện giả mạo"]
    end
    
    SCV --> HASH & DEP & TAMPER
    SCV --> RESULT["SupplyChainVerificationResult"]
```

## 5. Luồng Dữ Liệu Baseline & Drift

```mermaid
flowchart LR
    subgraph Create ["Tạo Baseline"]
        VAL1["Chạy validation"] --> BF["BaselineFile v2<br/>(Version, CreatedAt,<br/>SchemaVersion, GroundTruthMode,<br/>DatabaseVersion, SchemaHash,<br/>Violations, Schema)"]
        BF --> WRITE[".dataguard-baseline.json"]
    end
    
    subgraph Drift ["Phát Hiện Drift"]
        SNAP[".dataguard-snapshot.json"] --> LOAD["Tải snapshot"]
        DB["Database trực tiếp"] --> EXTRACT["Trích xuất schema hiện tại"]
        LOAD --> COMPARE["So sánh schema hash"]
        EXTRACT --> COMPARE
        COMPARE --> DRIFT{"Hash khớp?"}
        DRIFT -->|Khớp| OK["Không có drift"]
        DRIFT -->|Không khớp| REPORT["DriftReport<br/>(NewViolations, BaselineHash,<br/>CurrentHash)"]
    end
    
    subgraph Migrate ["Di Chuyển"]
        V1["BaselineFile v1"] --> MIG["BaselineManager.MigrateAsync()"]
        MIG --> V2["BaselineFile v2<br/>(+ DatabaseVersion, SchemaHash)"]
    end
```

## 6. Luồng Dữ Liệu Assessment

```mermaid
flowchart TD
    REQ["AssessmentRequest<br/>(WorkspaceRoot, ProjectFilters)"] --> INV["InventoryPack.DiscoverProjects()"]
    INV --> PROJ["Danh sách file .csproj"]
    
    PROJ --> PIR["ProjectInventoryReader.Read()<br/>(TFM, SDK-style, PackageRefs, ProjectRefs)"]
    PIR --> FACTS["ProjectFacts"]
    
    FACTS --> INVASS["InventoryPack.Assess()<br/>(Trạng thái hỗ trợ TFM qua LegacySupportTable)"]
    FACTS --> DHP["DependencyHealthPack.Assess()<br/>(Tính nhất quán lock file)"]
    
    WS["Workspace root"] --> BCI["BuildCiPack.Assess()<br/>(SDK pinning global.json,<br/>Phạm vi CI matrix)"]
    WS --> SEC["SecretsPack.AssessFile()<br/>(Giá trị giống secret trong .config/.yml)"]
    WS --> SEC2["SecretsPack.AssessMachinePaths()<br/>(Đường dẫn tuyệt đối trong config)"]
    
    INVASS & DHP & BCI & SEC & SEC2 --> FINDINGS["AssessmentFinding[]"]
    FINDINGS --> REPORT["AssessmentReport<br/>(Summary, Findings, Errors)"]
    
    REPORT --> JSON["JSON output"]
    REPORT --> SARIF2["SARIF output"]
    
    FINDINGS --> UP["UpgradePlanner.Plan()"]
    UP --> STEPS["UpgradeStep[]<br/>(sắp xếp leaf-first,<br/>blocking findings,<br/>lệnh validation)"]
```
