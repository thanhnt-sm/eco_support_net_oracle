# Analyzer constant Concat integration / Tích hợp Concat vào analyzer

> Historical branch evidence / Bằng chứng của nhánh nguồn: test totals, ancestry and performance below describe the original slice, not the combined build. See [local integration baseline](2026-10-20-local-integration-baseline.md) for the integrated scope; do not add these test totals. Các số đo dưới đây không phải benchmark của HEAD tích hợp.


## Ancestry before changes / Quan hệ nhánh trước sửa

All five preserved commits are independent children of main `39a030e1a99b75e92d8bf68f8bdc62428ac5923e`:

| Commit | Branch | Scope |
|---|---|---|
| ad3eb30bad523643c348b4ecf5e81e3337be948c | fix/scorecard-bounded-recovery | Scorecard workflow |
| 2f6e315feb01c4a515576f30d9f2904e345d78ae | fix/vs-output-runtime-tests | VS output tests |
| 6f038cdfc86aed6cacb019d944e3e4676fc5054a | fix/lsp-protocol-regressions | LSP lifecycle/ranges |
| a4c9869ec557eafecdc7bcb22e8bc33480306f41 | fix/classifier-prefix-and-identifiers | lexical classifier |
| bb7670fdc5e4fb49c0e095e387aa3dc3fada6c0c | fix/csharp-sql-shape-slice | Core constant Concat |

New branch `fix/analyzer-constant-concat` starts at bb7670fd. It includes ONLY that parent feature; it does not integrate the other four siblings. Test totals across those branches must not be added together or presented as one integrated build. All prior worktrees were preserved.

## Integration audit / Đường đi hiện tại

Visual Studio Run Validation/validate-on-build uses DataGuardPackage.Commands.cs -> CliArgumentBuilder.BuildValidateArguments (--project, --ide-safe) -> CLI ValidateCommand/Core project source -> SARIF/progress. VS Code Run Validation uses command-args.ts/extension.ts -> same CLI flags -> SARIF and summary.json -> Problems/query tree. A rebuilt matching CLI containing bb7670fd already makes Concat visible in command validation. Assess is environment assessment, not this SQL extraction path.

Typing diagnostics use the separately bundled netstandard2.0 Roslyn analyzer (Roslyn4.12), while Core is net9/Roslyn5.9. VS Code bundled LSP references only dependency-free SqlClassification and scans literals. Referencing Core at runtime from analyzer/LSP is inappropriate and would change deployment/dependency expectations.

## Bounded slice / Phạm vi

Reuse one source-owned pure constant-Concat evaluator through an explicit analyzer Compile link, without adding a runtime assembly/dependency or duplicating logic. Core retains its source-map adapter. Analyzer resolves only a direct Concat expression at an already recognized EF raw-SQL call site when its existing syntax resolver has no text. Existing suppression and unsupported/dynamic handling remain. Dapper typing, local dataflow, generator diagnostics and LSP composition remain out of scope.

Red/green acceptance: real analyzer diagnostics for DG017 and supported EF missing/present-column cases; aliases/named/nested args and negatives for dynamic/custom/object overloads. Location covers the composed expression. DG017 codefix must not edit one contained fragment as if it were the full SQL. Core's27 tests must continue passing, and a real CLI process exercises the extension-equivalent ide-safe invocation without credentials/database. No installed editor-host claim.

Source-sharing trade-off: the common evaluator must compile against both pinned Roslyn versions; packaging/analyzer checks must retain4.12 and no Core runtime dependency. This is a small source boundary, not a general extraction architecture rewrite. No push, /dev/shm retry, live DB or Marketplace.

## Priority and evidence / Ưu tiên và bằng chứng

- P1: missing typing diagnostics for direct EF constant Concat. Before integration, four positive analyzer cases fail; six existing/negative cases pass. Share the evaluator and assert exact diagnostic locations and mapped-column behavior.
- P1: composed-expression DG017 can offer a fragment-only rewrite. One red codefix regression demonstrates the unsafe action; the fix withholds it while preserving literal quickfixes.
- P2: command parity is observable through a real ide-safe CLI process: located DG004 SARIF plus flattened SQL in summary.json, using extension-equivalent arguments. This confirms the existing parent feature path, not a new UI-host implementation.
- P2 backlog: Dapper projection confidence, local-variable flow, generator and LSP composition parity require separate designs. No diagnostic is not proof of validation.

## Independent review and resource characterization / Review và tài nguyên

Independent reviewers examined supported/unsupported boundaries, source mapping, codefix safety, cancellation, state lifetime and actual analyzer workloads. No persistent cache, queue, application-code execution or retained Compilation/SemanticModel was added. Context-owned semantic binding occurs only for direct recognized EF Concat candidates; the helper keeps depth10/256KiB bounds and cancellation checks on recursive entry. Roslyn owns scheduling/concurrency; no new UI-thread dispatch is introduced. This does not prove UI responsiveness or mid-flight cancellation latency.

Actual analyzer baseline (bb7670fd) versus this slice, five repeats and five comment edits each, synthetic10/100/1000 calls. Baseline emits0 diagnostics; after emits10/100/1000 respectively. Repeat allocation medians:131344→176784,582040→934192,4602912→8545768 bytes. Edit wall medians:7.373→19.991,34.964→100.209,96.127→128.190ms. Edit process CPU medians:8.134→11.858,46.567→104.578,128.751→210.292ms. Additional supported diagnostics cost work; no speedup claim. Shared-host scheduling produced broad ranges (100-call after edit38–467ms), so no timing gate. Parsing and upfront binding are excluded; driver/analyzer creation and diagnostic execution included. Allocation is process-wide allocated bytes, not retained memory. Peak RSS/retained heap and installed IDE latency were not measured.

Malformed, oversized and depth12 cases returned no invented diagnostics or AD0001; pre-cancellation was observed. Deterministic tests assert semantics and diagnostic count/location, not timing. External reproducible harness and raw evidence: `/workspace/.tmp/risk-maintenance/analyzer-concat/perf-*`, `Program.cs`, `Harness.csproj`.

VI: Nhánh mới chỉ kế thừa bb7670fd, không gộp bốn patch độc lập còn lại. Chi phí tăng do analyzer nay thực hiện thêm kiểm tra và tạo diagnostic; không tuyên bố tối ưu tốc độ. Không có cache/queue mới hoặc giữ SemanticModel giữa các lần phân tích. Đã kiểm tra input lỗi, giới hạn kích thước/độ sâu và token hủy sẵn; chưa đo độ trễ hủy giữa chừng, bộ nhớ giữ lại hoặc editor host thực tế. Canonical coverage vẫn bị chặn bởi named mutex tại /dev/shm; không thử lại quyền đã hủy. Không push/phát hành, không truy cập DB.

## Local gates / Gate cục bộ

Release full-solution build:0 warnings/errors. CrossPlatform ordinary tests with Category!=LiveDb:2353 passed,5 Windows-junction skips (Core2106, Analyzer109, CodeFix39, Golden61, Observability38). Node189 passed. CrossPlatform format verification, docs sync, actionlint, workflow policy and diff whitespace checks pass. Package consumer on SDK9.0.100/Roslyn4.12 loads analyzer, reports DG017 for Concat and existing DG001, rejects runtime analyzer DLL leakage; no new package dependencies. Windows assemblies compile but editor-host/runtime tests are not run here. Canonical instrumented coverage remains blocked by the previously observed named-mutex permission failure; no rerun or coverage percentage claimed.
