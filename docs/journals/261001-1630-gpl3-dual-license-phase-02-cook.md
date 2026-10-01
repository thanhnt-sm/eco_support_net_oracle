# Cook of Phase 2 Legal text and metadata migration: All gates green, build-time analyzer and VSIX notices verified

**Date**: 2026-10-01 16:30
**Severity**: Low
**Component**: DataGuard Licensing, Packaging, Roslyn Analyzers, VS Extension, CLI, CI
**Status**: Resolved on branch `feat/gpl3-dual-license`; ready for owner review prior to push

**Plan:** `EXECUTE_PHASE_02_PLAN.md` · `plans/260930-1735-dataguard-gpl3-dual-license-migration/phase-02-legal-text-va-metadata-migration.md`
**Mode:** `/ck:cook --auto --tdd` · Tester subagent (`TesterSubagent`) → Code reviewer subagent (`CodeReviewerSubagent`, score 9.8/10, approved) → Finalize.

---

## 1. Executive Summary

Phase 2 of the DataGuard dual-licensing migration (`GPL-3.0-only` + Commercial starting from v0.4.0) has been fully executed, audited against the codebase, verified with automated end-to-end checks, reviewed by a dedicated review subagent, and signed off.

All 6 core deliverables from `EXECUTE_PHASE_02_PLAN.md` are complete:
1. **Root Legal Documents & Notices**: Verbatim GPL-3.0 text at `LICENSE`, historical MIT preserved at `docs/legal/MIT-v0.1.0-v0.3.0.txt`, Section 7 additional permissions at `docs/legal/ADDITIONAL-PERMISSIONS.md` (explicitly naming Oracle ODP.NET, Microsoft SNI, Visual Studio, and VS Code, with "not reviewed by a lawyer" disclaimer), and third-party notices at `docs/legal/THIRD-PARTY-NOTICES.md` with 6 byte-identical copies verified.
2. **Metadata Consolidation**: Centralized `PackageLicenseExpression=GPL-3.0-only`, `Authors`, and `Copyright` in `Directory.Build.props`; stripped redundant tags from 9 individual `.csproj` files; preserved permissive licensing on `DataGuard.Contracts.csproj` (D3 decision); added OCI license labels to `Dockerfile`.
3. **Analyzer Packaging (D4)**: `DataGuard.Analyzers.csproj` converted to build-time only (`IncludeBuildOutput=false`, `DevelopmentDependency=true`, assemblies hosted under `analyzers/dotnet/cs`), `IsPackable=false` set for `SqlClassification` and `LanguageServer`.
4. **Distribution Channel Notices**: CLI publish directory and nupkg bundle `LICENSE`, `THIRD-PARTY-NOTICES.md`, and `ADDITIONAL-PERMISSIONS.md`; Visual Studio VSIX bundles notices as `<Content>`; VS Code VSIX retains notices in `.vscodeignore`.
5. **Documentation & FAQ**: Dual licensing & FAQ section added to `README.md` and `README.vi.md`, `SUPPORT.md`, `CHANGELOG.md` with nightly v0.3.0 MIT snapshot.
6. **SPDX Consistency Gate**: Automated `scripts/check-license-consistency.py` and 44 Python unit tests in `scripts/tests/`.

---

## 2. Test & Verification Evidence

### Automated Test Suites
- **Python Unit Tests**: 44/44 passed (`scripts/tests/`).
- **SPDX Licence Consistency**: Scanned 193 files, 6 tracked copies verified identical, 0 unlisted licence occurrences.
- **NuGet & npm Licence Gate**: Checked 281 NuGet packages and 8 npm packages against `scripts/allowed-licences.txt` — OK.
- **Workflow Policy**: `check-workflow-policy.py` — OK.
- **Documentation Sync**: `verify_docs_sync.sh` — OK.
- **C# Unit Test Suites**:
  - `DataGuard.Analyzers.Tests`: 13/13 passed.
  - `DataGuard.CodeFixes.Tests`: 24/24 passed.
  - `DataGuard.GoldenCorpus.Tests`: 28/28 passed.
  - `DataGuard.VisualStudio.Tests`: 156/156 passed (1 skipped navigation mock test).
  - Total .NET tests: 221 passed, 0 failed, 1 skipped.

