---
type: scout
date: 2026-09-12
baseline: 93bf7288324dd746669ad09c5e2a592adc772748
---
# Ma trận source ↔ plans/research/docs

“Khớp” dưới đây là khớp capability và đường gọi đã đọc; không tự chứng minh database thật, IDE UI, release hay hiệu năng. Test suite thực chạy được ghi riêng tại [verification](verification.md). Không tính phần trăm hoàn thành tính năng vì tài liệu gồm cả roadmap, ví dụ và lịch sử.

## Ma trận capability

| Capability / nguồn cam kết | Source và caller | Test / giới hạn | Kết luận |
|---|---|---|---|
| Layering Core/Contracts/analyzer — ADR-002 | `src/DataGuard.Core/DataGuard.Core.csproj:7`, Contracts/Analyzers csproj; heavy engine ở CLI | Build solution pass; Core thực có vendor dependency | Implementation phù hợp ADR mới; package description “zero vendor” lệch. CE-03 |
| Full/Snapshot/Manual — core sources docs, CLI docs | `src/DataGuard.Cli/Program.cs:80-100,934-1015` chọn persisted schema/manual assembly/provider live | SourceAndBaselineTests, CliExitCodeTests; không chứng minh DB assertions | Có ba nhánh; `--offline` là Manual, quickstart gọi Snapshot bị lệch. CI-05 |
| EF design-time ModelSnapshot — sources/design-philosophy docs | `src/DataGuard.Core/Sources/EfModelSource.cs:215-244,585-675` tìm C# rồi parse JSON, fallback built assembly | SourceAndBaselineTests dùng JSON synthetic; chưa fixture C# snapshot thật | Một phần; CE-01 |
| DG rule engine — rules-engine/abstractions docs | `src/DataGuard.Core/Rules/ContractRules.cs:52-60,397-456`; CLI GetRulesForProvider | RulesEngineTests/GoldenCorpus; nullable rule dùng annotation Required thay vì IsNullable | Có engine; có giới hạn nullability/call-site metadata. CE-06/07 |
| Dependency graph — rules-engine docs | `src/DataGuard.Core/Rules/RuleDependencyGraph.cs:24-55,63-107,146-155` | RuleDependencyGraphTests; missing dependency placeholder không được phân biệt | Một phần; CE-04 |
| Concurrent validation — validation docs | `src/DataGuard.Cli/Program.cs:1024-1044`; `src/DataGuard.Core/Validation/ConcurrentValidationEngine.cs:26-61` | Concurrent engine có unit tests; test pass không phải benchmark | CLI có concurrent; vượt cap bị cắt kết quả, không có truncation signal. CE-09 |
| Public fluent pipeline — public-api docs | `src/DataGuard.Core/PublicApi/PublicApiSurface.cs:40-66,124-142` | PublicApiAndPipelineTests; execution luôn tuần tự | Một phần so với config concurrent/smart defaults. CE-02/08 |
| SQL Server adapter — adapter docs, ADR-002 | Parser ở `src/DataGuard.Core/Sources/SqlServerParsers.cs:19-185`; CLI `Program.cs:963-969` | Hai Testcontainers fixtures có early return; DB assertions chưa chứng minh | Implementation có thật; không coi project adapter ít file là thiếu |
| Oracle procedure/package — oracle-adapter docs | `src/DataGuard.Oracle.Adapter/OracleReaders.cs:58,196`; CLI `Program.cs:975-983` | Chưa live Oracle reproduction | Bất nhất placeholder `@packageName`/`:packageName` trên đường gọi live. AD-01 |
| Oracle REF CURSOR/result shape — oracle-adapter docs | `src/DataGuard.Cli/Program.cs:983-994` tạo result columns rỗng và ReturnsRefCursor false | Reader tồn tại nhưng chưa nối normal validation; chưa Oracle integration | Một phần; AD-04 |
| Oracle byte/char semantics — oracle-adapter docs | `src/DataGuard.Oracle.Adapter/OracleReaders.cs:467-475` trả BYTE/CHAR; `LengthMismatch.cs:213-214` kiểm tra B | Unit detector không thay thế test reader→detector live | Bất nhất biểu diễn; AD-05 |
| MySQL rules/length — adapter docs, fix-issues-gaps plan | MySql adapter classes; `src/DataGuard.Cli/Program.cs:1137-1142` chỉ đăng ký MY001/MY002/MY004 | MySqlAdapterTests gọi helper; không chứng minh các rule còn lại reachable CLI | Một phần; AD-02 |
| PostgreSQL rules/schema — adapter docs | `src/DataGuard.Cli/Program.cs:1005-1011,1143-1149`; parser trả routine descriptors | PostgreSqlAdapterTests; snapshot offline có thể cung cấp schema nhưng live path chưa nối schema reader | Một phần; AD-03 |
| IDE diagnostics — analyzers docs | `src/DataGuard.Analyzers/Analyzers.cs:24-61,238-383,501-706`; DG001 IDE khác DG101 engine | Analyzers 5 pass; supported descriptors không đồng nghĩa mọi rule được emit | Analyzer nhẹ; claim full database validation trong IDE/CI lệch ADR. TL-03/04 |
| Code fixes — code-fixes docs, PRODUCT | `src/DataGuard.CodeFixes/CodeFixProviders.cs:26-85,437-525`; 3 exported providers | CodeFixes 12 pass chủ yếu metadata/FixAll, chưa hành vi mỗi action | Một số advertised IDs không có action; provider/action count docs lệch. TL-01/02/06 |
| Baseline và snapshot drift — baseline/CLI docs | `src/DataGuard.Cli/Program.cs:413-425` hash lại baseline.Schema; `BaselineManager.cs:204-268` có hai hash semantics | CliExitCodeTests khóa legacy exit codes; chưa live schema change test | Lỗi self-comparison khi schema/hash nhất quán; CI-01 = CS-01; legacy fallback là limitation CS-02 |
| Credential config/env — security/CLI/USAGE docs | `src/DataGuard.Cli/Program.cs:195-200,247-260,374-379,574-590`; parsers nhận config trực tiếp | CredentialManager tests không chứng minh CLI dùng manager | Một số command overwrite connection null; env resolution chưa nối CLI. CI-02/03 |
| Credential encryption at rest — security docs | `src/DataGuard.Core/Security/CredentialManager.cs:87-100` chỉ mã hóa Windows nhưng flag theo config | CredentialManagerFullTests; chỉ áp dụng library store khi bật encryption trên non-Windows | Một phần; cờ encrypted có thể sai. CS-07 |
| Reporting/SARIF/evidence/export — reporting docs | `DiagnosticEmitter.cs:74-145,202-220,356-470`, `ContractEvidence.cs`, `ContractExport.cs` dưới Core/Reporting | DiagnosticEmitterFull/ContractEvidence/ContractExport tests pass | Default emitter có sanitize; direct StreamingSarifSink overload cần hợp nhất policy. CS-09/12 |
| Assessment local — docs/assess.md, ADR-003 | `src/DataGuard.Core/Assessment/AssessmentEngine.cs:29-65`; DependencyHealthPack local; CLI assess | AssessmentPack/UpgradePlanner tests; probe riêng phát DG1202 đúng sibling lock | Local capability khớp; không có remote CVE lookup/health score. CS-05; CS-03 đã bác bỏ |
| Auto-detection — auto-detection docs | `src/DataGuard.Core/AutoDetection/AutoDetectionEngine.cs`; public defaults/CLI init có wiring khác | AutoDetectionEngineTests; wizard Snapshot + baseline không tự là lỗi | Một phần; kiểm tra caller cụ thể trước claim zero-config. CE-08, CI-09, CS-06/13 |
| Plugin loading — plugins docs | RulePluginManager + `PublicApiSurface.cs:86-97` explicit directory | Có RulePluginManagerTests quản lý/empty paths; chưa plugin DLL hostile fixture | Có API; load context không phải security sandbox. CS-10 |
| Supply-chain verifier — security docs | `src/DataGuard.Core/Security/SupplyChainVerifier.cs` hash anchor/prefix heuristic | SupplyChainVerifierTests; không có release provenance verification của đợt này | Capability local có giới hạn; không đồng nhất với CI signing/SLSA. CS-11 |
| Telemetry — telemetry docs | `src/DataGuard.Core/Telemetry/TelemetryCollector.cs`; public pipeline opt-in | TelemetryTests; chưa benchmark/egress integration riêng | Default disabled và allowlist có code; chưa chứng minh reliability khi export lỗi. CS-14 |
| VS Code — marketplace plan và extension docs | Run/Cancel → CLI child process → temp SARIF; `src/DataGuard.VSCode/src/extension.ts:120-139,256-300` | npm test 2 pass (redaction/config containment), chưa UI/process integration | Runner có thật; docs settings/real-time/assess và URI containment overclaim. CI-06/07 |
| Visual Studio — marketplace plan và extension docs | `src/DataGuard.VisualStudio/DataGuardPackage.cs:42-73,135-218` Run/Cancel runner | Windows/VSSDK build và UI chưa chạy trên macOS | Static runner có thật; nhiều options/UI docs không có. CI-08 |
| Pre-commit hook — CLI hook installer | `src/DataGuard.Cli/Hooks/PreCommitHookInstaller.cs:199-203` ghi hookPath thay hookContent | Chưa chạy cài hook; không thay hook repo | Lỗi static ở nhánh Husky; CI-04 |
| Health HTTP endpoints — PRODUCT/STAGE_FLOW/architecture | Không tìm thấy route/HTTP host sau đối chiếu src, tests, solution, Dockerfile và CI | Không endpoint test/host runtime evidence | Tài liệu lệch, chưa implementation trong topology hiện tại. CI-10 |
| Research EcoSupport/MCP/roadmap — plans/research | Prototype và research độc lập, master/implementation plan đã superseded | Không chạy prototype để chứng minh DataGuard | Định hướng/lịch sử; không gộp thành implementation gap |

## Cách xử lý kết luận trùng và chưa đủ bằng chứng

- CI-01 và CS-01 là cùng một lỗi snapshot self-comparison; chỉ tính một vấn đề ưu tiên.
- CS-03 đã bị probe thực chạy bác bỏ. Snapshot + baseline overlay (CS-06), `--fail-on-drift` opt-in và research lịch sử không tự là bug.
- Default emitter/file streaming nhận SARIF đã sanitize; nguy cơ CS-09 thuộc direct overload, không được mô tả thành leak của mọi CLI output.
- SQL Server hidden-column case CE-05 còn phụ thuộc provider/metadata mode; không kết luận runtime bug chưa tái hiện.
- Số rule/provider khác nhau cần phân biệt lớp IDE, Core, provider và code action; không cộng các descriptor như coverage thực thi.

## Khuyến nghị kiểm chứng tiếp

Ưu tiên fixture/live test cho schema drift, Oracle bind và reader→detector; test command-level cho config/env/Husky; Roslyn action tests thay vì chỉ metadata. Chỉ triển khai các sửa đổi này trong nhiệm vụ tiếp theo, không thay source trong đợt báo cáo.
