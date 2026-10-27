---
phase: 1
title: "Analyzer loads in VS 2022 and .NET 9 SDK (Roslyn 4.12 pin, complete VSIX)"
status: completed
priority: P1
effort: "3.5h (+ Windows CI run)"
dependencies: []
---

# Phase 1: Analyzer loads in VS 2022 and .NET 9 SDK (Roslyn 4.12 pin, complete VSIX)

<!-- Updated: Red Team Session 2 - gate made deterministic (assembly-reference test + lock-file assert), Dependabot ignore narrowed to exact names/versions, manifest floor 17.12, reuse verify-analyzer-packaging.sh, dropped AnalyzerReleases note -->

## Overview
Make the in-editor light layer (DG001/DG017/DG097 squiggles and quick fixes) actually run: build `DataGuard.Analyzers`/`DataGuard.CodeFixes` against the Roslyn line that VS 2022 17.12+ and the .NET 9.0.1xx SDK ship (4.12), package the two dependency DLLs the analyzer needs into the VSIX, and add gates so neither regression can come back silently.

## Key Insights
- Compiler-version gate: Roslyn refuses an analyzer compiled against a newer `Microsoft.CodeAnalysis` than itself. Reproduced: consumer built with SDK 9.0.100 (Roslyn 4.12.0) + `DataGuard.Analyzers.dll` from HEAD → `warning CS9057 … references version '5.9.0.0' of the compiler, which is newer than the currently running version '4.12.0.0'` and zero DG diagnostics; same source rebuilt against `Microsoft.CodeAnalysis.CSharp 4.12.0` + `Microsoft.CodeAnalysis.Analyzers 3.11.0` → 0 errors, consumer reports DG001 ×2 + DG017 (`research/analyzer-roslyn-version.md`).
- 5.9.0 in the analyzer graph: `src/DataGuard.Analyzers/DataGuard.Analyzers.csproj:24-25` (CSharp + Analyzers), `src/DataGuard.CodeFixes/DataGuard.CodeFixes.csproj:16` (CSharp.Workspaces), `tests/DataGuard.Analyzers.Tests/DataGuard.Analyzers.Tests.csproj:26,30`, `tests/DataGuard.CodeFixes.Tests/DataGuard.CodeFixes.Tests.csproj:27-28`. Only the analyzer project references `Microsoft.CodeAnalysis.Analyzers` directly. `DataGuard.Core.csproj:23-24` and `tests/DataGuard.BinaryCompatibilityFixture` (`:13`) also reference 5.9 — they run Roslyn in-process (CLI extraction) and are **not** part of the fix; the Dependabot rule must not freeze them by accident. CodeFixes uses no post-4.12 Workspaces API (`CodeFixProviders.cs:547-548` `Renamer.RenameSymbolAsync` with `SymbolRenameOptions`, 4.4+). Testing packages 1.1.4 have floors ≤ 4.12 (lock files) → no NU1605.
- `Microsoft.CodeAnalysis.Analyzers 4.12.0` does not exist on nuget.org (NU1603 → 4.14.0); 3.11.0 is the matching line. Locked restore everywhere (`--locked-mode` in CI) → regenerate lock files with the repo tooling.
- **The gate must not depend on the runner's SDK:** CI installs `9.0.x` (`.github/workflows/ci.yml:17`) and `global.json` has `rollForward: latestFeature`, so a consumer built on the runner uses the newest 9.0 band (Roslyn ≥ 4.14) and would accept a 4.13/4.14 analyzer that VS 17.12 / SDK 9.0.1xx still refuse. Dependabot's NuGet group `patterns: ["*"]` (`.github/dependabot.yml:20-23`) proposes minor bumps; the existing `ignore` entries are major-only (`:24-33`, `Microsoft.Build.*`, `FluentAssertions`, `MessagePack`).
- VS floor: the manifest installs on `[17.0,19.0)` (`src/DataGuard.VisualStudio/source.extension.vsixmanifest:12,15,18,26`), but 17.0–17.11 ship Roslyn < 4.12 and would still refuse the analyzer. Decision Q1: raise the floor to 17.12 (recommended) or pin lower.
- VSIX content: only `DataGuard.Analyzers.dll` + `DataGuard.CodeFixes.dll` (`DataGuard.VisualStudio.csproj:103-110`, manifest `:30-35`); the analyzer references `DataGuard.Contracts` and `DataGuard.SqlClassification` (`ContractValidationAnalyzer.cs:12`, `SqlCallSyntax.cs:11`), both netstandard2.0 and copied next to the analyzer (`CopyLocalLockFileAssemblies=true`, `DataGuard.Analyzers.csproj:17`); the NuGet package bundles them for this reason (`:40-45`); `scripts/assert-vsix.ps1:22-29` does not require them. `System.Composition.Hosting 10.0.12` (`:26`) is unused in the analyzer sources (only `CodeFixProviders.cs:9` uses MEF attributes, which VS provides) → no VSIX item needed; [VERIFIED in validate session 1: the analyzer sources do not use System.Composition; only `CodeFixProviders.cs:9` MEF attributes, which VS supplies] — manual load check stays in Step D.
- `scripts/verify-analyzer-packaging.sh` already builds a temp consumer from the packed nupkg (mktemp + trap, `set -euo pipefail`) but only asserts that csc received `/analyzer:` (`:91-93`), which stays true under CS9057, and no workflow calls it → extend it instead of adding a second script (DRY; gives the orphan script its CI reference).
- `tests/DataGuard.VisualStudio.Tests/VsixAnalyzerPackagingTests.cs:47-51` returns early when no VSIX exists; the VS test job builds with `CreateVsixContainer=false` (`ci.yml:270-271`) and the packaging job runs no `dotnet test` (`:300-315`); xunit 2.9.3 has no dynamic skip → env-gated failure plus a `dotnet test` step in the packaging job (decision Q2).
- DG001 message names a non-existent `dataguard check` (`src/DataGuard.Analyzers/DiagnosticDescriptors.cs:89`, doc comment `UnvalidatedSqlCallGenerator.cs:16`, pinned by `tests/DataGuard.Analyzers.Tests/UnvalidatedSqlCallGeneratorTests.cs:41`). `AnalyzerReleases.Unshipped.md` needs no entry for a message change (ID/category/severity table only; a free-text line risks RS2xxx under `TreatWarningsAsErrors`).

