# Focused VS maintenance / Bảo trì VS theo phạm vi

Baseline / Mốc gốc: `341472510925e22459c1c71d0f6a470647d07a62` (fetched main, complete 389-commit history and tags). Branch: `fix/vs-maintenance-contracts`.

Verified code SHA / SHA mã đã kiểm chứng: `dc5e89c1acc8eef03822d1a84eb5a6622a6cb324`, in [draft PR #59](https://github.com/thanhnt-sm/eco_support_net_oracle/pull/59). The hosted results below are final for that SHA. This follow-up only synchronizes this plan; later documentation-only heads retain these explicitly SHA-bound results rather than relabelling them as new executions. / Kết quả hosted bên dưới đã kết thúc tại SHA này. Bản cập nhật chỉ đồng bộ kế hoạch; HEAD chỉ đổi tài liệu về sau giữ bằng chứng gắn đúng SHA, không đổi nhãn thành lượt chạy mới.

Authorization / Phê duyệt: four groups below; branch, commit, push and draft PR after genuine verification. No merge, release, deployment or protection changes. / Bốn nhóm dưới đây; chỉ tạo draft PR sau kiểm chứng thật; không merge, phát hành, triển khai hoặc đổi bảo vệ.

| Requirement / Yêu cầu | Source / Nguồn | Docs / Tài liệu | Regression / Hồi quy | Evidence / Bằng chứng |
|---|---|---|---|---|
| One-based SARIF → zero-based VS exactly once / Đổi tọa độ đúng một lần | [ErrorListPresenter.CreateTask](../../src/DataGuard.VisualStudio/ErrorListPresenter.cs); [SarifErrorListPublisher.Load](../../src/DataGuard.VisualStudio/SarifErrorListPublisher.cs) | [VS guide EN](../../docs/03-components/tooling/visual-studio-extension.md) / [VI](../../docs/03-components/tooling/visual-studio-extension.vi.md); [overview](../../src/DataGuard.VisualStudio/overview.md); [quickstart](../../docs/01-overview/quickstart.md) | [NavigationTests](../../tests/DataGuard.VisualStudio.Tests/NavigationTests.cs): Load → CreateTask → IVsTaskItem; first, 42:12, invalid, integer bounds; existing URI containment | Windows CI and packaged-VSIX tests passed at the verified code SHA; manual navigation remains open / Test Windows và trên VSIX đạt tại SHA mã đã kiểm chứng; còn nghiệm thu điều hướng thủ công |
| Forget validation and live consent / Xóa cả hai consent | [SolutionTrustGate](../../src/DataGuard.VisualStudio/SolutionTrustGate.cs); [DataGuardPackage.Consent](../../src/DataGuard.VisualStudio/DataGuardPackage.Consent.cs) | [VS guide EN](../../docs/03-components/tooling/visual-studio-extension.md) / [VI](../../docs/03-components/tooling/visual-studio-extension.vi.md); [overview](../../src/DataGuard.VisualStudio/overview.md) | [SolutionTrustGateTests](../../tests/DataGuard.VisualStudio.Tests/SolutionTrustGateTests.cs): none, validation, live, both; repeated removal; other solution | Windows CI and packaged-VSIX tests passed at the verified code SHA / Test Windows và trên VSIX đạt tại SHA mã đã kiểm chứng |
| Reviewed Pkcs release-line floors; five genuine lock restores / Ngưỡng Pkcs theo nhánh; restore thật năm lock | [test_alert19_resolution.py](../../scripts/tests/test_alert19_resolution.py); standalone project locks | This plan / Kế hoạch này | boundary policy; locked restores/build/smoke | Historical baseline: 1 failure; corrected suite: 117 passed, 1 API skip; five locked restores/builds passed / Mốc gốc có 1 lỗi; bộ test sau sửa: 117 đạt, 1 API skip; năm locked restore/build đạt |
| Correct command/mode/result/location guidance / Hướng dẫn đúng lệnh/chế độ/kết quả/vị trí | Menus.vsct; CliLocator; Publishing; LiveCommands; ExitCodeExplainer | [VS guide EN](../../docs/03-components/tooling/visual-studio-extension.md) / [VI](../../docs/03-components/tooling/visual-studio-extension.vi.md); [overview](../../src/DataGuard.VisualStudio/overview.md); [quickstart](../../docs/01-overview/quickstart.md) | docs sync plus source review | Docs sync and independent source review passed / Docs sync và review độc lập đạt |

## Environment / Môi trường

Debian 13; .NET SDK 9.0.319 (global.json 9.0.100/latestFeature), runtime 9.0.21; Node 22.23.3 (official SHA-256 verified), Python 3.12.14, Git 2.52.0, actionlint 1.7.12 (official checksum verified). SDK installed using Microsoft's dotnet-install script. Isolated caches/TMPDIR under `/tmp/dg-maint`; runtime requires its normal user mutex directory. Standalone xUnit smoke restore/build/test/format passed (1 passed, 0 skipped; build 0 warnings/errors). This is tooling evidence, not a product test.

The local Linux environment did not run Git Bash/Visual Studio. Hosted Windows net472 tests and VSIX packaging subsequently passed, as recorded below. Manual VS acceptance, the exact user VS version and reproduction remain pending. / Môi trường Linux cục bộ không chạy Git Bash/Visual Studio. Test Windows net472 và đóng gói VSIX sau đó đã đạt trên hosted CI như ghi bên dưới. Nghiệm thu VS thủ công, phiên bản VS chính xác và cách tái hiện của người dùng vẫn còn mở.

## Scope boundaries / Ranh giới

WI-02 and WI-05 are only partially addressed by this work; retain the broader audit backlog. / Chỉ xử lý một phần WI-02 và WI-05; giữ backlog audit rộng hơn.

Separate follow-ups, not fixes or executed exploits in this PR: VSCode production coverage 50.08% over 16 modules (50.08% remains prior production-coverage evidence; hosted CI subsequently passed 101 tests without fixing that denominator issue), reject missing/stale uploads; conditional Oracle execution with retained credentials/DescribeRefCursors in IDE-safe; repository-selected Vault endpoint with environment token outside IDE-safe; EF Entity(string, lambda) snapshot rejection; dropped Unknown type checks; unsafe VSCode placeholder fixes. / Các phát hiện tiếp theo tách biệt, chưa sửa hoặc khai thác thực nghiệm trong PR này. Không dùng DB sản xuất hoặc token thật.

## Historical prepublication record / Nhật ký lịch sử trước xuất bản

- Baseline Python failure reproduced at the exact baseline SHA above. / Đã tái hiện lỗi Python tại SHA gốc.
- Baseline unchanged local gates: topology, preflight, docs sync and actionlint passed; full gate completed as recorded below. / Gate gốc đã hoàn tất như ghi bên dưới.
- Original publication requirements were completed: regression review, unchanged pre-commit/pre-push, applicable script checks, genuine lock regeneration and independent review. Final hosted outcomes are recorded separately below. / Đã hoàn tất yêu cầu xuất bản ban đầu: review hồi quy, hook nguyên trạng, kiểm tra script, tạo lock thật và review độc lập. Kết quả hosted cuối cùng được ghi riêng bên dưới.

### Historical local results before publication / Kết quả cục bộ lịch sử trước xuất bản

- Historical baseline run of unchanged `SKIP_ACT=1 ./scripts/verify_local_gates.sh`: PASS. Cross-platform Release restore/build/analyzers/format, tests and audit completed. Tests: 2,030 passed, 5 skipped; union coverage 70.78% (14,712/20,786). LiveDb excluded by the repository's own gate. The script's “verified separately” message is not evidence: act/Docker/TruffleHog were not run here. / Gate nguyên trạng đạt; không tính 5 skip hay LiveDb là đạt; chưa chạy act/Docker/TruffleHog.
- Exact canonical `dotnet build DataGuard.sln --configuration Release`: PASS, 0 warnings/errors. VS test assembly compiled on Linux; no Windows execution claimed. / Build toàn solution đạt, không đồng nghĩa đã chạy test VS trên Windows.
- All five standalone locks regenerated by real `dotnet restore --force-evaluate`, followed by locked restore and Release build. SourceLink's transitive Hashing and Core/Analyzer dependency pins also synchronize; no downgrades. Sample adds two empty RID groups from central props. / Năm lock được tạo bằng restore thật, locked restore/build đạt, không hạ phiên bản.
- Initial fixture build failed because BaselineCoreAssembly was omitted; rerun with the same current-Core path used by CI passed. This is a consumer compile smoke, not a retained-release binary compatibility claim. Initial VS regression compile failed with VSTHRD012 twice; supplying JoinableTaskFactory fixed it, full build passed. / Ghi nhận hai lỗi lệnh/test ban đầu và kết quả chạy lại; không tuyên bố tương thích nhị phân với bản phát hành cũ.
- Python discovery: 118 total, 117 passed, 1 live GitHub API test skipped (Forbidden). Focused Pkcs: 7 total, 6 passed, same 1 skip. / Không tính API skip là pass.
- Workflow policy, docs sync, git-tools shell suites and CLI `--help` smoke: PASS. / Các kiểm tra này đạt.
- Benchmark execution smoke: classifier Dry 3 cases and observability Dry 4 cases completed. Both reported inability to raise process priority. No performance conclusion or full-run claim. Sample/plugin are libraries: compile smoke only. / Chỉ smoke thực thi, không kết luận hiệu năng; sample/plugin chỉ compile.
- Independent read-only review found no blocking code/lock issues. Its discovery-order documentation correction (`.dotnet/tools` before Program Files) was applied in EN/VI and overview. Review noted no automated execution of the caret event handler; manual Windows acceptance remains open. / Review độc lập không thấy lỗi chặn; đã sửa thứ tự tìm CLI; vẫn thiếu nghiệm thu caret trên Windows.
- C# regressions were not executed red-before-green because the VS SDK tests target Windows net472. Do not report compilation or source review as executed regression tests. / Chưa chạy hồi quy C# đỏ-trước-xanh do cần Windows; không đánh đồng compile/review với test thực thi.

## Final hosted evidence for the verified code SHA / Bằng chứng hosted cuối cùng theo SHA mã

All three workflows reached `completed/success` for `dc5e89c1acc8eef03822d1a84eb5a6622a6cb324`: [CI #315](https://github.com/thanhnt-sm/eco_support_net_oracle/actions/runs/38678614715), [Marketplace Extensions #259](https://github.com/thanhnt-sm/eco_support_net_oracle/actions/runs/38678614682), and [Standards audit #231](https://github.com/thanhnt-sm/eco_support_net_oracle/actions/runs/38678614680). / Cả ba workflow đã kết thúc thành công tại SHA mã này.

GitHub tested synthetic PR merge `e4844c8320c52d3694c4005d7fe894c5139d5ce0`; its tree `af31d73979265fcacf237e056c4c9f99106e3937` was verified identical to the code head tree. This is a CI merge ref, not a merged PR. / CI kiểm tra merge ref tổng hợp có tree giống hệt HEAD mã; PR chưa được merge.

| Check / Kiểm tra | Final outcome / Kết quả cuối | Verified job / Job đã xác minh |
|---|---|---|
| Windows net472, including coordinate and consent regressions / Windows net472 gồm hồi quy tọa độ và consent | 278 passed, 1 skipped, 0 failed / 278 đạt, 1 skip, 0 lỗi | [Visual Studio Build and Test](https://github.com/thanhnt-sm/eco_support_net_oracle/actions/runs/38678614715/job/116237805773) |
| Packaged VSIX and its tests / VSIX đã đóng gói và test tương ứng | Content assertion passed (26 entries, all 8 required present); 278 passed, 1 skipped, 0 failed / Kiểm tra nội dung đạt; 278 test đạt, 1 skip, 0 lỗi | [VSIX Packaging Gate](https://github.com/thanhnt-sm/eco_support_net_oracle/actions/runs/38678614715/job/116237806004) |
| Disposable LiveDB integration / Tích hợp LiveDB tạm trong CI | 10 passed, 0 skipped, 0 failed / 10 đạt, 0 skip, 0 lỗi | [Live DB Integration](https://github.com/thanhnt-sm/eco_support_net_oracle/actions/runs/38678614715/job/116237805652) |
| Linux C# and Python suites / Bộ test C# Linux và Python | C#: 2,030 passed, 5 skipped; Python: 117 passed, 1 API skip; 0 failures / Không có lỗi | [Build and Test](https://github.com/thanhnt-sm/eco_support_net_oracle/actions/runs/38678614715/job/116237805949) |
| VS Code | 101 passed, 0 skipped, 0 failed; production coverage issue remains separate / 101 đạt; vấn đề coverage production vẫn tách riêng | [VS Code Extension Tests](https://github.com/thanhnt-sm/eco_support_net_oracle/actions/runs/38678614715/job/116237805896) |
| Marketplace package / Gói Marketplace | Visual Studio and VS Code packaging passed; publication and attestation skipped for the PR / Đóng gói đạt; publish và attest được skip trên PR | [Visual Studio package](https://github.com/thanhnt-sm/eco_support_net_oracle/actions/runs/38678614682/job/116238464216), [VS Code package](https://github.com/thanhnt-sm/eco_support_net_oracle/actions/runs/38678614682/job/116238464237) |

CI #315 finished with all 11 jobs successful, including Docker build/smoke (no push), CodeQL, SBOM, benchmark smoke and security scanning. The security job's commit-range/working-tree scan passed; its scheduled full-history TruffleHog step was skipped. / Cả 11 job CI đạt, gồm Docker build/smoke không push, CodeQL, SBOM, benchmark smoke và security scan. Scan phạm vi commit/working tree đạt; bước TruffleHog toàn lịch sử theo lịch bị skip.

### Coverage runs are distinct / Phân biệt các lượt coverage

| Execution / Lượt chạy | C# union coverage / Coverage hợp nhất C# |
|---|---|
| Historical baseline local gate / Gate cục bộ mốc gốc | 70.78% (14,712/20,786) |
| Unchanged pre-push at `dc5e89c1acc8eef03822d1a84eb5a6622a6cb324` / Pre-push nguyên trạng tại SHA mã | 70.77% (14,710/20,786) |
| Hosted CI #315 for the same code tree / Hosted CI cùng tree mã | 70.76% (14,709/20,786) |

All four original commits passed unchanged pre-commit/commit-msg hooks; the code head passed unchanged pre-push before publication. These figures are separate executions, not substituted measurements. / Bốn commit ban đầu đều qua pre-commit/commit-msg nguyên trạng; HEAD mã qua pre-push nguyên trạng. Các số liệu thuộc từng lượt chạy riêng, không thay thế lẫn nhau.

### Remaining limits / Giới hạn còn lại

- Manual Visual Studio navigation acceptance at 1:1 and 42:12, the user's exact VS version/reproduction, and the skipped experimental-instance `Navigate_WhenFileMissing_WritesOutputMessage` scenario remain open. The 1 Windows skip is not a pass. / Chưa nghiệm thu điều hướng VS thủ công, chưa có phiên bản/cách tái hiện chính xác, ca experimental-instance bị skip chưa được coi là đạt.
- No red-before-green Windows execution was established. The subsequent green Windows results do not retroactively prove a baseline regression failure. / Không có bằng chứng Windows đỏ-trước-xanh; kết quả xanh về sau không chứng minh ngược rằng hồi quy đã chạy thất bại trên mã gốc.
- Five Linux junction tests and the live GitHub alert API test were skipped; local act/Git Bash and the full-history secret scan were not run. Hosted Docker and disposable LiveDB success do not change those limits. / Năm test junction Linux và test API alert bị skip; chưa chạy act/Git Bash cục bộ hoặc scan secret toàn lịch sử.
- No production database or real-token exploit testing, merge, release, deployment or protection change was performed. WI-02/WI-05 remain partial. / Không kiểm thử khai thác với DB sản xuất/token thật, không merge/phát hành/triển khai/đổi bảo vệ; WI-02/WI-05 vẫn chỉ xử lý một phần.

### Advisory policy / Chính sách advisory

Microsoft's [GHSA-846f-f764-76mx](https://github.com/dotnet/runtime/security/advisories/GHSA-846f-f764-76mx) and [GHSA-2fmv-rv2c-59x7](https://github.com/dotnet/runtime/security/advisories/GHSA-2fmv-rv2c-59x7) specify stable patched floors 8.0.2, 9.0.21 and 10.0.13. The test accepts later patches only within these reviewed major/minor lines; unknown lines, prereleases and malformed values fail closed pending review. / Chỉ chấp nhận patch từ ngưỡng trong ba nhánh đã review; nhánh mới, prerelease và giá trị sai bị từ chối chờ review.
