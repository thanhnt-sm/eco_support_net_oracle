# Báo cáo audit Luna — DataGuard.Core (Assessment, AutoDetection, Baseline, Plugins, Reporting, Security, Telemetry)

Ngày audit: 2026-09-12. Baseline: 93bf7288324dd746669ad09c5e2a592adc772748.
Không chạy test/build theo yêu cầu; đây là static audit và trace caller/test. Không có secret/session content trong báo cáo.

## Phạm vi và read coverage

Đã đọc toàn bộ 23 file được giao theo batch nhỏ, không bỏ qua phần cuối file:

- Assessment: src/DataGuard.Core/Assessment/AssessmentContracts.cs 1–150; src/DataGuard.Core/Assessment/AssessmentEngine.cs 1–89; src/DataGuard.Core/Assessment/Internal/AssessmentReportWriter.cs 1–27; src/DataGuard.Core/Assessment/Internal/BuildCiPack.cs 1–152; src/DataGuard.Core/Assessment/Internal/DependencyHealthPack.cs 1–112; src/DataGuard.Core/Assessment/Internal/InventoryPack.cs 1–121; src/DataGuard.Core/Assessment/Internal/PackagesConfigReader.cs 1–70; src/DataGuard.Core/Assessment/Internal/ProjectInventoryReader.cs 1–125; src/DataGuard.Core/Assessment/Internal/SecretsPack.cs 1–163; src/DataGuard.Core/Assessment/LegacySupportTable.cs 1–69; src/DataGuard.Core/Assessment/UpgradePlanner.cs 1–169.
- AutoDetection: src/DataGuard.Core/AutoDetection/AutoDetectionEngine.cs 1–633.
- Baseline: src/DataGuard.Core/Baseline/BaselineManager.cs 1–416.
- Plugins: src/DataGuard.Core/Plugins/RulePluginManager.cs 1–328.
- Reporting: src/DataGuard.Core/Reporting/ContractEvidence.cs 1–87; src/DataGuard.Core/Reporting/ContractExport.cs 1–272; src/DataGuard.Core/Reporting/DiagnosticEmitter.cs 1–491; src/DataGuard.Core/Reporting/SarifTypes.cs 1–163.
- Security: src/DataGuard.Core/Security/CredentialManager.cs 1–263; src/DataGuard.Core/Security/IAuditLogger.cs 1–304; src/DataGuard.Core/Security/SupplyChainVerifier.cs 1–233; src/DataGuard.Core/Security/ZeroTrustCredentialProvider.cs 1–441.
- Telemetry: src/DataGuard.Core/Telemetry/TelemetryCollector.cs 1–313.

Caller/test trace chính: src/DataGuard.Cli/Program.cs (baseline, snapshot refresh/show/diff, validate, assess); tests/DataGuard.Core.Tests/AssessmentPackTests.cs, AutoDetectionEngineTests.cs, SourceAndBaselineTests.cs, CliExitCodeTests.cs, UpgradePlannerTests.cs, CredentialManagerFullTests.cs, ZeroTrustCredentialProviderTests.cs, SupplyChainVerifierTests.cs, TelemetryTests.cs, DiagnosticEmitterFullTests.cs, ContractEvidenceTests.cs, ContractExportTests.cs. RulePluginManagerTests nằm trong PublicApiAndPipelineTests.cs, có tests quản lý/empty directory. Đối chiếu docs/03-components/core/{assessment,auto-detection,baseline,plugins,reporting,security,telemetry}.md, docs/assess.md, ADR-003, plans/2026-08-21-review-handoff.md, plans/ACTIVE_SESSION_REGISTER.md.

Quy ước: khớp = code/tài liệu phù hợp; một phần = wiring/giới hạn chưa đủ claim; chưa tìm thấy = không thấy caller/test thực thi; tài liệu lệch = mô tả rộng hơn code; định hướng = historical/research/plan; chưa đủ bằng chứng = cần runtime/DB/test riêng.

## Findings

### CS-01 — P1 / confidence cao — snapshot diff không đọc schema hiện tại

Trạng thái: tài liệu lệch code; lỗi thực thi.

src/DataGuard.Cli/Program.cs:413-425 khi snapshot có Schema gọi BaselineManager.ComputeSchemaHash(baseline.Schema), tức hash lại chính snapshot đã lưu. Comment tại 417-420 thừa nhận offline diff self-compare; không có nhánh đọc schema hiện tại dù lệnh nhận connection. Thay đổi DB sau refresh vì vậy có thể luôn báo khớp trên schema path. Chỉ nhánh legacy fallback Program.cs:436-443 so violation hash hiện tại, khác semantics.

Caller là snapshot diff (Program.cs:363-470). CliExitCodeTests.cs:76-146 chỉ khóa exit code bằng legacy v1/offline, chưa test live schema drift. docs/03-components/core/baseline.md:194-195 và docs/03-components/tooling/cli.md:115-130 mô tả compare current schema, nên claim chưa được chứng minh.

