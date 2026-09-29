# Tester report — Phase 5 verification (VS hardening red-team follow-up)

Plan: `plans/260929-0952-vs-hardening-redteam-tdd-followup/phase-05-verification-and-pr-update.md`
Branch `feat/vs-extension-hardening`; HEAD at start and end of this run: `c37a2ce` (`build(ci): shared VSIX assert, fork-PR upload guard, CLI release assets; docs for ide-safe contract`). Concurrent committer did not move HEAD during the run. `git status --porcelain` shows no uncommitted paths under `src/`/`tests/` other than the 9 CRLF-only lock files → the tree tested is HEAD `c37a2ce`.
Read-only run: no edits under `src/`/`tests/`, no git write commands. All scratch under the session scratchpad (`.../scratchpad/e2e/{a,b,d,f}`, `.../scratchpad/c`).

Diff-aware mode: not applicable — phase asks for full suites + packaging + e2e; ran everything.

## 1. Suites (Release)

Build: `dotnet build DataGuard.sln -c Release -m:1 -tl:off` → rc 0, **0 Warning(s), 0 Error(s)**, 42 s. (Solution build already emits the VSIX — `CreateVsixContainer` defaults true.)
Test: `dotnet test DataGuard.sln -c Release --no-build -tl:off` → rc 0, 65 s wall.

| Project | Passed | Failed | Skipped | Total | Duration | Expected min | Result |
|---|---|---|---|---|---|---|---|
| DataGuard.Core.Tests (net9.0) | 871 | 0 | 0 | 871 | 55 s | ≥ 871 | PASS |
| DataGuard.VisualStudio.Tests (net472) | 152 | 0 | 1 | 153 | 6 s | ≥ 152 | PASS (skip = NavigationTests, needs VS exp. instance — pre-existing) |
| DataGuard.Analyzers.Tests | 13 | 0 | 0 | 13 | 3 s | 13 | PASS |
| DataGuard.CodeFixes.Tests | 24 | 0 | 0 | 24 | 8 s | 24 | PASS |
| DataGuard.GoldenCorpus.Tests | 28 | 0 | 0 | 28 | 811 ms | 28 | PASS |
| DataGuard.Observability.Tests | 38 | 0 | 0 | 38 | 1 s | (report) | PASS |
| VS Code `npm test` (`src/DataGuard.VSCode`) | 93 | 0 | 0 | 93 | 1.27 s (14 s incl. tsc) | 93 | PASS |

Verbatim summary lines:
```
Passed!  - Failed:     0, Passed:    28, Skipped:     0, Total:    28, Duration: 811 ms - DataGuard.GoldenCorpus.Tests.dll (net9.0)
Passed!  - Failed:     0, Passed:    38, Skipped:     0, Total:    38, Duration: 1 s - DataGuard.Observability.Tests.dll (net9.0)
Passed!  - Failed:     0, Passed:    13, Skipped:     0, Total:    13, Duration: 3 s - DataGuard.Analyzers.Tests.dll (net9.0)
Passed!  - Failed:     0, Passed:   152, Skipped:     1, Total:   153, Duration: 6 s - DataGuard.VisualStudio.Tests.dll (net472)
Passed!  - Failed:     0, Passed:    24, Skipped:     0, Total:    24, Duration: 8 s - DataGuard.CodeFixes.Tests.dll (net9.0)
Passed!  - Failed:     0, Passed:   871, Skipped:     0, Total:   871, Duration: 55 s - DataGuard.Core.Tests.dll (net9.0)
ℹ tests 93 / ℹ pass 93 / ℹ fail 0 / ℹ skipped 0 / ℹ duration_ms 1269.0674
```

## 2. Packaging / assert / policy / lock files

MSBuild (Git Bash, `MSYS_NO_PATHCONV=1`, dash switches; added `-p:DeployExtension=false` to match ci.yml):
```
MSBuild.exe src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj -restore -t:Build -p:Configuration=Release -p:CreateVsixContainer=true -p:DeployExtension=false -m:1 -nologo -v:minimal
→ rc 0, 27 s
    DataGuard.Cli -> ...\src\DataGuard.Cli\bin\Release\net9.0\win-x64\dataguard.dll
    DataGuard.Cli -> ...\src\DataGuard.VisualStudio\obj\cli\
  DataGuard.VisualStudio -> ...\src\DataGuard.VisualStudio\bin\Release\net472\DataGuard.VisualStudio.vsix
```
VSIX: `src/DataGuard.VisualStudio/bin/Release/net472/DataGuard.VisualStudio.vsix`, 50 481 287 bytes.

