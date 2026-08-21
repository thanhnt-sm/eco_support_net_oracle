> ⚠️ **SUPERSEDED (2026-08-21)** — Kế hoạch 6 tuần 2025-01 đã hoàn thành/xóa khỏi phạm vi. Thực tế hiện tại vượt plan: thêm MySql/Pg adapters, DataGuard.Contracts, CodeFixes, 2 editor extensions; packages CHƯA publish (blocked NUGET_USER — xem register). Đừng dùng task list dưới đây làm nguồn việc cần làm. Nguồn hiện tại: `plans/2026-08-21-review-handoff.md` + `AI_AGENT_AUDIT.md`.

# DataGuard Implementation Plan

**Based on**: ADR-001 v4 Architecture (plans/adr/001-v4-architecture.md)
**Duration**: 6 weeks
**Team**: Solo developer (with redteam council for major decisions)

---

## Module Dependency Graph

```
DataGuard.Core (foundation, zero vendor deps)
    │
    ├── DataGuard.SqlServer.Adapter (depends on Core + ScriptDOM)
    │
    ├── DataGuard.Oracle.Adapter (depends on Core + Oracle.ManagedDataAccess.Core)
    │
    ├── DataGuard.Analyzers (depends on Core + Roslyn)
    │
    └── DataGuard.Cli (depends on Core + Adapters + Analyzers)
```

---

## Week 1: Core Skeleton + SQL Server POC

### Goals
- Establish repo structure, build system, CI
- Implement `EfModelSource` (IModel + ModelSnapshot.cs)
- Implement `StoredProcedureParser` for SQL Server (`sys.parameters`)
- Implement 2 basic rules: `ParameterCountRule`, `ParameterTypeMatchRule`
- POC: Detect parameter count mismatch on sample

### Tasks

| Task | Description | Acceptance Criteria |
|------|-------------|---------------------|
| 1.1 | Initialize repo with `Directory.Build.props`, `.sln`, `global.json` | `dotnet build` succeeds |
| 1.2 | Create `DataGuard.Core` project with Abstractions/Models | All interfaces compile |
| 1.3 | Implement `EfModelSource` - read `IModel` runtime + `ModelSnapshot.cs` | Extracts entity properties, column types, max lengths |
| 1.4 | Create `DataGuard.SqlServer.Adapter` project | References Core + ScriptDOM |
| 1.5 | Implement `StoredProcedureParser` for SQL Server (`sys.parameters` + `sp_describe_first_result_set`) | Returns parameter list + result shape for sample SP |
| 1.6 | Implement `ParameterCountRule` | Flags mismatch between C# call site and SP parameters |
| 1.7 | Implement `ParameterTypeMatchRule` with SQL Server type mapping | Flags CLR type ↔ SQL type mismatches |
| 1.8 | Create sample solution with EF Core + 1 SP + 1 entity | `dotnet build` runs rules, emits diagnostic |
| 1.9 | Setup GitHub Actions CI (build, test, pack) | CI passes on push |

### Deliverable
- Working POC: `dotnet run --project DataGuard.Cli validate` detects parameter count/type mismatch on sample

---

## Week 2: Complete Module 1 (Core Rules + SARIF + CLI)

### Goals
- Implement remaining 4 rules
- SARIF emission + CLI packaging
- Module 1 usable for SQL Server

### Tasks

| Task | Description | Acceptance Criteria |
|------|-------------|---------------------|
| 2.1 | Implement `ParameterDirectionRule` (IN/OUT/INOUT ↔ `out`/`ref`) | Flags direction mismatches |
| 2.2 | Implement `ColumnShapeMatchRule` (result set columns ↔ entity properties) | Flags missing/extra columns in result set |
| 2.3 | Implement `NullableMismatchRule` (NOT NULL ↔ non-nullable) | Flags nullability mismatches |
| 2.4 | Implement `NamingConventionRule` (snake_case ↔ PascalCase) | Configurable mapping, flags convention violations |
| 2.5 | Implement `DiagnosticEmitter` with 3 sinks: Roslyn, SARIF, Markdown | All 3 formats emit correctly |
| 2.6 | Implement `SarifSink` - SARIF 2.1 output | Validates against SARIF schema |
| 2.6 | Build `DataGuard.Cli` with `ValidateCommand`, `BaselineCommand` | `dataguard validate` works, `dataguard baseline` creates baseline file |
| 2.7 | Implement `BaselineManager` - create/read `.dataguard-baseline.json` | Baseline freezes existing drifts, CI skips baseline drifts |
| 2.8 | Package `DataGuard.Cli` as `dotnet tool` (`.nupkg` with `PackageType=DotnetTool`) | `dotnet tool install --local` works |
| 2.9 | Write unit tests for all 6 rules (mock ground truth) | >90% coverage on rules |
| 2.10 | Integration test with sample SQL Server DB (Testcontainers) | CI runs against real SQL Server |

### Deliverable
- Module 1 MVP: `dotnet tool install DataGuard.Cli` + `dataguard validate` works for SQL Server projects

---

## Week 3: Oracle Adapter + Module 2a (Dialect Check)