Khuyến nghị: có connection thì query/capture schema mới qua adapter rồi hash SnapshotTable mới; chỉ cho offline self-compare khi nói rõ đây là consistency check. Thêm test đổi cột/type/nullability sau refresh và test fail-on-drift schema path.

### CS-02 — P2 / confidence cao — fallback hash khác schema hash và có giới hạn drift

Trạng thái: limitation đã được CLI cảnh báo; cần phân biệt schema snapshot hash với violation fallback hash.

BaselineManager.cs:204-214 là hash violation 16-hex; :221-249 là full SHA-256 schema snapshot; :256-268 là 16-hex hash BaselineViolation cho legacy. CLI baseline truyền violation hash (Program.cs:211-219); snapshot refresh chỉ dùng schema hash khi capture Oracle schema thành công (Program.cs:291-305); Program.cs:441-443 fallback về violation hash. DDL không sinh violation sẽ im lặng trong fallback/non-Oracle/no-connection.

SourceAndBaselineTests kiểm tra migration và schema persistence; CliExitCodeTests kiểm tra legacy fallback. Review-handoff cũ có F3; CLI hiện đã cảnh báo format v1, nên đây là limitation/historical finding, không phải lỗi nghiêm trọng độc lập. Khuyến nghị lưu hash kind và test add/drop/type không sinh violation nếu fallback tiếp tục được hỗ trợ.

### CS-03 — Đã bác bỏ bằng probe — nhận diện sibling lockfile hoạt động

Agent chính chạy CLI đã build trên fixture vô hại trong `.tmp/luna-src-audit-260912-1936/inventory-probe/`: project net9.0, sibling lock có net8.0. Kết quả `inventory-probe-result.json` phát **DG1202** với evidence `packages.lock.json`, không có DG1201. Điều này chứng minh lockfile được nhận diện và DependencyHealthPack đã chạy. Kết luận “File.Exists(file/../sibling) luôn false” bị bác bỏ; không tính thành defect hay việc sửa bắt buộc.

Lệnh và output được ghi trong [verification](verification.md). Probe chỉ kiểm tra lockfile, không chứng minh mọi tình huống packages.config.

### CS-04 — P2 / confidence cao về source — path containment dùng prefix chuỗi

`src/DataGuard.Core/Assessment/Internal/ProjectInventoryReader.cs:23-26` và các reader tương tự dùng fullPath.StartsWith(root). Workspace /tmp/app sẽ chấp nhận prefix /tmp/app-evil. Tác động thường hẹp với enumeration nội bộ; đây là giới hạn ở public reader nhận path caller-provided, chưa chứng minh CLI assess đọc dữ liệu ngoài workspace.

PackagesConfigReaderTests.cs:236-245 chỉ test outside path tách hẳn, chưa có sibling-prefix hoặc hostile path cho ProjectInventory/Secrets. Khuyến nghị dùng Path.GetRelativePath rồi reject .. / absolute, và nêu rõ symlink policy.

### CS-05 — P2 / confidence cao — AllowRemoteLookups chưa có implementation, tài liệu overclaim

AssessmentContracts.cs:13-15 công bố AllowRemoteLookups; AssessmentEngine.Run không đọc property và mọi pack đều local. DependencyHealthPack.cs:5-8 chỉ kiểm tra lock target sections, không vulnerability/advisory lookup hay health score. docs/03-components/core/assessment.md:5,87,172 mô tả dependency health/remote advisory rộng hơn; docs/assess.md:69 nhấn local-first. ADR-003:24 là định hướng, không phải evidence implementation.

CLI assess Program.cs:719-726 không đặt AllowRemoteLookups; AssessmentPackTests chỉ local fixtures. Local assessment khớp; remote advisory/vulnerability chưa tìm thấy. Khuyến nghị bỏ option/đổi docs hoặc implement provider, timeout, partial-error result và test egress opt-in.

### CS-06 — Không đủ bằng chứng defect — wizard dùng Snapshot + baseline overlay

AutoDetectionEngine.cs:521-545 prompt nói lựa chọn 2 là Baseline nhưng switch "2" => GroundTruthMode.Snapshot; GroundTruthMode không có enum Baseline: baseline là overlay option, nên đây không đủ bằng chứng là bug; có thể là thiết kế Snapshot + baseline. AutoDetectionEngineTests chỉ test DetectAsync, không test wizard mapping. Khuyến nghị bổ sung fake-console test và làm rõ docs/UI rằng choice 2 bật overlay nào.

### CS-07 — P1 / confidence cao — CredentialManager lưu plaintext non-Windows nhưng gắn cờ encrypted

