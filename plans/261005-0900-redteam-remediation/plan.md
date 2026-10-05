---
title: "Red-team remediation: wire the MVP core, close every finding of redteam-261004-1500"
description: "Execution plan that resolves all 23 recommendations and every Critical/High/Medium finding in plans/reports/redteam-261004-1500-source-vs-original-goals.md"
status: in-progress
priority: P0
effort: 6 phases
branch: docs/redteam-261004-source-vs-goals
tags: [red-team, remediation, mvp-core, ci, tests]
blockedBy: []
blocks: []
created: 2026-10-05
---

# Red-team remediation plan

## Overview

Source report: `plans/reports/redteam-261004-1500-source-vs-original-goals.md` (HEAD `046f91d`). Baseline measured in this session with .NET SDK 9.0.318 installed in the sandbox: `dotnet build DataGuard.CrossPlatform.slnf -c Release -p:RunAnalyzers=true` 0 warnings / 0 errors; `dotnet test` 1007 passed, 5 skipped, 0 failed. Docker daemon is available, so Testcontainers can run in this session.

Goal: every item in §3 (Critical C1–C6, High H1–H15, Medium list) and §6 (recommendations 1–23) of the report has one of three outcomes recorded in this plan: **Fixed** (code + test + verification), **Fixed differently** (with rationale), or **Deferred with owner decision** (only where a fix needs a decision outside the repo, such as license text or a signing key). Nothing is silently dropped.

## Red-team of this plan (risks to the remediation itself)

| # | Risk | Likelihood | Mitigation built into the phases |
|---|---|---|---|
| R1 | Fixing DG015/DG005 removes false positives but the golden corpus never had negative cases, so a regression toward false negatives would be invisible | High | Phase 2 adds negative corpus cases and a minimum-count assertion **before** Phase 3 touches the rules again; Phase 1 fixes ship with their own positive+negative unit tests |
| R2 | Wiring DG002/DG003/DG101 to real catalog data turns previously dead rules on; on real repos they will produce findings that users did not see before and may be wrong | High | Phase 3 ships the matcher behind a provider-aware type table with explicit "unknown type ⇒ no finding" and routes new findings through severity Warning for one release; e2e tests use a fixture project plus a fake catalog |
| R3 | Changing `--offline` to mean snapshot breaks the IDE-safe handshake and VS/VS Code extensions that pass `--offline --assembly` | Medium | Phase 1 keeps `--offline --assembly` ⇒ Manual (unchanged) and only maps bare `--offline` ⇒ Snapshot; IdeSafe tests must stay green; CHANGELOG notes it |
| R4 | Blocking nightly/build_release on `dotnet test` makes nightly red for days if a flaky test exists | Medium | Phase 2 first converts the five silent-return integration tests to real skips, then wires the live-DB job as `needs`; the live-DB job uses the same images as the fixtures (`gvenzl/oracle-free:23-slim-faststart`, `mssql/server:2022-latest`) |
| R5 | Extracting SQL Server out of Core changes NuGet package shapes (`DataGuard.Core` loses SqlClient/ScriptDom); consumers of the library API that relied on transitive refs break | Medium | Phase 4 keeps the public types' names and namespaces, adds `DataGuard.SqlServer.Adapter` as a `ProjectReference` from `Cli`/tests, regenerates every `packages.lock.json`, and documents the change in CHANGELOG + `BinaryCompatibilityFixture` is built in CI |
| R6 | Baseline fingerprint v2 invalidates every existing `.dataguard-baseline.json` | High | Phase 3 reads legacy `RuleId:Message` entries as a fallback match for one major, writes v2 fingerprints on next `baseline` run, and `validate` prints a one-line migration hint |
| R7 | Oracle catalog fixes cannot be proven without a real Oracle | Medium | Docker works in this session; Phase 3 runs `OracleIntegrationTests` with `DATAGUARD_REQUIRE_LIVE_RELATIONAL=1` and adds a package + overload + 0-arg fixture; if the image cannot be pulled, the phase records that explicitly |
| R8 | Splitting `Program.cs` while other phases edit it causes merge churn | High | The split is the **last** code phase (Phase 5), after all behavioral edits land |
| R9 | License/legal items (GPL §7 text, commercial terms) and signing keys are owner decisions | Certain | Recorded as Deferred with the exact decision needed; no code pretends otherwise |

## Decisions taken for the plan

