# Phase 3 — Solution Design, Remediation Backlog & Human Checkpoint (`eco_support_net_oracle`)

- **Audit Date**: 2026-10-14
- **Target Repository**: `D:/100.Software/Github/eco_support_net_oracle` (DataGuard v0.4.0 @ `1e5a5922`)
- **Working Tree Status**: All 8 modified and 4 untracked user WIP files preserved untouched (`0` production or test source files modified during Phases 0–3).

---

## 1. Dual-Option Solution Designs for Key P0/P1 Architectural Decisions

### Decision 1: `RT-CI-01` — Non-`.sln` Lockfile Drift (`error NU1004` on 5 Standalone Projects)
- **Root Cause**: `.github/dependabot.yml` configures NuGet only at `directory: "/"`, which evaluates `DataGuard.sln` (24 projects) and skips the 5 standalone `.csproj` files (`tests/DataGuard.BinaryCompatibilityFixture`, `tools/benchmarks/DataGuard.Benchmarks`, `tools/benchmarks/DataGuard.Observability.Benchmarks`, `samples/DataGuard.Sample`, `samples/plugins/DataGuard.Samples.NamingPlugin`) that inherit `Directory.Build.props` (`NetAnalyzers 10.0.402`, `SourceLink.GitHub 10.0.402`, `RestorePackagesWithLockFile=true`).
- **Option A (Recommended — Sync 5 Lockfiles + Add Dependabot Directories + Lockfile Parity Gate)**:
  - Run `dotnet restore --force-evaluate` on the 5 standalone projects to sync `10.0.402`.
  - Add the 5 project directories to `.github/dependabot.yml` under `nuget`.
  - Add a unit test in `scripts/tests/` verifying that every `packages.lock.json` in the repo matches the direct package versions in `Directory.Build.props`.
  - *Trade-offs*: Preserves `--locked-mode` supply-chain integrity on all 29 projects and keeps `BinaryCompatibilityFixture` isolated from in-tree `ProjectReference`s.
- **Option B (Alternative — Add Standalone Projects to `DataGuard.sln` with Conditional Build Exclusion)**:
  - Add the 5 projects to `DataGuard.sln` and add a fallback default for `BaselineCoreAssembly` in `DataGuard.BinaryCompatibilityFixture.csproj`.
  - *Trade-offs*: Single Dependabot `/` target, but couples `BinaryCompatibilityFixture` to the main solution graph.

---

### Decision 2: `RT-SQL-01` — `OracleLiveQuerySchemaProvider` `GetSchemaTable()` Crash (`100% DG020 Failure`)
- **Root Cause**: `OracleLiveQuerySchemaProvider.cs:132` indexes `row["DataTypeName"]` on ODP.NET's `OracleDataReader.GetSchemaTable()`, which does not have a `"DataTypeName"` column and throws `ArgumentException: Column 'DataTypeName' does not belong to table SchemaTable` (caught at line 151 and reported as `LiveSchemaStatus.Failed` / `DG020`).
- **Option A (Recommended — Use `reader.GetDataTypeName(ordinal)` + Column Existence Guards)**:
  - Use `reader.GetDataTypeName(ordinal)` (which ODP.NET natively implements to return `"VARCHAR2"`, `"NVARCHAR2"`, `"NUMBER"`, `"DATE"`, etc.) and guard `DataRow` column lookups (`IsByteSemantic`, `ColumnSize`, `NumericPrecision`, `NumericScale`, `AllowDBNull`) with `schemaTable.Columns.Contains(...)`.
  - Widen the bind-parameter regex at line 103 from `(?<!:):([A-Za-z0-9_]+)` to `(?<!:):([A-Za-z0-9_$#]+)`.
  - *Trade-offs*: Zero new dependencies; returns exact Oracle type names expected by `OracleTypeCompatibility`.
