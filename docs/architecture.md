# Kiến Trúc DataGuard / DataGuard Architecture

## Tổng Quan / Overview

DataGuard là **Roslyn Analyzer phân phối qua NuGet** để xác thực hợp đồng (contract validation) giữa Entity ↔ Stored Procedure/Raw SQL, phục vụ các .NET developer sử dụng EF Core/Dapper với Oracle và SQL Server.

**Triết lý cốt lõi**: "Tách IDE khỏi CI" - Phân tích cú pháp nhẹ trong IDE, diff-engine nặng trong CI pipeline.

---

## Kiến Trúc Cấp Cao / High-Level Architecture

```mermaid
graph TB
    subgraph "Lớp IDE (Nhẹ - Mỗi Keystroke)"
        A1[UnvalidatedSqlCallGenerator<br/>IIncrementalGenerator]
        A2[Phát Hiện Chỉ Cú Pháp]
        A3[Cung Cấp Sửa Nhanh<br/>12 CodeFixProviders]
    end

    subgraph "Lớp CI (Nặng - Cổng PR)"
        B1[ValidationPipeline]
        B2[RuleDependencyGraph<br/>Sắp Xếp Tô-pô]
        B3[ConcurrentValidationEngine]
        B4[BaselineManager v2]
        B5[StreamingSarifSink]
    end

    subgraph "Động Cơ Cốt Lõi"
        C1[EfModelSource]
        C2[SqlServerParsers<br/>ScriptDOM]
        C3[OracleReaders<br/>Dựa Trên Catalog]
        C4[ContractRules<br/>6 Quy Tắc Built-in]
    end

    subgraph "Adapters / Bộ Điều Hợp"
        D1[SqlServer.Adapter<br/>ScriptDOM + sp_describe]
        D2[Oracle.Adapter<br/>ALL_ARGUMENTS + NLS]
    end

    subgraph "Bảo Mật"
        E1[CredentialManager<br/>DPAPI + Phát Hiện Quay Vòng]
        E2[ZeroTrustCredentialProvider<br/>KeyVault/AWS/Vault]
        E3[SupplyChainVerifier<br/>SLSA]
        E4[FileAuditLogger]
    end

    subgraph "Hạ Tầng"
        F1[BaselineManager v2<br/>SchemaHash + Phiên Bản DB]
        F2[TelemetryCollector<br/>Metrics Opt-in]
        F3[HealthChecks<br/>Liveness/Readiness/Startup]
        F4[RulePluginManager<br/>MEF-based]
        F5[AutoDetectionEngine<br/>Zero-Config]
    end

    A1 --> B1
    A2 --> B1
    B1 --> C1
    B1 --> C2
    B1 --> C3
    B1 --> C4
    C2 --> D1
    C3 --> D2
    B1 --> E1
    B1 --> E2
    B1 --> E3
    B1 --> E4
    B1 --> F1
    B1 --> F2
    B1 --> F3
    B1 --> F4
    B1 --> F5
```

---

## Đồ Thị Phụ Thuộc Module / Module Dependency Graph

```mermaid
graph LR
    Core[DataGuard.Core<br/>MIT, zero vendor deps]
    
    Core --> SqlServer[DataGuard.SqlServer.Adapter<br/>MIT + ScriptDOM]
    Core --> Oracle[DataGuard.Oracle.Adapter<br/>MIT + Oracle License]
    Core --> Analyzers[DataGuard.Analyzers<br/>MIT + Roslyn]
    Core --> CLI[DataGuard.Cli<br/>MIT + Core + Adapters]

    style Core fill:#e1f5fe
    style SqlServer fill:#f3e5f5
    style Oracle fill:#fff3e0
    style Analyzers fill:#e8f5e9
    style CLI fill:#fce4ec
```

---

## Các Thành Phần Cốt Lõi / Core Components

### 1. Incremental Generator (Lớp IDE Nhẹ)

```mermaid
graph TD
    A[SyntaxProvider.CreateSyntaxProvider] --> B[Predicate: IsPotentialSqlCall]
    B --> C[Transform: ExtractSqlCallSite]
    C --> D[Where: site != null]
    D --> E[RegisterSourceOutput]
    E --> F[ReportDiagnostic<br/>UnvalidatedSqlCall]

    style A fill:#e3f2fd
    style F fill:#ffcdd2
```

