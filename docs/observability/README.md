# DataGuard enterprise observability reference

Ngày 2026-09-14 (Phase 6 local-file final gate; nền tảng được tạo ngày 2026-09-13). Đây là
starter implementation dựa trên discovery tĩnh; không tuyên bố production-ready.

Discovery cho thấy DataGuard là CLI/library đa surface `net9.0`/`netstandard2.0`, có custom
`System.Diagnostics.Metrics` và telemetry NDJSON opt-in. Sản phẩm không phải backend/frontend và
không chạy trong Docker. Vì vậy đường chạy mặc định là `DataGuard.Core.Telemetry.FileObservabilitySink`:
ghi record observability dạng text NDJSON, archive theo ngày UTC, bounded queue và fail-open.

OTel SDK/Collector/LGTM/Kubernetes/profiling artifacts bên dưới là compatibility/reference adapter
cho một host khác nếu sau này có owner contract; chúng không được DataGuard CLI/library khởi tạo
hoặc mở endpoint mặc định. Observability file cũng không phải audit ledger; `FileAuditLogger` vẫn
là đường hash-chain riêng.

## Deliverables

- `red-team.md`: accepted/modified/rejected requirements, assumptions và known unknowns.
- `compatibility.md`: baseline, lựa chọn package và evidence; .NET 9 là STS và hết hỗ trợ 2026-11-10, nên .NET 10 LTS là target-state proposal.
- `architecture.md`: trust boundaries, data policy, sampling, failure model, Mermaid diagrams.
- `slo/`: SLI, error/result taxonomy, recording rules và burn-rate alerts mẫu.
- `runbooks/`: vận hành và rollback.
- `collector/` và `kubernetes/`: config mẫu với version pin, cần validate trong environment thật.
- `research.md`, `data-classification.md`, `threat-model.md`, `instrumentation.md`, `dashboards.md`, `cost-cardinality.md`, `rollout.md`, `adr.md`: evidence và policy nền tảng.
- `sample-usage.md`, `scenarios.md`: usage và edge-case contract cho phase 2.
- `configuration.schema.json`: JSON Schema cho `Observability` options và bounded/secure defaults.
- `phase6-owner-inputs.schema.json` và `phase6-owner-inputs.example.json`: contract đầu vào
  Phase 6B dạng redacted; template không phải approval và không chứa endpoint/secret value.
- `phase6-owner-inputs.md`: hướng dẫn packet, protocol mapping, privacy boundary và exact
  validator commands cho owner gate.
- `reference-code.md`: complete controller/application, gRPC seam, Kafka/RabbitMQ transport
  adapters, PostgreSQL/Redis adapters và reconciliation worker examples; broker/database
  bindings remain version-neutral until discovery supplies their clients.
- `scripts/verify_observability_phase6_preflight.sh`: non-mutating Phase 6A gate for exact image,
  config, rule, Kustomize, restore/build/test, vulnerability and owner-template evidence. The
  companion `scripts/verify_observability_phase6_owner_inputs.py --file <packet.json>` is the
  fail-closed gate for a real redacted owner packet. Phase 6B remains an owner-approved runtime
  canary; see the phase plan and validation report.
- `scripts/verify_observability_phase6_local_smoke.sh` plus `collector/local-smoke.yaml`: an
  ephemeral, local-only Collector health/OTLP/debug-export smoke. It never represents LGTM,
  Kubernetes or production evidence.
- `src/DataGuard.Core/Telemetry/ObservabilityFileSink.cs` and
  `tests/DataGuard.Core.Tests/ObservabilityFileSinkTests.cs`: product-native local sink,
  allowlist/redaction, daily archive and bounded/fail-open verification.

Buildable code: `src/DataGuard.Observability/`, `src/DataGuard.Observability.AspNetCore/` and `src/DataGuard.Observability.Messaging/`; the projects are now included in `DataGuard.sln` so the local build/test gate covers the reference slice. The internal packages pin `0.1.0` and include this README; concrete Kafka, RabbitMQ, gRPC and Redis bindings remain deferred because discovery found no corresponding client packages. Npgsql `10.0.3` exists in the existing PostgreSQL validation adapter, but service-level `Npgsql.OpenTelemetry` wiring remains an owner-gated compatibility adapter.
