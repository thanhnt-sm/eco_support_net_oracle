# Extension lifecycle / Vòng đời extension — 2026-10-16

Local-only branch `local/ide-lifecycle`, worktree `/workspace/ide-lifecycle`, base
verified PR64 commit cc79f6449017b58e0167e1324f1e6b56ceea5629. No commit/push/PR changes.
Preserve PR64, previous worktrees and separate Scorecard documentation patch. No
GitHub API calls or workaround for another task's denial; no branch-normalizer work.
SDK 9.0.319 and Node 22.23.3 verified before edits, actionlint 1.7.12 available.

Baseline: unchanged all-16-file c8 scope, `npm run test:coverage`. Prior exact base:
117 passing tests, 2737/4228 lines (64.73%); extension.ts 0/964. Capture fresh baseline.
No exclusion changes. Target meaningful coverage of registered activation/commands,
process completion/error/cancellation and consistent UI state; aspirational extension
>60% lines and overall >78%, not a reason to execute irrelevant statements.

| Requirement / Yêu cầu | Source | Regression contract / Kiểm thử |
| --- | --- | --- |
| Dispose owned resources even if subscriptions run after deactivate | extension.ts activate/deactivate | Record registration/disposal after deactivate; LSP stop completion deferred |
| Clear old Problems/tree/hover/lenses/scan state consistently | runCliCommand, clearFindingsAndDiagnostics | Success → missing/invalid result/start error; explicit clear |
| Superseded/cancelled work cannot publish | runCliCommand/cancelValidation | Controlled pending setup/read/process completions after reservation |
| Process subscriptions cleaned on every exit path | waitForExit | Stream listener counts after success/error/cancellation |
| IDE-safe handshake remains mandatory | runCliCommand | Missing acknowledgement discards synthetic SARIF |
| Accurate diagnostic ranges | loadDiagnostics/toRange | Actual command → multiline Problems coordinates |

Production extension is loaded via its public activate/deactivate entrypoints; command
callbacks are invoked as the editor would invoke them. Use real synthetic temp files,
recording VS Code/provider API doubles and fake child emitters; never spawn real CLI,
kill a real process, use business code, DB or credentials. No actual editor-runtime claim.
Regression-first, minimal fixes, independent review, unchanged local gate with SKIP_ACT=1,
EN/VI docs and separate patch/evidence. / Test module thật qua mock API; không phải editor thật.

## Results / Kết quả

- Base is the preserved PR64 local commit above; this batch changes only VS Code
  lifecycle production code, its tests, test entry list and EN/VI docs. Earlier VS
  SARIF/consent tests remain in the base; no new Windows claim is made here.
- Baseline: `npm run test:coverage`, 117 passed; all 16 production TS modules,
  lines 2737/4228 (64.73%), raw c8 branches 430/500 (86.00%).
- Initial regression run: 17 tests, 10 failed / 7 passed on unchanged source.
  Final 21-test suite against original source: 14 failed / 7 passed. Reviewer-found
  delayed config lookup: separately reproduced 1 failed / 20 passed before its fix.
- After: `npm test` and `npm run test:coverage`, 138 passed, zero skipped/failures.
  All 16 modules: lines 3414/4253 (80.27%), raw branches 603/751 (80.29%).
  `extension.ts`: 675/989 lines (68.25%), 151/234 branches (64.52%).
  Source denominator grew by 25 lines for fixes; no production exclusions, generated
  coverage configuration changes or threshold changes. c8/V8 discovers more branches
  when a formerly unexecuted module is exercised; raw branch percentages are reported
  with denominators, not represented as a fixed-branch improvement.
- `dashboard-panel.ts` (222 lines) and `sql-queries-tree-provider.ts` (238 lines)
  remain 0% production-module coverage. Lifecycle tests use recording collaborators,
  not those real UI implementations. No coverage gaming by removing difficult files.

| Requirement | Production source | Tests in extension-lifecycle.test.ts | Evidence |
| --- | --- | --- | --- |
| Owned decoration disposal | activate subscriptions | activation disposal after deactivate | red 1 → green |
| Multiline diagnostic end and nonempty same-line span | toRange/loadDiagnostics | registered validation; explicit clear/range | red 2 and final suite → green |
| Consistent stale-result clearing | runCliCommand/clearFindingsAndDiagnostics | missing/invalid result, spawn failure, explicit clear | red 3–4 and final suite → green |
| Completion cleanup | waitForExit finally | success/error/cancel data subscriptions | red 5–7 → green |
| Reservation cancellation | cancelValidation | pending temporary-directory preparation | red 9 → green |
| Current-run publication only | runCliCommand/waitForExit/loadDiagnostics | old process error, delayed SARIF/summary/config | red 10–11, final suite, review-red → green |
| Safety guards preserved | runCliCommand | missing ACK, trust/workspace/config/provider/secret/consent | original and final green |

Evidence directory (local, untracked): `/workspace/.tmp/ide-lifecycle-evidence/`.
Commands from repository with `source /workspace/.setup/activate.sh`:

```bash
cd src/DataGuard.VSCode
npm_config_cache=/workspace/.cache/npm npm ci --ignore-scripts
npm test
npm run test:coverage
node --test out/extension-lifecycle.test.js
# From repository root:
PATH=/workspace/.tools/actionlint-1.7.12:$PATH SKIP_ACT=1 ./scripts/verify_local_gates.sh
./scripts/verify_docs_sync.sh
git diff --check
```

Independent review: `/root/independent_review` inspected source/tests and original red
log, independently ran the then-current 20 tests, identified stale config output/error
publication, and requested narrower cancellation/LSP claims. The added config test
failed before guarding the continuation. Final review/gate evidence recorded below.

Limits / Giới hạn: synthetic files only, no real CLI/OS process kills, DB, credentials,
user code or external publication. Fake child has no pid. Actual VS Code editor host
is unavailable in this environment (previous official download attempt returned 403;
no display/Xvfb); no workaround attempted. LSP shutdown/start races, configuration-change
policy and cancellation before reservation are deferred. Windows/Visual Studio runtime
and hosted CI remain pending for this new patch; prior PR64 results are not new-batch
validation. SKIP_ACT=1 is the existing local mode, omitting act/Docker and remote secret
verification; hooks and gate source remain unchanged. / Chưa kiểm chứng runtime editor,
Windows hoặc hosted CI cho patch mới; không upload hay thay đổi trạng thái GitHub.

Final local verification: independent reviewer reran **21/21** lifecycle tests and
approved with no remaining blockers. Unchanged local gate exited 0: locked restore,
Release build/analyzers (0 warnings/errors), formatting checks, topology/preflight,
actionlint, docs, NuGet audit, **2213 C# tests passed / 5 Windows-only junction skips**;
union line coverage **17019/20978 = 81.13%**. Formatting reported workspace-loading
warnings while returning 0; no gate bypass or source changes. The C# denominator is
unchanged; small run-to-run covered-line differences are not attributed to TS fixes.
Gate needed the existing local execution exception for synthetic Git repositories
and .NET mutexes; no external upload or hosted workflow ran. Docs sync and whitespace
checks passed after the EN/VI edits. Preserved PR64 worktree remains clean at cc79f64.
