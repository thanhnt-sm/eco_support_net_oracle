---
phase: 6
title: "Phase 6A preflight review and re-plan"
status: in-progress
date: 2026-09-14
verdict: CAUTION
---

# Phase 6A preflight review and re-plan

> Historical report for the optional Collector/Kubernetes artifact gate. The active product
> review is recorded in `phase-06-local-file-review.md` after the owner clarified the CLI/library
> and non-Docker scope.

## Summary

Phase 6 was re-planned from a single owner-gated deployment step into a non-mutating local
preflight (6A) followed by the real integration/canary (6B). The new gate ran against the current
source and exact pinned artifacts. It passed 23 checks and intentionally performed no Kubernetes
apply, backend request, Secret read or profiler start.

## Evidence

| Check | Command | Actual | Result |
|---|---|---|---|
| Phase 6A gate | `./scripts/verify_observability_phase6_preflight.sh` | exit 0; `PHASE6A_STATUS=PASS checks=23` | PASS |
| Owner-input template | `python3 scripts/verify_observability_phase6_owner_inputs.py --template docs/observability/phase6-owner-inputs.example.json` | `TEMPLATE_STATUS=PASS`; `RUNTIME_AUTHORIZATION=OWNER_REQUIRED` | PASS |
| Local Collector process smoke | `./scripts/verify_observability_phase6_local_smoke.sh` (three consecutive runs) | exact image health ready; OTLP/HTTP 200; debug exporter saw synthetic span; ephemeral local container | PASS (local path only) |
| Mutating action guard | same command | `MUTATIONS_PERFORMED=0`; script performs no `kubectl apply` invocation | PASS |
| Collector image | exact Contrib digest `sha256:799dc6cf12c96192af37b5bdba804da8c10b3bc563b43cb90c3f3c58d9572ad6` | required component inventory and both configs validate | PASS |
| Prometheus rules | exact Prometheus digest `sha256:3c42b892cf723fa54d2f262c37a0e1f80aa8c8ddb1da7b9b0df9455a35a7f893` | syntax and deterministic unit tests pass | PASS |
| .NET repository | locked restore, Release build, full tests | exit 0; 726 passed, 0 failed, 0 skipped | PASS |
| Local wrapper benchmark | `dotnet run --project benchmarks/DataGuard.Benchmarks/DataGuard.Benchmarks.csproj --configuration Release -- --filter '*ObservabilityOverheadBenchmarks*' --inProcess` | four cases exit 0; direct 9.139/8.688 ns, observed 292.372/293.600 ns, 480 B | PASS (microbenchmark only) |
| Local Kubernetes context | `kubectl config current-context` | `error: current-context is not set`; no API server was contacted | NOT EXECUTED |
| Runtime canary | owner cluster/backend/profiler | no approved runtime contract in discovery | NOT EXECUTED |

## ck:predict persona review

| Topic | Architect | Security | Performance | UX/Operations | Devil's Advocate | Resolution |
|---|---|---|---|---|---|---|
| Split 6A/6B | Removes a false all-or-nothing gate while preserving runtime boundary | Prevents accidental deployment with unknown secrets | Local checks are cheap; runtime budget still measured later | Gives an actionable command and clear next state | A script can create false confidence | Label 6A as artifact evidence only; keep 6B owner-gated |
| Immutable artifacts | Exact digest makes config reproducible | Blocks tag substitution and inline credentials | Avoids unplanned image drift | Easier rollback to the same image | Digest can still contain a vulnerable component | Re-run vulnerability/component policy on every owner rollout |
| Collector inventory | Confirms config components exist in selected distribution | Does not prove redaction correctness | Detects alias/name drift early | Failure output is localized | Inventory output names differ from config aliases | Map canonical inventory names and let `validate` prove aliases |
| Full solution gate | Protects existing DataGuard surfaces | No telemetry code path is replaced | Catches integration regressions | One command produces repeatable evidence | Passing tests do not prove backend behavior | Keep backend/chaos/privacy/profiler checks explicitly unexecuted |
| Owner apply commands | Correct API-server admission sequence | Requires approved context and TLS contract | Canary limits blast radius | Rollout/rollback is observable | A copy-pasted command could target the wrong cluster | Require recorded context, owner approval and non-critical namespace |

## ck:code-review spec compliance

| Requirement | Status | Evidence |
|---|---|---|
| Produce a useful next gate with current source | PASS | executable `scripts/verify_observability_phase6_preflight.sh` plus owner-input validator |
| Keep backend/cluster operations owner-gated | PASS | script has no apply/backend/Secret/profiler action; 6B commands are separate |
| Pin exact versions and reject drift | PASS | exact OTel project pins, Collector and Prometheus digests checked |
| Validate config with exact distributions | PASS | Collector `validate`, component inventory and Prometheus `promtool` run by digest |
| Preserve build/test/security evidence | PASS | locked restore, Release build, 726 tests and NuGet advisory report |
| Record unexecuted runtime checks honestly | PASS | report/validation retain `NOT EXECUTED` for API/backend/canary gates |

