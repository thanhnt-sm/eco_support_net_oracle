# Observability Current State

## Metrics and telemetry

- **[CONFIRMED]** `TelemetryCollector` tạo `Meter("DataGuard.Core", "1.0.0")`, counters/histograms và bounded concurrent event queue (`src/DataGuard.Core/Telemetry/TelemetryCollector.cs:24-53`).
- **[CONFIRMED]** Metrics hiện có: `rule.executions`, `rule.duration` (tags rule/success), `validations.total`, `violations.total`, `violations.errors`, `violations.warnings`, `validation.contracts`, `validation.duration` (`TelemetryCollector.cs:127-166,514-524`).
- **[CONFIRMED]** Telemetry mặc định disabled; flush interval 30s, max queue 10,000 events, batch 1,000, payload 1 MiB, export timeout 5s (`TelemetryCollector.cs:451-470` và constants quanh `:514-524`).
- **[CONFIRMED]** Endpoint chỉ nhận HTTPS hoặc HTTP loopback/localhost; endpoint khác bị reject trước network call. Export dùng shared `HttpClient`, POST `application/x-ndjson`; circuit mở sau 3 lỗi liên tiếp; failed batch được requeue (`TelemetryCollector.cs:183-218,220-285,361-397`).
- **[CONFIRMED]** Dispose có bounded final flush và ghi nhận dropped/terminal loss; queue overflow/drop được đếm (`TelemetryCollector.cs:314-355,399-428`).
- **[INFERRED_MEDIUM]** `RecordEvent` nhận `details` và arbitrary `properties`, còn counter/histogram nhận metric name/tags động; trong collector không có field/property allow-list. Cần review PII/cardholder data và cardinality trước khi enable.
- **[CONFLICT]** `docs/03-components/core/telemetry.md:1-145` gọi export “compatible with OTLP/HTTP collectors”, nhưng code gửi generic NDJSON `application/x-ndjson`, không phải evidence của OTLP protobuf/JSON protocol.
- **[CONFIRMED]** Không tìm thấy package OpenTelemetry, `ActivitySource`, `AddMeter`, `AddSource`, `ActivityListener`, `MeterListener`, trace/span instrumentation hoặc `/metrics` endpoint trong source search. Đây là custom .NET metrics, chưa phải OpenTelemetry integration.

## Logs and diagnostic output

- **[CONFIRMED]** `ILogger` được inject tùy chọn ở một số Core classes; CLI và credential source còn dùng `Console.Error/WriteLine`. Không thấy host-wide logging provider/structured log sink configuration trong repository (`AutoDetectionEngine.cs`, `CredentialManager.cs`, `RulePluginManager.cs`, `ZeroTrustCredentialProvider.cs`; CLI `Program.cs`).
- **[CONFIRMED]** Diagnostic/SARIF path có SafePropertyKeys allow-list, sensitive value detection, source-root projection, relative artifact URI và atomic/sanitized sinks (`src/DataGuard.Core/Reporting/DiagnosticEmitter.cs:16-32,133-201,204-333`).
- **[CONFIRMED]** Contract evidence chỉ lưu provider/rule/severity/redacted message, deterministic sorting và atomic write (`ContractEvidence.cs:13-68`).
- **[INFERRED_MEDIUM]** `ContractEvidenceWriter.Redact` nhận diện ít pattern hơn `DiagnosticEmitter` (chủ yếu `password=`, `token=`, `authorization: bearer`), tạo khoảng trống nếu message dùng key syntax khác (`ContractEvidence.cs:79-85` so với `DiagnosticEmitter.cs:265-285`).

## Audit

- **[CONFIRMED]** `FileAuditLogger` mặc định ghi dưới OS ApplicationData/DataGuard/audit.log, kiểm tra symlink, sanitize/truncate text, lưu machine/user/process metadata và hash-chain SHA-256/checkpoint (`src/DataGuard.Core/Security/IAuditLogger.cs:54-96,158-213,219-270,330-343`).
- **[CONFIRMED]** Audit interface bao phủ database operation, credential access và configuration change (`IAuditLogger.cs:12-49`).
- **[CONFLICT]** `CredentialManager.LogAuditAsync` append JSON trực tiếp, serialize `details` và không áp dụng hash-chain/checkpoint (`src/DataGuard.Core/Security/CredentialManager.cs:392-416`). Hai đường audit cùng mặc định có thể trỏ audit log nên integrity/retention semantics chưa thống nhất.

## Health and readiness

- **[CONFIRMED]** WIP Host bind default `http://127.0.0.1:8080`, từ chối mọi URL không loopback/localhost, và map `/health/live`, `/health/startup`, `/health/ready` (`src/DataGuard.Host/Program.cs:3-32`; `HealthHostBinding.cs:1-15`; `HealthEndpoints.cs:5-20`).
- **[CONFIRMED]** Probes gồm snapshot, baseline, disk và managed memory; refresh background 10s, probe timeout 5s, maximum snapshot age 30s; ready trả 503 khi chưa sẵn sàng hoặc snapshot stale (`HealthHostOptions.cs:1-33`; `HealthProbeCoordinator.cs:3-74`; `LocalHealthProbes.cs:3-69`).
- **[CONFIRMED]** Không có auth middleware, mTLS/TLS certificate setup, remote binding hoặc metrics exporter trong Host code. Remote health explicitly requires separately configured authenticated host.
- **[INFERRED_HIGH]** Health hiện phù hợp local process/readiness signal, chưa phải production service-monitoring contract.

## Traces, profiling and backend

- **[CONFIRMED]** Không thấy tracing/span API, profiler, log aggregation client, metrics exporter, alert rule hoặc observability backend configuration.
- **[UNKNOWN]** Collector/backend, scrape/push model, dashboard, alerting, retention, sampling, correlation IDs, tenant labels, SLO/SLI và on-call ownership không có trong workspace; đây là blocking cho thiết kế nền tảng.
