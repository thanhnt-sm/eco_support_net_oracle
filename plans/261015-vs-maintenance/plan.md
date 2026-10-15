# Focused VS maintenance / Bảo trì VS theo phạm vi

Baseline / Mốc gốc: `341472510925e22459c1c71d0f6a470647d07a62` (fetched main, complete 389-commit history and tags). Branch: `fix/vs-maintenance-contracts`.

Authorization / Phê duyệt: four groups below; branch, commit, push and draft PR after genuine verification. No merge, release, deployment or protection changes. / Bốn nhóm dưới đây; chỉ tạo draft PR sau kiểm chứng thật; không merge, phát hành, triển khai hoặc đổi bảo vệ.

| Requirement / Yêu cầu | Source / Nguồn | Docs / Tài liệu | Regression / Hồi quy | Evidence / Bằng chứng |
|---|---|---|---|---|
| One-based SARIF → zero-based VS exactly once / Đổi tọa độ đúng một lần | ErrorListPresenter.CreateTask; SarifErrorListPublisher.Load | VS guides EN/VI; overview; quickstart | Load → CreateTask → IVsTaskItem; first, 42:12, invalid, integer bounds; existing URI containment | Source review and compile passed; Windows execution pending / Review và compile đạt; chờ chạy Windows |
| Forget validation and live consent / Xóa cả hai consent | SolutionTrustGate; DataGuardPackage.Consent | VS guides EN/VI; overview | none, validation, live, both; repeated removal; other solution | Source review and compile passed; Windows execution pending / Review và compile đạt; chờ chạy Windows |
| Reviewed Pkcs release-line floors; five genuine lock restores / Ngưỡng Pkcs theo nhánh; restore thật năm lock | test_alert19_resolution.py; standalone project locks | This plan / Kế hoạch này | boundary policy; locked restores/build/smoke | Baseline Python: 6 tests, 1 failure (10.0.13 != 9.0.21), 1 live API skip |
| Correct command/mode/result/location guidance / Hướng dẫn đúng lệnh/chế độ/kết quả/vị trí | Menus.vsct; CliDiscovery; Publishing; LiveCommands; ExitCodeExplainer | VS guides EN/VI; overview; quickstart | docs sync plus source review | Docs sync and independent source review passed / Docs sync và review độc lập đạt |

## Environment / Môi trường

Debian 13; .NET SDK 9.0.319 (global.json 9.0.100/latestFeature), runtime 9.0.21; Node 22.23.3 (official SHA-256 verified), Python 3.12.14, Git 2.52.0, actionlint 1.7.12 (official checksum verified). SDK installed using Microsoft's dotnet-install script. Isolated caches/TMPDIR under `/tmp/dg-maint`; runtime requires its normal user mutex directory. Standalone xUnit smoke restore/build/test/format passed (1 passed, 0 skipped; build 0 warnings/errors). This is tooling evidence, not a product test.

Linux does not provide Git Bash/Visual Studio. Windows net472 tests, VSIX packaging and manual VS acceptance remain separate, unrun gates. Exact user VS version and reproduction are pending. / Linux không có Git Bash/Visual Studio; chưa chạy test Windows net472, đóng gói VSIX hay nghiệm thu VS thủ công. Còn thiếu phiên bản VS và cách tái hiện của người dùng.

## Scope boundaries / Ranh giới

WI-02 and WI-05 are only partially addressed by this work; retain the broader audit backlog. / Chỉ xử lý một phần WI-02 và WI-05; giữ backlog audit rộng hơn.

Separate follow-ups, not fixes or executed exploits in this PR: VSCode production coverage 50.08% over 16 modules (101 tests passed in prior evidence; not rerun here), reject missing/stale uploads; conditional Oracle execution with retained credentials/DescribeRefCursors in IDE-safe; repository-selected Vault endpoint with environment token outside IDE-safe; EF Entity(string, lambda) snapshot rejection; dropped Unknown type checks; unsafe VSCode placeholder fixes. / Các phát hiện tiếp theo tách biệt, chưa sửa hoặc khai thác thực nghiệm trong PR này. Không dùng DB sản xuất hoặc token thật.

## Verification record / Nhật ký kiểm chứng

