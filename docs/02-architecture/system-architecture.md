# System Architecture

> **DataGuard** — .NET 9 contract validation engine for Entity ↔ Stored Procedure / Raw SQL.

This document describes the full system architecture of DataGuard, including component topology, layer design, data flow, security architecture, and CI/CD pipeline.

---

## 1. High-Level Component Topology

DataGuard consists of **11 source projects** organized in a strict layered architecture. The Contracts layer targets `netstandard2.0` for maximum IDE compatibility; the Core engine and adapters target `net9.0`; IDE extensions target their respective host frameworks.

```mermaid
graph TB
    subgraph "IDE Extensions"
        VS["DataGuard.VisualStudio<br/><i>net472 · VS 2022</i>"]
        VSC["DataGuard.VSCode<br/><i>TypeScript · VS Code</i>"]
    end

    subgraph "Tooling Layer"
        CLI["DataGuard.Cli<br/><i>net9.0 · System.CommandLine</i>"]
        CF["DataGuard.CodeFixes<br/><i>netstandard2.0 · Roslyn</i>"]
        AN["DataGuard.Analyzers<br/><i>netstandard2.0 · Roslyn</i>"]
    end

    subgraph "Adapter Layer"
        OA["DataGuard.Oracle.Adapter<br/><i>net9.0 · ODP.NET</i>"]
        SA["DataGuard.SqlServer.Adapter<br/><i>net9.0 · SqlClient</i>"]
        MA["DataGuard.MySql.Adapter<br/><i>net9.0 · MySqlConnector</i>"]
        PA["DataGuard.PostgreSql.Adapter<br/><i>net9.0 · Npgsql</i>"]
    end

    subgraph "Core Engine"
        CORE["DataGuard.Core<br/><i>net9.0 · Zero vendor deps</i>"]
    end

    subgraph "Contracts Layer"
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

## 2. Layer Architecture

DataGuard follows a strict **bottom-up dependency model**. Each layer only depends on layers below it — never sideways or upward.

```mermaid
graph TB
    subgraph "Layer 5 — IDE Hosts"
        L5["Visual Studio 2022 · VS Code"]
    end

    subgraph "Layer 4 — Tooling"
        L4A["CLI (dataguard)"]
        L4B["Analyzers + CodeFixes"]
    end

    subgraph "Layer 3 — Database Adapters"
        L3A["Oracle (ODP.NET)"]
        L3B["SQL Server (SqlClient)"]
        L3C["MySQL (MySqlConnector)"]
        L3D["PostgreSQL (Npgsql)"]
    end

    subgraph "Layer 2 — Core Engine"
        L2["DataGuard.Core<br/>Rules · Sources · Security · Baseline<br/>Reporting · Validation · Plugins<br/>Telemetry · Assessment · PublicApi"]
    end

    subgraph "Layer 1 — Contracts"
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

### Layer Responsibilities

| Layer | Target | Responsibility |
|-------|--------|---------------|
| **L1 — Contracts** | `netstandard2.0` | Shared attributes (`SkipContractCheck`, `ExpectedColumn`, `ExpectedSpParameter`), naming conventions (`snake_case` ↔ `PascalCase`). Zero runtime dependencies. |
| **L2 — Core Engine** | `net9.0` | Domain model (`Contracts.cs`), rules engine (DG001–DG016), contract sources (EF Core, SQL parsers), zero-trust security, baseline management, SARIF reporting, concurrent validation, MEF plugin system, telemetry, assessment engine, public API surface. |
| **L3 — Adapters** | `net9.0` | Database-specific readers. Oracle reads `ALL_ARGUMENTS`/`ALL_TAB_COLUMNS`. SQL Server uses `ScriptDom` + `SqlConnection`. MySQL/PostgreSQL use information_schema. Each adapter implements `IContractSource`. |
| **L4 — Tooling** | mixed | CLI (`System.CommandLine`), Roslyn analyzers (IDE light + CI heavy layers), code fix providers. |
| **L5 — IDE Hosts** | mixed | VS 2022 extension (VSIX, `net472`), VS Code extension (TypeScript, npm). |

