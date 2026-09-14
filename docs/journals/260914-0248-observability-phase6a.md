---
title: "Observability Phase 6A preflight"
date: 2026-09-14
tags: [observability, phase-6, red-team, validation]
---

# Observability Phase 6A preflight

## Context

The discovery bundle has no approved Kubernetes context, LGTM backend, concrete messaging client
contract, traffic budget or profiler choice. A monolithic Phase 6 would either stop without new
evidence or encourage unsafe placeholder deployment.

## What happened

Phase 6 was split into a non-mutating local preflight (6A) and an owner-gated runtime canary (6B).
The new `scripts/verify_observability_phase6_preflight.sh` checks the exact project/package pins,
immutable Collector and Prometheus images, config-copy equality, Kustomize/YAML/JSON validity,
Collector component/config validation, Prometheus rule tests, locked restore/build/tests and NuGet
advisories. The latest run passed 22 checks and reported zero mutations. A first run caught the
Collector inventory's canonical underscore names; the gate was corrected and rerun successfully.

## Decisions

- Keep Phase 6 `in-progress`: 6A is proven; 6B is not executable without owner inputs.
- Treat local Docker validation as artifact evidence only; it does not prove backend protocol,
  tenant isolation, residency, queue recovery, Kubernetes admission or profiler behavior.
- Keep server-side `kubectl apply --dry-run=server --validate=strict` and staged rollback as
  mandatory owner gates.
- Do not claim production readiness, 99.99% service availability or zero telemetry loss.

## Verification

- Phase 6A: exit 0, `PHASE6A_STATUS=PASS checks=22`, `MUTATIONS_PERFORMED=0`.
- Solution build: exit 0, 0 warnings/errors.
- Solution tests: exit 0, 726 passed, 0 failed, 0 skipped.
- Latest wrapper benchmark: 9.139/8.688 ns direct, 292.372/293.600 ns observed, 480 B;
  local microbenchmark only.
- `./scripts/verify_docs_sync.sh`, actionlint, YAML/JSON parsing, Kustomize render and whitespace
  gates pass. Local kubeconfig has no current context, so API-server validation is `NOT EXECUTED`.

## Next

Obtain the owner checklist (cluster/API/CNI, client versions, LGTM protocols/tenancy/TLS/residency,
traffic/RTO/RPO/SLO and profiler policy), rerun 6A in that environment, then execute only the
non-critical synthetic canary. Record every gate, failure injection, privacy search, overhead
measurement and rollback decision in a dated Phase 6B report.
