# SQL Server describe reconciliation / Đối chiếu bản sửa SQL Server

## Publication preparation / Chuẩn bị công bố

- Fresh publication base: `edf7314b6779a002f321d27305f9d2959d3281cb`.
- Publication branch: `fix/sqlserver-describe-contract`.
- The user has authorized publication of the four reviewed patches with manual PR merge. The parent coordinator owns commits, pushes and PR publication; this delegated review performs local checks only.
- The SQL Server source and two tests retain the reviewed correction. No database/image attempt is authorized in this review. Real SQL Server behavior remains unverified here.
- Earlier reconciliation results below belong to their recorded base and are not fresh execution evidence for this publication base. Full publication gates are owned by the parent.

Người dùng đã cho phép công bố bốn bản sửa đã review, merge PR thủ công. Parent phụ trách commit, push và tạo PR; lượt review này chỉ kiểm tra local. Base công bố là `edf7314b6779a002f321d27305f9d2959d3281cb`, nhánh `fix/sqlserver-describe-contract`. Không thử database/image; chưa xác minh SQL Server thật. Số đo cũ bên dưới thuộc base đã ghi, không thay thế bằng chứng chạy mới trên base công bố.

### Fresh publication validation / Kiểm tra mới trước công bố

- Source review: no blocking finding; provider and both test files are byte-identical to the reviewed reconciliation patch (`sqlserver-source-review.json`).
- Focused tests on the publication base: 18 passed, 0 failures, 0 skips, Release, SDK 9.0.319 (`sqlserver-focused.log`, `sqlserver-tests/focused.trx`).
- Full `dotnet build DataGuard.sln --configuration Release` on the publication base passed with 0 warnings and 0 errors (`sqlserver-full-build.log`, exit 0). This Linux build does not validate Windows IDE/VSIX behavior.
- Documentation synchronization and whitespace checks passed. The first test attempt could not start because the shared SDK path was absent; the parent restored the checksum-verified pinned toolchain before the successful run (`sqlserver-focused-setup-blocker.log`).
- Fresh evidence directory: `/workspace/.tmp/risk-maintenance/publication/`.
- No real SQL Server test or Docker/image attempt ran. No fresh coverage percentage is claimed from this focused run. The parent retains responsibility for publication hook gates and manual PR merge.

Review source không có vấn đề chặn; provider và hai file test giống hệt bản đã review. Trên base công bố, 18 test Release đạt, không lỗi hoặc bỏ qua, SDK 9.0.319. Build toàn bộ `DataGuard.sln` Release trên base công bố đạt, 0 warning và 0 error; build Linux không xác minh Windows IDE/VSIX. Đồng bộ tài liệu và whitespace đạt. Lần thử đầu bị chặn do thiếu SDK; parent đã khôi phục toolchain đã kiểm checksum trước lượt chạy thành công. Không chạy SQL Server thật hoặc Docker/image, không tuyên bố coverage mới từ lượt test tập trung này. Parent phụ trách gate công bố và merge PR thủ công.

## Original local scope / Phạm vi local ban đầu

- Exact base: `5927edd852a71177bf1b068da66972153e3a9af3` (`origin/main` observed locally).
- Isolated branch: `local/sqlserver-reconcile-20261016`.
- Reconcile the original SQL Server describe patch only; preserve the original worktrees and credential work.
- Local only: no commit, publication, remote writes, Docker/image attempts or live databases.
- Toolchain verified before edits: .NET SDK 9.0.319, Node 22.23.3. Node checks are coordinated separately by the parent.

## Problem and acceptance / Vấn đề và tiêu chí

P1: the provider incorrectly calls `sys.sp_describe_first_result_set` as a table-valued function. Invoke the stored procedure with `EXEC`, preserving SQL as an explicit `nvarchar(max)` parameter, browse mode 0, five-second timeout and existing cancellation/error handling. Offline tests cover command semantics and unchanged short/long Unicode payloads. A gated disposable integration test is supplied but not executed in this task.

P1: provider gọi sai `sys.sp_describe_first_result_set` như hàm trả bảng. Sửa thành `EXEC`, truyền nguyên SQL qua parameter `nvarchar(max)`, giữ browse mode 0, timeout năm giây và xử lý lỗi/cancellation hiện có. Test offline kiểm tra command và payload Unicode ngắn/dài. Test tích hợp container được giữ nhưng không chạy trong nhiệm vụ này.

## Source, tests and docs / Ánh xạ

| Source | Regression | Documentation |
| --- | --- | --- |
| `src/DataGuard.SqlServer.Adapter/SqlServerLiveQuerySchemaProvider.cs` | `tests/DataGuard.Core.Tests/SqlServerLiveDescribeCommandTests.cs` | `docs/guides/sqlserver-live-schema.md` (EN/VI) |
| Same provider / Cùng provider | `tests/DataGuard.Core.Tests/SqlServerLiveDescribeIntegrationTests.cs` (not executed / không chạy) | Same guide / Cùng tài liệu |

## Execution record / Kết quả

Evidence directory: `/workspace/.tmp/risk-maintenance/sqlserver-reconcile`.
Results are recorded below after execution. Compile failures are not counted as red assertion evidence.

README EN/VI remains unchanged: dynamic badges and scope limitations already exist. Local measurements do not prove hosted CI or Codecov ingestion. Marketplace publication pause is preserved. No timing comparisons are claimed while parallel work is active.

