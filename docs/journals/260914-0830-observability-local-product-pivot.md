---
date: 2026-09-14
title: "Phase 6 local file observability product pivot"
status: in-progress
---

# Phase 6 local file observability product pivot

## Context

Discovery plus owner clarification: DataGuard is a CLI/library, not a backend/frontend and not a
Docker workload. The earlier Collector/Kubernetes plan was useful as a reference but was not the
right product runtime gate.

## What changed

- Added `FileObservabilitySink` and a bounded local record queue to `TelemetryCollector`.
- Event/counter/histogram/validation summary calls now serialize an allowlisted observability
  envelope as UTF-8 NDJSON under a UTC-day archive path.
- Event bodies are omitted by default; optional bodies are redacted and capped. Dynamic IDs,
  payloads, credentials and arbitrary attributes are excluded.
- Added `TelemetryConfig` and `DataGuardConfiguration` settings for archive path and service metadata.
- Added `RemoteExportEnabled=false` and host `EnableHost=false`/`ExposeEndpoints=false` defaults;
  existing compatibility tests opt in explicitly.
- Kept `FileAuditLogger` separate; local diagnostic files are not audit records.

## Final verification

- Locked restore and Release solution build: exit 0, 0 warnings/errors.
- Full solution tests: 739 passed, 0 failed, 0 skipped.
- Local sink tests: 7 passed; product-pipeline archive integration: 1 passed; telemetry
  compatibility filter: 27 passed; observability package tests: 38 passed; host-lockdown filter:
  15 passed.
- `dotnet format --verify-no-changes --no-restore` on eight affected projects: exit 0 (only
  non-fatal workspace-load notices).
- YAML/JSON/template validation, Kustomize local render, docs synchronization, whitespace/diff
  checks and default `DataGuard.Host` smoke: exit 0.
- BenchmarkDotNet wrapper: 4 cases, exit 0; this is a local microbenchmark, not a production
  overhead budget.

## Remaining

Remote Collector/LGTM/Kubernetes/profiling checks remain `NOT EXECUTED` for the product path.
Archive retention/deletion still needs an owner policy; no automatic deletion was introduced.
No commit or push was performed.