- **Option B (Alternative — Switch to `reader.GetColumnSchemaAsync(cancellationToken)`)**:
  - Replace `GetSchemaTable()` with `DbDataReader.GetColumnSchemaAsync` (`DbColumn.DataTypeName`), matching `PostgreSqlLiveQuerySchemaProvider` and `MySqlLiveQuerySchemaProvider`.
  - *Trade-offs*: Uniform across adapters, though `DbColumn` does not expose ODP.NET's `IsByteSemantic` column directly without `GetSchemaTable()`.

---

### Decision 3: `RT-SEC-01`, `RT-SEC-02`, `RT-SEC-03`, `RT-SEC-05` — Zero-Trust Live DB, Vault & `--ide-safe` Boundaries
- **Root Causes**:
  1. `RT-SEC-02`: `HashiCorpVaultSecretStore` (`ISecretStore.cs:125-145`) sends `X-Vault-Token: <VAULT_TOKEN>` to any `https://` `VaultAddress` in `.dataguard.yml`.
  2. `RT-SEC-03`: `IdeSafePolicy.Apply` (`IdeSafePolicy.cs:71-133`) does not clear `Oracle.DescribeRefCursors` or `Oracle.UseRefCursorDescribe`, allowing stored procedure execution during `validate --ide-safe --allow-env-connection`.
  3. `RT-SEC-05` / `RT-IDE-11`: VS Code (`command-args.ts:84-87`) passes `--config .dataguard.yml` to live `snapshot refresh` and `baseline` commands without `--ide-safe`, and Visual Studio `ForgetSolutionConsentAsync` (`DataGuardPackage.Consent.cs:99`) fails to revoke `ComputeLiveConsentKey`.
  4. `RT-SEC-01`: `SqlTextScanner` assumes nested `/* /* */` comments and enables `$tag$` and `q'[...]'` across all dialects.
- **Option A (Recommended — Policy Enforcement + Operator Env Pinning + Dialect-Aware Lexing)**:
  - In `IdeSafePolicy.Apply`, force `Oracle.DescribeRefCursors = false`, `Oracle.UseRefCursorDescribe = false`, and `Oracle.RefCursorSamples = Empty`, and validate that `SnapshotFilePath` and `BaselineFilePath` are workspace-contained, non-UNC, non-reparse-point paths (`RT-SEC-03`, `RT-SEC-06`).
  - In `HashiCorpVaultSecretStore`, require `VaultAddress` to come from the operator's `VAULT_ADDR` environment variable (or match `VAULT_ADDR` when `VaultAddress` is also specified in `.dataguard.yml`), and set `AllowAutoRedirect = false` on `SharedHttpClient` (`RT-SEC-02`).
  - In VS Code (`command-args.ts`), omit `--config` from live database commands (`snapshot`, `baseline`, `verify-shape`) to match Visual Studio, and in Visual Studio (`DataGuardPackage.Consent.cs:99`), revoke both `ComputeConsentKey` and `ComputeLiveConsentKey` (`RT-SEC-05`, `RT-IDE-11`).
  - In `SqlTextScanner` and live schema providers, reject nested `/* /* */` comments and foreign-dialect quote tokens, and wrap Oracle/PostgreSQL live schema sessions in `SET TRANSACTION READ ONLY` (`RT-SEC-01`).
  - *Trade-offs*: Preserves all legitimate CLI/IDE workflows while closing all 4 trust boundary gaps.
- **Option B (Alternative — Remove `VaultAddress` & `DescribeRefCursors` from `.dataguard.yml` Schema)**:
  - Require `--vault-addr` and `--describe-ref-cursors` exclusively as CLI flags and reject them in `.dataguard.yml`.
  - *Trade-offs*: Breaking configuration change for existing `.dataguard.yml` files.

---

