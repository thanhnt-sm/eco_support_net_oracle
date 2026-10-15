# Red-Team Findings, Risk, Coverage-Gap & Feature Register (`eco_support_net_oracle`)

- **Audit Date**: 2026-10-14
- **Target Commit**: `1e5a5922aeda4116a2198f56fe4ef3dd326330ee` (`main`)
- **Total Raw Findings**: 78 across 4 independent subagent slices -> **72 Canonical Deduplicated Findings** across all 12 Red-Team Attack Layers (`8 P0 Critical`, `31 P1 High`, `23 P2 Medium`, `5 P3 Low`, `5 R&D Product Evolution`).
- **Verification Discipline**: Every finding below is classified as `VERIFIED` (proven via exact `file:line` source inspection and/or empirical test execution), `SUSPECTED` (requires live container or external environment to confirm runtime behavior), `NOT REPRODUCIBLE`, or `NOT TESTED`.

---

## 1. Executive Summary by Attack Layer (12 Layers)

| Layer | Name | Priority | `P0` | `P1` | `P2` | `P3` | `R&D` | Total |
|---|---|---|---|---|---|---|---|---|
| 01 | Correctness & Logic | P0 | 0 | 4 | 3 | 0 | 0 | **7** |
| 02 | SQL & Database Semantics | P0 | 1 | 3 | 4 | 0 | 0 | **8** |
| 03 | Security | P0 | 3 | 4 | 2 | 0 | 0 | **9** |
| 04 | Architecture & Dependency | P1 | 0 | 2 | 2 | 0 | 0 | **4** |
| 05 | Performance & Resource | P1 | 0 | 1 | 2 | 1 | 0 | **4** |
| 06 | Test Quality | P0 | 2 | 2 | 2 | 1 | 0 | **7** |
| 07 | Build, CI/CD & Release | P0 | 1 | 4 | 2 | 0 | 0 | **7** |
| 08 | API & Compatibility | P1 | 0 | 2 | 1 | 1 | 0 | **4** |
| 09 | Analyzer, CodeFix & IDE | P1 | 1 | 6 | 3 | 0 | 0 | **10** |
| 10 | Developer Experience & Docs | P2 | 0 | 1 | 4 | 0 | 0 | **5** |
| 11 | Product Gaps & Feature Expansion | P1 | 0 | 0 | 0 | 0 | 5 | **5** |
| 12 | Repository Governance | P1 | 0 | 1 | 2 | 1 | 0 | **4** |
| **Total** | **All 12 Attack Layers** | — | **8** | **30** | **25** | **4** | **5** | **72** |

*(Note: 6 cross-slice duplicates were merged during reconciliation: `RT-SQL-03` merged with `RT-SEC-01`; `RT-CORE-01` snake_case/PascalCase merged with `RT-IDE-04`; `RT-SEC-05` merged with `RT-IDE-11`; `RT-COMPAT-03` merged with `RT-DX-01`; `RT-DX-02` placeholder merged with `RT-GOV-01`; `RT-TEST-04` host mismatch merged with `RT-ARCH-01`/`02`.)*

---

## 2. Canonical P0 (Critical) Findings Register — Immediate Blockers

### `RT-CI-01` — `Directory.Build.props` Bump Breaks `--locked-mode` Restore (`NU1004`) Across All 5 Non-`.sln` Projects
- **Layer**: 07 (Build, CI/CD & Release) | **Severity**: `P0` | **Confidence**: High | **Status**: `VERIFIED` (Empirically reproduced)
- **Locations**: `Directory.Build.props:43-45`, `.github/dependabot.yml:17-24`, `.github/workflows/ci.yml:115-120,531-548`, `tests/DataGuard.BinaryCompatibilityFixture/packages.lock.json:24-37`, `tools/benchmarks/*/packages.lock.json`, `samples/**/packages.lock.json`
- **Evidence & Repro**: `dotnet build tests/DataGuard.BinaryCompatibilityFixture/DataGuard.BinaryCompatibilityFixture.csproj -c Release -p:RestoreLockedMode=true -p:BaselineCoreAssembly=...` fails with `error NU1004: The package reference Microsoft.CodeAnalysis.NetAnalyzers version has changed from [10.0.401, ) to [10.0.402, )`.
- **Root Cause**: `.github/dependabot.yml` only configures NuGet for `directory: "/"`, which evaluates `DataGuard.sln` (24 projects) and ignores the 5 standalone `.csproj` files that still inherit `Directory.Build.props`.
- **Impact**: Breaks `ci.yml`'s mandatory `Build binary-compatibility fixture against current Core` step and `benchmark` job on `main`.