### Goals
- Oracle Adapter with `ALL_ARGUMENTS` reader
- Module 2a: Oracle Dialect Checker (5 rules)

### Tasks

| Task | Description | Acceptance Criteria |
|------|-------------|---------------------|
| 3.1 | Create `DataGuard.Oracle.Adapter` project (separate package, optional) | References Core + `Oracle.ManagedDataAccess.Core` |
| 3.2 | Implement `AllArgumentsReader` - query `ALL_ARGUMENTS` + `ALL_PROCEDURES` | Returns parameter list for sample Oracle SP |
| 3.3 | Implement `RefCursorDescriber` using `DBMS_SQL.DESCRIBE_COLUMNS` | Returns result shape for REF CURSOR |
| 3.4 | Implement `AllTabColumnsReader` for length-mismatch metadata | Reads `ALL_TAB_COLUMNS` with `char_used` (B/C) |
| 3.5 | Implement `NlsSessionReader` for `NLS_LENGTH_SEMANTICS` | Returns Char/Byte semantics |
| 3.6 | Create `DataGuard.Oracle.DialectChecker` with 5 rules | All 5 rules implement `IContractRule` |
| 3.7 | Rule: `OracleSyntaxInNonOracleContext` (DECODE, NVL, (+), DUAL, etc.) | Detects Oracle syntax in non-Oracle context |
| 3.8 | Rule: `NonOracleFunctionInOracleContext` (ISNULL, TOP, GETDATE) | Detects SQL Server syntax in Oracle context |
| 3.9 | Rule: `ProviderOptionMismatch` (UseOracle not registered) | Detects missing `UseOracle` |
| 3.10 | Rule: `SqlServerSyntaxLeak` (EXEC dbo.Sp vs BEGIN/END) | Detects SQL Server EXEC in Oracle |
| 3.11 | Rule: `RawSqlUnmappedTypeUsage` (unmapped type in Oracle EF Core 8) | Detects unmapped type usage |
| 3.12 | Add Oracle type mapping table (NVARCHAR2, VARCHAR2, CLOB, NUMBER, etc.) | Complete mapping for all Oracle types |
| 3.12 | Integration test with Testcontainers.Oracle + gvenzl/oracle-xe | CI spins Oracle XE, runs dialect checks |

### Deliverable
- Oracle Adapter functional, 5 dialect rules working, CI tests on Oracle XE

---

## Week 4-5: Module 2b - Length Mismatch Check (ORA-12899 Prevention)

