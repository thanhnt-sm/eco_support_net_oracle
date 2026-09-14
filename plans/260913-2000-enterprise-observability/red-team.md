# Red Team Review

## Verdict: CAUTION

Five hostile lenses were applied to the plan and starter code. The plan can proceed for contract hardening and local validation, but production rollout is gated on owner-supplied topology, backend, data classification, traffic, retention and SLO evidence.

| Topic | Architect | Security | Performance | UX/Operations | Devil's Advocate | Resolution |
|---|---|---|---|---|---|---|
| SDK in DataGuard | Separate package preserves existing CLI behavior | New exporter can leak data | Instrumentation adds allocation/CPU | Operators need kill switch | DataGuard is not banking runtime | Accept bounded package; do not add to solution until scope owner decides |
| OTLP exporter | Protocol is portable | TLS/auth must be explicit | Queue must be bounded | Backend outage must be visible | OTLP package vulnerability can block restore | Accept exact pinned versions plus vulnerability gate |
| Business wrapper | Stable operation boundary | No arguments/returns | Stopwatch + instruments are measurable | Error classification is useful | Wrapper can double-record with native spans | Add recursion/double-record tests before adapters |
| Agent→gateway | Failure domains and tail sampling scale | Trust boundary at gateway | Queue and sampling cost money | Self-observability required | Unknown topology makes config fiction | Defer deployment artifact activation pending platform facts |
| Profiling | Independent data plane | Symbols/source are sensitive | Profiler overhead may violate SLO | Correlation links need setup | Flame graph does not prove source line | Defer profiler; canary and kill switch mandatory |

## Findings and dispositions

| Finding | Severity | Evidence | Disposition | Action |
|---|---|---|---|---|
| Existing custom NDJSON telemetry may duplicate OTel metrics | High | `_observability_discovery/observability-current-state.md:16-25` | Accept | Keep package opt-in; define one primary span-metrics path before enabling both. |
| Telemetry properties/tags have no existing allowlist | High | `_observability_discovery/security-and-data-handling.md:38-49` | Accept | New package finite tags; audit old collector before migration. |
| .NET 9 will leave support soon | High | `_observability_discovery/dependencies-and-build.md:1-20` | Accept | Preserve net9 compatibility; separate net10 LTS migration proposal. |
| Collector/backend topology is unknown | Critical | `_observability_discovery/gaps-and-unknowns.md:3-15` | Accept | Block production deployment; require owner evidence. |
| Activity API drift | Medium | `src/DataGuard.Observability/CoreObservability.cs:81` | Accept | Build catches obsolete API; use `AddException` only for explicit exception-detail opt-in. |

## Explicit non-goals

No claim of 99.99% availability, zero telemetry loss, source-line profiling, audit-ledger semantics, or universal instrumentation coverage is allowed without evidence.

## Next steps

Run `ck plan validate plans/260913-2000-enterprise-observability/plan.md` after installing `ck`, then execute phase 1 tests. Do not commit or deploy automatically.

## Product-scope red-team re-review (2026-09-14)

The owner clarified that the product is a CLI/library, not a backend/frontend and not a Docker
workload. The original server-centric Phase 6 assumptions were therefore challenged again:

| Requirement / assumption | Disposition | Rationale | Replacement design |
|---|---|---|---|
| Every signal must leave the process through OTLP/Collector | MODIFIED | No backend, endpoint or deployment runtime exists in discovery; forcing egress would change product behavior | Native `FileObservabilitySink` writes a bounded standard envelope to daily NDJSON; remote OTLP is optional |
| Start Docker/Collector/Kubernetes to prove the product works | REJECTED for product phase | Docker/cluster success is not evidence for a CLI/library execution path and adds external state | Run direct .NET build/tests and a local archive smoke; retain deployment artifacts as optional reference |
| Open health/diagnostic endpoints by default | MODIFIED | Product does not require an HTTP surface; default listeners/routes increase attack surface | `HealthHostOptions.ExposeEndpoints=false`; existing host test opts in explicitly |
| Save all raw event details for debugging | REJECTED | Details can carry PAN, tokens, account/customer IDs and unbounded cardinality | Omit body by default; optional body is redacted and capped; attributes are fail-closed allowlist |
| Treat observability text as an audit ledger | REJECTED | NDJSON append has no immutable hash-chain or regulatory retention guarantee | Keep `FileAuditLogger` as a separate integrity-controlled path |
| Enable remote endpoint when `ExportEndpoint` is present | MODIFIED | A configured URL is not owner authorization and may cause data egress | `RemoteExportEnabled=false` default; injected test sink remains compatible |
| Promise zero telemetry loss from local archive | REJECTED | Bounded queues and process/disk failure make zero loss unprovable | Measure queue drops/terminal loss and document at-least-once append behavior |

Verdict remains **CAUTION**: the local product path is implementable and testable, while remote
Collector/LGTM/profiling behavior remains `NOT EXECUTED` and requires a separate approved runtime
contract. Observability still measures and diagnoses the product; it does not create or guarantee a
99.99% SLA.
