# Sơ Đồ Tuần Tự

## 1. Luồng Validation Đầy Đủ (CLI → Database → Output)

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
    ZTCP->>ZTCP: Resolve từ env/vault
    ZTCP-->>CLI: CredentialHandle
    
    CLI->>Adapter: ExtractContractsAsync()
    Adapter->>DB: Query ALL_ARGUMENTS / sys.parameters
    DB-->>Adapter: Metadata tham số
    Adapter->>DB: Query ALL_TAB_COLUMNS / sys.columns
    DB-->>Adapter: Metadata cột
    Adapter-->>CLI: ContractDescriptor[]
    
    CLI->>Sources: EfModelSource.ExtractContractsAsync()
    Sources-->>CLI: EntityDescriptor[]
    
    CLI->>Rules: GetRulesForProvider("oracle")
    Rules-->>CLI: IContractRule[]
    
    CLI->>Engine: ValidateAsync(contracts, rules)
    
    loop Với mỗi (rule, contract) song song
        Engine->>Rules: rule.ValidateAsync(contract, allContracts)
        Rules-->>Engine: ContractViolation[]
    end
    
    Engine-->>CLI: ContractViolation[] (đã sắp xếp)
    
    CLI->>Emitter: EmitAsync(violations)
    Emitter->>Output: Ghi SARIF / Console / Evidence
    Output-->>User: Kết quả validation
    
    CLI-->>User: Exit code (0/1/2)
```

## 2. Luồng Phân Tích IDE (VS Code → Analyzer → Quick Fix)

```mermaid
sequenceDiagram
    actor Developer as Lập trình viên
    participant VSCode as VS Code
    participant Ext as DataGuard Extension
    participant CLI as dataguard CLI
    participant Analyzer as Roslyn Analyzer
    participant CodeFix as Code Fix Provider
    participant Diag as DiagnosticCollection

    Developer->>VSCode: Mở file .cs
    VSCode->>Analyzer: Incremental generator kích hoạt
    Analyzer->>Analyzer: Quét syntax tree tìm SQL calls
    Analyzer-->>VSCode: Diagnostic DG001 (gạch chân)
    
    Developer->>VSCode: Hover vào gạch chân
    VSCode-->>Developer: "Số lượng tham số không khớp: kỳ vọng 3, nhận 2"
    
    Developer->>VSCode: Ctrl+. (Quick Fix)
    VSCode->>CodeFix: RegisterCodeFixesAsync()
    CodeFix-->>VSCode: "Thêm tham số thiếu" / "Thêm [SkipContractCheck]"
    Developer->>VSCode: Chọn fix
    CodeFix->>VSCode: Áp dụng thay đổi code
    
    Developer->>VSCode: Chạy validation đầy đủ (Ctrl+Shift+P → DataGuard)
    VSCode->>Ext: runValidation()
    Ext->>CLI: spawn("dataguard", ["validate", "--format", "sarif"])
    CLI-->>Ext: SARIF output
    Ext->>Diag: Tải SARIF diagnostics
    Diag-->>VSCode: Hiển thị trong Problems panel
    VSCode-->>Developer: Tất cả violations được liệt kê
```

## 3. Luồng CI Pipeline

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
    Git->>GH: Kích hoạt CI workflow
    
    par Build và Test
        GH->>Build: dotnet restore --locked
        Build->>Build: dotnet build -c Release
        Build->>Build: dotnet format --verify-no-changes
        Build->>Build: dotnet test (với coverage)
        Build->>Build: Coverage gate (≥60%)
    and Quét bảo mật
        GH->>Security: Kiểm tra lỗ hổng NuGet
        GH->>Security: TruffleHog quét secret
    and SBOM
        GH->>SBOM: Tạo SBOM (Microsoft.Sbom.DotNetTool)
    and CodeQL
        GH->>CodeQL: Phân tích C# với custom queries
    end
    
    Build-->>GH: ✅ Pass / ❌ Fail
    Security-->>GH: ✅ Sạch / ❌ Tìm thấy lỗ hổng
    SBOM-->>GH: ✅ SBOM đã tạo
    CodeQL-->>GH: ✅ Không có vấn đề
    
    GH->>Docker: Build Docker image
    Docker->>Docker: Smoke test (--help)
    Docker-->>GH: ✅ Image hoạt động
    
    GH-->>Dev: Trạng thái CI (xanh/đỏ)
```

## 4. Tuần Tự Vòng Đời Baseline

