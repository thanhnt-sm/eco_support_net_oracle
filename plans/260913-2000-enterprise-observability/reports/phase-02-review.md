# Phase 2 review

Review date: 2026-09-13. Scope: core SDK, ASP.NET Core boundary adapters and generic W3C
messaging propagation. This review was rerun after the phase-5 metric/tag consistency pass.

## Verdict: CAUTION

The generic adapter slice builds and its 30 contract/privacy/cardinality tests passed at the
phase-2 snapshot after the consistency patch. The implementation is suitable for continued
local contract work, not a production certification. Concrete gRPC/Kafka/RabbitMQ/Redis
bindings, service-level Npgsql telemetry wiring, live backend behavior and deployed data-policy
verification remain outside the discovered repository.

| Severity | Finding | Evidence | Disposition |
|---|---|---|---|
| High | Adapter does not bind Kafka/RabbitMQ concrete headers or retry/DLQ semantics | `src/DataGuard.Observability.Messaging/W3CMessagePropagation.cs`; discovery contains no Kafka/RabbitMQ client packages | Defer until client versions and retry contracts are discovered |
| Medium | Npgsql `10.0.3` is present in the existing PostgreSQL validation adapter but no service-level `Npgsql.OpenTelemetry` wiring is selected | `src/DataGuard.PostgreSql.Adapter/DataGuard.PostgreSql.Adapter.csproj`; service topology/data-source contract is unknown | Keep vendor-specific integration outside the shared package; validate `Npgsql.OpenTelemetry` and redaction in phase 6 |
| High | `CaptureExceptionDetails=true` can emit raw exception message/stack | `src/DataGuard.Observability/CoreObservability.cs` | Keep opt-in only; startup rejects it for `production`/`prod`, and gateway allowlist excludes stacktrace |
| Medium | Concrete gRPC interceptor and native duplicate-span checks are not present | no gRPC package or service in discovery | Defer; add version-specific adapter after discovery |
| Medium | Health/readiness filtering for generated runtime metrics is not verified against a deployed sample | ASP.NET trace filter is covered; runtime metric behavior depends on SDK/instrumentation | Defer to live metric-sample gate |
| Medium | Endpoint middleware and MVC action filter can both be enabled by a host | `src/DataGuard.Observability.AspNetCore/AspNetCoreObservability.cs` | Recursion guard prevents same-name double wrapping; document one boundary per request and add host-level integration test when a service exists |
| Resolved | Baggage escaping and malformed/oversized carrier behavior | `W3CMessagePropagation`; adapter tests cover URL escaping, allowlist and size limits | Closed in phase 5 |
| Resolved | Required messaging duration/lag/redelivery instruments were absent from the generic slice | `MessagingOperationMetrics`; finite system/result normalization test | Generic recorder added; client wiring remains version-gated |
| Resolved | Result classifier and production exception-detail policy were implicit | `IBusinessOperationResultClassifier`, finite vocabulary and production validation test | Hosts may inject a bounded classifier; raw details remain disabled |
| Resolved | Telemetry callbacks could theoretically replace a business result | Activity lifecycle, exception recording and classifier-warning paths | Explicit fail-open guards plus faulty-listener/classifier regression tests |
| Resolved | Project scope mismatch | `DataGuard.sln` now includes all three reference projects and the test project | Closed locally; owner still controls wider solution adoption |

## Evidence

- `dotnet test tests/DataGuard.Observability.Tests/DataGuard.Observability.Tests.csproj --configuration Release --no-build --no-restore`: 30 passed, 0 failed across three repeated runs.
- `dotnet build DataGuard.sln --configuration Release --no-restore`: 0 errors, 0 warnings.
- Default exception test confirms message/token/PAN material is absent from activity events and tags.
- Failure metric test confirms the operation count includes failed attempts and the failure
  counter uses finite result labels (`timeout|authorization_denial|validation_failure|technical_failure`)
  plus normalized `error.type` (`timeout|authorization|validation|technical`).

No critical defect was found in the tested generic path. The unresolved findings prevent a
production-readiness claim and are carried into the phase-5 residual-risk register.
