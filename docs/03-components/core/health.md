# Health coordination

> Source: `src/DataGuard.Core/Health/`

Core health coordination is transport-neutral. `HealthProbeCoordinator` runs a
fixed set of local `IHealthProbe` instances outside request handling, applies a
per-probe timeout, and publishes one atomic `HealthSnapshot` through
`HealthStateStore`. A caller reads the last snapshot only; reading it never starts
database, credential, advisory, or supply-chain work.

Each component is `Healthy`, `Degraded`, `Unhealthy`, or `Unknown`. Readiness
requires completed startup and every required component to be healthy. A timeout
or probe exception becomes a generic non-secret component result; exception text,
connection strings, tokens, hashes, and absolute paths are not copied into the
snapshot.

This Core layer does not expose HTTP routes or bind a listener. The current-source optional `DataGuard.Host` supplies the HTTP boundary described below. It does not add authentication middleware: loopback binding is a local exposure restriction, not user authentication. Remote exposure needs a separately designed authenticated host.

`DataGuard.Host` is disabled unless `DataGuardHealth:EnableHost=true`. When enabled, it accepts
only one or more non-empty loopback HTTP(S) URLs; malformed, remote, or other-scheme URLs are
rejected before binding. Route mapping is a second opt-in via
`DataGuardHealth:ExposeEndpoints=false` (default), so the CLI/library does not expose HTTP
endpoints unless a controlled host wrapper explicitly enables both switches.

The current-source local probes cover configured snapshot and baseline readability, free
disk space, and managed-memory budget. An omitted snapshot or baseline is
`Unknown`; an empty readiness definition is never healthy. These probes do not
open database connections, resolve credentials, or make network requests.

When routes are explicitly enabled, readiness also requires a fresh snapshot. The host defaults to a 30-second
maximum snapshot age (`DataGuardHealth:MaximumSnapshotAgeSeconds`); once that
window expires, `/health/ready` returns 503 until the background coordinator
publishes a new observation. `/health/live` remains independent of probe age.
The host refreshes probes periodically (10 seconds by default, configured with
`DataGuardHealth:RefreshIntervalSeconds`) outside request handlers and stops the
refresh loop cooperatively during shutdown. Values must be positive and no more
than one hour; invalid configuration rejects host startup.

## Limits and evidence

`FileReadableHealthProbe` opens the configured file; it does not parse or validate snapshot/baseline contents, signatures or freshness. `MemoryHealthProbe` measures managed allocations through `GC.GetTotalMemory`, not total process/container memory. Healthy local probes do not prove database availability or contract correctness.

The coordinator bounds how long it waits for a probe with `WaitAsync`; timeout does not forcibly stop a custom probe that ignores cancellation. Custom probe status messages are retained, so host authors must avoid secrets in those messages. The built-in probes and coordinator-generated failure messages are generic.

Source: `src/DataGuard.Core/Health/HealthProbeCoordinator.cs`, `LocalHealthProbes.cs`, and `src/DataGuard.Host/{Program,HealthEndpoints}.cs`. The explicit host must be started separately; enabling library telemetry does not start it. This page does not claim a running endpoint, configured TLS certificate, authenticated remote service, or a platform integration test result.
