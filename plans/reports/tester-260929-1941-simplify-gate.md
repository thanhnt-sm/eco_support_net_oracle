# Tester report — simplify gate (full suite, combined working tree)

Branch `feat/vs-extension-hardening`, HEAD `8a2df13` at start and end (no concurrent commit). Tree under test = HEAD + 45 uncommitted non-`plans/` paths (simplify refactors CLI/Core, VS, VS Code, scripts; CodeQL test-fixture rewrite; VSIX csproj BuildAnalyzers restore/build split). Tree proof: md5 over the content of every path in `{git diff HEAD --name-only; git ls-files -o --exclude-standard}` minus `plans/` = `adef6ec2…` at start and at end (identical); the only file-list delta is `tests/.quarantine.json`, which I created (see §4).
Read-only for source: no edits under `src/`, no git write commands. One new file: `tests/.quarantine.json` (flaky-policy requirement — parent decides whether to keep it in the commit). Scratch under session scratchpad `.../scratchpad/{gate,e2e,head}`.
Baseline: `plans/reports/tester-260929-1449-post-review-gate.md` (tree `56cfec8`).

Full-suite mode (task-specified); diff-aware mapping not applied.

## 1. Suites (Release)

Build: `dotnet build DataGuard.sln -c Release -m:1 -tl:off` → rc 0, 55 s. Verbatim tail:
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:55.16
```
(only `warning`-matching line in the log is the `0 Warning(s)` summary itself.)

Test: `dotnet test DataGuard.sln -c Release --no-build -tl:off --logger trx` → **rc 1** on first run (one VS test; see §4), all counts otherwise at expectation.

| Project | Passed | Failed | Skipped | Total | Duration | Expected | Δ | Result |
|---|---|---|---|---|---|---|---|---|
| DataGuard.Core.Tests (net9.0) | 898 | 0 | 0 | 898 | 50 s | 898 | 0 (+6 vs 1449) | PASS |
| DataGuard.VisualStudio.Tests (net472) | 154 | 0 (+1 quarantined) | 1 | 156 | 8 s | 155 (+1 skip) | −1 (the quarantined test is excluded, not counted green) | PASS excl. quarantined — see §4 |
| DataGuard.Analyzers.Tests | 13 | 0 | 0 | 13 | 4 s | 13 | 0 | PASS |
| DataGuard.CodeFixes.Tests | 24 | 0 | 0 | 24 | 10 s | 24 | 0 | PASS |
| DataGuard.GoldenCorpus.Tests | 28 | 0 | 0 | 28 | 793 ms | 28 | 0 | PASS |
| DataGuard.Observability.Tests | 38 | 0 | 0 | 38 | 286 ms | 38 | 0 | PASS |
| VS Code `npm test` | 95 | 0 | 0 | 95 | 1.5 s | 95 | 0 | PASS; `npm run compile` (`tsc -p ./`) rc 0 |

Verbatim last lines:
```
Passed!  - Failed:     0, Passed:    38, Skipped:     0, Total:    38, Duration: 286 ms - DataGuard.Observability.Tests.dll (net9.0)
Passed!  - Failed:     0, Passed:    28, Skipped:     0, Total:    28, Duration: 793 ms - DataGuard.GoldenCorpus.Tests.dll (net9.0)
Passed!  - Failed:     0, Passed:    13, Skipped:     0, Total:    13, Duration: 4 s - DataGuard.Analyzers.Tests.dll (net9.0)
Passed!  - Failed:     0, Passed:    24, Skipped:     0, Total:    24, Duration: 10 s - DataGuard.CodeFixes.Tests.dll (net9.0)
Failed!  - Failed:     1, Passed:   154, Skipped:     1, Total:   156, Duration: 8 s - DataGuard.VisualStudio.Tests.dll (net472)
Passed!  - Failed:     0, Passed:   898, Skipped:     0, Total:   898, Duration: 50 s - DataGuard.Core.Tests.dll (net9.0)
TEST_RC=1
ℹ tests 95 / ℹ pass 95 / ℹ fail 0 / ℹ skipped 0 / ℹ duration_ms 1522.982   NPM_TEST_RC=0   NPM_COMPILE_RC=0
```
Closes 1449 Unresolved Q3: `VerifyShape_OutputThroughJunctionInsideWorkspace_IsRejectedBeforeConnecting` and `OracleCheck_…` ran and passed inside the Core 898 (trx outcome Passed), so `verify-shape --output` under a junction is now tested, not assumed.
Count-delta detail: Core 898 = 892 (1449) + 6 e2e tests added by `6d3eb67` (junction write-path cases) — the CodeQL fixture rewrite split `IdeSafeEndToEndTests.cs` into `IdeSafeWritePathEndToEndTests.cs`, `IdeSafeHandshakeEndToEndTests.cs`, `CliBaselineSuppressionEndToEndTests.cs` (+ `IdeSafeEndToEndSupport.cs`, `WorkspaceWithJunction.cs`, `DirectoryLinkTestHelper.cs`, `CliProcessTestRunner.cs`); all 13 HEAD test methods and all attributes (7 `[Fact]`, 4 `[WindowsFact]`, 1 `[WindowsTheory]` with the same 3 `InlineData` rows) are present in the new files — nothing dropped. Core Skipped = 0, so the `[WindowsFact]` junction tests ran, not skipped. VS skip = `NavigationTests.Navigate_WhenFileMissing_WritesOutputMessage` (VS experimental instance; pre-existing).

## 2. Gates

| Gate | Command | Result |
|---|---|---|
| Policy unit tests (replaces `--self-test`) | `python -m unittest discover -s scripts/tests -v` | rc 0 — `Ran 14 tests in 0.029s / OK` |
| Policy | `python scripts/check-workflow-policy.py` | rc 0 — `check-workflow-policy: OK (VSIX assert, fork-PR upload guard, release VS tests)` |
| actionlint 1.7.12 | `actionlint .github/workflows/ci.yml .github/workflows/release.yml` | rc 0, no findings (ci.yml now runs the unittest discover + `& ./scripts/assert-vsix.ps1`) |
| Docs sync | `bash ./scripts/verify_docs_sync.sh` | rc 0 — "All bilingual documentation & rule artifacts are synchronized and present" |
| Whitespace | `dotnet format whitespace DataGuard.sln --verify-no-changes` | rc 0 (usual workspace-warning notice only) |
| Lock files | md5 of all 24 `packages.lock.json` before the sln build vs after build + test + format + e2e + cold-obj packaging | **identical**; porcelain = the same 9 ` M` entries (Analyzers, Cli, Contracts, Core, MySql/Oracle/PostgreSql/SqlServer adapters, SqlClassification); `git diff --ignore-cr-at-eol --quiet -- '*/packages.lock.json'` clean → CRLF-only (`i/lf w/crlf`); publish lock redirect present at `src/DataGuard.Cli/obj/cli-publish*.packages.lock.json` |

## 3. Packaging (CI-equivalent, cold obj)

Red: not reproducible without editing the csproj (forbidden); the red case is the CI failure documented in `plans/reports/debugger-260929-1905-vsix-packaging-ci-failure.md` (single `Targets="Restore;Build"` MSBuild task → CS0518/CS0234/CS0653 on a clean checkout).
Green (this run): deleted `src/DataGuard.Analyzers/obj`, `src/DataGuard.CodeFixes/obj`, `src/DataGuard.Contracts/obj`, `src/DataGuard.SqlClassification/obj` (verified absent), then from PowerShell (`pwsh -NoProfile -File`, `Set-Location` repo root) the exact ci.yml line:
```
& $env:MSBUILD src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj /restore /t:Build /p:Configuration=Release /p:CreateVsixContainer=true /p:DeployExtension=false /m /nologo /v:minimal
MSBUILD_RC=0 ELAPSED=55s      (MSBuild = C:\Program Files\Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\MSBuild.exe, /m parallel as in CI — no pkgdef race this run)
VSIX=src\DataGuard.VisualStudio\bin\Release\net472\DataGuard.VisualStudio.vsix SIZE=50485411 MTIME=19:51:32
EXPECTED_VERSION=0.2.3   (ExtensionVersion.cs Fallback, same Select-String as ci.yml)
assert-vsix: OK - DataGuard.VisualStudio.vsix (48.1 MB), version 0.2.3, 22 entries, all 6 required entries present
ASSERT_RC=0
```
Then `dotnet test tests/DataGuard.VisualStudio.Tests -c Release --no-build --filter "FullyQualifiedName~VsixAnalyzerPackaging"` → rc 0:
```
Passed VsixAnalyzerPackagingTests.VsixPackage_WhenBuilt_ContainsCodeFixesDll [230 ms]
Passed VsixAnalyzerPackagingTests.VsixPackage_WhenBuilt_ContainsAnalyzersDll [7 ms]
Passed VsixAnalyzerPackagingTests.VsixManifest_WhenBuilt_DeclaresAnalyzerAsset [181 ms]
```
Caveat, so "3 pass" is not misread: all three still have `if (vsixPath == null) return;` (vacuous pass when no VSIX). They ran for real here because the VSIX existed at `bin/Release/net472/` (mtime 19:51:32, listed before and after the test; `--no-build` kept it). The recursive search + `TestPaths.RepoRoot` (walk up to `DataGuard.sln`) is what made them find it — before the change `TopDirectoryOnly` on `bin/Release` never saw the `net472/` subfolder, so the 155 count in 1449 included three vacuous passes. Recommend replacing the `return` with a `Skip`/`WindowsFact`-style explicit skip so a missing VSIX is visible, not green.

## 4. Flaky / Quarantined

**1 flaky this run** — `DataGuard.VisualStudio.Tests.CliRunSessionLiveTests.Timeout_TerminatedAfterSarifWasWritten_StillPublishes` (`CliRunSessionLiveTests.cs:84`).
First (full) run:
```
Expected boolean to be True because taskkill leaves exit code 0, which must not be read as a CLI verdict, but found False.
   at DataGuard.VisualStudio.Tests.CliRunSessionLiveTests.<Timeout_TerminatedAfterSarifWasWritten_StillPublishes>d__6.MoveNext() in ...\CliRunSessionLiveTests.cs:line 84
