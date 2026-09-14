# Research evidence

Nghiên cứu trực tuyến ngày 2026-09-13, chỉ dùng nguồn chính thức:

- [Microsoft .NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy): .NET 9 là STS, maintenance đến 2026-11-10; .NET 10 là LTS target-state proposal.
- [OpenTelemetry .NET](https://opentelemetry.io/docs/languages/dotnet/): traces, metrics và logs được đánh dấu stable; SDK hỗ trợ các .NET runtime còn được Microsoft hỗ trợ.
- [OpenTelemetry .NET traces](https://opentelemetry.io/docs/languages/dotnet/traces/): có hướng dẫn ASP.NET Core, sampling, tail sampling, exception reporting và links.
- [OpenTelemetry Collector gateway pattern](https://opentelemetry.io/docs/collector/deploy/gateway/): agent/gateway và trace-aware load balancing giúp tail sampling giữ đủ một trace tại collector.
- [OpenTelemetry Collector processors](https://opentelemetry.io/docs/collector/components/processor/): stability của memory limiter, redaction, transform và tail sampling phụ thuộc distribution/signal; phải validate exact image.
- [OpenTelemetry Profiles](https://opentelemetry.io/docs/specs/otel/profiles/): Profiles signal vẫn ở trạng thái Alpha; span-context links là khả năng tùy chọn và không thay thế kiểm thử backend.
- [Grafana Loki OTLP ingestion](https://grafana.com/docs/loki/latest/send-data/otel/): dùng `otlphttp` exporter tới `/otlp`; structured metadata phải được bật theo Loki version.
- [Grafana Tempo](https://grafana.com/docs/tempo/latest/): Tempo tích hợp traces với Loki/Mimir và hỗ trợ jump từ logs/metrics khi data-source links được cấu hình.
- [Grafana Pyroscope .NET](https://grafana.com/docs/pyroscope/latest/configure-client/language-sdks/dotnet/): native .NET profiling có runtime/profiler và credential/deployment contract riêng; không được bật bởi DI extension.
- [Grafana Pyroscope eBPF profiler](https://grafana.com/docs/pyroscope/latest/configure-client/opentelemetry/ebpf-profiler/): eBPF profiler cần Linux/privileged host access; Profiles protocol và symbolization còn có giới hạn nên phải canary.
- [Npgsql.OpenTelemetry 10.0.3](https://www.nuget.org/packages/Npgsql.OpenTelemetry): package chính thức của Npgsql bật tracing command; package targets .NET 8 và tương thích computed với `net9.0`. Npgsql 10 đổi tên metric/tag theo OTel, nên phải kiểm thử redaction và dashboard migration trước khi bật.
- [Npgsql 10 release notes](https://github.com/npgsql/doc/blob/main/conceptual/Npgsql/release-notes/10.0.md): command tracing/metrics align với OTel nhưng có breaking metric/tag changes; connection-string/GSSAPI và failed-command options cần owner review.
- [OpenTelemetry Collector releases](https://github.com/open-telemetry/opentelemetry-collector/releases): release list xác nhận `v0.160.0`; image manifest được pull và pin bằng digest trong phase 3.
- [Prometheus recording rules](https://prometheus.io/docs/prometheus/latest/configuration/recording_rules/): `promtool check rules` là syntax gate và rule unit tests là test gate.
- [OTel deployment semantic convention](https://opentelemetry.io/docs/specs/semconv/registry/attributes/deployment/): dùng `deployment.environment.name`; thuộc tính cũ `deployment.environment` là deprecated.

Các trang trên cung cấp compatibility/protocol direction, không thay thế kiểm thử với topology thật. Package restore/vulnerability scan của starter đã chạy PASS; Collector, backend, Kubernetes schema và profiler chưa có exact deployment input nên `NOT EXECUTED`.
