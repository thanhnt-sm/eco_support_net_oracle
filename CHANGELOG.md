# Changelog

All notable changes to DataGuard are documented here. Format based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), semantic versioning via git tags (MinVer).

## [Unreleased]

### Added
- `DataGuard.Contracts` package (netstandard2.0): `SkipContractCheck`, `ExpectedSpParameter`, `ExpectedColumn` attributes usable by quick-fixes in consumer projects.
- Manual ground-truth mode: `dataguard validate --offline --assembly <dll>` reads expected columns/parameters from attributes (zero DB access).
- Snapshot mode persists ground-truth schema; offline `validate` rebuilds rules from the snapshot; `snapshot diff --fail-on-drift` exits non-zero on drift.
- Oracle stored-procedure extraction wired into `validate` (ALL_PROCEDURES enumeration, overload grouping by SUBPROGRAM_ID/OVERLOAD).
- MySQL/PostgreSQL SP extraction wired into `validate`.
- Analyzer tests: descriptor arity guard + incremental generator execution tests; strict golden-corpus `unexpectedErrors` assertion; per-rule coverage tests for MY/PG/Oracle dialect rules.
- CI: security gates (vulnerability JSON gate, TruffleHog, CodeQL) now run on tag releases; Docker smoke test runs on PRs; dependabot tracks NuGet; restores run in `--locked-mode`.
- `packages.lock.json` for reproducible restores; MinVer versioning from git tags; SourceLink + deterministic builds; symbol (snupkg) publishing.
- `dataguard config` full round-trip via YamlDotNet (30+ fields, nested Oracle/SqlServer blocks).

### Changed
- `DataGuard.Analyzers` retargeted to netstandard2.0 and decoupled from DataGuard.Core (loads in Visual Studio); bundles `DataGuard.Contracts.dll`.
- License unified to MIT (removed PolyForm Noncommercial `LICENSE.md`); README rewritten as DataGuard landing page.
- `sp_describe_first_result_set` reads correct ordinals, uses `EXEC [schema].[proc]`, skips zero-result-set procedures.
- Rules engine: deterministic dependency graph (RuleId-based), DG004 matches mapped column names, DG101 separates engine parameter-count id from IDE DG001.
- Oracle catalog predicates use `UPPER()`; RefCursorDescriber uses `col_charsetform` (no PLS-00302) and supports OUT SYS_REFCURSOR.
- Credentials fail closed by default (`AllowPlaintextConfigFallback=false`); `config show` redacts secrets; `.dataguard*` gitignored.
- Dialect keyword lists curated (window functions no longer false-positive); TOP/LIMIT word-boundary matching.

### Fixed
- Analyzer package missing `DataGuard.Core.dll` at load time (bundled dependency closure).
- `oracle-check` exit code (returns 1 on failure); DG098/DG099 descriptor registration (Warning, not DG002 fallback).
- Docker image baking wrong version (VERSION build-arg); fake `github.com/DataGuard/DataGuard` URLs in 4 packages.
- MySQL LONGTEXT `CHARACTER_MAXIMUM_LENGTH` overflow; Oracle DG007 byte-vs-char comparison; PG unnamed-parameter drop.

### Removed
- Dead `HealthChecks`/`HealthCheckServer`; stale `docker-compose.yml`; legacy `loop-results.tsv`; EcoSupport README/SECURITY content.