## Requirements
<!-- Updated: Validation Session 1 - Q1a manifest floor 17.12; Q2a packaging-job dotnet test; Q3a repo-wide Roslyn freeze accepted -->
- **Functional:**
  1. `Microsoft.CodeAnalysis.CSharp` / `.CSharp.Workspaces` = `4.12.0` in the four analyzer-graph projects; `Microsoft.CodeAnalysis.Analyzers` = `3.11.0` in `DataGuard.Analyzers.csproj`; lock files regenerated; all analyzer/code-fix tests green on 4.12.
  2. **Deterministic guard:** a test in `DataGuard.Analyzers.Tests` and one in `DataGuard.CodeFixes.Tests` assert that the built analyzer/code-fix assembly's referenced `Microsoft.CodeAnalysis` version is `≤ 4.12.0.0` (`Assembly.GetReferencedAssemblies()`); `scripts/verify-analyzer-packaging.sh` additionally asserts the two lock files resolve `Microsoft.CodeAnalysis.CSharp`/`.Workspaces` to exactly `4.12.0`.
  3. **Smoke gate:** `verify-analyzer-packaging.sh` compiles its consumer with a temp `global.json` `{"sdk":{"version":"9.0.100","rollForward":"disable"}}`, includes a `SELECT *` Dapper source, and fails when the build log contains `CS9057` or lacks `DG017`; CI `build-and-test` runs it after "Build solution" (`ci.yml:81-82`) with a second `setup-dotnet` for `9.0.100`.
  4. VSIX contains and declares as `Microsoft.VisualStudio.Analyzer` assets: `DataGuard.Analyzers.dll`, `DataGuard.CodeFixes.dll`, `DataGuard.Contracts.dll`, `DataGuard.SqlClassification.dll`; `assert-vsix.ps1` requires all four; `VsixAnalyzerPackagingTests` fails when `DATAGUARD_VSIX_EXPECTED=1` and no VSIX is found; the packaging job runs `dotnet test tests/DataGuard.VisualStudio.Tests --no-build`-equivalent with that variable after packaging (Q2).
  5. Dependabot: `ignore` entries for the exact names `Microsoft.CodeAnalysis.CSharp`, `Microsoft.CodeAnalysis.CSharp.Workspaces`, `Microsoft.CodeAnalysis.Analyzers` with `versions: [">= 4.13.0"]` and a comment pointing at the guard; note that `DataGuard.Core`'s 5.9 copy is frozen by the same rule (decision Q3: accept, or split a per-directory Dependabot entry).
  6. Manifest `InstallationTarget`/`Prerequisite` floor raised to `17.12` (Q1); `docs/USAGE.md` §Visual Studio states "squiggles need VS 2022 17.12+".
  7. DG001 message → `run 'dataguard validate'` (descriptor, doc comment, test).
- **Non-functional:** no analyzer source change beyond the message; no new NuGet dependency; the extended script runs < 90 s and needs nuget.org only for packages already in the graph (licence gate unaffected: 4.12.0 and 3.11.0 are MIT).

