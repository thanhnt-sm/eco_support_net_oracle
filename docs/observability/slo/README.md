# SLI/SLO and alerts

SLA is the external contractual commitment; SLO is the internal target; SLI is the measured
good/valid event ratio or latency compliance; the error budget is `1 - SLO` over the window.
Observability measures and alerts on these quantities but cannot create or guarantee a 99.99%
service SLA.

| Journey | Good event | Bad event | Valid event | Latency threshold | Window | SLO | Exclusions | Owner |
|---|---|---|---|---:|---|---:|---|---|
| Authentication/login | authenticated response | technical failure/timeout/unknown | policy-valid, non-canceled attempts | 750 ms | 30d | 99.99% | invalid credentials, authorization policy | IAM owner |
| Account information query | authorized response | technical failure/timeout/unknown | policy-valid, non-canceled queries | 500 ms | 30d | 99.99% | business not-found | Account owner |
| Transfer initiation | accepted for processing | technical failure/timeout/unknown | validated/authorized, non-canceled requests | 500 ms | 30d | 99.99% | validation, business rejection, authz denial | Payments owner |
| Transfer approval | approval recorded | technical failure/timeout/unknown | eligible approvals | 1 s | 30d | 99.9% | maker-checker rejection | Payments owner |
| Transfer completion | persisted completed state | technical failure/timeout/unknown | accepted transfers | 5 min | 30d | 99.9% | explicit business rejection, duplicate no-op | Payments owner |
| Message processing | persisted processed/rejected outcome | technical failure/DLQ/expired retry | accepted messages | 5 min end-to-end | 30d | 99.9% | duplicate/replay idempotent no-op | Messaging owner |
| Settlement | settlement batch complete | technical failure/mismatch | scheduled batches | 15 min | 30d | 99.9% | approved market-calendar exclusion | Settlement owner |
| Reconciliation | completed with zero mismatch | technical failure/mismatch | scheduled runs | 15 min | 30d | 99.9% | approved source-system outage | Finance owner |

## Error/result taxonomy

The following is the default classification contract. HTTP and gRPC mappings are examples to
be confirmed against each service's public API. The generic technical-availability template
excludes cancellation, invalid-input and authentication/authorization outcomes from its valid
denominator; a handled business rejection remains a valid good response there. Acceptance,
completion or settlement SLOs must apply their journey-specific valid/good selector (for
example, exclude `business_rejection` when only accepted transfers count). `Unset` span status
is intentional for expected client/business outcomes; `Error` is reserved for technical impact.

| Classification | HTTP example | gRPC example | Business result label | Span status | Log level | Metric result | Availability SLO |
|---|---|---|---|---|---|---|---|
| Success | 200 | `OK` | `success` | Unset | Information | `success` | Good |
| Accepted for processing | 202 | `OK` | `accepted` | Unset | Information | `accepted` | Good for acceptance; completion measured separately |
| Eventually completed | 200 | `OK` | `eventually_completed` | Unset | Information | `eventually_completed` | Good for completion |
| Business rejection | 409/422 per contract | `FAILED_PRECONDITION`/`ALREADY_EXISTS` per contract | `business_rejection` | Unset | Information | `business_rejection` | Valid, not technical bad |
| Validation failure | 400/422 | `INVALID_ARGUMENT` | `validation_failure` | Unset | Information | `validation_failure` | Excluded |
| Authentication failure | 401 | `UNAUTHENTICATED` | `authentication_failure` | Unset | Warning | `authentication_failure` | Excluded from service availability; security event is separate |
| Authorization denial | 403 | `PERMISSION_DENIED` | `authorization_denial` | Unset | Warning | `authorization_denial` | Excluded |
| Technical failure | 500 | `INTERNAL`/`UNKNOWN` | `technical_failure` | Error | Error | `technical_failure` | Bad |
| Timeout | 504 or gateway timeout | `DEADLINE_EXCEEDED` | `timeout` | Error | Error | `timeout` | Bad |
| Dependency unavailable | 503 | `UNAVAILABLE` | `dependency_unavailable` | Error | Error | `dependency_unavailable` | Bad when user journey is affected |
| Expected cancellation | client disconnect/499 convention | `CANCELLED` | `cancellation` | Unset | Information | `cancellation` | Excluded |
| Concurrency conflict | 409 | `ABORTED` | `concurrency_conflict` | Unset | Information | `concurrency_conflict` | Valid; owner decides business SLI |
| Duplicate/replay | 409 | `ALREADY_EXISTS` | `duplicate` | Unset | Information | `duplicate` | Valid/idempotency signal |
| Idempotent no-op | 200/202 | `OK` | `idempotent_no_op` | Unset | Information | `idempotent_no_op` | Good when domain contract says effect already exists |
| Unknown failure | 500 | `UNKNOWN` | `unknown` | Error | Error | `unknown` | Bad until classified |

