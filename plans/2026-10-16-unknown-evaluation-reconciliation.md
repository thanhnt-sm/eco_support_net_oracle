# Unknown type evaluation reconciliation / Đối chiếu kiểu chưa xác định

## Scope and acceptance / Phạm vi và tiêu chí

Current publication preparation base: main `edf7314b6779a002f321d27305f9d2959d3281cb`, branch `fix/unknown-type-evaluation`, worktree `/workspace/dataguard-unknown-publish`. The user has authorized publication of the four reviewed patches; the parent owns commit/push/PR operations, and PR merging remains manual. This review performs only local checks. Marketplace publication, release and permission changes remain outside this authorization.

Historical reconciliation base: `5927edd852a71177bf1b068da66972153e3a9af3`, isolated branch `local/unknown-reconcile-20261016`. The original PR63 and reconciliation worktrees are preserved. The earlier local-only publication pause applied to that phase; the new authorization supersedes it only for the reviewed patches.

Tiếng Việt: Chuẩn bị công bố trên base `edf7314b6779a002f321d27305f9d2959d3281cb`; người dùng đã cho phép công bố bốn patch đã review. Agent cha phụ trách commit/push/PR, merge PR vẫn thủ công. Review này chỉ chạy kiểm chứng local; không cho phép Marketplace, release hoặc thay đổi quyền. Giữ nguyên các worktree và bằng chứng giai đoạn local-only trước đó.

P1: provider type tables can return `Unknown`; DG002 and DG018 must not silently equate this with a completed compatible comparison. Reconcile the existing bounded patch, with no dependency or grammar changes. Acceptance: manual/catalog parameter unknown types enter DG002 unevaluated status; live mapped unknown types enter existing DG020 unevaluated status; known compatibility and mismatch behavior remain; a mixed call retains its proven mismatch and its unknown status through the execution engine. `IsIncomplete` remains the separate execution-failure flag, not the unevaluated status flag.

Source-to-test-to-doc mapping:

| Source | Regression | User documentation |
| --- | --- | --- |
| `src/DataGuard.Core/Rules/ParameterTypeMatchRule.cs` | Manual/catalog unknown CLR and database types, compatible/incompatible controls, mixed engine result in `UnknownTypeEvaluationTests.cs` | `docs/guides/type-evaluation.md` (English and Vietnamese) |
| `src/DataGuard.Core/Rules/LiveSqlShapeValidationRule.cs` | Synthetic live schema unknown CLR/database types and compatible/incompatible controls in the same test file | Same bilingual guide |

## Verification plan / Kế hoạch kiểm chứng

1. Apply only the regression tests and reproduce red against exact main.
2. Reconcile production and documentation; run focused green.
3. Run unchanged `.githooks/pre-push` and `.githooks/pre-commit` locally, full `DataGuard.sln` Release build, and Python script tests. No hooks are bypassed. Pre-push retains its normal `SKIP_ACT=1` behavior; its wording does not prove separate act/Docker/secret-scan verification.
4. Preserve raw TRX/Cobertura and logs outside the repository at `/workspace/.tmp/risk-maintenance/unknown-reconcile`. Report production source scope and per-report branch totals without summing unstable branch identities across suites. Node checks belong to the parent after the .NET gates; do not claim performance comparisons from concurrent runs.
5. Independent review before parent packages the local patch.

## Limits / Giới hạn

Missing CLR metadata, missing live type metadata, unresolved procedure bindings and unsupported SQL/EF grammar remain outside this bounded patch. No finding does not prove these cases were evaluated. Tests use synthetic descriptors and a fake schema provider, never a database. Linux compilation does not prove Windows runtime, editor-host, VSIX installation or hosted CI behavior. No local metric proves Codecov ingestion; README EN/VI dynamic badges stay unchanged. No 100% correctness or whole-product coverage claim.

Tiếng Việt (phạm vi và giai đoạn kiểm chứng local trước đây): Chỉ sửa kết quả `Unknown` do bảng kiểu trả về, giữ finding sai kiểu đã xác định và chuyển phần chưa xác định sang trạng thái chưa đánh giá. Các nhánh thiếu metadata và cú pháp chưa hỗ trợ vẫn là giới hạn; không tự coi là hợp lệ. Kiểm chứng red/green và hook nguyên bản tại máy, lưu dữ liệu raw, không công bố hay thay đổi badge từ số đo local. Build Linux không thay thế kiểm chứng Windows/editor/CI. Node do agent cha chạy riêng; không suy diễn hiệu năng từ các lượt chạy đồng thời.

## Historical reconciliation results / Kết quả đối chiếu trước đây

Original regression red: 7 failed, 6 passed (13 total), against exact main before production edits. Expanded red adds an unregistered-provider engine regression: 8 failed, 6 passed (14 total). Focused green after reconciliation: 161 passed, no failures or skips, including all 14 new cases and related stored-procedure/live/type-table tests. Python script suite: 143 tests, one skip, success.

Independent read-only review found no blocking issues. Reviewer feedback added the unregistered-provider regression and clarified the IDE-safe implicit allowance in both languages. The only additional source change is XML documentation wording in `UnknownTypeCompatibility.cs`: no mismatch findings, rather than no reporting at all.

Both unchanged hooks completed successfully (exit 0). Full `DataGuard.sln` Release build: zero warnings, zero errors; this includes compilation, not Windows editor/runtime verification. Portable hook tests: **2,227 passed, five Windows junction tests skipped**, across Core (1,992 passed + five skipped), Analyzers (98), CodeFixes (38), GoldenCorpus (61), and Observability (38). Formatting emitted workspace-load warnings but its unchanged checks passed. Actionlint, documentation inventory, build/analyzers, coverage threshold and dependency audit passed inside the unchanged hook. The parent supplied the shared Node result recorded below.