README EN/VI không đổi: đã có badge động và giới hạn phạm vi. Số đo local không chứng minh hosted CI hoặc Codecov đã nhận dữ liệu. Giữ nguyên tạm dừng Marketplace. Không so sánh thời gian khi có tác vụ chạy song song.

- Red: compiled successfully; 3 runtime assertion failures, 0 passes (`red.log`, `red/red.trx`). This reproduces original behavior after extracting the internal production-used command-builder seam. It is not a test run on pristine main, which lacks that seam.
- Green: 18 passed, 0 failures/skips, covering command regressions plus existing `LiveSqlShapeValidationTests` (`green.log`, `green/green.trx`). Cancellation handling is unchanged in source; the new command tests do not independently demonstrate cancellation.
- Python: 143 tests run, 1 skipped, success (`python.log`).
- Independent read-only review: no blocking source/test findings; the provider and two test files match the preserved reviewed patch; the guide adds a reconciliation-plan link.

Red biên dịch thành công và có 3 assertion thất bại sau khi tách hàm tạo command giữ nguyên hành vi cũ; không phải chạy test trên main nguyên trạng. Green có 18 test đạt. Python chạy 143 test, bỏ qua 1, thành công. Reviewer độc lập không phát hiện vấn đề chặn trong source/test. Cancellation giữ nguyên trong source, không được chứng minh riêng bằng các test command mới.

### Final local gates / Gate local cuối cùng

| Gate | Result | Evidence |
| --- | --- | --- |
| Unchanged `.githooks/pre-push` | Exit 0; Release build and analyzers 0 warnings/errors; formatting, workflow actionlint, documentation checks and NuGet audit passed | `pre-push.log`, `pre-push.exit` |
| Non-live .NET suites within pre-push | 2,216 passed, 5 Windows-specific skips, 0 failures; five projects, 2,221 total | `full-tests/`, `test-counts.json` |
| Measured production line union | 17,023 / 20,980 = 81.14%; five unique reports, five byte-identical copies preserved and not double-counted | `raw-coverage/`, `production-coverage.json` |
| Unchanged `.githooks/pre-commit` | Exit 0; whitespace, staged topology and documentation checks passed | `pre-commit.log`, `pre-commit.exit` |
| `dotnet build DataGuard.sln --configuration Release` | Exit 0; 0 warnings, 0 errors on this Linux environment | `full-build.log`, `full-build.exit` |
| `python3 -m unittest discover -s scripts/tests` | 143 run, 1 skipped, success | `python.log` |
| Independent review | No blocker; provider/test files match preserved review; red/green evidence inspected | Reviewer handoff recorded in parent task |

Coverage is the union of measured production lines, not coverage of all product modules. Branch totals are retained per report only, with no aggregate branch claim. The initial attempt to validate all ten raw paths correctly rejected duplicate reports; SHA-256-identical copies were then explicitly inventoried before validating the five unique reports. Raw evidence was not rewritten.

Pre-push used its existing `SKIP_ACT=1` default: history secret scanning and Docker/act simulation were not performed here. Despite the generic hook message saying “verified separately”, this task makes no such claim. No live database, editor-host or hosted macOS/Windows CI checks ran. Full-solution build success on Linux does not prove the Windows IDE/VSIX path. A subsequent shared quiet Node run passed 117 tests; see the shared evidence below.

Coverage là hợp các dòng production đã được đo, không phải toàn bộ sản phẩm. Giữ branch theo từng report, không cộng thành tỷ lệ chung. Lần kiểm tra cả mười đường dẫn đã từ chối đúng report trùng; sau đó xác minh SHA-256 và thống kê rõ năm bản sao giống hệt trước khi kiểm tra năm report duy nhất. Không sửa bằng chứng raw.

Pre-push giữ mặc định `SKIP_ACT=1`: chưa chạy quét secret lịch sử hoặc Docker/act. Không coi thông báo chung “verified separately” của hook là bằng chứng đã kiểm tra. Chưa chạy database live, editor-host hoặc hosted CI macOS/Windows. Build solution trên Linux không chứng minh đường Windows IDE/VSIX. Lượt Node chạy riêng sau các gate đạt 117 test; bằng chứng dùng chung được ghi bên dưới.

At the end of the original local reconciliation, the patch remained uncommitted and unpublished. Deferred findings (SQL Server metadata character lengths, empty result sets and broader provider Unknown policy) are outside this isolated correction.

Tại thời điểm kết thúc đối chiếu local ban đầu, bản sửa chưa commit và chưa công bố. Các vấn đề độ dài ký tự metadata SQL Server, result set rỗng và chính sách Unknown rộng hơn nằm ngoài phạm vi bản sửa tách biệt này.

### Shared Node evidence / Bằng chứng Node dùng chung

One quiet run after both .NET gate sequences passed117 tests. Production LCOV:
2737/4228 lines (64.73%),430/500 branches (86.00%). Both reconciliation branches
have identical unchanged TypeScript tree `47bc9e88c44867d76c13ee8c8e690f768355c7c5`;
this is one shared verification, not two independent executions or a coverage
improvement attributed to either C# patch. Evidence:
`/workspace/.tmp/risk-maintenance/sql-unknown-reconcile/node-evidence.json`,
`node-coverage.log`, and `node-lcov.info`.

Một lượt Node riêng sau khi cả hai chuỗi gate .NET kết thúc:117 test đạt,2737/4228
dòng và430/500 nhánh. Hai nhánh có cùng cây TypeScript không đổi; dùng chung một
lượt kiểm chứng, không tuyên bố chạy hai lần hoặc tăng coverage nhờ bản vá C#.