**Tối Ưu Hóa**:
- Predicate không cấp phát (static methods, ReadOnlySpan<char>)
- HashSet<string> tính trước cho tên phương thức
- SyntaxProvider đơn lẻ cho mọi loại gọi SQL
- Phát sinh trực tiếp (không tập trung trung gian)

### 2. Pipeline Xác Thực Hợp Đồng (Lớp CI Nặng)

```mermaid
graph TD
    A[ValidationPipeline.ValidateAsync] --> B[RuleDependencyGraph.GetExecutionOrder]
    B --> C[Sắp Xếp Tô-pô<br/>Thuật Toán Kahn]
    C --> D[ConcurrentValidationEngine]
    D --> E[Partitioner.Create<br/>Parallel.ForEach]
    E --> F[SemaphoreSlim<br/>MaxDegreeOfParallelism]
    F --> G[Rule.ValidateAsync]
    G --> H[ConcurrentQueue<ContractViolation>]
    H --> I[BaselineManager.FilterNewViolations]
    I --> J[DiagnosticEmitter.EmitAsync]
    J --> K[SarifSink/ConsoleSink/MarkdownSink]

    style A fill:#e3f2fd
    style K fill:#ffcdd2
```

### 3. Đồ Thị Phụ Thuộc Quy Tắc / Rule Dependency Graph

```mermaid
graph TD
    L1[ParameterCountRule]
    L2[ParameterTypeMatchRule]
    L3[ParameterDirectionRule] --> L1
    L4[ColumnShapeMatchRule] --> L1
    L5[NullableMismatchRule] --> L2
    L6[NamingConventionRule] --> L1
    L6 --> L4
    L7[LengthExceedsColumnRule] --> L2
    L7 --> L4
    L8[ByteLengthOverflowRiskRule] --> L7
    L9[InferredSizeFallbackRule] --> L2
    L10[OracleSyntaxInNonOracleRule]
    L11[NonOracleFunctionInOracleRule]
    L12[ProviderOptionMismatchRule]
    L13[SqlServerSyntaxLeakRule]
    L14[RawSqlUnmappedTypeUsageRule]

    classDef level1 fill:#e3f2fd;
    classDef level2 fill:#e8f5e9;
    classDef level3 fill:#fff3e0;
    classDef level4 fill:#fce4ec;
    classDef level5 fill:#f3e5f5;
    classDef level6 fill:#e0e0e0;
    classDef level7 fill:#fff9c4;

    class L1,L2 level1;
    class L3,L4 level2;
    class L5,L6 level3;
    class L7,L9 level4;
    class L8 level5;
    class L10,L11,L12,L13,L14 level6;
```

---

## Kiến Trúc Adapter Oracle / Oracle Adapter Architecture

```mermaid
graph TD
    A[AllArgumentsReader] --> B[ALL_ARGUMENTS<br/>SEQUENCE + OVERLOAD]
    C[AllTabColumnsReader] --> D[ALL_TAB_COLUMNS<br/>CHAR_USED B/C]
    E[NlsSessionReader] --> F[NLS_LENGTH_SEMANTICS<br/>Phiên Bản DB]
    G[RefCursorDescriber] --> H[DBMS_SQL.DESCRIBE_COLUMNS]
    I[LengthMismatchDetector] --> J[3 Loại Mismatch]
    J --> J1[Entity MaxLength > Column CharLength]
    J --> J2[Tràn Byte Semantics]
    J --> J3[Fallback NVARCHAR2(2000)<br/>#33218]

    style A fill:#fff3e0
    style C fill:#fff3e0
    style E fill:#fff3e0
    style G fill:#fff3e0
    style I fill:#ffcdd2
```

---

## Kiến Trúc Bảo Mật / Security Architecture

