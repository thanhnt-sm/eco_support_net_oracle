# Phase 4 review — SLO, dashboards and runbooks

Date: 2026-09-13
Verdict: CAUTION / rule syntax and deterministic unit test passed; semantic activation waits for deployed metric samples and owner SLO approval.

## Evidence

| Check | Result |
|---|---|
| Rule syntax | Prometheus `v3.13.1` `promtool check rules`: 19 rules, exit 0 |
| Rule behavior | `promtool test rules` deterministic error-ratio, selector and no-increase low-traffic tests: exit 0 |
| Dashboard catalog | YAML parsed successfully; 11 dashboard definitions and explicit cross-signal link requirements |
| Runbooks | 19 incident classes covered by common 14-step evidence workflow and scenario matrix |
| Metric semantic verification | `NOT EXECUTED`: no deployed Collector/Mimir sample or owner-approved label contract |

## Red-team findings

1. The rules use the expected Prometheus normalization of the custom OTel instruments
   (`business_operation_*_total` and `_milliseconds_*`), but this repository has no live
   exporter sample. Rules are syntax-valid, not production-activated.
2. The core observer emits operation/result/error dimensions only; async completion, broker lag,
   DLQ, and dependency pool metrics require signal-specific instrumentation in the consuming
   service. Dashboard entries mark those metrics as integration contracts, not fabricated data.
   Business spans now carry the same allowlisted `banking.operation.name` and `banking.slo.class`
   keys used by the gateway tail-sampling policy.
3. Burn alerts include both short and long windows and minimum event counts. Low-traffic alerts
   use a counter `increase()` (not scrape absence) and explicitly require synthetic heartbeat
   validation; no telemetry is not automatically a business outage.
4. The technical-availability denominator now excludes cancellation, invalid-input and
   authentication/authorization outcomes; handled business rejection remains a valid good
   response in the generic template, while `unknown` is treated as a technical bad event.
   Acceptance/completion journeys must apply their own owner-approved result selector before
   activation.
5. Maintenance is represented as an Alertmanager silence/change record. Editing thresholds or
   suppressing recording rules during an incident is prohibited.
6. Cross-signal links are conditional: exemplars, Tempo derived fields and Pyroscope
   correlation need backend configuration and integration proof. No source-line profiling claim
   is made.

## Decision and next plan

Proceed to phase 5 for local build/test, sensitive-data and cardinality tests, overhead
benchmark, and rollout/rollback evidence. Keep SLO activation gated on owner approval and
sample-based PromQL verification.
