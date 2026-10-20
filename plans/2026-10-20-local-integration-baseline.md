# Local integration baseline / Baseline tích hợp cục bộ

Read-only fetch confirmed origin/main `39a030e1a99b75e92d8bf68f8bdc62428ac5923e`. Separate worktree `/workspace/dataguard-integrated-baseline`, branch `integration/local-maintenance-baseline`, no upstream. Source branches and commits remain unchanged. No remote/main merge or publication.

## Included once, in dependency order / Tích hợp đúng một lần

| Source commit | Integrated commit | Scope |
|---|---|---|
| ad3eb30bad523643c348b4ecf5e81e3337be948c | a61a4531f9bb1ebc39592de1e20add97a73ff4ca | Scorecard bounded retries/report integrity |
| 2f6e315feb01c4a515576f30d9f2904e345d78ae | 1f385fa446faf406df74bd5b6b4c2b45ca100340 | Production VS output runtime tests |
| 6f038cdfc86aed6cacb019d944e3e4676fc5054a | 5646f8fcc339bcddbbc094bdb371393a32265abd | LSP empty changes and CR/LF ranges |
| a4c9869ec557eafecdc7bcb22e8bc33480306f41 | 638e39bb2940015811849de40b0bdc8ad5bd0c8f | Classifier prefix bounds/escaped identifiers |
| bb7670fdc5e4fb49c0e095e387aa3dc3fada6c0c | 9873bf25a9097bff9d773d54cae2e797bbedcda7 | Core constant Concat extraction |
| 429806c3aa7c6404f1c93eeb1d881e9bebbaf44b | 33391a9a1923fca51ffcef13e7a476fde31108e7 | Shared EF analyzer evaluator/quickfix safety |
| 232ab1431cb5871b117d4b654363f1841ce10611 | 087783f4f3edad9cb16b682649c0e9307b172546 | Evaluator allocation/cancellation boundaries |

First five source commits independently descend from main39a030e. Last two extend bb7670fd→429806c3→232ab143; each was applied exactly once. No requested patch excluded. No new feature added.

## Conflict and semantic reconciliation / Đối chiếu ngữ nghĩa

Only conflicts: EN/VI vscode-extension.md additions at the same end-of-file location, first LSP vs classifier then combined sections vs Core Concat. Kept all three sections; later analyzer change updates the Concat scope. No production/test conflict or dropped assertion. Historical plan banners distinguish earlier branch test/metric evidence from this integrated build. Source-owned evaluator remains shared, analyzer Roslyn4.12 pinned; LSP remains literal-based. Dapper typing, local flow and generator parity remain unsupported. VS output tests require Windows runtime; Linux compilation does not execute them.

Historical optimization benchmark compared429806c3→232ab143 with equal features: evaluator allocation reduced78–80%, total analyzer about4.44% at1000calls. Large-sample timing increased (analyzer wall138.019→151.738ms, edit459.418→812.503ms). Attribution remains unresolved; no speedup claim and these are NOT integrated-HEAD benchmarks. Allocated bytes are not RSS/leaks; weakref probes only demonstrated sampled objects reclaimable.

## Verification contract / Hợp đồng kiểm chứng

Run locked restore, full Release solution build, CrossPlatform non-LiveDb tests, binary compatibility compile, Node tests, Python/workflow/Scorecard tests, local git-tool fixture tests, format/docs/actionlint/policy, and actual Roslyn4.12 package consumer against the final integrated HEAD. Store exact SHA, commands/results and exclusions in `/workspace/.tmp/risk-maintenance/integrated-baseline/REPORT.md`. Do not sum source branch counts. Mandatory hooks remain enabled.

Canonical instrumented coverage is unresolved due to the existing DataGuard_Mutex /dev/shm permission blocker; no retry or bypass. Windows editorhost/VS runtime, real remote CI/Scorecard infrastructure, live DB and Marketplace execution are not performed. No uploads/push/PR/merge. Nếu gate thất bại phải ghi lỗi thực tế, không hạ gate hoặc bỏ assertion để gọi thành công.
