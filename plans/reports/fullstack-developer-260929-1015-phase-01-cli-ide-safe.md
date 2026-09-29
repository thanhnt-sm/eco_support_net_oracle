# Phase 1 — CLI ide-safe handshake and contract hardening (TDD)

Plan: `plans/260929-0952-vs-hardening-redteam-tdd-followup/phase-01-cli-ide-safe-handshake-and-contract-hardening.md`
Branch: `feat/vs-extension-hardening` (uncommitted; `packages.lock.json` files untouched by me — pre-existing `M` status left as found).
Status: **completed** (see gate below).

## 1. Tests Before (RED)

Stubs added only so the test project compiled: `IdeSafePolicy.Apply(..., allowEnvConnection = false, environmentConnection = null)` throwing `NotImplementedException` when `allowEnvConnection`; `ProjectCSharpSqlSource.MaxSqlLiteralLength` const. Old `Apply(config, bool)` call shape unchanged.

Command (batch A, after `dotnet build DataGuard.sln -c Release` so the *old* `DataGuard.Cli.dll` was exercised):
```
dotnet test tests/DataGuard.Core.Tests -c Release --no-build --filter "(FullyQualifiedName~IdeSafe|FullyQualifiedName~PhantomIdentifierRuleRegex|FullyQualifiedName~Baseline|FullyQualifiedName~Assess)&FullyQualifiedName!~SelectFollowedBy200KSpaces"
→ Failed: 17, Passed: 66 (the 66 are pre-existing Baseline*/Assessment*/IdeSafePolicy tests + 3 new regression pins)
```
RED (17):
- `IdeSafeEndToEndTests.Validate_IdeSafe_WritesActiveLineFirstAndNeverConnects`
- `IdeSafeEndToEndTests.Validate_IdeSafeAllowEnvConnection_ReportsKeptEnvironmentConnection`
- `IdeSafeEndToEndTests.Validate_AllowEnvConnectionWithoutIdeSafe_IsNoOp` (old CLI rejects unknown option, exit 1)
- `IdeSafeEndToEndTests.Validate_IdeSafeRejectedOption_StillWritesActiveLineFirst`
- `IdeSafeEndToEndTests.Assess_IdeSafe_WritesActiveLineFirst_AndRelativisesToolErrorPath`
- `IdeSafeEndToEndTests.Assess_OutputUnderJunction_WritesSarif`
- `IdeSafeEndToEndTests.Validate_BaselineSuppressesViolation_WarnsInTextAndProgressModes`
- `IdeSafePolicyTests.Apply_AllowEnvConnectionMatrix_...` × 6 rows (all `allowEnv: true` rows; the 6 `allowEnv: false` rows are regression pins and were already green)
- `IdeSafePolicyTests.Apply_AllowEnvConnection_PrefersEnvironmentValueOverConfigValue`
- `IdeSafePolicyTests.Apply_AllowEnvConnectionWithoutEnv_StripsConfigConnection`
- `IdeSafePolicyTests.Apply_ExcessiveParallelismQueueAndTimeout_AreClampedAndReported`
- `PhantomIdentifierRuleRegexTests.ExtractContractsAsync_SqlLiteralOver256KiB_IsSkippedWithNote`

Batch B (the regex DoS test alone, hard timeout):
```
timeout 120 dotnet test ... --filter "FullyQualifiedName~SelectFollowedBy200KSpaces"   → exit 124 after 121 s (did not finish; bound is < 1 s)
```
(The first attempt with all filters in one run hung > 10 min on this test and was killed — same evidence.)

Already-green pins written first (kept as regression guards): `PhantomIdentifierRuleRegexTests.ValidateAsync_SelectListStopsAtFirstFrom_WhenSubqueryFollows`, `..._PhantomSelectListColumn_StillReportsDG016`, `IdeSafeEndToEndTests.Assess_WithoutIdeSafe_PrintsNoHandshake`, `IdeSafePolicyTests.Apply_InBoundsParallelismQueueAndTimeout_AreUntouchedAndUnreported`.

## 2. Files changed