```mermaid
graph TD
    A[ZeroTrustCredentialProvider] --> B[Ưu Tiên 1: Env Var<br/>CI/CD Injection]
    A --> C[Ưu Tiên 2: Azure Key Vault]
    A --> D[Ưu Tiên 3: AWS Secrets Manager]
    A --> E[Ưu Tiên 4: HashiCorp Vault]
    A --> F[Ưu Tiên 5: Local Encrypted Store<br/>DPAPI]
    A --> G[Ưu Tiên 6: Config File<br/>Chỉ Dev + Cảnh Báo]

    H[CredentialManager] --> I[Phát Hiện Quay Vòng<br/>Cảnh Báo 30 Ngày]
    H --> J[Mã Hóa DPAPI<br/>Phạm Vi CurrentUser]
    H --> K[FileAuditLogger<br/>Dòng JSON]

    L[SupplyChainVerifier] --> M[Xác Minh Hash Assembly]
    L --> N[Kiểm Tra Phụ Thuộc Tin Cậy<br/>Microsoft/Nhà Cung Cấp Duyệt]
    L --> O[Phát Hiện Thay Đổi<br/>File Thay Đổi/Strong Name]

    P[FileAuditLogger] --> Q[Dòng JSON Format]
    Q --> R[Máy/Tên User/Process/Chi Tiết]

    style A fill:#ffebee
    style H fill:#ffebee
    style L fill:#ffebee
    style P fill:#ffebee
```

---

## Kiến Trúc Baseline v2 / Baseline v2 Architecture

```mermaid
graph TD
    A[BaselineManager v2] --> B[Schema Phiên Bản 2]
    B --> C[DatabaseVersion]
    B --> D[SchemaHash SHA256-64bit]
    B --> E[Mảng Violations]

    C --> E1[Cảnh Báo Mismatch Phiên Bản<br/>So Sánh Major.Minor]
    D --> E2[Phát Hiện Drift Schema<br/>So Sánh SHA256]

    A --> F[Memory-Mapped File I/O<br/>>1MB files]
    A --> G[Ghi Nguyên Tử<br/>Temp + File.Replace]

    H[Di Trừ Legacy v1] --> I[Tự Di Trừ Khi Load]
    I --> J[Tính Hash Từ v1]
    J --> K[Nâng Cấp Lên v2]

    style A fill:#e8f5e9
    style B fill:#e8f5e9
    style F fill:#fff3e0
```

---

## Kiến Trúc SARIF Streaming / Streaming SARIF Architecture

```mermaid
graph TD
    A[DiagnosticEmitter.EmitAsync] --> B{Streaming?}
    B -->|Có| C[StreamingSarifSink<br/>Utf8JsonWriter]
    B -->|Không| D[FileSarifSink<br/>ToJson + WriteAllText]

    C --> E[Utf8JsonWriter<br/>Ghi Streaming]
    E --> F[WriteStartObject]
    E --> G[WritePropertyName: runs]
    E --> H[WriteStartArray]
    H --> I[Với Mỗi Run]
    I --> J[Ghi tool + driver]
    I --> K[Ghi mảng rules]
    I --> L[Ghi mảng results<br/>Streaming mỗi violation]
    L --> M[WriteEndArray]
    M --> N[WriteEndObject]
    N --> O[FlushAsync]

    style C fill:#e8f5e9
    style D fill:#fff3e0
    style O fill:#c8e6c9
```

---

## Luồng Tự Động Phát Hiện / Auto-Detection Engine Flow

```mermaid
graph TD
    A[AutoDetectionEngine.DetectAsync] --> B[DetectProviderFromConfigAsync]
    B --> C[appsettings.json]
    B --> D[appsettings.Development.json]
    B --> E[.dataguard.yml]
    B --> F[DATAGUARD_PROVIDER env]

    A --> G[DetectEfCoreAsync]
    G --> G1[Quét *.csproj gói EF Core]
    G --> G2[Quét *.cs cho DbContext]

    A --> H[DetectDapperAsync]
    H --> H1[Quét *.csproj gói Dapper]
    H --> H2[Quét *.cs cho Query/Execute]

    A --> I[DetectConnectionStringAsync]
    I --> I1[Env: DATAGUARD_CONNECTION_STRING]
    I --> I2[appsettings.json ConnectionStrings]
    I --> I3[.dataguard.yml]

    A --> J[DetectNamingConventionAsync]
    J --> J1[Parse *.cs PropertyDeclarationSyntax]
    J --> J2[Đếm snake_case vs PascalCase]

    A --> K[DetectEfCoreContextAsync]
    K --> K1[Tìm DbContext class trong *.cs]

    style A fill:#e3f2fd
```

