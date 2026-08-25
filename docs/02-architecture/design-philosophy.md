# Design Philosophy

> The principles that shape every line of DataGuard code.

DataGuard is not just a linter — it is a **contract enforcement system** for the boundary between .NET application code and database stored procedures / raw SQL. Every design decision flows from eight core principles.

---

## 1. Evidence-First

> **Every claim must be backed by database ground truth.**

DataGuard never guesses. When it reports that a stored procedure expects 5 parameters but the call site passes 4, that claim is backed by reading `ALL_ARGUMENTS` (Oracle), `sys.parameters` (SQL Server), or `information_schema` (MySQL/PostgreSQL). When it says a column is `VARCHAR2(100)` but the entity maps `MaxLength = 200`, the evidence comes from `ALL_TAB_COLUMNS`.

This principle extends to the output format. The `ContractEvidence` artifact is versioned, sorted, and redacted — a durable, machine-readable record that CI pipelines can gate on.

```mermaid
flowchart LR
    A["Code Claim<br/>(Entity has MaxLength=200)"] --> B{"Ground Truth?"}
    B -->|"Oracle"| C["ALL_TAB_COLUMNS<br/>VARCHAR2(100)"]
    B -->|"SQL Server"| D["sys.columns<br/>varchar(100)"]
    B -->|"MySQL"| E["information_schema<br/>VARCHAR(100)"]
    C & D & E --> F["ContractViolation<br/>DG006: Length mismatch"]
    F --> G["Evidence Artifact<br/>Versioned · Sorted · Redacted"]

    style B fill:#fff3e0,stroke:#f57c00
    style F fill:#ffcdd2,stroke:#c62828
    style G fill:#e8f5e9,stroke:#388e3c
```

**In code:**
- `EfModelSource` reads the EF Core `IModel` at runtime or from a design-time `ModelSnapshot.cs`
- `AllArgumentsReader` queries `ALL_ARGUMENTS` with overload/sequence awareness
- `AllTabColumnsReader` queries `ALL_TAB_COLUMNS` including `CHAR_USED` (byte vs char semantics)
- `ContractEvidenceWriter` produces deterministic, sorted JSON with no arbitrary metadata

---

## 2. Fail-Closed

> **When in doubt, deny. Credentials never exposed; plaintext fallback disabled by default.**

The `AllowPlaintextConfigFallback` setting defaults to `false`. This means that if the only connection string available is in a plain `.config` file and no vault is configured, DataGuard refuses to proceed rather than silently downgrading to an insecure credential path.

This is a deliberate departure from "developer convenience" defaults. In production CI/CD pipelines, a silent credential downgrade is a security vulnerability, not a convenience.

```mermaid
flowchart TD
    A["Request Credential"] --> B{"Vault configured?"}
    B -->|"Yes"| C["Retrieve from Vault<br/>(AWS/Azure/HashiCorp)"]
    B -->|"No"| D{"Env var set?"}
    D -->|"Yes"| E["Use environment variable"]
    D -->|"No"| F{"AllowPlaintextConfigFallback?"}
    F -->|"true"| G["⚠️ Use config file<br/>(Development only)"]
    F -->|"false (default)"| H["❌ REFUSE<br/>Fail closed"]

    style H fill:#ffcdd2,stroke:#c62828
    style G fill:#fff9c4,stroke:#f9a825
    style C fill:#e8f5e9,stroke:#388e3c
    style E fill:#e8f5e9,stroke:#388e3c
```

**In code:**
- `ZeroTrustCredentialProvider` checks sources in priority order: Vault → Env Var → Config
- `DataGuardConfiguration.AllowPlaintextConfigFallback = false` (default)
- `CredentialHandle` clears its value on `Dispose()` — no lingering secrets in memory

---

## 3. Zero-Trust

> **Never log secrets. Encrypt at rest. Detect rotation.**

Every component that touches credentials follows zero-trust principles:

- **Never log secrets**: `ZeroTrustCredentialProvider` strips credentials from all log output. The `CredentialHandle` wrapper prevents accidental serialization.
- **Encrypt at rest**: `CredentialManager` uses `System.Security.Cryptography.ProtectedData` (DPAPI) for local encryption. Cloud vaults provide their own encryption.
- **Detect rotation**: `CredentialRotationWarningDays` triggers warnings when credentials age beyond the configured threshold.
- **Audit trail**: `FileAuditLogger` writes hash-chained `AuditEntry` records. Each entry includes `PreviousHash` for tamper detection.
- **Redact output**: `ContractEvidenceWriter.Redact()` strips `password=`, `token=`, `Authorization: Bearer` from all evidence artifacts.

