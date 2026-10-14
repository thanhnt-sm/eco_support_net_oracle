---
phase: 1
title: "Analyzer loads in VS 2022 and .NET 9 SDK (Roslyn 4.12 pin, complete VSIX)"
status: pending
priority: P1
effort: "3h (+ Windows CI run)"
dependencies: []
---

# Phase 1: Analyzer loads in VS 2022 and .NET 9 SDK (Roslyn 4.12 pin, complete VSIX)

## Overview
Make the in-editor light layer (DG001/DG017/DG097 squiggles and quick fixes) actually run: build `DataGuard.Analyzers`/`DataGuard.CodeFixes` against the Roslyn line that VS 2022 and the .NET 9 SDK ship (4.12), package the two dependency DLLs the analyzer needs into the VSIX, and add gates so neither regression can come back silently.

## Key Insights
- Compiler-version gate: Roslyn refuses an analyzer compiled against a newer `Microsoft.CodeAnalysis` than itself. Reproduced: consumer built with SDK 9.0.100 (Roslyn 4.12.0) + `DataGuard.Analyzers.dll` from HEAD → `warning CS9057 … references version '5.9.0.0' of the compiler, which is newer than the currently running version '4.12.0.0'` and zero DG diagnostics; same source rebuilt against `Microsoft.CodeAnalysis.CSharp 4.12.0` + `Microsoft.CodeAnalysis.Analyzers 3.11.0` → builds with 0 errors and the consumer reports DG001 ×2 + DG017 (`research/analyzer-roslyn-version.md`).
- The 5.9.0 reference is in four projects: `src/DataGuard.Analyzers/DataGuard.Analyzers.csproj:24-25`, `src/DataGuard.CodeFixes/DataGuard.CodeFixes.csproj:16` (`CSharp.Workspaces`), `tests/DataGuard.Analyzers.Tests/DataGuard.Analyzers.Tests.csproj:26,30`, `tests/DataGuard.CodeFixes.Tests/DataGuard.CodeFixes.Tests.csproj:27-28`. The test projects use `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing 1.1.4` (`:31`), which runs against whatever Roslyn the test project references, so the suites prove nothing about the IDE's compiler today.
- `Microsoft.CodeAnalysis.Analyzers 4.12.0` does not exist on nuget.org (restore resolved 4.14.0 → NU1603 under warnings-as-errors); 3.11.0 is the matching line. Every project restores in locked mode (`packages.lock.json` committed; CI `--locked-mode`), so lock files must be regenerated.
- Dependabot ignores majors only for `Microsoft.Build.*` and `FluentAssertions` (`.github/dependabot.yml:24-30`); nothing stops the next Roslyn major bump from reintroducing the regression.
- VSIX content: only `DataGuard.Analyzers.dll` + `DataGuard.CodeFixes.dll` (`src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj:103-110`, `source.extension.vsixmanifest:30-35`); the analyzer references `DataGuard.Contracts` and `DataGuard.SqlClassification` (`ContractValidationAnalyzer.cs:12`, `SqlCallSyntax.cs:11`), both netstandard2.0 and copied next to the analyzer by `CopyLocalLockFileAssemblies=true` (`DataGuard.Analyzers.csproj:17`); the NuGet package bundles them for this reason (`:38-43`). `scripts/assert-vsix.ps1:22-29` does not require them. `System.Composition.Hosting 10.0.12` (`:26`) is a non-private reference: [UNVERIFIED on Windows] whether VS provides it to VSIX analyzers (MEF host) or it must ship too → validate Q.
- `tests/DataGuard.VisualStudio.Tests/VsixAnalyzerPackagingTests.cs:47-51` returns early (silent pass) when no VSIX exists; xunit 2.9.3 (`DataGuard.VisualStudio.Tests.csproj:20`) has no dynamic skip, so "explicit skip" needs an env-gated failure instead.
- DG001's message tells the user to run `dataguard check` (`src/DataGuard.Analyzers/DiagnosticDescriptors.cs:89`), a command that does not exist; the test pins it (`tests/DataGuard.Analyzers.Tests/UnvalidatedSqlCallGeneratorTests.cs:41`).

## Requirements
- **Functional:**
  1. `Microsoft.CodeAnalysis.CSharp` / `.CSharp.Workspaces` = `4.12.0`, `Microsoft.CodeAnalysis.Analyzers` = `3.11.0` in the four projects; lock files regenerated; all analyzer/code-fix tests green on 4.12.
  2. A consumer compiled with the repo's pinned SDK and the built analyzer gets DG diagnostics and **no** CS9057 (Linux CI gate script).
  3. VSIX contains and declares as `Microsoft.VisualStudio.Analyzer` assets: `DataGuard.Analyzers.dll`, `DataGuard.CodeFixes.dll`, `DataGuard.Contracts.dll`, `DataGuard.SqlClassification.dll`; `assert-vsix.ps1` requires all four; `VsixAnalyzerPackagingTests` fails (not passes) when `DATAGUARD_VSIX_EXPECTED=1` and no VSIX is found.
  4. Dependabot ignores `version-update:semver-major` for `Microsoft.CodeAnalysis.*`.
  5. DG001 message says `run 'dataguard validate'`.
