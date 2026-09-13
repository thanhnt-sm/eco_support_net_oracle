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

This Core layer does not expose HTTP routes or bind a listener. The explicit host
and its loopback/authentication policy are a separate delivery item.

`DataGuard.Host` accepts only one or more non-empty loopback HTTP(S) URLs; malformed, remote, or other-scheme URLs are rejected before binding.

The shipped local probes cover configured snapshot and baseline readability, free
disk space, and managed-memory budget. An omitted snapshot or baseline is
`Unknown`; an empty readiness definition is never healthy. These probes do not
open database connections, resolve credentials, or make network requests.

Readiness also requires a fresh snapshot. The host defaults to a 30-second
maximum snapshot age (`DataGuardHealth:MaximumSnapshotAgeSeconds`); once that
window expires, `/health/ready` returns 503 until the background coordinator
publishes a new observation. `/health/live` remains independent of probe age.
The host refreshes probes periodically (10 seconds by default, configured with
`DataGuardHealth:RefreshIntervalSeconds`) outside request handlers and stops the
refresh loop cooperatively during shutdown. Values must be positive and no more
than one hour; invalid configuration rejects host startup.
