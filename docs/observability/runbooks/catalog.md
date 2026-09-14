# Enterprise observability runbook catalog

## Common incident workflow (required for every scenario)

1. Read the alert meaning and confirm the affected user journey/SLO.
2. Record start time, alert labels, deployment version and current change window.
3. Check the executive SLO and business-journey dashboard for user impact and traffic sufficiency.
4. Check service RED (rate, error ratio, p50/p95/p99 duration) by stable operation/route name.
5. Select a representative trace; preserve its TraceId and verify parent/child continuity.
6. Open correlated logs by `trace_id`/`span_id`; do not copy payloads, tokens or PII into tickets.
7. Inspect dependency spans, retries, timeouts, pool saturation and circuit state.
8. Inspect the profile for the same service/version/time range; claim source-line fidelity only when symbols are present.
9. Apply the smallest safe mitigation (rate limit, traffic shift, feature flag or rollback).
10. Verify recovery through the SLI and a synthetic transaction, not merely telemetry arrival.
11. Preserve dashboards, traces, redacted logs, deployment manifests and command output as evidence.
12. Escalate to the named owner when the safety check or mitigation is outside the responder's authority.
13. Record false-positive conditions (maintenance, low traffic, synthetic failure, sampling gap).
14. Create post-incident actions for root cause, SLO/error-budget impact, privacy review and test coverage.

## Scenario matrix

The common workflow above is mandatory for each row. “Rollback” means the last known-good
application/collector configuration, never a destructive data reset.

| Alert/scenario | Meaning and user impact | Immediate safety checks | Investigation focus | Mitigation | Rollback | Escalate to | False-positive / evidence |
|---|---|---|---|---|---|---|---|
| Fast-burn availability | Critical operation is consuming budget quickly; user-visible failures likely | Confirm event count, maintenance silence, recent release | Representative error traces and dependency critical path | Stop rollout, enable safe fallback, shed non-critical load | Revert release/config | Service + SRE owner | Low traffic or planned change; preserve SLO panels |
| Slow-burn | Sustained degradation risks budget exhaustion | Check 6h/30m windows and minimum events | Trend by operation/version/region | Schedule remediation, reduce load, tune dependency | Roll back offending change | Service owner | Batch window or planned maintenance |
| Latency SLO | Tail latency exceeds journey threshold | Confirm histogram bucket/sample freshness | Trace critical path, queue/pool/GC | Reduce concurrency, disable expensive feature, scale | Revert feature/deployment | Performance + service owner | Traffic mix change; attach latency histogram |
| Business transaction failure | Transfer/approval/completion technical errors | Distinguish rejection/authz/cancel from technical failure | Result classification, idempotency, persistence spans | Pause affected command path, replay only safe messages | Restore previous transaction handler | Payments owner + incident commander | Expected business rejection; preserve audit record ID separately |
| Kafka consumer lag | Accepted work is waiting; completion SLO at risk | Check partition health and consumer group ownership | Broker latency, rebalance, poison message, downstream DB | Scale consumers within partition limit, quarantine poison message | Restore prior consumer image/config | Messaging owner | Traffic burst or planned pause; capture offsets not payload |
| RabbitMQ redelivery storm | Messages repeatedly fail and may amplify load | Check DLQ policy and retry count | Consumer exception, ack/nack path, broker health | Stop redelivery, route poison messages to DLQ, fix idempotency | Restore consumer and retry policy | Messaging owner | Duplicate/replay expected; preserve message ID hash only |
| PostgreSQL saturation | DB latency/pool exhaustion threatens journeys | Check connection pool, locks, CPU/IO and replica health | Slow query spans without parameters, wait events | Limit concurrency, fail over/read replica, shed reads | Restore pool/query config | DBA + service owner | Backup/maintenance window |
| Redis timeout | Cache/dependency timeout; assess fallback path | Check cluster health, TLS and connection pool | Cache hit ratio, timeout traces, fallback correctness | Disable cache-aside stampede, use bounded fallback | Restore cache feature flag | Platform + service owner | Planned failover; never log values |
| Outbound dependency failure | Downstream HTTP/gRPC unavailable/slow | Confirm status taxonomy and retry budget | Dependency spans, DNS/TLS, circuit breaker | Open circuit, reduce retries, route to alternate | Restore prior endpoint/config | Dependency owner | Client 4xx/business rejection |
| ThreadPool starvation | Queues/latency rise from blocked worker threads | Compare active requests, queue and CPU | Sync-over-async, blocking I/O, trace duration | Scale temporarily, disable blocking feature, hotfix async path | Roll back release | .NET performance owner | Short burst without SLO impact |
| GC pressure | Allocation/heap/pause pressure may affect latency | Correlate with SLO and working set | Allocation hotspots and profile/GC counters | Reduce allocation, scale memory, restart only with approval | Restore prior build/config | Runtime owner | Normal full GC without user impact |
| Collector queue saturation | Telemetry may be dropped; business path must stay healthy | Check queue, refused/export failure and pod resources | Backend status, retry storm, network/TLS | Increase bounded capacity, route to healthy gateway, lower sampling | Restore prior collector config | Observability platform owner | Planned backend maintenance; retain drop counters |
| Telemetry drop/missing | Diagnosis blind spot; not a business outage by itself | Verify app export, agent/gateway health and sampling | Collector self-metrics, DNS/cert, endpoint auth | Fail over collector, fix cert/endpoint, use synthetic trace | Revert endpoint/config | Platform owner | Sampling or no-traffic window |
| Cardinality explosion | Cost/memory risk and query degradation | Check new span names/labels/tenants | Attribute cardinality, deploy diff, top-N | Disable offending signal, drop dynamic labels, cap series | Revert instrumentation/config | Platform + service owner | Expected release surge; attach cardinality report |
| Log storm | Storage/ingestion cost and signal loss | Check rate-limited logger and repeated event IDs | Exception loop, retry/logging at every layer | Raise level, rate-limit, fix duplicate logging | Restore previous log policy | Service owner + platform | Incident debug burst; retain sample only |
| Profiling overhead regression | CPU/allocation overhead may harm SLO | Compare canary vs baseline and profiler flag | Flame graph, profiler/runtime version, symbols | Disable profiler kill switch, reduce rate, scale | Roll back profiler/runtime config | Performance + platform owner | Workload mix or JIT change |
| Possible PII leakage | Privacy/compliance exposure | Stop export/access, preserve hashes not raw data | Redaction counters, canary searches, access audit | Disable signal, rotate credentials, purge per policy | Restore only after privacy sign-off | Security/privacy owner | Canary false match; preserve controlled evidence |
| Expired certificate | Export or backend access fails; queue may fill | Check expiry chain and rotation status | Collector TLS logs and endpoint reachability | Rotate secret, validate chain, drain queue safely | Revert only to still-valid cert | PKI/platform owner | Clock skew; record certificate fingerprint |
| Backend 429/5xx | Telemetry backend throttles/unavailable | Check retry-after, queue and drop rates | Tenant quota, endpoint health, network | Reduce volume/sampling, request quota, fail over | Restore previous routing | Backend owner | Isolated tenant or planned maintenance |

## Safety rules

- Never paste request/response bodies, authorization headers, message payloads, SQL parameters,
  account/customer identifiers or raw exception messages into incident records.
- Never use diagnostic Loki logs as an immutable audit ledger. Security events and compliance
  records go to their approved tamper-evident systems.
- Do not declare the incident resolved from Collector health alone; verify the business SLI and
  a synthetic journey.
