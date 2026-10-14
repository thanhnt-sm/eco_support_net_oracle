---
phase: 5
title: "Visual Studio parity: summary counts, dead Init command, analyzer packaging"
status: pending
priority: P2
effort: "2h (+ Windows CI verification)"
dependencies: [1]
---

# Phase 5: Visual Studio parity: summary counts, dead Init command, analyzer packaging

## Overview
Bring `src/DataGuard.VisualStudio` to the same truthfulness as Phase 2 (never print `[OK] No issues found.` for a run that evaluated nothing), remove or wire the dead **Initialize DataGuard** menu item, and make the VSIX analyzer packaging either complete or explicitly gated. All C# here compiles on Linux (`DataGuard.sln` evaluation parity step in CI), but tests run only on `windows-latest`.

## Key Insights
- `ProgressLineParser.cs:155-168` reads only `ErrorCount/WarningCount/CriticalCount` from the `Summary` event; `ExitCodeExplainer.cs:37-40` prints `[OK] No issues found.` for exit 0. The CLI emits `UnavailableRuleCount`, `UnevaluatedContractCount`, `AcquisitionDiagnosticCount` (`ValidateCommand.cs:494-499`) and the `Warning: no ground truth` line (`:346`), which VS only echoes as a `[DataGuard CLI]` line.
- `InitCommand` 0x0106 is declared (`Commands/Menus.vsct:66-72, 87`) but no handler is registered (`DataGuardPackage.cs:84-92`); `UI/InfoBarManager.cs` (which would execute it) is never instantiated by the package (only by `InfoBarManagerTests.cs`).
- VSIX analyzer content is only `DataGuard.Analyzers.dll` and `DataGuard.CodeFixes.dll` (`DataGuard.VisualStudio.csproj:103-110`, `source.extension.vsixmanifest:30-35`), while the analyzer references `DataGuard.Contracts` and `DataGuard.SqlClassification` (`src/DataGuard.Analyzers/ContractValidationAnalyzer.cs:12`, `SqlCallSyntax.cs:11`) and `Microsoft.CodeAnalysis.CSharp 5.9.0` (`DataGuard.Analyzers.csproj:24`). `scripts/assert-vsix.ps1:22-29` does not check for the two dependency DLLs. `VsixAnalyzerPackagingTests.cs:47-51` returns early (silent pass) when no VSIX exists. [UNVERIFIED on Windows] whether VS 2022 (Roslyn 4.x) loads an analyzer built against 5.9.0; the manifest targets `[17.0,19.0)` (`vsixmanifest:11-19`).
- VS has no credential path (`CliArgumentBuilder.cs:117-135` sets only `DOTNET_ROLL_FORWARD`), so after Phase 4 its only IDE ground-truth sources are a committed snapshot or the EF source parse — which it gets for free through the CLI.

## Requirements
- **Functional:**
  1. `Summary` rendering: `… : N errors, N warnings; unevaluated N; unavailable rules N` when the counts are present; the `Warning: no ground truth` / `UNEVALUATED:` / `ACQUISITION:` lines render as `[WARN]` in the Output pane; exit 0 with `unevaluated > 0` or ground-truth warning → `[WARN] Ran syntactic rules only …` instead of `[OK] No issues found.`; status text in the Output pane mirrors it.
  2. `InitCommand`: **remove** the vsct button and id symbol (decision Q4; wiring it would need an `init` runner, an InfoBar and consent handling — out of scope for this plan).
  3. VSIX analyzer packaging: add `DataGuard.Contracts.dll` and `DataGuard.SqlClassification.dll` as `VSIXSourceItem` next to the analyzer and declare them as `Microsoft.VisualStudio.Analyzer` assets; `assert-vsix.ps1` and `VsixAnalyzerPackagingTests` check all four; `VsixAnalyzerPackagingTests` **skips explicitly** (not silent pass) when no VSIX exists.
- **Non-functional:** no new NuGet reference in the VSIX project; vsct change must keep `Menus.ctmenu` resource compiling (CI "Visual Studio VSIX Packaging Gate").

## Architecture
```
stderr Summary event ──► ProgressLineParser (counts: +Unevaluated, +Unavailable, +Acquisition)
                     ──► ProgressReadModels.RunSummary (new fields)
exit code + RunSummary ──► ExitCodeExplainer.Explain(exitCode, summary)  ──► Output pane line
VSIX: IncludeAnalyzersInVsix += Contracts.dll, SqlClassification.dll; vsixmanifest += 2 Analyzer assets
```

## Related Code Files
- **Modify:** `src/DataGuard.VisualStudio/ProgressLineParser.cs`, `ProgressReadModels.cs`, `ExitCodeExplainer.cs`, `DataGuardPackage.Publishing.cs` (pass summary to explainer)
- **Modify:** `src/DataGuard.VisualStudio/Commands/Menus.vsct` (remove `InitCommand` button + symbol), `src/DataGuard.VisualStudio/UI/InfoBarManager.cs` (delete; dead) and `tests/DataGuard.VisualStudio.Tests/InfoBarManagerTests.cs` (delete)
- **Modify:** `src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj:103-110`, `source.extension.vsixmanifest:30-35`, `scripts/assert-vsix.ps1`, `tests/DataGuard.VisualStudio.Tests/VsixAnalyzerPackagingTests.cs`
- **Modify:** `tests/DataGuard.VisualStudio.Tests/ProgressStreamReaderTests.cs`, `NavigationTests.cs` (Summary formatting cases), `CliArgumentBuilderTests.cs` (ExitCodeExplainer cases)
- **Modify:** `src/DataGuard.VisualStudio/overview.md` (stale "raw CLI output is not displayed"), `docs/USAGE.md` §Visual Studio, `CHANGELOG.md`

