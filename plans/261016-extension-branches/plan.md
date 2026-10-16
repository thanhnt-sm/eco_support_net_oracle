# Extension branch behavior / Nhánh hành vi extension — 2026-10-16

Local branch `local/ide-branches`, worktree `/workspace/ide-branches`, base cc79f64 plus
preserved cumulative shutdown patch SHA256
`1685ebfcbd890f43ebb0883ff0483cb0f5ec1543d7461c8cc30d4d5ed6c5c304`.
SDK9.0.319/Node22.23.3 verified before edits. Prior worktrees/patches/PR64 preserved.
No commits, publication, remote mutation or uploads. Read AGENTS/rules inherited from
prior batches; unchanged gates and all16 production TS coverage scope remain required.

| Requirement | Source | Acceptance evidence |
| --- | --- | --- |
| Assess stalled LSP with supported APIs | installed vscode-languageclient10.1.2 node/common main/client d.ts/js | Public serverProcess/dispose; no private internals or arbitrary PID kills |
| Deadline handles exposed owned server | extension.ts deactivate | Failing behavior test then bounded disposal/owned-child kill; synthetic child exit observation |
| Cover real error/config/cancellation branches | extension.ts and pure helpers | Public commands, synthetic local files, mocks for editor/process boundaries |
| >90% branches without line regression | all16 TS production files | Baseline/after raw counts; no exclusions, denominator manipulation or weak gates |

The installed client's stop rejects while Starting; dispose marks it disposed and
calls stop, with node shutdown scheduling process checking. A deadline can use the
public serverProcess handle to request SIGKILL on that child, never a guessed PID or
process group. A client whose transport has not yet exposed its process cannot be
forcibly terminated through that getter; retain late-settlement cleanup and report
this residual limit. Real editor/.NET LSP runtime remains unverified. Synthetic Node
fixture children may be spawned solely to verify owned-child termination, never CLI,
database, business code or external service. Independent review and EN/VI docs required.

## Measured results / Kết quả đo

Fresh baseline: **166 tests passed**, **3945/4309 lines (91.55%)**,
**775/925 raw branches (83.78%)**. Final: **189 tests passed**, no failures/skips,
**4146/4326 lines (95.83%)**, **1053/1126 branches (93.51%)**. Both all-file goals
exceed90%; all16 production modules retained. `extension.ts`: **903/1062 lines
(85.02%)**, **359/405 branches (88.64%)**. Individual extension branch coverage is
still below90%; do not confuse that with all-file coverage. Source denominator grew
by17 lines for the bounded force-disposal fallback. V8 discovers more branches as
new existing paths are exercised (+201 raw branches); no exclusions or coverage
script, normalizer, gate, hook, dependency/lock or threshold changes.

Only incremental production edit: deadline invokes a small owned-child fallback
using public APIs. Remaining changes add23 tests and docs/plan/test entry list.

| Requirement | Source | Tests/evidence |
| --- | --- | --- |
| Terminate exposed owned child after startup stalls | extension.ts forceLanguageServerShutdown/deactivate | `stalled LSP deadline...`: lsp-red.log1failure, then green; actual synthetic Node child spawn/exit, mocked client disposal rejects like Starting |
| Preserve cleanup on failed termination | same | already-exited child, kill returning false, kill throwing, dispose rejection; no arbitrary process targeted |
| Configuration and credentials | runCliCommand/configureConnection | unsafe config paths; existing config; explicit/blank synthetic secret; store/delete/cancel; workspace selection |
| Finding/provider commands | handleApplyQuickFix/registered commands | known actionable current finding/range, absent ID, manual-guidance forwarding, grouping and dashboard |
| Summary and diagnostic boundary cases | runCliCommand/loadDiagnostics | verify-shape mapping/defaults; malformed summary; missing/external SARIF locations; severity/range defaults |
| Process failure and bounded progress | runCliCommand/waitForExit | warning/error/assessment summaries, timeout, redacted failures, oversized progress and partial UTF8 |
| Pure contract branches | edge-contracts.test.ts | malformed summary counts; unknown/cancelled exits; environment filtering; cancellation exception ownership; redaction/SARIF defaults; dashboard optional arrays/locations |

