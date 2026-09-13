---
phase: 10
title: "Online advisory and health score"
status: in-progress
priority: P1
effort: "2d"
dependencies: [4, 8]
---

# Phase 10: Online advisory and health score

## Overview

Deliver optional online NuGet advisory lookup and a conservative dependency-health score without weakening local assessment. Claims: `docs/03-components/core/assessment.md:79-87` and `docs/01-overview/feature-showcase.md:338-343,430-435`. Current implementation honors `AllowRemoteLookups` through a double opt-in OSV policy and publishes an additive `AssessmentReport.DependencyHealth` envelope; local assessment remains the default and never performs egress without explicit policy.

## Requirements

- CLI opt-in is `dataguard assess --remote-advisories osv --allow-network`; API requires existing `AllowRemoteLookups=true` plus explicit operator endpoint policy. Absent option means zero HTTP.
- OSV is primary provider: batch-query NuGet package name/resolved version, consume returned advisory IDs and `modified`; paginate each query independently using its `page_token`, then fetch each deduplicated ID's vulnerability detail. Do not invent pagination for the single-vulnerability detail endpoint.
- Findings carry stable identity, affected package/version, OSV URL/ID, retrieval timestamp, and confidence. Local lock findings survive remote failures.
- HTTPS only; block redirects, non-OSV default hosts, credentials in URI/header, private-package egress by default, and unbounded response/page/concurrency/retry work. Remote failure is `ToolError`, never clean.
- Numeric score exists only when coverage complete. Missing lock parse, resolved version, eligible package coverage, or lookup yields `Partial`/`Unknown`, never an optimistic 100.

## Architecture

**XR01 — package-origin privacy:** lock presence/name/prefix does not prove public origin. Unknown origin is non-egress by default. Require an operator-controlled explicit public-coordinate allowlist or independently established restore-source provenance; workspace content cannot approve itself. Offer a sanitized request preview before consent. Test mixed public/private, unknown origin, same ID from different sources and exact outbound bodies. Unclassified coordinates never leave the process and remain uncovered in score coverage.

Add `RemoteAdvisoryMode`, `RemoteAdvisoryPolicy`, `IRemoteAdvisoryClient`, `OsvAdvisoryClient`, `PackageCoordinate`, `AdvisoryObservation`, `DependencyHealthSummary`, and `ScoreState`. Preserve public compatibility through an additive report-details envelope or `AssessmentReportV2`; do not alter `AssessmentEngine.Run` return type or positional constructors.

Policy is constructed at CLI boundary from both flags and owner-approved endpoint override. It validates exact HTTPS origin, blocks redirect, caps package/page/body/concurrency, sets short timeout, honors cancellation, and caches only public advisory data in-memory per run. Durable cache is disabled unless an explicit path/TTL is selected.

Score `dependency-health-v1` starts at 100 and subtracts published capped weights for confirmed affected vulnerable versions, lock inconsistency, and unsupported TFM only when assessed. Report carries coverage numerator/denominator and formula/input version. Incomplete lookup cannot drive pass/fail.

## Related Code Files

- Create: `src/DataGuard.Core/Assessment/RemoteAdvisories.cs`, `DependencyHealthScoring.cs`, `Internal/OsvAdvisoryClient.cs`.
- Modify: `AssessmentContracts.cs`, `AssessmentEngine.cs`, `Internal/DependencyHealthPack.cs`, `DataGuard.Cli/Program.cs`, configuration only where required.
- Create: `tests/DataGuard.Core.Tests/RemoteAdvisoryTests.cs`, `DependencyHealthScoreTests.cs`, CLI option tests using fake `HttpMessageHandler` only.
- Update after proof: assessment/feature-showcase/security EN/VI docs with provider, egress, score coverage, timeout, and offline contract.

## Implementation Steps

1. Extract direct/transitive NuGet identities and resolved versions from supported lock shapes; mark absent/unparseable data uncovered.
2. Define policy/client first; disabled mode must construct no client and issue no HTTP request.
3. Implement OSV batch, independent per-query pagination, ID/modified handling, bounded per-ID detail fetches, origin/redirect validation, limits, cancellation, and error classification.
4. Map advisories to stable findings/provenance without sending workspace path, source code, package-source credential, token, or connection string.
5. Implement versioned formula, deterministic sort, coverage and partial/unknown state; do not add implicit CI exit policy.
6. Wire double opt-in and tests before changing capability claims.

## Success Criteria

- [x] Default assess and `AllowRemoteLookups=false` make zero fake-handler requests.
- [x] Explicit OSV mode sends only NuGet coordinates, preserves batch query ordering across independent pagination, fetches bounded deduplicated details, and emits provenance-backed findings.
- [x] Redirect, non-HTTPS, host violation, oversized body, page cap, 429, timeout, malformed JSON, and cancellation yield bounded errors while local findings remain.
- [x] Private/unapproved packages are not sent absent explicit owner policy; tests inspect request body.
- [x] Reordered lock data yields bit-for-bit same score; complete coverage is documented numeric and incomplete coverage is `Partial`/`Unknown`.
- [x] Report/SARIF/evidence are secret-safe/backward-compatible and ordinary CI unit tests never contact OSV.

## Risk Assessment

Advisory data changes, can rate-limit, and reveals package names. Double opt-in, endpoint policy, payload minimization, bounded client, coverage state, and local-first continuation prevent hidden egress and false assurance.

## Dependency Gates

Phase 11 may consume advisory data only through the versioned report envelope. Phase 15 must reject capability closure if a numeric score can be emitted for incomplete coverage or if ordinary offline assessment initiates egress.
