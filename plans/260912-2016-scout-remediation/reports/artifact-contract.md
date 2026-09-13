# CP8 artifact contract

This contract fixes the distribution boundary for the three new runtime-facing
deliverables. It is an implementation contract, not evidence of shipment.

| Deliverable | Artifact boundary | Supported local evidence | Open gate |
|---|---|---|---|
| `DataGuard.Host` | Standalone executable archive with an explicit RID and the `dotnet DataGuard.Host.dll --urls http://127.0.0.1:<port>` launch form for framework-dependent builds. | `scripts/verify_host_publish.sh` publishes a framework-dependent `net9.0` artifact into a temporary directory, reports its SHA-256, requires DLL/deps/runtimeconfig closure, and proves loopback readiness from that output. `DATAGUARD_HOST_RID` enables a locked RID publish only after the owner pins its runtime graph. | RID-specific archives, remote-auth policy. |
| `DataGuard.Build` | A NuGet package with a task assembly and explicit `build` plus `buildTransitive` target imports. The target receives only an absolute manifest path; no implicit workspace discovery. | Focused test packs the nupkg, asserts the task DLL and both target imports, then restores/builds a clean local-package consumer with valid and invalid manifests. | Full manifest matrix, SARIF and diagnostic fixtures. |
| `DataGuard.LanguageServer` | A VS Code VSIX embeds a versioned, extension-relative stdio executable and records its SHA-256 in package metadata. It cannot download an executable at editor startup. | None. | Local protocol implementation, VSIX packing and extension-host edit smoke for each supported platform. |

## Build manifest boundary

`DataGuard.Build` accepts one operator-produced JSON file through
`DataGuardOfflineManifest`. The task must reject a missing, unreadable,
oversized, malformed, or unsupported-version manifest with a stable build error.
The manifest has a schema version, a target/provider identity, a content digest,
and a bounded list of redacted findings. The task never reads connection strings,
environment credentials, workspace YAML, project properties other than this
path, or the network. `DataGuardEnableDatabaseValidation` is deliberately not an
authorization mechanism and cannot enable live acquisition.

The initial package contract uses `DataGuard.Build.targets` under both `build/`
and `buildTransitive/`. Its target runs before `CoreCompile` only when
`DataGuardOfflineManifest` is explicitly set. An omitted property is a no-op;
a supplied invalid manifest fails closed. The target emits only stable diagnostic
IDs and redacted messages, never source connection data or manifest contents.

## Verification contract

The clean consumer fixture must restore/install only the produced package,
import its targets, compile with a valid fixture manifest, and demonstrate each
invalid input class above. A hostile project may set every legacy live-validation
property, but no task code may create an HTTP client, database connection, or
credential resolver. The fixture records package SHA-256, runtime, SDK and RID.

Windows VSIX installation, signed provenance/SBOM, platform secret stores, and
remote Host policy remain explicit external acceptance gates; this contract does
not mark any of them delivered.
