# 2026-09-29 — Red-team + validate of the VS hardening plan; TDD follow-up planned

**Input plan:** `plans/260929-0835-vs-extension-hardening/` (shipped as PR #24, commits `df442b9`…`8609e3e`)
**Output plan:** `plans/260929-0952-vs-hardening-redteam-tdd-followup/` (5 phases, tests-first, not executed by owner decision)
**Mode:** `/ck:plan red-team validate --tdd`, three Fable `code-reviewer` lenses in parallel (Security Adversary, Assumption Destroyer, Failure Mode Analyst), TypeSafe pre-adjudication, 7-question validation interview.

## What the red-team found that the implementation review missed

- **VS Code regression I introduced**: passing `--ide-safe` strips the user's own SecretStorage credential injected through `DATAGUARD_CONNECTION_STRING`, and the CLI's suppression line is filtered out of the channel, so users would see "0 findings" with no database rule run. Decision: new CLI flag `--allow-env-connection`, set by hosts only for user-supplied credentials.
- **No positive handshake**: hosts inferred "ide-safe honoured" from the absence of a rejection line. A hostile `.csproj` named `Unrecognized command or argument '--ide-safe'.csproj` is echoed by `assess` to stderr and would blank results while pointing users at an install command. Decision: the CLI prints `ide-safe: active` first; hosts publish only after seeing it.
- **Unclaimed package ID**: every "install the CLI" message names `DataGuard.Cli`, which returns 404 on nuget.org; `CliLocator` probes `~/.dotnet/tools` first when the bundle is missing. Decision: remove the guidance; owner reserves the ID.
- **Timeout-vs-exit race**: `StopProcess` reports `Terminated` when the CLI exits by itself during the `taskkill` attempt, so a run finishing at the boundary is discarded and its SARIF deleted.
- Plus consent keyed by folder only, release workflow skipping the VSIX assertions, fork PRs uploading unsigned VSIX artifacts, un-timed super-linear regex on attacker SQL, `Process.Start` on the UI thread, drain-grace dropping the Summary, inventory cleared before the reservation check, results published into whichever solution is open, and docs overclaiming VS Code coverage.

## Process notes

- Pushing needed two gate fixes: `.claude/` (kit runtime state) had to be allow-listed in `scripts/preflight_agent_check.sh` and recorded in `rules/workspace_governance.md`; `.gitignore` alone does not satisfy the filesystem-based preflight.
- The validate pass found three documentation-only inaccuracies in the shipped plan (test file name, old-CLI exit code 2 vs 1, CI timeout 30 vs 40); corrected in Phase 4 of the follow-up.
- Task hydration skipped: Task tools are not available in this session; the plan files are the source of truth.

## Next

```
/ck:cook D:\100.Software\Github\eco_support_net_oracle\plans\260929-0952-vs-hardening-redteam-tdd-followup\plan.md --tdd
```
