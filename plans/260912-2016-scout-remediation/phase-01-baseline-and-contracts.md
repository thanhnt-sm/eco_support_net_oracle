---
phase: 1
title: "Baseline and contracts"
status: completed
priority: P1
effort: "M"
dependencies: []
---

# Phase 1: Baseline and contracts

## Overview

**Full-delivery amendment:** read [scope amendment](scope-amendment.md) and [FC ledger](full-claims-ledger.md). Phase8 contracts precede source edits. This phase supplies foundation work only; legacy docs-only exclusions/claim removal cannot close a required feature. Any temporary safety removal must be followed by the mapped Phase9–14 implementation. Phase15, not Phase7, owns full closure. Preserve all safety and compatibility acceptance below.

Establish the execution baseline, evidence ledger and CP0/CP1 contract inventory for later remediation. This phase is planning/verification setup only: it does not implement `BaselineManager`, schema v3, atomic persistence, or production fixes. It covers no-change/history evidence CS-03/F3 and prepares the contract boundaries for CS-01/02/14 and CI-01.

Terra is the sole writer throughout this program. Sol advises/reviews; it does not authorize another writer.

## Requirements

- Capture CP0 tree fingerprint, dirty/untracked inputs and authoritative baseline commands without changing source or interpreting a green suite as DB/IDE proof.
- Create CP1 caller/contract inventory for exact seams: `Program.cs` command handlers; `PublicApiSurface`; `BaselineManager`; config/YAML records; provider rule catalog; all affected public constructors/methods/deconstruction and persisted fixtures.
- Record selected design contracts, not alternatives: v3 schema snapshot/legacy distinction (Phase 3), atomic bounded baseline persistence/telemetry (Phase 4), compiled trusted ModelSnapshot opt-in (Phase 2), and local-only safety boundaries.
- Preserve 484-pass and explicit DB/Windows limitations as baseline evidence; retain CS-03 probe and F3 historical material without source cleanup.

## Architecture

CP0 establishes reproducibility; CP1 establishes a bounded change map before implementation. `reports/core-design.md`, `reports/safety-design.md`, `research/solution-research.md`, and `findings-ledger.md` are the decision inputs. The later phases implement their assigned contracts without guessing a new public surface.

## Related Code Files

- `DataGuard.sln`, `Directory.Build.props`, `src/DataGuard.Cli/Program.cs`
- `src/DataGuard.Core/{Baseline/BaselineManager.cs,Models/Configuration.cs,PublicApi/PublicApiSurface.cs}`
- `tests/DataGuard.Core.Tests/{CliExitCodeTests,SourceAndBaselineTests,PublicApiAndPipelineTests}.cs`
- `./{findings-ledger.md,reports/core-design.md,reports/safety-design.md,research/solution-research.md}`
- Findings CS-03, F3, V02–V05; contract inventory for CS-01/02/14, CE-01/02/04/08/09 and CI-01–CI-03/09.

## Implementation Steps

1. Freeze CP0 HEAD/diff/untracked fingerprint and record the baseline build/test/docs results plus their exact known limitations in `reports/execution-evidence.md` per ledger format.
2. Build CP1 caller inventory: list each Program command, public API entry, configuration parser/serializer, baseline entry, provider registration and test seam, including existing signature/constructor/deconstruction and serialized-fixture compatibility checks.
3. Link each ledger AC to exactly one owner phase and evidence tier; designate CS-01 as CI-01 alias, preserve CS-03 no-change probe, and prevent F3/F6 cleanup inference.
4. Record the selected contract details from core/safety design in phase-local implementation checklists; record no alternative “parse or reject”/warning-or-error choices.
5. Update the affected EN/VI testing/baseline evidence pages in this batch—`docs/07-testing/test-strategy{,.vi}.md`, `docs/03-components/core/baseline{,.vi}.md`, and `docs/product-discovery/release-evidence.md`—to label baseline results/limits, without claiming future fixes.
6. Have Sol red-team the CP1 inventory, then freeze it before Phase 2 implementation; changes afterward require ledger evidence and Terra update.

## Red-team amendments

<!-- RT-01 RT-02 RT-03: accepted -->
CP1 explicitly inventories `ValidationPipeline.CheckDriftAsync`, `DriftReport.HasDrift`, `ValidationResult.IsClean/HasErrors`, engine `ValidateAsync`, all CLI format branches and both IDE completion handlers. Freeze execution/acquisition/drift statuses before edits. Unknown, corrupt, unsupported, incomplete and cancelled are not clean. Preserve ABI with additive detailed APIs; legacy methods must fail closed with a documented controlled exception when their old shape cannot represent a non-success outcome. No new mandatory argument or changed return type.

<!-- RT-03 RT-12: compatibility/lifetime detail accepted -->
Keep existing primary constructors/deconstruction unchanged for DataGuardConfiguration, BaselineFile, SnapshotColumn/Table, StoredProcedureDescriptor and ValidationResult. DefaultProvider may be a non-positional init property with YAML/JSON proof; new schema fields use versioned v3 DTOs/converters and detailed execution/rule outcomes use new envelopes. Mandatory compiled-against-baseline consumer fixture runs unchanged against modified assembly; current source rebuild is insufficient. Inventory `WithPlugins` → manager/rule ownership → pipeline disposal and exact EF artifact resolution/lifetime as CP1 seams.

## Success Criteria

- [x] CP0 fingerprint and baseline commands/limitations are durable and redacted.
- [x] CP1 enumerates exact callers/seams/public compatibility checks, not “all callers”.
- [x] Every ledger AC has one phase/evidence tier; aliases/no-change/history remain visible.
- [ ] Only plan/evidence and explicitly identified current-doc updates occur; no production source, test implementation, workflow or cleanup change.

### Sol Acceptance (2026-09-18)

CP1 continuation gate: **GO** — prospective only; the historical pre-Phase-2 ordering breach
remains preserved and is not retroactively approved. The tracked-diff scoped hash, precise caller
and ABI seams (including configuration, legacy engines, `SnapshotColumn`, `BaselineFile` and
`StoredProcedureDescriptor`), explicitly excluded untracked build artifacts, and bound
compiled-consumer coverage artifacts satisfy the foundation boundaries for future edits.


## Risk Assessment

The risk is prematurely designing from incomplete caller knowledge. CP1 prevents source-contract guessing; later phases must revisit inventory when adding a seam. No result count proves DB/IDE execution.
