# Kiến Trúc Hệ Thống

> **DataGuard** — Công cụ .NET 9 validate contract giữa Entity ↔ Stored Procedure / Raw SQL.

Tài liệu này mô tả kiến trúc hệ thống đầy đủ của DataGuard, bao gồm topology thành phần, thiết kế tầng, luồng dữ liệu, kiến trúc bảo mật và pipeline CI/CD.

---

## 1. Topology Thành Phẩm Cao Cấp

DataGuard bao gồm **11 dự án nguồn** được tổ chức theo kiến trúc tầng nghiêm ngặt. Tầng Contracts nhắm `netstandard2.0` để tương thích IDE tối đa; engine Core và adapters nhắm `net9.0`; phần mở rộng IDE nhắm framework đặc thù của từng host.

```mermaid
graph TB
    subgraph "Phần Mở Rộng IDE"
        VS["DataGuard.VisualStudio<br/><i>net472 · VS 2022</i>"]
        VSC["DataGuard.VSCode<br/><i>TypeScript · VS Code</i>"]
    end

    subgraph "Tầng Công Cụ"
        CLI["DataGuard.Cli<br/><i>net9.0 · System.CommandLine</i>"]
        CF["DataGuard.CodeFixes<br/><i>netstandard2.0 · Roslyn</i>"]
        AN["DataGuard.Analyzers<br/><i>netstandard2.0 · Roslyn</i>"]
    end

    subgraph "Tầng Adapter"
        OA["DataGuard.Oracle.Adapter<br/><i>net9.0 · ODP.NET</i>"]
        SA["DataGuard.SqlServer.Adapter<br/><i>net9.0 · SqlClient</i>"]
        MA["DataGuard.MySql.Adapter<br/><i>net9.0 · MySqlConnector</i>"]
        PA["DataGuard.PostgreSql.Adapter<br/><i>net9.0 · Npgsql</i>"]
    end

    subgraph "Engine Core"
        CORE["DataGuard.Core<br/><i>net9.0 · Không phụ thuộc vendor</i>"]
    end

    subgraph "Tầng Contracts"
        CT["DataGuard.Contracts<br/><i>netstandard2.0 · Attributes</i>"]
    end

    CLI --> CORE
    CLI --> OA
    CLI --> SA
    CLI --> MA
    CLI --> PA
    CLI --> AN

    AN --> CT
    CF --> AN

    CORE --> CT

    OA --> CORE
    SA --> CORE
    MA --> CORE
    PA --> CORE

    VS -.->|VSIX| AN
    VSC -.->|npm| AN

    style CT fill:#e1f5fe,stroke:#0288d1
    style CORE fill:#fff3e0,stroke:#f57c00
    style CLI fill:#e8f5e9,stroke:#388e3c
    style AN fill:#fce4ec,stroke:#c62828
    style CF fill:#fce4ec,stroke:#c62828
```

---

## 2. Kiến Trúc Tầng

DataGuard tuân theo **mô hình phụ thuộc đi lên từ dưới**. Mỗi tầng chỉ phụ thuộc vào các tầng bên dưới — không bao giờ phụ thuộc ngang hoặc đi lên.

```mermaid
graph TB
    subgraph "Tầng 5 — IDE Hosts"
        L5["Visual Studio 2022 · VS Code"]
    end

    subgraph "Tầng 4 — Công Cụ"
        L4A["CLI (dataguard)"]
        L4B["Analyzers + CodeFixes"]
    end

    subgraph "Tầng 3 — Adapter Cơ Sở Dữ Liệu"
        L3A["Oracle (ODP.NET)"]
        L3B["SQL Server (SqlClient)"]
        L3C["MySQL (MySqlConnector)"]
        L3D["PostgreSQL (Npgsql)"]
    end

    subgraph "Tầng 2 — Engine Core"
        L2["DataGuard.Core<br/>Rules · Sources · Security · Baseline<br/>Reporting · Validation · Plugins<br/>Telemetry · Assessment · PublicApi"]
    end

    subgraph "Tầng 1 — Contracts"
        L1["DataGuard.Contracts<br/>Attributes · NameConventions<br/><i>netstandard2.0</i>"]
    end

    L5 --> L4B
    L4A --> L2
    L4A --> L3A & L3B & L3C & L3D
    L4B --> L1
    L3A & L3B & L3C & L3D --> L2
    L2 --> L1

    style L1 fill:#e1f5fe,stroke:#0288d1
    style L2 fill:#fff3e0,stroke:#f57c00
    style L3A fill:#f3e5f5,stroke:#7b1fa2
    style L3B fill:#f3e5f5,stroke:#7b1fa2
    style L3C fill:#f3e5f5,stroke:#7b1fa2
    style L3D fill:#f3e5f5,stroke:#7b1fa2
    style L4A fill:#e8f5e9,stroke:#388e3c
    style L4B fill:#fce4ec,stroke:#c62828
    style L5 fill:#fffde7,stroke:#f9a825
```

