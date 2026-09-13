# Completion audit — current worktree

This is a current-state audit of the expanded remediation plan. A passing local
test proves only the requirement it exercises; it does not close a phase or the
program without the full acceptance evidence.

| Capability | Current evidence | Remaining evidence or implementation | State |
|---|---|---|---|
| Health host and loopback probes | `DataGuard.Host`, core probe tests, and a local loopback integration smoke are present. | Remote authentication/TLS/proxy/rate-limit policy and RID publish artifacts. | partial |
| Online advisories and health score | OSV policy/client, scoring types, and focused tests are present. | Operator-origin proof across real restore provenance and network acceptance. | partial |
| VS Code LSP | Local language server, VSIX packaging, digest manifest, clean installation smoke, and local extension-host activation, command-registration, and C# diagnostic smoke are present. | Supported-platform acceptance. | partial |
| Visual Studio integration | Source commands and documentation are present. | Windows VS SDK build, installation and host smoke. | blocked_environment |
| Offline build validation | `DataGuard.Build` validates bounded offline manifests; local-package clean-consumer integration test passes. | Operator-owned live preflight, complete semantic manifest contract and hostile-project matrix. | partial |
| C# ModelSnapshot parsing | Bounded syntax parser, direct `--ef-snapshot`, and source-only `--ef-project`/`--ef-context` selection are present with focused tests. | Full generated snapshot compatibility matrix and strict CLI acceptance. | partial |
| Snapshot schema capture | Refresh reuses one live acquisition and persists provider schema descriptors for Oracle, MySQL, PostgreSQL, and SQL Server; SQL Server and PostgreSQL live parser tests assert schema descriptors, while MySQL already emits one from `INFORMATION_SCHEMA.COLUMNS`. | Full four-provider snapshot refresh/diff matrix and strict acceptance evidence. | partial |
| Code actions | Five real providers, manifest-bound DG002 replacement, document compilation, and Fix All test are present. | Complete occurrence/action matrix and CP8 reconciliation of twelve-provider claim. | partial |
| Plugin admission | Digest/API/provenance gate, PE-reference closure/identity validation over verified main/dependency bytes, swap/symlink rejection, native fail-closed loading, manifest rule-ID duplicate/reserved-ID pre-load rejection, and admission tests are present. | Signed provenance, one-handle proof, and release evidence. | partial |
| Supply-chain integrity | Anchorless dependency checks fail closed; no prefix trust is treated as provenance. | Maintained signed provenance verifier, SPDX/in-toto binding and release digest proof. | open |
| Credential storage | Windows DPAPI and macOS Keychain encrypt-required paths are tested; Linux Secret Service uses `secret-tool` with secret standard input and fails closed when unavailable. | Linux host integration with a running Secret Service daemon. | partial |
| Performance claims | Bounded parser/classifier harnesses have host-scoped 15-sample results. The public pipeline pair has an output-equivalence gate and a full 100/1,000-contract comparison; its dirty-worktree run misses the 2–4× target (concurrent is 7.95×/2.47× slower). Generator, semantic analyzer, and streaming SARIF 100/1,000 corpora prove non-empty output before timing. | Clean committed evidence for every harness, a declared CPU-bound corpus/baseline, and owner decision on the failed target. | partial |
| Full acceptance | Local Release build, full C# suite (689 tests: Core 635, Golden 25, Analyzers 8, CodeFixes 21), docs sync, required live SQL Server/PostgreSQL/MySQL/Oracle Testcontainers gates, and local ARM64 Docker CLI smoke have passed. The ARM64 `act` `build-and-test` attempt hangs under local Rosetta emulation and remains an external CI gate. | Windows, signed artifact, native CI workflow and all occurrence-level acceptance gates. | open |

External environment gates are not treated as resolved: Windows VSIX/host proof,
Docker-backed workflow simulation, signed release trust material, and Linux
Secret Service integration each require their
corresponding environment and artifact inputs. macOS Keychain integration is
covered by the focused local test above.