```mermaid
flowchart LR
    subgraph "Zero-Trust Chain"
        A["Credential Source"] --> B["ZeroTrustCredentialProvider"]
        B --> C["CredentialHandle<br/>(IDisposable)"]
        C --> D["Use in application"]
        C --> E["Clear on Dispose"]
        B --> F["IAuditLogger<br/>(Hash-chained entries)"]
    end

    style B fill:#ffcdd2,stroke:#c62828
    style C fill:#ffcdd2,stroke:#c62828
    style E fill:#ffcdd2,stroke:#c62828
    style F fill:#fff9c4,stroke:#f9a825
```

---

## 4. Offline-First

> **Snapshot mode needs no database connection.**

DataGuard supports three ground truth modes:

| Mode | Database Required | Use Case |
|------|-------------------|----------|
| **Live** | Yes | CI/CD with database access |
| **Snapshot** | No (pre-captured) | Offline validation, air-gapped environments |
| **Manual** | No (attribute-based) | Early development, no DB yet |

The `BaselineManager` captures a full schema snapshot (tables, columns, data types, nullability) into a JSON file. This snapshot can be committed to source control and used for offline validation. The `SchemaHash` field enables drift detection — if the database schema changes, the snapshot becomes stale.

```mermaid
flowchart TD
    A["Live Database"] -->|"Capture"| B["BaselineManager"]
    B --> C["BaselineFile<br/>Version · SchemaHash<br/>DatabaseVersion · Violations"]
    C -->|"Commit to Git"| D["Source Control"]
    D -->|"Load offline"| E["Snapshot Validation"]
    E --> F{"SchemaHash match?"}
    F -->|"Yes"| G["✅ Validate against snapshot"]
    F -->|"No"| H["⚠️ Drift detected<br/>Re-capture recommended"]

    style G fill:#e8f5e9,stroke:#388e3c
    style H fill:#fff9c4,stroke:#f9a825
```

---

## 5. Extensible

> **MEF plugin architecture for custom rules.**

DataGuard uses MEF 2 (`System.Composition`) for plugin discovery. Custom rules are discovered by scanning assemblies for the `[ExportRule]` attribute. No configuration files, no registration code — just annotate and drop the assembly.

```csharp
[ExportRule(
    "CUSTOM001",
    Name = "Custom Naming Convention",
    Description = "Enforces custom naming convention for specific schemas",
    Category = "Naming",
    DefaultSeverity = "Warning",
    MinDataGuardVersion = "1.0.0",
    Author = "DataGuard Team",
    Tags = new[] { "naming", "custom" })]
public sealed class CustomNamingConventionRule : IContractRule
{
    // Implementation...
}
```

**Extension points:**

| Extension | Interface | Discovery |
|-----------|-----------|-----------|
| Custom rules | `IContractRule` | `[ExportRule]` attribute |
| Contract sources | `IContractSource` | Constructor injection |
| Diagnostic sinks | `ISarifSink` / `IDiagnosticSink` | `AddSarifSink()` / `AddDiagnosticSink()` |
| Credential providers | `ICredentialProvider` | Constructor injection |
| Audit loggers | `IAuditLogger` | Constructor injection |
| External tools | `IExternalToolPlugin` | MEF discovery |

---

## 6. Enterprise-Ready

> **Audit logging, SARIF output, baseline management, SBOM generation.**

DataGuard is designed for enterprise CI/CD pipelines:

- **SARIF 2.1.0 output**: Industry-standard format, natively supported by GitHub Code Scanning, Azure DevOps, and most SAST platforms.
- **Audit logging**: Hash-chained audit entries for compliance (SOC 2, ISO 27001).
- **Baseline management**: Commit baselines to source control for legacy codebase onboarding.
- **SBOM generation**: CycloneDX SBOM via `Microsoft.Sbom.DotNetTool` for supply chain transparency.
- **Sigstore signing**: Keyless OIDC signing of NuGet packages for provenance verification.
- **Evidence artifacts**: Versioned, redacted JSON for CI gate decisions.

