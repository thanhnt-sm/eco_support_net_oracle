---
type: scout
date: 2026-09-12
baseline: 93bf7288324dd746669ad09c5e2a592adc772748
status: completed
---
# Tổng hợp audit DataGuard bằng GPT Luna

## Kết luận

Đã hoàn tất điều phối 8 worker `gpt-5.6-luna` (reasoning medium) và đối chiếu **toàn bộ 89 file tracked trong 11 thư mục `src/`** với `plans/`, `research/`, `docs/`. Tổng inventory 307 file: 280 file text/source đã đọc, 27 file generated/binary được phân loại rõ. Không còn source chưa đọc theo ledger. Baseline sạch tại commit `93bf7288324dd746669ad09c5e2a592adc772748`.

Build và test hiện có đều pass, nhưng source còn một số lỗi và khoảng trống so với tài liệu, nổi bật là snapshot drift, config/credential wiring, Oracle query, EF snapshot và Husky hook. Test pass không chứng minh các đường DB/IDE hoặc mọi claim tài liệu đều hoạt động.

Đầu ra chỉ gồm plan/báo cáo; không sửa production, test, dependency lockfile, workflow hoặc plan lịch sử; không commit/push/publish. Cách tổ chức artifact theo `ck:project-organization`; các phase được scaffold/cập nhật bằng `ck:plan` CLI. Gói thực dùng là `claudekit-cli@4.5.2` qua npm cache vì gói `claudekit` không cung cấp executable `ck` trong môi trường này.

## Kết quả thực chạy

| Kiểm tra | Kết quả |
|---|---|
| Restore locked-mode + build Release | PASS; 0 warning, 0 error |
| C# solution tests | **484 passed**, 0 failed, 0 skipped theo VSTest: Core 442, GoldenCorpus 25, Analyzers 5, CodeFixes 12 |
| VS Code compile/test | **2 passed**, 0 failed |
| Docs-sync | PASS; validator chỉ kiểm tra file tồn tại, không chứng minh nội dung khớp |
| Dependency audit VS Code | 3 cảnh báo transitive **dev-only** qua `@vscode/vsce`: 2 high, 1 moderate; production-only audit 0 |
| Probe assessment sibling lockfile | Nhận diện đúng lockfile, phát DG1202 như dự kiến; bác bỏ nghi vấn CS-03 |
| Visual Studio build/runtime | Chưa xác minh: cần Windows/MSBuild/VSSDK |
| Database integration thật | Chưa xác minh: SQL Server fixtures có nhánh return sớm vẫn được tính Passed; không có bằng chứng assertion DB đã chạy |

Môi trường: macOS arm64, .NET SDK 9.0.310, Node 24.19.0. Chi tiết lệnh/exit code/raw output tại [verification](verification.md). Không đo coverage hoặc benchmark mới; không dùng số test/coverage từ tài liệu cũ thay kết quả trên.

## Các vấn đề cần ưu tiên

P1 = ưu tiên xử lý vì ảnh hưởng contract hoặc hành vi người dùng; P2 = bổ sung wiring, độ chính xác tài liệu/test; P3 = maintenance/lịch sử. Độ tin cậy cao dưới đây chủ yếu dựa trên code và caller đã đọc; các lỗi DB chưa được tái hiện với DB thật.