## Architecture
```
src/DataGuard.Analyzers (4.12) ─► bin/Release/netstandard2.0/{Analyzers,Contracts,SqlClassification}.dll
   │                                     │
   ├─ Analyzers.Tests: RoslynReferenceVersionTests (≤ 4.12.0.0)      ← deterministic, SDK-independent
   ├─ scripts/verify-analyzer-packaging.sh (+lock-file assert, pinned global.json, CS9057 absent, DG017 present)
   │        ▲ ci.yml build-and-test: setup-dotnet 9.0.100 → dotnet pack → script
   └─ DataGuard.VisualStudio.csproj IncludeAnalyzersInVsix (+2 VSIXSourceItem) → vsixmanifest (+2 assets, floor 17.12)
            → assert-vsix.ps1 (+2 entries) → VsixAnalyzerPackagingTests (env-gated) run in the packaging job
```

## Related Code Files
- **Modify:** `src/DataGuard.Analyzers/DataGuard.Analyzers.csproj`, `src/DataGuard.CodeFixes/DataGuard.CodeFixes.csproj`, `tests/DataGuard.Analyzers.Tests/DataGuard.Analyzers.Tests.csproj`, `tests/DataGuard.CodeFixes.Tests/DataGuard.CodeFixes.Tests.csproj` + their `packages.lock.json` (regenerate with `dotnet restore <proj> --force-evaluate`; never by hand)
- **Create:** `tests/DataGuard.Analyzers.Tests/RoslynReferenceVersionTests.cs`, `tests/DataGuard.CodeFixes.Tests/RoslynReferenceVersionTests.cs`
- **Modify:** `scripts/verify-analyzer-packaging.sh`, `.github/workflows/ci.yml` (`build-and-test`: `setup-dotnet 9.0.100` + pack + script step after `:82`; `visual-studio-vsix-package`: `dotnet test` step with `DATAGUARD_VSIX_EXPECTED=1`), `.github/dependabot.yml`
- **Modify:** `src/DataGuard.Analyzers/DiagnosticDescriptors.cs:89`, `src/DataGuard.Analyzers/UnvalidatedSqlCallGenerator.cs:16`, `tests/DataGuard.Analyzers.Tests/UnvalidatedSqlCallGeneratorTests.cs:41`
- **Modify:** `src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj:103-110`, `src/DataGuard.VisualStudio/source.extension.vsixmanifest` (assets `:30-35`; floor `:12,15,18,26`), `scripts/assert-vsix.ps1:22-29`, `tests/DataGuard.VisualStudio.Tests/VsixAnalyzerPackagingTests.cs`, `src/DataGuard.VisualStudio/ExtensionVersion.cs` only if the manifest test there pins the install range (check `CliArgumentBuilderTests` "ExtensionVersion vs manifest")
- **Modify:** `docs/USAGE.md` §"Roslyn Analyzers" + §Visual Studio (compiler line / VS floor), `CHANGELOG.md` (Unreleased: Fixed), `src/DataGuard.VisualStudio/overview.md` (analyzer assets), `docs/05-operations/extension-build-guide.md` if it lists VSIX contents

## TDD Structure (`--tdd` Mandatory)

### 1. Tests Before (Step 3.T — Regression Coverage First)
- File test cần tạo/cập nhật trước khi sửa code: `tests/DataGuard.Analyzers.Tests/RoslynReferenceVersionTests.cs`, `tests/DataGuard.CodeFixes.Tests/RoslynReferenceVersionTests.cs`, `scripts/verify-analyzer-packaging.sh`, `tests/DataGuard.VisualStudio.Tests/VsixAnalyzerPackagingTests.cs`
- Các kịch bản hành vi hiện tại cần khóa lại (pin current behavior):
  - [ ] `RoslynReferenceVersionTests` written against HEAD **fails** (reference is 5.9.0.0) — documents RC1; green after 3.I.
  - [ ] `verify-analyzer-packaging.sh` with the new CS9057/DG017 assertions **fails** on HEAD (pinned SDK consumer sees CS9057); its existing checks still pass.
  - [ ] Analyzer/CodeFix suites green on 5.9 (97 + 37 today) — baseline before the pin.
  - [ ] `VsixAnalyzerPackagingTests` with `DATAGUARD_VSIX_EXPECTED=1` and no VSIX → fails with a clear message; without the variable → returns (local runs unchanged).
  - [ ] `assert-vsix.ps1` on a synthetic zip with today's six entries → OK (pin before the list grows).

