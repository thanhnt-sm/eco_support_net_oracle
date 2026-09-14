---
title: "Observability Phase 6B owner-input contract"
date: 2026-09-14
tags: [observability, phase-6, red-team, security, validation]
---

# Observability Phase 6B owner-input contract

## Context

Phase 6A proved the repository artifacts but discovery still has no approved cluster, backend,
client, traffic or profiler contract. Runtime commands must therefore remain owner-gated and
must not accept credentials, endpoint values or payloads as evidence.

## What happened

Added a versioned redacted owner packet schema, a template and a standard-library-only validator.
The Phase 6A preflight now validates the template as its 23rd check. `--template` reports that
the shape is valid but cannot authorize runtime; `--file` requires explicit approval and a server
dry-run approval, validates all five client contracts and four backend contracts, and rejects
placeholders, credential markers and unsupported payload fields.

## Decisions

- Keep endpoint, tenant, workload-identity, approval and kill-switch values as `ref://` or
  `ticket://` references; never put their resolved values in the packet.
- Require exact package versions when a client is used and explicit `not_used` declarations when
  it is not; do not infer client topology from the generic adapter.
- Treat the checked-in example as template-only (`approved=false`); no local fixture can satisfy
  the owner approval or replace server-side Kubernetes admission.

## Verification

- `python3 scripts/verify_observability_phase6_owner_inputs.py --template ...`: exit 0;
  `TEMPLATE_STATUS=PASS`, `RUNTIME_AUTHORIZATION=OWNER_REQUIRED`.
- A transformed in-memory fixture with enabled backends and a native profiler passed approved mode;
  a credential-marker fixture was rejected with exit 2.
- `./scripts/verify_observability_phase6_preflight.sh`: exit 0;
  `PHASE6A_STATUS=PASS checks=23`, `PHASE6B_STATUS=OWNER_GATED`, `MUTATIONS_PERFORMED=0`.
- No Kubernetes, backend, Secret or profiler API was contacted; the local kubeconfig still has no
  current context.

## Next

Obtain a real owner packet through the approved evidence channel, run the validator against that
redacted file, then execute server-side dry-run and the staged non-critical canary only in the
recorded owner context. Keep all backend, chaos, privacy, profiling and rollback rows `NOT
EXECUTED` until their evidence exists.