Modified
- `src/DataGuard.Cli/IdeSafePolicy.cs` — `Apply` overload (`allowEnvConnection`, `environmentConnection`), resource clamps, `ActiveLine`/`AllowEnvConnectionOptionName` consts, `FormatKeptConnectionLine`, `WriteReport`.
- `src/DataGuard.Cli/Program.cs` (+57 net lines despite the helper split: `RelativizeToWorkspace` and the baseline block are private to the top-level-statement locals and < 25 lines each; policy/env logic went to `IdeSafePolicy`/`IdeSafeEnvironment`) — `AppContext.SetData("REGEX_DEFAULT_MATCH_TIMEOUT", 1 s)` as first statement; `--allow-env-connection` (validate only); `ide-safe: active` first stderr line for validate/assess (before rejection, before `ProgressEmitter`); env scrub after `Apply`; `IsSafeWritablePath` no longer walks ancestors; `RelativizeToWorkspace` helper; baseline `BaselineApplied` event / stderr line in `ValidateContractsAsync`; assess tool-error path relativised.
- `src/DataGuard.Core/Reporting/ProgressEvent.cs` — `ProgressEventKind.BaselineApplied`.
- `src/DataGuard.Core/Rules/PhantomIdentifierRule.cs` — `SelectListRegex` → `\bSELECT\b\s*(.*?)\s*\bFROM\b`, `NonBacktracking` (no `Compiled`). Matches a superset of the old pattern (`SELECT FROM` with an empty group, `SELECT*FROM` without whitespace); both yield no violation because empty/`*` tokens are skipped, so there is no rule-output change (golden corpus + `PhantomIdentifierRuleTests` green).
- `src/DataGuard.Core/Sources/ProjectCSharpSqlSource.cs` — `MaxSqlLiteralLength = 256 KiB`; `AddDescriptor` skips larger literals with `[WARN] DG1291 ...` (single choke point for all four extraction passes, before any rule/classifier regex runs).
- `tests/DataGuard.Core.Tests/IdeSafePolicyTests.cs` — matrix, clamp, `WriteReport`, `SelectVariablesToClear`, `Scrub` tests (existing tests untouched).

Created
- `src/DataGuard.Cli/IdeSafeEnvironment.cs` (99 lines) — `SelectVariablesToClear` (pure) + `Scrub`.
- `tests/DataGuard.Core.Tests/IdeSafeEndToEndTests.cs` (293 lines, see §5) — process-level tests + `WindowsFactAttribute`.
- `tests/DataGuard.Core.Tests/PhantomIdentifierRuleRegexTests.cs`.

Not changed although listed in the phase: `src/DataGuard.Core/Assessment/Internal/ProjectInventoryReader.cs:123` — `WithError` already carries the workspace-relative `ProjectPath` (set from `TryResolveInsideRoot`; bare file name before that). The absolute paths came only from `AssessmentEngine` (`Path = request.WorkspaceRoot` for DG1000/1005/1006/1007), handled at the CLI echo. `BaselineManager.FilterNewViolations` not given an out-param (file not owned); count computed as before−after in Program.cs.

## 3. Design decisions

Env precedence (verified in `CliConfigurationResolver.Resolve`): `--connection` > `DATAGUARD_CONNECTION_STRING` > config `ConnectionString`. Under `--ide-safe`, `--connection` is rejected before resolution, so whenever env is set the merged value *is* the env value. `Apply` nevertheless receives the raw env value and overwrites `ConnectionString` with it when kept, so a future precedence change cannot leak a config credential. Env value is captured once at the top of the action, before `Scrub`.

`GroundTruthMode × allowEnv × envPresent` (functionally, once a connection is kept every mode reaches the provider branch in `BuildContractsAsync` — Snapshot requires an empty connection, Manual requires the stripped assembly path — so the only decision is which mode survives):

| allowEnv | envPresent | in | ConnectionString | GroundTruthMode | notes |
|---|---|---|---|---|---|
| false | any | any | stripped | Snapshot | unchanged behaviour |
| true | false | any | stripped (config value) | Snapshot | `ConnectionString from config (database access disabled)` |
| true | true | Snapshot | kept (env) | Snapshot | |
| true | true | Full | kept (env) | Full | |
| true | true | Manual | kept (env) | Snapshot | Manual is meaningless without assembly loading → `GroundTruthMode=Manual (forced to Snapshot)` |

Always stripped regardless: `ManualAssemblyPath`, `KeyVaultUri/AwsRegion/VaultAddress`, `AuditLogPath`, telemetry, `EncryptConnectionStringAtRest`.

