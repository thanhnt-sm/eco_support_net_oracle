# Phase 1 review — red team and contract hardening

Review date: 2026-09-13. Scope: discovery-driven red-team adjudication and the vendor-neutral
operation contract.

## Verdict: CAUTION / contract accepted, deployment claims rejected

The requirements were adjudicated before implementation. The accepted slice is additive and
compatibility-first: it preserves DataGuard's `net9.0` and existing custom metrics/NDJSON/audit
paths, keeps vendor types out of domain code, rejects payload/credential capture by default and
uses bounded asynchronous export. The complete decision table is in
[`docs/observability/red-team.md`](../../../docs/observability/red-team.md).

| Gate | Result | Evidence |
|---|---|---|
| SLA/SLO distinction | PASS | Observability is measurement/diagnosis; no 99.99% availability claim |
| Fail-open transport | PASS | Bounded queue/retry/timeout design and unavailable-backend test |
| Secure defaults | PASS | Allowlist policy, no body/parameter capture and privacy canary |
| Stable operation names | PASS | Descriptor validation and finite configured allowlist |
| Composition over inheritance | PASS | Observer, middleware/filter/endpoint-filter seams; no universal base class |
| Profiling separation | PASS | Independent profiler/Pyroscope plane and kill switch |
| Audit separation | PASS | Existing audit path remains separate from diagnostic telemetry |
| Production certification | REJECTED | Topology, backend, traffic, residency, retention, SLO owners and runtime gates unknown |

## Re-plan after review

Proceed to phase 2 with a generic SDK/ASP.NET Core/W3C messaging slice only. Defer concrete
gRPC/Kafka/RabbitMQ/PostgreSQL/Redis bindings until discovery supplies package versions and
retry/streaming contracts. Add privacy/cardinality tests before any collector deployment.
