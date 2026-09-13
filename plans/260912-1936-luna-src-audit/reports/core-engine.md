# Audit Luna — DataGuard.Core (2026-09-12)

Phạm vi: baseline `93bf7288324dd746669ad09c5e2a592adc772748`; chỉ đọc source, caller và test. Không chạy test/build theo phân công. Đối chiếu chính: `docs/03-components/core/*`, `plans/ACTIVE_SESSION_REGISTER.md`, `plans/2026-08-21-review-handoff.md`, `plans/adr/002-core-dependency-scope.md`, `research/muc_tieu/{1,2,3,4}.md`. `plans/master-plan.md` và `plans/implementation-plan.md` được coi là superseded.

## Kết luận ngắn

CLI có wiring Full/Snapshot/Manual và nhánh concurrent thật ở `src/DataGuard.Cli/Program.cs:940-1052`; public `ValidationPipeline` lại chạy tuần tự và không áp dụng các source/mode. Design-time EF snapshot path không chứng minh được: `FindModelSnapshot` chọn `*ModelSnapshot.cs`, nhưng parser lại parse JSON. SQL Server parser có ground-truth live nhưng không lọc hidden result columns. Metadata package vẫn claim “Zero vendor dependencies”, trái ADR-002 đã accepted.

## Findings

### CE-01 — P1 — confidence cao — Design-time ModelSnapshot không chạy như tài liệu

- **Class:** `chưa khớp` / `tài liệu lệch`.
- **Evidence:** `EfModelSource.FindModelSnapshot` tìm file `*ModelSnapshot.cs` (`src/DataGuard.Core/Sources/EfModelSource.cs:654-670`); `ExtractFromDesignTimeAsync` chuyển path đó vào `ExtractFromModelSnapshotAsync` (`:591-605`), hàm đọc toàn bộ C# rồi gọi `JsonNode.Parse` (`:215-226`). Parser chỉ hiểu JSON giả lập (`:232-335`), không phải C# EF migration snapshot.
- **Caller/test:** caller duy nhất được tìm thấy là CLI không gọi API design-time này; test chỉ dùng JSON synthetic (`tests/DataGuard.Core.Tests/SourceAndBaselineTests.cs:185-237`). Khi snapshot thật tồn tại, parse rỗng rồi fallback `bin/**/*.dll` (`:608-651`), nên không còn “no build required”.
- **Docs:** `docs/03-components/core/sources.md:1-105` và bản `.vi.md` mô tả ModelSnapshot.cs được parse trực tiếp; `docs/02-architecture/design-philosophy.md:32` cùng claim.
- **Recommendation:** parse C# snapshot bằng Roslyn/EF design-time services hoặc đổi contract thành JSON snapshot thực sự; thêm fixture là file ModelSnapshot.cs thật và assert đường snapshot được dùng, không chỉ assert JSON helper.
- **Unresolved:** chưa có runtime evidence với DbContext migration thật; cần test dedicated.

### CE-02 — P2 — confidence cao — Public `ValidationPipeline` bỏ qua concurrent engine/config

- **Class:** `chưa khớp`.
- **Evidence:** constructor nhận `EnableConcurrentValidation`, `MaxDegreeOfParallelism`, `MaxViolationQueueSize` qua config nhưng chỉ tạo graph/telemetry/credential/logger (`src/DataGuard.Core/PublicApi/PublicApiSurface.cs:51-66`). `ValidateAsync` luôn nested-loop tuần tự `rule × contract` (`:124-142`), không gọi `ConcurrentValidationEngine`.
- **Caller/test:** CLI gọi engine thật khi config bật (`src/DataGuard.Cli/Program.cs:1024-1044`); public API tests chỉ kiểm tra có result (`tests/DataGuard.Core.Tests/PublicApiAndPipelineTests.cs:18-45`), không assert bounded parallelism hay false khi tắt.
- **Docs:** `docs/03-components/core/validation.md` và `.vi.md` nói pipeline dùng engine nội bộ; `docs/01-overview/feature-showcase.md:151` nói rule độc lập chạy song song.
- **Recommendation:** inject/use `ConcurrentValidationEngine` theo config hoặc sửa docs/public contract; thêm test spy/custom rule chứng minh bật/tắt config thực sự đổi execution path.
- **Unresolved:** giữ thứ tự dependency giữa các rule khi chạy concurrent cần quyết định rõ; CLI hiện chạy toàn bộ rule list concurrent.

