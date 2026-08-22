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
- MySql/PostgreSql adapter unit tests: dialect checker (syntax detection, context-aware) + length mismatch detector (12 tests each, no DB required).
- DataGuardApi surface tests: Version, CreatePipeline, WithRules, WithPlugins, ValidationResult/DriftReport computed properties, DataGuardFactory methods (15 tests).
- RulePluginManager tests: null directory, merge built-in rules, get by ID, metadata, dispose, empty directory (7 tests).
- SqlServerIntegrationTests: Testcontainers MsSql, auto-skip when Docker unavailable.

### Changed
- `DataGuard.Analyzers` retargeted to netstandard2.0 and decoupled from DataGuard.Core (loads in Visual Studio); bundles `DataGuard.Contracts.dll`.
- License unified to MIT (removed PolyForm Noncommercial `LICENSE.md`); README rewritten as DataGuard landing page.
- `sp_describe_first_result_set` reads correct ordinals, uses `EXEC [schema].[proc]`, skips zero-result-set procedures.
- Rules engine: deterministic dependency graph (RuleId-based), DG004 matches mapped column names, DG101 separates engine parameter-count id from IDE DG001.
- Oracle catalog predicates use `UPPER()`; RefCursorDescriber uses `col_charsetform` (no PLS-00302) and supports OUT SYS_REFCURSOR.
- Credentials fail closed by default (`AllowPlaintextConfigFallback=false`); `config show` redacts secrets; `.dataguard*` gitignored.
- Dialect keyword lists curated (window functions no longer false-positive); TOP/LIMIT word-boundary matching.
- `TreatWarningsAsErrors` enabled solution-wide (0 warnings enforced in CI).
- SEC-006: telemetry circuit breaker (`MaxConsecutiveExportFailures=3`) stops export on repeated failures, resets on success; endpoint allowlist (HTTPS + localhost/127.0.0.1 only); zero HttpClient when telemetry disabled.
- Legacy EcoSupport docs (15 files) marked with ARCHIVED warning banner.
- README architecture link fixed (`docs/architecture/system_architecture.md`).
- Testcontainers.MsSql unified to 4.14.0 across all test projects; removed SSH.NET direct pin (no longer needed).
- AWSSDK.SecretsManager 4.0.100.9 → 4.0.100.10, ScriptDom 180.78.1 → 180.102.0 (patch updates, 5 projects).
- MinVer 5.0.0 → 7.0.0 (build tool, tag-based versioning).
- YamlDotNet 15.1.0 → 18.1.0 (major, CLI YAML serialization).
- Microsoft.SourceLink.GitHub 8.0.0 → 10.0.400 (build tool, deterministic builds).

### Fixed
- Analyzer package missing `DataGuard.Core.dll` at load time (bundled dependency closure).
- `oracle-check` exit code (returns 1 on failure); DG098/DG099 descriptor registration (Warning, not DG002 fallback).
- Docker image baking wrong version (VERSION build-arg); fake `github.com/DataGuard/DataGuard` URLs in 4 packages.
- MySQL LONGTEXT `CHARACTER_MAXIMUM_LENGTH` overflow; Oracle DG007 byte-vs-char comparison; PG unnamed-parameter drop.
- Flaky AutoDetectionEngine tests: env var `DATAGUARD_CONNECTION_STRING` leaked from CredentialManagerFullTests via xUnit parallel execution; fixed with `[Collection("Sequential")]` + IDisposable cleanup on all env-var-sensitive test classes.

### Removed
- Dead `HealthChecks`/`HealthCheckServer`; stale `docker-compose.yml`; legacy `loop-results.tsv`; EcoSupport README/SECURITY content.
