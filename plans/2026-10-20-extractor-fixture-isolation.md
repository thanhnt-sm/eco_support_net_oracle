# Extractor fixture repository isolation / Cô lập repo của fixture extractor

Base `12cf09c94be896596cb862abf9355f4aab35ecb5`; separate branch `fix/extractor-fixture-isolation`. Branding stays uncommitted in `/workspace/stackaccord-beta-display`; no branding changes belong to this patch.

## Reproduction and cause / Tái hiện và nguyên nhân

Run `dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj --configuration Release --filter FullyQualifiedName~ProjectCSharpSqlSourceExtractorTests` in a sandbox whose temporary-root ancestor contains `.git`.

Before fix: 34 passed, 4 failed (38 total). Full test names under `DataGuard.Core.Tests.ProjectCSharpSqlSourceExtractorTests`:

- `DescriptorId_HasStableFormat_AndHashIgnoresWhitespaceAndShiftedLines`: expected `project-sql:Repo.cs:…`, observed `project-sql:dg-extractor…/Repo.cs:…`; assertion at original line254.
- `OversizedLiteralAndSyntaxErrors_AreReportedAsDiagnostics`: expected `Big.cs` diagnostic; observed temporary-directory prefix; original line292.
- `UnreadableFile_IsReportedAsDiagnostic`: expected `Broken.cs` diagnostic; observed temporary-directory prefix; original line279.
- `SkipContractCheck_OnMethodTypeEnclosingTypeOrOtherPartial_SkipsAndReports`: expected only `SkipContractCheck.cs` paths; observed temporary-directory prefix; original line236.

`/proc/self/mountinfo` identifies `/tmp/.git` and `/workspace/.git` as read-only synthetic tmpfs mounts (mode0555). Granting another temporary root produced the same boundary marker. No evidence indicates product/test code created these root markers. Do not remove sandbox mounts or weaken protection.

Production `FindRepositoryRoot` intentionally selects the nearest ancestor `.git` directory/file, consistent with documented repository-relative IDs. The fixture incorrectly relied on absence of ancestor repositories. Classification: fixture isolation assumption exposed by environment, not an established production defect.

Nguyên nhân là fixture chưa định nghĩa repo riêng, trong khi sandbox có marker `.git` ở thư mục cha. Giữ nguyên logic production và mọi assertion hiện có; không skip, không hạ gate, không đổi expected path để khớp đường dẫn ngẫu nhiên.

## Fix and acceptance / Sửa và tiêu chí chấp nhận

- Each fixture establishes its own `.git` marker inside its uniquely owned temporary directory; existing cleanup owns that directory only.
- Existing four assertions must pass unchanged under the same sandbox.
- New regressions preserve repository-relative descriptor IDs and diagnostic paths when scanning `src/`, and ensure nearest nested directory/file markers take precedence over an outer repository.
- EN/VI mapping: `docs/03-components/core/sources.md` and `.vi.md`, section on IDs/diagnostics; tests in `ProjectCSharpSqlSourceExtractorTests.cs`. No runtime/API/namespace/CLI/config/license/version changes.
- Run focused tests, complete cross-platform .NET gates, docs/UTF-8 and independent review. Record failures/skips and coverage scope honestly.

Không publish Marketplace. Nghiệm thu VSIX thật, nâng cấp và UI trong Visual Studio vẫn chưa thực hiện; patch fixture không thay đổi giới hạn đó.

## Verified result / Kết quả đã xác minh (2026-10-20)

- .NET SDK9.0.319 / Node22.23.3 verified before edits. Focused regression:41passed,0failed,0skipped (before fix:34passed,4failed).
- Full solution Release build:0warnings/0errors. `SKIP_ACT=1 ./scripts/verify_local_gates.sh`: passed after allowing IPC needed by instrumented CLI named mutexes. First sandbox coverage attempt failed with `DataGuard_Mutex`/EROFS and is retained in evidence; no assertions or coverage collection disabled.
- Full cross-platform tests with coverage:2,313passed,0failed,5existing Windows-only junction skips/2,318total; LiveDb excluded. Instrumented production-source union:17,110/21,010 lines=81.44%. Excludes installed Visual Studio UI and VS helper code linked into excluded test assemblies; not whole-product coverage.
- Formatting, analyzers, actionlint, docs sync and NuGet audit passed. Formatter reported workspace-load warnings; Release compilation had none. `act`, repository-history secret scan, Node suite, live databases and hosted Windows/macOS CI were not run in this fixture-only task.
- Combined branding+fixture-fix run:2,313passed,0failed,5skipped. Temporary fixture overlay restored exactly in the branding worktree, preserving separate patches.
- Independent read-only review: no material findings. Four changed/new files pass UTF-8/NFC checks; original assertions unchanged. No explicit no-ancestor-marker fallback regression was added because sandbox ancestors carry markers; production fallback is unchanged.

Đã xử lý lỗi fixture và xác minh kết hợp branding; chưa kiểm chứng cài đặt/upgrade/UI trên Visual Studio hoặc gói VSIX thật. Không push/tag/publish; báo commit và bằng chứng trước mọi bước remote.
