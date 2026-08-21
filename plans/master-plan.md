> ⚠️ **SUPERSEDED (2026-08-21)** — Status board dưới đây đã lỗi thời từ 2025-01-18. Toàn bộ Phase 1-8 đã thực thi xong qua nhiều phiên (xem git log 2026-08 và `plans/ACTIVE_SESSION_REGISTER.md`). Tài liệu này chỉ còn giá trị lịch sử cho research files mapping và kiến trúc v3 ban đầu. Định hướng hiện tại: `plans/2026-08-21-review-handoff.md` (enterprise handoff) + `plans/adr/`.

# Master Plan: DataGuard - Entity ↔ SP/Raw SQL Contract Validator

**Generated**: 2025-01-18
**Source Research**: `/Volumes/Data/101.AI/GitHub/eco_support_net_oracle/research/muc_tieu/`
**Goal**: Build complete Entity ↔ SP/Raw SQL Contract Validator (DataGuard) as NuGet-distributed Roslyn Analyzer

---

## Phase Overview

| Phase | Skill | Description | Status |
|-------|-------|-------------|--------|
| 1 | `ck:research` | Deep technical research on all 5 research files | ✅ Done (partial) |
| 2 | `ck:brainstorm` | Architecture decisions, redteam council | ⏳ In Progress |
| 3 | `ck:plan` | Detailed implementation planning | ⏳ Pending |
| 4 | `ck:cook` | Core implementation (Modules 1-2) | ⏳ Pending |
| 5 | `ck:test` | Unit, integration, golden corpus tests | ⏳ Pending |
| 6 | `ck:security` | Security audit, supply chain hardening | ⏳ Pending |
| 7 | `ck:deploy` | NuGet publishing, CLI tool packaging | ⏳ Pending |
| 8 | `ck:docs` | Documentation, ADRs, gap analysis | ⏳ Pending |
| 9 | `ck:skill-creator` | Companion skill for marketplace | ⏳ Pending |

---

## Research Files to Process (in order)

| File | Description | Key Topics |
|------|-------------|------------|
| `1.md` | Main architecture + 3-layer design | Core architecture, T-SQL first approach, NuGet analyzer |
| `2.md` | Redteam Round 1 (5 experts) | IDE performance, DBA security, supply chain, OSS growth, versioning |
| `2.txt` | Implementation plan references | External plan references |
| `3.md` | Redteam Round 2 (5 new experts) | Legacy baseline, Testcontainers, edge cases, licensing, SARIF |
| `4.md` | Dev workflow + test corpus | Daily workflow, golden corpus taxonomy, AI-generated hallucination tests |
| `5.md` | Repo structure + roadmap | Module 1 (MVP), Module 2 (Oracle dialect + length mismatch), roadmap |

---

## Key Architectural Decisions (from research)

### Product Form
- **NuGet-distributed Roslyn Analyzer** (not VS Extension)
- Core: `DataGuard.Core` (MIT/Apache-2.0, no vendor deps)
- Oracle Adapter: `DataGuard.Oracle.Adapter` (optional, Oracle license)
- CLI Tool: `dotnet tool install DataGuard.Cli`

### Three-Layer Architecture (v3)
1. **C# Static Extraction** (Roslyn, no DB) - finds SP/raw SQL calls
2. **DB Ground Truth** (3 modes) - Full (DB live), Snapshot (JSON, default), Manual (attributes)
3. **Diff Engine + SARIF** - emits diagnostics, integrates GitHub/Azure DevOps

### Ground Truth Modes
1. **Full Mode** - CI connects to Oracle, auto-refreshes snapshot
2. **Snapshot Mode** (DEFAULT) - offline JSON file committed to repo, zero CI creds
3. **Manual Mode** - dev declares shape via attributes, zero DB access

### MVP Order
- v0.1: Core diff-engine + SQL Server (ScriptDOM, static, no DB)
- v0.2: Oracle (catalog-based, needs DB connection)
- v0.3: Length-mismatch check (both vendors)
- v0.4: Oracle dialect-check (optional)

### Critical Features
- **Baseline mechanism** (`dataguard baseline`) - freezes existing drift in legacy codebases
- **SARIF output** - GitHub/Azure DevOps native annotations
- **Testcontainers.Oracle + gvenzl/oracle-xe** for free CI testing
- **License separation** - Core MIT, Oracle Adapter optional

---

## Skill Execution Chain

### 1. ck:brainstorm - Architecture Decisions
- Validate v3 architecture with redteam council
- Finalize module boundaries
- Confirm SARIF + baseline + snapshot modes

### 2. ck:plan - Implementation Planning
- Detailed task breakdown for each module
- Sprint planning (6 weeks per research)
- Dependency graph between modules

### 3. ck:cook - Implementation
**Module 1 (MVP - 2-3 weeks)**
- `DataGuard.Core` - Abstractions, Sources, Rules, Reporting
- `DataGuard.SqlServer.Adapter` - ScriptDOM parser
- `DataGuard.Core.Tests` - Unit tests
- `DataGuard.Analyzers` - Roslyn analyzers for FromSqlRaw/ExecuteSqlRaw
- `DataGuard.Cli` - dotnet tool commands

**Module 2 - Oracle Extensions (3-4 weeks)**
- `DataGuard.Oracle.Adapter` - ALL_ARGUMENTS, ALL_TAB_COLUMNS, NLS_SESSION
- `DataGuard.Oracle.DialectChecker` - 5 dialect rules
- `DataGuard.Oracle.LengthMismatch` - 3 mismatch types + EfCoreInferenceSimulator
- Tests with Testcontainers.Oracle + gvenzl/oracle-xe

### 4. ck:test - Test Suite
- Unit tests (mock ground truth)
- Integration tests (Testcontainers Oracle XE)
- Golden corpus regression tests (H1/H2/H3 taxonomy)
- SARIF emission verification

### 5. ck:security - Security Audit
- Supply chain hardening
- Package signing, SBOM
- Credential handling
- Least privilege documentation

### 6. ck:deploy - NuGet Publishing
- Package signing with Sigstore
- `dotnet tool` packaging
- GitHub Actions workflow

### 7. ck:docs - Documentation
- Gap analysis (Microsoft issues)
- Architecture docs
- ADRs
- Dev workflow guide
- Anthropic grant pitch

### 8. ck:skill-creator - Marketplace Skill
- Companion skill for Claude marketplace

---

## Redteam Council Protocol

For any major decision:
1. Spawn 5-expert council (Compiler, DBA, DevSecOps, OSS Growth, Enterprise Architect)
2. Each attacks design from their domain
3. Synthesize upgrades → vN+1 architecture
4. Document in ADR

---

## Next Action

**Start Phase 2: ck:brainstorm** - Convene redteam council to validate v3 architecture and finalize module boundaries before implementation planning.