---

## 3. Data Flow: Source Extraction → Rule Validation → Reporting

The validation pipeline follows a linear data flow with concurrent rule execution.

```mermaid
flowchart LR
    subgraph "1. Source Extraction"
        A1["EF Core Model<br/>(EfModelSource)"]
        A2["SQL Server SP<br/>(SqlServerStoredProcedureParser)"]
        A3["Raw SQL<br/>(RawSqlParser)"]
        A4["Oracle ALL_ARGUMENTS<br/>(AllArgumentsReader)"]
        A5["MySQL/PG<br/>(information_schema)"]
        A6["Manual Attributes<br/>(ExpectedColumn, ExpectedSpParameter)"]
    end

    subgraph "2. Contract Assembly"
        B["ContractDescriptor[]<br/>EntityDescriptor · StoredProcedureDescriptor<br/>RawSqlDescriptor · DatabaseSchemaDescriptor"]
    end

    subgraph "3. Rule Validation"
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

    subgraph "4. Violation Collection"
        D["ConcurrentValidationEngine<br/>Bounded parallelism · Backpressure"]
    end

    subgraph "5. Reporting"
        E1["SARIF 2.1.0<br/>(DiagnosticEmitter)"]
        E2["Console Output<br/>(ConsoleDiagnosticSink)"]
        E3["Evidence Artifact<br/>(ContractEvidenceWriter)"]
        E4["Contract Export<br/>(ContractExportWriter)"]
        E5["Baseline Diff<br/>(BaselineManager)"]
    end

    A1 & A2 & A3 & A4 & A5 & A6 --> B
    B --> C1 & C2 & C3 & C4 & C5 & C6 & C7 & C8 & CN
    C1 & C2 & C3 & C4 & C5 & C6 & C7 & C8 & CN --> D
    D --> E1 & E2 & E3 & E4 & E5
```

### Pipeline Stages

1. **Source Extraction** — Each `IContractSource` implementation connects to its data source (database, EF model, code attributes) and produces `ContractDescriptor` records. Sources run independently and can be parallelized.

2. **Contract Assembly** — Descriptors are collected into a unified `IReadOnlyList<ContractDescriptor>`. The assembly includes entity shapes, stored procedure parameters, result set columns, and database schema ground truth.

3. **Rule Validation** — The `ConcurrentValidationEngine` runs all registered `IContractRule` implementations against all contracts. Rules are executed with bounded parallelism (`MaxDegreeOfParallelism`) and backpressure (max 100K violations).

4. **Violation Collection** — Violations are collected in a `ConcurrentBag<ContractViolation>`, deduplicated, and sorted by `RuleId` then `Message`.

5. **Reporting** — The `DiagnosticEmitter` fans out violations to multiple sinks: SARIF files, console, evidence artifacts, contract exports, and baseline diffs.

---

## 4. Security Architecture: Zero-Trust Credential Chain

DataGuard implements a **zero-trust** security model. Credentials are never logged, never serialized in plain text, and are encrypted at rest when configured.