- **Non-functional:** no analyzer source change beyond the message; no new NuGet dependency; the Linux gate runs in < 60 s inside the existing `build-and-test` job.

## Architecture
```
src/DataGuard.Analyzers (4.12) ──► bin/Release/netstandard2.0/{Analyzers,Contracts,SqlClassification}.dll
        │                                   │
        ▼                                   ▼
scripts/check-analyzer-loads.sh      DataGuard.VisualStudio.csproj IncludeAnalyzersInVsix (+2 VSIXSourceItem)
(generates a temp consumer, builds   source.extension.vsixmanifest (+2 Analyzer assets)
 with the repo SDK, fails on CS9057  scripts/assert-vsix.ps1 (+2 required entries)
 or when DG017 is absent)            VsixAnalyzerPackagingTests (env-gated failure)
```
The script is the regression gate for the compiler-version contract; it needs no new source tree (workspace-governance rule 3) because the consumer project is generated into `$RUNNER_TEMP`/`mktemp -d` at run time.

## Related Code Files
- **Modify:** `src/DataGuard.Analyzers/DataGuard.Analyzers.csproj`, `src/DataGuard.CodeFixes/DataGuard.CodeFixes.csproj`, `tests/DataGuard.Analyzers.Tests/DataGuard.Analyzers.Tests.csproj`, `tests/DataGuard.CodeFixes.Tests/DataGuard.CodeFixes.Tests.csproj` + their `packages.lock.json` (regenerate with `dotnet restore --force-evaluate` per project; never by hand)
- **Modify:** `src/DataGuard.Analyzers/DiagnosticDescriptors.cs:89`, `tests/DataGuard.Analyzers.Tests/UnvalidatedSqlCallGeneratorTests.cs:41`, `src/DataGuard.Analyzers/AnalyzerReleases.Unshipped.md` (message change note)
- **Create:** `scripts/check-analyzer-loads.sh` (bash; mirrors the experiment in `research/analyzer-roslyn-version.md`)
- **Modify:** `.github/workflows/ci.yml` (`build-and-test`: one step after the solution build calling the script), `.github/dependabot.yml` (ignore rule)
- **Modify:** `src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj:103-110`, `src/DataGuard.VisualStudio/source.extension.vsixmanifest:30-35`, `scripts/assert-vsix.ps1:22-29`, `tests/DataGuard.VisualStudio.Tests/VsixAnalyzerPackagingTests.cs`, `.github/workflows/ci.yml` (`visual-studio-vsix-package` job: set `DATAGUARD_VSIX_EXPECTED=1` when running the VS tests after packaging — or run the test project in that job; see validate Q2)
- **Modify:** `docs/USAGE.md` §"Roslyn Analyzers" (supported compiler line), `CHANGELOG.md` (Unreleased: Fixed), `src/DataGuard.VisualStudio/overview.md` (analyzer assets)

## TDD Structure (`--tdd` Mandatory)

