---
phase: 2
title: "VS reports ground-truth and run state truthfully"
status: pending
priority: P1
effort: "2.5h (+ Windows CI run)"
dependencies: []
---

# Phase 2: VS reports ground-truth and run state truthfully

## Overview
A VS developer must see, in the Output pane and the status bar, whether the database contract was actually compared. Today a `validate` that loaded no ground truth ends with `[OK] No issues found.` and `0 errors, 0 warnings`; unevaluated contracts and unavailable rules are invisible; validate exit 4 is "Unexpected".

## Key Insights
- Signals the CLI already emits on stderr: `Warning: no ground truth (snapshot, connection, manual assembly or EF model) was loaded; only syntactic rules ran` (`src/DataGuard.Cli/Commands/ValidateCommand.cs:346`, under `--ide-safe` instead of exit 3, `:129,339-344`); `UNEVALUATED: …` (`:309,341,398,480`), `ACQUISITION: <path>: <message>` (`:469`), `Rule <id> not evaluated: …` (`:364`); `Summary` event `Data` with `ErrorCount, WarningCount, ViolationCount, UnavailableRuleCount, UnevaluatedContractCount, AcquisitionDiagnosticCount` (`:494-499`). `assess`'s `Summary` carries `CriticalCount/ToolErrorCount` instead (`AssessCommand.cs:171-179`).
- VS side: `ProgressLineParser.TryFormatProgress` `Summary` branch reads only `ErrorCount/WarningCount/CriticalCount` (`src/DataGuard.VisualStudio/ProgressLineParser.cs:155-168`); non-JSON lines are echoed as `[DataGuard CLI] <redacted>` (`:62-66`); `ProgressStreamReader.ProcessLine` sets counts only from `IsSummary` (`ProgressStreamReader.cs:89-95`); the aggregate is `ProgressReadResult` (`ProgressReadModels.cs:40-62`: `ErrorCount, WarningCount, HasSummary, IdeSafeUnsupported, IdeSafeAcknowledged, SawAnyProgressEvent`).
- `ExitCodeExplainer.Explain(string command, CliRunOutcome outcome, bool sarifExists)` (`ExitCodeExplainer.cs:18`) returns `[OK] No issues found.` for exit 0 (`:37-40`); `ExplainValidate` has cases 1/2/3 only (`:51-65`); `ExplainAssess(4)` = tool errors and must stay (`:76-77`; CLI `AssessCommand.cs:167-168,190`). Validate exit 4 = refused output path (`src/DataGuard.Cli/Services/OutputSinks.cs:11-20`).
- Single production call site: `DataGuardPackage.Publishing.cs:117`; status text at `:121-123` uses only the two counts; `OutputPaneWriter.WriteResultBlockAsync` (`OutputPaneWriter.cs:94-105`) likewise. Tests: `CliArgumentBuilderTests.cs:76-102` (explainer), `ProgressStreamReaderTests.cs` (parser/reader), `ProgressPumpTests.cs:19`, `DataGuardPackageTests.cs:56,369`, `RuleInventoryTests.cs:90` (Summary consumers).
- stdout is drained and discarded (`ProcessTerminator.cs:110-124`), so only stderr lines are signals: all four line kinds above are stderr. A repo-controlled path printed in `ACQUISITION:` could contain control characters; VS already treats only brace-wrapped JSON as events and redacts echoed text (`ProgressLineParser.cs:62-66`), so a forged `Summary` line is accepted only if it is valid JSON on its own line — the `GroundTruthMissing` flag must therefore be set from the exact `Warning:`/`UNEVALUATED: no ground truth` prefix, never from substrings.
- The only pre-run hint exists when `.dataguard.yml` is absent (`DataGuardPackage.Commands.cs:119-122`); with a yml and no snapshot there is none — the CLI line is the source of truth, no yml parsing in VS (KISS).