```mermaid
flowchart TB
    subgraph "Credential Sources"
        S1["Environment Variables<br/>(DATAGUARD_CONNECTION_STRING)"]
        S2["AWS Secrets Manager<br/>(AWSSDK.SecretsManager)"]
        S3["Azure Key Vault<br/>(KeyVaultUri)"]
        S4["HashiCorp Vault<br/>(VaultAddress)"]
        S5["Encrypted Config File<br/>(EncryptConnectionStringAtRest)"]
    end

    subgraph "Zero-Trust Layer"
        ZTP["ZeroTrustCredentialProvider<br/><i>Never logs secrets</i>"]
        CM["CredentialManager<br/><i>Rotation detection · DPAPI encryption</i>"]
        CH["CredentialHandle<br/><i>Secure IDisposable wrapper</i>"]
    end

    subgraph "Audit Layer"
        AL["IAuditLogger<br/>FileAuditLogger · NullAuditLogger"]
        AE["AuditEntry<br/><i>Hash-chained · Tamper-evident</i>"]
    end

    subgraph "Policy Engine"
        FP["Fail-Closed Policy<br/><i>AllowPlaintextConfigFallback = false</i>"]
        RD["Rotation Detection<br/><i>CredentialRotationWarningDays</i>"]
    end

    S1 & S2 & S3 & S4 & S5 --> ZTP
    ZTP --> CM
    CM --> CH
    CH -->|"Use & dispose"| APP["Application Code"]
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

### Security Principles

| Principle | Implementation |
|-----------|---------------|
| **Never log secrets** | `ZeroTrustCredentialProvider` strips credentials from all log output. `CredentialHandle` clears its value on `Dispose()`. |
| **Encrypt at rest** | `CredentialManager` uses `System.Security.Cryptography.ProtectedData` (DPAPI) for local encryption. AWS/Azure/Vault provide cloud-native encryption. |
| **Detect rotation** | `CredentialRotationWarningDays` triggers warnings when credentials age beyond threshold. |
| **Fail-closed** | `AllowPlaintextConfigFallback = false` (default) prevents silent credential downgrade to plain config files. |
| **Audit trail** | `FileAuditLogger` writes hash-chained `AuditEntry` records. Each entry includes `PreviousHash` for tamper detection. |
| **Redact output** | `ContractEvidenceWriter.Redact()` strips `password=`, `token=`, `Authorization: Bearer` from all evidence artifacts. |

---

## 5. CI/CD Pipeline Architecture

DataGuard uses GitHub Actions with a **defense-in-depth** pipeline: build → test → security scan → CodeQL → sign → publish.

```mermaid
flowchart LR
    subgraph "CI Pipeline (ci.yml)"
        CI1["Build & Test<br/>dotnet build · dotnet test<br/>Coverage ≥ 60%"]
        CI2["Security Scan<br/>NuGet vulnerabilities<br/>TruffleHog secrets"]
        CI3["CodeQL Analysis<br/>C# SAST"]
        CI4["Docker Smoke<br/>Build + --help"]
        CI5["SBOM Generation<br/>Microsoft.Sbom.DotNetTool"]
    end

    subgraph "Release Pipeline (release.yml)"
        R1["Build & Test<br/>Tag-versioned pack"]
        R2["Security Scan<br/>Vuln + TruffleHog"]
        R3["CodeQL Analysis"]
        R4["Sigstore Signing<br/>cosign sign-blob<br/>Keyless OIDC"]
        R5["NuGet Publish<br/>dotnet nuget push"]
        R6["GitHub Release<br/>Signed artifacts"]
        R7["Docker Multi-Arch<br/>linux/amd64 + linux/arm64"]
        R8["SBOM Generation"]
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

### Pipeline Stages

| Stage | Tool | Purpose |
|-------|------|---------|
| **Build** | `dotnet build --configuration Release` | Compile all 11 projects with `TreatWarningsAsErrors` |
| **Test** | `dotnet test` + XPlat Code Coverage | 291+ tests, 60% coverage gate |
| **Format Gate** | `dotnet format --verify-no-changes` | Enforce consistent code style |
| **Vulnerability Scan** | `dotnet list package --vulnerable` | Fail on any vulnerable NuGet package |
| **Secret Scan** | TruffleHog v3.97.0 | Verified secrets only, exclude build artifacts |
| **SAST** | CodeQL v4.37.7 | C# security analysis |
| **Signing** | Sigstore cosign v3.1.3 | Keyless OIDC signing with bundle output |
| **SBOM** | Microsoft.Sbom.DotNetTool v4.1.5 | CycloneDX SBOM for supply chain transparency |
| **Docker** | Multi-arch build | `linux/amd64` + `linux/arm64` via BuildKit |

---

## 6. Internal Module Architecture

The Core engine is organized into **12 internal modules**, each with a single responsibility.

