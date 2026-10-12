# Technical Journal: GitHub Jobs, Security Scans & Branch Integration Planning

**Date:** 2026-10-13
**Repository:** `thanhnt-sm/eco_support_net_oracle`
**Base Commit (`main`):** `79d72d2acff0cf5314ad875c9accfbfff6bfed44`
**Plan Reference:** `plans/2026-10-13-github-jobs-scans-main-integration/plan.md`

---

## 1. Context & Objectives

The user requested an end-to-end plan to:
1. Fix all GitHub Actions automated workflows, jobs, and security code scanning alerts.
2. Create and process all Pull Requests required to integrate all remote branches into `main`.
3. Strictly adhere to three mandatory verification gates:
   - **Consistency:** Grep every call site or duplicate copy across the entire codebase if any pattern or check is changed.
   - **Scope:** Ensure changes are strictly minimal; confirm behavior outside the issue is unchanged.
   - **Verification:** Execute full test modules/files; prevent sibling test regressions.
4. Maintain inviolable invariants: zero modification or weakening of test assets, preservation of user's local `global.json` modification, and zero destructive actions.

---

## 2. Evidence & Discoveries

### 2.1 GitHub Actions Workflows & Jobs
- On `main` HEAD (`79d72d2`), push CI run `38159541059` completed with 11/11 jobs `success`, while scheduled CI run `38258474397` completed with 10 jobs `success` + 1 job `skipped` (`docker-smoke`, scheduled trigger condition).
- Historical CI failures (minimatch 10 `BlueOak-1.0.0` licence issue in run `37560442889`, transitive `System.Private.Uri 4.3.0` vulnerability in runs `258`/`259`) were resolved in prior commits merged to `main`.
- Release workflow dry-run (run `37397905787`) passed, but `release.yml:755` skips `Publish to NuGet.org` on `dry_run: true`. Actual live NuGet OIDC publishing remains an **unresolved external prerequisite** requiring owner configuration of the trusted publisher policy on nuget.org.
- **Evidence limit:** CI successes do not establish absence of all source defects or success of live publishing.

### 2.2 Security & Code Scanning
- Corrected open-alert query with pagination finds alert #19 (Scorecard BranchProtectionID). Previous zero-open statement came from the default first page and was incorrect.
- Latest CodeQL (`csharp`) analysis has 0 findings across 57 rules.
- Scorecard analysis is active. Dependabot alerts are disabled at the repo level.

### 2.3 Remote Branches & Open PRs
- Exactly 5 branches exist on `origin`: `main` and 4 feature/doc branches (`ci/scorecard-signed-releases-provenance-backfill`, `docs/handoff-261006-alert19-branch-compare`, `docs/handoff-261006-pr40-pr45`, `docs/redteam-261004-source-vs-goals`).
 - Prior PRs #40 (`d0ec928`), #45 (`170e40d`), #46 (`c9bfaf1`), and #47 (`cbe47e9`) provide integration evidence. Historical affected-path comparisons are not a current-main residual comparison or branch-deletion gate.
- PR #51 (`docs/handoff-261006-pr40-pr45`) is now **CLOSED** with unchanged head `90220325ab7ea8263b8797075eb94cc8b588b96d` and 0 changed files. A fresh `gh pr view` confirmed this state; no further duplicate closure is needed.

---

## 3. Decisions & Implementation Architecture

1. **Phase 1 (Inventory and Baseline):** Complete audit of all workflows, alerts, and branch trees; preserve `global.json`.
2. **Phase 2 (Automation and Security Fixes):** Guard zero-regression baseline; run policy and licence verifiers locally.
3. **Phase 3:** Refresh prior-PR and pinned-SHA evidence, resolve empty #51 without phantom commits; retain remote branches, no deletion required.
4. **Phase 4:** Verify full scope, retaining open Scorecard alert #19 and real NuGet publication prerequisites until evidence closes them; enforce consistency, minimal scope and full-module verification.

---

## 4. Next Steps

Execute implementation via `/ck:cook plans/2026-10-13-github-jobs-scans-main-integration/plan.md`.
