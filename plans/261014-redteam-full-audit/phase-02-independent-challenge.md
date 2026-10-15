# Phase 2 — Independent 5-Role Adversarial Challenge (`eco_support_net_oracle`)

- **Audit Date**: 2026-10-14
- **Challenger Roles**: Security Reviewer, Correctness Reviewer, Architecture Reviewer, QA Reviewer, Product Reviewer (`ck:advisor` + TypeSafe System One `risk_security_triage` & `typesafe_advisor`)
- **Overall Challenge Verdict**: `REVISE` (Confidence: `0.97`)
  - **7 of 8 `P0` findings confirmed as unmitigated `P0` blockers** (`RT-CI-01`, `RT-TEST-01`, `RT-TEST-02`, `RT-SQL-01`, `RT-SEC-02`, `RT-SEC-03`, `RT-IDE-09`).
  - **1 `P0` downgraded to `P1`** (`RT-SEC-01`) due to existing `CommandBehavior.SchemaOnly`, `--ide-safe` `RulesConnectionString = null`, and MySQL `SET SESSION TRANSACTION READ ONLY` mitigations.
  - **2 `P1` findings downgraded to `P2`** (`RT-SQL-02` because `MappedTypeCompatibility` returns `Unknown` rather than `Incompatible` for `nchar(36)` byte length `72`; `RT-SEC-07` because `AuditLogPath` and `TelemetryFileDirectory` are already stripped under `--ide-safe`).
  - **2 `P1` claims refined for exact precision** (`RT-CORE-01`/`RT-IDE-04` and `RT-CORE-02`).
  - **Product R&D proposals triaged**: `RT-PROD-01`, `RT-PROD-02`, `RT-PROD-03`, and the 1-line `.sql` selector of `RT-PROD-04` approved for promotion to `P2`; `RT-PROD-05` (`.dacpac`/DDL simulation) and full LSP hover/codeAction rewrite deferred to prevent scope creep.

---

## 1. Five-Role Adversarial Challenge Matrix

### Role 1: Security Reviewer Challenge (`RT-SEC-01`..`10`, `RT-IDE-11`)

