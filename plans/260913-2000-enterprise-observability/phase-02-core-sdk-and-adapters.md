---
phase: 2
title: "Core SDK and adapters"
status: completed
priority: P1
effort: "1d"
dependencies: [1]
---

# Phase 2: Core SDK and adapters

## Overview

Complete hosting, logging correlation and workload-specific adapters only where the repository's actual service clients require them.

## Requirements

- ASP.NET Core middleware/filter, minimal API endpoint filter, gRPC interceptor, worker decorator and messaging adapters are distinct.
- Propagate W3C traceparent/tracestate; strip untrusted baggage at boundaries; use links for batch/fan-in.
- Do not record SQL parameters, Redis values, message bodies or dynamic IDs.
- Middleware must be opt-in through endpoint metadata; unannotated health/readiness/metrics endpoints remain noise-free.
- Messaging propagation must cap header size, accept malformed context as a new root, strip baggage except approved keys, and expose retry attempt metadata without message payload.

## Related Code Files

- Create: `src/DataGuard.Observability.AspNetCore/`
- Create: `src/DataGuard.Observability.Messaging/`
- Create: `tests/DataGuard.Observability.IntegrationTests/`
- Create: `docs/observability/scenarios.md`

## Implementation Steps

1. Add `DataGuard.Observability.AspNetCore` with endpoint metadata, middleware, minimal API filter and trace/log scope correlation.
2. Add `DataGuard.Observability.Messaging` with W3C trace context inject/extract, baggage allowlist and bounded carrier handling; keep Kafka/RabbitMQ client bindings as later adapters.
3. Add tests for opt-in boundaries, cancellation, malformed/oversized headers, baggage stripping, duplicate calls and sensitive-data absence.
4. Update package locks and docs; run build, test, format and vulnerability gates.

## Success Criteria

- [x] ASP.NET Core boundary, MVC attribute/filter, generic W3C messaging propagation and
  recursion guard, finite messaging metrics, malformed-carrier handling, log correlation and
  fail-open Activity/classifier guards and canonical failure taxonomy build; 30 reference tests pass.
- [ ] gRPC/Kafka/RabbitMQ/Redis client-specific adapters and duplicate native instrumentation tests remain pending because client packages/topology are absent from discovery. Npgsql `10.0.3` exists only in the existing PostgreSQL validation adapter; service-level `Npgsql.OpenTelemetry` wiring remains owner-gated.
- [x] Public XML API and package locks restore/build reproducibly for the three new projects.

## Current validation

`dotnet build` succeeds for the core, ASP.NET Core and messaging projects. `dotnet test tests/DataGuard.Observability.Tests/DataGuard.Observability.Tests.csproj --configuration Release --no-build --no-restore` reports 30 passed. Review is recorded in `plans/260913-2000-enterprise-observability/reports/phase-02-review.md`. This completes the generic adapter slice; it does not certify production messaging clients.