## TDD Structure (`--tdd` Mandatory)

### 1. Tests Before (Step 3.T — Regression Coverage First)
- File test cần tạo/cập nhật trước khi sửa code: `tests/DataGuard.VisualStudio.Tests/ProgressStreamReaderTests.cs`, `CliArgumentBuilderTests.cs`, `VsixAnalyzerPackagingTests.cs`
- Các kịch bản hành vi hiện tại cần khóa lại (pin current behavior):
  - [ ] `Summary` with only `ErrorCount/WarningCount` renders `… : N errors, N warnings` (unchanged when the new counts are absent).
  - [ ] `ExitCodeExplainer` texts for 1/2/3/130 unchanged; exit 0 **without** any unevaluated signal still `[OK] No issues found.`
  - [ ] `PublishGate.Decide` verdicts unchanged (`ProgressStreamReaderTests` existing cases).
  - [ ] `VsixAnalyzerPackagingTests` asserts the two analyzer DLLs + legal files when a VSIX exists (existing); add an explicit `Skip` reason when absent.

### 2. Refactor & Implementation (Step 3.I — Protected Code Changes)
1. **Step A (Write tests for current behavior):** pins above; green on Windows CI (cannot run here: document in the PR that the Windows job is the gate).
2. **Step B (Add shared infrastructure or seams):** `RunSummary` record with the three new nullable counts; `ExitCodeExplainer.Explain(int exitCode, RunSummary? summary)` overload (old signature delegates with `null`).
3. **Step C (Refactor / Implement):** parser + explainer + Output pane; vsct removal + `InfoBarManager` deletion; csproj/vsixmanifest/assert-vsix additions.
4. **Step D (Verify compile + tests):** Linux: `dotnet build DataGuard.sln -c Release` evaluation parity step from CI (`ci.yml` "Linux evaluation parity (Dependabot)"); Windows CI: `DataGuard.VisualStudio.Tests` + "Visual Studio VSIX Packaging Gate" green.

### 3. Tests After (Step 3.V — New Behavior Coverage)
- [ ] `Summary` with `UnevaluatedContractCount: 2, UnavailableRuleCount: 1` renders both; `Warning: no ground truth` line renders `[WARN]`.
- [ ] `Explain(0, summary{unevaluated:2})` → `[WARN] Ran syntactic rules only …`; `Explain(0, null)` → `[OK]` (compat).
- [ ] `Explain(4, …)` → output-path refusal text (closes D7).
- [ ] VSIX test: Contracts + SqlClassification DLLs present and declared as Analyzer assets; `assert-vsix.ps1` lists them.
- [ ] No `InitCommand` symbol in `Menus.vsct` (string test over the file, like `ExtensionVersion` tests do for the manifest).

### 4. Regression Gate (Mandatory Pass Command)
- Lệnh compile / type-check bắt buộc pass: `dotnet build DataGuard.sln --configuration Release` (Windows) ; trên Linux: `dotnet msbuild src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj -t:Restore -p:VSSDKBuildToolsAutoSetup=false` (bước parity trong CI)
- Lệnh chạy test bắt buộc pass: `dotnet test tests/DataGuard.VisualStudio.Tests --configuration Release` (Windows CI job `visual-studio`)

## Todo List
- [ ] [3.T] Pin Summary/Explainer/PublishGate/VSIX test hiện tại (+ Skip tường minh khi không có VSIX)
- [ ] [3.I] `RunSummary` + explainer theo summary; xoá `InitCommand`/`InfoBarManager`; thêm Contracts + SqlClassification vào VSIX + manifest + `assert-vsix.ps1`
- [ ] [3.V] Test hành vi mới (counts, `[WARN]` syntactic-only, exit 4, VSIX content, vsct không còn Init); cập nhật overview/USAGE/CHANGELOG; xanh trên Windows CI

## Success Criteria
- [ ] Mọi regression test (Tests Before) và test mới (Tests After) đều PASS trên Windows CI.
- [ ] Lệnh tại Regression Gate trả về exit code 0.
- [ ] Output pane của VS không còn `[OK] No issues found.` cho lần chạy chỉ có rule cú pháp.
- [ ] Menu Tools → DataGuard không còn mục chết "Initialize DataGuard".
- [ ] [UNVERIFIED → Windows] Analyzer trong VSIX nạp được (không AD0001/FileNotFound) trên VS 2022 17.x; nếu Roslyn 5.9 không nạp được trên 17.x, ghi nhận và mở quyết định owner (Q5).

## Risk Assessment & Security Considerations
- **Rủi ro:** không chạy được test VS trên Linux → **Giảm thiểu:** PR dựa vào job `windows-latest`; không merge khi job đỏ; thay đổi vsct tối thiểu.
- **Rủi ro:** thêm DLL vào VSIX làm tăng kích thước/được quét SBOM → **Giảm thiểu:** 2 DLL đã thuộc repo, SBOM job tự cập nhật; `assert-vsix.ps1` khóa danh sách.
- **Bảo mật:** không thêm đường credential cho VS trong plan này (giữ VS không bao giờ kết nối DB từ IDE, như USAGE §VS ghi); ground truth mới chỉ từ snapshot commit hoặc source parse (Phase 4).
