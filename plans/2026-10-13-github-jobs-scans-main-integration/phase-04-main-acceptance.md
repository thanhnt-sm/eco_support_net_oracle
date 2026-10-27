---
phase: 4
title: "Main acceptance"
status: pending
priority: P1
effort: ""
dependencies: [1, 2, 3]
---

# Phase 4: Main acceptance

## Overview
Verify actual execution on exact final main SHA, preserving open blockers instead of treating skipped publishing, first-page results or CI success as full completion.

## Requirements
- Functional: complete workflow/scan/branch ledger; all issue-specific acceptance proven.
- Non-functional: consistency, smallest correct diff, full affected module verification; no test/scanner/coverage weakening.

## Architecture
Paginated APIs and actual job/step conclusions prove remote state. Full module execution plus changed-path smoke proves source behavior. Local plan CLI proves plan readability only, not implemented fixes.

## Related Code Files
- Read: all eight .github/workflows YAML files; research reports and execution ledger.
- Read/run unchanged: scripts/tests, DataGuard.CrossPlatform.slnf and complete affected test projects identified in phase 2.
- Modify: execution evidence only in this phase.
- Delete: none.

## Implementation Steps
1. Pin final main SHA using `gh api repos/thanhnt-sm/eco_support_net_oracle/commits/main --jq .sha`. Paginate runs and `/commits/<sha>/check-runs?per_page=100`; select newest relevant run per workflow/event, not merely first 10 global runs. Pending checks are not success. Verify required context names against ruleset 24366111; unexplained skipped/neutral/cancelled required work fails acceptance.
2. Baseline CI: push 38159541059 has 11 successes; schedule 38258474397 has 10 successes and expected docker-smoke skip. Confirm explicit condition in ci.yml before accepting schedule skip. Inventory release, installers, marketplace, artifact build, provenance backfill, Scorecard, standards and managed Dependabot separately; retain historical and external blockers in ledger.
3. Paginate `code-scanning/alerts?state=open&per_page=100`, `code-scanning/analyses?ref=refs/heads/main&per_page=100` and secret scanning open endpoint. Select newest CodeQL by tool + ref + SHA, not analyses[0] which may be Scorecard. Alert #19 BranchProtectionID remains open until real governance correction and new Scorecard result prove closure. Dependabot disabled response is a prerequisite, never clean-alert evidence.
4. NuGet acceptance: configure Trusted Publisher matching repository/workflow/environment and user/package ownership. Verify genuine OIDC login success (not merely fallback API key) and successful package publication in an owner-authorized real release, with package version and run URL. Do not publish arbitrary test versions or tags to obtain proof. Dry-run skips publish-nuget (release.yml:755); if real-release prerequisites unavailable, leave implementation acceptance blocked.
5. Paginate branches/open PRs and capture resolved PR state. All unique changes integrated or explicitly blocked; already integrated retained branches do not require deletion. Refresh heads to detect new work.
6. Consistency: use repository grep tool across all affected callsites, duplicated configs, documentation and shipped copies for each changed pattern. LSP references required for exported code symbols when available. Record matches/dispositions.
7. Scope: inspect intended change list; minimal issue-resolving diff only. Preserve unrelated global.json and all verifier assets. Explain unchanged behavior outside issue for any broader necessary change.
8. Verification: run complete affected test file/module (Python full discovery, .NET complete project, npm full existing suite as applicable), never only one expected-to-flip test. Run actual changed CLI/API/packaging path smoke. Existing LiveDb tests require working Testcontainers; unavailable runtime is a blocker, not skipped success. For documentation-only changes, no runtime modules are affected: run `scripts/verify_docs_sync.sh`, `actionlint .github/workflows/*.yml`, and the full plan-file/link/frontmatter check; record that no application tests executed.

## Success Criteria
- [ ] Every enumerated job, scan, PR and branch has refreshed evidence and disposition.
- [ ] Required work executes successfully on exact accepted SHA; expected schedule skip explained.
- [ ] NuGet real OIDC login and actual publication proven; otherwise explicit unresolved blocker, no full implementation completion claim.
- [ ] Alert #19 governance finding resolved with scanner evidence; any owner risk acceptance separately labeled, never fixed.
- [ ] Consistency/scope checks recorded and entire affected modules plus changed-path smoke pass.
- [ ] User global.json preserved; no branch deletion or forced bypass.

## Risk Assessment
A successful top-level workflow may skip publishing. Alert list default pages omit older open findings. Newer main commits invalidate prior results. Never substitute docs correctness or dry-run packaging for real remote behavior.