---

## Luồng Wizard Tương Tác / Interactive Wizard Flow

```mermaid
sequenceDiagram
    participant User
    participant Wizard as InteractiveConfigBuilder
    participant AutoDetect as AutoDetectionEngine
    participant Config as DataGuardConfiguration
    participant File as .dataguard.yml

    User->>Wizard: dotnet dataguard init --wizard
    Wizard->>User: 🔧 DataGuard Interactive Setup Wizard
    Wizard->>AutoDetect: DetectProviderInteractiveAsync
    AutoDetect->>Wizard: Detected: SQL Server
    Wizard->>User: Detected: SQL Server
    Wizard->>User: 🔗 Enter connection string
    User->>Wizard: Server=...;Database=...;
    Wizard->>AutoDetect: DetectEfCoreAsync + DetectDapperAsync
    AutoDetect->>Wizard: EF Core: ✅, Dapper: ❌
    Wizard->>User: EF Core: ✅, Dapper: ❌
    Wizard->>User: 📝 Naming convention choice
    User->>Wizard: 1 (snake_case ↔ PascalCase)
    Wizard->>User: 📋 Baseline mode choice
    User->>Wizard: 1 (Snapshot - recommended)
    Wizard->>Config: Generate config
    Config->>File: Save .dataguard.yml
    Wizard->>User: ✅ Saved to .dataguard.yml
    Wizard->>User: Next: dataguard baseline → dataguard validate
```

---

## Cài Đặt Pre-Commit Hook

```mermaid
graph TD
    A[PreCommitHookInstaller.InstallAsync] --> B[FindGitRoot]
    B --> C{DetectHookType}
    C -->|Husky| D[.husky/pre-commit]
    C -->|Lefthook| E[lefthook.yml]
    C -->|Native| F[.git/hooks/pre-commit]

    D --> F[GenerateHuskyHook]
    E --> G[GenerateLefthookConfig]
    F --> H[GenerateNativeGitHook]

    F --> I[Ghi vào .git/hooks/pre-commit]
    D --> J[Ghi vào .husky/pre-commit]
    E --> K[Ghi lefthook.yml]

    style F fill:#e8f5e9
    style D fill:#fff3e0
    style E fill:#fff3e0
```

**Nội Dung Hook Sinh Ra**:
```bash
#!/bin/sh
# DataGuard pre-commit hook
echo "🔍 Running DataGuard pre-commit validation..."

if command -v dataguard &> /dev/null; then
    dataguard validate --offline --format text
    exit_code=$?
    
    if [ $exit_code -ne 0 ]; then
        echo "❌ DataGuard validation failed. Fix issues before committing."
        exit 1
    fi
    echo "✅ DataGuard validation passed."
else
    echo "⚠ DataGuard CLI not found. Skipping validation."
fi
exit 0
```

---

## Endpoint Health Check

```mermaid
graph TD
    A[DataGuardHealthCheck] --> B[/health/live<br/>Liveness]
    A --> C[/health/ready<br/>Readiness]
    A --> D[/health/startup<br/>Startup]

    B --> B1[Process Alive<br/>Uptime > 0]
    C --> C1[Credentials Available]
    C --> C2[Baseline Loadable]
    C --> C3[Supply Chain OK]
    C --> C4[Disk Space > 10%]
    C --> C5[Memory < 1GB]
    D --> D1[Startup > 30s or Ready]

    style A fill:#e3f2fd
```

---

## Kiến Trúc Plugin / Plugin Architecture

```mermaid
graph TD
    A[RulePluginManager] --> B[ContainerConfiguration]
    B --> C[WithAssembliesInDirectory]
    C --> D[MEF Container]
    D --> E[GetExports<IContractRule, IRuleMetadata>]
    E --> F[Lọc Compatible]
    F --> G[Merge với BuiltInRules]

    H[ExportRuleAttribute] --> I[RuleId, Name, Description]
    H --> I2[Category, Severity, MinVersion]
    H --> I3[Author, Tags]

    I --> J[CustomNamingConventionRule<br/>Example Plugin]

    style A fill:#f3e5f5
    style D fill:#f3e5f5
    style J fill:#e8f5e9
```

