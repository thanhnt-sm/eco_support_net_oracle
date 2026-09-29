# Tester report — post-review regression gate (full suite)

Branch `feat/vs-extension-hardening`. Task said HEAD `c37a2ce` + uncommitted review fixes; by the time the build started the git-manager had committed them as `56cfec8` (`fix(security): close review findings on the ide-safe follow-up`). HEAD stayed `56cfec8` for the whole run. Tree proof independent of HEAD: md5 of the 39 non-`plans/` files that differ from `c37a2ce` (or are untracked) — identical at start and end; `git diff HEAD --name-only` minus lock files = 0. So the tree tested is exactly commit `56cfec8` plus the 9 CRLF-only lock files.
Read-only run: no edits under `src/`/`tests/`, no git write commands. Scratch under the session scratchpad (`.../scratchpad/gate`, `.../scratchpad/e2e/{a,b2,g}`).
Baseline: `plans/reports/tester-260929-1412-followup-e2e.md` (tree `c37a2ce`).

Full-suite mode (task-specified): every suite + all gates; diff-aware mapping not applied.

## 1. Suites (Release)

Build: `dotnet build DataGuard.sln -c Release -m:1 -tl:off` → rc 0, 57 s.
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:57.06
```
(`grep -ci warning` on the log = 1, and that hit is the `0 Warning(s)` line itself.)

Test: `dotnet test DataGuard.sln -c Release --no-build -tl:off` → rc 0, 73 s wall.

| Project | Passed | Failed | Skipped | Total | Duration | Expected | Δ vs baseline | Result |
|---|---|---|---|---|---|---|---|---|
| DataGuard.Core.Tests (net9.0) | 892 | 0 | 0 | 892 | 1 m 5 s | ≥ 892 | +21 | PASS |
| DataGuard.VisualStudio.Tests (net472) | 155 | 0 | 1 | 156 | 10 s | ≥ 155 (+1 skip) | +3 | PASS (skip = NavigationTests, VS exp. instance) |
| DataGuard.Analyzers.Tests | 13 | 0 | 0 | 13 | 4 s | 13 | 0 | PASS |
| DataGuard.CodeFixes.Tests | 24 | 0 | 0 | 24 | 10 s | 24 | 0 | PASS |
| DataGuard.GoldenCorpus.Tests | 28 | 0 | 0 | 28 | 755 ms | 28 | 0 | PASS |
| DataGuard.Observability.Tests | 38 | 0 | 0 | 38 | 427 ms | 38 | 0 | PASS |
| VS Code `npm test` (`src/DataGuard.VSCode`) | 95 | 0 | 0 | 95 | 1.01 s (10 s incl. tsc) | ≥ 95 | +2 | PASS |

Verbatim last lines:
```
Passed!  - Failed:     0, Passed:    38, Skipped:     0, Total:    38, Duration: 427 ms - DataGuard.Observability.Tests.dll (net9.0)
Passed!  - Failed:     0, Passed:    28, Skipped:     0, Total:    28, Duration: 755 ms - DataGuard.GoldenCorpus.Tests.dll (net9.0)
Passed!  - Failed:     0, Passed:    13, Skipped:     0, Total:    13, Duration: 4 s - DataGuard.Analyzers.Tests.dll (net9.0)
Passed!  - Failed:     0, Passed:    24, Skipped:     0, Total:    24, Duration: 10 s - DataGuard.CodeFixes.Tests.dll (net9.0)
Passed!  - Failed:     0, Passed:   155, Skipped:     1, Total:   156, Duration: 10 s - DataGuard.VisualStudio.Tests.dll (net472)
Passed!  - Failed:     0, Passed:   892, Skipped:     0, Total:   892, Duration: 1 m 5 s - DataGuard.Core.Tests.dll (net9.0)
TEST_RC=0
ℹ tests 95 / ℹ pass 95 / ℹ fail 0 / ℹ skipped 0 / ℹ duration_ms 1014.4118   NPM_RC=0
```

## 2. Gates

| Gate | Command | Result |
|---|---|---|
| Policy self-test | `python scripts/check-workflow-policy.py --self-test` | rc 0 — 14/14 `[PASS]`, `check-workflow-policy --self-test: OK` |
| Policy | `python scripts/check-workflow-policy.py` | rc 0 — `check-workflow-policy: OK (VSIX assert, fork-PR upload guard, release VS tests)` |
| actionlint 1.7.12 | `actionlint .github/workflows/ci.yml .github/workflows/release.yml` | rc 0, no findings |
| Whitespace | `dotnet format whitespace DataGuard.sln --verify-no-changes` | rc 0, 16.5 s (only the usual "Warnings were encountered while loading the workspace" notice) |
| Lock files | md5 of all 24 `packages.lock.json` before build vs after build+test+format+e2e | **identical**; `git status --porcelain \| grep packages.lock.json` = exactly the 9 pre-existing ` M` entries (Analyzers, Cli, Contracts, Core, MySql/Oracle/PostgreSql/SqlServer adapters, SqlClassification); `git diff --ignore-cr-at-eol --quiet` clean → CRLF noise only |

Pre-commit hook (`.githooks/pre-commit`) runs `dotnet format whitespace --verify-no-changes`; the concurrent commit did not collide with any of my builds (no MSB3021 / file-in-use).

## 3. E2E scenarios (rebuilt CLI `src/DataGuard.Cli/bin/Release/net9.0/DataGuard.Cli.dll`, built 14:54:15, contains `SafeWritablePath`)

`DATAGUARD_CONNECTION_STRING` unset via `env -u` for (a)/(g); set explicitly for (b). All `--output` paths absolute. Default provider `sqlserver`.

### (a) hostile config + `validate --ide-safe --format sarif` — PASS (unchanged)
Fixture as baseline (`GroundTruthMode: Full`, `ConnectionString: Server=127.0.0.1,1;Connect Timeout=1`, `ManualAssemblyPath`, `AuditLogPath`, `EnableTelemetry: true`).
```
validate --ide-safe --project <a> --config <a>/.dataguard.yml --format sarif --output <a>/out/validation.sarif
EXIT: 0   ELAPSED: 845 ms
stderr (2 lines, verbatim):
ide-safe: active
ide-safe: suppressed GroundTruthMode=Full (forced to Snapshot); ManualAssemblyPath (assembly loading disabled); ConnectionString from config (database access disabled); AuditLogPath (repo-chosen write path); telemetry output (EnableTelemetry/TelemetryFileDirectory)
```
`validation.sarif` 375 bytes, `runs[0].results = []`; `summary.json` 138 bytes beside it. No connection attempt, exit 0, < 1 s. Identical to baseline.

### (b) env connection + `--ide-safe --allow-env-connection` — PASS (new assertions hold)
Task's "must NOT contain the config-sourced value" would be vacuous with the baseline fixture (config and env strings were equal), so the config was changed to a distinct sentinel:
`ConnectionString: Server=config-sentinel.invalid,1;Password=CONFIGSECRET;Connect Timeout=1`; env kept as `Server=127.0.0.1,1;Connect Timeout=1`.
```
ENV DATAGUARD_CONNECTION_STRING=Server=127.0.0.1,1;Connect Timeout=1
validate --ide-safe --allow-env-connection --project <b2> --config <b2>/.dataguard.yml --format sarif --output <b2>/out/validation.sarif
EXIT: 3   ELAPSED: 1716 ms
stderr (4 lines, verbatim):
ide-safe: active
ide-safe: suppressed ManualAssemblyPath (assembly loading disabled); live SQL shape rule disabled (use verify-shape); AuditLogPath (repo-chosen write path); telemetry output (EnableTelemetry/TelemetryFileDirectory)
ide-safe: kept environment connection (--allow-env-connection)
UNEVALUATED: contract acquisition failed: A network-related or instance-specific error occurred while establishing a connection to SQL Server. The server was not found or was not accessible. Verify that the instance name is correct and that SQL Server is configured to allow remote connections. (provider: TCP Provider, error: 0 - The wait operation timed out.)
```
- `live SQL shape rule disabled (use verify-shape)` present: **1 hit** (merged into the `suppressed` line, not a separate `ide-safe:` line — note for the hosts' parsers, which only key on the first `ide-safe: active` line).
- `config-sentinel` in stderr+stdout: **0**; `CONFIGSECRET`: **0**; `127.0.0.1` in stderr: **0** (neither connection value is echoed — the SqlClient error text carries no server name).
- Exit 3, connect failed in 1.7 s, no SARIF/summary written (pre-results failure) — same as baseline.

### (g) junction INSIDE the workspace → **FAIL** (SARIF written through the link)
Layout (`New-Item -ItemType Junction`, no admin): `g/ws/{Repo.cs,.dataguard.yml}`; `g/ws/junction → g/outside-target` (with real subdir `sub`); sibling `g/sibling-junction → g/sibling-target` (with real subdir `out`). cwd = `g/ws` for all three (both hosts spawn with the workspace/solution dir as cwd: `DataGuardPackage.Commands.cs:90` passes `solution.Directory`, `extension.ts:448` passes `workspaceFolder.uri.fsPath`, so `SafeWritablePath.IsSafe(path, Directory.GetCurrentDirectory())` is bounded correctly in production).

| Case | `--output` | Expected | Observed | Result |
|---|---|---|---|---|
| g1 (task-literal: junction is the parent) | `<ws>/junction/x.sarif` | rejected (exit 4 / write-path error) | **EXIT 0**, `outside-target/x.sarif` 375 bytes written; `summary.json` absent | **FAIL** |
| g2 (discriminator for the new ancestor walk) | `<ws>/junction/sub/x.sarif` | rejected | **EXIT 0**, `outside-target/sub/x.sarif` written; `summary.json` absent | **FAIL** |
| g3 (control: junction outside workspace) | `<sibling-junction>/out/x.sarif` | written | EXIT 0, `x.sarif` + `summary.json` written | PASS |

Verbatim g1 stderr (g2 identical):
```
ide-safe: active
ide-safe: suppressed GroundTruthMode=Full (forced to Snapshot); ManualAssemblyPath (assembly loading disabled); ConnectionString from config (database access disabled); AuditLogPath (repo-chosen write path); telemetry output (EnableTelemetry/TelemetryFileDirectory)
```
Rerun g1 ×3 (flaky rule): `EXIT=0 sarif_written=yes` all three — deterministic, not flaky.

Control through the guarded sink — `assess --ide-safe --workspace <ws> --format sarif --output <ws>/junction/a.sarif`, same cwd:
```
EXIT: 4
ide-safe: active
Assessment failed: Refusing to write SARIF through a symbolic link or invalid path.
```
`a.sarif` not written, no `.tmp` leftovers. So the policy itself works; `validate` never invokes it for the SARIF.

**Root cause (read, not guessed):** `validate --format sarif` writes via `emitter.AddSarifSink(new FileSarifSink(output!))` (`src/DataGuard.Cli/Program.cs:387`) → `FileSarifSink.WriteAsync` → `ContractExportWriter.WriteAtomicallyAsync` (`src/DataGuard.Core/Reporting/ContractExport.cs:228-237`, plain `Directory.CreateDirectory` + `File.Move`). No `SafeWritablePath`/reparse check anywhere on that path; `DiagnosticEmitter.cs` and `ContractExport.cs` are unchanged since `c37a2ce`. Only the *sidecar* `summary.json` (Program.cs:406) goes through the guarded `WriteTextAtomicallyAsync`, and its rejection is swallowed by the `catch (Exception) { /* Non-fatal */ }` at :409 — which is why g1/g2 show `x.sarif` but no `summary.json`, with no message at all. The comment deleted in this review round ("the validate SARIF sink applies the same policy") was never true, and the new `SafeWritablePath.cs:4` docstring repeats the claim: *"Write-path policy shared by every CLI file sink (SARIF, summary.json, init/config output)"* — the fix's own stated scope is not met for the validate SARIF sink.

Same unguarded sink, observed not inferred: `validate --ide-safe --format evidence --output <ws>/junction/e.json` → **EXIT 0, `outside-target/e.json` 75 bytes written** (`ContractEvidenceWriter` → `ContractExport.cs:313`). `verify-shape --output` (`Program.cs:1505`, second `FileSarifSink`) shares it too (not run here).

.NET sees the junction correctly (`DirectoryInfo.LinkTarget` = target, attrs `Directory, ReparsePoint`, both slash forms), so this is not a detection issue.

**Pre-existing vs regression:** pre-existing — `git show c37a2ce:src/DataGuard.Cli/Program.cs | grep FileSarifSink` gives the same two sink calls (:432, :1550); baseline scenario (a) never pointed `--output` at a link, so nothing measured it. **But it is exactly reviewer finding M3's attack** (`code-reviewer-260929-1412-vs-hardening-followup.md:18`: "Dev runs … `validate --output out/sub/x.sarif` in a hostile checkout whose `docs`/`out` is a committed symlink → file written outside the repo"). The M3 fix (bound the ancestor walk to CWD, `SafeWritablePathTests`) hardened the helper but the helper is never called on the validate SARIF/evidence path, so M3's scenario still succeeds verbatim. Verdict: M3 is not closed.

**Recommended fix:** in `Program.cs` validate/verify-shape actions, call `IsSafeWritablePath(output!)` before `emitter.EmitAsync` and fail with the same `Refusing to write SARIF through a symbolic link or invalid path.` + exit 4 path that `assess` uses; or move the check into `FileSarifSink`/`ContractExportWriter.WriteAtomicallyAsync` (needs `SafeWritablePath` lifted from Cli to Core). Also surface (or at least log) the swallowed `summary.json` rejection. Add `IdeSafeEndToEndTests.Validate_SarifOutputUnderInWorkspaceJunction_IsRejected` mirroring `Assess_OutputUnderJunction_WritesSarif` (:192) — the only junction e2e today covers `assess`, which is why 892 green tests did not catch this.

## 4. Flaky / Quarantined
None this run. All suites passed first attempt; the single failing scenario (g1) failed 4/4 (deterministic). `tests/.quarantine.json` untouched.

## 5. Performance / build notes
- Build 57 s (baseline 42 s — `-m:1`, cold obj after the review-fix commit); Core.Tests 65 s (baseline 55 s; +21 tests incl. live-junction `SafeWritablePathTests`); whole sln test 73 s wall; npm 10 s; format 16.5 s.
- No VSSDK pkgdef race with `-m:1`. No hook/build collision with the concurrent commit.
- (b) connect failure 1.7 s — `Connect Timeout=1` honoured.

## 6. Coverage observations
- No automated test drives `validate --format sarif --output <link>`; `SafeWritablePathTests` (5 cases) test the helper in isolation and `IdeSafeEndToEndTests` only exercises `assess` under a junction. Add the validate/verify-shape/evidence variants (see fix above).
- `summary.json` rejection is silent (`catch (Exception)` at Program.cs:409) — a repo-controlled link on the output dir hides the policy hit from the user.
- (b) shape-rule note is appended to the `suppressed` list rather than emitted as its own line; fine for the CLI contract (`IdeSafePolicy.LiveShapeRuleDisabledNote`) but `IdeSafePolicyTests` should pin ordering if hosts ever parse it.
- Both hosts pass the workspace/solution dir as cwd — verified by grep, not by a live host launch (out of scope, as in baseline).

## Unresolved questions
1. Report path: task says `tester-260929-1449-post-review-gate.md`; naming hook computed `tester-260929-1450-{slug}.md`. Used the task's path (parent reads it), as in the baseline.
2. Merge gate: the (g) expectation this task defined did not pass, and the failing case is reviewer M3's own scenario, so "M3 closed" in `56cfec8` is not true for `validate`/`verify-shape`/`evidence`. Whether that blocks merge is the owner's call; the fix is small (see §3) and the review already rated M3 "bounded, user-side". Recommendation: fix in this branch before `code-review` re-runs, since the commit message claims the findings are closed.
3. `verify-shape --output` (`Program.cs:1505`) not executed here — same sink, assumed same result; the fix should cover it and a test should pin it.

**Status:** DONE_WITH_CONCERNS
**Summary:** All suites green at the expected counts (Core 892, VS 155+1 skip, Analyzers 13, CodeFixes 24, GoldenCorpus 28, Observability 38, VS Code 95); build 0 warnings; policy self-test/policy/actionlint/format all rc 0; lock files byte-identical (9 CRLF-only entries, nothing new); scenarios (a) and (b) PASS including the new shape-rule note and config-secret non-disclosure. Scenario (g) FAILS deterministically (4/4): `validate --ide-safe --format sarif` (and `--format evidence`) writes through an in-workspace junction with exit 0 because `FileSarifSink`/`ContractEvidenceWriter` → `ContractExportWriter.WriteAtomicallyAsync` never calls `SafeWritablePath`; `assess` on the same junction rejects correctly (exit 4).
**Concerns/Blockers:** Correctness concern, address before review: the gate as specified failed on the hosts' primary command, and it is reviewer M3's scenario, so the review fix is incomplete against its finding (pre-existing sink, no test covered it). Fix: `IsSafeWritablePath(output!)` check before `EmitAsync`/evidence write at `Program.cs:387`, `:420`, `:1505` with the assess-style exit 4 message, or lift the guard into `ContractExportWriter.WriteAtomicallyAsync`; add `IdeSafeEndToEndTests` validate-under-junction case; surface the swallowed `summary.json` rejection. Nothing else blocks.