assert-vsix (expected version = `ExtensionVersion.cs:19` `Fallback = "0.2.3"`, same derivation as ci.yml line 174):
```
pwsh -NoProfile -File scripts/assert-vsix.ps1 -VsixPath src/DataGuard.VisualStudio/bin/Release/net472/DataGuard.VisualStudio.vsix -ExpectedVersion 0.2.3
→ rc 0
assert-vsix: OK - DataGuard.VisualStudio.vsix (48.1 MB), version 0.2.3, 22 entries, all 6 required entries present
```
Policy:
```
python scripts/check-workflow-policy.py → rc 0
check-workflow-policy: OK (VSIX assert, fork-PR upload guard, release VS tests)
```
Lock files — md5 of all 24 `packages.lock.json` snapshotted before the sln build, after the sln build, after the MSBuild packaging: **identical all three times**. `git status --porcelain | grep packages.lock.json` = the same 9 pre-existing ` M` entries (Analyzers, Cli, Contracts, Core, MySql/Oracle/PostgreSql/SqlServer adapters, SqlClassification), nothing new. Those 9 diffs are CR-at-EOL only (`git ls-files --eol` → `i/lf w/crlf`; `git diff --ignore-cr-at-eol --quiet` → clean) — no content change, so the `NuGetLockFilePath=obj/...` redirect for the bundled-CLI publish holds.

## 3. E2E scenarios (built CLI `dotnet src/DataGuard.Cli/bin/Release/net9.0/DataGuard.Cli.dll`)

Common hostile fixture (`Repo.cs` with one inline `SELECT`, `.dataguard.yml`):
```
GroundTruthMode: Full
ConnectionString: Server=127.0.0.1,1;Connect Timeout=1
ManualAssemblyPath: tools/evil.dll
AuditLogPath: C:/hostile/audit.log
EnableTelemetry: true
```
`DATAGUARD_CONNECTION_STRING` unset in the shell; (a)/(d)/(f) run with `env -u`, (b) with it set explicitly. Provider defaults to `sqlserver` (Program.cs:516) so `Server=127.0.0.1,1` is a real SQL Server connect attempt.

### (a) hostile config + `validate --ide-safe --format sarif` — PASS
```
validate --ide-safe --project <a> --config <a>/.dataguard.yml --format sarif --output <a>/out/validation.sarif
EXIT: 0   ELAPSED: 615 ms
stderr (2 lines, verbatim):
ide-safe: active
ide-safe: suppressed GroundTruthMode=Full (forced to Snapshot); ManualAssemblyPath (assembly loading disabled); ConnectionString from config (database access disabled); AuditLogPath (repo-chosen write path); telemetry output (EnableTelemetry/TelemetryFileDirectory)
```
SARIF written: 375 bytes, `runs[0].results = []`. Exit 0 (not 2/4), < 5 s, no connection attempt (no SQL error text).

### (b) env connection + `--ide-safe --allow-env-connection` — PASS (carve-out proven)
```
ENV DATAGUARD_CONNECTION_STRING=Server=127.0.0.1,1;Connect Timeout=1
validate --ide-safe --allow-env-connection --project <b> --config <b>/.dataguard.yml --format sarif --output <b>/out/validation.sarif
EXIT: 3   ELAPSED: 1643 ms
stderr (4 lines, verbatim):
ide-safe: active
ide-safe: suppressed ManualAssemblyPath (assembly loading disabled); AuditLogPath (repo-chosen write path); telemetry output (EnableTelemetry/TelemetryFileDirectory)
ide-safe: kept environment connection (--allow-env-connection)
UNEVALUATED: contract acquisition failed: A network-related or instance-specific error occurred while establishing a connection to SQL Server. The server was not found or was not accessible. Verify that the instance name is correct and that SQL Server is configured to allow remote connections. (provider: TCP Provider, error: 0 - The wait operation timed out.)
```
Connection attempted and failed fast (1.6 s), exit 3 (validation incomplete). Note `GroundTruthMode=Full` is *not* in the suppression list here (survives because a connection is kept) and the config-file `ConnectionString` is silently replaced by the env value — matches `IdeSafePolicy.Apply`. No SARIF written on exit 3 (contract acquisition failed before results) — expected for UNEVALUATED.

