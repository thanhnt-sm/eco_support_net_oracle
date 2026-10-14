---
phase: 3
title: "Language server lifecycle, ranges and DG017 de-duplication"
status: pending
priority: P2
effort: "2.5h"
dependencies: []
---

# Phase 3: Language server lifecycle, ranges and DG017 de-duplication

## Overview
Make `src/DataGuard.LanguageServer` a well-behaved JSON-RPC peer (answers `shutdown`, exits on `exit`, returns `MethodNotFound` for any other request), make its diagnostics cover the whole SQL literal instead of the first keyword, and stop SELECT * from appearing twice in Problems (LSP DG017 + CLI DG017).

## Key Insights
- Reproduced with a raw JSON-RPC probe (see `research/lsp-probe.md`): `initialize` answered; `textDocument/hover` (id 2) **never answered**; `shutdown` (id 3) **never answered**; after `exit` the process stayed alive until killed. `vscode-languageclient` `stop()` sends `shutdown` then `exit`; without a response it waits its timeout, then kills, every time the extension deactivates or restarts.
- The main loop only branches on `initialize`, `didOpen/didChange`, `didClose` (`src/DataGuard.LanguageServer/Program.cs:12-57`); every other message is silently dropped, requests included (JSON-RPC requires a response for any message with `id`).
- Diagnostic range = `[token.Start, token.Start + token.Length)` of the statement keyword (`src/DataGuard.SqlClassification/SqlClassifier.cs:200-207`; `Program.cs:66-72`), i.e. 6 characters for `SELECT`. The classification already knows the literal (`CSharpStringLiteralScanner` gives `literal.Content` and its offset, `SqlClassifier.cs:115`).
- DG017 is emitted by the LSP (`Program.cs:69`, `code = "DG017"`) and by the CLI SARIF (`src/DataGuard.Core/Rules/SelectStarRule`… reported as DG017 in `validation.sarif`, reproduced) and both are published with `source: "DataGuard"` (`Program.cs:70`, `extension.ts:798`) into two different collections → two identical rows in Problems after a validate run.
- Every SQL literal gets `DGSQL001` at severity Information (`Program.cs:68-71`): on a repository with hundreds of queries this is a permanent wall of blue squiggles with no action attached.
- There is no test project for the language server; `tests/DataGuard.Core.Tests` already runs the CLI as a child process (`CliProcessTestRunner.cs`), the same pattern works for the server. [UNVERIFIED] whether `DataGuard.Core.Tests` may add a `ProjectReference` to an `Exe` project without affecting `DataGuard.CrossPlatform.slnf` packaging — the filter already builds the server? → validate Q.

## Requirements
- **Functional:**
  1. `shutdown` → `{"result": null}`; `exit` → process exits 0 (1 if `shutdown` was not received, per spec); any other request with `id` → error `-32601 MethodNotFound`; notifications other than the handled ones are ignored; `$/cancelRequest` ignored.
  2. Range of a diagnostic = the whole string literal content span (start of SQL text to end), not the keyword.
  3. `DGSQL001` severity → Hint (4) so it stays in hover/underline but not in the Problems count; DG017 remains Warning.
  4. Host-side de-duplication: when CLI SARIF findings are loaded, a CLI finding with the same `ruleId` and the same `(file, startLine)` as an LSP diagnostic is still shown (it carries the authoritative CLI message), and the extension **filters LSP diagnostics** through `clientOptions.middleware.handleDiagnostics` to drop LSP DG017 on a line already covered by a CLI DG017 for that file. Cleared when `clearFindingsAndDiagnostics` runs.
- **Non-functional:** server stays dependency-free (System.Text.Json only); message loop still bounded (`MaximumMessageBytes`); no behaviour change for `didOpen/didChange/didClose` timing (150 ms debounce).

## Architecture
```
stdin ──► ReadMessageAsync ──► dispatch:
   initialize           → result capabilities (unchanged)
   initialized          → ignore
   shutdown  (id)       → result null; shutdownRequested = true
   exit                 → return exit code (shutdownRequested ? 0 : 1)
   textDocument/didOpen|didChange|didClose → unchanged
   any other with id    → error -32601
   any other notification → ignore
publishDiagnostics: range from SqlClassification.LiteralStart/LiteralLength (new fields) instead of keyword
extension.ts: middleware.handleDiagnostics(uri, diags, next) → next(uri, dedupeAgainstCli(uri, diags))
```

## Related Code Files
- **Modify:** `src/DataGuard.LanguageServer/Program.cs`
- **Modify:** `src/DataGuard.SqlClassification/SqlClassifier.cs` (`SqlClassification` gains `LiteralStart`/`LiteralLength`; `ClassifySql` receives the literal span), `CSharpStringLiteralScanner.cs` only if the literal span is not already exposed
- **Create:** `tests/DataGuard.Core.Tests/LanguageServerProtocolTests.cs` (spawns `dotnet DataGuard.LanguageServer.dll`, speaks JSON-RPC over stdio; reuse the framing helper pattern from `CliProcessTestRunner.cs`)
- **Modify:** `tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj` (`ProjectReference` to `DataGuard.LanguageServer` so the dll is built and locatable)
- **Modify:** `src/DataGuard.VSCode/src/extension.ts` (`startLanguageServer` middleware; `loadDiagnostics` records `(file,line,ruleId)` keys), new pure helper `src/DataGuard.VSCode/src/ui/diagnostic-dedupe.ts` + test
- **Modify:** `src/DataGuard.VSCode/test/extension-host.cjs` (assert the DGSQL001 range spans the literal), `src/DataGuard.VSCode/CHANGELOG.md`

