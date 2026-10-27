---
phase: 3
title: "PR and branch integration"
status: pending
priority: P1
effort: ""
dependencies: [1, 2]
---

# Phase 3: PR and branch integration

## Overview
Resolve all open PRs and unique unmerged branch changes without reverting newer main changes. PR #51 presently has zero changed files; prior integration evidence is in research/integration.md, not permission to delete branches.

## Requirements
- Functional: account for every branch/PR; create PRs for genuine remaining changes, never phantom PRs for squash-merged history.
- Non-functional: preserve user global.json; no force-push, admin merge bypass, branch deletion or unnecessary publishing.

## Architecture
Compare pinned branch SHAs with merged PR heads and later main commits. A merge-base diff measures original changes, NOT unmerged content after squash merge. Two-dot diff also includes newer main work missing from old branches. Neither is a zero-diff deletion gate.

## Related Code Files
- Read: research/integration.md and GitHub PRs #40/#45/#46/#47/#51.
- Modify source: only exact genuine residual changes discovered by refreshed comparison.
- Update evidence: reports/execution-ledger.md; preserve historical docs as historical records.
- Delete: none. Remote deletion is outside this task unless owner explicitly requests it.

## Implementation Steps
1. Refresh paginated branches and open PRs, capture full head/base SHAs. For each branch inspect prior PR head SHA, merged state, merge commit ancestry and post-PR commits; compare affected paths to current main and explain later main changes. Earlier research calls trees cryptographically identical despite different hashes: treat that as integration evidence to recheck, not equality proof.
2. For #51 use `gh pr view 51 --json headRefOid,files,state,baseRefName` and `gh pr diff 51`. If `state` is `OPEN`, the head is unchanged (90220325ab7ea8263b8797075eb94cc8b588b96d), and the diff remains empty, close it as a duplicate of #46 with an evidence comment; re-read immediately before mutation. If it is already closed, record the state and make no closure request. If the head or diff changed, stop the duplicate path and review new content.
3. No new PR is justified solely because squash-merged branch is graph-ahead. For genuine residual source changes, isolate in clean worktree, retain later main fixes, run complete affected test modules plus smoke, push only intended files and create `gh pr create --base main --head <branch>`.
4. For nonempty PRs update against fresh main, review exact SHA, satisfy all required checks and independent reviews (including code-owner/last-push approval). Merge with `gh pr merge <number> --squash --match-head-commit <reviewed-sha>`. Never use --admin. Re-evaluate each remaining PR after base advances.
5. If owner specifically insists on merging empty #51 rather than duplicate closure, obtain that decision; do not automatically generate meaningless commits or bypass reviews.
6. Verify resolution via paginated open PR list and exact closed/merged state. Retained already-integrated branches are reconciled, not orphan failures. No remote deletion necessary.

## Success Criteria
- [ ] Every branch has refreshed pinned-SHA integration evidence or a PR carrying its real residual changes.
- [ ] #51 resolved without discarding concurrently pushed changes.
- [ ] All actual merges satisfy immutable SHA, required checks and independent review gates.
- [ ] No branch deletion, verifier weakening, later-main regression or global.json staging.

## Risk Assessment
Squash merge creates unique graph commits without unique content. Comparing old branch tip directly to new main can appear to delete newer fixes. Never restore an old tree wholesale; inspect residual changes and preserve current main behavior. Concurrent pushes invalidate reviewed SHAs.