- Baseline Python failure reproduced at the exact baseline SHA above. / Đã tái hiện lỗi Python tại SHA gốc.
- Baseline unchanged local gates: topology, preflight, docs sync and actionlint passed; full gate completed as recorded below. / Gate gốc đã hoàn tất như ghi bên dưới.
- Required before publication: regression review, unchanged pre-commit/pre-push, applicable script checks, genuine lock regeneration and independent review. Track terminal hosted CI against the pushed SHA. / Trước xuất bản: hồi quy, hook nguyên trạng, kiểm tra script, tạo lock thật và review độc lập; theo dõi CI đến kết quả cuối theo SHA đã push.

### Local results before publication / Kết quả cục bộ trước xuất bản

- Unchanged `SKIP_ACT=1 ./scripts/verify_local_gates.sh`: PASS. Cross-platform Release restore/build/analyzers/format, tests and audit completed. Tests: 2,030 passed, 5 skipped; union coverage 70.78% (14,712/20,786). LiveDb excluded by the repository's own gate. The script's “verified separately” message is not evidence: act/Docker/TruffleHog were not run here. / Gate nguyên trạng đạt; không tính 5 skip hay LiveDb là đạt; chưa chạy act/Docker/TruffleHog.
- Exact canonical `dotnet build DataGuard.sln --configuration Release`: PASS, 0 warnings/errors. VS test assembly compiled on Linux; no Windows execution claimed. / Build toàn solution đạt, không đồng nghĩa đã chạy test VS trên Windows.
- All five standalone locks regenerated by real `dotnet restore --force-evaluate`, followed by locked restore and Release build. SourceLink's transitive Hashing and Core/Analyzer dependency pins also synchronize; no downgrades. Sample adds two empty RID groups from central props. / Năm lock được tạo bằng restore thật, locked restore/build đạt, không hạ phiên bản.
- Initial fixture build failed because BaselineCoreAssembly was omitted; rerun with the same current-Core path used by CI passed. This is a consumer compile smoke, not a retained-release binary compatibility claim. Initial VS regression compile failed with VSTHRD012 twice; supplying JoinableTaskFactory fixed it, full build passed. / Ghi nhận hai lỗi lệnh/test ban đầu và kết quả chạy lại; không tuyên bố tương thích nhị phân với bản phát hành cũ.
- Python discovery: 118 total, 117 passed, 1 live GitHub API test skipped (Forbidden). Focused Pkcs: 7 total, 6 passed, same 1 skip. / Không tính API skip là pass.
- Workflow policy, docs sync, git-tools shell suites and CLI `--help` smoke: PASS. / Các kiểm tra này đạt.
- Benchmark execution smoke: classifier Dry 3 cases and observability Dry 4 cases completed. Both reported inability to raise process priority. No performance conclusion or full-run claim. Sample/plugin are libraries: compile smoke only. / Chỉ smoke thực thi, không kết luận hiệu năng; sample/plugin chỉ compile.
- Independent read-only review found no blocking code/lock issues. Its discovery-order documentation correction (`.dotnet/tools` before Program Files) was applied in EN/VI and overview. Review noted no automated execution of the caret event handler; manual Windows acceptance remains open. / Review độc lập không thấy lỗi chặn; đã sửa thứ tự tìm CLI; vẫn thiếu nghiệm thu caret trên Windows.
- C# regressions were not executed red-before-green because the VS SDK tests target Windows net472. Do not report compilation or source review as executed regression tests. / Chưa chạy hồi quy C# đỏ-trước-xanh do cần Windows; không đánh đồng compile/review với test thực thi.

### Advisory policy / Chính sách advisory

Microsoft's [GHSA-846f-f764-76mx](https://github.com/dotnet/runtime/security/advisories/GHSA-846f-f764-76mx) and [GHSA-2fmv-rv2c-59x7](https://github.com/dotnet/runtime/security/advisories/GHSA-2fmv-rv2c-59x7) specify stable patched floors 8.0.2, 9.0.21 and 10.0.13. The test accepts later patches only within these reviewed major/minor lines; unknown lines, prereleases and malformed values fail closed pending review. / Chỉ chấp nhận patch từ ngưỡng trong ba nhánh đã review; nhánh mới, prerelease và giá trị sai bị từ chối chờ review.
