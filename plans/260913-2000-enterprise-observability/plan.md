---
title: "Enterprise banking observability platform"
description: "Hardening plan for a privacy-first, fail-open local observability capability for the DataGuard CLI/library based on discovery evidence."
status: in-progress
priority: P1
effort: 5d
branch: main
tags: [feature, cli, library, observability, critical]
blockedBy: []
blocks: []
created: 2026-09-13
---

# Enterprise banking observability platform

## Overview

Turn the validated starter at `src/DataGuard.Observability` into a reviewable internal package while
adding a product-native, file-based observability path to DataGuard's existing CLI/library telemetry.
Discovery shows no production service topology, LGTM backend, traffic, SLO owner, residency or RTO/RPO;
those unknowns gate any optional remote deployment claim. The default product path is local NDJSON
archive, not Docker, Kubernetes or an HTTP endpoint.

## Phases

| Phase | Name | Status |
|---|---|---|
| 1 | Red Team and Contract Hardening | Completed |
| 2 | Core SDK and adapters | Completed (generic slice; client-specific adapters deferred) |
| 3 | Collector and Kubernetes | Completed (local artifact gate; cluster gate NOT EXECUTED) |
| 4 | SLO, dashboards and runbooks | Completed (syntax/unit gate; live metric gate NOT EXECUTED) |
| 5 | Validation, benchmark and canary | Completed (repository gate; runtime canary NOT EXECUTED) |
| 6 | Local file observability and endpoint lockdown | In progress (local implementation; optional remote gate deferred) |

## Dependencies

- Owner decision on production topology, backend, data classification, retention, traffic and SLOs.
- Approved OpenTelemetry package vulnerability policy and lock files.
- Kubernetes/Collector/Grafana platform owner provides immutable image digests and tenant/TLS contracts.

## Success criteria

- Buildable package and tests with exact versions.
- No default payload/credential/PII capture; finite metric dimensions.
- Fail-open export with bounded memory and explicit drop metrics.
- HTTP/gRPC/messaging propagation adapters tested before rollout.
- Collector/backend configs validated using the exact distribution.
- Benchmark and canary evidence before critical transaction enablement.
- A bounded, fail-open file sink that serializes standard observability records to UTC-day NDJSON
  archives without opening an endpoint.
- A redacted attribute/body policy and tests proving sensitive values and dynamic identifiers do
  not enter local records.
- HTTP route and remote exporter switches disabled by default; compatibility surfaces remain
  explicit and owner-gated.

## Red Team Review

The initial review is recorded in `red-team.md`. Verdict: CAUTION. Proceed only with the bounded starter and owner gates; do not claim 99.99% availability or production readiness.

## Tooling limitation

`ck plan create` was attempted on 2026-09-13 and is unavailable (`ck: command not found`). Phase files were created manually using the skill's canonical schema; install/enable `ck` before relying on CLI status mutation.

## Validation Log

### 2026-09-13 verification pass

- Claims checked: package compile, DI registration, operation allowlist, secure endpoint validation, exception handling, dependency vulnerabilities, formatting and documentation sync.
- Verified: `dotnet restore DataGuard.sln --locked-mode` and solution build 0 errors/0 warnings; observability contract tests 37 passed; full solution tests 726 passed; three internal `0.1.0` packages pack with README; vulnerability scans report no vulnerable packages; format, diff and documentation-sync gates pass.
- Failed: 0.
- Unverified: Collector/Kubernetes/backend/profiler runtime behavior, because discovery contains no approved deployment topology or backend.
- Advisor status: Luna MAX advisor spawn attempted, but environment usage limit prevented execution; no advisor finding is treated as evidence.
- Phase 2 review: generic ASP.NET Core/W3C messaging adapters build and test; concrete gRPC/Kafka/RabbitMQ bindings remain deferred pending client discovery. The recursion guard and MVC filter are covered; the metric/tag consistency pass aligns `banking.slo.class` with gateway tail sampling.
- Phase 2 snapshot verification: core/ASP.NET Core/messaging builds pass; adapter test suite reports 30 passed and the solution snapshot reports 719 passed. Later phase-5 hardening added sampler validation, Minimal API endpoint-filter and classified-result status regression tests; the intermediate gate was 31/720, then 32/721, and the final gate is 33/722.
- Phase 3 verification: exact Collector Contrib 0.160.0 digest validates both agent and gateway configs; local YAML parsing passes; Kubernetes server-side validation and outage tests are `NOT EXECUTED` without a cluster/backend.
- Phase 4 verification: Prometheus 3.13.1 `promtool check rules` and deterministic `test rules` pass for 19 rules; live metric-name/bucket verification is `NOT EXECUTED`.
- Final SLO red-team: the `unknown` technical-failure result was initially absent from the bad-event matcher; selectors and the Prometheus fixture now include it while retaining the validation-outcome exclusion, and exact `promtool check/test rules` pass.
- Configuration red-team: non-finite `TraceSamplingRatio` values (`NaN`/`Infinity`) are rejected explicitly and covered by regression tests; no exporter/backend behavior changes.
- Final transport/cardinality red-team: gateway receiver mTLS now matches the agent exporter and
  mounts a server certificate plus client CA; messaging metrics require an explicit finite
  operation allowlist; duration histograms carry finite result labels for valid-event latency
  selectors; inline endpoint credentials are rejected at startup; core diagnostic logs have
  stable EventIds and bounded exception types; the Tempo exporter default requires only CA trust,
  with client-certificate mTLS isolated to an owner overlay.