### Trách Nhiệm Tầng

| Tầng | Target | Trách Nhiệm |
|-------|--------|-------------|
| **L1 — Contracts** | `netstandard2.0` | Attributes chia sẻ (`SkipContractCheck`, `ExpectedColumn`, `ExpectedSpParameter`), quy ước đặt tên (`snake_case` ↔ `PascalCase`). Không phụ thuộc runtime. |
| **L2 — Core Engine** | `net9.0` | Domain model (`Contracts.cs`), rules engine (DG001–DG016), nguồn contract (EF Core, SQL parsers), bảo mật zero-trust, quản lý baseline, báo cáo SARIF, validation đồng thời, hệ thống plugin MEF, telemetry, engine đánh giá, API công khai. |
| **L3 — Adapters** | `net9.0` | Readers đặc thù database. Oracle đọc `ALL_ARGUMENTS`/`ALL_TAB_COLUMNS`. SQL Server dùng `ScriptDom` + `SqlConnection`. MySQL/PostgreSQL dùng information_schema. Mỗi adapter triển khai `IContractSource`. |
| **L4 — Công Cụ** | mixed | CLI (`System.CommandLine`), Roslyn analyzers (tầng IDE nhẹ + tầng CI nặng), code fix providers. |
| **L5 — IDE Hosts** | mixed | VS 2022 extension (VSIX, `net472`), VS Code extension (TypeScript, npm). |

---

## 3. Luồng Dữ Liệu: Trích Xuất Nguồn → Validate Rule → Báo Cáo

Pipeline validation theo luồng dữ liệu tuyến tính với thực thi rule đồng thời.

```mermaid
flowchart LR
    subgraph "1. Trích Xuất Nguồn"
        A1["EF Core Model<br/>(EfModelSource)"]
        A2["SQL Server SP<br/>(SqlServerStoredProcedureParser)"]
        A3["Raw SQL<br/>(RawSqlParser)"]
        A4["Oracle ALL_ARGUMENTS<br/>(AllArgumentsReader)"]
        A5["MySQL/PG<br/>(information_schema)"]
        A6["Attributes Thủ Công<br/>(ExpectedColumn, ExpectedSpParameter)"]
    end

    subgraph "2. Tập Hợp Contract"
        B["ContractDescriptor[]<br/>EntityDescriptor · StoredProcedureDescriptor<br/>RawSqlDescriptor · DatabaseSchemaDescriptor"]
    end

    subgraph "3. Validate Rule"
        C1["ParameterCountRule<br/>DG001"]
        C2["ParameterTypeMatchRule<br/>DG002"]
        C3["ColumnShapeMatchRule<br/>DG003"]
        C4["NullableMismatchRule<br/>DG004"]
        C5["NamingConventionRule<br/>DG005"]
        C6["PhantomIdentifierRule<br/>DG015/DG016"]
        C7["LengthMismatchRule<br/>DG006"]
        C8["DialectCheckRule<br/>DG007"]
        CN["... DG008–DG014"]
    end

    subgraph "4. Thu Thập Vi Phạm"
        D["ConcurrentValidationEngine<br/>Song song có giới hạn · Áp lực ngược"]
    end

    subgraph "5. Báo Cáo"
        E1["SARIF 2.1.0<br/>(DiagnosticEmitter)"]
        E2["Console<br/>(ConsoleDiagnosticSink)"]
        E3["Bằng Chứng<br/>(ContractEvidenceWriter)"]
        E4["Export Contract<br/>(ContractExportWriter)"]
        E5["So Sánh Baseline<br/>(BaselineManager)"]
    end

    A1 & A2 & A3 & A4 & A5 & A6 --> B
    B --> C1 & C2 & C3 & C4 & C5 & C6 & C7 & C8 & CN
    C1 & C2 & C3 & C4 & C5 & C6 & C7 & C8 & CN --> D
    D --> E1 & E2 & E3 & E4 & E5
```

### Các Giai Đoạn Pipeline

1. **Trích Xuất Nguồn** — Mỗi triển khai `IContractSource` kết nối với nguồn dữ liệu (database, model EF, attributes code) và tạo ra các bản ghi `ContractDescriptor`. Các nguồn chạy độc lập và có thể song song hóa.