Local measured production lines: **17,027 / 20,987 (81.13%)**, unioned across five fresh collector reports. The normalizer at main confirms this exact numerator and denominator. This is measured-source coverage, not all production source or all product surfaces. The source manifest lists 176 represented source files: DataGuard.Analyzers: 6; DataGuard.Build: 1; DataGuard.Cli: 33; DataGuard.CodeFixes: 1; DataGuard.Contracts: 2; DataGuard.Core: 98; DataGuard.Host: 5; DataGuard.LanguageServer: 1; DataGuard.MySql.Adapter: 5; DataGuard.Observability: 1; DataGuard.Observability.AspNetCore: 1; DataGuard.Observability.Messaging: 2; DataGuard.Oracle.Adapter: 6; DataGuard.PostgreSql.Adapter: 5; DataGuard.SqlClassification: 3; DataGuard.SqlServer.Adapter: 6. Raw data, normalized data and a JSON manifest are retained locally; no upload occurred. First normalization invocation used the nested raw backup layout and failed the expected-report-count check; rerunning with five separate direct collector directories passed without changing data or script.

| Suite | Raw collector branches (covered/total) | Normalized represented branches (covered/total) |
| --- | --- | --- |
| Analyzers | 724/1,259 | 724/1,259 |
| CodeFixes | 438/1,371 | 436/1,367 |
| Core | 9,462/14,815 | 8,921/13,733 |
| GoldenCorpus | 1,772/14,539 | 1,768/13,457 |
| Observability | 227/362 | 227/362 |

Branch rows are separate scopes, not additive; normalized totals exclude omitted generated source records. Linux runtime tests, source coverage and full solution compilation do not establish Windows/editor-host/VSIX/hosted-CI correctness. No live database, image test, act simulation or verified-secret scan ran. No performance claims are made because other reconciliation gates ran concurrently.

Tiếng Việt: Hai hook nguyên bản và build Release toàn solution đều đạt; build không có warning/error. Có 2.227 test .NET đạt và năm test junction chỉ dành Windows được skip, giữ nguyên giới hạn nền tảng. Coverage dòng đo được 17.027/20.987 (81,13%), chỉ trên nguồn được collector đo; không phải toàn bộ sản phẩm. Giữ riêng số branch từng suite và dữ liệu raw, không cộng branch giữa suite, không upload hoặc suy ra badge/CI đã cập nhật. Kiểm chứng Node do agent cha phụ trách.

Shared Node validation, reported by the parent after both .NET gate runs became idle: **117 tests passed**, 2,737/4,228 lines (64.73%) and 430/500 branches (86%). This was **one execution in the SQL reconciliation worktree**, reused as evidence because both branches retain the identical unchanged VS Code TypeScript tree `47bc9e88c44867d76c13ee8c8e690f768355c7c5`; it was not two independent runs. Evidence: `/workspace/.tmp/risk-maintenance/sql-unknown-reconcile/node-evidence.json` and `node-coverage.log`. This measurement does not validate an editor host or prove whole-extension correctness.

Tiếng Việt: Agent cha chạy Node đúng một lần tại worktree SQL sau khi các gate .NET kết thúc: 117 test đạt, dòng 2.737/4.228 (64,73%), branch 430/500 (86%). Hai nhánh có cùng tree TypeScript không đổi `47bc9e88c44867d76c13ee8c8e690f768355c7c5`, nên dùng chung bằng chứng; không báo thành hai lần kiểm chứng độc lập. Số đo này không thay thế test editor host hay chứng minh đúng toàn extension.


## Publication-base review / Review trên base công bố

Focused regressions on `edf7314b6779a002f321d27305f9d2959d3281cb`: **161 passed, zero failed or skipped**. The full `DataGuard.sln` Release build also passed with zero errors and one recovered MSB3026 file-copy retry warning in Visual Studio resources. An initial invocation could not start because the saved SDK path was missing (exit 127, no tests executed); after the parent restored the official hash-verified SDK 9.0.319, the recorded rerun passed. Logs and TRX: `/workspace/.tmp/risk-maintenance/publication/unknown-focused-rerun.log`, `unknown-focused/unknown-focused.trx`, and `unknown-full-build.log`.

Review found no blocking classification issues. Production, regression-test and EN/VI guide files remain byte-identical to the previous reconciliation; hashes are recorded in `unknown-review-files.json`. Unknown remains unevaluated (DG002 or live DG020), compatible/incompatible verdicts retain their existing behavior, and independently proven mismatches coexist with unknown status. Missing metadata and unsupported grammar remain outside scope. Docs sync and whitespace diff checks pass.

Historical coverage/full-gate numbers above are evidence from the previous base, not new measurements. The parent owns unchanged publication hook gates and hosted verification. This reviewer performed no commit, push, PR action or live database operation.

Tiếng Việt: Trên base công bố mới, 161 test tập trung đạt, không skip; build Release toàn solution đạt với một warning retry sao chép tài nguyên Visual Studio đã phục hồi và không lỗi. Lần gọi đầu không chạy được vì thiếu SDK, không được tính là test; lượt chạy lại sau khi khôi phục SDK đã đạt. Review không thấy lỗi phân loại chặn công bố. Số coverage cũ vẫn là bằng chứng lịch sử, không giả thành số đo trên base mới. Agent cha phụ trách hook công bố và kiểm chứng hosted; reviewer không commit/push/PR hoặc kết nối database.
