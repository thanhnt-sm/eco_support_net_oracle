---
phase: 2
title: "VS reports ground-truth and run state truthfully"
status: pending
priority: P1
effort: "2.5h (+ Windows CI run)"
dependencies: []
---

# Phase 2: VS reports ground-truth and run state truthfully

<!-- Updated: Red Team Session 2 - syntactic-only text gated on HasSummary && sarifExists; UnevaluatedContractCount gets its own text; ground-truth kind rendered from the Summary (Phase 3 emits it); Redact gaps; CliRunOutcomes helper; TryFormatProgress overload -->

## Overview
A VS developer must see, in the Output pane and the status bar, whether the database contract was actually compared. Today a `validate` that loaded no ground truth ends with `[OK] No issues found.` and `0 errors, 0 warnings`; unevaluated contracts and unavailable rules are invisible; validate exit 4 is "Unexpected".

## Key Insights
- CLI signals on stderr: `Warning: no ground truth (snapshot, connection, manual assembly or EF model) was loaded; only syntactic rules ran` (`src/DataGuard.Cli/Commands/ValidateCommand.cs:346`, printed **before** rules run and SARIF is written, `:408-420`), `UNEVALUATED: …` (`:309,341,398,480`), `ACQUISITION: <path>: <message>` (`:469`), `Rule <id> not evaluated: …` (`:364`), and the `Summary` event with `ErrorCount, WarningCount, ViolationCount, UnavailableRuleCount, UnevaluatedContractCount, AcquisitionDiagnosticCount` (`:494-499`). `UnevaluatedContractCount` counts contracts no rule could evaluate **after** ground truth loaded (`:443-447`) — it is not "no ground truth". `assess` Summary carries `CriticalCount/ToolErrorCount` (`AssessCommand.cs:171-179`). Phase 3 adds `GroundTruth` (`none|entities|schema|schema+entities`) to the validate Summary so VS can say what was compared.
- VS: `ProgressLineParser.TryFormatProgress` Summary branch reads only three counts (`src/DataGuard.VisualStudio/ProgressLineParser.cs:155-168`); non-JSON lines are echoed as `[DataGuard CLI] <Redact(text)>` (`:57-63`); `ProgressStreamReader.ProcessLine` copies counts only from `IsSummary` (`ProgressStreamReader.cs:89-95`); aggregate `ProgressReadResult` (`ProgressReadModels.cs:40-62`); `ParsedProgress` is a readonly struct built only in `ProgressLineParser.cs:48,54,62,65`; `TryFormatProgress` has 32 test references → add an overload, keep existing signatures.
- `ExitCodeExplainer.Explain(string command, CliRunOutcome outcome, bool sarifExists)` (`ExitCodeExplainer.cs:18`): order is summary-without-SARIF (`:27`) → warnings (`:32`) → `[OK]` (`:37-40`) → per command (`:47`); `ExplainValidate` cases 1/2/3 (`:51-65`, exit 1 without summary = "failed before producing a validation summary"); `ExplainAssess(4)` = tool errors (`:76-77`, CLI `AssessCommand.cs:167-168,190`) must stay; validate exit 4 = refused output path (`src/DataGuard.Cli/Services/OutputSinks.cs:12-20`). Single production caller `DataGuardPackage.Publishing.cs:117` (Explain runs when `HasSummary || exit != 0 || TerminatedAtTimeout`, `:115`); status text `:121-123`; result block `OutputPaneWriter.cs:94-105`. Tests build outcomes with `CliRunOutcomes.Create` (`tests/DataGuard.VisualStudio.Tests/CliRunOutcomes.cs:8-15`), explainer cases in `CliArgumentBuilderTests.cs:74-102`.
- stdout is drained and discarded (`ProcessTerminator.cs:137-151`, called at `CliRunSession.cs:68`) → only stderr lines are signals; the Warning line always precedes the Summary (same `Console.Error` writer). A Warning without a later Summary means the CLI crashed afterwards (`:507-515`, exit 1) → the syntactic-only text must never replace "failed before producing a validation summary".
- Line protocol hardening: VS treats only brace-wrapped valid JSON as events; the `GroundTruthMissing` flag must come from the exact line prefix (`Warning: no ground truth` / `UNEVALUATED: no ground truth`), never from a substring (spoof pattern in `ProgressStreamReaderTests.cs:18`).
- `DataGuardLogger.Redact` (`DataGuardLogger.cs:28`) masks `password|pwd|passwd|secret|token… = value` up to the first space/`;`/`,`; gaps: quoted/braced values (`Password="a b;c"`, `{p;w}`), URI `scheme://user:pw@host`, Oracle EZConnect `user/pw@host`. `User Id` is deliberately kept (pinned `CliArgumentBuilderTests.cs:157`). Driver exception text reaches the new `[WARN]` path via `UNEVALUATED: contract acquisition failed: {ex.Message}`.
- The only pre-run hint exists when `.dataguard.yml` is absent (`DataGuardPackage.Commands.cs:119-122`); with a yml but no snapshot there is none — the CLI line is the source of truth; no yml parsing in VS (KISS).

