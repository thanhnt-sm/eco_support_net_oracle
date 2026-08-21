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
| 0.1.x (pre-release) | Best effort — see release notes |

## Security posture

- **Credentials**: secret managers (Azure Key Vault, AWS Secrets Manager, HashiCorp Vault) or environment
  variables are the only supported sources in production; plaintext config-file credentials are
  disabled by default (`AllowPlaintextConfigFallback=false`).
- **Supply chain**: NuGet packages are signed (Sigstore keyless), published via Trusted Publishing
  (OIDC), and carry SBOM + provenance attestation; GitHub Actions are SHA-pinned.
- **CI gates**: vulnerability scan (fail on vulnerable packages), TruffleHog secret scan, and CodeQL
  run on every branch/PR and tag release.
- **Audit**: credential access is written to an append-only tamper-evident hash-chain log with
  tail-truncation detection.
- **Plugins**: rule plugins load only from an explicitly configured directory into an isolated,
  collectible assembly-load context.