### (c) simulated old CLI — stream contract PASS; host decision paths covered by unit tests
Stub `scratchpad/c/dataguard.cmd` (`@echo off` / `>&2 echo Unrecognized command or argument '--ide-safe'.` / `exit /b 1`; `>&2 echo` form avoids the trailing space `echo … 1>&2` appends):
```
cmd /c "C:\...\scratchpad\c\dataguard.cmd" validate --ide-safe --progress
EXIT: 1   stdout: 0 bytes
stderr bytes: Unrecognized command or argument '--ide-safe'.\r\n
starts-with VS CliArgumentBuilder.IdeSafeRejectionPrefix: yes
VS Code OLD_CLI_REJECTION /^Unrecognized command or argument '--(?:ide-safe|allow-env-connection)'/ .test(line.trim()): true
```
A real IDE-host launch is **out of scope** (VS `CliLocator.cs:177` only accepts `.exe`; VS Code `extension.ts:447-453` uses `spawn(cliPath, …, { shell: false })`, so a `.cmd` would not launch there either). Decision paths verified with the pure collaborators:

VS (`dotnet test tests/DataGuard.VisualStudio.Tests -c Release --no-build --filter "…CliRunSessionLive|…ProgressStreamReader|…CliArgumentBuilder"` → Passed 41, Failed 0, 11 s):
- `CliRunSessionLiveTests.OldCli_RejectionLineAndExitOne_IsCliTooOld` [126 ms] — live cmd.exe fake, rejection line + exit 1 → `CliTooOld`
- `CliRunSessionLiveTests.HandshakeMissing_ResultsAreDiscarded` [129 ms] — no ack → discarded
- `ProgressStreamReaderTests.Verdict_OldCliStream_IsCliTooOld`
- `ProgressStreamReaderTests.Verdict_WithoutAcknowledgement_IsHandshakeMissing_NotPublish`
- `ProgressStreamReaderTests.Verdict_RejectionLineButProgressEvents_IsHandshakeMissing` (rejection line + progress events ≠ old CLI)
- `ProgressStreamReaderTests.Characterization_RejectionLineAtStart_FlagsIdeSafeUnsupported`
- `ProgressStreamReaderTests.IdeSafeActiveNotExactFirstLine_IsNotAcknowledged` × 3 (leading space / `(v2)` suffix / not first)
- `CliArgumentBuilderTests.IsIdeSafeUnsupportedMessage_DetectsOnlyRejectionOfTheFlag` × 7 rows
Host strings (source): `DataGuardPackage.Publishing.cs:43` status `"DataGuard: CLI too old"` / `"DataGuard: Results discarded"`; `:135` `"[DataGuard] The configured CLI does not support --ide-safe (it is too old) and was not run in IDE-safe mode; its results were discarded. "` + ReinstallGuidance; `:138` `"[DataGuard] CLI did not confirm ide-safe mode; results discarded."`.

VS Code (`node --test out/ide-safe-contract.test.js` → 8/8 pass, 204 ms):
- `hasIdeSafeAck accepts only an exact ack as the first non-empty stderr line`
- `isOldCliRejection is anchored at line start and recognises both new flags`
- `MIN_CLI_VERSION names the first CLI that acknowledges ide-safe`
- `buildIdeSafeFailureMessage adds the update hint only for an old-CLI rejection`
Host strings (`ide-safe-contract.ts`): `:22` `"DataGuard CLI did not confirm IDE-safe mode; results were discarded"`; `:38` `` `${ACK_FAILURE_MESSAGE}. Update the dataguard CLI (${MIN_CLI_VERSION} or later) or set dataguard.cliPath` ``; `MIN_CLI_VERSION = "0.3.0"`.

### (d) spoofed `.csproj` name + `assess --ide-safe` — PASS
Workspace: `Unrecognized command or argument '--ide-safe'.csproj` (minimal SDK project, net9.0) + `Repo.cs`.
```
assess --ide-safe --workspace <d> --format sarif --output <d>/out/assess.sarif
EXIT: 1   ELAPSED: 301 ms
stderr (1 line, verbatim):
ide-safe: active
stdout:
Assessment SARIF written to <d>/out/assess.sarif
```
SARIF 1082 bytes, tool `DataGuard.Assessment`, 1 result: `DG1301 warning | Projects target net9.0 but no global.json pins the workspace SDK. | ['global.json']`. Exit 1 = findings present (`Program.cs`: `ExitCode = Errors.Count > 0 ? 4 : Findings.Count > 0 ? 1 : 0`), not 2/4. The spoofed filename **never reached stderr or stdout** (grep for `Unrecognized` across stderr/stdout/SARIF: no hits), so the CLI side gives the host nothing to misread. Host-side proof that a spoof that *did* reach stderr does not flip the verdict:
- `ProgressStreamReaderTests.SpoofedFileNameContainingRejectionText_DoesNotFlagOldCli` — PASS
- `CliArgumentBuilderTests.IsIdeSafeUnsupportedMessage_DetectsOnlyRejectionOfTheFlag(line: "[DG1290] C:\x\Unrecognized command or argument '…", expected: False)` — PASS