### Decision 4: `RT-ARCH-01` & `RT-ARCH-02` — Plugin Admission Identity Closure & `.NET 10` Roll-Forward
- **Root Cause**: `PluginAdmission.cs:268-277,317-339` compares full assembly identities (`Name, Version=9.0.0.0, Culture=neutral, PublicKeyToken=...`) using exact `StringComparer.Ordinal` equality against `hostAssemblyIdentities`, rejecting all `net9.0` plugins when the host runs on `.NET 10` (`Version=10.0.0.0`), when `DataGuard.Core` version advances (`0.3.0.0` -> `0.4.0.0`), or in single-file publish builds where `TRUSTED_PLATFORM_ASSEMBLIES` is empty.Meanwhile `DataGuard.Cli.csproj` lacks `<RollForward>LatestMajor</RollForward>`.
- **Option A (Recommended — Version-Compatible Host Assembly Matching + `<RollForward>LatestMajor</RollForward>`)**:
  - In `PluginAdmission` and `VerifiedPluginLoadContext`, match host-provided assemblies by `(Name, CultureName, PublicKeyToken)` with `Host.Version >= Reference.Version` (and fallback to `AppDomain.CurrentDomain.GetAssemblies()` when `TRUSTED_PLATFORM_ASSEMBLIES` is empty), while keeping exact `FullName` + SHA-256 verification on plugin-bundled `ManagedDependencies`.
  - Add `<RollForward>LatestMajor</RollForward>` to `DataGuard.Cli.csproj`, `DataGuard.LanguageServer.csproj`, and `DataGuard.Host.csproj`, and set `DOTNET_ROLL_FORWARD=LatestMajor` in VS Code's `buildChildEnvironment`.
  - *Trade-offs*: Matches standard CoreCLR assembly unification semantics and preserves cryptographic verification of plugin dependencies.
- **Option B (Alternative — Multi-Target All Executables for `net9.0;net10.0`)**:
  - Multi-target `DataGuard.Cli`, `DataGuard.LanguageServer`, and `DataGuard.Host` for `net9.0;net10.0`.
  - *Trade-offs*: Doubles build/publish time and still breaks plugins across minor `DataGuard.Core` version updates.

---

### Decision 5: `RT-IDE-01`, `RT-IDE-02`, `RT-IDE-06`..`09` — Roslyn Packaging, CodeFixes, LSP & VS Code QuickFix
- **Root Causes**:
  1. `RT-IDE-01`: `DataGuard.CodeFixes.csproj` packs DLLs into `codefixes/dotnet/cs` (ignored by NuGet/MSBuild) and omits `DataGuard.SqlClassification.dll`; `DataGuard.Analyzers.csproj` leaks `System.Composition.Hosting 10.0.13` without `PrivateAssets="all"`.
  2. `RT-IDE-02`: `DG001` FixAll emits duplicate `[SkipContractCheck]` (`CS0579`) when a method has 2+ SQL calls and applies `[SkipContractCheck]` to invalid targets (`CS0592`).
  3. `RT-IDE-06`..`08`: `DataGuard.LanguageServer/Program.cs` mutates a non-thread-safe `Dictionary` across threads, double-disposes `CancellationTokenSource`, mixes `Console.Out` with `Console.OpenStandardOutput()`, and emits `DGSQL001` on English strings in C# code.
  4. `RT-IDE-09`: VS Code `DataGuardQuickFixProvider` uses `/\bSELECT\s+\*\b/i` (which never matches `* `), falling through to inject `//` C# comments inside multi-line SQL literals or replacing `SELECT *` with hardcoded `Id, Name, CreatedAt` columns without checking `isPathInWorkspaceFolder`.
