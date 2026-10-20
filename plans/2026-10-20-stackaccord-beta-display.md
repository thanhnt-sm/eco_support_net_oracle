# StackAccord Beta display preparation / Chuẩn bị tên hiển thị Beta

Original base: `12cf09c94be896596cb862abf9355f4aab35ecb5`; integration parent: `84ad8a713b858d17d6015efc31e261a9fd7eb3d0` (fixture fix, unchanged commit). Branch: `fix/stackaccord-beta-display`.

This is source preparation, not publication or installed acceptance. Đây là chuẩn bị mã nguồn, chưa phải xuất bản hoặc nghiệm thu cài đặt. Existing nightly .63 predates this patch / nightly .63 chưa chứa patch này.

## Scope and mapping / Phạm vi và đối chiếu

| Source / Mã nguồn | Contract and regression / Hợp đồng và kiểm thử | EN/VI documentation / Tài liệu |
|---|---|---|
| `source.extension.vsixmanifest`, `DataGuardPackage.cs` | Full name `StackAccord – .NET Database Contracts (Beta)`; 44 characters; Beta description; unchanged technical identity, publisher and source version | `docs/product/marketplace-copy.md`, `.vi.md`; `docs/product/release-claims.md` |
| `Commands/Menus.vsct`, `OutputPaneWriter.cs`, `DataGuardPackage.Support.cs` | Display captions only; keep GUIDs, command names, Options category, diagnostic prefixes and support format | `docs/03-components/tooling/visual-studio-extension.md`, `.vi.md`; `docs/operations/support-summary.md`, `.vi.md`; `docs/USAGE.md` |
| `overview.md` | Bounded claims; unsupported/unknown is not validated; Planned features remain Planned; no automatic upload | `docs/testing/vs-beta-acceptance.md`, `.vi.md` |
| `scripts/tests/test_stackaccord_display.py` | Source metadata, identity/settings, listing consistency and NFC UTF-8 regression | This plan / Kế hoạch này |
| `VsixAnalyzerPackagingTests.cs` | Real packaged manifest identity/display checks when VSIX exists; `DATAGUARD_VSIX_EXPECTED=1` requires the artifact | Existing Windows packaging gate and acceptance checklist |

VSIX [schema limits](https://learn.microsoft.com/en-us/visualstudio/extensibility/vsix-extension-schema-2-0-reference?view=visualstudio): DisplayName 50 characters, Description 1000, Tags 100. These checks do not establish Marketplace portal acceptance.

Không đổi technical ID `DataGuard.VisualStudio`, publisher `thanhnt-sm`, namespace, CLI/config, repo, license hoặc version. Options vẫn là **Tools > Options > DataGuard** để bảo toàn thiết lập. Beta là nhãn đánh giá, không tự tạo kênh Marketplace private/prerelease.

## Gates and remaining Windows acceptance / Kiểm tra và nghiệm thu Windows còn thiếu

- Run full solution Release build, source metadata tests, Python suite, docs sync and UTF-8 checks. Chạy build Release toàn solution, kiểm thử metadata/Python, docs sync và UTF-8.
- Run relevant local .NET tests; record unavailable runtime/packaging checks separately. Ghi riêng kiểm tra runtime/packaging chưa thực hiện được.
- On an authorized Windows build, produce the actual VSIX using the existing packaging workflow with publication disabled. Run `VsixAnalyzerPackagingTests` with `DATAGUARD_VSIX_EXPECTED=1`; record source SHA, artifact SHA-256, version and run ID. A Linux compilation or a test returning without a VSIX is not packaging acceptance.
- Trên Windows được phép: kiểm tra đúng gói VSIX, tên trong installer/Manage Extensions/About, menu/Output và preview hỗ trợ; tiếng Việt/Unicode, không cắt tên hoặc lỗi DPI; giữ Options/consent sau nâng cấp cùng ID; không tạo extension trùng. Kiểm tra save/cancel, navigation và lifecycle theo checklist EN/VI.
- Installed VS host and upgrade acceptance remain unavailable until tested on the exact artifact. Chưa nghiệm thu Visual Studio thực tế; không suy ra kết quả từ CI hoặc nightly cũ.
- No commit, push, tag or Marketplace publication in this task. Review the local patch before separately authorizing a PR; publication remains paused.

## Local result / Kết quả local (2026-10-20)

SDK 9.0.319; Node 22.23.3. Remote main and local base verified at `12cf09c`. Release build: 0 warnings/errors. Display regressions: 4/4 passed (initial metadata 3 failures; navigation regression failed before its fix). Python suite: 296 total, 295 passed, 1 API-availability skip. Docs sync and UTF-8/NFC checks passed. Independent review found no remaining material branding issue after navigation corrections.

Full cross-platform .NET run: 2,306 passed, 4 failed, 5 Windows-only junction skips (2,315 total; LiveDb excluded). Four extractor path assertions also fail on unchanged `37a016f` baseline (Core source/tests identical to base `12cf09c`): DescriptorId_HasStableFormat_AndHashIgnoresWhitespaceAndShiftedLines, OversizedLiteralAndSyntaxErrors_AreReportedAsDiagnostics, UnreadableFile_IsReportedAsDiagnostic, SkipContractCheck_OnMethodTypeEnclosingTypeOrOtherPartial_SkipsAndReports. A `.git` marker under the temporary root changes repository-relative paths; rerunning with `/var/tmp` also encountered a marker. No assertions were disabled. This test-environment/isolation issue remains unresolved and is not part of the display patch.

Chưa thể gọi toàn bộ gates là đạt: còn 4 assertion extractor lỗi như bản nền. Không chạy lại coverage, live DB hoặc Node suite cho patch chỉ đổi hiển thị VS. Test packaging mới chỉ được biên dịch; chưa chạy trên VSIX thật. `dotnet format whitespace` exit 0 nhưng báo warning khi tải workspace. Chưa commit/push/publish.

## Follow-up verification / Xác minh bổ sung

The four extractor failures were traced to sandbox-created read-only `.git` mounts at writable roots (`/proc/self/mountinfo`), exposing an implicit fixture assumption. A separate patch on `fix/extractor-fixture-isolation` gives each fixture its own repository boundary and adds three nested-repository cases; production behavior and existing assertions stay unchanged.

Combined branding + fixture-fix verification passed: **2,313 .NET tests passed, 0 failed, 5 Windows-only junction skips / 2,318 total** (`Category!=LiveDb`). The fixture fix was temporarily overlaid for this run, then restored byte-for-byte in this branding worktree; it remains a separate reviewable patch. This supersedes the earlier test blocker only when the fixture fix is included. Kiểm thử kết hợp đã đạt; nhánh branding riêng vẫn cần áp patch fixture để tái hiện kết quả trong sandbox này. Nghiệm thu VSIX/Visual Studio thật vẫn chưa thực hiện.

Fixture fix committed locally as `84ad8a713b858d17d6015efc31e261a9fd7eb3d0` on `fix/extractor-fixture-isolation`; standalone gates with coverage passed (2,313 passed, 5 Windows skips; 81.44% instrumented production lines). No push or publication.

## Authorized PR integration / Tích hợp PR được phép

After local validation, the owner authorized pushing separate fixture and branding PRs and merging only when existing rules and checks permit. Branding fast-forwards to the exact fixture commit; its own commit contains only the display patch. Open the branding PR against the fixture branch while that PR is pending, then retarget to main after fixture integration. No duplicated/cherry-picked fixture commit, rule bypass or Marketplace publication. Earlier local-only statements above record the preparation stage.