```mermaid
sequenceDiagram
    actor User
    participant CLI as DataGuard.Cli
    participant BM as BaselineManager
    participant Engine as Validation Engine
    participant DB as Database
    participant FS as File System

    User->>CLI: dataguard baseline
    CLI->>Engine: Chạy validation đầy đủ
    Engine->>DB: Trích xuất tất cả contracts
    DB-->>Engine: ContractDescriptor[]
    Engine->>Engine: Validate tất cả rules
    Engine-->>CLI: ContractViolation[]
    
    CLI->>BM: CreateBaselineAsync(violations)
    BM->>DB: GetDatabaseVersionAsync()
    DB-->>BM: chuỗi version
    BM->>BM: ComputeSchemaHash(violations)
    BM->>BM: Tạo BaselineFile v2
    BM->>FS: Ghi .dataguard-baseline.json
    FS-->>BM: Đã ghi
    BM-->>CLI: BaselineInfo
    CLI-->>User: "Baseline đã tạo: 15 violations được đóng băng"
    
    Note over User,FS: Sau đó: dataguard validate (có baseline)
    User->>CLI: dataguard validate
    CLI->>BM: LoadBaselineAsync()
    BM->>FS: Đọc .dataguard-baseline.json
    FS-->>BM: BaselineFile
    CLI->>Engine: Validate
    Engine-->>CLI: Violations hiện tại
    CLI->>BM: Diff(current, baseline)
    BM-->>CLI: Chỉ violations mới
    CLI-->>User: "3 violations mới (15 đã baseline)"
```

## 5. Tuần Tự Phát Hiện Drift Snapshot

```mermaid
sequenceDiagram
    actor User
    participant CLI as DataGuard.Cli
    participant BM as BaselineManager
    participant Adapter as DB Adapter
    participant DB as Database
    participant FS as File System

    User->>CLI: dataguard snapshot diff --fail-on-drift
    CLI->>FS: Tải .dataguard-snapshot.json
    FS-->>CLI: SnapshotTable[]
    
    CLI->>Adapter: Trích xuất schema hiện tại
    Adapter->>DB: Query ALL_TAB_COLUMNS / sys.columns
    DB-->>Adapter: Cột hiện tại
    Adapter-->>CLI: Schema hiện tại
    
    CLI->>BM: So sánh(snapshot, current)
    BM->>BM: So sánh hash
    
    alt Phát hiện drift
        BM-->>CLI: DriftReport (HasDrift=true)
        CLI-->>User: "DRIFT: CUSTOMERS.EMAIL đổi VARCHAR2(100)→VARCHAR2(255)"
        CLI-->>User: Exit code 1
    else Không drift
        BM-->>CLI: DriftReport (HasDrift=false)
        CLI-->>User: "Không phát hiện drift schema"
        CLI-->>User: Exit code 0
    end
```

## 6. Tuần Tự Tải Plugin

```mermaid
sequenceDiagram
    participant PM as RulePluginManager
    participant MEF as MEF Container
    participant ASM as Plugin Assembly
    participant ALD as AssemblyLoadContext
    participant Rule as Custom Rule

    PM->>ALD: Tạo context cách ly
    ALD->>ASM: Tải plugin assembly
    ASM-->>MEF: ExportRuleAttribute types
    MEF->>MEF: Phát hiện [ExportRule] exports
    MEF-->>PM: Lazy<IContractRule, RulePluginMetadata>[]
    
    PM->>PM: Xác thực metadata (MinDataGuardVersion)
    PM->>PM: Kiểm tra tags và categories
    
    Note over PM,Rule: Khi có yêu cầu validation
    PM->>Rule: Tạo instance
    Rule->>Rule: ValidateCoreAsync()
    Rule-->>PM: ContractViolation[]
    
    PM->>ALD: Unload context (dọn dẹp)
```

## 7. Tuần Tự Resolve Credentials

```mermaid
sequenceDiagram
    participant CLI as DataGuard.Cli
    participant ZTCP as ZeroTrustCredentialProvider
    participant ENV as Biến môi trường
    participant KV as Azure Key Vault
    participant AWS as AWS Secrets Manager
    participant VAULT as HashiCorp Vault
    participant CM as CredentialManager
    participant AUDIT as FileAuditLogger

    CLI->>ZTCP: GetDatabaseConnectionAsync()
    
    alt CONNECTION_STRING env var
        ZTCP->>ENV: Đọc CONNECTION_STRING
        ENV-->>ZTCP: Connection string
    else KeyVaultUri đã cấu hình
        ZTCP->>KV: GetSecret("connection-string")
        KV-->>ZTCP: Giá trị secret
    else AwsRegion đã cấu hình
        ZTCP->>AWS: GetSecretValue("dataguard/connection")
        AWS-->>ZTCP: Giá trị secret
    else VaultAddress đã cấu hình
        ZTCP->>VAULT: Read("secret/data/dataguard")
        VAULT-->>ZTCP: Giá trị secret
    end
    
    ZTCP->>ZTCP: Tạo CredentialHandle
    ZTCP-->>CLI: CredentialHandle (giá trị không bao giờ log)
    
    CLI->>CM: StoreCredential(handle)
    CM->>CM: Mã hóa khi lưu (AES-256-GCM)
    CM->>CM: Kiểm tra rotation (cảnh báo 30 ngày)
    CM->>AUDIT: LogCredentialAccessAsync()
    AUDIT->>AUDIT: Thêm vào chuỗi hash
```