```mermaid
graph TB
    subgraph "DataGuard.Core"
        subgraph "Domain Model"
            ABS["Abstractions<br/>IContractSource · IContractRule<br/>ContractDescriptor · ContractViolation"]
        end

        subgraph "Rules Engine"
            RULES["Rules<br/>ContractRuleBase · DG001–DG016<br/>PhantomIdentifierRule"]
            RDG["RuleDependencyGraph<br/>Topological sort · Built-in deps"]
        end

        subgraph "Sources"
            EF["EfModelSource<br/>Runtime IModel · Design-time Snapshot"]
            SP["SqlServerParsers<br/>ScriptDom · SqlParameterVisitor"]
            MANUAL["ManualContractSource<br/>Attribute-based ground truth"]
        end

        subgraph "Security"
            ZTP["ZeroTrustCredentialProvider"]
            CM["CredentialManager"]
            AUDIT["IAuditLogger · FileAuditLogger"]
        end

        subgraph "Baseline"
            BM["BaselineManager<br/>Snapshot · Drift detection<br/>Schema hash"]
        end

        subgraph "Reporting"
            DE["DiagnosticEmitter<br/>SARIF · Console · File"]
            CE["ContractEvidenceWriter<br/>Redacted JSON"]
            CX["ContractExportWriter<br/>TypeScript DTO generation"]
        end

        subgraph "Validation"
            CVE["ConcurrentValidationEngine<br/>Bounded parallelism"]
        end

        subgraph "Plugins"
            RPM["RulePluginManager<br/>MEF 2 discovery"]
        end

        subgraph "Telemetry"
            TC["TelemetryCollector<br/>Opt-in · Local only"]
        end

        subgraph "Assessment"
            AE["AssessmentEngine<br/>Inventory · Dependency health<br/>Build/CI · Secrets"]
            UP["UpgradePlanner<br/>Leaf-first ordering"]
        end

        subgraph "Public API"
            API["DataGuardApi · ValidationPipeline<br/>DataGuardFactory"]
        end

        subgraph "Configuration"
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

## 7. Extension Points

DataGuard is designed for extensibility at every layer:

| Extension Point | Mechanism | Use Case |
|----------------|-----------|----------|
| **Custom Rules** | MEF 2 `[ExportRule]` attribute | Add domain-specific validation rules |
| **Contract Sources** | `IContractSource` interface | Add new data sources (e.g., Dapper queries) |
| **Diagnostic Sinks** | `ISarifSink` / `IDiagnosticSink` | Custom output formats (SonarQube, GitHub) |
| **Credential Providers** | `ICredentialProvider` interface | Custom secret management backends |
| **Audit Loggers** | `IAuditLogger` interface | Custom audit destinations (SIEM, database) |
| **External Tools** | `IExternalToolPlugin` interface | Integrate third-party linters |
| **Naming Conventions** | `NamingConvention` enum | Custom DB↔C# mapping strategies |

---

## 8. Technology Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Target framework | `net9.0` | Latest LTS-adjacent, performance improvements, `Parallel.ForEachAsync` |
| Contracts target | `netstandard2.0` | Maximum IDE host compatibility (VS, VS Code, Roslyn) |
| SQL parsing | `ScriptDom` | Official Microsoft T-SQL parser, AST-level analysis |
| Oracle access | `ODP.NET Managed` | `ALL_ARGUMENTS`/`ALL_TAB_COLUMNS` for ground truth |
| Analyzer model | Dual-layer | IDE: `IIncrementalGenerator` (syntax-only, ~ms). CI: `DiagnosticAnalyzer` (full semantic) |
| Plugin system | MEF 2 (`System.Composition`) | Assembly-level discovery, no runtime configuration |
| Output format | SARIF 2.1.0 | Industry standard, GitHub Code Scanning native |
| Signing | Sigstore cosign | Keyless, OIDC-based, no secret management |
| Container | Multi-arch Docker | `linux/amd64` + `linux/arm64` via BuildKit |

---

## See Also

- [Design Philosophy](design-philosophy.md) — Principles behind these decisions
- [Component Model](component-model.md) — Detailed component responsibilities and interfaces
- [Technology Stack](tech-stack.md) — Full dependency evaluation
- [Data Flow Diagrams](../04-diagrams/data-flow.md) — Detailed flow visualizations
- [Sequence Diagrams](../04-diagrams/sequence-diagrams.md) — Interaction sequences