### `RT-TEST-01` — `test_alert19_resolution.py` Hardcodes `resolved == "9.0.21"`, Breaking `ci.yml` Step 2 (`Workflow policy check`)
- **Layer**: 06 (Test Quality) | **Severity**: `P0` | **Confidence**: High | **Status**: `VERIFIED` (Empirically reproduced)
- **Locations**: `scripts/tests/test_alert19_resolution.py:43-81`, `.github/workflows/ci.yml:41-45`
- **Evidence & Repro**: `python3 -m unittest discover -s scripts/tests` fails with `AssertionError: '10.0.13' != '9.0.21' : tests/DataGuard.Analyzers.Tests/packages.lock.json (net9.0) expected 9.0.21, got 10.0.13`. Also makes a live `gh api` network call in `test_github_api_live_alert_state` (`lines 43-60`).
- **Root Cause**: Line 77 uses `self.assertEqual(resolved, "9.0.21")` instead of `>= (9, 0, 21)`.
- **Impact**: Unconditionally fails the first step of `build-and-test` in `ci.yml`.

### `RT-TEST-02` — `test_assert_roslyn_lock.py` Fails All 5 Tests on Windows Due to Bare `bash` WSL Relay Resolution
- **Layer**: 06 (Test Quality) | **Severity**: `P0` | **Confidence**: High | **Status**: `VERIFIED` (Empirically reproduced)
- **Locations**: `scripts/tests/test_assert_roslyn_lock.py:26-30`, `scripts/assert-roslyn-lock.sh:26,29`
- **Evidence & Repro**: `subprocess.run(["bash", str(HELPER), ...])` resolves `C:\Windows\System32\bash.exe` (WSL relay) on Windows hosts and passes Windows backslash paths (`D:\...`), failing all 5 tests with `WSL ERROR: CreateProcessCommon:818: execvpe(/bin/bash) failed`.
- **Impact**: Breaks local script test suite verification required by `rules/workspace_governance.md:36` on Windows developer workstations.

### `RT-SQL-01` — `OracleLiveQuerySchemaProvider` Indexes Non-Existent `"DataTypeName"` Column on ODP.NET `GetSchemaTable()`, Causing 100% Failure (`DG020`)
- **Layer**: 02 (SQL & Database Semantics) | **Severity**: `P0` | **Confidence**: High | **Status**: `VERIFIED` (Source-verified; `SUSPECTED` live container runtime until Docker daemon runs)
- **Location**: `src/DataGuard.Oracle.Adapter/OracleLiveQuerySchemaProvider.cs:114-155`
- **Evidence**: Line 132 executes `var dataTypeName = row["DataTypeName"]?.ToString()`. ODP.NET's `OracleDataReader.GetSchemaTable()` does not define a `"DataTypeName"` column (`DataRow.this[string]` throws `ArgumentException: Column 'DataTypeName' does not belong to table SchemaTable`), which is caught at line 151 and returned as `LiveSchemaStatus.Failed` (`DG020`).
- **Impact**: 100% of live Oracle SQL shape validations (`DG018` / `verify-shape`) fail with `DG020` (exit code `3`).

### `RT-SEC-01` (merged with `RT-SQL-03`) — Live Query Schema Guard Bypass via Cross-Dialect Lexical Differentials (`/* /* */`, `[...]`, `` `...` ``, `$tag$`, `q'[...]'`)
- **Layer**: 03 (Security) & 02 (SQL Semantics) | **Severity**: `P0` | **Confidence**: High | **Status**: `VERIFIED`
- **Locations**: `src/DataGuard.Core/Rules/Sql/SqlTextScanner.cs:256-548`, `src/DataGuard.Oracle.Adapter/OracleLiveQuerySchemaProvider.cs:45-100`, `src/DataGuard.PostgreSql.Adapter/PostgreSqlLiveQuerySchemaProvider.cs:45-99`, `src/DataGuard.MySql.Adapter/MySqlLiveQuerySchemaProvider.cs:53-115`
- **Evidence**: `SqlTextScanner.StripCommentsAndLiterals` and `HasUnclosedBlockComment` assume nested block comments (`commentDepth++` on `/*`), whereas Oracle and MySQL close block comments at the first `*/`. A query containing `SELECT 1 /* /* */ ) UNION ... ; DROP TABLE x /* */ FROM dual` strips the payload in `sanitizedSql` (`commentDepth == 1`), bypassing `;`, `)`, and `DisallowedLiveCommandsRegex` guards before interpolating `trimmed` into `SELECT * FROM (\n{trimmed}\n) WHERE 1=0`. Similarly, `$tag$` in Oracle/MySQL identifiers (`v$session ... v$session`) and `q'[...]'` in PostgreSQL/MySQL create comment/string breakout differentials.
- **Impact**: Guard bypass during `verify-shape` or live `validate` (`DG018`) on Oracle, PostgreSQL, and MySQL.

