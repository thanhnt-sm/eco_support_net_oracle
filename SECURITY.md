# Security Policy

## Reporting a Vulnerability

We take the security of DataGuard and the downstream .NET/EF Core ecosystems it protects seriously.

If you discover a potential vulnerability in DataGuard (Core, adapters, analyzers, CLI, or the VS Code
extension), please report it privately:

- Open a **private GitHub Security Advisory** at
  `https://github.com/thanhnt-sm/eco_support_net_oracle/security/advisories`
  (preferred — the advisory stays private until a fix is released)

Please include:

- The affected package/component and version (or commit SHA)
- A minimal reproduction (SQL, config, or code snippet)
- Impact description (data exposure, injection, denial of service, supply chain)

We aim to acknowledge reports within 5 business days and to ship fixes as fast as the severity allows.

## Supported versions

| Version | Supported |
|---------|-----------|
| 0.2.x (latest tag `v0.2.2`) | Best effort — see release notes; CLI 0.2.x is rejected by the IDE hosts for `validate`/`assess` |
| 0.3.x (upcoming, next tag `v0.3.0`) | Will be the supported line; required by the Visual Studio and VS Code extensions |
| 0.1.x | No longer supported |

## Security posture

- **Credentials**: database-backed CLI commands resolve the connection in this order:
  `--connection-env NAME` (the name of an environment variable) → `DATAGUARD_CONNECTION_STRING` →
  the credential provider (Azure Key Vault, AWS Secrets Manager, HashiCorp Vault when configured, then
  the encrypted credential file) → `ConnectionString` in `.dataguard.yml` **only** with
  `AllowPlaintextConfigFallback: true` (default `false`: the key is ignored with a one-line warning that
  names it, never its value). `--connection <value>` still works but prints
  `warning: a connection string on the command line is visible to process listings; prefer --connection-env`.
  A failing secret store is reported at Warning level with the store name (never the secret) and the
  next source is tried. `--offline` and `--ide-safe` never consult secret stores. The AWS SDK ships
  only with the CLI; `DataGuard.Core` exposes `ISecretStore` for hosts to register their own stores.
- **Credential file at rest**: `EncryptConnectionStringAtRest` defaults to `true` — Windows DPAPI,
  macOS Keychain, or the Linux Secret Service through `/usr/bin/secret-tool`. When no backend is
  available the file falls back to owner-only plaintext with a warning; set
  `RequireEncryptedCredentialStore: true` to refuse instead.
- **Supply chain**: NuGet packages are signed (Sigstore keyless), published via Trusted Publishing
  (OIDC), and carry SBOM + provenance attestation; GitHub Actions are SHA-pinned.
- **Licence allow-list**: CI fails when any NuGet package resolved by `DataGuard.sln` (including
  transitive ones) or any production npm package of the VS Code extension carries a licence outside
  `scripts/allowed-licences.txt` (permissive SPDX ids plus justified, marker-pinned per-package
  exceptions such as the Visual Studio SDK and the Oracle driver); see `scripts/check-nuget-licences.py`.
- **CI gates**: vulnerability scan (fail on vulnerable packages), TruffleHog secret scan, and CodeQL
  run on every branch/PR and tag release.
- **Audit**: credential access is written to an append-only hash-chain log with tail-truncation
  detection. With a key (`DATAGUARD_AUDIT_KEY`, or the file named by `AuditKeyFile`; at least 16 bytes,
  kept outside the repository and the log directory) every link is HMAC-SHA256, so an attacker who can
  write the log directory but lacks the key cannot forge or re-chain entries. Without a key the chain is
  plain SHA-256 and `VerifyIntegrityAsync` reports `Unkeyed` (intact but forgeable) instead of `Valid`.
  `CredentialManager` and every other component write through the same `IAuditLogger` (one chain, no
  raw lines); connection-string fingerprints are HMACs under a per-log random salt stored in the
  checkpoint header, and masked configuration values keep at most four characters.
- **Manual mode**: `ManualContractSource` reads attributes through `MetadataLoadContext`: no code from
  the assembly runs and it never enters the default load context. A `ManualAssemblyPath` taken from
  `.dataguard.yml` additionally requires `--allow-assembly-from-config` (otherwise exit 2);
  `--offline --assembly <path>` on the command line needs no flag.
- **Plugins**: rule plugins load only from an explicitly configured directory into an isolated,
  collectible assembly-load context.
- **IDE hosts (`validate` / `assess`)**: the Visual Studio and VS Code extensions run `validate` and
  `assess` with `--ide-safe`, which ignores every repository-controlled setting that could load an
  assembly (`GroundTruthMode: Manual`, `ManualAssemblyPath`), open a database, secret-manager or
  network connection, or write to a repo-chosen path, and clamps `MaxDegreeOfParallelism` to the
  processor count, `MaxViolationQueueSize` to 100 000 and `ValidationTimeoutSeconds` to 900. The CLI
  prints `ide-safe: active` as its first stderr line; a host discards the results when that line is
  missing (an older CLI, 0.2.2 and below, rejects the flag and is reported as too old — CLI 0.3.0 or
  later is required). A baseline still applies under `--ide-safe`, but every suppression is visible:
  `baseline: <n> violations suppressed by <path>` on stderr and a `BaselineApplied` progress event.
- **IDE hosts (user credential)**: `--allow-env-connection` (only with `--ide-safe`, `validate` only)
  keeps the `DATAGUARD_CONNECTION_STRING` credential the host supplies (or the variable named by
  `--connection-env`, which `--ide-safe` rejects unless `--allow-env-connection` is also given;
  `--allow-assembly-from-config` is always rejected under `--ide-safe`); connection strings from
  `.dataguard.yml` are always stripped. VS Code passes it only when a credential is stored in its
  SecretStorage. Under `--ide-safe`, `validate` uses that credential **only to read the
  ground-truth catalog** (schema and stored-procedure definitions); it never sends or describes
  repository-extracted SQL against the database — the live SQL-shape check
  (`sp_describe_first_result_set` over repository SQL) stays disabled under `--ide-safe` and runs
  only through `verify-shape`. `snapshot`, `baseline` and `verify-shape` are live-database
  commands: in VS Code they use your credential and always ask for a modal confirmation that names
  the masked target host; Visual Studio exposes no live-database command.
- **Visual Studio trust gate**: one-time consent per solution file (keyed by the solution directory,
  the `.sln` path and the `.dataguard.yml` hash) before the first run, never prompts from build
  events, never auto-installs the CLI, and only accepts an absolute custom CLI path.
- **CLI distribution**: `DataGuard.Cli` is not yet published on nuget.org (the package ID is to be
  reserved by the owner) — install `dataguard` from
  [GitHub Releases](https://github.com/thanhnt-sm/eco_support_net_oracle/releases): each release
  attaches `dataguard-<version>-<rid>.zip` (`win-x64`, `linux-x64`, `osx-arm64`; framework-dependent,
  .NET 9 runtime required) with a `.sha256` file next to it and a build-provenance attestation —
  verify the checksum before use; the Visual Studio VSIX bundles `cli\dataguard.exe`, and CI and
  release both assert the VSIX contents (`scripts/assert-vsix.ps1`). VSIX artifacts built from fork
  pull requests are never uploaded.
- **Hardening**: every regex in the CLI process runs with a 1 s match timeout, and SQL literals over
  256 KiB are skipped with a `[WARN] DG1291` note instead of being fed to the rules.
