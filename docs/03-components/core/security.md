# Security subsystem

> Source: `src/DataGuard.Core/Security/`, `src/DataGuard.Core/Security/SecretStores/`; CLI AWS adapter: `src/DataGuard.Cli/Security/AwsSecretsManagerSecretStore.cs`.

These components resolve credentials, protect local credential storage where an OS backend is available, record audit events, and compare an assembly with a supplied hash. They are not a guarantee that arbitrary inputs contain no secrets, that process memory is protected, or that a release has verified SLSA provenance. This page describes source behavior; it is not evidence of live cloud or platform certification.

## Credential resolution

The current-source `ZeroTrustCredentialProvider` constructors compose Azure → host-registered stores in the supplied order → Vault. The resolver iterates that configured list and consults only stores whose `IsConfigured` is true; `ISecretStore` itself does not prescribe an order. With this composition, the overall lookup is:

| Order | Source | Boundary |
| --- | --- | --- |
| 1 | `DATAGUARD_CONNECTION_STRING` for database credentials, then `DATAGUARD_{NAME}` | Environment values take priority; treat environment/process access as privileged. |
| 2 | Azure Key Vault | Validate the configured endpoint before requesting an IMDS token. |
| 3 | Host-registered `ISecretStore` instances | CLI registers AWS Secrets Manager when configured; Core has no AWS SDK dependency. |
| 4 | HashiCorp Vault | Require configured `VaultAddress` to match independently supplied `VAULT_ADDR` before reading `VAULT_TOKEN`. |
| 5 | Local credential store | `CredentialManager.GetStoredConnectionStringAsync`; protection and availability depend on the platform. |
| 6 | Configuration | Only when `AllowPlaintextConfigFallback=true`; otherwise reject this fallback. |

A configured remote store returning no value permits the next source. Failures are logged with store/credential identifiers and exception type, not the arbitrary exception message, then resolution continues. Requested cancellation propagates. Thus an individual store failure does not abort all resolution. The plaintext fallback switch is intended for development; the provider checks the flag, not a separate runtime environment identity.

Azure accepts an HTTPS root of the form `https://name.vault.azure.net` on the default port, with no user information, query or fragment. It requests a managed-identity token from the fixed IMDS endpoint and escapes the secret name in the Key Vault request. This implementation does not support arbitrary or sovereign-cloud Vault domains.

HashiCorp Vault requires HTTPS root addresses without user information, query or fragment. Configured and trusted origins must have the same case-insensitive IDN host and port. It reads KV v2 `/v1/secret/data/{escaped-name}` and the nested `data.data.value` field. HTTPS alone is not authorization to send the ambient token to a repository-selected endpoint. The production shared HTTP client disables redirects; callers injecting clients into the store classes must preserve that requirement.

## Credential memory and storage

`CredentialHandle` holds a `char[]`; `Dispose` clears that array, and its finalizer is a fallback. `Use` passes the backing array to the callback and does **not** clear it after each call. Callbacks can retain/copy it; `GetString` creates an immutable string. Neither API prevents memory dumps, earlier string copies, or misuse by callers.

`CredentialManager` resolves environment → configured connection string → stored value through its own API. This is distinct from `ZeroTrustCredentialProvider`'s gated fallback policy; callers must choose the intended API.

| Platform | Protected storage |
| --- | --- |
| Windows | DPAPI `CurrentUser`, constant entropy `DataGuard.Credential.Protection`, `ENC:` plus base64 ciphertext in the record. |
| macOS | Login Keychain through Security.framework; the file stores a `KEYCHAIN:` reference. |
| Linux | Secret Service through `/usr/bin/secret-tool`; the file stores a `SECRET-SERVICE:` reference. Secret input uses stdin, not command arguments. |

Linux helper operations have a 10-second timeout and bounded output (1 MiB secret, 16 KiB error). OS backend selection or an injected `ICredentialSecretStore` does not prove the user's actual Keychain/Secret Service session is usable. With encryption requested but no backend available, storage warns and falls back to owner-only plaintext unless `RequireEncryptedCredentialStore=true`. A configured backend write failure fails rather than silently producing an encrypted-looking plaintext record. Explicit `EncryptConnectionStringAtRest=false` selects plaintext.

The credential file path rejects links/reparse points and linked ancestors, with the fixed OS temporary-root alias exception. Reads/records are capped at 1 MiB. Writes use a same-directory flushed temporary file and owner-only Unix permissions. The final path recheck narrows, but does not eliminate, a check-to-open race against a process able to mutate the directory.

Rotation detection compares the current and stored connection string, then emits audit fingerprints when they differ; it does not rotate a credential at the database. `CredentialRotationWarningDays` is declared configuration, not evidence of a scheduled rotation service in this component.

## Audit logging

`FileAuditLogger.Create(config)` selects the optional HMAC key from `DATAGUARD_AUDIT_KEY` or `AuditKeyFile` (trimmed text, at least 16 bytes). With a key, links use HMAC-SHA256; without one, links use SHA-256 and verification reports `Unkeyed`, not authenticated integrity. `NullAuditLogger` performs no logging.

Entries link the previous hash to serialized content. A per-path in-process lock and an exclusive writer file handle with bounded retries coordinate appends; the writer rereads the tail. The checkpoint holds the latest hash, keyed flag and per-file salt used for fingerprints. Verification checks link continuity, keyed-to-unkeyed downgrades, and the checkpoint, returning `Valid`, `Unkeyed`, `Tampered` or `KeyRequired`.

This is local integrity evidence, not an external immutable audit service. An unkeyed chain can be recomputed by an attacker who can rewrite its files; HMAC depends on protecting the key. Protect the log, checkpoint, and their directory. Crash/partial-write recovery and adversarial filesystem behavior require their own operational checks.

Known sensitive-text patterns are redacted; configuration-change masking retains at most the first/last two characters of longer values and masks short values. These mechanisms cannot identify every secret in arbitrary text. Do not supply secrets in names, free-form details, exception messages or configuration labels.

## Supply-chain checks

`SupplyChainVerifier.VerifyAsync` hashes the current Core assembly. A supplied anchor must be an absolute existing file path, no linked ancestors, and at most 1,024 bytes. `ExpectedHashMatch` compares the trimmed file text with the assembly SHA-256. The caller must establish the anchor's provenance independently: the implementation does not verify signatures, attestations or SLSA documents.

`AssemblyIntegrity` records whether an acceptable anchor path exists; equality is the separate `ExpectedHashMatch` result. Every referenced dependency is currently reported unverified. There is no built-in signed-provenance/SBOM verification integration that turns those dependency results into passes. `StrongNameSigning` is informational and always passes; the debug-build indicator can fail its check. `OverallPassed` requires every check to pass. Do not present this component as release certification.

## Configuration and evidence

Defaults: `EnableCredentialRotationDetection=true`, `EncryptConnectionStringAtRest=true`, `RequireEncryptedCredentialStore=false`, `EnableAuditLogging=true`, `AllowPlaintextConfigFallback=false`; cloud addresses/region and audit path/key file are optional. See [configuration](../../guides/configuration.md) for operator setup.

Relevant regression sources include `ZeroTrustCredentialProviderTests`, `SecretStoreEndpointTests`, credential-store tests, audit-chain tests and supply-chain tests under `tests/DataGuard.Core.Tests/`. Source/tests describe intended boundaries; this documentation update did not contact cloud endpoints, inspect credentials, or run Windows/macOS secret-store integration.