### 2. Refactor & Implementation (Step 3.I — Protected Code Changes)
1. **Step A (Write tests for current behavior):** tests + script assertions above; record red/green state.
2. **Step B (Add shared infrastructure or seams):** CI steps (`setup-dotnet 9.0.100`, pack, script; packaging-job `dotnet test`), Dependabot ignore entries.
3. **Step C (Refactor / Implement):** version pins + lock-file regeneration; csproj/manifest/assert-vsix entries; manifest floor; DG001 message + doc comment + test.
4. **Step D (Verify compile + tests):** `dotnet build DataGuard.CrossPlatform.slnf -c Release` (0 warnings); `dotnet restore DataGuard.CrossPlatform.slnf --locked-mode`; the four test projects green on 4.12; `scripts/verify-analyzer-packaging.sh` green; `python3 scripts/check-workflow-policy.py` + `python3 -m unittest discover -s scripts/tests`; `./scripts/verify_docs_sync.sh`; push → Windows jobs green with four entries; **manual on Windows:** install the VSIX in VS 2022 ≥ 17.12, open a C# project with a `SELECT *` Dapper call → DG017 squiggle + DG001 (record in the PR).

### 3. Tests After (Step 3.V — New Behavior Coverage)
- [ ] `RoslynReferenceVersionTests` green (≤ 4.12.0.0) in both test projects.
- [ ] `verify-analyzer-packaging.sh` → exit 0; log contains `DG017`, no `CS9057`, prints `dotnet --version` = 9.0.100 inside the consumer; lock-file assert passes; fails when the lock file is edited to 4.14.0 (negative test in `scripts/tests`).
- [ ] VSIX contains the four DLLs and the manifest declares four `Microsoft.VisualStudio.Analyzer` assets; `InstallationTarget` floor 17.12 (Windows CI).
- [ ] `assert-vsix.ps1` synthetic zip without `DataGuard.Contracts.dll` → FAIL naming it.
- [ ] `UnvalidatedSqlCallGeneratorTests` message = `… run 'dataguard validate' for full validation`.

### 4. Regression Gate (Mandatory Pass Command)
- Lệnh compile / type-check bắt buộc pass: `dotnet build DataGuard.CrossPlatform.slnf --configuration Release` và `dotnet build tests/DataGuard.VisualStudio.Tests/DataGuard.VisualStudio.Tests.csproj -c Release -p:VSSDKBuildToolsAutoSetup=false` (Linux compile parity)
- Lệnh chạy test bắt buộc pass: `dotnet test tests/DataGuard.Analyzers.Tests -c Release --no-build && dotnet test tests/DataGuard.CodeFixes.Tests -c Release --no-build && bash scripts/verify-analyzer-packaging.sh && python3 -m unittest discover -s scripts/tests`; trên Windows CI: `dotnet test tests/DataGuard.VisualStudio.Tests -c Release` + job `Visual Studio VSIX Packaging Gate`

## Todo List
- [ ] [3.T] `RoslynReferenceVersionTests` (đỏ trên HEAD), mở rộng `verify-analyzer-packaging.sh` (CS9057/DG017/lock assert, đỏ trên HEAD), env-gated `VsixAnalyzerPackagingTests`, pin `assert-vsix.ps1`
- [ ] [3.I] Pin 4.12.0 / 3.11.0 + regenerate lock; CI steps (SDK 9.0.100, pack, script, packaging-job test); Dependabot ignore theo tên/phiên bản; Contracts + SqlClassification vào VSIX/manifest/assert-vsix; manifest floor 17.12; DG001 message
- [ ] [3.V] Test/script xanh, Windows gate xanh với 4 entry + floor, docs/CHANGELOG; xác nhận tay squiggle trong VS; vượt Regression Gate

## Success Criteria
- [ ] Mọi regression test (Tests Before) và test mới (Tests After) đều PASS.
- [ ] Lệnh tại Regression Gate trả về exit code 0.
- [ ] Trong VS 2022 ≥ 17.12 với VSIX mới: `SELECT *` trong chuỗi Dapper hiện DG017, lời gọi chưa validate hiện DG001 (xác nhận tay, ghi vào PR).
- [ ] Bump Roslyn 4.13+ bị Dependabot ignore; nếu sửa tay, `RoslynReferenceVersionTests` và script đỏ trong CI bất kể SDK trên runner.

## Risk Assessment & Security Considerations
- **Rủi ro:** API Roslyn 5.9 dùng trong analyzer/code fix → **Giảm thiểu:** analyzer đã biên dịch sạch với 4.12 trong phiên này; CodeFixes chỉ dùng API ≤ 4.4 (kiểm tra ở 3.I).
- **Rủi ro:** regenerate lock file kéo transitive khác (NU1902/NU1903, licence gate) → **Giảm thiểu:** giữ pin `System.Private.Uri 4.3.2`; chạy `--locked-mode` + `scripts/check-nuget-licences.py` cục bộ trước khi push.
- **Rủi ro:** ignore Dependabot theo tên cũng đóng băng bản 5.9 của `DataGuard.Core` → **Giảm thiểu:** Q3; mặc định chấp nhận và ghi chú trong `dependabot.yml`.
- **Bảo mật:** không thêm gói mới; hai DLL thêm vào VSIX là code của repo, nằm trong SBOM/attestation hiện có (`marketplace.yml:286-321`).