## Instrument and metric contract

The names below are the intended OTel instrument contract. Prometheus normalization is a
syntax expectation for the custom instruments, not live backend evidence; every row remains
`Verified from sample = No` until an exact deployed exporter sample is captured. Runtime,
dependency and messaging metrics are integration contracts and are deliberately not fabricated
by the generic package.

| OTel instrument | Unit | Attributes | Prometheus-normalized name | Verified from sample |
|---|---|---|---|---:|
| `business.operation.count` | `{operation}` | `operation`, `result` | `business_operation_count_total` | No |
| `business.operation.failure.count` | `{failure}` | `operation`, `result`, `error.type` | `business_operation_failure_count_total` | No |
| `business.operation.duration` | `ms` | `operation`, `result` | `business_operation_duration_milliseconds_{bucket,count,sum}` | No |
| `messaging.processing.duration` | `ms` | `operation`, `result`, `messaging.system` | `messaging_processing_duration_milliseconds_{bucket,count,sum}` | No; generic recorder present, client wiring deferred |
| `messaging.delivery.lag` | `ms` | `operation`, `messaging.system` | `messaging_delivery_lag_milliseconds_{bucket,count,sum}` | No; generic recorder present, client wiring deferred |
| `messaging.redelivery.count` | `{redelivery}` | `messaging.system`, `result` | `messaging_redelivery_count_total` | No; generic recorder present, client wiring deferred |
| ASP.NET Core built-in meters | version-specific | allowlisted route/service dimensions | deployment sample required | No |
| .NET runtime meters | version-specific | process/runtime dimensions only | deployment sample required | No |
| Collector self-metrics | version-specific | collector/pipeline/exporter | `otelcol_*` sample required | No |

Do not add `trace_id`, `span_id`, transaction/account/customer/message identifiers, raw URL,
SQL values or exception messages to any metric dimension. If the deployed exporter chooses a
different histogram suffix or resource conversion, update the rules and this table in the same
reviewed change before alert activation.

For asynchronous journeys measure `accepted → queued → consumed → processed → persisted →
completed/rejected`, not only broker acknowledgement. A duplicate or replay is correlated with
the original trace using a span link and classified as an idempotent outcome.

Business rejection is a handled outcome rather than a technical availability failure by default;
validation failure, authentication/authorization denial and expected client cancellation are
excluded from the technical-availability denominator. Journey owners may choose stricter
acceptance/completion selectors. Low-traffic operations use synthetic heartbeat, minimum-event
gating and absent-success detection; an absent-data alert is not proof of an outage until the
heartbeat and dependency checks agree.

The complete rule file is [`rules.yaml`](rules.yaml); it contains recording rules, 1h/5m and
6h/30m multi-window burn alerts, a latency rule, telemetry-quality templates and low-traffic
handling. The low-traffic rule uses `increase()` rather than `absent_over_time()` so unchanged
counter scrapes cannot masquerade as a recent success. Technical bad-event selectors include `technical_failure`, `timeout`,
`dependency_unavailable` and `unknown`; `rules.test.yaml` proves the unknown selector while
excluding validation outcomes and checks the no-increase low-traffic alert. Every rule annotation
contains runbook/dashboard links; maintenance is handled by an approved Alertmanager silence,
never by mutating the rule.

The burn threshold is `burn_rate × (1-SLO)`. For 99.99%, a 14.4 burn rate is `0.00144` and a
6 burn rate is `0.0006`; both short and long windows plus a minimum valid-event count are
required before paging. Metric names and latency bucket boundaries must be checked against a
deployed sample before activation (`NOT EXECUTED` in this repository).