- **Option A (Recommended — Standard `analyzers/dotnet/cs` Layout + Safe CodeFix/QuickFix + Thread-Safe LSP)**:
  - Pack `DataGuard.CodeFixes.dll` and `DataGuard.SqlClassification.dll` into `analyzers/dotnet/cs` with `<DevelopmentDependency>true</DevelopmentDependency>` in `DataGuard.CodeFixes.csproj`, and add `PrivateAssets="all"` to `System.Composition.Hosting` in `DataGuard.Analyzers.csproj`.
  - In `CodeFixProviders.cs`, deduplicate target declarations during FixAll, check if `SkipContractCheck` is already present, and resolve only `MethodDeclarationSyntax` or `ClassDeclarationSyntax` (or widen `SkipContractCheckAttribute` targets).
  - In `DataGuard.LanguageServer/Program.cs`, use `ConcurrentDictionary`, dispose `CancellationTokenSource` only in `PublishDebouncedAsync`'s `finally`, write both header and body bytes to a single `stdout` `Stream`, and require valid SQL clause structure in `SqlClassifier.Analyze`.
  - In VS Code `quick-fix-provider.ts`, fix the `SELECT *` regex to replace `*` with `/* TODO: specify columns */` (preserving table qualifiers), remove the destructive `// [DataGuard Auto-Fix]` fallback inside multi-line strings, align rule titles with `ProviderRuleCatalog`, and enforce `isPathInWorkspaceFolder`.
- **Option B (Alternative — Merge `DataGuard.CodeFixes` into `DataGuard.Analyzers` & Remove VS Code QuickFix Provider)**:
  - Ship a single combined NuGet package and remove `quick-fix-provider.ts` from VS Code.
  - *Trade-offs*: Pulls `Microsoft.CodeAnalysis.CSharp.Workspaces` into build-only `csc` runs and removes quick fixes when C# Dev Kit is not installed.

---

## 2. Prioritized Remediation Backlog (Work Items, Dependencies, Estimates & Acceptance Criteria)