## Requirements
- **Functional:**
  1. `ProgressReadResult` gains `UnevaluatedContractCount`, `UnavailableRuleCount`, `AcquisitionDiagnosticCount` (nullable ints from `Summary`) and `GroundTruthMissing` (bool, from the raw `Warning: no ground truth` / `UNEVALUATED: no ground truth` line).
  2. Policy lines (`Warning:`, `UNEVALUATED:`, `ACQUISITION:`, `Rule <id> not evaluated:`) render as `[DataGuard] [WARN] <redacted>` (not `[DataGuard CLI]`), so they stand out and are logged.
  3. `Explain`: before `[OK]`, when exit is 0 or 1 and (`GroundTruthMissing` or `UnevaluatedContractCount > 0`) → `[WARN] Ran syntactic rules only: no snapshot, EF model or database connection was available, so the database contract was NOT compared. Commit .dataguard-snapshot.json (Tools > DataGuard > Refresh Snapshot once Phase 4 ships) or add the EF ModelSnapshot.cs to the solution. See Error List for syntactic findings.`; `ExplainValidate(4)` → `[ERROR] The CLI refused to write results to the output path (see [DataGuard CLI] lines above).`; `ExplainAssess(4)` unchanged.
  4. Summary line and status bar include the new counts: `… N errors, N warnings; unevaluated N; unavailable rules N` / `DataGuard: syntactic only (N errors, N warnings)` when `GroundTruthMissing`.
  5. `WriteResultBlockAsync` shows the same "syntactic only" line.
- **Non-functional:** no signature change to `Explain` (14 callers untouched); every echoed line stays redacted (`DataGuardLogger.Redact`); no yml parsing in the extension.

## Architecture
```
stderr line ─► ProgressStreamReader.ProcessLine
                 ├─ raw prefix check: "Warning: no ground truth" | "UNEVALUATED: no ground truth" → result.GroundTruthMissing = true
                 ├─ ProgressLineParser.FormatProgressLine
                 │     ├─ JSON Summary → counts (+3 new) ; formatted "[DataGuard] ✔ … ; unevaluated N; unavailable rules N"
                 │     └─ policy prefixes → "[DataGuard] [WARN] <redacted>"
                 └─ ParsedProgress (+3 nullable counts) → ProgressReadResult
DataGuardPackage.Publishing.ReportOutcomeAsync ─► ExitCodeExplainer.Explain(command, outcome, sarifExists) (new branch) ─► status text
```

## Related Code Files
- **Modify:** `src/DataGuard.VisualStudio/ProgressReadModels.cs` (`ParsedProgress`, `ProgressReadResult`), `ProgressLineParser.cs` (`Summary` branch; policy-prefix branch before the generic echo), `ProgressStreamReader.cs` (raw prefix flag; copy new counts), `ExitCodeExplainer.cs` (new branch + validate case 4), `DataGuardPackage.Publishing.cs:115-123` (status text), `OutputPaneWriter.cs:94-105`
- **Modify:** `tests/DataGuard.VisualStudio.Tests/ProgressStreamReaderTests.cs`, `CliArgumentBuilderTests.cs` (explainer cases), `DataGuardPackageTests.cs` (status text if covered), `src/DataGuard.VisualStudio/overview.md:10` (stale "raw output not displayed"), `docs/USAGE.md` §Visual Studio (new lines), `CHANGELOG.md`

## TDD Structure (`--tdd` Mandatory)

### 1. Tests Before (Step 3.T — Regression Coverage First)
- File test cần tạo/cập nhật trước khi sửa code: `tests/DataGuard.VisualStudio.Tests/ProgressStreamReaderTests.cs`, `tests/DataGuard.VisualStudio.Tests/CliArgumentBuilderTests.cs`
- Các kịch bản hành vi hiện tại cần khóa lại (pin current behavior):
  - [ ] `Summary` with only `ErrorCount/WarningCount` → `HasSummary`, counts, formatted `… : 2 errors, 1 warnings` (existing characterization kept); new counts `null`.
  - [ ] A `Warning: no ground truth …` line today → echoed as `[DataGuard CLI] Warning: …` and `GroundTruthMissing` does not exist (pin as "echoed once, redacted"); flipped in 3.V to `[WARN]`.
  - [ ] `Explain("validate", exit 0, HasSummary, 0 warnings, sarif)` → `[OK] No issues found.` (stays true when no ground-truth signal).
  - [ ] `Explain("assess", exit 4, …)` → `[WARN] Assessment completed with tool errors (check config or permissions).` (must not change).
  - [ ] `Explain("validate", exit 4, …)` → `Unexpected exit code 4.` (documents the gap; flipped in 3.V).
  - [ ] `PublishGate.Decide` verdicts and `IdeSafeAcknowledged` first-line logic unchanged (existing tests).

### 2. Refactor & Implementation (Step 3.I — Protected Code Changes)
1. **Step A (Write tests for current behavior):** add the pins; green on Windows CI (compile check on Linux).
2. **Step B (Add shared infrastructure or seams):** new properties on `ParsedProgress`/`ProgressReadResult`; `ProgressLineParser.IsPolicyLine(text)` helper; `ExitCodeExplainer.IsSyntacticOnly(ProgressReadResult)` helper.
3. **Step C (Refactor / Implement):** parser `Summary` counts and `[WARN]` rendering; reader raw flag; explainer branch + validate case 4; status text; result block; docs.
4. **Step D (Verify compile + tests):** Linux: `dotnet build tests/DataGuard.VisualStudio.Tests … -p:VSSDKBuildToolsAutoSetup=false`; Windows CI: `dotnet test tests/DataGuard.VisualStudio.Tests`. Manual (Windows): Run Validation on a solution with `.dataguard.yml` but no snapshot → Output pane shows `[WARN] Ran syntactic rules only …`, status `DataGuard: syntactic only (…)`.

