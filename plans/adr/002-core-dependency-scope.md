# ADR-002: Core Dependency Scope & Analyzer Layering

**Date**: 2026-08-21
**Status**: Accepted
**Supersedes**: ADR-001 claim that DataGuard.Core is "zero vendor dependencies"

## Context

Redteam review (2026-08-21) found three contradictory facts:

1. `DataGuard.Core` references 15+ vendor packages (AWSSDK.SecretsManager, Microsoft.Data.SqlClient,
   Microsoft.SqlServer.TransactSql.ScriptDom, Microsoft.EntityFrameworkCore(.Relational),
   Microsoft.CodeAnalysis.CSharp(.Workspaces), YamlDotNet, System.Composition, ...), while ADR-001 and
   the csproj Description still claim "zero vendor dependencies".
2. `DataGuard.Analyzers` targeted net9.0 and referenced Core (net9.0), so the IDE analyzer could not
   load in Visual Studio's Roslyn host (netstandard2.0) and the package carried a net9.0 dependency.
3. Quick-fix attributes (`SkipContractCheck`, `ExpectedSpParameter`, `ExpectedColumn`) lived inside the
   analyzer assembly that consumers do not reference at compile time, so generated code could not compile.

## Decisions

### D1 — Core keeps vendor dependencies (claim corrected)

`DataGuard.Core` remains the engine: rules, adapters' shared abstractions, security, telemetry, baseline.
It targets net9.0 and legitimately depends on the DB/cloud providers it orchestrates.
The "zero vendor dependencies" claim is removed from the package Description; ADR-001 wording is
superseded. Grant narrative must not claim zero-dependency Core.

### D2 — New `DataGuard.Contracts` (netstandard2.0) for IDE-shared types

New project `src/DataGuard.Contracts` (netstandard2.0) holds the three contract attributes plus the
`ParameterDirection` enum used by them. It has zero dependencies. It is referenced by:

- `DataGuard.Analyzers` (netstandard2.0) — attributes resolve in both csc and VS hosts.
- `DataGuard.Core` — `ManualContractSource` reads the attributes via reflection.

### D3 — `DataGuard.Analyzers` targets netstandard2.0 and drops the Core reference

The analyzer is now the true "IDE light layer": only syntax-level checks (unvalidated SQL call marker,
injection pattern, missing FROM, EXEC prefix) that need no database ground truth. The heavy rules
engine runs in the CLI (`dataguard validate`) against DB/snapshot/manual ground truth.
The analyzer package bundles `DataGuard.Contracts.dll` next to its own assembly and declares it as a
NuGet dependency so quick-fixes compile in consumer projects.

### D4 — DG012 stays IDE-only

`ProviderOptionMismatchRule` requires Roslyn DbContext provider-registration context that the contract
engine does not have. It is removed from the engine rule set; the descriptor remains for the analyzer
surface.

## Consequences

- IDE analyzers load in Visual Studio and `dotnet build`; analyzer package has a netstandard2.0
  dependency graph only.
- Quick-fix attributes compile in consumer projects (DataGuard.Contracts flows as a package dependency).
- Ground-truth modes: Full (live DB via adapters), Snapshot (persisted schema in `snapshot.json`,
  offline validation), Manual (`--assembly` attribute reflection) are all wired end-to-end in the CLI.
- Future: if Core must ever be "zero-dep", split `Rules`/`Abstractions` into a netstandard2.0 assembly;
  recorded as backlog, not required for v0.1.