| Work Item ID | Wave / Priority | Source Findings | Target Files | Dependencies | Effort | Measurable Acceptance Criteria (`Given-When-Then`) | Rollback Plan |
|---|---|---|---|---|---|---|---|
| **`WI-01`** | **Wave 0 (`P0`)** | `RT-CI-01`, `RT-CI-02` | `tests/DataGuard.BinaryCompatibilityFixture/packages.lock.json`, `tools/benchmarks/*/packages.lock.json`, `samples/**/packages.lock.json`, `tests/DataGuard.GoldenCorpus.Tests/DataGuard.GoldenCorpus.Tests.csproj`, `.github/dependabot.yml`, `Directory.Build.props` | None | `1.5h` | **Given** `Directory.Build.props` pins `NetAnalyzers 10.0.402`, **When** `dotnet restore --locked-mode` and `dotnet build DataGuard.CrossPlatform.slnf -c Release -warnaserror` run, **Then** all 29 projects restore with `0` `NU1004` errors and build with `0` `MSB3277` warnings. | `git checkout -- **/packages.lock.json .github/dependabot.yml` |
| **`WI-02`** | **Wave 0 (`P0`)** | `RT-TEST-01`, `RT-TEST-02`, `RT-TEST-07` | `scripts/tests/test_alert19_resolution.py`, `scripts/tests/test_assert_roslyn_lock.py`, `scripts/tests/test_trx_to_junit.py`, `scripts/assert-roslyn-lock.sh` | None | `1.5h` | **Given** `Pkcs` resolved to `10.0.13` and a Windows or Linux host, **When** `python3 -m unittest discover -s scripts/tests -v` runs, **Then** all 117 tests pass (`0` failures, `0` live network calls). | `git checkout -- scripts/` |
| **`WI-03`** | **Wave 1 (`P0`)** | `RT-SQL-01`, `RT-SQL-02`, `RT-SQL-04` | `src/DataGuard.Oracle.Adapter/OracleLiveQuerySchemaProvider.cs`, `src/DataGuard.Oracle.Adapter/LengthMismatch.cs`, `src/DataGuard.SqlServer.Adapter/SqlServerLiveQuerySchemaProvider.cs` | `WI-01` | `2.5h` | **Given** an Oracle `SELECT` query described via `OracleLiveQuerySchemaProvider`, **When** `DescribeResultSetAsync` reads schema metadata, **Then** it uses `reader.GetDataTypeName(ordinal)` without throwing `ArgumentException` and returns `LiveSchemaStatus.Described`; `DG009` respects `IsUnicode(false)` and `"string?"`. | `git revert <commit>` |
| **`WI-04`** | **Wave 1 (`P0`)** | `RT-SEC-01`, `RT-SEC-02`, `RT-SEC-03`, `RT-SEC-04`, `RT-SEC-06`, `RT-SEC-08`, `RT-COMPAT-02` | `src/DataGuard.Cli/IdeSafePolicy.cs`, `src/DataGuard.Core/Security/SecretStores/ISecretStore.cs`, `src/DataGuard.Core/Rules/Sql/SqlTextScanner.cs`, `src/DataGuard.Cli/Commands/{BaselineCommand,AssessCommand,InitCommand,ScanCommand,PreflightCommand}.cs`, `src/DataGuard.Core/Sources/ConnectionDiscovery.cs` | `WI-01` | `4.0h` | **Given** a hostile `.dataguard.yml` (`Oracle.DescribeRefCursors: true`, `VaultAddress: https://attacker.tld`, `SnapshotFilePath: \\attacker\share`, `BaselineFilePath: ../out`), **When** CLI commands run under `--ide-safe` or without matching `VAULT_ADDR`, **Then** zero stored procedures execute, zero `VAULT_TOKEN` requests are sent, UNC/uncontained paths are refused with exit code `4`, and EZConnect/`{a;b}` secrets are redacted. | `git revert <commit>` |
| **`WI-05`** | **Wave 1 (`P0`)** | `RT-IDE-09`, `RT-IDE-10`, `RT-IDE-11` (`RT-SEC-05`), `RT-DX-05` | `src/DataGuard.VSCode/src/ui/{quick-fix-provider.ts,decoration-manager.ts,findings-tree-provider.ts,sql-queries-tree-provider.ts,redaction.ts}`, `src/DataGuard.VSCode/src/{command-args.ts,extension.ts}`, `src/DataGuard.VisualStudio/DataGuardPackage.Consent.cs` | None | `3.5h` | **Given** `SELECT * FROM Users` in VS Code and a solution with live DB consent in Visual Studio, **When** QuickFix is applied or `Forget Solution Consent` is invoked, **Then** `SELECT *` is safely replaced without injecting C# comments into SQL or `Id, Name, CreatedAt`, `isPathInWorkspaceFolder` is enforced, VS Code live commands omit `--config`, and VS revokes both validate and `:live` consent keys. | `git revert <commit>` |
| **`WI-06`** | **Wave 2 (`P1`)** | `RT-IDE-06`, `RT-IDE-07`, `RT-IDE-08` | `src/DataGuard.LanguageServer/Program.cs`, `src/DataGuard.SqlClassification/SqlClassifier.cs`, `tests/DataGuard.Core.Tests/LanguageServerLifecycleTests.cs` | `WI-01` | `2.5h` | **Given** 1,000 rapid `textDocument/didChange` notifications and C# strings containing English sentences (`"Update profile"`, `"Select option"`), **When** `DataGuard.LanguageServer` processes them, **Then** zero `ObjectDisposedException` or `Dictionary` races occur, `WriteAsync` uses a single `Stream`, and English sentences emit `0` `DGSQL001` diagnostics. | `git revert <commit>` |
| **`WI-07`** | **Wave 2 (`P1`)** | `RT-ARCH-01`, `RT-ARCH-02`, `RT-TEST-03`, `RT-TEST-04` | `src/DataGuard.Core/Plugins/PluginAdmission.cs`, `src/DataGuard.Core/Plugins/RulePluginManager.cs`, `src/DataGuard.Cli/DataGuard.Cli.csproj`, `tests/DataGuard.Core.Tests/{CliProcessTestRunner.cs,OfflineManifestValidationTaskTests.cs,HealthHostIntegrationTests.cs,DataGuard.Core.Tests.csproj}` | `WI-01` | `3.0h` | **Given** a `net9.0` plugin running on a `.NET 10` roll-forward host (or updated `DataGuard.Core` version), **When** `PluginAdmission.Verify` and `CliProcessTestRunner` execute, **Then** the plugin is admitted (`Host.Version >= Reference.Version`), child processes use `Environment.ProcessPath` with isolated `NUGET_PACKAGES`, and all 1,802 `DataGuard.Core.Tests` pass regardless of system `PATH` order. | `git revert <commit>` |
| **`WI-08`** | **Wave 2 (`P1`)** | `RT-CORE-01` (`RT-IDE-04`), `RT-CORE-02`, `RT-CORE-03`, `RT-CORE-04`, `RT-COMPAT-01` | `src/DataGuard.Contracts/NameConventions.cs`, `src/DataGuard.Core/Rules/NamingConventionRule.cs`, `src/DataGuard.Core/Sources/ModelSnapshotCSharpParser.cs`, `src/DataGuard.PostgreSql.Adapter/PostgreSqlDialectChecker.cs`, `src/DataGuard.MySql.Adapter/MySqlDialectChecker.cs`, `src/DataGuard.Core/Baseline/BaselineManager.cs`, `src/DataGuard.Build/OfflineManifestValidationTask.cs` | `WI-01` | `4.0h` | **Given** EF Core `Entity("Ns.Order", b => b.ToTable("Orders", "sales"))`, acronym property `CustomerID`, PascalCase property `CustomerName`, PG/MySQL SP calls, and a v4 snapshot, **When** parsed/validated/migrated, **Then** `ToPascalCase("CustomerName") == "CustomerName"`, `ToSnakeCase("CustomerID") == "customer_id"`, `ModelSnapshotCSharpParser` extracts `"sales.Orders"`, PG/MySQL dialect rules ignore `IsStoredProcedure == true` and comments/literals, `MigrateBaselineAsync` leaves v2–v4 files untouched, and `OfflineManifestValidationTask` accepts all catalog rule IDs. | `git revert <commit>` |
| **`WI-09`** | **Wave 2 (`P1`)** | `RT-IDE-01`, `RT-IDE-02`, `RT-IDE-03`, `RT-IDE-05` | `src/DataGuard.Analyzers/DataGuard.Analyzers.csproj`, `src/DataGuard.CodeFixes/DataGuard.CodeFixes.csproj`, `src/DataGuard.CodeFixes/CodeFixProviders.cs`, `src/DataGuard.Analyzers/{ContractValidationAnalyzer.cs,SqlTextHeuristics.cs}`, `scripts/verify-analyzer-packaging.sh` | `WI-08` | `3.5h` | **Given** `DataGuard.Analyzers.nupkg` and `DataGuard.CodeFixes.nupkg`, **When** packed and consumed, **Then** `DataGuard.Analyzers` has zero `System.Composition.Hosting` nuspec dependencies, `DataGuard.CodeFixes` packs under `analyzers/dotnet/cs`, `DG001` FixAll on a method with 2+ SQL calls produces `0` `CS0579`/`CS0592` errors, and `DG004` heuristics handle `COALESCE(a,b) AS x` and CTEs without false errors. | `git revert <commit>` |
| **`WI-10`** | **Wave 3 (`P1`/`P2`)** | `RT-CI-03`, `RT-CI-04`, `RT-CI-05`, `RT-CI-06`, `RT-CI-07`, `RT-TEST-05`, `RT-TEST-06` | `.github/workflows/{ci.yml,release.yml,marketplace.yml,build_release.yml}`, `Dockerfile`, `scripts/{trx_to_junit.py,assert-vsix.ps1}`, `tests/DataGuard.Core.Tests/*` | `WI-01`, `WI-02` | `3.5h` | **Given** CI/release workflows and Windows security tests, **When** executed, **Then** `Dockerfile`/`release.yml`/`build_release.yml` pass `-p:MinVerVersionOverride`, `verify-ci` checks all required `ci.yml` jobs, `ci.yml` deduplicates `src/` coverage paths and collects coverage on `VisualStudio.Tests`, and all 6 Windows symlink tests run via `DirectoryLinkTestHelper`. | `git revert <commit>` |
| **`WI-11`** | **Wave 3 (`P1`/`P2`)** | `RT-DX-01`..`06`, `RT-GOV-01`..`04`, `RT-PERF-01`..`04`, `RT-PROD-01`..`04a` | `src/DataGuard.Cli/Services/ConfigLoader.cs`, `src/DataGuard.Cli/Commands/SnapshotCommands.cs`, `docs/legal/ADDITIONAL-PERMISSIONS.md`, `README*.md`, `SECURITY*.md`, `docs/USAGE.md`, `CONTRIBUTING*.md`, `plans/ACTIVE_SESSION_REGISTER.md` | `WI-08`, `WI-09` | `4.5h` | **Given** a fresh project running `dataguard init` and documentation sync gates, **When** `verify_docs_sync.sh` and CLI commands run, **Then** CWD `.dataguard.yml` is loaded by `config show`/`validate`, `snapshot refresh` sets `EnableBaseline = false`, zero `<contact email placeholder>` or broken `404` links remain, all regexes have explicit timeouts, and approved product quick-wins (`RT-PROD-01`..`04a`) pass unit tests. | `git revert <commit>` |

