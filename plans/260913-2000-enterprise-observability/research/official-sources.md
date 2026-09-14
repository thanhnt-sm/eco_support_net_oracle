# Research report: .NET OTel and LGTM baseline

Date: 2026-09-13. Scope: version support, protocol and topology guidance needed by the plan.

## Findings

- Microsoft support policy places .NET 9 in STS with end of support on 2026-11-10; preserve current `net9.0` for compatibility and propose a separately scheduled .NET 10 LTS migration.
- OTel .NET documentation marks traces, metrics and logs stable and documents ASP.NET Core instrumentation, sampling, links and exception reporting.
- OTel Collector's official gateway pattern supports agent→gateway and trace-aware load balancing when tail sampling must see a complete trace.
- Collector processor stability varies by distribution; exact image validation is required before using memory limiter, redaction, transform or tail sampling.
- Loki's official OTLP ingestion uses Collector `otlphttp` to `/otlp`; structured metadata must be enabled according to Loki version.
- Tempo officially supports correlation with Grafana, Loki and Mimir, but links require explicit data-source/exemplar/derived-field configuration.
- The official Collector release page lists contrib `v0.160.0` as the selected stable release on
  2026-09-02; the repository pins and validates its image digest rather than using a floating tag.
- The official Prometheus rules documentation defines `promtool check rules` and rule unit tests;
  the selected validator image is Prometheus `v3.13.1`.
- Current OTel deployment semantic conventions use stable `deployment.environment.name`; the
  deprecated `deployment.environment` key is not used by the implementation.
- Existing repository discovery includes Npgsql `10.0.3` in the PostgreSQL validation adapter;
  the official `Npgsql.OpenTelemetry 10.0.3` package targets .NET 8 and is computed compatible
  with `net9.0`. Npgsql 10 changes tracing/metric names, so service-level wiring remains a
  compatibility adapter pending redaction and workload verification.

## Decision impact

Use exact pinned package versions in the starter, use the validated contrib image only as a
repository target artifact, keep backend activation deferred until platform facts arrive, and
block production claims until restore, config validation, privacy tests and workload benchmarks run.

Sources and links are maintained in `docs/observability/research.md`.

`ck:docs-seeker` scripts were also run for the OpenTelemetry query; Context7 returned `Documentation not found`, so official OpenTelemetry/Grafana/Microsoft web documentation was used as fallback. No unsupported API claim was accepted without a successful local compile.