### (e) timeout boundary — PASS (unit, live taskkill)
- `CliRunSessionLiveTests.Timeout_WhenProcessExitsDuringKill_AlreadyExitedPathPublishes` [2 s] — AlreadyExited → publishes
- `CliRunSessionLiveTests.Timeout_TerminatedAfterSarifWasWritten_StillPublishes` [2 s] — Terminated + SARIF on disk + normal exit code → publishes
- `CliRunSessionLiveTests.Timeout_WithoutSarif_TerminatesTreeAndDiscards` [1 s] — Terminated, no SARIF → discard, tree dead
- `CliRunSessionLiveTests.ProcessStarter_RunsOffTheCallingSynchronizationContext` [138 ms]
All passed first run; no rerun needed.

### (f) sanity — hostile config `validate` WITHOUT `--ide-safe` — PASS (no behaviour change)
```
validate --project <f> --config <f>/.dataguard.yml --format sarif --output <f>/out/validation.sarif
EXIT: 3   ELAPSED: 1542 ms
stderr (1 line, verbatim):
UNEVALUATED: contract acquisition failed: A network-related or instance-specific error occurred while establishing a connection to SQL Server. … (provider: TCP Provider, error: 0 - The wait operation timed out.)
```
`ide-safe:` lines: 0. Full mode honoured (connection attempted to 127.0.0.1:1), exit 3.

## 4. Flaky / Quarantined
None this run. Every suite and every filtered rerun passed on the first attempt; `tests/.quarantine.json` not touched.

## 5. Performance / build notes
- Core.Tests now 55 s in Release (memory said ~3m40s — earlier figure was Debug or an older tree); whole sln test 65 s wall.
- `CliRunSessionLiveTests` timeout tests are the slowest VS tests (1–2 s each, live taskkill) — acceptable.
- Solution build with `-m:1`: 42 s, no VSSDK pkgdef race observed.
- Build 0 warnings.

## 6. Coverage observations (gaps, not failures)
- No process-level test asserts that (b)'s `UNEVALUATED` connection failure happens *fast* (Connect Timeout honoured) — only that it happens. Fine for now; a hostile `Connect Timeout=600` with `--allow-env-connection` would hang the host up to the VS run timeout (host owns the env value, so out of red-team scope).
- (d) shows the CLI never echoes project file names to stderr under `assess`; the spoof defence is currently only exercised by unit tests. If a future assess rule prints file paths to stderr, `SpoofedFileNameContainingRejectionText_DoesNotFlagOldCli` is the guard — keep it.
- `IdeSafeEndToEndTests` has no `--format sarif` + `--ide-safe` variant for `validate` (the junction test covers `assess` only); scenario (a) here is the manual equivalent.

## Unresolved questions
1. Report path: task text says `tester-260929-1412-followup-e2e.md`, naming hook says `tester-260929-1414-{slug}.md`; used the task's path since the parent reads it.
2. (b) exits 3 with no SARIF when the kept connection fails. Phase text only required "connection attempted and fails fast"; confirm the hosts treat exit 3 + no SARIF as "previous results kept" (Phase 2 report says so for VS) — not re-verified here.
3. Assess tool version in SARIF reads `DataGuard.Assessment 0.0.0.0` and validate SARIF driver version `0.1.0-alpha.1` on a local (untagged) build — cosmetic, MinVer/tag-driven; will differ on the v0.3.0 tag.

**Status:** DONE
**Summary:** All suites green (Core 871, VS 152+1 skip, Analyzers 13, CodeFixes 24, GoldenCorpus 28, Observability 38, VS Code 93); Release build 0 warnings; VSIX packaged and `assert-vsix` OK at 0.2.3; policy OK; lock files byte-identical across builds (9 pre-existing CRLF-only entries, nothing new); e2e scenarios (a)–(f) all PASS with verbatim output recorded.
**Concerns/Blockers:** none blocking. Real IDE-host launch for (c) is out of scope by design (VS requires `.exe`, VS Code spawns without shell); verified via the pure collaborators' unit tests.