## TDD Structure (`--tdd` Mandatory)

### 1. Tests Before (Step 3.T — Regression Coverage First)
- File test cần tạo/cập nhật trước khi sửa code: `tests/DataGuard.Core.Tests/LanguageServerProtocolTests.cs`, `tests/DataGuard.Core.Tests/SqlClassifierTests.cs` (create if absent; check `Grep "SqlClassifier" tests/` first)
- Các kịch bản hành vi hiện tại cần khóa lại (pin current behavior):
  - [ ] `initialize` returns `capabilities.textDocumentSync == 1`.
  - [ ] `didOpen` of `var q = "SELECT CustomerId FROM Customers";` publishes one `DGSQL001` with `source == "DataGuard"`; `SELECT * FROM Orders` publishes `DG017` severity 2.
  - [ ] `didChange` supersedes the pending publication (only the latest version's diagnostics arrive); `didClose` publishes an empty list.
  - [ ] Oversized `Content-Length` (> 1 MiB) terminates the server with a non-zero exit (current guard, `Program.cs:146-150`).
  - [ ] `SqlClassifier.ClassifyCSharp` current `Start/Length` values for the probe text (pin so the new fields are additive).

### 2. Refactor & Implementation (Step 3.I — Protected Code Changes)
1. **Step A (Write tests for current behavior):** add the process-based tests and the classifier pins; green on current code (the `shutdown` test is written as "no response within 500 ms" to document RC4, flipped in 3.V).
2. **Step B (Add shared infrastructure or seams):** `LiteralStart/LiteralLength` on `SqlClassification`; a `Dispatch(JsonElement root)` method in `Program.cs` returning `(response?, exitCode?)` so the loop is testable in-process as well.
3. **Step C (Refactor / Implement):** lifecycle handling; MethodNotFound; range from literal span; DGSQL001 → Hint; `extension.ts` middleware + `diagnostic-dedupe.ts`.
4. **Step D (Verify compile + tests):** `dotnet build DataGuard.CrossPlatform.slnf -c Release`; `dotnet test ... --filter FullyQualifiedName~LanguageServerProtocolTests`; `npm test`; rerun the raw probe: `shutdown` answered, `exit` ends the process with code 0.

### 3. Tests After (Step 3.V — New Behavior Coverage)
- [ ] `shutdown` → response with `result: null` within 500 ms; `exit` → process exit code 0; `exit` without prior `shutdown` → exit code 1.
- [ ] `textDocument/hover` → error `-32601`; `$/cancelRequest` and `workspace/didChangeConfiguration` → no response, server keeps serving.
- [ ] Diagnostic range for `"SELECT CustomerId FROM Customers"` spans the full literal content; multi-line raw literal (`"""..."""`) spans multiple lines.
- [ ] `DGSQL001` severity == 4; `DG017` severity == 2.
- [ ] `dedupeAgainstCli`: LSP DG017 on `(file, line)` present in the CLI set is dropped; DGSQL001 is kept; after `clear`, nothing is dropped.

### 4. Regression Gate (Mandatory Pass Command)
- Lệnh compile / type-check bắt buộc pass: `dotnet build DataGuard.CrossPlatform.slnf --configuration Release` và `npm run compile`
- Lệnh chạy test bắt buộc pass: `dotnet test tests/DataGuard.Core.Tests --configuration Release --no-build --filter "FullyQualifiedName~LanguageServerProtocolTests|FullyQualifiedName~SqlClassifier"` và `npm test`

## Todo List
- [ ] [3.T] Test process-based cho LSP (initialize/didOpen/didChange/didClose/oversize) + pin `SqlClassifier` offsets
- [ ] [3.I] Lifecycle (`shutdown`/`exit`/MethodNotFound), range theo literal, DGSQL001=Hint, middleware dedupe trong `extension.ts`
- [ ] [3.V] Test hành vi mới (lifecycle, range, severity, dedupe), cập nhật `extension-host.cjs` + CHANGELOG; vượt Regression Gate

## Success Criteria
- [ ] Mọi regression test (Tests Before) và test mới (Tests After) đều PASS.
- [ ] Lệnh tại Regression Gate trả về exit code 0.
- [ ] Probe thủ công (`research/lsp-probe.md`) cho thấy `shutdown` được trả lời và `exit` kết thúc tiến trình.
- [ ] Sau `Run Validation` trên sample, Problems chỉ có **một** dòng DG017 cho `SELECT * FROM ORDERS`.

## Risk Assessment & Security Considerations
- **Rủi ro:** test spawn tiến trình flaky trên CI chậm → **Giảm thiểu:** timeout 10 s/test, đọc theo framing `Content-Length` (không sleep), cùng pattern `CliProcessTestRunner`.
- **Rủi ro:** đổi range làm `extension-host.cjs` (chạy tay với VS Code thật) lệch → **Giảm thiểu:** cập nhật assertion trong cùng phase.
- **Bảo mật:** server vẫn không đọc file hệ thống, chỉ text do client gửi; giới hạn 1 MiB/8 KiB header giữ nguyên; MethodNotFound không echo payload.