- Phase 5 verification: four BenchmarkDotNet cases execute with exit 0; local privacy/cardinality tests pass; profiling overhead, backend chaos and canary rollback are `NOT EXECUTED`.
- Phase 5 hardening: a parallel activity-listener test-isolation race was found by the full-suite gate, fixed by operation-scoped capture, and revalidated with three isolated runs plus the final parallel suite.
- Final sampling red-team: corrected the tail-sampling candidate-set semantics and added the explicit `UseAlwaysOnHeadSamplingForTailSampling`/`TraceSamplingRatio=1.0` contract with regression coverage; the 10% default remains unchanged.
- Final repository gate: `dotnet build DataGuard.sln --configuration Release --no-restore` and `dotnet test DataGuard.sln --configuration Release --no-build --no-restore` both exit 0 (726 tests). Production readiness remains blocked by owner-gated runtime evidence.
- Post-review revalidation: after the final metrics-builder API correction, Minimal API endpoint-filter test, classified-result status hardening, TLS receiver/NetworkPolicy correction, SLO unknown selector fix, gateway mTLS receiver hardening, finite messaging operation allowlist, result-labelled duration histogram, inline endpoint-credential rejection, Tempo Secret/exporter consistency fix, low-traffic counter-increase correction and stable log EventIds/bounded exception type, build (0 warnings/errors), full tests (726/726), five-project format, locked restore, vulnerability scans, exact Collector/Prometheus validators, Kustomize/YAML/JSON/actionlint/docs gates and the four-case benchmark were rerun and passed. The latest 2026-09-14 benchmark is 9.139/8.688 ns direct, 292.372/293.600 ns observed, 480 B; this does not change the phase-6 runtime gate.

### Re-plan after phase 5

Phase 6 was initially adjusted into a safe two-step Collector/Kubernetes gate. That historical
artifact is retained, but the product owner then clarified that DataGuard is a CLI/library and is
not deployed in Docker. The active boundary is now `phase-06-local-file-observability.md`: local
NDJSON archive first, endpoint mapping and remote export opt-in only.

### Phase 6A review and re-plan (2026-09-14)

- `./scripts/verify_observability_phase6_preflight.sh`: exit 0; 23 checks passed, including the
  owner-input template contract. A real packet validator remains owner-gated.
- `./scripts/verify_observability_phase6_local_smoke.sh`: exit 0; exact Collector digest reached
  health, accepted OTLP/HTTP (200) and emitted the synthetic span through the debug exporter in
  an ephemeral local container. LGTM/Kubernetes/profiler gates remain unexecuted.
- The script performed no `kubectl apply`, backend request, Secret read or profiler start.
- A component-inventory naming mismatch (`k8sattributes`/`otlphttp` versus the image's canonical
  `k8s_attributes`/`otlp_http`) was found by the first run and corrected before the passing run.
- Red-team verdict: **CAUTION**. The local contract is reproducible, but no local check can stand
  in for Kubernetes server-side admission, backend tenant/protocol behavior, runtime chaos,
  privacy searches or profiler overhead.
- Next action: obtain the Phase 6B owner inputs, re-run Phase 6A in the owner environment, then
  execute only the staged non-critical canary with evidence and rollback controls.

### Product-scope re-plan (2026-09-14)

- Implemented `FileObservabilitySink` under `src/DataGuard.Core/Telemetry/`; events, counters,
  histograms and validation summaries now feed a bounded, allowlisted local queue.
- Daily files are written under `yyyy/MM/dd/observability-yyyy-MM-dd.ndjson`; event bodies are
  omitted by default and optional bodies are redacted/capped.
- `TelemetryConfig.RemoteExportEnabled` and `CoreObservabilityOptions.RemoteExportEnabled` default
  to false. `HealthHostOptions.ExposeEndpoints` defaults to false; only the host integration test
  opts in.
- Docker/Collector/Kubernetes/LGTM/profiler checks are explicitly `NOT EXECUTED` for this product
  phase. They remain optional reference artifacts, not runtime prerequisites.
- Final product gate after the implementation hardening passed locked restore, Release build with
  0 warnings/errors, 739 solution tests, 7 local-sink tests plus one product-pipeline integration
  test, 38 observability package tests, eight format checks, YAML/JSON/template validation,
  Kustomize local render, docs synchronization and
  the default host smoke. Phase 6 remains `in-progress` because retention ownership and any
  remote canary are intentionally unresolved.
