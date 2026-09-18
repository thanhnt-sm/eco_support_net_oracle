---
phase: 2
title: "Core and provider correctness"
status: completed
priority: P1
effort: "L"
dependencies: [1]
---

# Phase 2: Core and provider correctness

## Overview

**Full-delivery amendment:** read [scope amendment](scope-amendment.md) and [FC ledger](full-claims-ledger.md). Phase8 contracts precede source edits. This phase supplies foundation work only; legacy docs-only exclusions/claim removal cannot close a required feature. Any temporary safety removal must be followed by the mapped Phase9–14 implementation. Phase15, not Phase7, owns full closure. Preserve all safety and compatibility acceptance below.

Make Core pipeline, graph and pure adapter reader/catalog primitives correct. Phase 2 owns no Program.cs edits; all CLI composition, AD-02/03 registration reachability and status integration are Phase 3. The public and engine detailed APIs are implemented here and tested directly; real database proof is Phase 7. <!-- RT-14: accepted -->

Terra is sole writer; Sol advises/reviews. Terra owns all `Program.cs` integration.

## Requirements

- Make public pipeline and direct engine honor graph-level execution; expose the shared seam for Phase 3 CLI integration. Preserve dependency ordering and report truncation/dropped violations.
- Declared-but-unregistered dependency is a validation failure after registration completes; intentional late registration remains valid before validation.
- Select compiled trusted ModelSnapshot/IModel extraction only when explicitly opted in; validate trusted artifact boundary and never app/factory auto-execute. Source-only `ModelSnapshot.cs` remains unsupported with diagnostic, not a pretend JSON parse.
- APIs stay additive: retain signatures, constructors and deconstruction. Preserve the resolved/default-provider distinction from Phase 3 without inventing a new provider-factory API.
- Correct Oracle bind syntax and normal-validation metadata. REF CURSOR is metadata only when unknown; never execute procedures automatically.
- Characterize SQL Server browse-mode `is_hidden` before deciding whether filtering is a defect. Make raw SQL parse errors/status explicit, distinguish declaration from unavailable call-site type/direction, and never report unknown prerequisites clean.
- Reach MySQL MY003/MY005–MY007 and PostgreSQL PG004/PG005 through named `ProviderRuleCatalog`; add SQL Server schema capture seam and append PG live schema. PG004 must report unavailable without analyzer context; normalize Oracle byte/character semantics.

## Architecture

Rules execute graph levels: dependencies complete before next level; independent rules share `ConcurrentValidationEngine`. Its detailed result carries truncation metadata while current public returns stay compatible. `ProviderRuleCatalog` describes reachable rules; named provider readers and a SQL Server schema capture seam supply metadata without destructive procedure probing.

## Related Code Files

- `src/DataGuard.Core/{PublicApi/PublicApiSurface,Validation/ConcurrentValidationEngine,Rules/RuleDependencyGraph,Sources/EfModelSource,Models/Configuration}.cs`
- `src/DataGuard.Core/Sources/SqlServerParsers.cs` and the new named SQL Server schema-reader seam selected in `reports/core-design.md`
- `src/DataGuard.Oracle.Adapter/{OracleReaders,LengthMismatch}.cs`; `src/DataGuard.{MySql,PostgreSql}.Adapter/`; new `src/DataGuard.Cli/ProviderRuleCatalog.cs`
- `src/DataGuard.Cli/Program.cs` (read-only consumer inventory here; edits owned Phase 3)
- `tests/DataGuard.Core.Tests/{PublicApiAndPipelineTests,RuleDependencyGraphTests,CoverageExpansionTests,SourceAndBaselineTests,OracleAdapterTests,MySqlAdapterTests,PostgreSqlAdapterTests}.cs`
- Findings CE-01–CE-09 and AD-01–AD-05 in `reports/core-design.md`/Luna reports; ledger `./findings-ledger.md`.

## Implementation Steps

1. Reconfirm CP1 seams and selected core-design contracts; do not add a generic provider factory or unstated caller surface.
2. Retain dependency declarations separately from implementations; validate unresolved nodes after registration and test typo plus valid forward declaration.
3. Route public pipeline/direct engine through one graph-level executor; leave Program wiring for Phase 3. Respect enablement, degree, cap and cancellation; retain stable order and existing return signatures through additive detailed result API.
4. Return cap/truncation metadata so callers cannot report a truncated set as clean. Add serial/concurrent, cancellation and cap tests.
5. Replace misleading EF flow with selected compiled trusted artifact opt-in; source-only snapshot gives explicit unsupported diagnostic, never auto DbContext/app execution. Add trusted/missing/untrusted fixture tests.
6. Correct `ALL_ARGUMENTS` bind syntax; enrich normal Oracle descriptors with catalog-backed REF CURSOR/result metadata only. Never add automatic DBMS_SQL/procedure execution.
7. Characterize `is_hidden` in supported SQL Server browse mode before filtering; add named SQL Server schema capture seam, `ProviderRuleCatalog`, MY/PG capability/prerequisite tests, and explicit PG004 unavailable status.
8. Surface malformed raw SQL status and unknown call-site metadata; use nullable metadata as primary signal, cover duplicate columns, and update `docs/03-components/core/{sources,rules-engine,validation}{,.vi}.md` plus affected adapter EN/VI pages in this batch.
9. CE-08: parameterless `DataGuardApi.CreatePipeline()` uses smart default configuration; explicit configuration uses `WithSmartDefaults()` only when enabled. Preserve disabled opt-out and caller-owned values. Add default/opt-out/no-mutation tests in `PublicApiAndPipelineTests.cs`; update public-api EN/VI docs.

