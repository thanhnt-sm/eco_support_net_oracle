# Changelog

All notable changes to DataGuard are documented here. Format based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), semantic versioning via git tags (MinVer).

## [Unreleased]

### Security
- **CLI (2026-09-29)**: new `--ide-safe` option on `validate` and `assess`. Forces Snapshot mode and strips `ManualAssemblyPath`, connection strings (config and `DATAGUARD_CONNECTION_STRING`), secret-manager settings, `AuditLogPath` and telemetry output from repository-controlled configuration; rejects `--connection`, `--offline`, `--assembly`, `--ef-*`, `--allow-network`, `--remote-advisories` with exit code 2. Closes the path where a hostile `.dataguard.yml` could make an IDE-launched CLI call `Assembly.LoadFrom` or authenticate to an attacker host.
- **Visual Studio Extension (2026-09-29)**: per-solution trust gate — first run asks for consent, stored per (solution directory, SHA-256 of `.dataguard.yml`) in the VS user settings store; "Run Validation on Build" never prompts and skips unconsented solutions. CLI is always launched with `--ide-safe`; an older CLI that rejects the flag stops the run instead of falling back. Removed the unprompted `dotnet tool install -g DataGuard.Cli` auto-install. Custom CLI path must be absolute (no solution-relative resolution). All exception messages written to the Output pane are redacted; redaction now covers `User Id=…;Password=…` forms and JWT-shaped tokens.

- **VS Code Extension (2026-09-29)**: `validate` and `assess` now pass `--ide-safe` in addition to the existing workspace-trust gate, so a tampered `.dataguard.yml` in a trusted workspace can no longer load assemblies or open connections.

### Added
- **CI (2026-09-29)**: `visual-studio-vsix-package` job runs the real VSSDK packaging build (`CreateVsixContainer=true`) on every push/PR and asserts the VSIX contains `cli/dataguard.exe`, the Roslyn analyzers, the pkgdef and a manifest version matching `ExtensionVersion.Fallback`. CreatePkgDef regressions no longer wait for a release tag.
- **Visual Studio Extension (2026-09-29)**: Output pane warns when the solution has no `.dataguard.yml` (source-only rules ran); exit code 3 is explained as "validation incomplete" with the CLI reason; Help → About and log banner report the real manifest version.

- **Visual Studio Extension (2026-09-28)**: Error List navigation opens the document via `VsShellUtilities.OpenDocument` with fallback to document/project opening (caret positioning added 2026-09-29, see Fixed).
- **Visual Studio Extension (2026-09-28)**: Bundled Roslyn analyzers (`DataGuard.Analyzers.dll`) and code fixes (`DataGuard.CodeFixes.dll`) into VSIX container via MSBuild target and manifest asset declarations for out-of-the-box IDE squiggles.
- **Visual Studio Extension (2026-09-28)**: Output Window logs rule inventory summary banner (`DataGuard: Ran N rules across M unique check types`) after validation completion.
- **Core SQL Source (2026-09-28)**: Stored procedure heuristic detection in `ProjectCSharpSqlSource.IsSqlString` expanded to support `PROC_`, `FNC_`, and `P_` prefixes alongside existing `SP_` and `USP_`.
- **Oracle Adapter (2026-09-28)**: Enriched `DG010` dialect incompatibility messages now explicitly display target provider and migration syntax hint in visible text.
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
- **Visual Studio Extension (2026-09-29)**: `DataGuardPackage.cs` (1 629 lines) split into single-purpose units (`CliArgumentBuilder`, `CliRunSession`, `ProcessTerminator`, `ProgressLineParser`, `RuleInventory`, `SarifErrorListPublisher`, `SolutionTrustGate`, `TempDirectoryCleaner`, `ExitCodeExplainer`, `ExtensionVersion`); behaviour-preserving except where listed under Security/Fixed.
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
- **Visual Studio Extension (2026-09-29)**: Error List double-click now positions the caret at the SARIF line/column via `ErrorListProvider.Navigate` (the previous handler only opened the document). SARIF results with malformed fields are skipped individually instead of aborting the whole load; Error List is capped at 2 000 tasks per run with the truncation count reported; percent-encoded relative URIs are decoded; `assess` no longer shows the previous `validate` run's rule inventory.
- **Build (2026-09-29)**: the VSIX build's nested `dotnet publish -r win-x64` of the CLI rewrote nine committed `packages.lock.json` files (adding `win-x64` + ILLink.Tasks), which then failed the next `--locked-mode` restore with NU1004. The publish now uses `NuGetLockFilePath=obj\cli-publish.packages.lock.json`, so committed lock files stay untouched.
- Analyzer package missing `DataGuard.Core.dll` at load time (bundled dependency closure).
- `oracle-check` exit code (returns 1 on failure); DG098/DG099 descriptor registration (Warning, not DG002 fallback).
- Docker image baking wrong version (VERSION build-arg); fake `github.com/DataGuard/DataGuard` URLs in 4 packages.
- MySQL LONGTEXT `CHARACTER_MAXIMUM_LENGTH` overflow; Oracle DG007 byte-vs-char comparison; PG unnamed-parameter drop.
- Flaky AutoDetectionEngine tests: env var `DATAGUARD_CONNECTION_STRING` leaked from CredentialManagerFullTests via xUnit parallel execution; fixed with `[Collection("Sequential")]` + IDisposable cleanup on all env-var-sensitive test classes.

### Removed
- Dead `HealthChecks`/`HealthCheckServer`; stale `docker-compose.yml`; legacy `loop-results.tsv`; EcoSupport README/SECURITY content.
