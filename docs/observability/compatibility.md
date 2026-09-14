# Research and compatibility matrix

Ngày discovery/selection: 2026-09-13. The current system is preserved: no target framework,
runtime, existing logging provider or backend is replaced. “Current” means detected in the
repository or explicitly `absent/unknown`; it is never inferred from the prompt.

| Component | Current version | Selected implementation | Recommended target-state | Stability / support | Compatibility evidence | Decision | Risk |
|---|---|---|---|---|---|---|---|
| .NET SDK/runtime | 9.0.310 / 9.0.12 | net9.0 preserved | net10.0 LTS migration proposal | .NET 9 STS EOS 2026-11-10; .NET 10 LTS | local `dotnet --info`; [Microsoft policy](https://dotnet.microsoft.com/en-us/platform/support/policy) | Preserve / Major Upgrade Proposed | security/support window; separate migration |
| ASP.NET Core | 9.0 shared framework | `Microsoft.AspNetCore.App` framework reference | net10.0 after compatibility project | vendor-supported with runtime | local framework list and successful build | Preserve | no current web host in discovery |
| OpenTelemetry API/SDK | absent | 1.18.0 | current stable compatible with target runtime | stable API/SDK | NuGet restore + compile; [OTel .NET docs](https://opentelemetry.io/docs/languages/dotnet/) | Add adapter | upstream semantic changes |
| OTel hosting integration | absent | 1.18.0 | current stable | stable package | NuGet restore + compile | Add adapter | provider lifecycle/configuration |
| OTel logging provider | existing logging stack; no OTel provider | 1.18.0 via `OpenTelemetry` + OTLP exporter | current stable | stable API; async exporter | exact net9.0 assembly compile; bounded log queue | Add opt-in provider | formatted body/privacy policy and existing-provider coexistence require live verification |
| ASP.NET Core instrumentation | absent | 1.12.0 | current stable compatible package | stable instrumentation package | NuGet restore + compile | Add adapter | duplicate native Activity risk |
| HttpClient instrumentation | absent | 1.12.0 | current stable compatible package | stable instrumentation package | NuGet restore + compile | Add adapter | route/filter policy needs integration test |
| gRPC instrumentation | no package detected | none | version matching discovered Grpc.Net.Client | unverified | no gRPC client/server package in discovery | Defer | adding a package would change dependency graph |
| Runtime instrumentation | absent | 1.18.0 | current stable | stable package | NuGet restore + compile | Add adapter | metric names require sample verification |
| Process instrumentation | not selected | none | add only if workload needs it | unverified | no requirement or sample | Defer | duplicate host metrics |
| Npgsql/PostgreSQL instrumentation | Npgsql 10.0.3 in `DataGuard.PostgreSql.Adapter` (also transitively reachable from CLI/tests) | None in the shared package; vendor-specific wiring is intentionally not shipped | `Npgsql.OpenTelemetry` 10.0.3 at the approved service/data adapter, subject to owner pinning | stable package; Npgsql 10 tracing/metrics align with OTel conventions but include breaking metric/tag changes | local `DataGuard.PostgreSql.Adapter.csproj`/lock file; [Npgsql.OpenTelemetry 10.0.3](https://www.nuget.org/packages/Npgsql.OpenTelemetry); [Npgsql 10.0 release notes](https://github.com/npgsql/doc/blob/main/conceptual/Npgsql/release-notes/10.0.md) | Defer | service data source, connection-string redaction and workload path still unknown |
| Redis instrumentation | no Redis package detected | none | adapter matching discovered client | unverified | discovery package scan | Defer | client API and payload policy |
| Kafka instrumentation | no Kafka package detected | none | adapter matching discovered client | unverified | discovery package scan | Defer | headers/retry semantics are client-specific |
| RabbitMQ instrumentation | no RabbitMQ package detected | none | adapter matching discovered client | unverified | discovery package scan | Defer | ack/redelivery semantics are client-specific |
| OTLP exporter | absent | `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.18.0 | current stable | stable exporter | restore/build and vulnerability scan clean | Add adapter | endpoint/TLS contract absent |
| Internal observability package | absent | `DataGuard.Observability*` 0.1.0 | 1.x after client adapters and runtime gates | pre-1.0 stable SemVer; no prerelease suffix | `dotnet pack` creates pinned packages with README | Add internal package | API may change during owner-gated integration |
| Collector distribution | absent | contrib 0.160.0, digest `sha256:799dc6cf12c96192af37b5bdba804da8c10b3bc563b43cb90c3f3c58d9572ad6` | platform-approved supported release | component maturity varies; redaction beta traces/alpha logs+metrics | Docker pull, component inventory, agent/gateway `validate` exit 0 | Add target artifact | no cluster/backend; image policy owner gate |
| Collector receivers/processors/exporters | absent | OTLP, memory_limiter, k8sattributes, filter/drop-probes, redaction, tail_sampling, batch, load_balancing, otlphttp, health_check | same set only after owner review | verified present in exact image; filter/redaction/load-balancing have maturity caveats | pinned image `components` output + config validation | Add target artifact | config compatibility with future upgrades |
| Loki | absent | no binary; OTLP/HTTP exporter endpoint `/otlp` template | owner-selected Loki release | version unknown | [Loki OTLP docs](https://grafana.com/docs/loki/latest/send-data/otel/) | Defer backend | structured metadata/retention/tenant unknown |
| Tempo | absent | no binary; OTLP/gRPC via trace-ID load balancing template | owner-selected Tempo release | version unknown | [Tempo config docs](https://grafana.com/docs/tempo/latest/configuration/) | Defer backend | backend topology and metrics generator unknown |
| Mimir | absent | no binary; OTLP/HTTP metrics template | owner-selected Mimir release or remote-write contract | version unknown | protocol intentionally owner-verified | Defer backend | OTLP vs remote-write and tenant limits unknown |
| Pyroscope | absent | no runtime profiler selected | native .NET profiler/eBPF evaluation | version/platform unknown | separate profiling ADR; no code claim | Defer | overhead, symbols, privilege, data residency |
| Prometheus rule tooling | absent host tool | 3.13.1 image digest `sha256:3c42b892cf723fa54d2f262c37a0e1f80aa8c8ddb1da7b9b0df9455a35a7f893` (validator only) | platform rule evaluator version | stable release used for syntax test | `promtool check/test rules` exit 0 | Add validation tool | live metric labels/buckets unverified |
| Grafana | absent | none | owner-selected Grafana LGTM version | unknown | no deployment detected | Defer | datasource UIDs/permissions unknown |
| Kubernetes/Helm/Kustomize | `kubectl` 1.36.1/Kustomize 5.8.1 present; Helm absent | Kustomize YAML + server-side command contract | owner-selected cluster/API version | unverified | Ruby YAML parse, `kubectl kustomize` and config-copy comparison pass; no cluster | Defer apply | API/CNI/RBAC/secret contracts unknown |

## Selection rules

1. Preserve the detected net9.0 surface and existing custom metrics/NDJSON/audit paths.
2. Pin every package/image/tool used in the artifacts; no `latest`, wildcard or floating range.
3. Prefer stable components; experimental or alpha pieces are isolated behind an owner gate and
   feature/kill switch.
4. A successful parse/compile proves syntax and package compatibility only. Backend semantics,
   SLO labels, TLS, tenant isolation and workload overhead require integration evidence.
5. .NET 10 is a target-state proposal, not a hidden observability dependency. Its migration has
   to be planned, benchmarked and rolled back independently.
