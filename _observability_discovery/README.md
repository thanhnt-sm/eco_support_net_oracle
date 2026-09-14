# Enterprise Banking Observability — Current-State Discovery

Ngày lập hồ sơ: 2026-09-13 (Asia/Ho_Chi_Minh)

## Phạm vi và nguyên tắc

Hồ sơ này là kết quả quét tĩnh, chỉ đọc, trên workspace hiện tại. Chỉ thư mục `_observability_discovery/` được tạo mới; không sửa source, package, TargetFramework, CI/CD, container hay cấu hình ngoài thư mục này. Báo cáo không chứa source code gốc, secret, certificate, payload, dump hoặc dữ liệu khách hàng.

Không chạy `dotnet restore`, `dotnet build`, `dotnet test`, `dotnet format`, Docker build/pull/push, package install/update, deploy hoặc lệnh git làm thay đổi trạng thái. Không dùng web search; lifecycle/CVE bên ngoài vì vậy chưa được xác minh.

## Kết luận điều hành

- **[CONFIRMED]** Repository là DataGuard, một công cụ .NET kiểm tra contract/schema với CLI, Core pipeline, database adapters, analyzer/code-fix, IDE integration và một số component WIP (Host/LSP/Build).
- **[CONFIRMED]** Có telemetry tự xây dựng dựa trên `System.Diagnostics.Metrics` và NDJSON HTTP export tùy chọn; mặc định tắt. Không thấy OpenTelemetry SDK/exporter, `ActivitySource`, `MeterListener` hoặc OTLP endpoint thực sự trong source.
- **[CONFIRMED]** Có audit log file với sanitize, identity/process metadata và hash chain; song song `CredentialManager` còn một đường append JSON riêng không dùng hash chain.
- **[CONFIRMED]** Health host chỉ bind loopback, có live/startup/ready probes; không có metrics endpoint, authentication hoặc remote health listener trong component này.
- **[CONFIRMED]** Credential resolution ưu tiên environment → managed secret stores → local encrypted store → plaintext config chỉ khi bật cờ; URL/transport có allow-list ở các nhánh HTTP tùy chọn.
- **[INFERRED_MEDIUM]** Telemetry event properties và metric tags nhận dữ liệu động mà không có allow-list tại collector; cần data-classification review trước khi bật trong banking production.
- **[CONFLICT]** Một số tài liệu mô tả adapter/health/telemetry là chưa có, trong khi source hiện tại đã có các component tương ứng; tài liệu lịch sử không được dùng làm ground truth.
- **[UNKNOWN]** Chưa xác định production topology, collector/backend, traffic, SLO/SLI, alerting, retention, PII/PCI classification, ownership, RTO/RPO, cost budget hoặc quyền thay đổi.

## Phân loại bằng chứng

`CONFIRMED` = bằng chứng trực tiếp; `INFERRED_HIGH` = suy luận mạnh từ nhiều nguồn; `INFERRED_MEDIUM` = dấu hiệu chưa đủ xác nhận; `UNKNOWN` = không tìm thấy trong workspace; `CONFLICT` = nguồn mâu thuẫn; `REDACTED` = có thông tin nhưng không ghi giá trị; `UNVERIFIED_EXTERNAL` = cần xác minh bên ngoài.

## Tệp trong bundle

- `repository-inventory.md` — topology, dirty state, project/TFM và phạm vi build.
- `runtime-and-architecture.md` — entrypoint, data flow, boundaries và component maturity.
- `observability-current-state.md` — metrics, events, logs, audit, health, tracing và egress.
- `security-and-data-handling.md` — secret scan, credential chain, redaction, plugin/supply-chain controls và findings.
- `ci-cd-and-delivery.md` — workflow, permissions, artifact, security gates và Docker.
- `dependencies-and-build.md` — project matrix, lock-file inventory, package families và verification.
- `evidence-index.md` — bảng dẫn chứng path/line range cho các phát hiện chính.
- `gaps-and-unknowns.md` — thông tin thiếu, ý nghĩa, nơi/owner có thể trả lời và mức blocking.
- `scan-method.md` — phương pháp, lệnh chỉ đọc đã thực hiện và giới hạn.

Bundle cuối cùng: `observability-discovery-bundle.zip` (chỉ chứa các báo cáo Markdown nêu trên).
