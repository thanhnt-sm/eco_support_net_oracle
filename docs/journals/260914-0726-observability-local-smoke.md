---
title: "Observability local Collector process smoke"
date: 2026-09-14
tags: [observability, phase-6, validation, collector]
---

# Observability local Collector process smoke

## Context

The repository has a pinned Collector agent/gateway artifact but no approved Kubernetes context
or LGTM backend. A config-only validation cannot prove that the selected binary starts and accepts
the protocol, while a production deployment would be unsafe without owner inputs.

## What happened

Added `collector/local-smoke.yaml` and `scripts/verify_observability_phase6_local_smoke.sh`.
The command starts the exact immutable Collector image in a disposable local Docker container,
probes the health extension, posts one non-sensitive OTLP/JSON trace to loopback and waits for the
debug exporter to show the stable synthetic operation.

## Decisions

- Keep the smoke config separate from the agent/gateway deployment contract: it intentionally uses
  plaintext loopback and a debug exporter, and has no LGTM endpoint, tenant or credential.
- Report `EXTERNAL_MUTATIONS=0` and `LOCAL_CONTAINER=EPHEMERAL`; do not call this an LGTM,
  Kubernetes, queue-recovery or production canary.
- Keep Phase 6B runtime rows `NOT EXECUTED` until the owner packet and server-side dry-run exist.

## Verification

- `docker ... validate --config=file:/etc/otel/local-smoke.yaml`: exit 0.
- `./scripts/verify_observability_phase6_local_smoke.sh`: three consecutive runs exit 0;
  `HEALTH=READY`, OTLP/HTTP `200`, `DEBUG_EXPORT=SEEN` on each run.
- The container stopped through the script's exit trap; no LGTM backend, Kubernetes API, Secret or
  profiler was contacted.

## Next

Use the owner-input validator and approved cluster context to move from this local process smoke
to server-side dry-run, backend protocol/TLS/tenant verification and a staged non-critical canary.