### Packaging & Artifact Inspection
- **NuGet Packages**: Exactly 13 `.nupkg` + 1 `.snupkg` produced via `dotnet pack DataGuard.CrossPlatform.slnf -c Release`.
  - 12 packages declare `<license type="expression">GPL-3.0-only</license>`.
  - `DataGuard.Contracts` declares permissive license expression (retained as in v0.3.0).
- **Analyzer Packaging Gate**: `scripts/verify-analyzer-packaging.sh` passed clean:
  - 0 `lib/` entries in package.
  - 3 DLLs in `analyzers/dotnet/cs` (`Analyzers`, `Contracts`, `SqlClassification`).
  - Consumer application builds cleanly, loads analyzer via `/analyzer:`, outputs 0 `DataGuard.Analyzers.dll`/`SqlClassification.dll` to `bin/`, and retains 1 `DataGuard.Contracts.dll` (via permissive Contracts dependency).
- **VSIX Packaging**: Visual Studio VSIX built via MSBuild Enterprise 18 (`src/DataGuard.VisualStudio/bin/Release/net472/DataGuard.VisualStudio.vsix`).
  - VSIX container inspect verified: `LICENSE.txt` (35,149 bytes), `THIRD-PARTY-NOTICES.md` (18,493 bytes), `ADDITIONAL-PERMISSIONS.md` (3,498 bytes).
  - Added `VsixPackage_WhenBuilt_ContainsLegalNotices` to `VsixAnalyzerPackagingTests.cs`: 4/4 tests pass against the live VSIX container artifact.
- **CLI Publish Directory & Tool nupkg**:
  - All 3 legal notice files confirmed present in `bin/cli_publish`.
  - Tool package confirmed bundling notices at root and under `tools/net9.0/any/`.

---

## 3. Enhancements Made During Cook Run

1. **`scripts/verify-analyzer-packaging.sh` Path Robustness**:
   - `NUPKG_DIR` resolved to an absolute path (`NUPKG_DIR="$(cd "$NUPKG_DIR" && pwd)"`) so running the script with relative paths like `bin/nupkg` functions correctly without NuGet local feed resolution errors.
2. **`VsixAnalyzerPackagingTests.cs` Permanent Regression Test**:
   - Added `VsixPackage_WhenBuilt_ContainsLegalNotices` asserting that `LICENSE.txt`, `THIRD-PARTY-NOTICES.md`, and `ADDITIONAL-PERMISSIONS.md` exist inside any built VSIX container.
3. **Plan State Sync**:
   - Updated `phase-02-legal-text-va-metadata-migration.md` to `status: completed` and checked off all 6 verified success criteria.
   - Updated `plans/260930-1735-dataguard-gpl3-dual-license-migration/plan.md` to mark Phase 2 `Completed`.

---

## 4. Subagent Delegation Audit

- **`TesterSubagent`**: Ran all gates and tests. 265 passed, 0 failed, 1 skipped.
- **`CodeReviewerSubagent`**: 9.8/10 score, 0 critical issues, Verdict: Approved.
- **Next Phase Dependency**: Phase 3 (`phase-03-ai-crawl-notice-rewrite.md`), Phase 4 (`phase-04-contribution-cla.md`), Phase 5 (`phase-05-vsix-signing.md`), Phase 6 (`phase-06-verification-adr-va-release-v0-4-0.md`).
- **Push Gate Reminder**: Owner local diff review (`git diff main...feat/gpl3-dual-license`) required before pushing to remote, as pushing publishes GPL-3.0 VSIX artifacts and attestation.