CredentialManager.cs:87-100 chỉ encrypt khi Windows nhưng luôn ghi IsEncrypted = true nếu config bật; non-Windows ConnectionString vẫn plaintext. GetStoredConnectionStringAsync :199-203 không decrypt non-Windows. Comment :139 hứa libsecret nhưng không có implementation. CredentialManagerFullTests.cs:219-236 cố định plaintext non-Windows, là hành vi hiện tại chứ không chứng minh security claim.

Docs security giới hạn DPAPI Windows-only ở :188-191,330 nhưng top-level claim encrypt-at-rest quá rộng. Khuyến nghị fail-closed khi encryption bật mà không có backend, hoặc implement backend platform; không đặt IsEncrypted=true khi lưu plaintext.

### CS-08 — P2 / confidence trung bình — audit logger giữ một phần giá trị cấu hình

IAuditLogger.cs:70-90 nhận details/errorMessage tùy ý rồi FileAuditLogger serialize nguyên văn (IAuditLogger.cs:154-169). LogConfigurationChangeAsync dùng MaskValue giữ 4 ký tự đầu/cuối (139-151), vẫn là disclosure không cần thiết. Hash-chain chỉ bảo toàn integrity, không redaction.

CredentialManager hiện chỉ ghi boolean và ZeroTrust ghi hash/source. AuditAndConfigTests.cs:85-103 đã chứng minh full test strings không xuất hiện, nhưng MaskValue vẫn giữ 4 ký tự đầu/cuối và interface public cho phép caller truyền details/errorMessage tùy ý. Đây là residual disclosure/contract hardening, không phải evidence có đường leak thực tế trong caller hiện tại. Khuyến nghị structured whitelist hoặc hash/mask toàn bộ, thêm test caller truyền secret-shaped details.

### CS-09 — P2 / confidence cao — StreamingSarifSink bypass redaction trên direct overload

DiagnosticEmitter.CreateSarifLog redacts SafeText/CreateSafeProperties (DiagnosticEmitter.cs:74-145). FileSarifSink(streaming:true) nhận SarifLog đã sanitized và không phải đường bypass mặc định; tuy nhiên public StreamingSarifSink.WriteAsync(IEnumerable<ContractViolation>) ghi thẳng message và mọi Properties (356-461), nên direct caller có thể tạo SARIF không được redact.

DiagnosticEmitterFullTests.cs:371-486 chỉ kiểm tra cấu trúc; ContractEvidenceTests.cs:14-33 chỉ test evidence redaction. Khuyến nghị dùng sanitizer/property allowlist chung cho mọi sink và test password/token/JWT qua buffered + streaming.

### CS-10 — P2 / confidence trung bình — plugin metadata/isolation chưa đủ bằng chứng

RulePluginManager.cs:72-103 chỉ quét DLL khi caller truyền explicit directory, điểm này khớp security intent. Nhưng collectible AssemblyLoadContext không phải sandbox: plugin vẫn chạy code tùy ý với quyền process. Docs plugins :102-104 nói types không interfere và failures không crash host, rộng hơn guarantee. Metadata tạo từ GetCustomAttributes<ExportMetadataAttribute> (:106-110), trong khi ExportRuleAttribute :214-246 là MetadataAttribute; chưa có test hydrate metadata. GetRuleMetadata() trả cả incompatible metadata.

PublicApiAndPipelineTests.cs:152-231 có test null/nonexistent/empty directory, built-in merge, lookup, metadata-empty và dispose; chưa có fixture DLL kiểm tra metadata/version/throw. Khuyến nghị test external fixture DLL và sửa docs thành not sandboxed nếu plugin được coi là untrusted.

### CS-11 — P2 / confidence trung bình — SupplyChainVerifier trusted-prefix heuristic không phải provenance

SupplyChainVerifier.cs:83-157 đánh dấu trusted chỉ theo prefix (Microsoft., AWSSDK., Dapper...). Tên giả mạo có thể pass. Class/docs gọi supply-chain integrity nhưng không xác minh NuGet signature, package hash, SBOM hay SLSA provenance. expectedHashFile chỉ so assembly hash hiện tại.

SupplyChainVerifierTests.cs:12-53 kiểm tra no-anchor/missing hash/debug/dependency, chưa test spoof prefix/signature. Phân loại heuristic một phần; SLSA/NuGet integrity chưa tìm thấy. Khuyến nghị đổi wording hoặc tích hợp verifier nguồn tin cậy và test negative package name.

### CS-12 — P2 / confidence trung bình — ContractExport không deterministic đầy đủ/TS identifier chưa validate

ContractExportWriter.Build sort entity/procedure/table nhưng giữ thứ tự nested collections (ContractExport.cs:166-223); cùng contracts khác input order cho output khác nhau. TypeScriptContractWriter.ToTypeScriptIdentifier trả nguyên tên (:254-256), nên tên có dấu cách, -, keyword hoặc ký tự đặc biệt có thể tạo TS không hợp lệ.

