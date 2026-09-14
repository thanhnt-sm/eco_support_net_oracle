# Evidence Index

Các dòng dưới đây là chỉ dẫn tới bằng chứng trong workspace; không sao chép source vào bundle.

| ID | Finding | Class | Evidence (path:line range) |
|---|---|---|---|
| E-01 | Telemetry is opt-in; Meter/queue/timer created when enabled | CONFIRMED | `src/DataGuard.Core/Telemetry/TelemetryCollector.cs:24-53,451-470` |
| E-02 | Validation/rule metric names and tags | CONFIRMED | `TelemetryCollector.cs:127-166,514-524` |
| E-03 | Endpoint allow-list, NDJSON HTTP export, bounded batch, circuit breaker | CONFIRMED | `TelemetryCollector.cs:183-285,361-397` |
| E-04 | No OTel SDK/tracing/metrics endpoint found | CONFIRMED | repository-wide source/package search; corroborated by `TelemetryCollector.cs` only using `System.Diagnostics.Metrics` |
| E-05 | Pipeline enables telemetry/audit via config | CONFIRMED | `src/DataGuard.Core/PublicApi/PublicApiSurface.cs:64-70,123-127,249-255`; `Models/Configuration.cs:6-44` |
| E-06 | SARIF allow-list, text/path sanitizer, atomic sink | CONFIRMED | `src/DataGuard.Core/Reporting/DiagnosticEmitter.cs:16-32,133-201,204-333` |
| E-07 | Contract evidence redaction is narrower | INFERRED_MEDIUM | `src/DataGuard.Core/Reporting/ContractEvidence.cs:79-85` vs `DiagnosticEmitter.cs:265-285` |
| E-08 | File audit hash chain and verification | CONFIRMED | `src/DataGuard.Core/Security/IAuditLogger.cs:54-96,158-213,219-270` |
| E-09 | Separate direct audit append path | CONFLICT | `src/DataGuard.Core/Security/CredentialManager.cs:392-416` |
| E-10 | Health endpoints and loopback-only binding | CONFIRMED | `src/DataGuard.Host/Program.cs:3-32`; `HealthEndpoints.cs:5-20`; `HealthHostBinding.cs:1-15` |
| E-11 | Health probe freshness/timeouts | CONFIRMED | `src/DataGuard.Host/HealthHostOptions.cs:1-33`; `src/DataGuard.Core/Health/HealthProbeCoordinator.cs:3-74` |
| E-12 | Credential priority and plaintext fail-closed default | CONFIRMED | `src/DataGuard.Core/Security/ZeroTrustCredentialProvider.cs:100-183`; `src/DataGuard.Core/Models/Configuration.cs:6-44` |
| E-13 | Key Vault/Vault transport and token handling | CONFIRMED | `ZeroTrustCredentialProvider.cs:191-320` |
| E-14 | macOS Keychain and Linux Secret Service bounded process bridge | CONFIRMED | `Security/SecretStores/MacOsKeychainSecretStore.cs:7-96`; `LinuxSecretServiceSecretStore.cs:9-116` |
| E-15 | Plugin manifest/digest/provenance admission | CONFIRMED | `src/DataGuard.Core/Plugins/PluginAdmission.cs:65-131`; `RulePluginManager.cs:77-117` |
| E-16 | Supply-chain verifier fail-closed and dependency provenance state | CONFIRMED | `src/DataGuard.Core/Security/SupplyChainVerifier.cs:24-95,146-189` |
| E-17 | CLI safe output and read-only assessment path | CONFIRMED | `src/DataGuard.Cli/Program.cs:27-83,1038-1173` |
| E-18 | CI security gates and pinned actions | CONFIRMED | `.github/workflows/ci.yml:1-308`; `release.yml:1-607`; actionlint exit 0 |
| E-19 | Docker pinned base and non-root runtime | CONFIRMED | `Dockerfile:1-67`; `.dockerignore:1-16` |
| E-20 | Solution/project scope mismatch | CONFLICT | `DataGuard.sln` project entries vs 22 visible `.csproj` paths |
| E-21 | Dirty/untracked snapshot | CONFIRMED | `git status --porcelain --untracked-files=all`; `git diff --stat` |
| E-22 | No production topology/owner/SLO/retention evidence | UNKNOWN | repository-wide search of source/docs/workflows; see `gaps-and-unknowns.md` |

## Command evidence summary

- `actionlint .github/workflows/*.yml` → exit 0, no output.
- `xmllint` over all visible `.csproj` → exit 0.
- `jq empty` over all visible JSON lock/package files → exit 0.
- Secret-pattern scan → no concrete high-confidence secret in production source/config scope.
- No build/test/restore was run; no runtime or external network claims follow from static inspection.
