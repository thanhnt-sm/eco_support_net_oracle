---
phase: 3
title: "CLI snapshot and configuration"
status: in-progress
priority: P1
effort: "L"
dependencies: [2]
---

# Phase 3: CLI snapshot and configuration

## Overview

**Full-delivery amendment:** read [scope amendment](scope-amendment.md) and [FC ledger](full-claims-ledger.md). Phase8 contracts precede source edits. This phase supplies foundation work only; legacy docs-only exclusions/claim removal cannot close a required feature. Any temporary safety removal must be followed by the mapped Phase9–14 implementation. Phase15, not Phase7, owns full closure. Preserve all safety and compatibility acceptance below.

Make CLI configuration resolution and snapshot operations select a real source instead of overwriting configured values or self-comparing stored schema. Program.cs is Terra-only. It consumes Phase 1 CP1 inventory and Phase 2 named reader/catalog seams, not a generic provider factory.

## Requirements

- One precedence everywhere: explicit option > environment > YAML > product default. Resolve connection, provider, schema and package once, then pass immutable resolved config.
- Explicit empty/invalid values error rather than erase YAML; redact secrets in config display/logging.
- Persist/use `DefaultProvider`: `init --provider` writes it; explicit provider wins. Do not conflate with factory default removal.
- Snapshot refresh/diff must run a four-provider matrix (SQL Server, Oracle, MySQL, PostgreSQL). v3 includes `SchemaHashKind`, provider/scope/canonicalization version and canonical table/column identity, type, nullability, max/char length, precision/scale, char semantics, default and ordinal. Connected diff captures this fresh schema/hash. No connection is **UNEVALUATED**, never a green consistency pass; `--fail-on-drift` fails closed. Legacy violation diff is explicit deprecated opt-in with warning.
- Keep `validate --offline` Manual and assembly-required unless owner approves new mode; never silently reinterpret as Snapshot.
- Pass one CLI cancellation token through readers, rules, baseline and output. Phase 4 completes BaselineManager/telemetry resilience overlap.

## Architecture

`Program.cs` builds a `CliConfigurationResolver` command context before dispatch. Named SQL Server/Oracle/MySQL/PostgreSQL capture seams create read-only schemas used by refresh/live diff. Persisted snapshots are schemas, never executable artifacts. Preserve published exit numbers where possible, but release-note the removal of false-green offline diff semantics.

## Related Code Files

- `src/DataGuard.Cli/Program.cs` (Terra owner/integrator)
- `src/DataGuard.Core/{Models/Configuration,Baseline/BaselineManager,Security/CredentialManager,Telemetry/TelemetryCollector}.cs`
- `src/DataGuard.Cli/CliConfigurationResolver.cs` (new), `src/DataGuard.Core/Sources/SqlServerParsers.cs`, `src/DataGuard.{Oracle,MySql,PostgreSql}.Adapter/`
- `tests/DataGuard.Core.Tests/{CliExitCodeTests,AuditAndConfigTests,SourceAndBaselineTests,CredentialManagerFullTests}.cs`
- Findings CI-01–CI-03/05/09, CS-01/02/07/14, AD-01–AD-04, V06 in `reports/core-design.md`/ledger `./findings-ledger.md`.

## Implementation Steps

1. Inventory every Program.cs config reader; create single resolver and command-context test seam before behavior changes.
2. Test option > environment > YAML > default for all fields, including null/empty distinction and secret-safe `config show`.
3. Add configuration field and safe YAML round-trip for `DefaultProvider`; make init persist it and test option-overrides-config/default.
4. Replace direct `ConnectionString = connection` overlays for validate, baseline, oracle-check, refresh and diff with resolved values.
5. Use Phase-2 named SQL Server capture seam and adapter readers plus `ProviderRuleCatalog` for four-provider capture. Refresh writes v3 schema/hash; connected diff captures fresh schema; no connection returns UNEVALUATED/non-clean and `--fail-on-drift` fails closed; legacy violation diff needs explicit opt-in warning.
6. Add command tests for provider selection, DDL type/nullability/default/ordinal drift, env-only/YAML-only config, UNEVALUATED exit behavior and legacy opt-in. Mark DB cases integration-required.
7. Thread cancellation through reader/rule/baseline/output calls; test propagation and hand manager/telemetry edge handling to Phase 4.
8. Update affected EN/VI CLI/baseline/config/quickstart docs and release-note behavior in this batch: Manual offline, evaluated vs UNEVALUATED diff, precedence and default provider. Sol reviews; Phase 6 performs cross-doc reconciliation.
9. <!-- RT-14: accepted --> After resolver/schema context exists, wire Phase-2 pure catalog/readers and graph-level execution into Program.cs. Own AD-02 exact MY001–7 registration, AD-03 PG001–5 + live schema append, Oracle result-shape metadata and all status consumers. Phase 2 supplies primitives only; one Terra batch leases Program.cs. No circular phase gate.

