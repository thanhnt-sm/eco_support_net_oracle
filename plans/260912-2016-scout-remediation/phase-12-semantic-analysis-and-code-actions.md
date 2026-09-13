---
phase: 12
title: "Semantic analysis and code actions"
status: in-progress
priority: P1
effort: "9d"
dependencies: [2, 5, 8]
---

# Phase 12: Semantic analysis and code actions

## Overview

Deliver the claimed CI semantic validation and actionable Roslyn fixes without putting database access in the compiler analyzer, executing dynamic application code, or manufacturing stored-procedure edits from ambiguous SQL. This phase resolves every claim through an implementation or a recorded CP8 owner contract; it does not silently choose “five providers” and discard the separate “twelve providers” claim.

## Requirements

- Implement the heavy build capability promised by `docs/03-components/tooling/analyzers.md:3,14-17,127-156,200-206`, `docs/01-overview/feature-showcase.md:207-218`, and `docs/02-architecture/design-philosophy.md:198-227`.
- Implement actual C# EF `ModelSnapshot.cs` extraction promised by `docs/03-components/core/sources.md:84-109` and `design-philosophy.md:31-35`, without build/assembly execution by default.
- Implement the code-fix/provider contracts in `docs/03-components/tooling/code-fixes.md:3,18-189`; reconcile provider/action counts and feature-showcase facade claims through CP8 rather than silently downgrading a claim.
- Cover YAML/config, CLI wizard, hook installer, request/body-parser, and source-to-rule entry points recorded in the main claims ledger whenever they participate in this semantic/build contract.

## Architecture

**XR08 — operator-owned live build authority:** `DataGuardEnableDatabaseValidation` in project/imported properties is a request, not authorization. The MSBuild task has no credential resolver or network client. An explicit operator-launched CLI preflight, outside project execution, acquires approved metadata for an allowlisted target/provider and writes a bounded manifest; build consumes that manifest offline. CLI flags/environment/config alone inside MSBuild cannot grant authority. If a later broker design is chosen, CP8 must prove authenticated out-of-workspace grants and target binding before enabling it. Hostile-project fixtures with checked-in props/config must yield zero task DB/network calls. This prevents DataGuard's confused-deputy behavior, not arbitrary execution already inherent in untrusted MSBuild projects; never claim building an untrusted project is sandboxed.

```text
operator CLI preflight -> bounded hash-bound manifest -> offline MSBuild task -> SARIF/findings -> MSBuild diagnostics
Roslyn analyzer -> shared local classifier + AdditionalFiles metadata -> safe local diagnostics/actions
EF C# ModelSnapshot -> Roslyn syntax parser -> descriptors + extraction diagnostics -> explicit CLI source selector
```

- Create a build integration package/targets plus task/adapter. The existing netstandard analyzer stays local/semantic-light; it never opens a DB.
- Full validation uses the XR08 operator-launched preflight and offline manifest. `DataGuardEnableDatabaseValidation=true` cannot itself authorize live access. Build consumes offline AdditionalFiles contracts/snapshot only; it must not enumerate arbitrary routines.
- Add an EF C# snapshot parser for a bounded supported fluent-API subset: entity/table/schema, property CLR type/name/type/max length/nullability, and keys. Dynamic expressions, arbitrary method invocation, unsupported syntax, and unreadable metadata return structured extraction diagnostics and fail the selected strict mode—never empty success and never execution.
- Add an additive `DesignTimeExtractionResult`/options facade while preserving current public methods. Built-assembly context instantiation becomes explicit opt-in only.
- All code actions consume verifier-supplied diagnostic properties or an explicit AdditionalFiles contract manifest. Ambiguous DB/routine identity yields a disabled action with reason and is not counted as delivered remediation.

## Related Code Files

- XR04: create `src/DataGuard.SqlClassification/` in this phase, with a netstandard-compatible source-text/URI/version/span/rule contract, no DB/network/plugin dependency, shared golden fixtures and generator migration. Phase11 consumes this artifact; it does not create a competing classifier. Freeze exact framework/API at CP8.

- Create: `src/DataGuard.Build/` targets/task/adapter and integration-fixture project; `src/DataGuard.Core/Sources/ModelSnapshotCSharpParser.cs` plus extraction result/options types.
- Modify: `src/DataGuard.Analyzers/Analyzers.cs`, `src/DataGuard.CodeFixes/CodeFixProviders.cs`, `src/DataGuard.Contracts/ContractAttributes.cs`, `src/DataGuard.Core/Sources/EfModelSource.cs`, `src/DataGuard.Cli/Program.cs`, and relevant solution/project manifests.
- Modify: `src/DataGuard.Cli/Hooks/PreCommitHookInstaller.cs`, config/YAML parsing and CLI wizard seams only for ledger-owned contract tests; do not bundle unrelated command redesign.
- Test: `tests/DataGuard.Analyzers.Tests/`, `tests/DataGuard.CodeFixes.Tests/`, `tests/DataGuard.Core.Tests/`, a new build-integration fixture, and CLI focused tests.