### CE-03 — P2 — confidence cao — Core package metadata vẫn claim zero vendor dependency

- **Class:** `tài liệu lệch` / `chưa khớp ADR`.
- **Evidence:** `src/DataGuard.Core/DataGuard.Core.csproj:7` ghi `Zero vendor dependencies`, trong khi chính csproj `:24-41` có AWSSDK, EF Core, SqlClient, ScriptDom, Roslyn, YamlDotNet và nhiều Microsoft.Extensions; `packages.lock.json:1-606` ghi direct/transitive graph tương ứng.
- **Docs/ADR:** `plans/adr/002-core-dependency-scope.md` D1 nói claim đã được loại bỏ và Core giữ vendor dependencies; `plans/2026-08-21-review-handoff.md:39-70` ghi cùng kết luận/đối chiếu red-team.
- **Recommendation:** sửa Description/package metadata thành mô tả engine có provider/cloud dependencies; đồng bộ docs/grant narrative, rồi kiểm tra generated NuGet metadata.
- **Unresolved:** việc tách adapter/driver khỏi Core là backlog kiến trúc, không kết luận cần làm trong audit này.

### CE-04 — P2 — confidence cao — RuleDependencyGraph không thể báo missing dependency

- **Class:** `một phần`.
- **Evidence:** `RegisterRule` tự tạo placeholder cho mọi dependency chưa đăng ký (`src/DataGuard.Core/Rules/RuleDependencyGraph.cs:40-54`); `Validate` kiểm tra `_nodes.ContainsKey(depId)` (`:146-155`), nên dependency typo đã luôn tồn tại dưới dạng node null và chỉ bị bỏ qua lúc execution (`:92-103`).
- **Caller/test:** `BuiltInRuleDependencies.CreateDefault` cố ý dùng `DG101` placeholder rồi đăng ký rule thật (`src/DataGuard.Core/Rules/RuleDependencyGraph.cs:340-366`); tests chỉ assert default graph valid/order (`tests/DataGuard.Core.Tests/RuleDependencyGraphTests.cs:1-110`), chưa chứng minh missing dependency warning.
- **Docs:** `docs/03-components/core/rules-engine.md` claim “missing dependency” được validate/warn.
- **Recommendation:** phân biệt declared dependency với registered implementation; `Validate` phải warn/error nếu node vẫn null, đồng thời giữ placeholder hợp lệ cho dependency declaration có chủ đích.
- **Unresolved:** chính sách missing dependency (error hay warning) cần owner chốt.

### CE-05 — P2 — confidence trung bình — SQL Server result parser chưa chứng minh xử lý hidden columns

- **Class:** `chưa đủ bằng chứng` / `một phần`.
- **Evidence:** comment xác nhận cột 0 là `is_hidden` (`src/DataGuard.Core/Sources/SqlServerParsers.cs:147-150`), nhưng vòng đọc chỉ lấy cột 2–8 (`:155-173`) và không đọc/lọc `reader.GetBoolean(0)`. Điều này có thể đưa hidden metadata columns vào `ResultColumns`, nhưng hành vi đó phụ thuộc row shape/version của `sp_describe_first_result_set`; không kết luận là runtime bug nếu supported query luôn trả hidden=false.
- **Caller/test:** `SqlServerStoredProcedureParser.ExtractContractsAsync` gọi `GetResultColumnsAsync` cho từng proc (`src/DataGuard.Core/Sources/SqlServerParsers.cs:45-72`). Hai integration surfaces (`tests/DataGuard.Core.Tests/SqlServerIntegrationTests.cs:44-75`, `tests/DataGuard.Core.Tests/SqlServerParserIntegrationTests.cs:46-76`) early-return khi Docker thiếu (`:49`, `:51`), nên bộ test hiện tại không cung cấp runtime SQL Server proof; không có hidden-column fixture trong targeted `rg` coverage.
- **Docs:** `docs/03-components/adapters/sqlserver-adapter.md:117-138` mô tả discovery/result-set columns và mapping; không nêu hidden filtering.
- **Recommendation:** nếu provider contract yêu cầu, đọc `is_hidden`/`column_ordinal` và skip hidden/invalid ordinal; bổ sung runtime fixture khi SQL Server container khả dụng. Nếu không, ghi rõ assumption supported result shape.
- **Unresolved:** cần verify chính xác provider shape của cột hidden trên supported SQL Server versions.