## Requirements
- **Functional:**
  1. `ProgressReadResult` gains `UnevaluatedContractCount`, `UnavailableRuleCount`, `AcquisitionDiagnosticCount` (nullable ints from `Summary`), `GroundTruth` (string from `Summary.Data.GroundTruth`, null when absent) and `GroundTruthMissing` (bool from the exact raw-line prefix).
  2. Policy lines (`Warning:`, `UNEVALUATED:`, `ACQUISITION:`, `Rule <id> not evaluated:` at line start) render as `[DataGuard] [WARN] <Redact(text)>`; other raw lines unchanged.
  3. `Explain` (no signature change), inserted **after** the `:27` summary-without-SARIF check and before `:32`: when `HasSummary && sarifExists && GroundTruthMissing` → exit 0: `[WARN] Ran syntactic rules only: no snapshot, EF model or database connection was loaded, so the database contract was NOT compared. Commit .dataguard-snapshot.json (Tools > DataGuard > Refresh Snapshot) or keep the EF ModelSnapshot.cs in the solution. See Error List for syntactic findings.`; exit 1: existing "Validation found errors" + ` Ground truth was not loaded (syntactic only).` When `UnevaluatedContractCount > 0` (any exit with summary): append `[WARN] N contract(s) could not be evaluated against the loaded ground truth (see UNEVALUATED lines).` `ExplainValidate(4)` → `[ERROR] The CLI refused to write results to the output path (see [DataGuard CLI] lines above).`; `ExplainAssess(4)` unchanged.
  4. Summary line: `… N errors, N warnings; ground truth: <kind>; unevaluated N; unavailable rules N` (segments only when present). Status bar: `DataGuard: syntactic only (N errors, N warnings)` when `GroundTruthMissing && HasSummary`, else unchanged. Result block mirrors the ground-truth kind line.
  5. `DataGuardLogger.Redact`: add quoted/braced value forms, `scheme://user:pw@host` → `scheme://[REDACTED]@host`, EZConnect `user/pw@` → `[REDACTED]@`; `User Id` stays visible (existing pin).
- **Non-functional:** `Explain` signature and `TryFormatProgress` existing overloads untouched (14 + 32 callers); every echoed line redacted; no yml parsing.

## Architecture
```
stderr line ─► ProgressStreamReader.ProcessLine
                 ├─ exact prefix "Warning: no ground truth" | "UNEVALUATED: no ground truth" → result.GroundTruthMissing
                 ├─ ProgressLineParser.FormatProgressLine
                 │     ├─ JSON Summary → counts (+3) + GroundTruth kind; formatted "[DataGuard] ✔ …; ground truth: entities; unevaluated N; unavailable rules N"
                 │     └─ policy prefixes → "[DataGuard] [WARN] <Redact>"
                 └─ ParsedProgress (+fields, new ctor overload) → ProgressReadResult
Publishing.ReportOutcomeAsync ─► ExitCodeExplainer.Explain(command, outcome, sarifExists) (new branch after :27) ─► status text (:121-123)
```

## Related Code Files
- **Modify:** `src/DataGuard.VisualStudio/ProgressReadModels.cs`, `ProgressLineParser.cs` (Summary branch; policy-prefix branch after `Redact`; new `TryFormatProgress` overload), `ProgressStreamReader.cs`, `ExitCodeExplainer.cs`, `DataGuardPackage.Publishing.cs:115-123`, `OutputPaneWriter.cs:94-105`, `DataGuardLogger.cs:25-28`
- **Modify:** `tests/DataGuard.VisualStudio.Tests/ProgressStreamReaderTests.cs`, `CliArgumentBuilderTests.cs` (explainer + Redact cases), `CliRunOutcomes.cs` (helper gains the new fields), `DataGuardPackageTests.cs` (status text if covered), `src/DataGuard.VisualStudio/overview.md:10`, `docs/USAGE.md` §Visual Studio, `CHANGELOG.md`

## TDD Structure (`--tdd` Mandatory)

### 1. Tests Before (Step 3.T — Regression Coverage First)
- File test cần tạo/cập nhật trước khi sửa code: `tests/DataGuard.VisualStudio.Tests/ProgressStreamReaderTests.cs`, `tests/DataGuard.VisualStudio.Tests/CliArgumentBuilderTests.cs`
- Các kịch bản hành vi hiện tại cần khóa lại (pin current behavior):
  - [ ] `Summary` with only `ErrorCount/WarningCount` → `HasSummary`, counts, formatted `… : 2 errors, 1 warnings`; new fields null/false.
  - [ ] `Warning: no ground truth …` line today → echoed as `[DataGuard CLI] Warning: …` (flipped to `[WARN]` in 3.V).
  - [ ] `Explain("validate", 0, HasSummary, 0 warnings, sarif)` → `[OK] No issues found.` (stays true without the flag).
  - [ ] `Explain("validate", 1, no summary)` → "failed before producing a validation summary" (must survive the new branch).
  - [ ] `Explain("assess", 4, …)` → tool-errors text (must not change); `Explain("validate", 4, …)` → `Unexpected exit code 4.` (flipped in 3.V).
  - [ ] `Redact("Server=x;User Id=sa;Password=y;…")` → `User Id=sa;Password=[REDACTED]` (existing pin at `:157`, kept).
  - [ ] `PublishGate.Decide` and `IdeSafeAcknowledged` first-line logic unchanged.