### 1. Tests Before (Step 3.T — Regression Coverage First)
- File test cần tạo/cập nhật trước khi sửa code: `scripts/check-analyzer-loads.sh`, `tests/DataGuard.VisualStudio.Tests/VsixAnalyzerPackagingTests.cs`
- Các kịch bản hành vi hiện tại cần khóa lại (pin current behavior):
  - [ ] `scripts/check-analyzer-loads.sh` run against HEAD **fails** with the CS9057 message (documents RC1; becomes the green gate after 3.I).
  - [ ] `dotnet test tests/DataGuard.Analyzers.Tests` and `tests/DataGuard.CodeFixes.Tests` green on 5.9 (97 + 37 today) — baseline before the pin.
  - [ ] `VsixAnalyzerPackagingTests`: with `DATAGUARD_VSIX_EXPECTED=1` and no VSIX → fails with a clear message; without the variable and no VSIX → returns (today's behaviour kept for local runs).
  - [ ] `assert-vsix.ps1` with a synthetic zip containing only today's six entries → OK (pin), used as the "before" of the entry-list change.

### 2. Refactor & Implementation (Step 3.I — Protected Code Changes)
1. **Step A (Write tests for current behavior):** script + env-gated test; run the four test projects; record counts.
2. **Step B (Add shared infrastructure or seams):** the script; CI step; Dependabot ignore.
3. **Step C (Refactor / Implement):** version pins + lock-file regeneration; csproj/manifest/assert-vsix entries; DG001 message + its test + Unshipped note.
4. **Step D (Verify compile + tests):** `dotnet build DataGuard.CrossPlatform.slnf -c Release` (0 warnings); the four test projects green on 4.12; `scripts/check-analyzer-loads.sh` green; `./scripts/verify_docs_sync.sh`; push → Windows jobs `Visual Studio Build and Test` + `Visual Studio VSIX Packaging Gate` green with the four entries.

### 3. Tests After (Step 3.V — New Behavior Coverage)
- [ ] `scripts/check-analyzer-loads.sh` → exit 0; output contains `DG017` and not `CS9057`.
- [ ] `VsixAnalyzerPackagingTests`: VSIX contains the four DLLs and the manifest declares four `Microsoft.VisualStudio.Analyzer` assets (Windows CI).
- [ ] `assert-vsix.ps1` synthetic zip without `DataGuard.Contracts.dll` → FAIL naming it.
- [ ] `UnvalidatedSqlCallGeneratorTests` message = `… run 'dataguard validate' for full validation`.
- [ ] Lock-file guard: `dotnet restore DataGuard.CrossPlatform.slnf --locked-mode` succeeds (CI already does this).

### 4. Regression Gate (Mandatory Pass Command)
- Lệnh compile / type-check bắt buộc pass: `dotnet build DataGuard.CrossPlatform.slnf --configuration Release` và `dotnet build tests/DataGuard.VisualStudio.Tests/DataGuard.VisualStudio.Tests.csproj -c Release -p:VSSDKBuildToolsAutoSetup=false` (Linux compile parity)
- Lệnh chạy test bắt buộc pass: `dotnet test tests/DataGuard.Analyzers.Tests -c Release --no-build && dotnet test tests/DataGuard.CodeFixes.Tests -c Release --no-build && bash scripts/check-analyzer-loads.sh`; trên Windows CI: `dotnet test tests/DataGuard.VisualStudio.Tests -c Release` + job `Visual Studio VSIX Packaging Gate`

## Todo List
- [ ] [3.T] Viết `scripts/check-analyzer-loads.sh` (đỏ trên HEAD vì CS9057), env-gated `VsixAnalyzerPackagingTests`, pin `assert-vsix.ps1`
- [ ] [3.I] Pin Roslyn 4.12.0 / Analyzers 3.11.0 ở 4 project + regenerate lock; thêm Contracts + SqlClassification vào VSIX/manifest/assert-vsix; Dependabot ignore; DG001 message
- [ ] [3.V] Script xanh, test 4 project xanh, Windows gate xanh với 4 entry, docs/CHANGELOG; vượt Regression Gate

## Success Criteria
- [ ] Mọi regression test (Tests Before) và test mới (Tests After) đều PASS.
- [ ] Lệnh tại Regression Gate trả về exit code 0.
- [ ] Mở một project C# trong VS 2022 có VSIX cài: `SELECT *` trong chuỗi Dapper hiện DG017, lời gọi chưa validate hiện DG001 (xác nhận tay trên Windows; ghi vào PR).
- [ ] Một bump Roslyn major do Dependabot bị chặn bởi `ignore`, và nếu lọt thì `check-analyzer-loads.sh` đỏ trong CI.

## Risk Assessment & Security Considerations
- **Rủi ro:** API Roslyn 5.9 được dùng trong analyzer → không biên dịch với 4.12 → **Giảm thiểu:** đã biên dịch thử sạch với 4.12 trong phiên này (`research/analyzer-roslyn-version.md`); nếu CodeFixes dùng API Workspaces mới, sửa tại chỗ (không nâng lại 5.x).
- **Rủi ro:** regenerate lock file kéo theo thay đổi transitive (NuGet audit NU1902/NU1903 từ các gói Testing 1.1.4) → **Giảm thiểu:** giữ pin `System.Private.Uri 4.3.2` đã có; chạy `--locked-mode` + licence gate cục bộ trước khi push.
- **Rủi ro:** `System.Composition.Hosting` thiếu trong VSIX làm analyzer không nạp dù đúng Roslyn → **Giảm thiểu:** validate Q2 + xác nhận tay trên Windows; nếu cần, thêm như VSIXSourceItem (đã có trong `IncludeNuGetDepsInVsix` pattern).
- **Bảo mật:** không thêm gói mới; hai DLL thêm vào VSIX là code của repo, đã trong SBOM/attestation theo workflow (`marketplace.yml:286-321`).