stderr contract under `--ide-safe` (validate): `ide-safe: active` (line 1, exactly once) → optional rejection line (exit 2) → `ide-safe: suppressed ...` (only when something other than the kept note was suppressed) → `ide-safe: kept environment connection (--allow-env-connection)` (only when kept) → progress/other output. Assess: `ide-safe: active` → optional rejection. Nothing printed without `--ide-safe`; `--allow-env-connection` without `--ide-safe` is silently ignored (option only registered on `validate`).

Clamps (only when exceeding; in-bounds config is returned by reference-equal record so `Apply_CleanSnapshotConfig_IsUnchangedAndReportsNothing` still holds): `MaxDegreeOfParallelism > ProcessorCount → ProcessorCount` (0 = auto untouched), `MaxViolationQueueSize > 100000 → 100000`, `ValidationTimeoutSeconds > 900 → 900`; each adds `Name=value (clamped to bound)` to `Suppressed`.

Scrub: clears `DATAGUARD_CONNECTION_STRING` (unless allowEnv), `VAULT_TOKEN`, `VAULT_ADDR`, `AWS_*` (prefix, incl. `AWS_PROFILE`), `ConnectionStrings__*` (prefix, case-insensitive); process-local; best-effort (`SecurityException`/`ArgumentException` swallowed, returns cleared names). Called in validate right after `Apply` (before `GetRulesForProvider`/adapters run) and in assess right after the rejection check.

Write path: `IsSafeWritablePath` now only rejects a parent directory or target file that is itself a reparse point; ancestors are not walked. This is a global relaxation (also affects validate `summary.json` and `init`/`config` writes) — strictly more permissive, only for junctioned ancestors; matches validate's `FileSarifSink`, which has no link check at all. Junction test mirrors the VS layout (`%TEMP%\DataGuard\<guid>\validation.sarif`): junction → real dir, output in a *sub*-directory of the junction.

Baseline warning is unconditional (not ide-safe only), per requirement; `ValidateContractsAsync` is shared with `snapshot diff/refresh` and `baseline create` (progress null), so those print the stderr line too when a baseline filters something. Path is relative to the CLI working directory.

Regex: `REGEX_DEFAULT_MATCH_TIMEOUT` = 1 s applies to every regex in the CLI process (incl. `Compiled`, adapters, classification); a pathological-but-legit input now surfaces as `RegexMatchTimeoutException` → `Validation failed: ...` exit 1 instead of a hang. Golden corpus runs in-process without that setting (unchanged).

## 4. Tests After + regression gate

Tests After (written after implementation): `IdeSafePolicyTests.SelectVariablesToClear_ClassifiesNames` (12 rows, pure), `IdeSafeEnvironmentScrubTests.Scrub_ClearsSecretVariablesInProcess_AndKeepsAllowedConnection` (set + restore in `finally`; own `[Collection]` with `DisableParallelization = true` because `AutoDetectionEngineTests`/`ZeroTrustCredentialProviderTests` also set/clear `DATAGUARD_CONNECTION_STRING` in-process), `IdeSafePolicyTests.WriteReport_KeptConnection_WritesSuppressionLineThenKeptLine`.

GREEN phase run (same filter, both batches, after rebuild): `Failed: 0, Passed: 101, Skipped: 0` + literal-cap test fixed (fixture had to use `const` fields — plain locals are not extracted by `ProjectCSharpSqlSource`) → `PhantomIdentifierRuleRegex` class `Passed: 4`. Regex DoS test: 200 000-space input completes in ms.

Gate:
- `dotnet build DataGuard.sln -c Release -m:1` → 0 Warning(s), 0 Error(s). (`-m:1` because the parallel solution build hits a pre-existing VSSDK race in `DataGuard.VisualStudio.csproj` — `VSSDK1025/VSSDK1309` "obj\Release\net472\*.pkgdef/extension.vsixmanifest not found" — unrelated to this phase; the VS project builds clean on its own and CI builds `DataGuard.CrossPlatform.slnf` + the VSIX in a separate MSBuild step. `dotnet build DataGuard.CrossPlatform.slnf -c Release` also → 0/0.)
- `dotnet test tests/DataGuard.Core.Tests -c Release` → Passed: 871, Failed: 0, Skipped: 0 (51 s)
- `dotnet test tests/DataGuard.GoldenCorpus.Tests -c Release` → Passed: 28, Failed: 0 (regex rewrite and literal cap change no corpus result)

## 5. Skipped / deviations

