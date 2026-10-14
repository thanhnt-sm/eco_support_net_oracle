---
phase: 2
title: "VS Code surfaces ground-truth and run state truthfully"
status: pending
priority: P1
effort: "3h"
dependencies: [1]
---

# Phase 2: VS Code surfaces ground-truth and run state truthfully

## Overview
A developer in the database phase must be able to tell, from the status bar and the DataGuard output channel, whether the database-backed rules actually ran. Today a run with **no ground truth** or with **unevaluated contracts** looks like a normal pass/fail (RC2) and an exception exit looks like "found findings" (RC3).

## Key Insights
- The CLI already tells the host everything it needs, on stderr: `Warning: no ground truth (...) ; only syntactic rules ran` (`src/DataGuard.Cli/Commands/ValidateCommand.cs:346`), `UNEVALUATED: ...` (`:307-311`, `:478-482`), `ACQUISITION: ...`, `Rule DG012 not evaluated: ...` (reproduced with `--provider oracle`), and the `Summary` progress event with `UnavailableRuleCount`, `UnevaluatedContractCount`, `AcquisitionDiagnosticCount` (`ValidateCommand.cs:494-499`).
- The host drops all of them: `formatProgressLine` keeps only `[INFO]/[WARN]/[ERROR]`-prefixed, `ide-safe:`/`baseline:` and JSON lines (`src/DataGuard.VSCode/src/ide-safe-contract.ts:60-85`); `formatProgressEvent` renders `Summary` only through `Detail`/`Phase` (`:91-116`), so the counts are never shown.
- Exit mapping: `0 → idle`, `1 → warning "found findings"`, else → error "could not complete" (`extension.ts:596-603`). The CLI exits 1 for exceptions too (`ValidateCommand.cs:507-515`, `ScanCommand.cs:144-151`, `BaselineCommand.cs:260-268`, `SnapshotCommands.cs:107-115`) and 3 for unevaluated/no-connection (`ValidateCommand.cs:307-311`, `SnapshotCommands.cs:54-59`, `VerifyShapeCommand.cs:95-100`), 4 for an unsafe output path (`OutputSinks.cs:307`).
- The raw-error echo on non-zero exit filters on substrings `Error|fail|Exception` (`extension.ts:502-511`); `Warning: no ground truth` passes none of them.
- Channel header says "Local assessment started" for every non-validate command including `scan` (`extension.ts:450`).
- The CLI writes `summary.json` next to `--output` for `validate --format sarif` too (`ValidateCommand.cs:421-438`), so the scan tree can be refreshed after validate without a second run (the host already reads it, `extension.ts:538`).

## Requirements
- **Functional:**
  1. A validate/assess run that loaded no ground truth ends in status `warning` with a notification *"DataGuard ran syntactic rules only: no snapshot, connection or EF model was loaded"* offering actions **Configure Connection Credential** and **Refresh Snapshot** (Phase 4 adds the EF path).
  2. `UNEVALUATED:`, `ACQUISITION:`, `Warning:` and `Rule <id> not evaluated:` lines are echoed as `[WARN]` (redacted); the `Summary` event renders `errors/warnings; unevaluated N; unavailable rules N`.
  3. Exit 1 is "findings" **only** when SARIF (validate/assess) or summary.json (scan/verify-shape) was produced; otherwise it is an error with the first non-JSON stderr line in the notification.
  4. Exit 3 → status `error`, notification *"DataGuard could not evaluate: <first UNEVALUATED/ACQUISITION line>"*; exit 4 → *"DataGuard refused the output path"*; exit 2 → *"DataGuard rejected the arguments or config: <line>"*.
  5. Channel header names the command.
- **Non-functional:** all new logic in pure functions (`ide-safe-contract.ts` / new `run-outcome.ts`) with node tests; `extension.ts` only wires them; no new dependency.

## Architecture
```
stderr lines ──► processProgressText ──► formatProgressLine (extended: WARN for policy lines)
                                     └─► collectRunSignals (new): { groundTruthMissing, unevaluated, unavailableRules, firstDiagnosticLine, summaryCounts }
exit code + signals + artifactPresent ──► classifyRunOutcome (new, pure) ──► { status, notification, actions[] }
extension.ts: setStatus(outcome.status); showMessage(outcome.notification, ...outcome.actions)
```
`collectRunSignals` is fed by the same line stream as `formatProgressLine` (no second pass over output). `ChildExit` gains `signals`.

## Related Code Files
- **Create:** `src/DataGuard.VSCode/src/run-outcome.ts`, `src/DataGuard.VSCode/src/run-outcome.test.ts`
- **Modify:** `src/DataGuard.VSCode/src/ide-safe-contract.ts` (policy-line classes, `Summary` rendering with counts), `src/DataGuard.VSCode/src/ide-safe-contract.test.ts`
- **Modify:** `src/DataGuard.VSCode/src/extension.ts` (`ProgressTextState`, `waitForExit`, outcome branch at `:595-603`, header at `:450`, action buttons that invoke `dataguard.configureConnection` / `dataguard.refreshSnapshot`)
- **Modify:** `src/DataGuard.VSCode/package.json` (`test` script: add the new test file), `src/DataGuard.VSCode/README.md`, `CHANGELOG.md`, `docs/USAGE.md` §VS Code

## TDD Structure (`--tdd` Mandatory)

