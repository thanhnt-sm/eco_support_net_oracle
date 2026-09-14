# Collector deployment contract

The selected target is the two-tier agent → gateway topology. The exact distribution is
`otel/opentelemetry-collector-contrib:0.160.0` with manifest-list digest
`sha256:799dc6cf12c96192af37b5bdba804da8c10b3bc563b43cb90c3f3c58d9572ad6` (resolved with
Docker on 2026-09-13). The digest is used by the Kubernetes artifacts; do not replace it
with `latest` or a floating tag.

`agent.yaml` uses OTLP gRPC/HTTP with a receiver TLS certificate, `memory_limiter`,
`k8sattributes`, the alpha `filter/drop-probes` metric guard, `batch`, and a
trace-ID-aware `load_balancing` exporter to the gateway headless Service. Metrics and logs
use service-name routing because the Collector rejects `traceID` for metrics. The gateway
uses an mTLS OTLP receiver, `redaction`, `tail_sampling`, and trace-aware TLS load balancing to the Tempo distributor;
metrics use OTLP/HTTP to Mimir and logs use OTLP/HTTP to Loki `/otlp`.

Both configs expose Collector self-observability metrics on `0.0.0.0:8888` (the Kubernetes
manifests restrict that port to namespaces labelled `observability.metrics-scrape=true`).
Scrape `otelcol_*` accepted/refused/export-failure and queue metrics separately from business
SLIs; a telemetry-pipeline alert is not a business availability event.

The redaction processor is beta for traces and alpha for logs/metrics in the selected
contrib distribution. It is a defence-in-depth layer, not a substitute for application
allowlists and sensitive-data tests. Backend endpoints, tenant identity, and certificates
are supplied by Kubernetes Secret references; `insecure: true` is intentionally absent.

Both tiers use bounded in-memory queues and bounded retry windows. Persistent queue storage
is not enabled until the owner supplies RPO, disk class, encryption and recovery tests.

Queue sizing: `disk_bytes = peak_items_per_second × average_item_bytes × outage_seconds ×
safety_factor`. Validate against limits and observe the exact distribution's queue/refused/
dropped metrics. A full queue drops telemetry according to exporter policy; it never blocks a
business request or implies zero telemetry loss.

Validation performed with the pinned image:

```sh
docker run --rm \
  -v "$PWD/docs/observability/collector/agent.yaml:/etc/otel/agent.yaml:ro" \
  otel/opentelemetry-collector-contrib@sha256:799dc6cf12c96192af37b5bdba804da8c10b3bc563b43cb90c3f3c58d9572ad6 \
  validate --config=file:/etc/otel/agent.yaml

docker run --rm \
  -e LOKI_OTLP_ENDPOINT=https://loki.example.invalid/otlp \
  -e MIMIR_OTLP_ENDPOINT=https://mimir.example.invalid/otlp \
  -e GRAFANA_TENANT_ID=platform \
  -v "$PWD/docs/observability/collector/gateway.yaml:/etc/otel/gateway.yaml:ro" \
  otel/opentelemetry-collector-contrib@sha256:799dc6cf12c96192af37b5bdba804da8c10b3bc563b43cb90c3f3c58d9572ad6 \
  validate --config=file:/etc/otel/gateway.yaml
```

Both commands exited `0` locally. Kubernetes server-side validation and backend outage
tests remain `NOT EXECUTED` because no target cluster/backend contract is present.

## Local process smoke

`local-smoke.yaml` is a development-only configuration with loopback plaintext OTLP and the
`debug` exporter. Run the reproducible smoke command to start an ephemeral container, probe
`health_check`, send one synthetic trace and verify that the debug exporter receives it:

```sh
./scripts/verify_observability_phase6_local_smoke.sh
```

This proves only that the selected Collector binary can start and accept an OTLP trace locally;
it does not prove TLS, backend protocol/tenant routing, queue recovery, residency or Kubernetes
admission. The agent/gateway configs remain the deployment contract.