## Supported API assessment / Đánh giá API

Installed `vscode-languageclient`10.1.2 declarations:
`lib/node/main.d.ts` exposes `serverProcess: ChildProcess | undefined`;
`lib/common/client.d.ts` exposes `dispose(timeout?: number): Promise<void>`.
Implementation inspected locally: common `stop/shutdown` rejects when Starting;
`dispose` marks the client disposed before its stop attempt returns and prevents
future `start` reuse. Node shutdown may clear its child reference and schedules its
own PID-based process cleanup, so capture the owned child first. The extension's
new code uses only that ChildProcess object's kill method; it adds no direct PID or
process-group operations. Independent review confirmed this reading and requested
Starting-like dispose rejection coverage; the synthetic-child test now includes it.

A real owned Node fixture is spawned solely to idle; deadline fallback requests kill,
and test awaits its actual exit event. Client/editor APIs remain doubles. This proves
the direct owned-child fallback for the fixture, not real language-client integration,
.NET LSP handshake, descendant process termination or Windows editor runtime.
If no process is exposed at the deadline, or OS termination is refused, full termination
cannot be asserted. The previous bounded wait and late-start cleanup remain intact.
Eliminating the unexposed-transport race would require owning server creation or changing
the library's initialization contract; deferred to avoid a broad launch refactor.

## Reproduction / Chạy lại

Evidence directory: `/workspace/.tmp/ide-branches-evidence/` (local/untracked), including
baseline/after logs, raw summary JSON/LCOV, lsp-red/lsp-green, incremental and cumulative
patches, prior cumulative snapshot, review notes and manifest. Use verified SDK/Node:

```bash
source /workspace/.setup/activate.sh
cd src/DataGuard.VSCode
npm_config_cache=/workspace/.cache/npm npm ci --ignore-scripts
npm test
npm run test:coverage
node --test out/extension-lifecycle.test.js out/edge-contracts.test.js
# At repository root:
PATH=/workspace/.tools/actionlint-1.7.12:$PATH SKIP_ACT=1 ./scripts/verify_local_gates.sh
./scripts/verify_docs_sync.sh
git diff --check
```

Prior source/patch hashes and clean PR64 checkout will be verified unchanged. Incremental
patch applies over prior shutdown cumulative patch; cumulative patch applies to cc79f64.
No publication/remote mutation. Actual VS Code host remains unavailable (prior official
download403/no display); no workaround attempted. Windows/Visual Studio and hosted CI
remain pending. SKIP_ACT=1 is existing local mode, omitting act/Docker and remote secret
verification, not claiming they passed. Remaining uncovered branches include platform
process termination, rare filesystem races/errors and obsolete/private helper paths.
No code is excluded to avoid these branches. / Chưa kiểm chứng runtime editor/Windows;
không giảm gate, loại file production hoặc publish.

Independent final review reran **61/61 lifecycle + edge-contract tests** and found no
code blockers. EN/VI wording now distinguishes one tracked late-start cleanup operation
from the separate deadline dispose attempt (dispose itself calls stop in the library).
The fallback tests include Starting-like disposal rejection and still observe the
synthetic owned child exit. Failed/rejected kill paths preserve editor cleanup without
claiming termination succeeded. Docs and diff checks pass; final gate recorded below.

## Final local gate / Gate cuối

Unchanged local gate exited0: locked restore, Release build/analyzers (0 errors/warnings),
formatting, topology/preflight, actionlint, docs and NuGet audit passed. C# tests:
**2213 passed / 5 Windows-only junction skips**, union coverage **17022/20978 (81.14%)**.
Formatting emitted workspace-loading warnings while returning0. No C# changes; small
coverage variation is not attributed to TypeScript work. The established local sandbox
exception was used for .NET mutexes/synthetic Git repositories. Final docs/diff checks
passed. Incremental patch applied over prior snapshot and compared byte-for-byte;
cumulative reverse-apply check passed. Prior lifecycle/shutdown source hashes and clean
PR64 checkout remain unchanged. No remote mutation, commit, push or upload occurred.
