# LSP protocol regression maintenance / Bảo trì regression giao thức LSP

Baseline: main `39a030e1a99b75e92d8bf68f8bdc62428ac5923e`. Local branch: `fix/lsp-protocol-regressions`. Scope: synthetic subprocess fixtures only; no database, secrets, default telemetry or cloud service. Preserve Marketplace publication pause.

## Prioritized plan / Kế hoạch ưu tiên

No P0 confirmed. Two bounded fixes in this batch:

| Priority | Reproduction and source | Acceptance / Tiêu chí |
|---|---|---|
| P1 | Initialize, open SQL, send `didChange` with `contentChanges: []`: `Program.cs` calls `Last()` and process exits. | Keep pending diagnostic and process alive; shutdown reply and exit zero. Không hủy diagnostic đang chờ. |
| P2 | Open SQL with standalone CR before SELECT: `ComputeLineStarts` counted LF only. | Exact token range for CR, LF, CRLF and mixed sequences with tab and emoji. Vị trí UTF-16 chính xác. |

Implementation: return before cancellation for empty changes; consume CRLF as one line separator. Existing full-text sync, 150 ms debounce, limits and syntax-only classification remain. Line indexing remains O(n), position lookup O(log lines); no dependency or architecture change. Tests: `tests/DataGuard.Core.Tests/LanguageServerLifecycleTests.cs`. Documentation: paired EN/VI `docs/03-components/tooling/vscode-extension*`.

This deepens the existing lightweight IDE feedback goal from ADR-0002 without expanding SQL validation claims. Empty changes are a content no-op; this server currently does not attach document versions to published diagnostics.

## Evidence / Bằng chứng

Verified tools: .NET 9.0.319, Node 22.23.3. Evidence directory: `/workspace/.tmp/risk-maintenance/lsp-protocol-regressions/`.

- `red.log`: 4 failed, 7 passed; failures include empty changes and three standalone-CR cases.
- `green.log`: 11 passed, 0 failed, 0 skipped after the two source fixes.
- `build.log`, `tests.log`, `benchmark.jsonl`: final build, noninstrumented test execution and before/after characterization recorded separately.
- Benchmark uses 7 serialized requests per 100/2000-line synthetic CR document, includes 150 ms debounce and process warm-up effects; no pure-algorithm throughput or statistically established speedup claim.
- Current production coverage is unmeasured: prior canonical instrumented test execution hit read-only `/dev/shm` mutex creation. Do not infer coverage from ordinary passing tests; do not bypass hooks. Windows runtime/editor-host and hosted CI are not established by this local batch.

## Independent adversarial backlog / Backlog đối kháng độc lập

| Priority | Confirmed evidence | Next acceptance |
|---|---|---|
| P1 | C# scanner retries each dollar of malformed prefix; n+(n−1)+… work. 100k dollars plus a later SQL literal took ~3.015 s through baseline LSP, versus ~0.289 s at 1k (startup/debounce included). | Consume failed prefixes once, including interpolation-hole caller; preserve later literals and offsets; bounded before/after measurement. |
| P2 | `SELECT * FROM [Orders]]DELETE FROM Fake]` creates false DELETE and truncates identifier. | Escaped brackets remain one identifier; unescape names correctly, including qualified/repeated escapes. |
| P2 | C# `// comment\rvar sql = "SELECT * FROM Orders";` loses literal; LF succeeds. | C# newline grammar coverage including CR/LF/CRLF/NEL/LS/PS, directives and offsets. Separate from LSP range fix. |
| P2 | Framed invalid JSON aborts process; initialize after shutdown returns capabilities. | Framed parse error and correct shutdown-state errors, bounded recovery tests. |
| Hardening | Partial-header EOF succeeds; conflicting duplicate lengths accept first. Existing header/body caps reject oversize declarations. | Distinguish clean EOF/truncation; reject ambiguous lengths. |

Pending dictionary concurrency, stale publication and cancellation during classification are source-review hypotheses, not reproduced failures. Require deterministic barrier-based regression before changing ownership/state architecture. Không coi giả thuyết race là lỗi đã chứng minh.

## Primary contracts / Nguồn chính

- [LSP didChange](https://github.com/microsoft/language-server-protocol/blob/gh-pages/_specifications/lsp/3.17/textDocument/didChange.md): array has no nonempty constraint; content-no-op inference follows sequential application.
- [Microsoft TextDocument](https://github.com/microsoft/vscode-languageserver-node/blob/main/textDocument/src/main.ts): reference line-offset behavior for CR/LF/CRLF; no source copied.
- [LSP position](https://github.com/microsoft/language-server-protocol/blob/gh-pages/_specifications/lsp/3.17/types/position.md): UTF-16 default.

## Measured local results / Kết quả local

- Full `DataGuard.sln` Release build: zero warnings/errors.
- CrossPlatform noninstrumented tests (`Category!=LiveDb`): 2320 passed, 5 Windows junction tests skipped, zero failures. Baseline maintenance run: 2313 passed, same 5 skipped; seven new regression cases account for the difference.
- Independent reviewer: 11 lifecycle tests passed; no blocking issue. Immediate open/change sends cannot guarantee pending timing under arbitrary scheduler stalls; the test decisively covers the original crash and diagnostic/session retention.
- Median diagnostic latency, 7 samples/workload: 100 CR lines 154.00 → 154.35 ms; 2000 CR lines 156.44 → 154.36 ms. Debounce dominates; no statistically established improvement claimed. After measurements assert corrected line positions.
- Node compilation and tests: 189 passed, zero failures/skips. Dependencies installed from lockfile with lifecycle scripts disabled and a workspace-local npm cache. No extension-host run.
- Documentation synchronization and actionlint passed. A whitespace check found formatting in new test initializers; those were formatted before the final verification.
- Node coverage command passed: 95.83% lines/statements, 93.51% branches, 97.35% functions. Scope is compiled `out/**/*.js` with `--all`, excluding `*.test.js`, as defined by the existing coverage script; this is neither .NET production coverage nor editor-host acceptance.