2. **Tập Hợp Contract** — Các descriptor được thu thập vào `IReadOnlyList<ContractDescriptor>` thống nhất. Bao gồm hình dạng entity, tham số stored procedure, cột result set và schema database ground truth.

3. **Validate Rule** — `ConcurrentValidationEngine` chạy tất cả các triển khai `IContractRule` đã đăng ký trên tất cả contracts. Rules được thực thi với song song có giới hạn (`MaxDegreeOfParallelism`) và áp lực ngược (tối đa 100K vi phạm).

4. **Thu Thập Vi Phạm** — Vi phạm được thu thập trong `ConcurrentBag<ContractViolation>`, loại bỏ trùng lặp và sắp xếp theo `RuleId` rồi `Message`.

5. **Báo Cáo** — `DiagnosticEmitter` phân phối vi phạm đến nhiều đích: file SARIF, console, artifacts bằng chứng, export contract và so sánh baseline.

---

## 4. Kiến Trúc Bảo Mật: Chuỗi Credential Zero-Trust

DataGuard triển khai mô hình bảo mật **zero-trust**. Credentials không bao giờ được ghi log, không bao giờ được serialize dạng plain text và được mã hóa khi lưu trữ khi được cấu hình.

```mermaid
flowchart TB
    subgraph "Nguồn Credentials"
        S1["Biến Môi Trường<br/>(DATAGUARD_CONNECTION_STRING)"]
        S2["AWS Secrets Manager<br/>(AWSSDK.SecretsManager)"]
        S3["Azure Key Vault<br/>(KeyVaultUri)"]
        S4["HashiCorp Vault<br/>(VaultAddress)"]
        S5["File Config Mã Hóa<br/>(EncryptConnectionStringAtRest)"]
    end

    subgraph "Tầng Zero-Trust"
        ZTP["ZeroTrustCredentialProvider<br/><i>Không bao giờ ghi log secrets</i>"]
        CM["CredentialManager<br/><i>Phát hiện rotation · Mã hóa DPAPI</i>"]
        CH["CredentialHandle<br/><i>Wrapper IDisposable an toàn</i>"]
    end

    subgraph "Tầng Audit"
        AL["IAuditLogger<br/>FileAuditLogger · NullAuditLogger"]
        AE["AuditEntry<br/><i>Hash chuỗi · Chống giả mạo</i>"]
    end

    subgraph "Chính Sách"
        FP["Chính Sách Đóng<br/><i>AllowPlaintextConfigFallback = false</i>"]
        RD["Phát Hiện Rotation<br/><i>CredentialRotationWarningDays</i>"]
    end

    S1 & S2 & S3 & S4 & S5 --> ZTP
    ZTP --> CM
    CM --> CH
    CH -->|"Sử dụng & giải phóng"| APP["Mã Ứng Dụng"]
    CM --> AL
    AL --> AE
    FP --> ZTP
    RD --> CM

    style ZTP fill:#ffcdd2,stroke:#c62828
    style CM fill:#ffcdd2,stroke:#c62828
    style CH fill:#ffcdd2,stroke:#c62828
    style AL fill:#fff9c4,stroke:#f9a825
    style AE fill:#fff9c4,stroke:#f9a825
```

### Nguyên Tắc Bảo Mật

| Nguyên Tắc | Triển Khai |
|------------|-----------|
| **Không bao giờ ghi log secrets** | `ZeroTrustCredentialProvider` loại bỏ credentials khỏi tất cả output log. `CredentialHandle` xóa giá trị khi `Dispose()`. |
| **Mã hóa khi lưu trữ** | `CredentialManager` dùng `System.Security.Cryptography.ProtectedData` (DPAPI) để mã hóa local. AWS/Azure/Vault cung cấp mã hóa cloud-native. |
| **Phát hiện rotation** | `CredentialRotationWarningDays` kích hoạt cảnh báo khi credentials quá hạn. |
| **Đóng khi lỗi** | `AllowPlaintextConfigFallback = false` (mặc định) ngăn chặn hạ cấp credential im lặng sang file config plain. |
| **Nhật ký audit** | `FileAuditLogger` ghi các bản ghi `AuditEntry` hash chuỗi. Mỗi mục bao gồm `PreviousHash` để phát hiện giả mạo. |
| **Redact output** | `ContractEvidenceWriter.Redact()` loại bỏ `password=`, `token=`, `Authorization: Bearer` khỏi tất cả artifacts bằng chứng. |

---