## Selected exit contract

<!-- RT-15: accepted -->
Freeze this exact numeric contract at CP1 and pin per-command tests; release-note operational error 1→4 and new incomplete3. Usage2 includes missing required Manual assembly (previously1); no silent compatibility claim. If owner rejects behavioral migration, STOP/revise design before implementation; do not choose arbitrary numbers.

| Command/outcome | Exit |
|---|---|
| validate/oracle-check complete, no Error violations | 0 |
| validate/oracle-check complete with Error violations | 1 |
| assess complete with findings | 1 |
| assess complete without findings | 0 |
| snapshot diff complete, no drift | 0 |
| snapshot diff complete, drift and --fail-on-drift absent | 0 (explicit drift output) |
| snapshot diff complete, drift and --fail-on-drift present | 1 |
| any validation/diff/acquisition incomplete, unavailable, missing baseline or unsupported snapshot version | 3 |
| malformed/corrupt baseline, I/O/DB/operational failure or assessment ToolErrors | 4 |
| invalid args/config/provider/format or required option missing | 2 |
| cancellation requested / Ctrl-C | 130 |
| refresh/baseline/init successful operation | 0 |

Failure precedence: usage2 before execution; cancellation130 when cancellation is causal; operational4 over incomplete3 over findings1 over clean0. Same status in SARIF/evidence and IDE; exports with incomplete input do not publish normal success payload. Explicit legacy violation diff uses the complete drift rows only for violation-diff semantics, never DDL proof.

## Success Criteria

<!-- RT-01 RT-02: accepted -->
Additional mandatory flow work:

- Own `ValidationPipeline.CheckDriftAsync` and `DriftReport` alongside CLI diff. Add a detailed schema-capable drift API with explicit Complete/Missing/Corrupt/UnsupportedVersion/Unevaluated/Failed and hash-kind/source. Existing violation-only API remains explicitly violation-diff (not DDL evidence) and must fail closed if asked to assess incompatible structural baseline or missing/corrupt input. Never return `HasDrift=false` as a substitute for unknown. Test v1/v2/v3, no-baseline, malformed/unsupported versions and DDL change with zero new violations; preserve compiled consumer compatibility.
- `BuildContractsAsync` needs typed acquisition outcome Complete/Unavailable/Incomplete/Failed, not a bare list that erases missing-source state. Distinguish legitimately empty evaluated schema from no source/legacy Schema=null. Missing owner/schema/permissions and partial reads cannot become clean.
- Every validate output branch (text, SARIF, evidence, contracts, TypeScript; JSON is an assess format, not a validate format) checks acquisition/execution status before success/export. No pre-validation successful typed export from unknown input. Machine-readable non-success metadata/diagnostic and nonzero exit; suppress invalid normal export artifacts. Test all format branches, missing/corrupt snapshot, provider mismatch, denied/partial source and legitimate empty schema. Both IDEs consume this status in Phase 5.

- [x] No command null-overwrites resolved file/environment configuration.
- [ ] Every snapshot operation has tested four-provider source selection.
- [x] Connected diff reads current schema; no-connection result is UNEVALUATED/non-clean and legacy mode is explicit.
- [x] Provider default persists and follows precedence.
- [x] Cancellation reaches readers, rules, baseline and outputs.

## Risk Assessment

Precedence can alter automation behavior; cover legacy YAML and env-only inputs. Do not claim DB capture without executed integration markers. No HTTP health host or default fail-on-drift change.
