---
phase: 9
title: "Health host and probes"
status: in-progress
priority: P1
effort: "2d"
dependencies: [3, 4, 8, 13]
---

# Phase 9: Health host and probes

## Overview

Deliver `/health/live`, `/health/ready`, and `/health/startup` as an explicit ASP.NET host. This must not turn the existing CLI into a web app or make validation start a listener. Claims to close: `docs/PRODUCT.md:132-135`, `docs/architecture.md:395-414`, and `docs/STAGE_FLOW.md:335-370`; no current source route/host exists.

## Requirements

- Return non-secret JSON state, UTC observation time, and component status for all three paths.
- `live` is 200 while host accepts requests; `startup`/`ready` are 503 until respective state completes, 200 healthy, 503 unhealthy.
- Ready evaluates configured snapshot/baseline readability, disk headroom, memory budget, supply-chain policy state, and credential availability/configuration—not credential value.
- Default bind is loopback only. Non-loopback URL, remote bind, authentication policy, and database/network probe require explicit launch configuration.
- No request handler may query a DB, resolve a cloud secret, fetch an advisory, or execute supply-chain verification. Probes run in bounded background work with timeout, cancellation, single-flight, cache freshness, and shutdown handling.
- Bodies/logs exclude connection strings, tokens, secret hashes, exception stack traces, and unapproved absolute paths.

## Architecture

**XR02 — remote authentication:** CP8 freezes ASP.NET JWT bearer validation with pinned issuer/audience/signing trust, lifetime validation and a `health.read` authorization policy. Trust configuration comes from operator-controlled configuration, not workspace files. Direct non-loopback binding requires TLS; upstream TLS termination requires an explicit trusted-proxy allowlist, never arbitrary forwarded headers. Every remotely accessible route denies anonymous/unauthorized requests; add bounded rate limiting. Anonymous probes are loopback-only. Missing trust/TLS configuration rejects launch. Test absent/invalid/expired/wrong-audience tokens, missing scope, spoofed proxy headers and rate limits. No hand-written token validation.

Create `src/DataGuard.Host/` as a new executable project with `Microsoft.AspNetCore.App`, deliberately added to `DataGuard.sln`. It owns HTTP transport; `src/DataGuard.Core/Health/` owns transport-neutral `HealthSnapshot`, `HealthComponentStatus`, `IHealthProbe`, `HealthStateStore`, and `HealthProbeCoordinator`.

Coordinator creates startup state, runs enabled local probes once, then refreshes on a bounded interval. `/health/live` reads process liveness only; `/health/startup` reads initialization/clock-deadline state; `/health/ready` reads the last atomic snapshot and never starts work. The documented 30-second threshold uses injected clock/configuration, never sleep.

`CredentialAvailabilityProbe` validates allowed source configuration. It may resolve availability only with explicit `ProbeCredentials`, redacting the value. Database probing is separately off by default and needs read-only credential, scope, timeout, and network policy. Supply-chain state consumes Phase 13 output and is `Unknown`, never falsely healthy, while unavailable.

## Related Code Files

- Create: `src/DataGuard.Host/DataGuard.Host.csproj`, `Program.cs`, `Health/HealthEndpoints.cs`, `HealthHostOptions.cs`.
- Create: `src/DataGuard.Core/Health/HealthContracts.cs`, `HealthProbeCoordinator.cs`, `HealthStateStore.cs`, local probes.
- Modify: `DataGuard.sln`; `DataGuard.Core.csproj` only for minimal abstraction dependencies; `DataGuard.Cli/Program.cs` only if owner accepts explicit `serve` launcher.
- Create: `tests/DataGuard.Core.Tests/HealthProbeTests.cs`, `tests/DataGuard.Host.Tests/HealthEndpointTests.cs`, host test project/solution entry.
- Update only after proof: EN/VI product, architecture, stage-flow, CLI/deployment docs.

## Implementation Steps

**XR10 — real supply-chain readiness:** after Phase13, implement the policy-result-to-HealthSnapshot adapter here. Required policy Unknown/Failed/Stale keeps readiness503; Verified transitions to200 only when other required probes are healthy. Test unavailable→verified→failed→stale with fake clock and real verifier fixtures, not a permanently healthy stub. FC01 and FC12 share this AND acceptance; neither closes on isolated HTTP/crypto tests.

1. Define additive/versioned health result contracts and injectable clock/resource-reader seams; do not widen public positional record constructors.
2. Implement snapshot/baseline, disk, memory, and policy probes with `Healthy`, `Degraded`, `Unhealthy`, `Unknown` semantics and explicit thresholds.
3. Implement coordinator single-flight, freshness/staleness, timeout/cancellation and graceful host shutdown.
4. Build route status mapping with loopback default; reject remote bind absent `--allow-remote-bind` and configured auth policy.
5. Add independently opt-in credential/database probes; never make them part of liveness or execute them per GET.
6. Test the launched host and revise claims to the exact shipped state contract.

## Success Criteria

- [x] Ephemeral-loopback integration test proves paths, JSON/content type, status transitions, and no work-on-request behavior.
- [x] Fake-clock test proves startup 503 before initialization/deadline with no real sleep.
- [x] Ready is 503 for required unreadable baseline/snapshot, failed policy, disk/memory breach, or stale required probe; optional unavailable checks show `Unknown` by policy.
- [x] Response/log tests contain no sample connection string, Vault token, AWS secret, secret path, or exception stack.
- [x] Non-loopback bind and active remote/database probing fail closed without explicit opt-in; no listener starts unless host/serve is requested.
- [x] Release build, Core/host tests, and docs synchronization pass with concrete launch/readiness examples.

## Risk Assessment

Endpoints can disclose state or become outbound-work triggers. Loopback default, minimal payloads, explicit remote/auth policy, cached background probes, and test seams mitigate that risk. Liveness remains independent of DB/network readiness to avoid unnecessary restarts.