---

## 3. Mandatory Human Checkpoint (Before Phase 4 Remediation Execution)

Per Section 7 & Section 16 of the Master Mission, **no production or test files in `eco_support_net_oracle` have been modified during Phases 0–3**. Before proceeding to Phase 4 (`Remediation`), human approval is requested on:

1. **Wave 0 + Wave 1 Execution Authorization (`WI-01`..`WI-05`)**:
   - Approve syncing the 5 stale `packages.lock.json` files and adding their directories to `.github/dependabot.yml` (`RT-CI-01`), fixing `test_alert19_resolution.py` and `test_assert_roslyn_lock.py` (`RT-TEST-01`, `RT-TEST-02`), fixing `OracleLiveQuerySchemaProvider` (`RT-SQL-01`), hardening `--ide-safe` / `HashiCorpVaultSecretStore` / VS Code live commands (`RT-SEC-01`..`06`), and fixing VS Code's `DataGuardQuickFixProvider` (`RT-IDE-09`).
2. **Commercial Contact Channel for `RT-GOV-01`**:
   - Confirm the replacement string for `<contact email placeholder>` across the 7 legal/documentation files (`docs/legal/ADDITIONAL-PERMISSIONS.md`, `README.md`, `README.vi.md`, `SUPPORT.md`, `CHANGELOG.md`, and VSIX copies) — e.g., `https://github.com/thanhnt-sm/eco_support_net_oracle/discussions` (or a specific commercial licensing email address).
3. **Config Discovery Policy (`RT-DX-01` / `RT-COMPAT-03`)**:
   - Confirm whether `ConfigLoader.TryLoadConfig(null)` should auto-discover `./.dataguard.yml` in the current working directory across all non-`--ide-safe` CLI commands (`validate`, `snapshot`, `baseline`, `config show`, `config validate`), or only in `config show` and `config validate`.
4. **Product Evolution Scope (`RT-PROD-01`..`04a`)**:
   - Confirm whether to include the 4 approved high-ROI product enhancements (`RT-PROD-01` Roslyn `AdditionalFiles` snapshot, `RT-PROD-02` `DG012`/`PG004` activation, `RT-PROD-03` Dapper `CommandDefinition` unwrapping, `RT-PROD-04a` `.sql` LSP selector) in Wave 3 after P0/P1 remediation is complete.