| Topic | Decision |
|---|---|
| Unavailable rules (C2) | A rule registered `Unavailable` is reported once on stderr and recorded as an `Unavailable` outcome; it never sets exit 3 unless `--fail-on-unavailable` (new flag) or config `FailOnUnavailableRules: true`. `--skip-rules` is applied before the unavailable check |
| DG016 collision (H11) | `RawSqlParseStatusRule` moves to **DG019** (free ID). `PhantomIdentifierRule` splits into `PhantomTableRule` (DG015) and `PhantomColumnRule` (DG016) sharing one internal analyzer, so `--skip-rules` is exact |
| Phantom detection (12) | SQL Server provider uses ScriptDOM AST scope resolution; other providers use a hardened tokenizer (comments/literals masked, paren-aware select list, CTE list, `FROM` inside `EXTRACT/TRIM/IS DISTINCT FROM`, TVF, temp/table-var, cross-db, DUAL/sys objects, union-of-tables for unqualified JOIN columns). AST for non-T-SQL dialects is out of scope and documented |
| SP matching (8) | New `StoredProcedureCallMatchRule` owns DG101 (count), feeds DG002 (type) and DG003 (direction) through a shared resolution result. Type compatibility is an `ITypeCompatibility` per provider injected by `ProviderRuleCatalog`; unknown CLR or DB type ⇒ no finding |
| Snapshot v2 (10) | `BaselineFile` gains `StoredProcedures`, `LengthSemantics`, `Charset`; format version bumps to 3; `validate` reads `.dataguard-snapshot.json` by default, verifies provider + hash kind, warns on major.minor DB-version drift, errors (exit 3) on provider mismatch |
| Core/SqlServer split (16) | `SqlServerParsers.cs`, `SqlServerLiveQuerySchemaProvider` and `RawSqlParser` move to `DataGuard.SqlServer.Adapter`; Core drops `Microsoft.Data.SqlClient` and `ScriptDom`. `AWSSDK.SecretsManager` stays in Core only if the AWS secret store cannot be isolated in one phase; otherwise it moves behind a reflection-free provider in `DataGuard.Cli` |
| Analyzer (18) | `ContractValidationAnalyzer` becomes a syntax-only `RegisterSyntaxNodeAction` analyzer (no `IOperation`, no `SemanticModel`); generator model drops `Location` in favor of `(path, start, length)`; `FromSqlRaw` receiver is matched syntactically on `DbSet` member access or any `.FromSqlRaw(` call |
| Credentials (21) | CLI resolves the connection through `ZeroTrustCredentialProvider` when `--connection` is absent; new `--connection-env NAME`; `--connection` prints a deprecation warning. Audit log gains HMAC when `DATAGUARD_AUDIT_KEY` is set and a single writer path |
| Out-of-scope components (22) | One ADR per component with `keep | freeze | extract` recommendation; no deletion in this plan (governance rule 4) |

## Phases

| Phase | Name | Status |
|-------|------|--------|
| 1 | [Stop the bleeding: CLI gates and rule correctness](./phase-01-stop-the-bleeding.md) | Completed (6b8a991, e4b7eff, 713781f) |
| 2 | [Test and CI integrity](./phase-02-test-and-ci-integrity.md) | Completed (fa00c74, d74ef86, 3d7a720, 1fbcfc9) |
| 3 | [Wire the MVP core](./phase-03-wire-the-mvp-core.md) | Completed (bd6bd23, b68c5cf, 91a7822, b1a78c0, e498afa, 64e4c74; merged at 979ef18) |
| 4 | [Architecture: adapters, pipeline, analyzer, credentials](./phase-04-architecture.md) | Completed (9d81238, 02103a5, ae4aabd, f3b03fb; merged at e797cfe) |
| 5 | [Hygiene: dead code, god files, governance, docs](./phase-05-hygiene-and-docs.md) | In progress (5A merged: 4a814f6, b79faba) |
| 6 | [Verification, ADRs, journal, PR](./phase-06-verification-and-handoff.md) | In progress (ADRs merged: a6a2a66) |

## Traceability: report item → phase

| Report item | Phase |
|---|---|
| C1 SP contract never validated · rec 8 | 3 |
| C2 Oracle/PG always exit 3 · rec 1 | 1 |
| C3 DG015 SQL Server phantom · rec 2, 12 | 1 (key fix), 3 (AST) |
| C4 empty PASS gate · rec 5 | 1 |
| C5 DG005 inverted · rec 3 | 1 |
| C6 Oracle package catalog · rec 9 | 3 |
| H1/H2 fabricated columns, inconsistent DB failure · rec 15 | 3 |
| H3 baseline fingerprint · rec 11 | 3 |
| H4 snapshot integrity/version · rec 10 | 3 |
| H5 DG013 self-inflicted · rec 4 | 1 |
| H6 type map · rec 8 | 3 |
| H7/H8 length semantics · rec 14 | 3 |
| H9 extractor FN/FP · rec 13 | 3 |
| H10 phantom FP family · rec 12 | 1, 3 |
| H11 DG016 collision | 1 |
| H12 false-green integration · rec 6 | 2 |
| H13/H14/H15 workflows · rec 7 | 2 |
| Medium: two pipelines, plugin metadata, graph, credentials, manual LoadFrom, acquisition swallow, catalogs PG/MySQL/SQLServer, SqlClassifier, DG001, god files, dead code, tests, governance | 3, 4, 5 |
| rec 16 Core/SqlServer split · 17 single pipeline · 18 analyzer · 21 credentials | 4 |
| rec 19 golden corpus · tests | 2 |
| rec 20 god files, dead code · 23 docs | 5 |
| rec 22 ADRs | 6 |

## Verification protocol (every phase)

1. `dotnet build DataGuard.CrossPlatform.slnf -c Release -p:RunAnalyzers=true` → 0 warnings.
2. `dotnet format DataGuard.CrossPlatform.slnf --verify-no-changes`.
3. `dotnet test DataGuard.CrossPlatform.slnf -c Release --no-build` → 0 failed; skipped count must be explained.
4. `python3 scripts/check-workflow-policy.py`, `python3 -m unittest discover -s scripts/tests`, `./scripts/verify_docs_sync.sh`.
5. Phase 3 additionally: `DATAGUARD_REQUIRE_LIVE_RELATIONAL=1 dotnet test --filter Category=LiveDb`.
6. Phase 6: full suite, coverage ≥ 60 %, `BinaryCompatibilityFixture` builds, e2e `validate` on `samples/` for all four providers in snapshot mode.
