# Configuration contract

The optional service package binds `Observability` configuration. Environment variables use the
standard double underscore mapping, for example `Observability__Enabled=true` and
`Observability__ServiceName=payments`. DataGuard itself is a CLI/library: its primary path is the
core `FileObservabilitySink` described in [local-file-observability.md](local-file-observability.md).
The OTel/OTLP settings below apply only to a separately hosted compatibility adapter.

`AddCoreObservability` is idempotent and first registration wins. Register it once in the
composition root; a later call is ignored after validating its local options and cannot switch
an already-registered provider from disabled to enabled.

Secure defaults are disabled remote export (`RemoteExportEnabled=false`), HTTPS-only OTLP when
explicitly enabled, 10% parent-based head sampling, bounded trace/log batch queues
(`MaxQueueSize`, bounded batch size and 5-second exporter timeout), structured log state without
formatted message, and exception details off. Startup rejects missing service name, out-of-range
queue/sampling, unsafe resource values, non-HTTPS endpoints or inline endpoint
credentials/query/fragment; it also rejects `CaptureExceptionDetails=true` when the deployment
environment is `production`/`prod`. Backend availability is never checked during startup. A
production overlay must supply TLS/auth through secret references and must not inline credentials.
The checked-in agent/gateway receiver contracts are optional reference artifacts only.

`UseAlwaysOnHeadSamplingForTailSampling=true` is an explicit cost/capacity override. It requires
`TraceSamplingRatio=1.0`; otherwise startup fails. Use it only when the gateway has a validated
tail-sampling policy and capacity budget. Without this mode, gateway tail-sampling can retain
only traces that survived the SDK's 10% (or configured) head sample; it cannot recover a trace
that was never exported by the application.

The optional resource fields are `ServiceNamespace`, `ServiceInstanceId`,
`DeploymentEnvironment`, `ClusterName`, `KubernetesNamespace` and an opaque
`WorkloadIdentity`. They are bounded resource metadata only; do not enable backend resource-to-
metric-label conversion for pod, instance, tenant or identity values without a series-budget
review. Kubernetes metadata enrichment remains a Collector concern and is controlled by the
agent ServiceAccount/RBAC policy.

ASP.NET Core and HttpClient trace instrumentation explicitly disables automatic exception-event
capture and does not install custom enrichers. Their package-level URL query redaction remains
enabled. ASP.NET Core trace instrumentation suppresses `/health*` and `/metrics*` probe requests to
avoid probe-driven trace noise. Framework meters are version-specific; the checked-in agent and
gateway apply an alpha `filter/drop-probes` processor to datapoints whose verified route template
matches those paths. Because route attribute names can vary by instrumentation version, capture
one real sample and adjust/approve that filter before activation. Probe failures remain visible
through the host's health/readiness result and structured application/Collector
self-observability; they are not silently treated as business-operation failures.

When `Enabled=true`, `LogsEnabled=true`, `RemoteExportEnabled=true` and an OTLP endpoint is
supplied, the optional hosting builder registers one asynchronous OpenTelemetry logger provider in
addition to the existing logging stack. `IncludeScopes` only carries structured scopes; it does
not create `trace_id` or `span_id`. The active `Activity` supplies those correlation fields.
Formatted log bodies are disabled by default, and the Collector redaction allowlist remains the
final defence-in-depth layer. Core diagnostic events use stable EventIds `7001` (operation failure)
and `7002` (result classifier failure). If the switch is false, no network exporter is registered.

## Local file sink settings

`TelemetryConfig.FileSinkEnabled=true` is the default when no exporter delegate is injected. Set
`FileSinkDirectory` to an operator-owned directory; otherwise the OS local application-data path is
used. Daily files are UTF-8 NDJSON under `yyyy/MM/dd/`, with `MaxQueuedEvents`, `MaxPayloadBytes`
and `MaxRecordBytes` bounds. Event bodies are omitted unless `IncludeEventDetails=true`; even then
they are redacted and capped. Retention and deletion are intentionally outside the sink and must be
owned by the product operator.