## Adversarial review and adjudication

| Finding | Severity | Verdict | Rationale/action |
|---|---|---|---|
| The script could pass local render while the API server rejects a manifest | High | Accept | Keep server-side `--dry-run=server --validate=strict` as a mandatory 6B gate |
| Component inventory names can differ from configuration aliases | Medium | Accept/fixed | First run caught it; canonical `k8s_attributes`/`otlp_http` mapping added and `validate` retained |
| Docker image pull is an external side effect | Medium | Accept/contained | Only exact digest is allowed; Docker cache/network is a local tool prerequisite, no backend mutation |
| `dotnet list --format json` schema may change between SDKs | Low | Defer | Current SDK 9.0.310 gate passes; record SDK in owner evidence and update parser on SDK upgrade |
| A passing preflight might be mistaken for production readiness | High | Accept/contained | Status remains `in-progress`, output says `OWNER_GATED`, and every runtime row remains `NOT EXECUTED` |
| Owner packet could smuggle endpoint credentials or payload fields | High | Accept/fixed | Redacted schema and fail-closed validator accept only references; the template cannot authorize runtime |
| Local process smoke could be mistaken for an LGTM canary | High | Accept/contained | Keep it separate from 6A mutation-free checks; output labels LGTM/Kubernetes/profiler as not configured/not contacted |

## Findings and actions

1. **Fixed before PASS:** the first script run looked for `k8sattributes` and `otlphttp` in the
   component inventory. Contrib 0.160.0 reports canonical names `k8s_attributes` and `otlp_http`.
   The script now checks canonical names while Collector `validate` checks the configuration
   aliases. This is a gate correctness fix, not a runtime behavior change.
2. **Accepted limitation:** the local gate cannot prove Kubernetes server-side schema, CNI
   egress, mTLS trust, tenant routing, residency/retention, backend protocol, queue recovery,
   client-native span duplication or profiler symbol/source behavior.
3. **Security boundary:** no endpoint credentials, Secret contents, PII, tokens or profile data
   were added. Placeholder URLs are used only for config expansion during validation.
4. **Status correction:** Phase 6 is now `in-progress`; 6A is complete, while every 6B exit
   criterion remains unchecked. No production-readiness claim is permitted.
5. **Owner-input gate:** the Phase 6B packet now has a versioned schema, a redacted template,
   operator guide and a standard-library-only validator. The template is validated offline, while
   a real packet must explicitly set `approved=true` and `server_dry_run_approved=true`; no API or
   backend is contacted by the validator.
6. **Local signal-path smoke:** the exact Collector digest starts in an ephemeral container and
   accepts one synthetic OTLP trace. This is useful process/transport evidence, but it does not
   change any of the owner-gated backend, Kubernetes, privacy, chaos or profiling rows.

## Verdict

**CAUTION — Phase 6A PASS; Phase 6B owner-gated.** The preflight is useful and reproducible, but
it is not a substitute for an approved cluster/backend canary. The next safe action is to collect
the owner input checklist, then run the server-side dry-run and the staged non-critical canary.

## Re-plan

- Keep `scripts/verify_observability_phase6_preflight.sh` as the first command in every owner
  environment and release candidate.
- Do not enable `UseAlwaysOnHeadSamplingForTailSampling` or continuous profiling solely because
  6A passed; both require capacity and overhead evidence.
- Create a new dated report after each 6B gate, including command, context, redacted target,
  observed SLI, telemetry loss, rollback decision and residual risk.
- If any runtime gate fails, disable the smallest affected signal, preserve evidence and return to
  the owner-input or artifact gate rather than broadening rollout.

## Unresolved questions

- Which Kubernetes context/API/CNI and namespace labels are approved for the canary?
- Which concrete gRPC/Kafka/RabbitMQ/Npgsql/Redis client versions and retry/DLQ semantics are in
  the target services?
- What are the real Loki/Tempo/Mimir/Pyroscope protocols, tenant, residency, retention and TLS
  contracts?
- What traffic, outage tolerance, SLO owner, profiler choice and overhead budget govern rollout?

## Product-scope disposition (2026-09-14)

The Collector process smoke and 23-check preflight remain valid as optional reference evidence,
but they are not required to run DataGuard. The active Phase 6 implementation uses a local
`FileObservabilitySink`, daily NDJSON archives and opt-in endpoint switches. No Docker, Collector,
Kubernetes, LGTM or profiler process was started for that product path.