### CE-06 — P2 — confidence cao — NullableMismatchRule bỏ qua `PropertyDescriptor.IsNullable`

- **Class:** `một phần`.
- **Evidence:** rule lấy `hasRequired` từ annotation key đúng literal `"Required"` (`src/DataGuard.Core/Rules/ContractRules.cs:429-432`) và không dùng `prop.IsNullable`; vì vậy EF metadata non-nullable không có annotation Required bị coi là nullable, còn nullable property có annotation khác không được phản ánh.
- **Caller/test:** `EfModelSource` đã trích `property.IsNullable` vào `PropertyDescriptor` (`src/DataGuard.Core/Sources/EfModelSource.cs:58-82`), nhưng rule discards it. Tests rule hiện dùng schema/annotations, chưa kiểm tra EF metadata path (`tests/DataGuard.Core.Tests/RulesEngineTests.cs` và source tests).
- **Docs:** `docs/03-components/core/rules-engine.md` mô tả so entity nullability với DB; `docs/03-components/core/abstractions.md` mô tả trường `IsNullable` là contract metadata.
- **Recommendation:** dùng `prop.IsNullable` làm source chính, annotations chỉ bổ sung explicit override; map schema theo table thay vì dictionary global nếu nhiều bảng trùng tên cột.
- **Unresolved:** semantics C# nullable reference types versus EF `IsNullable` cần test matrix.

### CE-07 — P2 — confidence cao — RawSqlParser nuốt parse errors và gắn mọi parameter là Input

- **Class:** `một phần` / `chưa đủ bằng chứng cho call-site validation`.
- **Evidence:** ScriptDOM trả `errors` nhưng kết quả không được kiểm tra (`src/DataGuard.Core/Sources/SqlServerParsers.cs:205-214`); parser vẫn trả `RawSqlDescriptor` cho SQL malformed (`:231-241`). Visitor chỉ override `Visit(ProcedureParameter)` (`:245-253`), nên lấy parameter khai báo trong `CREATE PROCEDURE`, không chứng minh được argument/call-site CLR type; mọi output `ParameterDescriptor.Direction` bị set Input (`:215-223`).
- **Caller/test:** test chỉ dùng `CREATE PROCEDURE` declaration và assert 4 declarations (`tests/DataGuard.Core.Tests/SourceAndBaselineTests.cs:117-151`); `ParameterTypeMatchRule`/`ParameterDirectionRule` skip khi thiếu `ClrType`/`CallSiteDirection` (`ContractRules.cs:152-169`, `:231-244`).
- **Docs:** `docs/03-components/core/sources.md:152-185` và `docs/03-components/core/rules-engine.md` mô tả raw SQL parser/parameter validation nhưng không giới hạn rõ declaration-only coverage.
- **Recommendation:** expose parse diagnostics hoặc fail/mark invalid input; parse invocation arguments separately and attach call-site metadata, nếu không thì ghi rõ rules là unavailable/insufficient-ground-truth thay vì gọi là full validation.
- **Unresolved:** cần thiết kế contract giữa Roslyn analyzer và Core cho CLR type/direction.

### CE-08 — P2 — confidence trung bình — Public API không áp dụng smart defaults

- **Class:** `một phần`.
- **Evidence:** `DataGuardConfiguration.Default()` khởi tạo excluded lists và smart flags (`src/DataGuard.Core/Models/Configuration.cs:84-94`), `WithSmartDefaults` là extension (`:101-145`), nhưng `DataGuardApi.CreatePipeline()` tạo thẳng `new DataGuardConfiguration()` (`src/DataGuard.Core/PublicApi/PublicApiSurface.cs:40-44`) và constructor lưu config nguyên trạng (`:60-63`), không gọi extension.
- **Caller/test:** CLI có config-building riêng; public test chỉ tạo pipeline mặc định và entity (`tests/DataGuard.Core.Tests/PublicApiAndPipelineTests.cs:18-45`). Không có test chứng minh provider auto-detection/default list qua public API.
- **Docs:** `docs/03-components/core/auto-detection.md` mô tả config sẵn sàng cho `DataGuardApi.CreatePipeline(config)`; `docs/03-components/core/public-api.md` mô tả default fluent pipeline.
- **Recommendation:** quyết định rõ `CreatePipeline` có normalize config không; nếu có, gọi `WithSmartDefaults` và `DataGuardConfiguration.Default`, nếu không sửa docs/guard null semantics.
- **Unresolved:** auto-detection có thể cần side effects/assembly scan nên owner phải chốt boundary.

