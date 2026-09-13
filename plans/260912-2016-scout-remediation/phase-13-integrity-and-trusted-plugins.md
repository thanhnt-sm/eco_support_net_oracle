---
phase: 13
title: "Integrity and trusted plugins"
status: in-progress
priority: P1
effort: "3d"
dependencies: [4, 8]
---

# Phase 13: Integrity and trusted plugins

## Overview

Replace heuristic supply-chain/plugin trust claims with verifiable policy and deliver platform secret storage without fake encryption. Claims: `docs/PRODUCT.md:119-123,247-254`, `docs/03-components/core/security.md:256-291`, `docs/03-components/core/plugins.md:5,73-104`.

Current verifier is local hash plus assembly-prefix heuristic; `RulePluginManager` loads DLLs before provenance decision; `CredentialManager` can mark non-Windows plaintext encrypted. These are not signed provenance, trusted intake, or libsecret/Keychain evidence.

## Requirements

- Offline-verify signed provenance, artifact digest, signer identity, repository/workflow/ref policy, and SPDX SBOM subject/digest/license policy. Missing evidence is `Unverified` or strict-policy failure, never trusted prefix.
- Use a maintained NuGet signature/Sigstore verifier dependency or supported verifier integration; never handwritten signature/certificate cryptography. Release CI creates SBOM/provenance bound to built package digest.
- Plugin manifest/provenance/digest/API compatibility are verified before `AssemblyLoadContext.LoadFromAssemblyPath`. CLI defaults `RequireSigned`; rejection is safe/auditable; duplicate RuleId rejects deterministically.
- Select Windows DPAPI, Linux Secret Service/libsecret, or macOS Keychain through a secret-store abstraction. Encrypt-required mode fails closed when unavailable; no plaintext write may be labelled encrypted.
- A trusted plugin remains arbitrary in-process code; ALC is unload/type isolation, not sandbox. Invocation failure is contained by host boundary.
- Owner must approve replacement of the impossible absolute memory-dump claim with measurable no-log/no-serialization/best-effort-zeroing wording; it may not be silently closed.

## Architecture

**XR03 — immutable admission and independent trust:** operator-owned trust roots/identity policy live outside plugin/workspace control. Freeze offline bundle timestamp/transparency proof/freshness and trust-root update policy at CP8; self-supplied roots never establish trust. Signed manifests cover managed/native dependency closure. Read without following attacker-controlled links and verify from one handle; load the exact verified managed bytes with LoadFromStream or an owner-only immutable staging copy. Dependency resolution uses the verified admission map, never ambient unverified probing. Reject native dependencies unless the same immutable staging/verification boundary is demonstrated. Test swap between verification/load, symlink replacement, substituted policy/manifest, unsigned dependency, stale bundle and zero-network verification. Signatures still do not sandbox trusted code.

Create `SupplyChainPolicy`, `ProvenanceVerificationResult`, `ISignedProvenanceVerifier`, `ISbomVerifier`, and explicit policy states. Validate in-toto/SLSA subject digest and signature/certificate against pinned issuer, identity, repository, workflow and ref; validate SPDX package/license policy. Prefix list becomes non-authoritative hint or is removed from pass/fail.

Create `PluginTrustPolicy`, `PluginManifest`, `PluginAdmissionResult`, and shared verifier bridge. Manifest adjacent to DLL binds plugin ID/version/host API/digest/provenance. No reflection/load occurs before admission. Rule invocation receives host-owned exception boundary and lifetime disposal; ALC is never described as a security boundary.

Create `ISecretStore`, platform implementations, and typed unavailable result. Values remain in handles for minimum scope; config/audit/report only receive source/outcome. Extend public surfaces with wrappers/new result types, never direct positional-record or return-type breakage.

## Related Code Files

- Create: `src/DataGuard.Core/Security/Provenance/` contracts, maintained-verifier adapter, SPDX policy/parser adapter, `SupplyChainPolicy.cs`.
- Create: `src/DataGuard.Core/Plugins/PluginTrustPolicy.cs`, `PluginManifest.cs`, `PluginAdmission.cs`, safe invocation boundary.
- Create: `src/DataGuard.Core/Security/SecretStores/` with interface, DPAPI, Secret Service, Keychain, unavailable store.
- Modify: `SupplyChainVerifier.cs`, `Plugins/RulePluginManager.cs`, `CredentialManager.cs`, `ZeroTrustCredentialProvider.cs`, public factories/options only additively, release workflow/package metadata as needed.
- Create: `tests/DataGuard.Core.Tests/ProvenanceVerificationTests.cs`, `PluginTrustTests.cs`, `SecretStoreTests.cs`, approved fixture plugin/artifacts.
- Update after proof: product/security/plugins EN/VI docs with exact policy/platform semantics.

## Implementation Steps

1. Select maintained verifier through dependency review; define trusted issuer/identity/repository/workflow/ref/digest/license policy schema.
2. Implement offline provenance/SPDX verification; generate/upload/verify CI artifacts so nupkg, SBOM and provenance bind one digest.
3. Parse/admit plugin manifest before load; enforce CLI signed default, explicit legacy compatibility opt-in, digest and duplicate checks and safe rejection report.
4. Add invocation lifetime/error boundary and best-effort unload; never claim sandboxing.
5. Replace encryption branching with platform stores and fail-closed unavailable behavior; audit only source/outcome.
6. Obtain owner memory-dump language decision, then update docs to exact testable contract.

## Success Criteria

- [ ] Fixtures accept valid signed provenance/SPDX and reject tampered subject/digest, wrong signer/issuer/repo/workflow/ref, invalid evidence, missing SBOM, disallowed license.
- [ ] Release test proves generated package digest equals provenance and SPDX subject, with no network needed at runtime verification.
- [x] Trusted plugin loads only after admission; unsigned/tampered/incompatible/duplicate/malformed plugins reject before assembly load.
- [x] Throwing admitted plugin rule is contained; unload/disposal is tested best-effort without sandbox claim.
- [ ] DPAPI, Secret Service, and Keychain have fake contract plus platform-gated integration tests; unavailable backend writes no plaintext and never sets `IsEncrypted=true`.
- [ ] Audit/SARIF/evidence exclude representative tokens, connection strings and secret values; owner-gated memory wording is recorded before claim closure.

## Risk Assessment

Integrity verification is security-sensitive and release artifacts are external state. Maintained verification, pinned policy identities, offline fixtures, strict/diagnostic modes and CI digest linkage reduce risk. Signatures establish operator trust, not plugin behavioral safety; unavailable OS stores must fail closed rather than emulate encryption.