### 3. Tests After (Step 3.V — New Behavior Coverage)
- [ ] `Summary` with `UnevaluatedContractCount: 2, UnavailableRuleCount: 1, AcquisitionDiagnosticCount: 0` → counts set; formatted line ends `; unevaluated 2; unavailable rules 1`.
- [ ] `Warning: no ground truth …` → `GroundTruthMissing == true`, echoed as `[DataGuard] [WARN] Warning: no ground truth …`; `UNEVALUATED: 3 contract(s) could not be evaluated:` and `ACQUISITION: x.cs: y` and `Rule DG012 not evaluated: z` → `[WARN]` echo; a line `  DG020 id: reason` (no prefix) → unchanged `[DataGuard CLI]` echo.
- [ ] `Warning: no ground truth` embedded mid-line (`[DG1290] C:\x\Warning: no ground truth.csproj`) → flag **not** set (spoof resistance, same pattern as `ProgressStreamReaderTests` `SpoofLine`).
- [ ] `Explain("validate", exit 0/1, GroundTruthMissing)` → `[WARN] Ran syntactic rules only …`; `Explain("validate", exit 0, UnevaluatedContractCount 2)` → same; `Explain("validate", exit 4)` → refused-output text; `Explain("assess", exit 4)` unchanged.
- [ ] Status text: `DataGuard: syntactic only (0 errors, 0 warnings)` when flagged; unchanged otherwise.
- [ ] Redaction: a `UNEVALUATED: contract acquisition failed: … Password=x;User Id=sa` line echoes with both values masked (extend `DataGuardLogger.Redact` test if `User Id` is not covered — check `DataGuardLogger.cs` first).

### 4. Regression Gate (Mandatory Pass Command)
- Lệnh compile / type-check bắt buộc pass: `dotnet build tests/DataGuard.VisualStudio.Tests/DataGuard.VisualStudio.Tests.csproj -c Release -p:VSSDKBuildToolsAutoSetup=false` (Linux) ; `dotnet build DataGuard.sln -c Release` (Windows)
- Lệnh chạy test bắt buộc pass: `dotnet test tests/DataGuard.VisualStudio.Tests/DataGuard.VisualStudio.Tests.csproj -c Release` (Windows CI job `Visual Studio Build and Test`)

## Todo List
- [ ] [3.T] Pin Summary/echo/Explain/PublishGate hiện tại trong `ProgressStreamReaderTests` + `CliArgumentBuilderTests`
- [ ] [3.I] Mở rộng `ParsedProgress`/`ProgressReadResult`, parser `[WARN]` + counts, reader raw flag, `Explain` branch + validate exit 4, status/result block, docs
- [ ] [3.V] Test counts, `[WARN]` echo, spoof, Explain mới, status text, redaction; xanh trên Windows CI; vượt Regression Gate

## Success Criteria
- [ ] Mọi regression test (Tests Before) và test mới (Tests After) đều PASS (Windows CI).
- [ ] Lệnh tại Regression Gate trả về exit code 0.
- [ ] Output pane không bao giờ in `[OK] No issues found.` cho lần chạy có `GroundTruthMissing` hoặc `UnevaluatedContractCount > 0`.
- [ ] Error List giữ nguyên hành vi (SARIF vẫn nạp; không đổi `SarifErrorListPublisher`).

## Risk Assessment & Security Considerations
- **Rủi ro:** tên file do repo kiểm soát chứa ký tự điều khiển được CLI in trong `ACQUISITION:` tạo dòng giả → **Giảm thiểu:** flag chỉ từ prefix đúng ở đầu dòng; JSON `Summary` chỉ được chấp nhận khi parse hợp lệ (đã vậy); Phase 3 escape control char phía CLI.
- **Rủi ro:** thông điệp driver trong `UNEVALUATED: contract acquisition failed: …` chứa credential → **Giảm thiểu:** mọi echo qua `DataGuardLogger.Redact`; thêm test `User Id`.
- **Bảo mật:** không đổi `PublishGate` (handshake ide-safe vẫn bắt buộc cho validate/assess).