## 5. Kiến Trúc Pipeline CI/CD

DataGuard sử dụng GitHub Actions với pipeline **phòng thủ nhiều lớp**: build → test → security scan → CodeQL → ký → publish.

```mermaid
flowchart LR
    subgraph "Pipeline CI (ci.yml)"
        CI1["Build & Test<br/>dotnet build · dotnet test<br/>Coverage ≥ 60%"]
        CI2["Quét Bảo Mật<br/>Lỗ hổng NuGet<br/>TruffleHog secrets"]
        CI3["Phân Tích CodeQL<br/>C# SAST"]
        CI4["Smoke Docker<br/>Build + --help"]
        CI5["Tạo SBOM<br/>Microsoft.Sbom.DotNetTool"]
    end

    subgraph "Pipeline Phát Hành (release.yml)"
        R1["Build & Test<br/>Đóng gói theo tag"]
        R2["Quét Bảo Mật<br/>Vuln + TruffleHog"]
        R3["Phân Tích CodeQL"]
        R4["Ký Sigstore<br/>cosign sign-blob<br/>Keyless OIDC"]
        R5["Publish NuGet<br/>dotnet nuget push"]
        R6["GitHub Release<br/>Artifacts đã ký"]
        R7["Docker Đa Kiến Trúc<br/>linux/amd64 + linux/arm64"]
        R8["Tạo SBOM"]
    end

    CI1 --> CI2 --> CI3 --> CI4
    CI1 --> CI5

    R1 --> R2 --> R3 --> R4 --> R5 --> R6
    R1 --> R7
    R1 --> R8

    style CI1 fill:#e8f5e9,stroke:#388e3c
    style R4 fill:#ffcdd2,stroke:#c62828
    style R5 fill:#e1f5fe,stroke:#0288d1
```

### Các Giai Đoạn Pipeline

| Giai Đoạn | Công Cụ | Mục Đích |
|-----------|---------|---------|
| **Build** | `dotnet build --configuration Release` | Biên dịch tất cả 11 dự án với `TreatWarningsAsErrors` |
| **Test** | `dotnet test` + XPlat Code Coverage | 291+ tests, cổng coverage 60% |
| **Format Gate** | `dotnet format --verify-no-changes` | Ép buộc kiểu code nhất quán |
| **Quét Lỗ Hổng** | `dotnet list package --vulnerable` | Thất bại trên bất kỳ gói NuGet có lỗ hổng |
| **Quét Secret** | TruffleHog v3.97.0 | Chỉ secrets đã xác minh, loại trừ artifacts build |
| **SAST** | CodeQL v4.37.7 | Phân tích bảo mật C# |
| **Ký** | Sigstore cosign v3.1.3 | Ký keyless OIDC với output bundle |
| **SBOM** | Microsoft.Sbom.DotNetTool v4.1.5 | SBOM CycloneDX cho minh bạch chuỗi cung ứng |
| **Docker** | Build đa kiến trúc | `linux/amd64` + `linux/arm64` qua BuildKit |

---

## 6. Kiến Trúc Module Nội Bộ

Engine Core được tổ chức thành **12 module nội bộ**, mỗi module có một trách nhiệm duy nhất.