| Ưu tiên | Phát hiện đã đối chiếu | Tác động và bằng chứng |
|---|---|---|
| P1 | `snapshot diff` hash lại snapshot đã lưu | Với schema/hash nhất quán, có thể báo không drift dù DB đã đổi. `src/DataGuard.Cli/Program.cs:413-425`; CI-01 và CS-01 là cùng một lỗi. [CLI/IDE](cli-ide.md), [Core services](core-services.md) |
| P1 | Connection config bị ghi đè và env credential chưa nối vào CLI | Một số command gán connection option null vào config; parser nhận ConnectionString trực tiếp thay vì resolver được docs mô tả. `Program.cs:195-200,247-260,374-379,574-590`. [CI-02/03](cli-ide.md) |
| P1 | Husky hook ghi đường dẫn thay nội dung script | `WriteAllTextAsync(hookPath, hookPath, ...)` thay vì hookContent. `src/DataGuard.Cli/Hooks/PreCommitHookInstaller.cs:199-203`. [CI-04](cli-ide.md) |
| P1 | Oracle query trộn placeholder `@packageName` với `:packageName` | Có đường gọi live từ CLI; cần sửa/xác minh với Oracle thật. `src/DataGuard.Oracle.Adapter/OracleReaders.cs:58,196`. [AD-01](adapters.md) |
| P1 | Một số rule/reader adapter chưa đi hết CLI | MySQL MY003/MY005–MY007, PostgreSQL PG004/PG005 chưa đăng ký; live PostgreSQL thiếu schema source cho length check; Oracle normal validate tạo result columns rỗng. Snapshot offline PostgreSQL là nhánh khác, không gộp thành “mọi CLI đều thiếu schema”. [AD-02/03/04](adapters.md) |
| P1 | EF design-time chọn file C# rồi parse JSON | ModelSnapshot.cs thật không đi qua parser như tài liệu “no build required”; fallback cần assembly đã build. `src/DataGuard.Core/Sources/EfModelSource.cs:215-244,585-675`. [CE-01](core-engine.md) |
| P1 | Credential store non-Windows có thể ghi plaintext nhưng báo encrypted | Khi caller bật EncryptConnectionStringAtRest, encryption chỉ chạy Windows nhưng metadata flag vẫn true. Không khẳng định CLI mặc định đã dùng đường store này. `src/DataGuard.Core/Security/CredentialManager.cs:87-100`. [CS-07](core-services.md) |
| P1 | Một số advertised code-fix IDs không có action | Provider đăng ký ID nhưng switch không xử lý; 12 test CodeFixes chủ yếu kiểm tra metadata/FixAll. `src/DataGuard.CodeFixes/CodeFixProviders.cs:30-85`. [TL-01/06](tooling.md) |
| P2 | Public pipeline, graph và rule semantics còn lệch | Public pipeline tuần tự dù config concurrent; missing-dependency placeholder không bị phát hiện; nullable rule bỏ qua IsNullable; concurrent cap cắt kết quả không có signal. [CE-02/04/06/09](core-engine.md) |
| P2 | Tài liệu mô tả rộng hơn capability | Health endpoints chưa có host/route; DependencyHealth chỉ local lock checks, chưa CVE/health score; docs nói 12 providers nhưng có 3; nhiều IDE settings/real-time behavior chưa có. [Docs](docs.md), [Tooling](tooling.md), [CLI/IDE](cli-ide.md) |
| P2 | Dependency tooling và các public API cần kiểm tra thêm | 3 advisory dev-only; direct StreamingSarifSink overload chưa sanitize như default emitter; path containment reader dùng prefix. Không mô tả thành leak của mọi CLI output. [Verification](verification.md), [CS-04/09](core-services.md) |
| P3 | Lịch sử và generated assets | 20 `.pyc` tracked được phân loại, giữ nguyên; các tài liệu EcoSupport đã archived/superseded không bị coi là lỗi DataGuard. [Plans/research](plans-research.md) |

## Đánh giá theo module

| Module | Mức khớp chính |
|---|---|
| DataGuard.Core | Có implementation rộng; design-time EF, public pipeline, drift, một số semantics/security cần xử lý |
| DataGuard.Cli | Command surface có thật; config/env, offline docs, snapshot diff và hook còn vấn đề |
| DataGuard.SqlServer.Adapter | Parser thực ở Core và CLI gọi được; không phải adapter thiếu implementation |
| DataGuard.Oracle.Adapter | Có readers/rules; query, byte/char metadata và REF CURSOR wiring còn gap |
| DataGuard.MySql.Adapter | Có implementation; một số rules chưa được CLI đăng ký |
| DataGuard.PostgreSql.Adapter | Có implementation; live schema source và một số rules chưa nối đầy đủ |
| DataGuard.Contracts | Attributes/conventions và lightweight layering có thật |
| DataGuard.Analyzers | Analyzer/generator nhẹ có thật; không đồng nghĩa mọi advertised descriptor đều được emit |
| DataGuard.CodeFixes | Có 3 exported providers; advertised IDs/actions và docs chưa khớp |
| DataGuard.VSCode | Run/Cancel → CLI → SARIF có thật; 2 helper tests pass, UI/process integration chưa xác minh |
| DataGuard.VisualStudio | Static runner có thật; build/UI Windows chưa xác minh, docs settings overclaim |

## Bàn giao và bước tiếp theo

1. Xử lý nhóm P1 kèm test chứng minh trigger cụ thể; ưu tiên snapshot drift, config/env và Oracle trước các claim triển khai rộng.
2. Thêm command-level/reader-to-rule/Roslyn-action tests vào các khoảng trống đã xác định; SQL Server integration cần báo Skip/Fail rõ thay vì return như pass.
3. Đồng bộ tài liệu Việt–Anh với capability thực tế, phân biệt schema diff với violation fallback, provider với action, static code với runtime evidence.
4. Kiểm tra Visual Studio trên Windows và DB-backed paths ở môi trường phù hợp. Marketplace/NuGet publish, benchmark, coverage mới và cleanup generated assets là việc riêng, chưa thực hiện trong audit.

Tài liệu tra cứu: [plan](../plan.md), [inventory 307 file](inventory.md), [ma trận traceability](traceability.md), [verification](verification.md). Tám báo cáo worker: [Core engine](core-engine.md), [Core services](core-services.md), [Adapters](adapters.md), [Tooling](tooling.md), [CLI/IDE](cli-ide.md), [Docs](docs.md), [Plans/research](plans-research.md), [Verification](verification.md).

Các kết luận đã chỉnh sau review: bác bỏ lỗi sibling lockfile bằng probe; không coi Snapshot + baseline overlay hoặc fail-on-drift opt-in là bug tự động; thu hẹp streaming redaction về đúng overload; gộp snapshot finding trùng; không coi metadata hay test pass là bằng chứng DB/IDE/release.
