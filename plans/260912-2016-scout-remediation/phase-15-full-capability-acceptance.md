---
phase: 15
title: "Full capability acceptance"
status: in-progress
priority: P1
effort: "L"
dependencies: [7,9,10,11,12,13,14]
---

# Phase 15: Full capability acceptance

## Overview

Final acceptance for implemented claims. Phase7 is foundation verification, not completion of the expanded program.

## Requirements

Reconcile66 parents,15RT children,20FC groups and every individual claim. Mandatory regression + appropriate live/host evidence + EN/VI parity. No planned label, renamed claim or package-only smoke satisfies runtime requirements.

## Architecture

Core/CLI remains validation authority; Host checks state, LSP local analysis and Build invoke defined protocols. All consumers retain acquisition/execution completeness and redaction; online capability does not override offline default.

## Related Code Files

- All changed source/test projects, including proposed Host/Build/LanguageServer and platform verifiers.
- DataGuard.sln, relevant workflow/container recipes, docs current EN/VI and full-claims-ledger.md.
- reports/full-claims-evidence.md and reports/claim-occurrences.md produced in execution.

## Implementation Steps

**XR09/XR10 artifact and integration gates:** inspect produced Host archives, VSCode VSIX and Build nupkg for the CP8 artifact matrix, checksums/dependency closure and entrypoints. Install into a clean temporary consumer using only those artifacts (not source checkout/bin output), launch Host endpoints, activate packaged LSP on edits, and import Build targets for offline diagnostics. Record RID/host/runtime and keep unavailable platform claims blocked. Joint verifier→health transition tests must cover Unknown/Verified/Failed/Stale before FC01/FC12 close. Workflow editing and local packing are allowed; publication/dispatch is not inferred.

1. Release restore/build/test original4 and every added test project; enumerate actual projects, never reuse484 baseline count.
2. Run HTTP integration for three paths/startup/readiness/auth/limits; authorized local Docker smoke has no public listener.
3. Advisory mocks cover perquery pagination/detail lookup/aliases/withdrawn/CVSS/unknown/cache/429/timeouts. Explicit network public-package smoke only; never upload private dependency trees casually.
4. VSCode extension host proves LSP edits, commands/settings, safe streams, cancellation/replacement. Windows experimental VS proves Options/Results/Run/Cancel/build hooks; build/pack alone insufficient.
5. Build fixture always imports offline snapshot/manifest diagnostics; separately test operator-launched live preflight and hostile-project non-egress. Every codefix transformed fixture compiles and FixAll is tested.
6. Real generated EF source fixtures prove no-build extraction; static procedure body subset, wizard/hook CLI and JSON/YAML equivalence verified without arbitrary procedure execution.
7. Cryptographic negative corpus proves artifact digest/signer/issuer/provenance/SBOM policy before plugin load. Verify Windows/Linux/macOS secret-store integrations or retain platform blockers.
8. Reproducible benchmark matrix provides raw metrics/statistics/environment/tree; unmet target stays open rather than removed.
9. Docs-sync + changed links + EN/VI semantic claim audit + YAML/actionlint for touched workflows. No publish/dispatch/commit inferred.
10. Sol independently recomputes every group/subrow/parent relation, audits exact-tree evidence and reports blockers with owner/prerequisite/resume.

## Success Criteria

- [ ] Every required current capability implemented, behavior-tested and docs-aligned.
- [x] Safety/ABI/false-clean regression gates remain satisfied by the current local suite and plan checker.
- [x] No unmatched occurrence or parent closed before child in the current claim census/plan graph.
- [x] External/absolute/performance gates have explicit `blocked_external`/`blocked_owner` records with owner and resume evidence; blocked status still prevents full complete.
- [ ] Sol final GO for delivery, distinct from design GO.

## Risk Assessment

Windows/provider/signing access and absolute claims may block full delivery. Continue independent work but do not weaken scope/security/tests. Plan update does not initiate implementation or publication.

## Current blocker records

| Gate | State | Owner | Prerequisite to resume | Resume evidence |
|---|---|---|---|---|
| Windows Visual Studio/VSIX/DPAPI runtime | `blocked_external` | Terra + Windows operator | Windows host with the supported VS SDK/experimental instance and DPAPI profile | Redacted install, Run/Cancel/timeout, Error List containment, and credential-path log |
| Signed provenance/SPDX release binding | `blocked_external` | Release owner | Signed CI artifact plus pinned issuer/identity policy and generated SBOM subject digest | Offline verifier fixtures and package/SBOM/provenance digest equality |
| Linux Secret Service daemon | `blocked_external` | Linux operator | Linux host with an active Secret Service/libsecret session | Store/read integration marker and fail-closed unavailable case |
| Comparative performance claims | `blocked_owner` | Terra + Sol | Clean committed tree, declared CPU-bound corpus, sequential baseline, and accepted variance policy | Evaluator-accepted comparative run or explicit owner disposition of the failed target |
| CP8 provider/API reconciliation and final Sol GO | `blocked_owner` | Terra + Sol | Decision on five versus twelve providers and `DataGuard.Validate` compatibility signature | Updated FC09/accounting matrix and independent Sol delivery verdict |