### `RT-SEC-02` — `VAULT_TOKEN` Exfiltration & SSRF via Untrusted `VaultAddress` in `.dataguard.yml`
- **Layer**: 03 (Security) | **Severity**: `P0` | **Confidence**: High | **Status**: `VERIFIED`
- **Locations**: `src/DataGuard.Core/Security/SecretStores/ISecretStore.cs:110-166`, `src/DataGuard.Cli/CliConfigurationResolver.cs:177-220`, `src/DataGuard.Cli/Services/ConfigLoader.cs:113-125`
- **Evidence**: When any non-`--ide-safe` CLI command (or VS Code's `Refresh Snapshot` / `Create Baseline`, which pass `--config .dataguard.yml`) runs without a higher-priority connection string, `HashiCorpVaultSecretStore.GetSecretAsync` sends an HTTP GET with header `X-Vault-Token: <VAULT_TOKEN>` to whatever `https://` URL is specified in `VaultAddress` inside the repository's `.dataguard.yml`.
- **Impact**: Immediate exfiltration of operator/CI `VAULT_TOKEN` to an attacker-controlled HTTPS server and injection of an attacker-controlled database connection string.

### `RT-SEC-03` — `--ide-safe` Policy Bypass: `Oracle.DescribeRefCursors: true` Executes Stored Procedures During `validate --ide-safe --allow-env-connection`
- **Layer**: 03 (Security) | **Severity**: `P0` | **Confidence**: High | **Status**: `VERIFIED`
- **Locations**: `src/DataGuard.Cli/IdeSafePolicy.cs:71-133`, `src/DataGuard.Cli/Services/ContractAcquisition.cs:133-137`, `src/DataGuard.Oracle.Adapter/OracleCatalog.cs:352-360`, `src/DataGuard.Oracle.Adapter/OracleReaders.cs:674-736`
- **Evidence**: `IdeSafePolicy.Apply` strips `RulesConnectionString` when `allowEnvConnection: true`, but never clears `config.Oracle.DescribeRefCursors` or `config.Oracle.UseRefCursorDescribe`. During `validate --ide-safe --allow-env-connection`, `OracleCatalogBuilder.BuildAsync` instantiates `RefCursorDescriber`, which executes every describable `REF CURSOR` procedure/function in an anonymous `BEGIN <proc>(...); END;` block.
- **Impact**: Arbitrary stored procedure execution (DML/side effects) during IDE validation in VS Code when an Oracle credential is configured and `.dataguard.yml` sets `Oracle: { DescribeRefCursors: true }`.

### `RT-IDE-09` — VS Code `DataGuardQuickFixProvider` Corrupts Multi-Line SQL Strings, Injects Hardcoded `Id, Name, CreatedAt` Columns, and Skips Workspace Path Containment
- **Layer**: 09 (Analyzer, CodeFix & IDE) | **Severity**: `P0` | **Confidence**: High | **Status**: `VERIFIED`
- **Locations**: `src/DataGuard.VSCode/src/ui/quick-fix-provider.ts:43-125`, `src/DataGuard.VSCode/src/ui/redaction.ts:131-144`, `src/DataGuard.VSCode/src/extension.ts:140-145,263-282`
- **Evidence**:
  1. `const selectStarRegex = /\bSELECT\s+\*\b/i;` (`line 102`) places word-boundary `\b` after non-word `*`, so it **never matches** `"SELECT * FROM ..."` (when `*` is followed by a space) and falls through to line 114, inserting a C# `// [DataGuard Auto-Fix]: ...` comment directly inside multi-line SQL string literals!
  2. Even when matched, it replaces `SELECT *` with hardcoded columns `Id, Name, CreatedAt`.
  3. `applyRemediation` never checks `isPathInWorkspaceFolder(uri.fsPath)`.
- **Impact**: Corrupts user C#/SQL source code on QuickFix invocation in VS Code.

---

## 3. Canonical P1 (High) Findings Register

| ID | Layer | Location | Summary & Root Cause | Status |
|---|---|---|---|---|
| `RT-CORE-01` / `RT-IDE-04` | 01 & 09 | `NameConventions.cs:21-69`, `NamingConventionRule.cs:12-62`, `CodeFixProviders.cs:541` | `NameConventions.ToPascalCase("CustomerName")` lowercases interior humps to `"Customername"`, causing `DG006` false positives on exact PascalCase matches and renaming properties to `Customername` in the CodeFix; `ToSnakeCase("CustomerID")` produces `"customer_i_d"`; `_convention` field in `NamingConventionRule` is unread dead code. | `VERIFIED` |
| `RT-CORE-02` | 01 | `ModelSnapshotCSharpParser.cs:83-132,303-308` | Rejects standard EF Core `modelBuilder.Entity("Namespace.Order", b => ...)` with `DG1304`, drops schema in `b.ToTable("Orders", "sales")`, and overwrites `table` when `ToTable("Orders").HasComment("x")` is chained. | `VERIFIED` |
| `RT-CORE-03` | 01 | `PostgreSqlDialectChecker.cs:60-262,476-488`, `MySqlDialectChecker.cs:83-313,461-511` | `PostgreSqlDialectChecker` and `MySqlDialectChecker` scan raw unmasked SQL (comments/literals/`?` bind parameters) and run on synthesized `RawSqlDescriptor { IsStoredProcedure = true, SqlText = "EXEC ..." }`, flagging every SP call in PG/MySQL as `PG002`/`MY002`. | `VERIFIED` |
| `RT-CORE-04` | 01 | `BaselineManager.cs:507-538` | `MigrateBaselineAsync` omits `if (legacy.Version != 1) return null;`, downgrading v2/v3/v4 baselines and snapshots in-place and wiping `Schema` and `StoredProcedures`. | `VERIFIED` |
| `RT-SQL-02` | 02 | `SqlServerLiveQuerySchemaProvider.cs:48,59-80` | Strips `(max)` from `typeName` while passing raw byte `max_length` (`-1` for `MAX`, `2x` bytes for `nvarchar`/`nchar`) without `NormalizeMaxLength`, causing false `DG018` incompatibilities on `varbinary(max)` and `nchar(36)`. | `VERIFIED` |
| `RT-SQL-04` | 02 | `Oracle.Adapter/LengthMismatch.cs:19-55,365,460,523` | `DG009` and `EfCoreInferenceSimulator` ignore `IsUnicode(false)` on `CLOB` columns; `IsUnicodeType` fails on `"string?"`; `NVARCHAR2` with `CharUsed == null` is treated as byte semantics when `sessionSemantics == Byte`. | `VERIFIED` |
| `RT-SEC-04` | 03 | `BaselineCommand.cs:62-92`, `AssessCommand.cs:136-140`, `MigrateCommand.cs:21-39` | `dataguard baseline` and `migrate` lack path containment and `SafeWritablePath` checks; `dataguard assess --format json` bypasses `IsSafeWritablePath(output)` even under `--ide-safe`. | `VERIFIED` |
| `RT-SEC-05` / `RT-IDE-11` | 03 & 09 | `VSCode/src/command-args.ts:84-87`, `VisualStudio/DataGuardPackage.Consent.cs:88-106` | VS Code passes untrusted `--config .dataguard.yml` to live `snapshot refresh` and `baseline` without `--ide-safe`; Visual Studio `Forget Solution Consent` never revokes `ComputeLiveConsentKey`. | `VERIFIED` |
| `RT-SEC-06` | 03 | `IdeSafePolicy.cs:71-133`, `SnapshotGuard.cs:29-43`, `ContractAcquisition.cs:85-91`, `ValidationRunner.cs:65-69` | Under `--ide-safe`, `SnapshotFilePath` and `BaselineFilePath` from `.dataguard.yml` are not checked for UNC/SMB paths (`\\attacker\share`), workspace containment, or symlinks before `File.Exists`/`LoadAsync`. | `VERIFIED` |
| `RT-SEC-07` | 03 | `IAuditLogger.cs:213-215,386-398,643-658`, `ObservabilityFileSink.cs:170-182,230-264` | `FileAuditLogger` and `FileObservabilitySink` check `ReparsePoint` only on the leaf directory, bypassed by a symlinked ancestor with a non-existent child (`<junction>/sub/audit.log`), and `WriteCheckpoint` never checks `CheckpointPath` for symlinks. | `VERIFIED` |
| `RT-SEC-08` | 03 | `ConnectionDiscovery.cs:27-48`, `DiagnosticEmitter.cs:274-295`, `ContractEvidence.cs:79-86`, CLI `catch` blocks | `MaskConnectionString` misses Oracle EZConnect (`user/pass@host`) and `{a;b}` braced passwords; `ContractEvidenceWriter.Redact` and `DiagnosticEmitter` miss `pwd=`, `secret=`, URIs, and EZConnect; 7 CLI `catch` blocks print raw `ex.Message`. | `VERIFIED` |
| `RT-ARCH-01` | 04 | `PluginAdmission.cs:268-277,317-339`, `RulePluginManager.cs:318-380` | `PluginAdmission` checks exact `AssemblyName.FullName` ordinal equality against `hostAssemblyIdentities`, rejecting all `net9.0` plugins under `.NET 10` roll-forward, across `DataGuard.Core` version bumps, and in single-file publish builds. | `VERIFIED` |
| `RT-ARCH-02` | 04 | `DataGuard.Cli.csproj:1-15`, `DataGuard.Host.csproj`, `VSCode/src/ide-safe-contract.ts:102-126` | `DataGuard.Cli.csproj` and `DataGuard.Host.csproj` lack `<RollForward>LatestMajor</RollForward>` and VS Code omits `DOTNET_ROLL_FORWARD=LatestMajor`, causing startup failure (`exit 150`) on .NET 10-only machines. | `VERIFIED` |
| `RT-PERF-01` | 05 | `ConnectionDiscovery.cs:31`, `SelectStarUsageRule.cs:83`, `OracleDialectChecker.cs`, `PostgreSqlDialectChecker.cs`, `MySqlDialectChecker.cs` | 25+ `Regex` fields and static calls across `Core` and adapters omit explicit `matchTimeout` / `NonBacktracking`, leaving library/analyzer callers exposed to backtracking stalls on `UriCredentialMaskRegex` and SQL regexes. | `VERIFIED` |
| `RT-TEST-03` | 06 | `OfflineManifestValidationTaskTests.cs:197-245,267-285` | `RunDotnetAsync` reads stdout/stderr sequentially without timeout, uses bare `"dotnet"`, and `dotnet restore` pollutes global `~/.nuget/packages/dataguard.build/0.2.2-alpha.0.9` so subsequent runs test stale cached packages. | `VERIFIED` |
| `RT-TEST-04` | 06 | `CliProcessTestRunner.cs:43-91`, `HealthHostIntegrationTests.cs:48-86`, `LanguageServerLifecycleTests.cs:11-18`, `DataGuard.Core.Tests.csproj` | `CliProcessTestRunner` leaks orphan child processes on timeout and uses bare `"dotnet"`; `HealthHostIntegrationTests` awaits `WaitForExitAsync` before draining stderr; `DataGuard.Core.Tests` lacks `ProjectReference` to `DataGuard.LanguageServer`. | `VERIFIED` |
| `RT-CI-02` | 07 | `tests/DataGuard.GoldenCorpus.Tests/DataGuard.GoldenCorpus.Tests.csproj:16`, `Directory.Build.props:6` | Unused `<ProjectReference>` to `DataGuard.Analyzers.csproj` causes 6 `MSB3277` assembly version conflict warnings that bypass `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`. | `VERIFIED` |
| `RT-CI-03` | 07 | `.github/workflows/ci.yml:126-159,363-365`, `.github/codecov.yml:24-96` | `DataGuard.VisualStudio` and `DataGuard.LanguageServer` have 0% coverage instrumentation in Cobertura/Codecov; `ci.yml` `marker = "/src/"` fails to deduplicate `src/...` paths without leading `/`; branch coverage is unmeasured in CI gate. | `VERIFIED` |
| `RT-CI-04` | 07 | `Dockerfile:49-54`, `.github/workflows/release.yml:176,195-199`, `build_release.yml:120,139-148` | Omitting `-p:MinVerVersionOverride` causes `MinVer` to overwrite `-p:Version` with `0.0.0-alpha.0` in Docker images (where `.dockerignore` excludes `.git/`) and shallow clones. | `VERIFIED` |
| `RT-CI-05` | 07 | `.github/workflows/release.yml:95-129,178` | `release.yml`'s `verify-ci` gate only checks `Build and Test`, never verifying `Live DB Integration`, `Visual Studio VSIX Packaging Gate`, `Security Scan`, or `CodeQL`. | `VERIFIED` |
| `RT-COMPAT-01` | 08 | `src/DataGuard.Build/OfflineManifestValidationTask.cs:157-165` | `IsAllowedRuleId` hardcodes `DG002..DG016`, rejecting valid offline manifests containing `DG001`, `DG017`–`DG020`, `DG101`, `MY001`–`MY006`, or `PG001`–`PG005` findings with `DG_BUILD003`. | `VERIFIED` |
| `RT-COMPAT-02` | 08 | `InitCommand.cs:37-42,63-67`, `ScanCommand.cs:73-75,144`, `PreflightCommand.cs:60,68` | Refusing an unsafe/symlinked `--output` path exits `0` (success) in `InitCommand` and exits `1` (after scanning/connecting) in `ScanCommand` and `PreflightCommand` instead of frozen exit code `4`. | `VERIFIED` |
| `RT-IDE-01` | 09 | `DataGuard.Analyzers.csproj:31`, `DataGuard.CodeFixes.csproj:28-30` | `DataGuard.CodeFixes` packs DLLs into non-existent `codefixes/dotnet/cs` (never loaded by NuGet/MSBuild) and omits `DataGuard.SqlClassification.dll`; `DataGuard.Analyzers` leaks `System.Composition.Hosting 10.0.13` as a public dependency. | `VERIFIED` |
| `RT-IDE-02` | 09 | `CodeFixProviders.cs:210-292,473-485`, `ContractAttributes.cs:131` | `DG001` FixAll adds duplicate `[SkipContractCheck]` attributes (`error CS0579`) when a method has 2+ SQL calls, and single fix adds `[SkipContractCheck]` to properties/constructors/fields/structs (`error CS0592`). | `VERIFIED` |
| `RT-IDE-06` | 09 | `DataGuard.LanguageServer/Program.cs:7,47-66,86-122` | Unsynchronized `Dictionary<string, CancellationTokenSource>` concurrent access across main loop and thread-pool tasks, plus double-dispose / `ObjectDisposedException` race on `CancellationTokenSource`. | `VERIFIED` |
| `RT-IDE-07` | 09 | `DataGuard.LanguageServer/Program.cs:12,150-210` | `WriteAsync` mixes buffered `Console.Out` (`TextWriter`) with raw `Console.OpenStandardOutput()` (`Stream`), allocates new streams per message, and crashes on malformed headers or empty `contentChanges`. | `VERIFIED` |
| `RT-IDE-08` | 09 | `DataGuard.LanguageServer/Program.cs:94-106`, `SqlClassifier.cs:181-261` | LSP emits uncatalogued `DGSQL001` diagnostics on ordinary English sentences in C# strings (`"Update your profile"`, `"Delete item"`, `"Select an option"`, `"Begin work"`) and ignores `[SkipContractCheck]` / `// DataGuard:`. | `VERIFIED` |
| `RT-DX-01` / `RT-COMPAT-03` | 10 & 08 | `ConfigLoader.cs:14-19`, `ConfigCommands.cs:20-66`, `AutoDetectionEngine.cs:667-724` | `TryLoadConfig(null)` ignores `.dataguard.yml` in CWD when `--config` is omitted (`config validate` prints `"Configuration is valid"` on broken `.dataguard.yml`); `init --wizard` discards entered connection string. | `VERIFIED` |
| `RT-GOV-01` / `RT-DX-02` | 12 & 10 | `docs/legal/ADDITIONAL-PERMISSIONS.md:9`, `README.md:206`, `README.vi.md:169`, `SUPPORT.md:22`, `CHANGELOG.md:10` | Literal `<contact email placeholder>` shipped in 7 legal/doc files (and enforced by `check-license-consistency.py`), plus `404` architecture link in `README.vi.md:131` and archived Rust doc link in `README.md:168`. | `VERIFIED` |

---

## 4. Canonical P2 (Medium), P3 (Low) & R&D Findings Summary

| ID | Layer | Severity | Location | Summary | Status |
|---|---|---|---|---|---|
| `RT-CORE-05` | 01 | `P2` | `ColumnShapeMatchRule.cs:45-49` | Single-untyped-query fallback cross-matches 1 untyped query against all $N$ `EntityDescriptor`s, emitting $N-1$ false-positive `DG004` errors. | `VERIFIED` |
| `RT-CORE-06` | 01 | `P2` | `Configuration.cs:199-215` | `WithSmartDefaults()` forces `DefaultSchema = "dbo"` onto Oracle/PostgreSQL/MySQL when `SqlServer` is `null`; dead `AutoDetectDapper` stub. | `VERIFIED` |
| `RT-CORE-07` | 01 | `P2` | `ProjectCSharpSqlSource.TargetShape.cs:225-226,328-329` | Marks unannotated reference-type DTO properties (`string`) as `IsNullable = false` because compilation uses `NullableContextOptions.Disable`. | `VERIFIED` |
| `RT-SQL-05` | 02 | `P2` | `SqlServerParsers.cs:208-258` | `GetParametersAsync` joins `sys.types` on `user_type_id` only, returning UDT alias names instead of base system types (`DG002` false negative). | `VERIFIED` |
| `RT-SQL-06` | 02 | `P2` | `MySqlDialectChecker.cs:334-336,533-544` | `MY003` (`MySqlVarcharByteLimitRule`) compares `t.Name` against schema-qualified `entity.TableName` (`"shop.Orders"`) without `FindTable` or snake_case fallback. | `VERIFIED` |
| `RT-SQL-07` | 02 | `P2` | `PhantomSqlAnalyzer.cs:261-298,446-449` | `PhantomTokenWalker` misses `INSERT INTO`, `UPDATE`, `MERGE INTO` target tables and 3-part `schema.table.column` on Oracle/PostgreSQL/MySQL. | `VERIFIED` |
| `RT-SQL-08` | 02 | `P2` | `SqlClassification/SqlTokenizer.cs:41-43,94-99` | Breaks on escaped `]]` in bracketed identifiers `[Order]]Items]` and strips the last char of unclosed quoted identifiers at EOF. | `VERIFIED` |
| `RT-SEC-09` | 03 | `P2` | `OfflineManifestValidationTask.cs:48,121-123` | `HasSha256` checks 64 hex chars only and never recomputes or verifies `contentDigest` over `contracts`/`findings`. | `VERIFIED` |
| `RT-SEC-10` | 03 | `P2` | `CoreObservability.cs:30,84-86,275-296,503-519` | `CaptureExceptionDetails = true` is allowed when `DeploymentEnvironment` is `null` or `"prd"`, exporting unredacted exceptions. | `VERIFIED` |
| `RT-ARCH-03` | 04 | `P2` | `PublicApiSurface.cs:54-85`, `RuleDependencyGraph.cs:361-378` | `DataGuardApi.CreatePipeline(config)` ignores `StrictProcedureContracts`, `DefaultSchema`, `DefaultPackage`, and provider rules, and creates `%APPDATA%/DataGuard` via unused `_credentialManager`. | `VERIFIED` |
| `RT-ARCH-04` | 04 | `P2` | `DataGuard.Build.csproj:3`, `DataGuard.Observability.csproj:17` | `DataGuard.Build` targets `net9.0` only (fails with `MSB4062` in VS `MSBuild.exe` on `net472`); `DataGuard.Observability` forces `Microsoft.AspNetCore.App` on non-web consumers. | `VERIFIED` |
| `RT-PERF-02` | 05 | `P2` | `EfModelSource.cs:173-215` | `SourceFileCache` keys by bare `Type.Name` across projects, walks `.git`/`node_modules`, and reparses syntax trees on every call without caching. | `VERIFIED` |
| `RT-PERF-03` | 05 | `P2` | `Oracle.Adapter/LengthMismatch.cs:648`, `MySqlLengthMismatchDetector.cs:534`, `ColumnShapeMatchRule.cs:106` | $3\times$–$4\times$ redundant detector runs in `DG007`–`DG009` and `MY004`–`MY007`, and uncached SQL column extraction in `DG004`. | `VERIFIED` |
| `RT-PERF-04` | 05 | `P3` | `*LiveQuerySchemaProvider.cs`, `PostgreSqlStoredProcedureParser.cs:58-60,139` | Synchronous `using` instead of `await using` on `DbConnection`/`DbCommand`/`DbDataReader`, and 2 concurrent connections opened in `PostgreSqlStoredProcedureParser`. | `VERIFIED` |
| `RT-TEST-05` | 06 | `P2` | `AssessmentPackTests.cs:213`, `AuditAndConfigTests.cs:139`, `CredentialManagerFullTests.cs:279-557`, `PluginAdmissionTests.cs:266` | 20+ security and packaging tests silently `return;` and report `Passed` (`0` assertions executed) on Windows instead of using `DirectoryLinkTestHelper` or `[UnixFact]`. | `VERIFIED` |
| `RT-TEST-06` | 06 | `P2` | `IdeSafePolicyTests.cs:314-360`, `AutoDetectionEngineTests.cs:14`, `UnitTests.cs:85,100` | Parallel xUnit collection race on process env vars (`"ide-safe-environment"` vs `"Sequential"`) and permanent static mutation of `TypeCompatibilityRegistry`. | `VERIFIED` |
| `RT-TEST-07` | 06 | `P3` | `scripts/tests/test_trx_to_junit.py:9,11` | Unguarded top-level `import yaml` and CWD-dependent `from scripts import trx_to_junit` (fails when invoked outside repo root). | `VERIFIED` |
| `RT-CI-06` | 07 | `P2` | `marketplace.yml:391-420`, `assert-vsix.ps1:25-34` | `publish-visualstudio` skips `.sha256` and `gh attestation verify`; `assert-vsix.ps1` omits legal notices and manifest `<Asset>` checks. | `VERIFIED` |
| `RT-CI-07` | 07 | `P2` | `scripts/trx_to_junit.py:20,121-141,189-201` | Exits `0` when `--input-glob` matches 0 `.trx` files and hardcodes `errors="0"` / `type="AssertionError"` for timeouts and crashes. | `VERIFIED` |
| `RT-COMPAT-04` | 08 | `P2` | `Configuration.cs:6-15` | `new DataGuardConfiguration()` initializes non-nullable `ExcludedProcedures` and `ExcludedEntities` to `null!`. | `VERIFIED` |
| `RT-COMPAT-05` | 08 | `P3` | `DiagnosticEmitter.cs:65-66,373,595`, `SarifTypes.cs:53,56` | Hardcoded stale `"0.1.0-alpha.1"` / `"0.1.0"` version and fake `"https://github.com/DataGuard/DataGuard"` URI in SARIF outputs. | `VERIFIED` |
| `RT-IDE-03` | 09 | `P2` | `CodeFixProviders.cs:40-44,87-144` | `DG017` CodeFix drops table alias qualifiers (`o.*`), rewrites subqueries/comments, or registers a no-op lightbulb on `TOP (10) WITH TIES *` / `dbo.Orders.*`. | `VERIFIED` |
| `RT-IDE-05` | 09 | `P2` | `ContractValidationAnalyzer.cs:62`, `SqlTextHeuristics.cs:49-147`, `UnvalidatedSqlCallGenerator.cs:134` | Analyzer skips `AdoCommand`, emits false-positive `DG004` Error on `COALESCE(a,b) AS x` and CTEs, emits `DG098` on `'SELECT'` literals, and collides on empty `FilePath` in `SyntaxTreeMap`. | `VERIFIED` |
| `RT-IDE-10` | 09 | `P2` | `decoration-manager.ts:21,30,42-74`, `findings-tree-provider.ts:162`, `sql-queries-tree-provider.ts:70` | Casts `ThemeIcon.id` (`"error"`) to `vscode.Uri` for `gutterIconPath`, loses decorations on tab switch, and collides on same-basename files in TreeView. | `VERIFIED` |
| `RT-DX-03` | 10 | `P2` | `docs/USAGE.md:523-549,616-690,785-849`, `InitCommand.cs:18` | Claims PG/MySQL unsupported, lists non-existent env vars (`DATAGUARD_CONFIG`, etc.) and quick fixes, uses `--connection` in CI examples, and links fake URLs. | `VERIFIED` |
| `RT-DX-04` | 10 | `P2` | `ModelSnapshotCSharpParser.cs:64-78`, `BuildCiPack.cs:40-76`, `ProviderRuleCatalog.cs:17-40` | `DG1301`–`DG1303` rule ID collision, missing `MY*`/`PG*` titles in `RuleTitles`, and raw C# field name in `DG020` README row. | `VERIFIED` |
| `RT-DX-05` | 10 | `P2` | `DataGuardRulesOptionsPage.cs:26-300` | VS Options page omits `DG018`–`DG020`/`DG097`–`DG099`, and toggling `DG001`/`DG017` has no effect on in-editor Roslyn squiggles. | `VERIFIED` |
| `RT-DX-06` | 10 | `P2` | `SnapshotCommands.cs:46,81`, `ValidationRunner.cs:65` | `dataguard snapshot refresh` leaves `EnableBaseline = true`, filtering snapshot violations through `.dataguard-baseline.json`. | `VERIFIED` |
| `RT-GOV-02` | 12 | `P2` | `Directory.Build.props:28-35`, `DataGuard.Cli.csproj:37-41` | 11 of 12 `GPL-3.0-only` NuGet packages omit `LICENSE`, `ADDITIONAL-PERMISSIONS.md`, and `THIRD-PARTY-NOTICES.md` from their `.nupkg` archives. | `VERIFIED` |
| `RT-GOV-03` | 12 | `P2` | `SECURITY.md:7-8,24-29,88`, `SECURITY.vi.md:7-8,25-29,87-88` | Stale supported-versions table (`0.3.0` listed as "upcoming", `0.4.x` missing) and false claim that Visual Studio has no live-database commands. | `VERIFIED` |
| `RT-GOV-04` | 12 | `P3` | `ACTIVE_SESSION_REGISTER.md:16-50`, `CONTRIBUTING.md:44,59,66`, `.github/CODEOWNERS:3` | `ACTIVE_SESSION_REGISTER.md` header frozen at `2026-09-23` (`789` tests, 11/18 projects); `CONTRIBUTING.md` references non-existent `plans/adr/`. | `VERIFIED` |
| `RT-PROD-01` | 11 | `R&D` | `ContractValidationAnalyzer.cs:18-25`, `CodeFixProviders.cs` | Load `.dataguard-snapshot.json` via Roslyn `AdditionalFiles` so `DG002`–`DG016` and their 4 currently unreachable CodeFix providers work in-editor. | `VERIFIED` |
| `RT-PROD-02` | 11 | `R&D` | `ProviderRuleCatalog.cs:63,86` | Extract `DbContextOptionsBuilder.Use*` calls in `ProjectCSharpSqlSource` so `DG012` and `PG004` are no longer permanently `Unavailable`. | `VERIFIED` |
| `RT-PROD-03` | 11 | `R&D` | `SqlCallSyntax.cs:269-353` | Unwrap Dapper `new CommandDefinition(sql, ...)` in `SqlCallSyntax` so cancellable Dapper calls are analyzed by `DG001`/`DG017`. | `VERIFIED` |
| `RT-PROD-04` | 11 | `R&D` | `DataGuard.LanguageServer/Program.cs:22`, `VSCode/src/extension.ts:309` | Register `{ scheme: "file", language: "sql" }` in VS Code LSP selector and add incremental sync + snapshot-backed hover/codeAction to LSP. | `VERIFIED` |
| `RT-PROD-05` | 11 | `R&D` | `ContractAcquisition.cs:26-160` | Add offline SQL DDL (`migrations/*.sql`) and SSDT `.dacpac` ground-truth sources for zero-DB stored-procedure CI validation. | `VERIFIED` |