```
Deterministic rerun rule: 3 isolated reruns (`--no-build --filter FullyQualifiedName~…`), same tree/env → **3/3 pass** (2 s each) → flaky by the fixed rule, not a real failure. Quarantined in `tests/.quarantine.json` (new file; `expires` 2026-10-29, `issue_link` null). Excluded from the pass count above with this note; not reported as passing.
Cause hypothesis (from code, not verified): the first assertion (`ProceedToPublish` true) passed while `TerminatedAtTimeout` was false; per `CliRunTimeoutHandler.ShouldPublishAfterTimeout` that combination only arises from `ProcessStopOutcome.AlreadyExited`, i.e. `ProcessTerminator.StopProcess` saw the cmd.exe fake (`ping -n 8`, ~7 s) already gone when the 1 s timeout fired. The full run had six test projects in parallel plus my concurrent e2e CLI processes (my sequencing choice — 1449 ran e2e after the suite), so a starved timeout continuation / slow `taskkill` spawn is the likely race. Not caused by the refactor: the working-tree diff in `CliRunSession.cs`/`ProcessTerminator.cs` is a pure move of `HasExited` with an identical body; the test-file diff only swaps the temp-dir helper. Recommendation: lengthen the fake to `ping -n 20` or make the test tolerate `AlreadyExited` only when it can prove the process outlived the timeout; and don't run other CPU-heavy work beside the VS live tests.

## 5. E2E — refactor equivalence (HEAD CLI vs working-tree CLI) and vs 1449

Working-tree CLI: `src/DataGuard.Cli/bin/Release/net9.0/DataGuard.Cli.dll` (built 19:45 by the sln build, before packaging). HEAD CLI: `git archive HEAD` exported to scratchpad and `dotnet build src/DataGuard.Cli -c Release` there (rc 0; its 19 warnings are all SourceLink "source control information not available" from the archive export, not gate-relevant). Same runner script, same fixtures, cwd = fixture dir ((a),(b)) or `g/ws` ((g)), `DATAGUARD_CONNECTION_STRING` unset via `env -u` except (b).

| Scenario | Exit head/wt | stderr | stdout | Landed files |
|---|---|---|---|---|
| (a) hostile config, `validate --ide-safe --format sarif` | 0 / 0 | IDENTICAL (285 B, 2 lines) | IDENTICAL (0 B) | `a/out/validation.sarif` 375 B + `summary.json` 138 B (both) |
| (b) env conn + `--allow-env-connection` (config-sentinel fixture from 1449) | 3 / 3 | IDENTICAL (661 B, 4 lines) | IDENTICAL | none (pre-results UNEVALUATED), both |
| (g1) `--output <ws>/junction/x.sarif` | 4 / 4 | IDENTICAL (351 B, 3 lines) | IDENTICAL | none in `outside-target` (both) |
| (g2) `--output <ws>/junction/sub/x.sarif` | 4 / 4 | IDENTICAL (351 B) | IDENTICAL | none (both) |
| (g3) control `<sibling-junction>/out/x.sarif` (junction outside ws) | 0 / 0 | IDENTICAL (285 B) | IDENTICAL | `sibling-target/out/x.sarif` 375 B + `summary.json` (both) |
| (ge) `--format evidence --output <ws>/junction/e.json` | 4 / 4 | IDENTICAL (361 B) | IDENTICAL | none (both) |

`cmp` byte-level on every stderr/stdout pair and on the landed-file list: **no difference anywhere** — the refactor's "identical output" promise holds on the observable CLI contract for these six paths. (b) line 2 — the line the refactor could have reordered (`RulesConnectionString`/`ProviderRuleCatalog.Get` signature change) — is exactly `ide-safe: suppressed ManualAssemblyPath (assembly loading disabled); live SQL shape rule disabled (use verify-shape); AuditLogPath (repo-chosen write path); telemetry output (EnableTelemetry/TelemetryFileDirectory)` in both. `config-sentinel`/`CONFIGSECRET`/`127.0.0.1` in (b) stderr+stdout: 0 hits.

Vs the 1449 report's verbatim blocks: (a) and (b) IDENTICAL after `--strip-trailing-cr` (CLI stderr is CRLF on Windows, the markdown excerpt is LF — first-line bytes checked with `od`; no textual difference). (g1)/(g2): differ from 1449 by exactly one added line, `Refusing to write SARIF through a symbolic link or invalid path.`, and exit 4 instead of 0 with nothing landed — 1449 documented the *failing* pre-fix behavior, and `6d3eb67` (after 1449) is the fix, so 1449 is not a valid baseline for (g); the current output matches `IdeSafeWritePathEndToEndTests` `InlineData` and the HEAD CLI byte-for-byte. (g) is therefore PASS (was FAIL in 1449).

## 6. Performance / build notes
- Build 55 s (1449: 57 s); Core.Tests 50 s (65 s); VS 8 s; CodeFixes 10 s; npm test 1.5 s / ~12 s incl. tsc; format ~16 s; cold-obj packaging 55 s with `/m` (1412 warm: 27 s); HEAD CLI scratch build 12 s.
- No VSSDK pkgdef race with `/m` this run (one sample; the `-m:1` advice in memory stands for local sln builds).
- (b) connect failure 2.5 s wall incl. host start (`Connect Timeout=1` honoured).

## 7. Coverage observations
- `VsixAnalyzerPackagingTests` silent-return when no VSIX (see §3) — make it an explicit skip.
- The flaky live test's 1 s timeout vs 7 s fake is a thin margin under load (§4).
- No test pins that `RefuseUnsafeOutput` runs *before* `IdeSafePolicy.WriteReport` or after; ordering observed (handshake lines first, refusal third) is what the hosts' first-line parsers need — worth an assertion in `IdeSafeWritePathEndToEndTests` if it isn't already implied.

## Unresolved questions
1. `tests/.quarantine.json` is new and untracked: keep it in the upcoming commit (flaky-policy tracking) or drop it? I wrote it because the policy is mandatory; the parent's "read-only for source" instruction may intend to exclude it.
2. The flaky test's cause is a hypothesis from the classification code, not reproduced; a `debugger` RCA is only warranted if it fails again in CI (CI runs the VS tests without concurrent e2e load).
3. Packaging "red" was cited from the debugger report, not reproduced (would require editing the csproj).

**Status:** DONE_WITH_CONCERNS
**Summary:** All suites at expected counts (Core 898, VS 154 + 1 quarantined + 1 skip, Analyzers 13, CodeFixes 24, GoldenCorpus 28, Observability 38, VS Code 95 + compile), build 0 warnings, all gates rc 0 (unittest 14, policy, actionlint, docs sync, format), cold-obj CI packaging green (MSBuild rc 0, assert-vsix 0.2.3/22 entries, 3 VSIX tests genuinely run and pass), lock files md5-identical (9 CRLF-only entries), e2e (a)/(b)/(g)/(evidence) byte-identical between HEAD-built and working-tree CLI and consistent with 1449 modulo the `6d3eb67` junction fix.
**Concerns/Blockers:** Non-blocking: `CliRunSessionLiveTests.Timeout_TerminatedAfterSarifWasWritten_StillPublishes` failed 1/1 in the full run then passed 3/3 isolated (flaky under load, quarantined in new `tests/.quarantine.json` — parent decides whether to commit that file); `VsixAnalyzerPackagingTests` still pass vacuously when no VSIX exists. Nothing correctness-blocking in the refactor.