### 2. Refactor & Implementation (Step 3.I — Protected Code Changes)
1. **Step A (Write tests for current behavior):** pins; green on Windows CI (Linux compile).
2. **Step B (Add shared infrastructure or seams):** new fields on `ParsedProgress`/`ProgressReadResult`; `ProgressLineParser.IsPolicyLine(text)`; `ExitCodeExplainer.DescribeGroundTruth(ProgressReadResult)`; `CliRunOutcomes.Create` overload.
3. **Step C (Refactor / Implement):** parser/reader/explainer/status/result block; Redact forms; docs.
4. **Step D (Verify compile + tests):** Linux compile parity; Windows CI tests. Manual (Windows): Run Validation on a solution with `.dataguard.yml` but no snapshot → `[WARN] Ran syntactic rules only …`, status `DataGuard: syntactic only (…)`; with a snapshot + a procedure missing from it → the unevaluated text, not the syntactic text.

### 3. Tests After (Step 3.V — New Behavior Coverage)
- [ ] Summary with the three counts + `GroundTruth: "entities"` → fields set; formatted segments present; absent fields → no segment.
- [ ] Raw lines: `Warning: no ground truth …` → flag + `[WARN]`; `UNEVALUATED: 3 contract(s)…` / `ACQUISITION: x.cs: y` / `Rule DG012 not evaluated: z` → `[WARN]`; `  DG020 id: reason` → `[DataGuard CLI]` unchanged; spoof `[DG1290] C:\x\Warning: no ground truth.csproj` → flag not set.
- [ ] `Explain("validate", 0, summary, sarif, GroundTruthMissing)` → syntactic-only text; `(1, summary, sarif, flag)` → "found errors … (syntactic only)"; `(1, no summary, flag)` → "failed before producing a validation summary"; `(0, summary, Unevaluated 2)` → unevaluated text, **not** syntactic; `("validate", 4)` → refused-output text; `("assess", 4)` unchanged.
- [ ] Status text: `DataGuard: syntactic only (0 errors, 0 warnings)` only with summary + flag.
- [ ] Redact: `Password="a b;c"`, `Password={p;w}`, `postgresql://u:p@h/db`, `scott/tiger@orcl` all masked; `User Id=sa` kept.

### 4. Regression Gate (Mandatory Pass Command)
- Lệnh compile / type-check bắt buộc pass: `dotnet build tests/DataGuard.VisualStudio.Tests/DataGuard.VisualStudio.Tests.csproj -c Release -p:VSSDKBuildToolsAutoSetup=false` (Linux); `dotnet build DataGuard.sln -c Release` (Windows)
- Lệnh chạy test bắt buộc pass: `dotnet test tests/DataGuard.VisualStudio.Tests/DataGuard.VisualStudio.Tests.csproj -c Release` (Windows CI job `Visual Studio Build and Test`)

## Todo List
- [ ] [3.T] Pin Summary/echo/Explain/Redact/PublishGate hiện tại
- [ ] [3.I] Mở rộng `ParsedProgress`/`ProgressReadResult` (+ overload), parser `[WARN]` + counts + ground-truth kind, reader raw flag, `Explain` branch sau `:27` + validate exit 4, status/result block, Redact forms, `CliRunOutcomes`, docs
- [ ] [3.V] Test counts, `[WARN]`, spoof, Explain (syntactic vs unevaluated vs crash), status, Redact; Windows CI xanh; vượt Regression Gate

## Success Criteria
- [ ] Mọi regression test (Tests Before) và test mới (Tests After) đều PASS (Windows CI).
- [ ] Lệnh tại Regression Gate trả về exit code 0.
- [ ] Output pane không bao giờ in `[OK] No issues found.` cho lần chạy có `GroundTruthMissing`; một lần chạy crash sau dòng Warning vẫn báo "failed before producing a validation summary".
- [ ] Error List giữ nguyên hành vi (không đổi `SarifErrorListPublisher`).

## Risk Assessment & Security Considerations
- **Rủi ro:** dòng giả từ tên file/khóa yml chứa ký tự điều khiển → **Giảm thiểu:** flag chỉ từ prefix đúng ở đầu dòng; JSON Summary phải parse hợp lệ; Phase 3 escape các đường dẫn/khóa CLI in ra.
- **Rủi ro:** thông điệp driver chứa credential → **Giảm thiểu:** mọi echo qua `Redact` đã mở rộng (Req 5) với test từng dạng.
- **Bảo mật:** `PublishGate` không đổi (handshake ide-safe vẫn bắt buộc cho validate/assess).
