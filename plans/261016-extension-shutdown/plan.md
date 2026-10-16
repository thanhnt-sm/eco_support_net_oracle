# Pending commands and LSP shutdown / Lệnh chờ và dừng LSP — 2026-10-16

Local worktree `/workspace/ide-shutdown`, branch `local/ide-shutdown`, base cc79f64
plus the preserved lifecycle patch SHA256
`2da0a9736519ade2ba24e74a9450b98d110533d8ad6726b23fd2e40eb797a9eb`.
The original worktrees/patches and PR64 remain untouched. No remote actions, commit,
push, upload, account setup, protection changes, or automated publication.
SDK 9.0.319 / Node 22.23.3 checked before edits. Existing AGENTS/rules apply.

## Acceptance / Tiêu chí

| Requirement | Source | Behavioral evidence planned |
| --- | --- | --- |
| Cancel pending workspace/secret/consent continuation | extension.ts runCliCommand/cancelValidation | Deferred API replies after cancel/deactivate/replacement cannot spawn |
| Shutdown prevents late client creation and retains cleanup for pending startup | extension.ts activate/deactivate/startLanguageServer | Deferred artifact/start, stop completion and rejection, disposal order |
| Exercise uncovered real UI controllers | dashboard-panel.ts, sql-queries-tree-provider.ts | Actual production classes, recorded editor API; navigation guards/messages/grouping |
| Preserve safety and coverage scope | all 16 production TS files | Same coverage command baseline/after, no exclusions/threshold changes |

Tests before fixes; minimal production changes only after reproduced failure.
Independent review, unchanged local gate (`SKIP_ACT=1`), docs sync and EN/VI mapping.
Target >90% all-file line coverage through useful behaviors, not a guaranteed outcome;
report raw branch counts and remaining uncovered behavior separately. No tests invoke
real CLI, OS process termination, DB, external uploads or user business code. Editor
API doubles and local synthetic files do not establish actual VS Code/Windows runtime.

## Execution and evidence / Thực hiện và bằng chứng

Fresh baseline after applying the preserved patch: **138 tests passed**, all 16 TS
production modules, **3414/4253 lines (80.27%)**, raw c8 branches **603/751 (80.29%)**.
Final TypeScript run: **166 passed**, no failures/skips; **3945/4309 lines (91.55%)**,
**775/925 branches (83.78%)**. The >90% goal is met for lines, not branches.
`extension.ts`: **750/1045 lines (71.77%)**, **187/264 branches (70.83%)**.
Dashboard controller: **218/222 lines (98.19%)**; SQL-tree controller: **238/238 (100%)**.
All 16 files remain included; production denominator grew by 56 lifecycle-management
lines. No exclusion, gate, hook, lockfile, coverage-normalizer or threshold changes.
V8 branch discovery expands when formerly unexecuted modules run; report counts as
well as percentages. Remaining branches include actual process-kill platform paths,
filesystem failure paths, command setup and webview-renderer fallbacks.

| Requirement | Source | Test / outcome | Evidence |
| --- | --- | --- | --- |
| Cancel at workspace/secret/consent await | extension.ts commandRequest | Three deferred replies cannot spawn | red.log: 3 failures → green |
| Replace/deactivate pending secret read | runCliCommand/deactivate | Late continuation cannot spawn or clear current result | red.log: 2 failures → green |
| Await owned stop once | LanguageServerSession | Delayed stop; subscription before/after deactivation | red.log: failure → green |
| No late client after verification | startLanguageServer | Delayed valid/invalid artifact verification | red.log: failure → green |
| Wait for startup before stopping | stopLanguageServer | Delayed start settles, then exactly one stop | red.log: failure → green |
| Failure does not bypass cleanup | startLanguageServer/stopLanguageServer | Stop, constructor and start rejection, redaction | red.log and review-red.log → green |
| Bounded LSP wait | deactivate | Mock-clock deadline while start/stop remains pending; UI disposed before release, late stop once | review-red.log: 2 failures → green |
| Suppress stale errors | pending command/LSP shutdown | Cancelled secret rejection and late stop rejection | final green |
| Real controller contracts | dashboard-panel.ts/sql-queries-tree-provider.ts | 11 navigation/dispatch/update/grouping/redaction/disposal tests | full final green; production UI unchanged |

Initial new lifecycle regressions: **9 failed / 22 passed** on the preserved lifecycle
source. Independent review found unbounded wait and constructor failure; added tests
reproduced **3 failed / 32 passed** before those corrections. Final focused suites:
**49/49 passed** independently, with no remaining review blockers. No production UI
changes were needed for the 11 controller tests. Full suite adds 28 tests to baseline.

Evidence (local only): `/workspace/.tmp/ide-shutdown-evidence/` contains fresh baseline,
red/review-red and final logs, summaries, prior patch snapshot, incremental and cumulative
patches and manifest. Run commands with `source /workspace/.setup/activate.sh`:

```bash
cd src/DataGuard.VSCode
npm_config_cache=/workspace/.cache/npm npm ci --ignore-scripts
npm test
npm run test:coverage
node --test out/extension-lifecycle.test.js out/ui/controllers-behavior.test.js
# Repository root:
PATH=/workspace/.tools/actionlint-1.7.12:$PATH SKIP_ACT=1 ./scripts/verify_local_gates.sh
./scripts/verify_docs_sync.sh
git diff --check
```

## Limits / Giới hạn

The LSP wait is bounded to 1,000 ms; CLI cleanup can independently add up to its existing
1,000 ms wait. The tracked shutdown promise stops a late-starting client exactly once.
Installed `vscode-languageclient` 10.1.2 `lib/common/client.js` rejects stop while Starting;
a permanently pending initialization may remain pending after UI cleanup. No claim of
OS process termination is made. Stalled operation tests use a mock clock and leave the
operation unresolved through the deadline, then release it to verify late cleanup.
Controller tests run production classes through editor API doubles, not a real editor
or webview DOM. No real CLI/LSP process is launched; synthetic DLL bytes only exercise
integrity checks. Existing official editor-download 403/no display limitations persist;
no download workaround or external action was attempted. Windows/Visual Studio runtime,
actual editor behavior and hosted CI remain pending. PR64 and earlier patches untouched.
`SKIP_ACT=1` is existing local mode: act/Docker and remote secret verification are omitted,
not claimed passed. / Không publish; chưa kiểm chứng editor/Windows/process thật.

## Final local gate / Gate cục bộ cuối

Unchanged `SKIP_ACT=1 ./scripts/verify_local_gates.sh` exited 0. Locked restore,
Release build/analyzers (0 warnings/errors), topology/preflight, actionlint, EN/VI
docs, formatting and NuGet audit passed. C# tests: **2213 passed / 5 Windows-only
junction skips**, union line coverage **17017/20978 (81.12%)**. Formatting emitted workspace-
loading warnings but returned 0. No C# source or coverage configuration changed;
small run-to-run coverage variation is not attributed to the TS fixes. Local gate
execution used the established sandbox exception for synthetic Git repositories and
.NET mutexes. Final independent code review: 49/49 focused tests, no blockers.
Documentation review confirmed metrics and runtime limits; its wording correction
is applied. Final docs sync and diff whitespace checks passed. No remote action ran.
