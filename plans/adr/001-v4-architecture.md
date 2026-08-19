# ADR-001: DataGuard v4 Architecture - Entity ↔ SP/Raw SQL Contract Validator

**Status**: Accepted
**Date**: 2025-01-18
**Supersedes**: v3 architecture from research

---

## Context

Building **DataGuard** - a NuGet-distributed Roslyn Analyzer for Entity ↔ Stored Procedure/Raw SQL contract validation, targeting .NET developers using EF Core/Dapper with Oracle and SQL Server.

Research files in `/Volumes/Data/101.AI/GitHub/eco_support_net_oracle/research/muc_tieu/` contain comprehensive analysis including two rounds of redteam review (10 experts total).

---

## Decision: v4 Architecture

### Core Principle: "Separate IDE from CI"

**Tách hoàn toàn 2 tầng thực thi** (Redteam #1 - Compiler Engineer):
| Layer | Runs Where | Responsibility | Performance |
|-------|------------|----------------|-------------|
| **IDE Layer** (Light) | Every keystroke in IDE | Syntax-level only: marks "unvalidated SQL call" | ~ms, uses `IIncrementalGenerator` + `SyntaxValueProvider.ForAttributeWithMetadataName` |
| **CI Layer** (Heavy) | PR gate / scheduled | Full diff-engine + DB connection + SARIF emission | Minutes, runs as `dotnet tool` in CI pipeline |

**Rationale**: Running diff-engine + DB connection in live analyzer would slow builds 4-5x (StyleCop/FxCop precedent). IDE layer only does syntax-level marking; heavy work deferred to CI.

---

### Ground Truth: Three Modes (Default = Snapshot)

**Three trust levels** (Redteam #2 - DBA Security):
| Mode | Description | CI Credentials | Default? |
|------|-------------|----------------|----------|
| **Full** | Live DB connection, auto-refresh snapshot | Required (read-only role + EXECUTE on procs) | No |
| **Snapshot** (DEFAULT) | Offline JSON file committed to repo, refreshed by DBA manually | **None** - zero CI creds | **Yes** |
| **Manual** | Dev declares shape via `[ExpectedColumn]` attributes on DTOs | None | No |

**Snapshot Mode = Default** - Safe for high-security orgs, easy DBA approval, zero CI credential risk.

**Snapshot File** includes Oracle/SQL Server version metadata; tool warns (not fails) if snapshot version diverges from target environment (Redteam #5 - Enterprise Architect).

---

### Security Hardening (Redteam #3 - DevSecOps)

- **Package Signing**: Sigstore/NuGet package signing, SBOM per release
- **Credential Handling**: Never read connection string directly; inject via env var/secret vault; never log values
- **Least Privilege**: Document read-only role + EXECUTE on specific procs only
- **Package Signing**: Sigstore/NuGet signing, publish SBOM per release
- **Supply Chain**: Tool becomes supply-chain target; sign packages, SBOM, least-privilege docs

---

### Licensing Separation (Redteam #9 - Legal)

| Package | License | Dependencies |
|---------|---------|--------------|
| `DataGuard.Core` | MIT/Apache-2.0 | Zero vendor deps |
| `DataGuard.SqlServer.Adapter` | MIT | Microsoft.SqlServer.TransactSql.ScriptDom (MIT) |
| `DataGuard.Oracle.Adapter` | MIT + Oracle License | `Oracle.ManagedDataAccess.Core` (Oracle Distribution License) |
| `DataGuard.Analyzers` | MIT | Roslyn |
| `DataGuard.Cli` | MIT | Core + Adapters |

**Rationale**: Core stays OSI-approved for Anthropic grant eligibility; Oracle adapter is optional opt-in.

---

### Baseline Mechanism - MVP Mandatory (Redteam #6 - SAST Specialist)

**Problem**: Legacy codebases have hundreds of existing drifts. Tool flooding CI with noise = immediate uninstall.

**Solution**: `dataguard baseline` command runs **once** on legacy codebase:
- Freezes all existing drift into `.dataguard-baseline.json` (committed to repo)
- CI only fails on **new drift** after baseline
- Model: "stop the bleeding" (Psalm/Android Lint baseline pattern)

**Baseline file includes**: version metadata, ground truth mode, timestamp, schema hash.

---

### Output Format: SARIF 2.1 (Redteam #10 - DX)

- GitHub Code Scanning / Azure DevOps native rendering
- Annotations on exact code lines in PR reviews
- Metadata for responsibility routing (dev vs DBA)
- Enterprise dashboard compatibility

---

### Module Architecture (v4)

```
DataGuard.Core (MIT, zero vendor deps)
├── Abstractions: IContractSource, IContractRule, Models
├── Sources: EfModelSource, StoredProcedureParser, RawSqlParser
├── Rules: ParameterTypeMatch, ParameterCount, ParameterDirection, 
│         ColumnShapeMatch, NullableMismatch, NamingConvention
├── Reporting: DiagnosticEmitter, ViolationReporter, SarifSink
└── Baseline: BaselineManager, BaselineFile

DataGuard.SqlServer.Adapter (MIT)
├── ScriptDomParser (Microsoft.SqlServer.TransactSql.ScriptDom)
├── SysParametersReader
└── FirstResultSetDescriber (sp_describe_first_result_set)

DataGuard.Oracle.Adapter (MIT + Oracle License - separate package)
├── AllArgumentsReader (ALL_ARGUMENTS)
├── AllTabColumnsReader (ALL_TAB_COLUMNS)
├── NlsSessionReader (NLS_LENGTH_SEMANTICS)
├── RefCursorDescriber (DBMS_SQL.DESCRIBE_COLUMNS)
├── DialectChecker (5 rules)
└── LengthMismatchDetector (3 mismatch types + EfCoreInferenceSimulator)

DataGuard.Analyzers (MIT)
├── FromSqlRawAnalyzer
├── ExecuteSqlRawAnalyzer
└── DapperQueryAnalyzer

DataGuard.Cli (MIT) - dotnet tool
├── ValidateCommand
├── BaselineCommand
├── SnapshotCommand (refresh)
└── OracleCheckCommand
```

---

### MVP Module Order (Validated)

| Version | Scope | Rationale |
|---------|-------|-----------|
| **v0.1** | Core diff-engine + SQL Server (ScriptDOM, static, no DB) | Free, open-source, no DB needed; larger .NET user base for early feedback |
| **v0.2** | Oracle Adapter (catalog-based, needs DB) | Requires live Oracle; builds on proven diff-engine |
| **v0.3** | Length-mismatch (both vendors) + Baseline | Solves ORA-12899 at design-time |
| **v0.4** | Oracle Dialect Check (optional) | PL/SQL parser complexity; optional value-add |

---

### Test Strategy (Redteam #7 - QA Infrastructure)

| Test Layer | DB Required? | Purpose |
|------------|--------------|---------|
| Unit | No (mock ground truth) | Diff logic, naming normalization |
| Integration | Yes (Testcontainers.Oracle + gvenzl/oracle-xe) | ALL_ARGUMENTS/DESCRIBE real behavior |
| Golden Corpus | Yes (periodic) | % hallucination caught; H1/H2/H3 taxonomy |

**Golden Corpus Taxonomy** (from 2026 AI hallucination study):
- H1 (41/50): Phantom identifiers (invented tables/columns)
- H2 (7/50): Column-table mismatch
- H3 (2/50): Dialect confusion (MySQL → Oracle)
- **Length Mismatch**: char/byte semantics, NVARCHAR2(2000) fallback

---

### Dev Workflow (Redteam #4 - Workflow Design)

```
1. Install:     dotnet add package DataGuard.Core (+ Oracle.Adapter)
2. Init:        dataguard init → .dataguard.yml
3. Baseline:    dataguard baseline --connection "CI schema"  (once for legacy)
4. Daily IDE:   Syntax-only warning: "⚠ SP call not validated - run 'dataguard check'"
5. Pre-commit:  dataguard check --offline (sub-second, no DB)
6. CI Gate:     dataguard check --connection "CI" --format sarif
7. Schema change: dataguard snapshot refresh --connection "CI" (review diff in PR)
```

---

## Consequences

### Positive
- ✅ Zero CI credential risk with Snapshot default
- ✅ No IDE performance impact (syntax-only live analysis)
- ✅ Anthropic grant eligible (Core MIT, license separation)
- ✅ Legacy-friendly (baseline mechanism)
- ✅ SARIF native integration
- ✅ Free CI testing (Testcontainers + gvenzl/oracle-xe)
- ✅ Clear upgrade path: SQL Server → Oracle → Length → Dialect

### Negative / Risks
- ⚠️ Requires DBA to run `baseline`/`snapshot refresh` initially
- ⚠️ Dynamic SQL (`EXECUTE IMMEDIATE`) needs `[SkipContractCheck]` attribute
- ⚠️ Oracle adapter has separate license (user must opt-in)
- ⚠️ Version drift between CI schema and prod = false negatives (documented)
- ⚠️ `NLS_LENGTH_SEMANTICS` affects length calculations (must read at runtime)

---

## Implementation Roadmap (6 Weeks)

| Week | Deliverable |
|------|-------------|
| 1 | Core skeleton, `EfModelSource`, `StoredProcedureParser` (SQL Server), 2 basic rules, POC |
| 2 | All 6 rules + `DiagnosticEmitter` + SARIF sink, Module 1 usable for SQL Server |
| 3 | Oracle Adapter (`ALL_ARGUMENTS`), Module 2a Dialect Checker |
| 4-5 | Module 2b Length Mismatch (`EfCoreInferenceSimulator`, `LengthSemanticsResolver`) |
| 6 | Roslyn Analyzers + CLI packaging (`dotnet tool`), docs, baseline mechanism |

---

## Related Documents

- `plans/master-plan.md` - Full execution plan
- `plans/adr/002-module-boundaries.md` - Module boundary decisions
- `plans/adr/003-security-model.md` - Security model details
- `plans/adr/004-test-strategy.md` - Test strategy details
- Research: `/Volumes/Data/101.AI/GitHub/eco_support_net_oracle/research/muc_tieu/`

---

## Sign-off

| Role | Name | Status |
|------|------|--------|
| Compiler Engineer | Redteam #1 | ✅ Accepted (IDE/CI separation) |
| DBA Security | Redteam #2 | ✅ Accepted (Snapshot default) |
| DevSecOps | Redteam #3 | ✅ Accepted (Package signing, SBOM) |
| OSS Growth | Redteam #4 | ✅ Accepted (dbt contracts narrative) |
| Enterprise Architect | Redteam #5 | ✅ Accepted (Version metadata) |
| SAST Specialist | Redteam #6 | ✅ Accepted (Baseline mandatory) |
| QA Infrastructure | Redteam #7 | ✅ Accepted (Testcontainers free tier) |
| Data Modeling | Redteam #8 | ✅ Accepted (Edge cases documented) |
| Legal | Redteam #9 | ✅ Accepted (License separation) |
| DX | Redteam #10 | ✅ Accepted (SARIF, baseline) |

**Final Decision**: Proceed with v4 architecture as specified above. Begin Phase 3: Implementation Planning (`ck:plan`).