```mermaid
graph TB
    subgraph "DataGuard.Core"
        subgraph "Domain Model"
            ABS["Abstractions<br/>IContractSource · IContractRule<br/>ContractDescriptor · ContractViolation"]
        end

        subgraph "Rules Engine"
            RULES["Rules<br/>ContractRuleBase · DG001–DG016<br/>PhantomIdentifierRule"]
            RDG["RuleDependencyGraph<br/>Sắp xếp topo · Deps có sẵn"]
        end

        subgraph "Sources"
            EF["EfModelSource<br/>Runtime IModel · Design-time Snapshot"]
            SP["SqlServerParsers<br/>ScriptDom · SqlParameterVisitor"]
            MANUAL["ManualContractSource<br/>Ground truth dựa trên attribute"]
        end

        subgraph "Security"
            ZTP["ZeroTrustCredentialProvider"]
            CM["CredentialManager"]
            AUDIT["IAuditLogger · FileAuditLogger"]
        end

        subgraph "Baseline"
            BM["BaselineManager<br/>Snapshot · Phát hiện drift<br/>Hash schema"]
        end

        subgraph "Báo Cáo"
            DE["DiagnosticEmitter<br/>SARIF · Console · File"]
            CE["ContractEvidenceWriter<br/>JSON đã redact"]
            CX["ContractExportWriter<br/>Tạo TypeScript DTO"]
        end

        subgraph "Validation"
            CVE["ConcurrentValidationEngine<br/>Song song có giới hạn"]
        end

        subgraph "Plugins"
            RPM["RulePluginManager<br/>Khám phá MEF 2"]
        end

        subgraph "Telemetry"
            TC["TelemetryCollector<br/>Tùy chọn · Chỉ local"]
        end

        subgraph "Đánh Giá"
            AE["AssessmentEngine<br/>Inventory · Sức khỏe dependency<br/>Build/CI · Secrets"]
            UP["UpgradePlanner<br/>Sắp xếp lá trước"]
        end

        subgraph "API Công Khai"
            API["DataGuardApi · ValidationPipeline<br/>DataGuardFactory"]
        end

        subgraph "Cấu Hình"
            CFG["DataGuardConfiguration<br/>GroundTruthMode · NamingConvention<br/>Oracle/SqlServer configs"]
        end
    end

    API --> CVE
    CVE --> RULES
    RULES --> ABS
    EF & SP & MANUAL --> ABS
    RULES --> RDG
    DE --> ABS
    BM --> ABS
    ZTP --> CM
    CM --> AUDIT
    RPM --> ABS

    style ABS fill:#e1f5fe,stroke:#0288d1
    style API fill:#e8f5e9,stroke:#388e3c
    style CVE fill:#fff3e0,stroke:#f57c00
    style ZTP fill:#ffcdd2,stroke:#c62828
```

---

## 7. Điểm Mở Rộng

DataGuard được thiết kế để mở rộng ở mọi tầng:

| Điểm Mở Rộng | Cơ Chế | Trường Hợp Sử Dụng |
|---------------|--------|---------------------|
| **Rules Tùy Chỉnh** | Attribute MEF 2 `[ExportRule]` | Thêm rules validation đặc thù domain |
| **Nguồn Contract** | Interface `IContractSource` | Thêm nguồn dữ liệu mới (ví dụ: queries Dapper) |
| **Đích Chẩn Đoán** | `ISarifSink` / `IDiagnosticSink` | Định dạng output tùy chỉnh (SonarQube, GitHub) |
| **Credential Providers** | Interface `ICredentialProvider` | Backend quản lý secret tùy chỉnh |
| **Audit Loggers** | Interface `IAuditLogger` | Đích audit tùy chỉnh (SIEM, database) |
| **Công Cụ Bên Ngoài** | Interface `IExternalToolPlugin` | Tích hợp linters bên thứ ba |
| **Quy Ước Đặt Tên** | Enum `NamingConvention` | Chiến lược mapping DB↔C# tùy chỉnh |

---

## 8. Quyết Định Công Nghệ

| Quyết Định | Lựa Chọn | Lý Do |
|------------|---------|-------|
| Target framework | `net9.0` | Gần LTS mới nhất, cải thiện hiệu suất, `Parallel.ForEachAsync` |
| Contracts target | `netstandard2.0` | Tương thích IDE host tối đa (VS, VS Code, Roslyn) |
| Parse SQL | `ScriptDom` | Parser T-SQL chính thức của Microsoft, phân tích cấp AST |
| Truy cập Oracle | `ODP.NET Managed` | `ALL_ARGUMENTS`/`ALL_TAB_COLUMNS` cho ground truth |
| Mô hình analyzer | Tầng kép | IDE: `IIncrementalGenerator` (chỉ syntax, ~ms). CI: `DiagnosticAnalyzer` (semantic đầy đủ) |
| Hệ thống plugin | MEF 2 (`System.Composition`) | Khám phá cấp assembly, không cần cấu hình runtime |
| Định dạng output | SARIF 2.1.0 | Tiêu chuẩn ngành, GitHub Code Scanning native |
| Ký | Sigstore cosign | Keyless, dựa trên OIDC, không cần quản lý secret |
| Container | Docker đa kiến trúc | `linux/amd64` + `linux/arm64` qua BuildKit |

---

## Xem Thêm

- [Triết Lý Thiết Kế](design-philosophy.md) — Nguyên tắc đằng sau các quyết định này
- [Mô Hình Thành Phần](component-model.md) — Trách nhiệm và interface chi tiết
- [Stack Công Nghệ](tech-stack.md) — Đánh giá đầy đủ các phụ thuộc
- [Sơ Đồ Luồng Dữ Liệu](../04-diagrams/data-flow.md) — Trực quan hóa luồng chi tiết
- [Sơ Đồ Tuần Tự](../04-diagrams/sequence-diagrams.md) — Các chuỗi tương tác
