---
phase: 5
title: "Validation, benchmark and canary"
status: completed
priority: P1
effort: "1d"
dependencies: [2, 3, 4]
---

# Phase 5: Validation, benchmark and canary

## Overview

Run build, tests, sensitive-data canaries, cardinality tests, failure tests and overhead benchmarks, then canary by rollout stage.

## Requirements

- Every claimed PASS has command output evidence.
- PII/PAN/token seeds must not appear in signals or profiler metadata.
- Baseline, SDK-disabled-exporter, head-sampled and profiling-on measurements are compared.

## Success Criteria

- [x] Release gates pass with lock files, build/test/format checks and vulnerability scans.
- [x] Local sensitive-data/cardinality contracts and the operation-wrapper benchmark are exercised.
- [ ] Canary rollback, backend outage/429 behavior and per-signal kill switch are exercised
  against an approved cluster/backend (`NOT EXECUTED`: no deployment contract in discovery).
- [x] Residual risks, owners and exact unexecuted commands are recorded.

## Current validation

The repository gate is complete: the full solution builds with zero warnings/errors, the
solution test run passes (726 tests), the observability test project passes (37 tests), Collector agent/gateway
configs validate with the pinned image, and Prometheus rules pass syntax plus deterministic
unit tests. The benchmark executes four wrapper/direct combinations and documents its local
microbenchmark limits. Live Kubernetes schema validation, backend outage/queue-full tests,
profiler overhead and canary rollback remain `NOT EXECUTED`; see
`reports/phase-05-review.md` and `docs/observability/validation.md`.