```mermaid
flowchart LR
    subgraph "Enterprise Features"
        A["SARIF 2.1.0<br/>GitHub · Azure DevOps"]
        B["Audit Log<br/>Hash-chained · SOC 2"]
        C["Baseline<br/>Source-controlled snapshots"]
        D["SBOM<br/>CycloneDX · Supply chain"]
        E["Sigstore<br/>Keyless signing"]
        F["Evidence<br/>Versioned · Redacted"]
    end

    A & B & C & D & E & F --> G["Enterprise CI/CD Pipeline"]

    style G fill:#e8f5e9,stroke:#388e3c
```

---

## 7. Compiler-in-the-Loop

> **Roslyn analyzers catch issues on keystroke.**

DataGuard uses a **dual-layer analyzer architecture**:

| Layer | Technology | Speed | Scope |
|-------|-----------|-------|-------|
| **IDE Light** | `IIncrementalGenerator` | ~ms per keystroke | Syntax-only: unvalidated SQL calls, missing attributes |
| **CI Heavy** | `DiagnosticAnalyzer` | Seconds | Full semantic: database-connected validation |

The IDE layer runs on every keystroke and marks unvalidated SQL calls with squiggly underlines. It uses `IIncrementalGenerator` for zero-allocation, incremental caching — no GC pressure during typing.

The CI layer runs in the build pipeline and performs full contract validation with database ground truth. It uses the same diagnostic IDs as the IDE layer, so warnings seen in the IDE are a subset of CI failures.

```mermaid
flowchart TD
    subgraph "IDE (On Keystroke)"
        A["IIncrementalGenerator<br/>Syntax-only · ~ms"]
        A --> B["DG001: Unvalidated call<br/>DG002: Missing attribute"]
    end

    subgraph "CI (On Build)"
        C["DiagnosticAnalyzer<br/>Semantic + DB ground truth"]
        C --> D["DG001–DG016: Full validation"]
    end

    B -->|"Same diagnostic IDs"| D

    style A fill:#e1f5fe,stroke:#0288d1
    style C fill:#fff3e0,stroke:#f57c00
```

---

## 8. dbt-Inspired

> **Model contracts pattern ported to .NET.**

DataGuard borrows the **model contracts** concept from dbt (data build tool). In dbt, you define contracts that specify the shape of your data models — column names, types, nullability. DataGuard applies the same pattern to .NET:

- **Entity contracts**: EF Core entities define the expected shape (properties, types, nullability).
- **Stored procedure contracts**: Database stored procedures define parameters and result sets.
- **Contract enforcement**: DataGuard validates that the two sides match, catching drift before it reaches production.

This is the core insight: **the boundary between application code and database is a contract, and contracts should be enforced automatically.**

```mermaid
flowchart LR
    subgraph "dbt Pattern"
        A1["Model Definition"] --> B1["Contract<br/>(columns, types)"]
        B1 --> C1["Enforcement<br/>(dbt build)"]
    end

    subgraph "DataGuard Pattern"
        A2["EF Core Entity"] --> B2["Contract<br/>(properties, types)"]
        A3["Stored Procedure"] --> B2
        B2 --> C2["Enforcement<br/>(dataguard validate)"]
    end

    style B1 fill:#e1f5fe,stroke:#0288d1
    style B2 fill:#fff3e0,stroke:#f57c00
    style C2 fill:#e8f5e9,stroke:#388e3c
```

---

## Philosophy Summary

```mermaid
mindmap
  root((DataGuard Philosophy))
    Evidence-First
      Database ground truth
      Versioned artifacts
      Deterministic output
    Fail-Closed
      No plaintext fallback
      Refuse on doubt
      Secure by default
    Zero-Trust
      Never log secrets
      Encrypt at rest
      Detect rotation
    Offline-First
      Snapshot mode
      Git-committed baselines
      Drift detection
    Extensible
      MEF 2 plugins
      Interface-based DI
      Custom rules/sources
    Enterprise-Ready
      SARIF output
      Audit logging
      SBOM + Sigstore
    Compiler-in-the-Loop
      IDE: keystroke speed
      CI: full validation
      Same diagnostic IDs
    dbt-Inspired
      Model contracts
      Automatic enforcement
      Drift detection
```

---

## See Also

- [System Architecture](system-architecture.md) — How these principles manifest in the architecture
- [Component Model](component-model.md) — Interface contracts and extension points
- [Feature Showcase](../01-overview/feature-showcase.md) — Features enabled by these principles
