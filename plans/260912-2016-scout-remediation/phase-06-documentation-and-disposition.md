---
phase: 6
title: "Documentation and disposition"
status: blocked
priority: P2
effort: "M"
dependencies: [4, 5]
---

# Phase 6: Documentation and disposition

## Overview

**Full-delivery amendment:** read [scope amendment](scope-amendment.md) and [FC ledger](full-claims-ledger.md). Phase8 contracts precede source edits. This phase supplies foundation work only; legacy docs-only exclusions/claim removal cannot close a required feature. Any temporary safety removal must be followed by the mapped Phase9–14 implementation. Phase15, not Phase7, owns full closure. Preserve all safety and compatibility acceptance below.

Synchronize canonical English/Vietnamese docs with implemented, verified behavior and make every finding disposition auditable. Default is bugfix plus honest docs, not speculative delivery. This phase has no new production logic; CE-03 package description metadata and CS-06 wizard wording/characterization are explicitly scoped below.

## Requirements

- Update canonical DataGuard docs only; preserve archived/historical EcoSupport labeling.
- State four-provider snapshot matrix, resolver precedence, v3/legacy semantics, Manual offline contract, EF trust/opt-in, REF CURSOR metadata-only, action/provider reality and IDE limits accurately.
- Until delivery, mark health/CVE/score/IDE/build and performance capabilities pending honestly and retain their required FC acceptance. After implementation, align claims to verified behavior; no removal or qualification alone closes the feature.
- The coordinator-owned ledger records every ID evidence/owner/state. F6 is owner-gated: retain default; never mark all closed where blocked or evidence is missing.
- Preserve the evidence distinction: the current 650-test suite does not establish live DB assertions; Windows is separate.

## Architecture

Documentation is a view over source contracts and executed evidence. A canonical capability map links claims to code/test/logs; ledger dispositions retain decisions rather than hiding findings. EN/VI pairs change together in meaning, not necessarily line count.

## Related Code Files

- `docs/{01-overview,03-components,05-operations}/`, `docs/{PRODUCT.md,USAGE.md,architecture.md,STAGE_FLOW.md}`
- `docs/product-discovery/{capability-matrix.md,release-evidence.md}`
- Coordinator file `./findings-ledger.md`; design inputs `reports/{core-design,safety-design,sol-advisory}.md`
- Findings DOC-01–DOC-08, CI-05/07–10, TL-02/03, CE-01/03/08, CS-05/06/11, AD-06, F1/F2/F6/F7.

## Implementation Steps

1. Inventory each canonical EN/VI claim and map it to Phase 1–5 source/test evidence. Do not alter archived pages merely because wording is stale.
2. Update baseline/config/snapshot/EF/provider text for exact precedence, v3 migration and online/offline limits. No DB claim without Phase-7 executed marker.
3. Record current versus required analyzer/code-fix/IDE/assessment/host capabilities and link each pending claim to phases9–14. Final documentation must reflect delivered features, not the former local-only scope.
4. Reconcile quickstart/usage/product/architecture against canonical component pages. Stamp version/test claims with date/commit and qualify/remove unmeasured performance claims.
5. Coordinate (never overwrite) ledger updates: link source/test/evidence, leave blocked entries open, preserve CS-03 no-change probe, apply F6 retain or blocked_owner only with owner decision.
6. Validate docs-sync/links in Phase 7 and correct only failures attributable to these edits.
7. CE-03 changes `src/DataGuard.Core/DataGuard.Core.csproj` Description to remove zero-vendor-dependency claim; AD-06 package/version docs match actual locks without dependency upgrade. Run Release build for changed package metadata.
8. F2 repairs ADR001 supersession/banner and each broken ADR link by actual target lookup, preserving historical rationale. F1/F3 retain dated historical evidence rather than rewrite old results as current. CS-06 fake-console test proves Baseline→Snapshot+EnableBaseline and wording explains overlay, not new enum.
9. DOC-06 explicitly distinguishes dev plaintext opt-in from banking no-plaintext profile, consistent with CS-07 storage behavior. F6 default retain20 tracked pyc; proposed cleanup manifest/owner approval is separate, no deletion in this program without it. Record retain or blocked_owner honestly.

## Success Criteria

- [ ] Canonical docs state shipped behavior/limitations accurately in both languages.
- [ ] No unsupported health/CVE/IDE/DB-analyzer claim remains.
- [x] Historical docs remain classified, not erased or silently rewritten; the generated census records 15 historical/proposal files.
- [x] Ledger keeps F6 and all blocked/unevidenced findings non-closed; F6 remains `open` with the retain disposition.
- [ ] Current result claims link to dated, reproducible evidence.

## Risk Assessment

Docs create product contracts. Prefer narrow limitation language over roadmaps. Terra owns ledger and docs; Sol reviews read-only. Preserve all pre-existing edits and do not invent another concurrent writer.

## Blocker

Phase 6 is explicitly blocked until its upstream dependent Phase 4 and Phase 5 complete fully,
and until the remaining claims (health, CVE, IDE, DB-analyzer) are matched against reproducible
execution evidence and the updated `findings-ledger.md`.