### Goals
- Implement `EfCoreInferenceSimulator` (mirrors #33218 behavior)
- Implement `LengthSemanticsResolver` (reads NLS_LENGTH_SEMANTICS)
- Implement `LengthMismatchDetector` with 3 mismatch types

### Tasks

| Task | Description | Acceptance Criteria |
|------|-------------|---------------------|
| 4.1 | Implement `EfCoreInferenceSimulator.Predict()` - mirrors #33218 | Predicts NVARCHAR2(2000) fallback when size=null + Unicode |
| 4.2 | Implement `LengthSemanticsResolver.ResolveAsync()` - reads NLS_LENGTH_SEMANTICS | Returns Char/Byte from `nls_session_parameters` |
| 4.3 | Implement `LengthMismatchDetector.Detect()` with 3 branches: | |
| 4.3a | Branch 1: Entity MaxLength > Column CharLength | Flags `LengthExceedsColumnViolation` |
| 4.3b | Branch 2: Byte semantics + Unicode overflow risk | Flags `ByteLengthOverflowRiskViolation` |
| 4.3c | Branch 3: CLOB/VARCHAR2 switching risk (mirror #33218) | Flags `InferredSizeFallbackViolation` |
| 5.1 | Implement `OracleTypeMap` complete (all Oracle types) | Covers all Oracle data types |
| 5.2 | Handle edge cases: overloaded procedures (signature key), byte/char semantics | Overloads keyed by signature; CHAR vs BYTE handled |
| 5.3 | Integration tests with golden corpus (H1/H2/H3/Length) | Detects 96%+ of AI hallucination cases |
| 5.4 | Test with Vietnamese data (Unicode, byte vs char) | Catches byte-length overflow for Vietnamese |

### Deliverable
- Length mismatch detection catches ORA-12899 at design-time
- Handles char/byte semantics, NVARCHAR2(2000) fallback, overloaded procedures

---

## Week 6: Roslyn Analyzers + CLI Packaging + Docs + Release

### Goals
- Roslyn Analyzers for IDE integration
- CLI packaging as dotnet tool
- Documentation, ADRs, release automation

### Tasks

| Task | Description | Acceptance Criteria |
|------|-------------|---------------------|
| 6.1 | Implement `FromSqlRawAnalyzer` - SyntaxNodeAnalyzer on InvocationExpression | Diagnostics appear in IDE on FromSqlRaw calls |
| 6.2 | Implement `ExecuteSqlRawAnalyzer` | Diagnostics on ExecuteSqlRaw |
| 6.3 | Implement `DapperQueryAnalyzer` (optional) | Diagnostics on Dapper.Query<T> |
| 6.4 | Package `DataGuard.Analyzers` as NuGet analyzer package | `dotnet add package` adds analyzers to project |
| 6.5 | Finalize `DataGuard.Cli` as dotnet tool (sign, pack) | `dotnet tool install -g DataGuard.Cli` works |
| 6.6 | NuGet package signing with Sigstore | `dotnet nuget sign` works, packages verified |
| 6.7 | Publish to NuGet.org (Core, SqlServer, Oracle, Analyzers, Cli) | All 5 packages on NuGet.org |
| 6.8 | GitHub Actions release workflow (tag → build → sign → publish) | Tag push triggers full release |
| 6.9 | Write documentation: README, gap-analysis, architecture, ADRs | All docs in `docs/` |
| 6.10 | Write Anthropic grant pitch (README + pitch doc) | Ready for submission |

### Deliverable
- All 5 NuGet packages published
- `dotnet tool install -g DataGuard.Cli` works
- Roslyn analyzers work in IDE
- Documentation complete

---

## Test Strategy Summary

### Test Pyramid

| Layer | Coverage | Tool |
|-------|----------|------|
| Unit | Rules, diff logic, naming normalization | xUnit, mock ground truth |
| Integration | SQL Server (Testcontainers), Oracle (Testcontainers + gvenzl/oracle-xe) | xUnit, Testcontainers |
| Golden Corpus | H1/H2/H3/Length regression (periodic) | xUnit, asserts exact diagnostic codes |

### Golden Corpus Targets
| Category | Target Detection Rate |
|----------|----------------------|
| H1 Phantom Identifiers | >95% |
| H2 Column/Table Mismatch | >90% |
| H3 Dialect Confusion | >80% |
| Length Mismatch | >95% |

---

## CI/CD Pipeline

```yaml
# .github/workflows/ci.yml
on: [push, pull_request]
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - dotnet restore
      - dotnet build --no-restore
      - dotnet test --no-build --collect:"XPlat Code Coverage"
  
  test-sqlserver:
    runs-on: ubuntu-latest
    services:
      sqlserver:
        image: mcr.microsoft.com/mssql/server:2022-latest
    steps:
      - dotnet test DataGuard.SqlServer.Adapter.Tests
  
  test-oracle:
    runs-on: ubuntu-latest
    steps:
      - uses: docker://gvenzl/oracle-xe:21-slim
      - dotnet test DataGuard.Oracle.Adapter.Tests
  
  test-golden-corpus:
    runs-on: ubuntu-latest
    if: schedule || manual
    steps:
      - dotnet test --filter "Category=GoldenCorpus"
```

---

## Risk Mitigation per Sprint

| Sprint | Risk | Mitigation |
|--------|------|------------|
| 1 | EF Core `IModel` API complexity | Start with ModelSnapshot.cs parsing (more stable) |
| 1 | ScriptDOM learning curve | Use existing EFCorePowerTools examples as reference |
| 2 | Baseline file format stability | Version baseline file from v1, schema migration later |
| 3 | Oracle CI permissions | Use Testcontainers (no external creds needed) |
| 3 | Oracle license in CI | Testcontainers + gvenzl/oracle-xe = free, no license |
| 4 | NVARCHAR2(2000) fallback logic | Pin Oracle.ManagedDataAccess.Core version; test against multiple EF Core versions |
| 4 | Byte/char semantics | Read NLS_LENGTH_SEMANTICS at runtime; test with Vietnamese data |
| 5 | Roslyn analyzer performance | Use incremental generators; syntax-only in IDE |
| 6 | NuGet signing complexity | Use Sigstore keyless signing; automate in release workflow |

---

## Definition of Done (Per Module)

- [ ] All public APIs have XML docs
- [ ] Unit tests ≥ 90% coverage
- [ ] Integration tests pass on CI
- [ ] No Roslyn diagnostics in own code
- [ ] NuGet package builds, signs, publishes
- [ ] Documentation updated
- [ ] ADR written for any architectural decision

---

## Milestone Gates

| Milestone | Criteria | Go/No-Go |
|-----------|----------|----------|
| Week 1 End | POC detects parameter mismatch on sample | Go if compiles + 1 rule works |
| Week 2 End | Module 1 MVP validates SQL Server project | Go if 6 rules + SARIF + CLI work |
| Week 3 End | Oracle Adapter reads SP metadata | Go if ALL_ARGUMENTS + DESCRIBE work |
| Week 4-5 End | Length mismatch catches ORA-12899 cases | Go if golden corpus >90% detection |
| Week 6 End | All packages publish, tool installs | Go if `dotnet tool install` works end-to-end |

---

## Resources

- **EF Core Design-Time Services**: `IDesignTimeServices` for ModelSnapshot
- **ScriptDOM**: `Microsoft.SqlServer.TransactSql.ScriptDom`
- **Oracle Driver**: `Oracle.ManagedDataAccess.Core` (pin version)
- **Testcontainers**: `Testcontainers.Oracle`, `Testcontainers.MsSql`
- **Oracle XE Image**: `gvenzl/oracle-xe:21-slim`
- **SARIF SDK**: `Microsoft.CodeAnalysis.Sarif`
- **Sigstore**: `sigstore-dotnet` for keyless signing