### CE-09 — P2 — confidence cao — Concurrent engine silently drops violations at configured cap

- **Class:** `khớp hành vi tài liệu` nhưng `rủi ro contract chưa đủ rõ`.
- **Evidence:** khi `Interlocked.Increment` vượt `MaxViolationQueueSize`, worker `return` bỏ qua violation (`src/DataGuard.Core/Validation/ConcurrentValidationEngine.cs:45-54`); kết quả sau đó chỉ `Take` và sort (`:57-60`). Không có cờ `truncated`, dropped count, hoặc phân biệt “validation sạch” với “kết quả bị giới hạn”.
- **Caller/test:** CLI dùng engine khi concurrent bật (`src/DataGuard.Cli/Program.cs:1024-1044`); targeted tests ở `tests/DataGuard.Core.Tests/CoverageExpansionTests.cs:31-58` kiểm tra giới hạn kích thước, nhưng không assert caller được cảnh báo khi lỗi bị drop. Không suy luận runtime loss ngoài code path này.
- **Docs:** `docs/03-components/core/validation.md:133` và `.vi.md:133` thừa nhận drop im lặng, nhưng không mô tả tác động đến exit/error semantics.
- **Recommendation:** trả metadata truncation/dropped count hoặc fail closed khi cap chạm lỗi severity Error; ít nhất log/diagnostic rõ để CI không coi danh sách bị cắt là đầy đủ.
- **Unresolved:** owner cần chốt cap là safety limit hay correctness-preserving queue; nếu safety limit, định nghĩa exit semantics khi truncated.

## Mapping theo yêu cầu

| Surface | Verdict | Evidence |
|---|---|---|
| Full/Snapshot/Manual | `khớp một phần` | CLI `Program.cs:940-1008`; public Core API chỉ nhận contracts, không build source/mode |
| DG rule graph | `khớp một phần` | graph topo thật `RuleDependencyGraph.cs:63-107`; public pipeline dùng graph nhưng CLI không dùng graph; missing dependency check vô hiệu |
| SQL Server parser | `khớp một phần` | live catalog + `sp_describe_first_result_set` thật; hidden columns/parse-call-site gaps |
| Public API | `khớp một phần` | facade/fluent/baseline thật; concurrent + smart defaults không wiring |
| Concurrent engine | `khớp` ở CLI, `tài liệu lệch` ở public API | CLI `Program.cs:1024-1044`; pipeline `PublicApiSurface.cs:124-142` tuần tự |
| Violation queue cap | `khớp một phần` | engine có cap/drop đúng docs; chưa có truncation signal/error semantics |
| Vendor dependency scope | `tài liệu lệch` | csproj + lock trái ADR-002 D1 |

## Coverage ledger (đã đọc đầy đủ theo file/chunk)

| File | Coverage |
|---|---|
| `src/DataGuard.Core/Abstractions/Contracts.cs` | full, 1-199 |
| `src/DataGuard.Core/Models/Configuration.cs` | full, 1-174 |
| `src/DataGuard.Core/Sources/EfModelSource.cs` | full, 1-675 |
| `src/DataGuard.Core/Sources/ManualContractSource.cs` | full, 1-101 |
| `src/DataGuard.Core/Sources/SqlKeywordMatcher.cs` | full, 1-21 |
| `src/DataGuard.Core/Sources/SqlServerParsers.cs` | full, 1-345 |
| `src/DataGuard.Core/Rules/ContractRules.cs` | full, 1-520 |
| `src/DataGuard.Core/Rules/PhantomIdentifierRule.cs` | full, 1-204 |
| `src/DataGuard.Core/Rules/RuleDependencyGraph.cs` | full, 1-366 |
| `src/DataGuard.Core/Validation/ConcurrentValidationEngine.cs` | full, 1-62 |
| `src/DataGuard.Core/PublicApi/PublicApiSurface.cs` | full, 1-336 |
| `src/DataGuard.Core/DataGuard.Core.csproj` | full, 1-46 |
| `src/DataGuard.Core/packages.lock.json` | full, 1-606 |

Không chạy test/build theo yêu cầu worker; không sửa source, commit, push hoặc publish. Lưu ý: các SQL Server integration tests được đọc nhưng chỉ chạy phần live khi Docker khả dụng; khi Docker thiếu, chúng early-return, vì vậy tổng pass count không phải bằng chứng DB runtime.
