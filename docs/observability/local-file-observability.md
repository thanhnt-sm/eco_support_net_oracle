# Local file observability

## Phạm vi

DataGuard là CLI/library. Không có HTTP API hoặc container runtime trong đường chạy mặc định.
`TelemetryCollector` giữ các `Meter` hiện hữu để tương thích .NET/OpenTelemetry, đồng thời tự động
đưa event, counter, histogram và validation summary vào `FileObservabilitySink` khi telemetry được
bật mà không inject exporter khác.

Đường ghi này là diagnostic observability, không phải audit ledger. Audit bảo mật vẫn đi qua
`FileAuditLogger` với hash-chain/checkpoint riêng.

## Cấu hình

```csharp
var config = new TelemetryConfig(Enabled: true)
{
    FileSinkDirectory = "/var/lib/dataguard/observability/archive",
    FileSinkFilePrefix = "observability",
    ServiceName = "dataguard-cli",
    ServiceVersion = "1.0.0",
    EnvironmentName = "local",
    IncludeEventDetails = false,
    MaxQueuedEvents = 10_000,
    MaxBatchEvents = 1_000,
    MaxPayloadBytes = 1_048_576,
    MaxRecordBytes = 65_536,
    RemoteExportEnabled = false,
};

using var collector = new TelemetryCollector(config);
```

`FileSinkDirectory=null` dùng local application-data của OS, thường là
`$XDG_DATA_HOME/DataGuard/observability/archive` trên Linux hoặc thư mục LocalApplicationData
tương ứng trên Windows/macOS. Nên cấu hình path riêng cho job/host để dễ backup và retention.

## Định dạng và archive

Mỗi dòng là một JSON object độc lập (UTF-8, không BOM). File được chia theo UTC date:

```text
<FileSinkDirectory>/2026/09/14/observability-2026-09-14.ndjson
```

Envelope có các trường bounded:

```json
{
  "record_id": "01b3...",
  "timestamp": "2026-09-14T00:12:33.010Z",
  "signal": "metric",
  "event_name": "business.operation.duration",
  "service_name": "dataguard-cli",
  "service_version": "1.0.0",
  "operation_name": "banking.transfer.initiate",
  "value": 42.5,
  "unit": "ms",
  "trace_id": "4bf92f3577b34da6a3ce929d0e0e4736",
  "span_id": "00f067aa0ba902b7",
  "attributes": { "operation": "banking.transfer.initiate", "result": "success" },
  "resource": {
    "service.name": "dataguard-cli",
    "service.version": "1.0.0",
    "deployment.environment.name": "local"
  }
}
```

`trace_id`/`span_id` chỉ xuất hiện khi process đã có W3C `Activity`; sink không tạo trace giả.
Envelope này map tới khái niệm OTel nhưng không phải OTLP protobuf/HTTP wire payload.

## Privacy và cardinality

- Event body bị bỏ mặc định. `IncludeEventDetails=true` chỉ cho body đã redact và tối đa 1 KiB.
- Attributes là allowlist cố định (`operation`, `result`, `dependency`, `protocol`, `error.type`,
  `slo.class`, `rule`, `success`, `provider`, `reason`, `status`), mỗi giá trị tối đa 128 ký tự.
- PAN, bearer token, email, account/secret/password và line-break được redact; transaction/account/
  customer/message/trace IDs không trở thành attribute.
- Queue và record size có giới hạn. `DroppedObservabilityRecordCount` và `TerminalLossCount` là
  tín hiệu cần đưa vào local diagnostics; không tuyên bố zero-loss.

## Endpoint lockdown

- `TelemetryConfig.RemoteExportEnabled=false` là mặc định. Có `ExportEndpoint` không đồng nghĩa
  được phép egress; phải tắt file sink và bật switch owner-gated mới chọn legacy exporter.
- `CoreObservabilityOptions.RemoteExportEnabled=false` chặn OTel OTLP exporters mặc định.
- `HealthHostOptions.EnableHost=false` chặn listener và `ExposeEndpoints=false` chặn mapping
  `/health/*`; test host hiện hữu truyền cả hai switch rõ ràng. CLI/library không cần mở listener.
- Không có code nào ở local sink gọi `HttpClient`, Collector, Loki, Tempo, Mimir, Pyroscope,
  Docker hay Kubernetes.

## Failure và retention

File append là best effort và bounded. Disk đầy, permission error, lock hoặc process crash có thể
làm mất record; operation nghiệp vụ vẫn tiếp tục. Retry có thể tạo cùng `record_id` nhiều lần sau
partial append, vì vậy consumer/ETL phải deduplicate theo ID nếu cần.

Sink không tự xoá archive. Owner phải đặt retention/backup/encryption-at-rest theo policy dữ liệu.
Không dùng archive này làm bằng chứng bất biến hoặc thay thế audit/compliance store.

## Kiểm chứng

```sh
dotnet test tests/DataGuard.Core.Tests/DataGuard.Core.Tests.csproj \
  --configuration Release --no-restore --filter FullyQualifiedName~ObservabilityFileSinkTests
```

Các test kiểm tra archive theo ngày UTC, correlation từ `Activity`, allowlist/redaction, queue
bounded và remote endpoint không được gọi khi switch tắt.