---

## Tóm Tắt Luồng Dữ Liệu / Data Flow Summary

```mermaid
graph LR
    subgraph "Nguồn Input"
        S1[EF Core Model<br/>IModel / ModelSnapshot]
        S2[SQL Server SP<br/>sys.parameters + sp_describe]
        S3[Oracle SP<br/>ALL_ARGUMENTS + NLS]
        S4[Raw SQL<br/>ScriptDOM / Catalog]
    end

    subgraph "Xử Lý Cốt Lõi"
        P1[ContractDescriptors]
        P2[RuleDependencyGraph<br/>Thứ Tự Tô-pô]
        P3[ConcurrentValidationEngine<br/>Parallel Rules]
        P4[BaselineManager v2<br/>Lọc Violation Mới]
    end

    subgraph "Sink Đầu Ra"
        O1[SARIF 2.1<br/>GitHub/Azure DevOps]
        O2[Console/Text<br/>Đọc Được Bởi Người]
        O3[Markdown/JSON<br/>Reports]
        O4[Roslyn Diagnostics<br/>IDE/Build]
    end

    S1 --> P1
    S2 --> P1
    S3 --> P1
    S4 --> P1
    P1 --> P2
    P2 --> P3
    P3 --> P4
    P4 --> O1
    P4 --> O2
    P4 --> O3
    P4 --> O4
```

---

## Ngăn Xếp Công Nghệ / Technology Stack

| Layer / Lớp | Technology / Công Nghệ | Version / Phiên Bản |
|-------|------------|---------|
| Runtime | .NET | 9.0 |
| Analyzers | Roslyn | 4.11.0 |
| SQL Server Parser | ScriptDOM | 170.3.0 |
| Oracle Driver | Oracle.ManagedDataAccess.Core | 23.6.0 |
| SQL Client | Microsoft.Data.SqlClient | 5.2.0 |
| EF Core | Microsoft.EntityFrameworkCore | 8.0.0 |
| JSON | System.Text.Json | 8.0.5 |
| MEF | System.Composition.Hosting | 8.0.0 |
| Telemetry | System.Diagnostics.Metrics | Built-in |
| Health Checks | Microsoft.Extensions.Diagnostics.HealthChecks | Built-in |
| Logging | Microsoft.Extensions.Logging | Built-in |

---

## Mục Triển Khai / Deployment Targets

| Target / Mục Tiêu | Method / Phương Pháp | Artifacts / Sản Phẩm |
|--------|-----------|---------|
| NuGet.org | `dotnet nuget push` | 5 packages (Core, SqlServer, Oracle, Analyzers, Cli) |
| Docker | `docker build` | `ghcr.io/org/dataguard:tag` |
| dotnet tool | `dotnet tool install -g` | DataGuard.Cli nupkg |
| GitHub Actions | CI/CD Pipeline | Signed packages + SBOM + Attestations |

---

## Ma Trận License / License Matrix

| Package / Gói | License / Giấy Phép | Vendor Deps / Phụ Thuộc Vendor |
|---------|---------|-------------|
| DataGuard.Core | MIT | None |
| DataGuard.SqlServer.Adapter | MIT | ScriptDOM (MIT) |
| DataGuard.Oracle.Adapter | MIT + Oracle License | Oracle.ManagedDataAccess.Core |
| DataGuard.Analyzers | MIT | Roslyn (MIT) |
| DataGuard.Cli | MIT | Core + Adapters |

---

## Chiến Lược Versioning / Versioning Strategy

```
MAJOR.MINOR.PATCH
│    │    └─ Sửa lỗi, không thay đổi API
│    └───── Tính năng mới, tương thích ngược
└────────── Thay đổi phá vỡ (breaking changes)
```

Hiện Tại: **1.0.0** (Phiên bản ổn định đầu tiên)

---

*Được tạo từ mã nguồn DataGuard. Cập nhật lần cuối: 2025-01-19*