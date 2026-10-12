---
phase: 1
title: "Inventory and baseline"
status: pending
priority: P1
effort: ""
dependencies: []
---

# Phase 1: Inventory and baseline

## Overview
Refresh the evidence captured in research/automation.md and research/integration.md before any remote mutation. Cover every workflow, scan, PR and branch; preserve the user's modified global.json.

## Requirements
- Functional: enumerate all jobs and branches, distinguish current failures from historical failures, establish required merge checks and permissions.
- Non-functional: no secret values in evidence; no test/verification changes, destructive Git operations or publishing during inventory.

## Architecture
GitHub API is the authoritative remote snapshot. Repository workflow YAML supplies exact job IDs and commands. Record SHA + timestamp + URL so stale observations cannot authorize a later merge.

## Related Code Files
- Read: `.github/workflows/ci.yml`, `build_release.yml`, `release.yml`, `installers.yml`, `marketplace.yml`, `scorecard.yml`, `standards-audit.yml`, `release-provenance-backfill.yml` under `.github/workflows/`.
- Read: `global.json` (user-modified; never stage implicitly), each referenced script/config/source identified by a failing log.
- Create/update execution evidence only: `reports/execution-ledger.md` under this plan directory.
- Modify/Delete source: none in this phase.

## Implementation Steps
1. Run `git remote -v`, `git status --short --branch`, `gh auth status`, `gh repo view --json nameWithOwner,defaultBranchRef,url`. Expected repo `thanhnt-sm/eco_support_net_oracle`, default `main`; preserve local `global.json` changes. Use a separate clean worktree for future implementation; do not stash/reset this tree.
2. Enumerate `gh workflow list --all` and `gh api --paginate 'repos/thanhnt-sm/eco_support_net_oracle/actions/runs?per_page=100'`. Group latest runs by workflow, event and relevant ref; retain old failed-run URLs as history, not unresolved defects when repaired later. Include Dependabot Updates, which is GitHub-managed rather than a normal editable YAML.
3. For active failures, run `gh run view <id> --json jobs,headSha,headBranch,event,conclusion,url` and `gh run view <id> --log-failed`. Read the implicated workflow/source ranges. Do not rerun simply to confirm an existing error. Record job/step, exact failing command and root-cause evidence.
4. Paginate `/code-scanning/alerts`, `/code-scanning/analyses`, `/dependabot/alerts`, `/secret-scanning/alerts` on the repository. Query open and closed/dismissed dispositions when API supports them. Zero alerts only counts with a successful API response; absent scanner analysis is not proof of a clean scan. Redact all secret fields; 403/404 remain explicit access/feature prerequisites.
5. Paginate `/branches` and `/pulls?state=open&per_page=100`; compare every non-main branch using `/compare/main...<branch>` with URL-encoded branch names. Read PR files/checks/reviews/head SHA. Inspect `/branches/main/protection`, `/rulesets` and applicable rule details. Capture contexts, merge strategy, reviews, signatures and up-to-date requirements.
6. Freeze the verification baseline: identify tests, fixtures, assertions, scanner policies, coverage threshold (CI currently 60%), lock checks, `scripts/check-workflow-policy.py` and all other verification assets. Do not weaken or rewrite these to pass. A genuine verifier defect requires an explicit separate user decision.
7. Assign every item a disposition: active repair, historically repaired, already integrated, unique branch needing PR, or explicit prerequisite blocker. Inventory completeness requires matching API totals/pages, not an arbitrary first-page limit.

## Success Criteria
- [ ] All workflow/job definitions and all returned API pages accounted for; newest relevant SHAs are recorded.
- [ ] Every open alert, PR and branch has an actionable disposition and evidence URL.
- [ ] Every active repair has exact file/symbol/command mapped before an edit.
- [ ] Local global.json preserved; no remote mutation or verifier weakening.

## Risk Assessment
Rate limits, deleted logs, API permissions and continuously moving heads can invalidate evidence. Retry narrower read endpoints where appropriate; capture the missing prerequisite precisely and finish all other inventories. Historical plans are not evidence that current source is fixed. Scheduled/release workflows may publish artifacts: inspect triggers and conditions before dispatching anything.
