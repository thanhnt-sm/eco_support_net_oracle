---
phase: 6
title: "Local file observability review"
status: in-progress
date: 2026-09-14
verdict: CAUTION
---

# Phase 6 local file observability review

## Scope decision

Discovery plus owner clarification classify DataGuard as a CLI/library, not a backend/frontend and
not a Docker workload. The active acceptance gate is therefore direct .NET execution with a local
text archive. The earlier Collector/Kubernetes report remains historical and optional.

## Changes reviewed

| Area | Decision | Evidence |
|---|---|---|
| Automatic processing | Existing `TelemetryCollector` event/counter/histogram/validation calls feed a bounded local queue | `src/DataGuard.Core/Telemetry/TelemetryCollector.cs` |
| Standard envelope | `ObservabilityRecord` carries signal, resource, operation, status, value, W3C IDs when present | `src/DataGuard.Core/Telemetry/ObservabilityFileSink.cs` |
| Daily archive | UTC path `yyyy/MM/dd/observability-yyyy-MM-dd.ndjson`, UTF-8 NDJSON | `FileObservabilitySinkTests` |
| Privacy | Body omitted by default; optional body redacts PAN/bearer/email and caps length; attributes fail-closed | `ObservabilityFileSinkTests` |
| Fail-open | Queue, payload and record bounds; drop/terminal counters; archive errors do not escape telemetry calls | `TelemetryCollector` tests |
| Endpoint lockdown | Remote exporter switch and Host process/routes default false; integration test opts in explicitly | `CoreObservabilityOptions`, `HealthHostOptions`, tests |
| Audit separation | Local diagnostic files do not replace `FileAuditLogger` hash-chain | ADR-010/014 and local sink guide |

## Red-team findings

| Finding | Severity | Disposition |
|---|---|---|
| A disk/permission failure can drop local telemetry | High | Accepted with bounded queue, measured drops and owner-managed retention |
| Partial append followed by retry can duplicate a line | Medium | Accepted at-least-once semantics; stable `record_id` allows downstream deduplication |
| Dynamic metric names/tags could create cardinality | High | Accepted only through bounded event names and fixed attribute allowlist; tests cover IDs |
| Body redaction cannot prove every future PII format | High | Body remains off by default; add canary patterns before enabling details |
| Direct text is not immutable audit evidence | High | Rejected as audit use; retain separate hash-chain logger |
| Remote endpoint could be enabled by configuration drift | High | Two explicit switches/owner review; no implicit `HttpClient` path |

The final red-team pass also made record creation fail-open for faulty metric/tag values, bounded
resource/operation tokens, redacted authorization schemes (`Bearer`/`Basic`/`Digest`), and counted
in-flight flush failures as terminal loss during shutdown instead of requeueing after `Stopping`.

## Verification snapshot

The final verification table is maintained in `docs/observability/validation.md`. The final gate
on 2026-09-14 passed locked restore, Release solution build (0 warnings/errors), 739 solution
tests, 7 local-sink tests plus one product-pipeline integration test, 38 OTel package tests,
15 host-lockdown tests, format checks for eight
affected projects, YAML/JSON/template validation, Kustomize local rendering, docs synchronization
and the default host smoke. No endpoint, Collector, backend or profiler was started for the
product runtime.

## Verdict

**CAUTION.** The product-native local path is implementable and testable, but no claim is made for
remote backend availability, Kubernetes admission, profiling overhead, retention compliance or
99.99% service SLA. Remote Collector/LGTM/profiler rollout requires a new owner-approved phase.

## Re-plan

1. Complete the full build/test/docs/static verification.
2. Keep archive retention/deletion outside the sink until an owner policy is approved.
3. If a remote host is later requested, require exact protocol/TLS/tenant/residency/traffic and
   rollback evidence before changing the disabled defaults.