| Finding ID | Initial Severity | Challenged Severity | Adversarial Challenge & Verification Evidence |
|---|---|---|---|
| `RT-SEC-01` | `P0` Critical | **`P1` High (Downgraded)** | **Mitigations verified in source**: (1) Under `--ide-safe` (including `--allow-env-connection`), `IdeSafePolicy.cs:96,127` sets `RulesConnectionString = null`, so `LiveSqlShapeValidationRule` (`DG018`) never executes during IDE `validate`. (2) All 3 providers pass `CommandBehavior.SchemaOnly`: Oracle rejects `;` stacked statements (`ORA-00911`) and only describes; Npgsql sends protocol `Parse`+`Describe` without `Execute`; MySQL (`MySqlLiveQuerySchemaProvider.cs:98-102`) executes `SET SESSION TRANSACTION READ ONLY` and `SET SESSION max_execution_time = 5000`. However, the cross-dialect lexer differentials (`/* /* */` nested comments, `$tag$`, `q'[...]'`) genuinely bypass `StripCommentsAndLiterals` / `HasUnclosedBlockComment` during live `verify-shape` and non-`--ide-safe` `validate`, and neither Oracle nor PostgreSQL sets a read-only transaction before `SchemaOnly`. |
| `RT-SEC-02` | `P0` Critical | **`P0` Critical (Confirmed)** | **Exploitability verified**: Requires `VAULT_TOKEN` set in environment (`ISecretStore.cs:132`). When set, `ZeroTrustCredentialProvider.cs:66-70` calls `HashiCorpVaultSecretStore` *before* local credential stores whenever `DATAGUARD_CONNECTION_STRING` is unset, transmitting `X-Vault-Token` to any `https://` `VaultAddress` in `.dataguard.yml`. Furthermore, VS Code's `Refresh Snapshot` and `Create Baseline` pass `--config .dataguard.yml` without `--ide-safe` (`RT-SEC-05`). |
| `RT-SEC-03` | `P0` Critical | **`P0` Critical (Confirmed)** | **Exploitability verified**: During `validate --ide-safe --allow-env-connection`, `IdeSafePolicy.Apply` keeps the environment connection for `ContractAcquisition.AcquireGroundTruthContractsAsync` (`ContractAcquisition.cs:133`), which passes `config.Oracle` straight to `OracleCatalogBuilder.BuildAsync` (`OracleCatalog.cs:352`). When `.dataguard.yml` sets `Oracle: { DescribeRefCursors: true }`, `RefCursorDescriber` executes `DECLARE ... BEGIN <proc>(...); END;` via `ExecuteNonQueryAsync` (`OracleReaders.cs:733`) without any transaction guard or modal confirmation. |
| `RT-SEC-04` | `P1` High | **`P1` High (Confirmed)** | `BaselineCommand.cs:62-92` and `MigrateCommand.cs:21-39` perform zero path containment or `IsSafeWritablePath` checks; `AssessCommand.cs:139` (`--format json`) skips `IsSafeWritablePath(output)` even under `--ide-safe`. |
| `RT-SEC-05` / `RT-IDE-11` | `P1` High | **`P1` High (Confirmed)** | VS Code `command-args.ts:84-87` passes `--config <repo>/.dataguard.yml` to `snapshot refresh` and `baseline` without `--ide-safe` (violating Visual Studio's security boundary in `CliArgumentBuilder.cs:106-108`). Visual Studio's `ForgetSolutionConsentAsync` (`DataGuardPackage.Consent.cs:99-105`) calls `ForgetConsent(ComputeConsentKey(...))` but omits `ForgetConsent(ComputeLiveConsentKey(...))`. |
| `RT-SEC-06` | `P1` High | **`P1` High (Confirmed)** | `IdeSafePolicy.Apply` preserves `SnapshotFilePath` and `BaselineFilePath` from `.dataguard.yml` without checking for UNC/SMB paths (`\\attacker\share\snap.json`), device paths, symlinks, or workspace containment before `File.Exists`/`LoadAsync`. |
| `RT-SEC-07` | `P1` High | **`P2` Medium (Downgraded)** | `FileAuditLogger` (`IAuditLogger.cs:386`) and `FileObservabilitySink` (`ObservabilityFileSink.cs:170`) only check `ReparsePoint` on the leaf directory, missing symlinked ancestors (`<junction>/sub/audit.log`). Downgraded to `P2` because `IdeSafePolicy.cs:115-125` already disables audit logging and telemetry under `--ide-safe`. |
| `RT-SEC-08` | `P1` High | **`P1` High (Confirmed)** | `ConnectionDiscovery.MaskConnectionString` (`ConnectionDiscovery.cs:27-48`) misses Oracle EZConnect (`scott/tiger@host:1521/svc`) and `{a;b}` brace-escaped passwords containing semicolons; `ContractEvidenceWriter.Redact` and `DiagnosticEmitter` miss `pwd=`, `secret=`, URIs, and EZConnect; 7 CLI `catch` blocks print raw `ex.Message`. |

---

### Role 2: Correctness Reviewer Challenge (`RT-SQL-01`..`08`, `RT-CORE-01`..`07`)

| Finding ID | Initial Severity | Challenged Severity | Adversarial Challenge & Verification Evidence |
|---|---|---|---|
| `RT-SQL-01` | `P0` Critical | **`P0` Critical (Confirmed, Zero Mitigation)** | Verified in `OracleLiveQuerySchemaProvider.cs:132`: `row["DataTypeName"]` on ODP.NET's `GetSchemaTable()` `DataRow` throws `ArgumentException: Column 'DataTypeName' does not belong to table SchemaTable` before the `?? row["DataType"]` fallback can ever execute. Caught at line 151 and reported as `LiveSchemaStatus.Failed` (`DG020`). 100% of live Oracle shape checks fail. |
| `RT-CORE-01` / `RT-IDE-04` | `P1` High | **`P1` High (Confirmed, Refined)** | **Refinement**: `NamingConventionRule.cs:47` compares `columnName.Equals(pascalCaseName, StringComparison.OrdinalIgnoreCase)`, so `ToPascalCase("CustomerName") -> "Customername"` does *not* trigger a false positive in `NamingConventionRule` on exact PascalCase matches. However: (a) `NamingConventionFixProvider` (`CodeFixProviders.cs:541`) **does** rename C# properties to `"Customername"`; (b) `ToSnakeCase("CustomerID") -> "customer_i_d"` **does** trigger false-positive `DG006` violations on every acronym property (`CustomerID` vs `CUSTOMER_ID`); and (c) `_convention` (`NamingConventionRule.cs:14,24`) is unread dead state. |
| `RT-CORE-02` | `P1` High | **`P1` High (Confirmed, Refined)** | **Refinement**: `ModelSnapshotCSharpParser.cs:86` rejects standard EF Core `modelBuilder.Entity("Namespace.Order", b => ...)` with `DG1304` and `TryFirstString` drops the schema argument in `b.ToTable("Orders", "sales")`. At line 119, `chain.Contains("ToTable")` via `AncestorsAndSelf()` does not overwrite `table` on `b.ToTable("Orders").HasComment("x")` (where `ToTable` is a descendant of `HasComment`), but **does** overwrite `table` on EF Core 7+ `b.ToTable("Orders", t => t.HasComment("x"))` (where `ToTable` is an ancestor of `HasComment`). |
| `RT-CORE-03` | `P1` High | **`P1` High (Confirmed)** | `PostgreSqlDialectChecker.cs:482` and `MySqlDialectChecker.cs:477` match `contract is RawSqlDescriptor rawSql` without `{ IsStoredProcedure: false }`, so every `CommandType.StoredProcedure` call (synthesized as `"EXEC <proc>"` by `ProjectCSharpSqlSource`) triggers `PG002`/`MY002`, and unmasked `?` parameters trigger `PG001`. |
| `RT-CORE-04` | `P1` High | **`P1` High (Confirmed)** | `BaselineManager.MigrateBaselineAsync` (`BaselineManager.cs:512-538`) omits `if (legacy.Version != 1) return null;`, silently overwriting v2/v3/v4 baselines/snapshots with `Version = 2` and `Schema = null`. |
| `RT-SQL-02` | `P1` High | **`P2` Medium (Downgraded)** | **Falsified P1 false-positive claim**: `SqlServerTypeCompatibility.cs:37` checks `OneOf(clr, C.ByteArray)` for `varbinary` (ignoring `MaxLength = -1`), and `MappedTypeCompatibility.Character("Guid", db)` (`line 59`) returns `Unknown` (not `Incompatible`) when `nchar(36)` has `MaxLength = 72`. Since `LiveSqlShapeValidationRule.cs:268` only flags `== Incompatible`, no false-positive `DG018` is emitted. Remains a valid `P2` metadata fidelity bug (`MaxLength` in bytes/`-1` instead of chars/`null`). |
| `RT-SQL-04` | `P1` High | **`P1` High (Confirmed)** | `LengthMismatch.cs:365` (`DG009`) and `EfCoreInferenceSimulator.Predict` (`line 22`) ignore `IsUnicode(property)` when `false`, and `IsUnicodeType` (`line 523`) fails on `"string?"`. |

---

### Role 3: Architecture Reviewer Challenge (`RT-CI-01`, `RT-ARCH-01`..`04`, `RT-IDE-01`)

- **`RT-CI-01` (`P0` Confirmed)**: Rejects adding all 5 standalone `.csproj` files to `DataGuard.sln` because `DataGuard.BinaryCompatibilityFixture` intentionally compiles against an external `-p:BaselineCoreAssembly` DLL rather than a project reference. **Approved Architecture**: Regenerate the 5 lockfiles, add the 5 directories to `.github/dependabot.yml`, and enforce lockfile version parity with `Directory.Build.props` in `scripts/tests/`.
- **`RT-ARCH-01` & `RT-ARCH-02` (`P1` Confirmed)**: Approves matching host assemblies in `PluginAdmission` by `(Name, CultureName, PublicKeyToken)` with `Host.Version >= Reference.Version` (and falling back to `AppDomain.CurrentDomain.GetAssemblies()` when `TRUSTED_PLATFORM_ASSEMBLIES` is empty in single-file hosts), while keeping exact `FullName` + SHA-256 verification for plugin-bundled `ManagedDependencies`. Approves `<RollForward>LatestMajor</RollForward>` on CLI/Host/LSP executables.
- **`RT-IDE-01` (`P1` Confirmed)**: Rejects merging `DataGuard.CodeFixes` into `DataGuard.Analyzers` (which would drag `Microsoft.CodeAnalysis.CSharp.Workspaces` into build-only `csc` runs). **Approved Architecture**: Pack `DataGuard.CodeFixes.dll` and `DataGuard.SqlClassification.dll` under `analyzers/dotnet/cs` in `DataGuard.CodeFixes.csproj` with `<DevelopmentDependency>true</DevelopmentDependency>`, and add `PrivateAssets="all"` to `System.Composition.Hosting` in `DataGuard.Analyzers.csproj`.

---

### Role 4: QA Reviewer Challenge (`RT-TEST-01`..`07`, `RT-CI-03`)

- **`RT-TEST-01` (`P0` Confirmed)** & **`RT-TEST-02` (`P0` Confirmed)**: Both verified by direct test execution in Phase 0 (`python3 -m unittest discover -s scripts/tests` failed 6 tests: 1 in `test_alert19_resolution.py` and 5 in `test_assert_roslyn_lock.py`).
- **`RT-TEST-05` (`P2` Confirmed)**: Confirmed that 6 directory-symlink security tests in `DataGuard.Core.Tests` silently `return;` on Windows instead of calling `DirectoryLinkTestHelper.CreateDirectoryLink` (which already exists in `tests/DataGuard.Core.Tests/DirectoryLinkTestHelper.cs` and uses NTFS junctions `mklink /J` without admin rights!), and 4 in-memory `FakeSecretStore` tests in `CredentialManagerFullTests.cs` needlessly bail out on Windows.
- **`RT-CI-03` (`P1` Confirmed)**: Confirmed that `DataGuard.VisualStudio` and `DataGuard.LanguageServer` have 0% coverage instrumentation in Cobertura/Codecov, and `ci.yml:146` (`marker = "/src/"`) fails to collapse relative paths starting with `src/` (no leading `/`).

---

### Role 5: Product Reviewer Challenge (`RT-PROD-01`..`05`)

| Proposal ID | Title | Product Challenge Verdict | Rationale |
|---|---|---|---|
| `RT-PROD-01` | Roslyn `AdditionalFiles` `.dataguard-snapshot.json` integration | **APPROVE (Promote to `P2`)** | Unlocks 4 already-written CodeFix providers (`DG002`, `DG006`, `DG007`/`009`, `DG012`) that are currently dead code in real IDEs because `ContractValidationAnalyzer` never emits `DG002`–`DG016`. |
| `RT-PROD-02` | Extract `DbContextOptionsBuilder` calls to enable `DG012` & `PG004` | **APPROVE (Promote to `P2`)** | Eliminates permanent `RuleAvailability.Unavailable` status on `DG012` and `PG004` that currently breaks `--fail-on-unavailable` on Oracle and PostgreSQL projects. |
| `RT-PROD-03` | Unwrap Dapper `new CommandDefinition(sql, ...)` in `SqlCallSyntax` | **APPROVE (Promote to `P2`)** | High user value (~25 LOC change): `CommandDefinition` is the standard way Dapper users pass `CancellationToken`, and is currently a 100% blind spot in `DataGuard.Analyzers`. |
| `RT-PROD-04` | Expand `DataGuard.LanguageServer` | **SPLIT (Approve `.sql` selector only; Defer full LSP rewrite)** | Adding `{ scheme: "file", language: "sql" }` in `extension.ts:309` is a 1-line fix since `Program.cs:94` already classifies `.sql` files. Defer incremental sync and snapshot-backed LSP hover/codeActions as redundant with `RT-PROD-01`. |
| `RT-PROD-05` | Offline `.dacpac` / SQL migration DDL ground-truth source | **DEFER to v0.5+ Epic** | High complexity (multi-dialect `CREATE`/`ALTER` stateful simulation); should not delay P0/P1 stabilization. |
