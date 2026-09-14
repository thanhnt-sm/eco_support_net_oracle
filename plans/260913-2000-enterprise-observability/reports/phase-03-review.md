# Phase 3 review — Collector and Kubernetes

Date: 2026-09-13
Verdict: CAUTION / local artifact gate passed; deployment gate remains blocked by missing owner inputs.

## Evidence

| Check | Result |
|---|---|
| Collector distribution | `otel/opentelemetry-collector-contrib` 0.160.0, manifest digest `sha256:799dc6cf12c96192af37b5bdba804da8c10b3bc563b43cb90c3f3c58d9572ad6` |
| Agent config validation | `otelcol-contrib validate` in pinned image: exit 0 |
| Gateway config validation | `otelcol-contrib validate` in pinned image: exit 0 |
| Component presence | `batch`, `k8sattributes`, `memory_limiter`, `redaction`, `tail_sampling`, `load_balancing`, `otlphttp`, `health_check`: present in image component inventory |
| Collector self-observability | Both configs use the exact 0.160.0 `service.telemetry.metrics.readers.pull.exporter.prometheus` schema on `0.0.0.0:8888`; Kubernetes NetworkPolicy restricts scrape namespaces |
| YAML syntax | Ruby Psych parsed all Collector and Kubernetes YAML files: exit 0 |
| Kubernetes render | `kubectl kustomize docs/observability/kubernetes` plus source/deployment-copy `cmp`: exit 0 |
| Cluster validation | `NOT EXECUTED`: `kubectl` client exists but no API server/approved cluster is available |

## Red-team findings

1. The first draft incorrectly used `routing_key: traceID` for metrics. The pinned Collector rejected it; the config now uses trace-ID routing only for traces and service routing for metrics/logs.
2. The redaction processor is beta for traces and alpha for logs/metrics in this distribution. Application-side allowlists and canary tests remain mandatory; this is not a compliance boundary by itself.
3. `tail_sampling` is stateful. Gateway replicas require a stable trace-aware route; the agent uses the Kubernetes resolver plus a headless gateway Service. EndpointSlice RBAC and CNI egress rules must be tested in the target cluster.
4. Sending queues and retry windows are bounded and in-memory. Queue exhaustion can drop telemetry; it cannot be described as zero loss. Persistent queues are deferred until RPO, disk encryption and recovery tests are approved.
5. NetworkPolicy uses explicit namespace labels rather than `{}` wildcard selectors. The platform owner must label approved ingress/backend namespaces and add a narrowly scoped control-plane egress rule where required by the CNI.
6. Backend endpoint URLs, tenant identity and certificates are Secret references. No credentials are embedded. Loki requires OTLP `/otlp` plus structured metadata support; Mimir protocol and tenant contract still require owner verification.
7. A validation pass caught that the legacy `service.telemetry.metrics.address` key is rejected by
   0.160.0. It was replaced with the image's `readers.pull.exporter.prometheus` schema and both
   configs were revalidated successfully.
8. RBAC now separates the read-only gateway service/endpointslice Role from the agent's
   read-only ClusterRole for the metadata required by `k8sattributes`; CNI control-plane egress
   remains an owner-supplied cluster-specific rule.

## Decision and next plan

Proceed to phase 4 with the local Collector artifact marked validated. Do not apply Kubernetes manifests or call the platform production-ready. Phase 4 must define owner-independent SLI/alert templates with explicit metric verification gates and dashboard/runbook contracts.
