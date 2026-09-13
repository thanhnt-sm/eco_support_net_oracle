---
phase: 7
title: "Verification and closure"
status: in-progress
priority: P1
effort: "L"
dependencies: [6]
---

# Phase 7: Verification and closure

## Overview

**Full-delivery amendment:** read [scope amendment](scope-amendment.md) and [FC ledger](full-claims-ledger.md). Phase8 contracts precede source edits. This phase supplies foundation work only; legacy docs-only exclusions/claim removal cannot close a required feature. Any temporary safety removal must be followed by the mapped Phase9–14 implementation. Phase15, not Phase7, owns full closure. Preserve all safety and compatibility acceptance below.

Verify remediations with reproducible evidence and close only findings with required proof. The audit’s 484 C# passes are baseline observation, not proof database assertions ran. No production feature work belongs here.

## Requirements

- Record exact command, cwd, platform, exit, timestamp, assertion count and redacted artifact path in coordinator `reports/execution-evidence.md`; raw logs stay gitignored.
- Database tests explicitly skip with reason when unavailable or fail integration-required, never early-return as pass. Running tests emit executed fixture/connection/assertion marker.
- Verify schema migration/diff, four-provider capture selection, precedence, cancellation/atomicity, redaction, hook lifecycle, Roslyn output and VS Code containment against ledger AC IDs.
- Windows Visual Studio/VSSDK/DPAPI/path tests are separately reported; macOS cannot close V04/CI-08.
- Docs sync/link verification and ledger status change require exact evidence. F6 stays open unless owner disposition exists.

## Architecture

Exact command/run/evidence instructions: [verification runbook](reports/verification-runbook.md). These commands are planned, not already executed results.

Evidence tiers are deterministic unit/CLI, opt-in live integration with markers, and platform gates. Every finding maps to required tier; a green aggregate suite never substitutes for a marker proving DB/IDE branch execution.

## Related Code Files

- `DataGuard.sln`, `Directory.Build.props`, `.github/workflows/`, `scripts/verify_docs_sync.sh`
- `tests/DataGuard.Core.Tests/{CliExitCodeTests,SourceAndBaselineTests,SqlServerIntegrationTests,SqlServerParserIntegrationTests,CredentialManagerFullTests,DiagnosticEmitterFullTests,TelemetryTests,OracleAdapterTests,MySqlAdapterTests,PostgreSqlAdapterTests}.cs`
- `tests/DataGuard.{Analyzers,CodeFixes,GoldenCorpus}.Tests/`, `src/DataGuard.VSCode/`, `src/DataGuard.VisualStudio/`
- Audit limit `../260912-1936-luna-src-audit/reports/verification.md`; acceptance source `./findings-ledger.md` (V02–V05, F5).

## Implementation Steps

1. Freeze changed-file/finding-to-AC matrix; add focused tests only where pre-fix suite could have passed.
2. Run locked restore, release build and affected/full tests with per-project result names; record evidence fields required by ledger.
3. Replace integration silent returns with named skip or integration-required failure. With authorized isolated DB schemas, log provider/version/fixture/actual assertion count and assert changed DDL/metadata.
4. Exercise SQL Server/Oracle/PostgreSQL/MySQL selection and live capture, Oracle bind/REF CURSOR metadata and schema prerequisite; never call procedures merely for a marker.
5. Execute CLI precedence/drift/legacy/cancellation, Core atomic/bounds/telemetry/redaction, POSIX hook, Roslyn behavior and VS Code confinement/process cases.
6. On Windows run VSSDK/VSIX build plus Run/Cancel/timeout containment smoke and DPAPI/path cases. If unavailable, record `blocked_external` with prerequisites rather than infer pass.
7. Update `docs/07-testing/test-strategy{,.vi}.md` and current release-evidence wording in this batch with exact executed/blocked markers, then run `./scripts/verify_docs_sync.sh`, relevant actionlint/YAML checks and Docker smoke only when relevant artifacts changed; retain raw logs outside tracked root.
8. Sol reviews evidence matrix. Coordinator moves a ledger row only with its complete AC conjunction; aggregate counts cannot close it.

## Success Criteria

<!-- RT-13: accepted -->
Harnesses are implementation deliverables, not presumed existing tests. At CP1 assign proposed new `tests/DataGuard.Core.Tests/ProviderLiveIntegrationTests.cs` and shared opt-in fixture helper for Oracle/PG/MySQL, using isolated owner-authorized connection profiles; no new container packages until lockfile/dependency review. Define flags `DATAGUARD_RUN_ORACLE_INTEGRATION`, `DATAGUARD_RUN_POSTGRESQL_INTEGRATION`, `DATAGUARD_RUN_MYSQL_INTEGRATION` and corresponding `DATAGUARD_<PROVIDER>_TEST_CONNECTION` secret inputs. Required=1 with missing connection/failed setup must fail; disabled must report explicit skip using xUnit2-compatible discovery mechanism. Record actual provider/version/assertion markers.

Windows gate has two distinct deliverables: proposed `tests/DataGuard.VisualStudio.Tests/` seam project compatible with VSSDK target framework, and documented manual Visual Studio experimental-instance procedure (install exact built VSIX, open controlled solution, Run/Cancel/timeout/malformed and escaped SARIF with fake CLI, collect redacted log/screenshots/version). Seam build/test is not host smoke. No CI-host automation/workflow dispatch is assumed. Without host/environment evidence V04 remains blocked_external, even if VSIX packages successfully. Enabling any new external profile requires authorized isolated environment, never production connection reuse.

- [x] Build/test evidence is reproducible and redacted.
- [x] DB cases follow explicit skip/fail policy and have markers when executed.
- [x] Four-provider/safety regressions have focused evidence.
- [x] Windows gate is explicitly recorded as `blocked_external` with owner, prerequisite, and resume evidence; it is not inferred as passed.
- [ ] Docs sync passes; ledger closure is owner/evidence backed.

## Risk Assessment

DB/Windows availability may limit proof; retain open gates rather than weakening tests. Never commit generated verification artifacts unless policy explicitly calls for curated evidence.
