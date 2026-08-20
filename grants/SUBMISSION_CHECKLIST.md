# 📋 Anthropic "Claude for Open Source" Submission Checklist
### Track: Ecosystem Impact Track (Developer Tooling / Open Source Infrastructure)

This document is the pre-submission checklist for the **DataGuard** application to the
**Anthropic Claude for Open Source** program.

---

## 🎯 Product & Narrative

- [x] Product: **DataGuard** — contract validation for .NET Entity ↔ Stored Procedure / Raw SQL
      (Roslyn analyzers + `dataguard` CLI, MIT license, NuGet-distributed).
- [x] Narrative: **porting dbt's model-contract pattern (Core v1.5, 2023) to the .NET
      stored-procedure gap Microsoft publicly declined to build** — EF Core issue
      [#245](https://github.com/dotnet/efcore/issues/245) (2014) is cited as proof.
- [x] Repo landing page (README.md) tells the DataGuard story end-to-end (no legacy product content).
- [x] `samples/DataGuard.Sample` + `scripts/demo_scan.sh` give reviewers a runnable demo
      (offline, no database required).

## ✅ Verification Evidence (run before submission)

- [x] `dotnet build DataGuard.sln -c Release` — 0 errors
- [x] `dotnet test DataGuard.sln` — 65 tests pass (analyzer, core, golden corpus)
- [x] `scripts/demo_scan.sh` — offline demo runs and emits DG diagnostics
- [x] `dotnet pack` — 8 packages (Core, Contracts, SqlServer, Oracle, MySql, PostgreSql,
      Analyzers, Cli) build with MIT license, SourceLink, MinVer versioning
- [ ] NuGet.org live publish — **blocked on owner**: create `NUGET_USER` secret
      (Trusted Publishing, deadline 2026-11-01) and push tag `v0.1.0`
- [ ] CI green on GitHub runner (docker smoke, CodeQL custom queries) — needs first run after push

## 📦 Package Set

| Package | Purpose |
|---------|---------|
| `DataGuard.Core` | Rules engine, security, telemetry, baseline/snapshot |
| `DataGuard.Contracts` | netstandard2.0 attributes for IDE quick-fixes |
| `DataGuard.SqlServer/Oracle/MySql/PostgreSql.Adapter` | Ground-truth readers |
| `DataGuard.Analyzers` | IDE analyzers + quick fixes (netstandard2.0) |
| `DataGuard.Cli` | `dataguard` dotnet tool |
