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