## Success Criteria

<!-- RT-10: accepted -->
Provider-to-v3 field map is normative; extend captures before hashing. Versioned column DTO keeps ProviderType, DefaultExpression and Ordinal separate (do not overload DataDefault with COLUMN_TYPE). All columns include name, nullability, lengths/precision/scale and char semantics where applicable; explicit NotApplicable differs from Unavailable. If required metadata cannot be read, capture is incomplete/unavailable, not a partial clean hash.

| Provider | Identity / ordinal | Actual default / type source |
|---|---|---|
| SQL Server | sys.schemas/tables/columns; column_id | sys.default_constraints.definition; sys.types + column facets |
| Oracle | ALL_TAB_COLUMNS owner/table/column/COLUMN_ID | DATA_DEFAULT; DATA_TYPE and BYTE/CHAR facets |
| PostgreSQL | information_schema.columns schema/table/column/ordinal_position | column_default; data_type/udt_name and facets |
| MySQL | INFORMATION_SCHEMA.COLUMNS database/table/column/ORDINAL_POSITION | COLUMN_DEFAULT distinct from COLUMN_TYPE/DATA_TYPE |

Read-only query design must preserve provider-specific default representation (literal versus expression when catalog distinguishes it). Test adapter→v3 conversion for each field and null/not-applicable/unavailable before CLI integration, including default-only/ordinal-only changes and whitespace inside literals.

<!-- RT-11: accepted -->
Add `RuleExecutionOutcome` envelope with rule ID, Evaluated/Unavailable/Skipped/Failed, prerequisite reason, violations and incomplete metadata without changing IContractRule signature. A capability adapter inventories effective rules, including no-op PG004 and Oracle DG012, and never infers Evaluated from empty violations. Define skipped-as-not-applicable versus required-input-unavailable; the latter makes requested validation incomplete, while legitimate not-applicable rules do not make every run fail. Propagate coverage/outcome through detailed public API, CLI, SARIF/evidence; test catalog membership vs actual evaluation separately.

<!-- RT-12: accepted -->
EF loader owns a collectible load context with dependency resolution rooted at the exact trusted artifact, sharing necessary host EF contract identity explicitly. Only exact ModelSnapshot type is instantiated; private/mismatched dependencies cause explicit failure, not DbContext discovery. Release model/reflection references before unload; test repeated load/dispose and Windows file replacement. In-process arbitrary code cannot have a guaranteed timeout/unload; state cooperative limits and stop/owner gate if hard isolation is required rather than silently add process hosting.

<!-- RT-09: accepted -->
Trust grant is explicit invocation/host API with exact assembly path + ModelSnapshot type, not repository YAML, environment auto-discovery or a directory wildcard. Validate artifact identity/path before load, prohibit DLL directory scanning and DbContext/host factory fallback, reject linked artifacts by policy; document in-process user code/module initializers cannot be sandboxed or forcibly cancelled. A trust flag is permission to execute that artifact, not proof it is safe. Set `OracleConfiguration.UseRefCursorDescribe` default false explicitly; ordinary commands ignore executable describe even if old YAML enables it. Advanced retained API requires separate authorized invocation/allowlist/typed samples; tests default construction and zero ordinary procedure calls.

- [x] Public-pipeline/direct-engine share graph-level semantics; CLI integration is explicitly pending Phase 3, so CE-02/09 cannot close before consumer AC there.
- [x] Missing dependencies, caps and cancellation are observable.
- [x] EF compiled artifact behavior and source-only unsupported status are explicit and safe.
- [x] Four provider paths have unit-tested registration/metadata behavior.
- [x] Oracle binds/REF CURSOR metadata are correct without procedure runs.

## Risk Assessment

<!-- RT-03: accepted -->
End-to-end overflow AC: detailed engine/result records status and known/unknown counts; legacy engine/pipeline `ValidateAsync` cannot return a partial clean list/result. Add a documented incomplete exception/adapter while preserving method signatures. CLI findings exit remains distinct from incomplete/operational failure; status must appear in SARIF invocation/notification or run properties and evidence, not only console. Phase 5 renders partial diagnostics as incomplete. Test old compiled consumer and an overflow where the only Error would have been omitted, baseline filtering removes all accepted violations, exactly N vs N+1, zero cap and cancelled work. No exact suppressed count if enumeration stopped.

Concurrency can alter ordering/result shape; preserve ordering and add metadata. Provider operations are DB-sensitive, so unit tests are not integration proof. Do not broaden into health HTTP, CVE service or IDE settings.