- `Assess_OutputUnderJunction_WritesSarif` is `[WindowsFact]` (skips with reason on non-Windows); if `mklink /J` itself fails on Windows the test `Assert.Fail`s with mklink's stderr rather than passing. It passed here.
- Files over the 200-line guideline: `IdeSafeEndToEndTests.cs` (293), `IdeSafePolicyTests.cs` (~300, three test classes), `IdeSafePolicy.cs` (226; `Apply` + clamps + rejection/format helpers — could split `IdeSafeReport` out later). `IdeSafeEndToEndTests.cs` needs its own process helper (cannot share `CliExitCodeTests`' private helper without editing an unowned file, and that helper concatenates stdout+stderr and reads them sequentially, which can deadlock under `--progress`). Splitting would need a shared test-helper file outside the ownership list — left for the lead to decide.
- Literal-cap test lives in `PhantomIdentifierRuleRegexTests` (same regex-DoS theme) because `ProjectCSharpSqlSourceTests.cs` is not owned.
- `DG1291` note is a stdout `[WARN]` line (mirrors the existing `[INFO] Found SQL ...` convention); not a progress event.
- `ProjectInventoryReader.cs` untouched (see §2).

## 6. Concerns for the lead

- (a) `IsSafeWritablePath` relaxation is global: `init`/`config`/validate `summary.json` writes also stop rejecting junctioned ancestors, without `--ide-safe` involved. DRY reading of "same policy as validate"; strictly more permissive; flagged as a conscious scope decision.
- (b) `docs/USAGE.md` (`--ide-safe` section) now contradicts the CLI (env credential can be kept with `--allow-env-connection`; `ide-safe: active` handshake) — unowned file, left for Phase 4; tree is docs-inconsistent until then.
- (c) `BaselineApplied` progress kind needs the VS/VS Code host handlers (Phase 2/3) in the same PR.
- (d) File-size overages listed in §5.

## 7. Unresolved questions

1. Phase 2 must teach the VS extension's `TryFormatProgress` about `BaselineApplied` (currently unknown kinds print `[structured diagnostic redacted]`), and VS Code parity likewise — same PR per plan.
2. `docs/USAGE.md` §`--ide-safe` still says the connection string is always ignored; needs the `--allow-env-connection` and `ide-safe: active` contract (Phase 4 scope?).
3. Should the global relaxation of `IsSafeWritablePath` (ancestor walk dropped for `init`/`config`/`summary.json` too) stay, or be limited to SARIF outputs? I kept it global for parity/DRY.
4. `dotnet build DataGuard.sln` (parallel) fails intermittently on this machine in the VSIX project before and after this change — worth a `BuildInParallel=false`/dependency fix in the VS csproj, outside this phase.

## Review fixes (CLI/Core)

Source: `plans/reports/code-reviewer-260929-1412-vs-hardening-followup.md` findings H1, M1, M3, L4, L5, L6. Branch `feat/vs-extension-hardening` on top of c37a2ce, uncommitted. Ownership respected: `src/DataGuard.Cli`, `src/DataGuard.Core`, `tests/DataGuard.Core.Tests` only; no docs, no VS/VS Code, no `packages.lock.json` edits (the 9 lock files were already modified in the working tree before this pass).

### RED -> GREEN

RED run (after stubbing only the compile surface: `ideSafe` param, `HasLiveConnection` probe, `LiveShapeRuleDisabledNote` const): `Failed: 11, Passed: 83` (filter over the 9 affected classes).

| Finding | RED test(s) | Result |
|---|---|---|
| H1 | `ProviderRuleCatalogTests.Get_IdeSafeWithKeptConnection_RegistersConnectionlessLiveShapeRule`; `IdeSafePolicyTests.Apply_KeptEnvConnection_ReportsLiveShapeRuleDisabled`; `IdeSafeEndToEndTests.Validate_IdeSafeAllowEnvConnection_ReportsKeptEnvironmentConnection` (new fragment assertion) | 3 RED -> GREEN. Companion positives added: `Get_WithConnection_RegistersLiveShapeRuleBoundToConnection`, `Apply_WithoutKeptConnection_DoesNotReportLiveShapeRule`. Gap: the e2e exits 3 at contract acquisition (kept credential is unreachable), so the rule stage is never reached with a real credential; the `ideSafe` threading validate action -> `ValidateContractsAsync` -> `GetRulesForProvider` -> catalog is verified by reading, and the catalog test covers the last hop only. |
| M1 | `ConcurrentValidationExecutionTests.ValidateAsync_RegexTimeoutInRule_ThrowsNamingTheFailedRule` (was: message `Validation result exceeded the configured violation cap.`); `StreamAsync_RegexTimeoutInRule_PropagatesInsteadOfDroppingSilently` (was: no exception, violations silently dropped) | 2 RED -> GREEN. Plus `RegexMatchTimeoutStartupTests.Apply_RegistersOneSecondDefaultUnderTheRuntimeKey` (new helper, GREEN on first run) |
| M3 | `SafeWritablePathTests` (5 facts + 2 theory rows): in-workspace link -> rejected; junction outside workspace with plain subdir -> accepted; target directly under a link -> rejected; `ws2` prefix sibling -> not treated as inside; blank -> rejected | Not captured RED: the ancestor walk was written in the same extraction as the helper. Regression guard for the junctioned-%TEMP% case is the pre-existing `Assess_OutputUnderJunction_WritesSarif` (still GREEN). |
| L4 | `IdeSafePolicyTests.SelectVariablesToClear_ClassifiesNames` +7 rows (`DATAGUARD_DATABASECONNECTION`, `dataguard_db_password`, `PGPASSWORD`, `pgpassword`, `MYSQL_PWD` cleared; `DATAGUARD_CLI_PATH`, `DATAGUARDIAN` kept); `IdeSafeEnvironmentScrubTests` +2 names | 6 RED -> GREEN |
| L5 | `PhantomIdentifierRuleRegexTests.ValidateAsync_SelectFollowedBy200KSpaces_CompletesWithinFiveSeconds` (renamed from `...UnderOneSecond`) | bound 1 s -> 5 s, reason in comment |
| L6 | `Validate_IdeSafeAllowEnvConnection_ReportsKeptEnvironmentConnection`: `stderr.Should().NotContain("from-config")` | GREEN |

GREEN: filter run `Passed: 94/94`; full `tests/DataGuard.Core.Tests` -c Release `Passed: 892, Failed: 0, Skipped: 0` (was 871); `tests/DataGuard.GoldenCorpus.Tests` `Passed: 28`; `dotnet build DataGuard.sln -c Release -m:1` 0 warnings / 0 errors; `dotnet format whitespace DataGuard.sln --verify-no-changes` clean; no CRLF in touched files.

### Files changed

- `src/DataGuard.Cli/ProviderRuleCatalog.cs` — `Get(provider, connectionString, progress, bool ideSafe = false)`; under `ideSafe` the core rules receive `null` connection, so `LiveSqlShapeValidationRule` is the connection-less variant (DG018 still registered, never describes).
- `src/DataGuard.Cli/IdeSafePolicy.cs` — `LiveShapeRuleDisabledNote = "live SQL shape rule disabled (use verify-shape)"`, added to `Suppressed` whenever the env connection is kept (rendered in the `ide-safe: suppressed ...` line).
- `src/DataGuard.Cli/Program.cs` — startup calls `RegexMatchTimeoutStartup.Apply()` (still the first statement); `IsSafeWritablePath` is a one-line wrapper over `SafeWritablePath.IsSafe(path, Directory.GetCurrentDirectory())`; `ideSafe` threaded validate action -> `ValidateContractsAsync` -> `GetRulesForProvider` -> catalog (other callers default false).
- `src/DataGuard.Cli/SafeWritablePath.cs` (new) — target + parent never links (unchanged); ancestors walked only while strictly inside the workspace root (prefix compare with trailing separator, case-insensitive on Windows only); root itself and anything above it never inspected.
- `src/DataGuard.Cli/RegexMatchTimeoutStartup.cs` (new) — `AppContextKey`, `DefaultMatchTimeout` (1 s), `Apply()`, `Configured`.
- `src/DataGuard.Cli/IdeSafeEnvironment.cs` — fixed list + `PGPASSWORD`, `MYSQL_PWD`; every `DATAGUARD_*` cleared except allowlist `DATAGUARD_PROVIDER`, `DATAGUARD_CLI_PATH` (and `DATAGUARD_CONNECTION_STRING` when `--allow-env-connection`). Rationale: `ZeroTrustCredentialProvider` resolves `DATAGUARD_<CREDENTIAL-NAME>` for any caller-supplied name (only `DatabaseConnection` exists today).
- `src/DataGuard.Core/Rules/LiveSqlShapeValidationRule.cs` — `internal bool HasLiveConnection` (test probe; `InternalsVisibleTo` already present).
- `src/DataGuard.Core/Validation/ConcurrentValidationEngine.cs` — `StreamAsync` no longer swallows: rule exception -> `InvalidOperationException("Rule DG016 failed: RegexMatchTimeoutException: ...")` faulting the channel; `ValidateAsync` message now `Validation incomplete: rule execution failed for DG016 (RegexMatchTimeoutException).` when any rule failed (cap wording kept only for pure drops).
- Tests: `SafeWritablePathTests.cs`, `RegexMatchTimeoutStartupTests.cs` (new); `ProviderRuleCatalogTests.cs`, `IdeSafePolicyTests.cs`, `ConcurrentValidationExecutionTests.cs`, `IdeSafeEndToEndTests.cs`, `PhantomIdentifierRuleRegexTests.cs` (edited).

### M1 exit-code semantics (verbatim for docs)

`RegexMatchTimeoutException` (or any rule exception) during `validate`, both engines:

- Concurrent path (default, `EnableConcurrentValidation: true`): `ConcurrentValidationEngine.ValidateAsync` throws `ValidationIncompleteException` -> Program.cs validate `catch (Exception ex)` -> stderr `Validation failed: Validation incomplete: rule execution failed for DG016 (RegexMatchTimeoutException).` -> **exit 1**. No SARIF / summary.json is written (validation throws before the output stage), so IDE hosts fail closed ("produced no SARIF").
- Sequential path (`EnableConcurrentValidation: false`): the rule exception propagates unchanged -> stderr `Validation failed: The Regex engine has timed out while trying to match a pattern to an input string. ...` -> **exit 1**, no SARIF.
- Before this fix the concurrent path already exited 1 but with the misleading text `Validation failed: Validation result exceeded the configured violation cap.`; the silent-drop path (`StreamAsync`) had no callers in `src`. USAGE/CHANGELOG wording "Validation failed ..., exit 1" is therefore accurate; docs may add that the failing rule id and exception type are named and that no SARIF is produced.
- Regex bound effectiveness: `RegexMatchTimeoutStartupTests` proves the `REGEX_DEFAULT_MATCH_TIMEOUT` AppContext switch is set to 1 s by the helper the CLI calls first; it cannot prove `Regex` honoured it inside the test process (its default is frozen at first use by xunit). A true process-level proof would need a deterministic pathological input against a Compiled regex in the CLI; not added.

### Observed `--ide-safe --allow-env-connection` stderr (hostile fixture, unreachable DB)

```
ide-safe: active
ide-safe: suppressed GroundTruthMode=Manual (forced to Snapshot); ManualAssemblyPath (assembly loading disabled); live SQL shape rule disabled (use verify-shape)
ide-safe: kept environment connection (--allow-env-connection)
UNEVALUATED: contract acquisition failed: A network-related or instance-specific error ...
```
(exit 3 here because the kept credential is unreachable; `from-config` never appears.)

### Concerns

- Files over the 200-line guideline, unchanged in kind: `ConcurrentValidationEngine.cs` (~320), `IdeSafePolicyTests.cs` (~345), `IdeSafeEndToEndTests.cs` (~295), `IdeSafePolicy.cs` (~233). Not refactored in this pass.
- `SafeWritablePathTests` uses `mklink /J` on Windows (fails loudly, never skips) and `Directory.CreateSymbolicLink` elsewhere; Linux run not exercised here.
- `packages.lock.json` (9 files) were already dirty at session start; md5 of all lock files is identical to the prior agent's 14:20 baseline (`lock-md5-after-pkg.txt`), so this pass's builds did not change them.

### Follow-up: validate/oracle-check output through an in-workspace junction (post-review gate scenario g)

Source: `plans/reports/tester-260929-1449-post-review-gate.md` (g1/g2 FAIL). Base HEAD 56cfec8, uncommitted. Cause confirmed by reading: `validate --format sarif|evidence|contracts|yaml|typescript` and `oracle-check --output` hand the path straight to `FileSarifSink` / `ContractEvidenceWriter` / `ContractExportWriter.WriteAtomicallyAsync`, none of which consult `SafeWritablePath`; only `assess` (`WriteSarifAsync`) and `init`/`config` did. The `summary.json` refusal was swallowed by `catch (Exception)`.

RED (`IdeSafeEndToEndTests` filter): `Failed: 4, Passed: 9`
- `Validate_OutputThroughJunctionInsideWorkspace_IsRejected` `[WindowsTheory]` x3 — (`sarif`, junction is the parent = g1), (`sarif`, `junction/sub/` = g2), (`evidence`, g2). Observed RED: exit 0, file landed in `outside-target`.
- `OracleCheck_OutputThroughJunctionInsideWorkspace_IsRejectedBeforeConnecting` — observed RED: exit 1 `Oracle check requires --connection` (sink never reached, but also never checked).
- Control `Validate_OutputUnderSiblingJunctionOutsideWorkspace_IsWritten` (g3) GREEN before and after; `Assess_OutputUnderJunction_WritesSarif` unchanged (now uses the shared `CreateJunction` helper).

GREEN: e2e + `CliExitCodeTests` + `SafeWritablePathTests` filter `Passed: 42/42`; full Core `Passed: 897, Failed: 0` (+5); GoldenCorpus 28; `dotnet build DataGuard.sln -c Release -m:1` 0 warnings; `dotnet format whitespace --verify-no-changes` clean; lock files md5-identical to baseline.

Fix (option chosen: CLI-side guard, not lifting into the Core writer — `SafeWritablePath` and the workspace root live in `DataGuard.Cli`; lifting would mean moving the helper into Core and threading the root through `FileSarifSink`/`ContractEvidenceWriter` constructors for the same three call sites):
- `src/DataGuard.Cli/Program.cs`
  - new static local `RefuseUnsafeOutput(outputPath, artifact)`: `IsSafeWritablePath` -> else stderr `Refusing to write {artifact} through a symbolic link or invalid path.` + `Environment.ExitCode = 4` (assess's tool-error code) + `return true`.
  - `validate`: applied once at output resolution for every non-text format (`SARIF` for sarif, `<format> output` otherwise), before acquisition/validation runs — so a rejected path costs no work and no connection.
  - `oracle-check`: applied before `RunOracleValidationAsync` (hence before `--connection` is required); artifact `SARIF`.
  - `summary.json` catch now logs `summary.json not written: {ex.Message}` to stderr instead of swallowing; still non-fatal.
- `tests/DataGuard.Core.Tests/IdeSafeEndToEndTests.cs`: `CreateJunction` helper, `WindowsTheoryAttribute`, 3 new tests (file now 409 lines — over the 200-line guideline; splitting needs a shared process helper outside a single file, left for the lead as before).

Exact messages for docs: sarif -> `Refusing to write SARIF through a symbolic link or invalid path.`; evidence -> `Refusing to write evidence output through a symbolic link or invalid path.`; exit code **4** for `validate` and `oracle-check` (matches `assess`).

#### Addendum: `verify-shape` (naming correction + fail-fast guard)

The coordinator's ":1505 verify-shape sink" is the `oracle-check` command (guarded above). The real `verify-shape` command (`Program.cs:611-861`) takes `--output` and writes via `WriteTextAtomicallyAsync`, which already calls `IsSafeWritablePath` — so it was never unguarded, but it refused *late*: only after the full live describe pass, as `verify-shape failed: Refusing to write to unsafe path: <path>` with exit 1.

RED: `IdeSafeEndToEndTests.VerifyShape_OutputThroughJunctionInsideWorkspace_IsRejectedBeforeConnecting` `[WindowsFact]` — observed exit 1, late message, 2 s live pass against `127.0.0.1,1`.
GREEN: `RefuseUnsafeOutput(output, "verify-shape output")` placed after the `--project` precondition and before the `try` that builds the schema provider — stderr `Refusing to write verify-shape output through a symbolic link or invalid path.`, exit **4**, no connection attempted, nothing written.

Final gate after this addendum: `dotnet build DataGuard.sln -c Release -m:1` 0 warnings; Core `Passed: 898, Failed: 0` (+1); GoldenCorpus 28; `dotnet format whitespace --verify-no-changes` clean; lock files md5-identical to baseline; changed files still only `src/DataGuard.Cli/Program.cs` and `tests/DataGuard.Core.Tests/IdeSafeEndToEndTests.cs` (now 442 lines).

Sink inventory for the lead (not touched, outside scenario g): the only remaining direct `File.WriteAllTextAsync` on a user-chosen path in `Program.cs` is `init` (`:1382`), which is already preceded by `IsSafeWritablePath`; `snapshot`/`baseline` writers live in Core and were not audited here.