## Implementation Steps

1. CP8 owner decision: define canonical diagnostic/action/provider accounting. The plan must enumerate all claimed “12 providers” and “five providers”; each becomes an implemented exported provider, an implemented alias/facade, or a named contradictory claim gate with owner/date. Never silently pick five.
2. Define an offline AdditionalFiles contract manifest with schema version, provider, routine identity, expected parameters/result shape, source location, hash, and redacted evidence. Validate schema/bounds before analyzer/task use.
3. Build `DataGuard.Build` target/task to run offline contracts and consume the explicit XR08 preflight artifact. Convert findings to stable MSBuild diagnostics, preserve SARIF, and fail closed on unavailable selected metadata. Never start credential-bearing CLI live work from project-controlled properties.
4. Implement C# ModelSnapshot parsing with Roslyn syntax, cancellation, file-size/node limits, and explicit supported patterns. Add `validate --ef-project`/context source selection so the safe parser is user-reachable.
5. Complete provider classes/actions: primary, MaxLength, Skip, Naming Convention, and UseOracle are minimum named classes. Each advertised diagnostic has a registration/action/application test; remove no claim by merely shrinking ID metadata.
6. Implement compatibility facades only after CP8 namespace approval: `DataContract`, `SqlParameter`, `ResultSet`, and `DataGuard.Validate`. Resolve collision with BCL `System.Runtime.Serialization.DataContractAttribute`, current `ExpectedColumn`/`ExpectedSpParameter`, and namespace import rules through explicit fully qualified/generated usage and compiler tests.
7. Make automatic SQL/procedure edits conditional on verified manifest identity. For missing identity/type/direction, expose a review-only explanation and leave code unchanged; this path is a safety outcome, not a completed auto-fix.
8. Add ledger tests for YAML configuration, CLI wizard selection, hook body/content, and any body-parser/request paths that feed semantic contract data, so source facts cannot be lost before the build/action layers.

## Tests Before

- Construct always-offline build fixtures for missing metadata, bad manifest and selected failure severity; separately test operator-launched preflight. Hostile project props, Directory.Build imports, workspace YAML, restore/design-time build and untrusted CI input alone cause zero DataGuard network/DB/secret resolution.
- Add realistic generated C# `ModelSnapshot` fixtures from supported EF patterns; include unsupported/dynamic patterns and assert diagnostics, not empty descriptors.
- Build Roslyn action matrix: diagnostic -> registration -> selected action -> applied document -> compilation; include Fix All, duplicate prevention, null root, marker suppression, and incompatible manifest.
- Add facade namespace-collision compilation fixtures before shipping any compatibility attribute/API.

## Success Criteria

- [x] All DataGuard MSBuild task paths remain offline without credential resolution. Separately invoked operator CLI preflight is observable, credential-safe, target-allowlisted and produces a bounded hash-bound manifest; project/imported/config properties cannot authorize it.
- [x] Offline AdditionalFiles contract validation emits stable MSBuild diagnostics for supported DG002–DG016 semantics; packed-consumer tests prove errors fail the build and warnings remain visible.
- [x] C# `ModelSnapshot.cs` parsing yields expected descriptors without build or context instantiation; unknown constructs produce a visible strict-mode failure/diagnostic.
- [ ] XR05: every required FC09 occurrence has an executable, compile-valid transformation and auditable provider/action accounting. Unresolved taxonomy/semantics is blocked_owner and prevents Phase12/FC09 success; review-only explanations, comments and non-delivery decisions never count as delivered fixes.
- [x] No action treats arbitrary SQL as a procedure or invents type/direction metadata.
- [x] YAML/wizard/hooks/body-parser pathways that create semantic inputs have before/after regression coverage (YAML export round-trip, CLI wizard output, and managed hook install/status/uninstall are covered; no body-parser path is part of the shipped semantic-input surface).

## Risk Assessment

- MSBuild diagnostic import must avoid duplicate compiler diagnostics and must not leak connection data into binlog/SARIF.
- Compatibility attributes can collide with BCL names and alter serialization semantics; CP8 approval plus compiler fixtures are mandatory.
- EF snapshots vary by EF version. Bounded syntax support plus explicit diagnostics is safer than execution or heuristic emptiness.
- Full build validation consumes offline evidence. Project/imported/config properties never authorize live DB access; only the separately operator-launched preflight crosses that boundary.