### 1. Tests Before (Step 3.T — Regression Coverage First)
- File test cần tạo/cập nhật trước khi sửa code: `src/DataGuard.VSCode/src/ide-safe-contract.test.ts`, `src/DataGuard.VSCode/src/run-outcome.test.ts`
- Các kịch bản hành vi hiện tại cần khóa lại (pin current behavior):
  - [ ] `formatProgressLine("ide-safe: active")` → `[INFO] ...`; `ide-safe: suppressed ...` / `baseline: ...` → `[WARN]`; `[INFO] Found SQL ...` echoed; JSON `BaselineApplied` → `[WARN] baseline: N suppressed by ...`; arbitrary text → `undefined` (today's contract, must keep).
  - [ ] Redaction still applies to every echoed line (reuse the cases in `redaction.test.ts` for a line containing `Password=`).
  - [ ] `classifyRunOutcome({ exitCode: 0, artifactPresent: true, signals: none })` → `idle`; `exitCode: 1, artifactPresent: true` → `warning` "found findings" (today's behaviour preserved for the real-findings case).

### 2. Refactor & Implementation (Step 3.I — Protected Code Changes)
1. **Step A (Write tests for current behavior):** add the pins above; green on current code (with `classifyRunOutcome` initially a thin port of the `:596-603` branch).
2. **Step B (Add shared infrastructure or seams):** `run-outcome.ts` with `RunSignals`, `collectRunSignals(line, signals)`, `classifyRunOutcome(input)`; `ChildExit.signals`; `ProgressTextState.signals`.
3. **Step C (Refactor / Implement):** extend `formatProgressLine` for the four policy-line prefixes; render `Summary` counts; implement the outcome table (requirements 1–4); header text; action buttons.
4. **Step D (Verify compile + tests):** `npm test`; manual run with the real CLI on `samples/DataGuard.Sample`: expect status `warning` + the syntactic-only notification; with `DATAGUARD_CONNECTION_STRING` pointing at an unreachable host and a stored credential: expect `error` + `could not evaluate: contract acquisition failed: ...` (reproduced in this session: exit 3).

### 3. Tests After (Step 3.V — New Behavior Coverage)
- [ ] `formatProgressLine("Warning: no ground truth ...")` → `[WARN] ...`; `UNEVALUATED: ...` / `ACQUISITION: ...` / `Rule DG012 not evaluated: ...` → `[WARN] ...`; secrets redacted.
- [ ] `Summary` event with `UnevaluatedContractCount: 2, UnavailableRuleCount: 1` renders both counts; missing counts render as before.
- [ ] `collectRunSignals` sets `groundTruthMissing` on the Warning line and on `UNEVALUATED: no ground truth`; sets `firstDiagnosticLine` to the first `UNEVALUATED:/ACQUISITION:/Error:` line.
- [ ] `classifyRunOutcome`: exit 1 without artifact → `error` with the first diagnostic line; exit 3 → `error` "could not evaluate"; exit 4 → output-path message; exit 2 → arguments/config message; exit 0/1 with `groundTruthMissing` → `warning` + two actions; cancelled/timed-out inputs unchanged.
- [ ] Header line test: `scan` → "Scan started for <ws>".

### 4. Regression Gate (Mandatory Pass Command)
- Lệnh compile / type-check bắt buộc pass: `npm run compile` (trong `src/DataGuard.VSCode`)
- Lệnh chạy test bắt buộc pass: `npm test` (trong `src/DataGuard.VSCode`)

## Todo List
- [ ] [3.T] Pin `formatProgressLine`/redaction/outcome hiện tại trong `ide-safe-contract.test.ts` + `run-outcome.test.ts`
- [ ] [3.I] Tạo `run-outcome.ts` (signals + classify), mở rộng `formatProgressLine` và `Summary`, nối vào `extension.ts` (status, notification, action buttons, header)
- [ ] [3.V] Test hành vi mới (policy lines, counts, bảng outcome, header), cập nhật README/CHANGELOG/USAGE; vượt Regression Gate

## Success Criteria
- [ ] Mọi regression test (Tests Before) và test mới (Tests After) đều PASS.
- [ ] Lệnh tại Regression Gate trả về exit code 0.
- [ ] Chạy `validate` trên `samples/DataGuard.Sample` không snapshot: status bar `warning`, notification "syntactic rules only" với 2 nút hành động; output channel có dòng `[WARN] Warning: no ground truth ...`.
- [ ] `baseline` không nguồn (exit 1, không artifact) hiển thị lỗi, không còn "found findings".

## Risk Assessment & Security Considerations
- **Rủi ro:** echo thêm dòng stderr có thể lộ chuỗi kết nối trong thông điệp lỗi của driver → **Giảm thiểu:** mọi dòng đi qua `redactSensitiveText` (đã có test red-team `redaction-redteam.test.ts`); thêm case cho thông điệp `contract acquisition failed: ... Server=...`.
- **Rủi ro:** phân loại exit 1 theo "artifact có mặt" sai khi CLI ghi SARIF rồi ném exception → **Giảm thiểu:** CLI ghi SARIF ở cuối pipeline (`ValidateCommand.cs:417`), test khóa thứ tự; chấp nhận "findings" nếu SARIF hợp lệ.
- **Bảo mật:** nút **Refresh Snapshot** vẫn đi qua modal live-DB (`confirmLiveDatabaseCommand`), không bypass.
