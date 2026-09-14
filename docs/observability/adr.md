# Architecture Decision Records

## ADR-001 — Discover before pin

Status: Accepted. Preserve discovered `net9.0` baseline and pin new packages only after restore/audit. .NET 10 LTS is a separate migration proposal. Rollback: remove package registration.

## ADR-002 — SDK plus explicit adapters

Status: Accepted. Use OTel SDK for native ASP.NET/HTTP/runtime signals; use middleware, endpoint filters, gRPC interceptors, messaging decorators and worker wrappers at selected boundaries. Automatic instrumentation is a canary option due profiler/AOT/hidden-flow risks.

## ADR-003 — Declarative business boundary

Status: Accepted. `ObservedOperationDescriptor` and `IBusinessOperationObserver` are the vendor-neutral contract. No universal base class or method weaving.

## ADR-004 — Agent → gateway Collector

Status: Proposed. Agent isolates service failure; gateway centralizes redaction/tail sampling. Blocked until topology/backend/tenant contract is supplied.

## ADR-005 — Parent-based head sampling with explicit tail-candidate mode

Status: Proposed. Default is a bounded parent-based head ratio (10%). Gateway tail sampling
operates only on received candidates. For complete error/latency/critical candidate coverage,
the host must explicitly set `UseAlwaysOnHeadSamplingForTailSampling=true` and
`TraceSamplingRatio=1.0`; this is a capacity-reviewed incident or service-class override, never
the default. Rollback is to disable the flag and restore the approved ratio.

## ADR-006 — Allowlist and fail-open transport

Status: Accepted. No payloads/credentials/IDs in telemetry; bounded asynchronous queues, timeout/retry/drop counters. Local invalid configuration fails startup; backend outage never fails business operation.

## ADR-007 — Finite metric dimensions

Status: Accepted. Operation/result/dependency/error.type/banking.slo.class only after normalization. Never use trace/span/customer/account/message IDs as labels.

## ADR-008 — Profiling is an independent plane

Status: Proposed. Evaluate Pyroscope native profiler/eBPF separately with canary, kill switch, privilege and symbol policy. `AddCoreObservability` does not collect profiles.

## ADR-009 — Trace/profile correlation is conditional

Status: Accepted. Links require backend-supported exemplars/derived fields and aligned time/service metadata; flame graph source line is not promised without symbols/source mapping.

## ADR-010 — Diagnostic logs are not audit ledger

Status: Accepted. Existing hash-chain audit path remains separate; Loki/diagnostic logs cannot substitute immutable, tamper-evident compliance records.

## ADR-011 — One span-metrics implementation

Status: Proposed. Choose Collector connector or backend metrics generator after backend discovery; do not enable both duplicate producers.

## ADR-012 — Tenant isolation

Status: Proposed. Tenant labels only when bounded and approved; route/authenticate by tenant at gateway/backend and test cross-tenant queries.

## ADR-013 — Bounded loss model

Status: Accepted. Queue sizing follows peak rate × payload × outage duration × safety factor. Drops, 429, 5xx, restart and disk-full are measured; zero loss is not claimed.

## ADR-014 — Local text archive is the product primary

Status: Accepted. DataGuard is a CLI/library with no required backend or Docker runtime. Existing
automatic `TelemetryCollector` calls feed `FileObservabilitySink`, which writes an allowlisted
OpenTelemetry-mapped NDJSON envelope under a UTC-day archive path. This text envelope is not OTLP
wire data and is not an audit ledger. Rollback is `FileSinkEnabled=false` or the existing injected
export delegate; the Meter instruments remain unchanged.

## ADR-015 — Endpoint surfaces are opt-in and temporarily locked

Status: Accepted. `TelemetryConfig.RemoteExportEnabled`,
`CoreObservabilityOptions.RemoteExportEnabled` and `HealthHostOptions.ExposeEndpoints` default to
false. The legacy OTLP exporter and host health routes remain compiled for compatibility tests and
future owner-approved adapters, but the CLI/library does not open or call them implicitly.