Caller CLI export Program.cs:125; ContractExportTests.cs:14-79 chỉ assert presence. Khuyến nghị canonical sort nested collections và sanitize/escape identifier với collision handling; thêm permutation/invalid-name tests.

### CS-13 — P2 / confidence trung bình — AutoDetection scan toàn cây và giữ connection string plaintext

AutoDetectionEngine.cs:109-140,226-275,330-386 dùng Directory.GetFiles AllDirectories không loại bin/obj/node_modules, không cap size/per-file IO policy. DetectConnectionStringAsync trả nguyên connection string từ env/appsettings/YAML (:279-327) vào DataGuardConfiguration; đây là legacy onboarding, không phải ZeroTrust provider.

AutoDetectionEngineTests.cs:55-65 mong đợi plaintext; test đó xác nhận legacy behavior. Khuyến nghị trả reference/provider hoặc dùng ZeroTrustCredentialProvider, loại generated dirs và nêu secret boundary.

### CS-14 — P2 / confidence trung bình — lifecycle/cancellation gaps baseline/telemetry

BaselineManager.CreateBaselineAsync nhận cancellation nhưng SaveAsync không truyền token (BaselineManager.cs:45-75,139-184); load memory-map cast capacity sang int (:110-132) không có size cap. TelemetryCollector.FlushEvents (:152-202) block GetAwaiter().GetResult() trong timer, dequeue rồi export; failure làm mất queue events.

Telemetry default disabled và endpoint allowlist (:35-45,166-201) khớp docs/ADR; explicit HTTPS egress vẫn tồn tại khi enabled. TelemetryTests có disabled/allowlist/circuit-breaker nhưng chưa cancellation/loss semantics; SourceAndBaselineTests chưa large-file/cancellation. Khuyến nghị async flush, bounded file validation, retry/drop policy rõ.

## Đối chiếu tài liệu và phạm vi lịch sử

- docs/assess.md local-first và curated support rows nhìn chung khớp InventoryPack/LegacySupportTable; remote advisory/vulnerability là một phần/tài liệu lệch (CS-05).
- baseline.md chưa phân biệt đủ schema full hash với violation 16-hex fallback; current-schema diff không đúng runtime (CS-01/02).
- security.md ghi DPAPI Windows-only ở chi tiết, nhưng claim zero-trust/encrypt-at-rest tuyệt đối bị giới hạn bởi plaintext non-Windows, audit và SARIF sink (CS-07/08/09).
- review-handoff và ACTIVE_SESSION_REGISTER chứa nhiều test count/coverage lịch sử khác nhau (80/214/291/320). Không dùng các số này làm hiện trạng; chỉ dùng test files/case names đã trace. F3/F4 là historical/định hướng, không thay thế runtime evidence.
- master-plan/research/opportunity material không dùng để tuyên bố feature đã có; health/vulnerability remote lookup chỉ là proposal/document claim.

## Ma trận coverage

| Khu vực | Runtime caller | Test tìm thấy | Đánh giá |
|---|---|---|---|
| Assessment | CLI assess → Engine/packs | AssessmentPack, UpgradePlanner | Có đường chạy; facts/path boundary chưa khóa |
| AutoDetection | legacy onboarding/tests; CLI caller trực tiếp chưa thấy chắc chắn | AutoDetectionEngineTests | Unit cơ bản; wizard mapping chưa test |
| Baseline | CLI baseline/snapshot/validate | SourceAndBaseline, CliExitCode | Legacy fallback có test; live schema diff chưa |
| Plugins | public manager/example/docs | RulePluginManagerTests trong PublicApiAndPipelineTests | Có test quản lý/empty paths; chưa fixture plugin DLL cho loading/metadata/isolation |
| Reporting | CLI export/validate/emitter | DiagnosticEmitterFull, Evidence, Export | Buffered có test; streaming leak chưa |
| Security | CLI/pipeline seams/providers | Credential/ZeroTrust/SupplyChain | Happy-path; hostile/redaction cases thiếu |
| Telemetry | Collector API; wiring pipeline chưa xác minh đầy đủ | TelemetryTests | Opt-in/allowlist có test; egress explicit |

## Ưu tiên đề xuất

1. CS-01: sửa và đặc tả schema-vs-violation drift.
2. CS-07: đóng plaintext credential storage; sau đó harden CS-08/09 theo threat model.
3. CS-04: kiểm tra và làm rõ path containment ở public readers; CS-03 đã bác bỏ, không cần sửa vì finding này.
4. CS-05/10/11: align claims với wiring, bổ sung seam tests; CS-06 là câu hỏi thiết kế, không phải lỗi đã chứng minh.
5. CS-12/13/14: deterministic export, auto-detection boundary, lifecycle hardening.
