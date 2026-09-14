# Dashboard and cross-signal navigation

The backend-neutral catalog is [`dashboards/dashboard-catalog.yaml`](dashboards/dashboard-catalog.yaml).
It defines the required views: executive SLO, business journeys, service RED, dependencies,
messaging, .NET runtime, Collector health, telemetry quality, cardinality/cost, profiling and
security/privacy. It is intentionally a catalog rather than a guessed Grafana export; the
platform owner must bind datasource UIDs, tenant filters and verified metric names.

Navigation requires explicit Grafana data-source links:

1. SLO panel → operation panel → exemplar/TraceId → Tempo trace.
2. Tempo trace → derived field `trace_id` → Loki query constrained by service and time range.
3. Trace → Pyroscope profile only when the profiler emits matching service/version/time
   metadata and the backend supports the link; a flame graph alone does not prove source-line
   fidelity.
4. Trace → deployment/change dashboard by `service.version` and release identifier.

No link is assumed to exist merely because LGTM products are deployed. Exemplar preservation,
Tempo derived fields and Pyroscope correlation must be verified in an integration environment;
they are `NOT EXECUTED` for the current repository.
