---
type: capability-delivery-ledger
date: 2026-09-12
scope: full-documented-capability-delivery
---
# Full claims delivery ledger

## Context

Owner explicitly selected implementation of missing documented features. [Amendment](scope-amendment.md) supersedes docs-only disposition. This registry seeds20 capability groups, **not an assertion that all individual claims have already been enumerated**. Phase8 must record each occurrence as FCxx.nnn in reports/claim-occurrences.md (future execution artifact), including EN/VI peers, source/test links and unmatched claims.

All group states below start open. Required acceptance includes actual implementation +tests +appropriate host/live proof +docs parity. “Planned” text, deletion of claim, no-op action or deferred feature is not closure. Terra owner allgroups, Sol reviewer; safety and external-authority constraints remain.

## Required delivery groups

| Group | Phase | Original parents | Capability | Claim evidence | Required delivery | State |
|---|---|---|---|---|---|---|
| FC01 | 9 | CI-10, DOC-04 | HTTP health live/ready/startup | [docs/PRODUCT.md:132](../../docs/PRODUCT.md:132) | Three real routes + startup/readiness checks incl credential/baseline/supply-chain/disk/memory, secure explicit host launch | open |
| FC02 | 10 | CS-05, DOC-03 | Online CVE/advisory lookup | [docs/01-overview/feature-showcase.md:430](../../docs/01-overview/feature-showcase.md:430) | NuGet identities; OSV querybatch/detail/paging; aliases/CVE/severity/source/time; opt-in egress | open |
| FC03 | 10 | CS-05, DOC-03 | Dependency health score | [docs/01-overview/feature-showcase.md:338](../../docs/01-overview/feature-showcase.md:338) | Versioned explainable score and coverage; incomplete/unknown never healthy100 | open |
| FC04 | 11 | CI-07 | VSCode commands/settings | [docs/03-components/tooling/vscode-extension.md:19](../../docs/03-components/tooling/vscode-extension.md:19) | Validate/Cancel/Assess/Refresh, provider/config/baseline settings, command args and host tests | open |
| FC05 | 11 | CI-07, TL-03 | VSCode LSP/realtime/output | [docs/01-overview/feature-showcase.md:222](../../docs/01-overview/feature-showcase.md:222) | Stdio LSP local diagnostics + debounced updates, safe bounded live streams, counts and run coordination | open |
| FC06 | 11 | CI-08 | VS Options and Results tools | [docs/03-components/tooling/visual-studio-extension.md:81](../../docs/03-components/tooling/visual-studio-extension.md:81) | DialogPage/provider/settings, Show Settings/Results Tool Window, persistence and Windows host proof | open |
| FC07 | 11 | CI-08 | VS build/run/cancel/live output | [docs/03-components/tooling/visual-studio-extension.md:151](../../docs/03-components/tooling/visual-studio-extension.md:151) | CTS/timeouts/dispose/build opt-in/stream redaction/ErrorList parity and host smoke | open |
| FC08 | 12 | TL-03, CE-07 | Heavy semantic/build validation | [docs/03-components/tooling/analyzers.md:127](../../docs/03-components/tooling/analyzers.md:127) | MSBuild integration + semantic inputs, offline manifests default; explicit live opt-in, no compiler network | open |
| FC09 | 12 | TL-01, TL-02, TL-04, TL-05, TL-06, DOC-04 | Full advertised code fixes | [docs/PRODUCT.md:142](../../docs/PRODUCT.md:142) | 12 vs5 taxonomy CP8 decision, all advertised IDs actual transformations+compile+FixAll; no dummy providers | open |
| FC10 | 12 | CE-01 | No-build C# ModelSnapshot extraction | [docs/03-components/core/sources.md:84](../../docs/03-components/core/sources.md:84) | Real generated C# fluent subset parsed without build/execution, supported matrix+unsupported diagnostic | open |
| FC11 | 12 | TL-05, TL-06, DOC-01 | Promised attributes/public facade | [docs/01-overview/feature-showcase.md:179](../../docs/01-overview/feature-showcase.md:179) | DataContract/SqlParameter/ResultSet and DataGuard.Validate semantics+namespace compatibility, real caller tests | open |
| FC12 | 13 | CS-11 | Cryptographic provenance/SBOM integrity | [docs/03-components/core/security.md:258](../../docs/03-components/core/security.md:258) | Verify signature identity/issuer/digest +in-toto/SPDX binding, offline trust policy; not package-name heuristic | open |
| FC13 | 13 | CS-10 | Plugin metadata/trust/lifecycle | [docs/03-components/core/plugins.md:73](../../docs/03-components/core/plugins.md:73) | Real metadata/API compat, pre-load verified manifest, owned lifetime and fail status; ALC not sandbox | open |
| FC14 | 13 | CS-07, DOC-07 | Cross-platform credential protection | [docs/PRODUCT.md:247](../../docs/PRODUCT.md:247) | DPAPI/SecretService/Keychain scoped OS stores +rotation/availability flows, failclosed backend unavailable | open |
| FC15 | 12 | CS-06, CI-04, CI-09 | Interactive init and hook CLI | [docs/PRODUCT.md:52](../../docs/PRODUCT.md:52) | init --wizard, hook install/status/uninstall wired to safe existing services; no overwrite/secrets default | open |
| FC16 | 12 | CS-12 | YAML contract export | [docs/01-overview/feature-showcase.md:67](../../docs/01-overview/feature-showcase.md:67) | Deterministic YAML using same schema asJSON, output/cancel/redaction/status coverage | open |
| FC17 | 12 | AD-03, AD-04, CE-07 | Routine body/result analysis | [docs/01-overview/feature-showcase.md:296](../../docs/01-overview/feature-showcase.md:296) | PG/MySQL static supported subset and result metadata, explicit Unknown for dynamic SQL, no automatic routine execution | open |
| FC18 | 14 | CE-02, CE-09, DOC-08, F7 | Performance/caching/allocation claims | [docs/PRODUCT.md:125](../../docs/PRODUCT.md:125) | Measured pipeline/analyzer/SARIF/baseline, memory+file cache1h contract, no stale live-drift proof, target optimization | open |
| FC19 | 8,13,14 | CE-03, CS-07, DOC-08, F7 | Absolute/contradictory guarantees | [docs/03-components/core/security.md:5](../../docs/03-components/core/security.md:5) | Owner-approved bounded threat/perf/architecture interpretation or remain blocked; never claim universal proof | open |
| FC20 | 8,15 | DOC-01, DOC-02, DOC-05, F1, F2 | Exhaustive claim census | [docs/01-overview/feature-showcase.md:3](../../docs/01-overview/feature-showcase.md:3) | Every current document occurrence mapped incl additions afterbaseline; all unmatched claims added beforeclosure | open |

