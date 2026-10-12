---
title: "GitHub jobs scans and branch integration into main"
description: "End-to-end evidence-based plan to audit and resolve all GitHub workflow jobs, security scans, and PR/branch reconciliations into main."
status: pending
priority: P1
effort: 8h
branch: main
tags: [ci, github-actions, code-scanning, branches, pr, verification]
blockedBy: []
blocks: [20261011-fix-review-findings]
created: 2026-10-13
---

# Plan: GitHub jobs scans and branch integration into main

## Overview

This implementation plan provides an evidence-based roadmap to resolve all GitHub automated jobs, workflows, code scanning alerts, and branch integrations into `main` for `thanhnt-sm/eco_support_net_oracle`.

1. **GitHub Actions Workflows:** On `main` HEAD (`79d72d2acff0cf5314ad875c9accfbfff6bfed44`), push run `38159541059` completed with 11/11 jobs `success`, and scheduled run `38258474397` completed with 10 jobs `success` + 1 job `skipped` (`docker-smoke`, because the scheduled cron trigger does not satisfy its push/PR/dispatch filter). Historical failures (minimatch BlueOak licence, Uri 4.3.0 vulnerability) have been resolved on `main`.
2. **NuGet OIDC Publishing Status:** The release workflow dry-run (run `37397905787`) passed, but `.github/workflows/release.yml:755` (`if: ${{ github.event_name == 'push' || inputs.dry_run == false }}`) **skips** `Publish to NuGet.org` on `dry_run: true`. Therefore, actual live OIDC publishing to nuget.org cannot be claimed resolved from dry-run passes and remains an **unresolved external prerequisite** requiring owner configuration of the trusted publisher policy on nuget.org.
3. **Security baseline:** Authenticated paginated `code-scanning/alerts?state=open&per_page=100` returns alert #19 (`BranchProtectionID`, Scorecard). Earlier zero-open claims inspected only the default first page and are retracted. CodeQL analysis 1936690091 has zero findings; secret scanning open alerts returns zero; Dependabot alerts are disabled (403).
4. **PR and branches:** Refreshed authenticated API confirms PR #51 CLOSED at 2026-10-12T19:02:49Z, same head 90220325ab7ea8263b8797075eb94cc8b588b96d; open PR list is empty. This planning session did not issue closure commands. Four retained branches have prior integration evidence; refresh pinned residual comparisons before proposing any new PR. No deletion required.
## Mandatory Execution & Verification Standards

In accordance with user requirements, every phase must satisfy three non-negotiable verification gates before claiming completion:
- **Consistency Gate:** If a pattern, signature, or check changed in one place, grep every other call site or duplicate copy needing identical change. A fix at only some matching sites fails.
- **Scope Gate:** If diff exceeds the minimal issue-resolving change, confirm behavior unchanged outside the reported issue. Prefer the smallest correct diff over a broader rewrite.
- **Verification Gate:** Run the issue's full test module or file, not only the expected-to-flip test. A sibling-test-breaking change fails.
- **Preservation Invariant:** Local modifications to `global.json` must remain untouched and unstaged.
- **Immutable Test Invariant:** Tests, verifiers, and coverage thresholds must never be weakened or deleted to force green CI.

## Phases

| Phase | Name | Status |
|-------|------|--------|
| 1 | [Inventory and baseline](./phase-01-inventory-and-baseline.md) | Pending |
| 2 | [Automation and security fixes](./phase-02-automation-and-security-fixes.md) | Pending |
| 3 | [PR and branch integration](./phase-03-pr-and-branch-integration.md) | Pending |
| 4 | [Main acceptance](./phase-04-main-acceptance.md) | Pending |

## Key Findings and Decisions

| Item | Status | Cryptographic Evidence / Finding | Resolution Decision |
|---|---|---|---|
| **CI Workflows** | Executed checks green | Push 11 successes; schedule 10 successes + docker-smoke skipped | Do not infer publish-only correctness; repair VS publisher verification gap in phase 2 |
| **NuGet OIDC Publishing** | Prerequisite Open | Dry run `37397905787` passed, but `release.yml:755` skips publishing on dry run | Unresolved prerequisite: requires owner setup of trust policy on nuget.org |
| **Code Scanning** | Open governance finding | Alert #19 BranchProtectionID remains open; CodeQL analysis has 0 findings | Resolve protection policy without bypass/weakening, or obtain explicit owner risk decision; never label accepted risk fixed |
| **PR #51** | Closed externally | Authenticated current API: CLOSED, head 90220325; open PR list empty | No further closure or empty merge needed; retain audit evidence |
| **Remote Branches (4)** | Prior integration evidence | PRs #40/#45/#46/#47; squash graph ahead is not unique content proof | Refresh pinned residual comparisons; retain reconciled branches, no deletion |

## Cross-Plan Dependencies

| Relationship | Plan | Status |
|---|---|---|
| Blocks workflow substeps only | `20261011-fix-review-findings` | Active; reconcile overlapping ci.yml/release.yml edits before hardening |
| Related, no output dependency | Marketplace, licence migration, scout remediation and init plans | Preserve existing scopes; no forced completion or reactivation |

## Evidence and completion boundary
Planning is not implementation. No remote settings, PR states or refs changed. NuGet log 37299220786 proves OIDC 401 (policy-creator username/trust mismatch) AND fallback API-key 403 (invalid/expired/package permissions); both remain prerequisites. Branch research integration assertions must be refreshed; different tree hashes are not equality proof. Deletion is not required by branch integration.

## Red Team Review
Safety review accepted: duplicate closure/no deletion, no merge-base false-zero gate. Evidence review accepted after source inspection: VS publisher lacks checksum/provenance verification and attestation-read permission; stale workflow inventory corrected. Implementation remains pending; no reviewer suggestion is treated as already fixed.

## Validation Log
Full seven-file plan artifact check passed: canonical phase sections, no scaffolds and all relative Markdown links valid. No production source, test or workflow changed in this planning session; application test-module execution is not claimed. External execution-ledger.md appeared with runtime/closure claims; only independently queried PR/alert state is adopted, not unattributed test results. TypeSafe todo done still rejected (P=0.16), so session state is not represented as complete.