## State and rollup

Keep66 original parents and15RT children intact. Add FC groups/subrows; totals independent:
- parent closed +blocked +open =66;
- RT child closed +blocked +open =15;
- FC group closed +blocked +open =20 (extend explicitly if discovered scope needs extra group);
- individual occurrences count=N determined by complete phase8 census, with same invariant.

Each mapped parent gains AND dependency on relevant FC group(s). Foundation implementation can be verified without closing the entire parent; store foundation_verified evidence separately. No regression of existing evidence, no rewriting historical “passed” dates. Feature subrow may be blocked_owner for an irreducible contradictory contract; parent stays nonclosed. Full program completion requires all required individual claims verified, not only all FC headers filled.

Additional mandatory AND dependencies:10 accepted XR obligations in [expanded red-team](reports/full-claims-red-team.md), with their own open/blocked/closed rollup and FC mappings. All start open; accepted into design does not mean implemented.

## Additional implementation details not lost between groups

- FC15 uses existing AutoDetectionEngine/PreCommitHookInstaller behind CLI commands; preserve RT transactional/containment/ownership protections.
- FC16 needs a new declared format and all consumer handling; the old five-format matrix is foundation, not a ban on adding YAML. Explicit parse/help/tests change required.
- FC17 cannot execute arbitrary routine bodies to deliver metadata. Supported static language matrix is part of documented capability; unsupported dynamic input gets status, never invented columns.
- FC18 caches pure content hashes by content/provider/canonicalizer version and can persist bounded cache for1h, but live diff must acquire current schema. TTL cannot authorize stale no-drift.
- FC19 includes zero-vendor dependency vs EF/provider architecture, never-memory-dump guarantees, zero-allocation/all-input performance absolutes and conflicting count wording. No silent scope reduction: owner decision or measured bounded contract required.
- FC20 includes credential-rotation, audit-chain and other advertised features not initially numbered: characterize existing implementation then add any missing behavior to FC subrows with concrete owner/test, rather than assume present.

## Bootstrap actions

Terra opens phase8 after phase1, expands individual claims and maps all old docs-only rows before production edits. Sol independently samples every current-doc family and checks per-file census coverage. Any codefix removed temporarily for safety in foundation must be restored as a correct implementation where current claim requires it; temporary removal cannot close FC09.
