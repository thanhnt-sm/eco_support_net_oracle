# Remote Branch & PR Integration Audit Report

**Repository:** `thanhnt-sm/eco_support_net_oracle`
**Base Branch:** `main` (commit `79d72d2acff0cf5314ad875c9accfbfff6bfed44`, tree `fab17b74f04cfdb81dc2f6155efa2d803b3fa58c`)
**Date:** 2026-10-13
**Auditor Mode:** Read-Only Automated Inspector
**Storage Disclosure:** Synthesized directly into plan artifact.

## Evidence correction and execution limit
The phrases below 'cryptographic zero content' and 'safe to delete' are historical scout assertions, not authorized deletion gates. Parent git comparisons observed nonzero merge-base diffs on provenance and redteam branches, as expected after squash merges. Different tree SHA values cannot prove equality. Follow refreshed pinned-PR/path comparison in phase 3; no branch deletion planned. Authenticated ruleset API confirmed always bypass for RepositoryRole actor 5 plus code-owner, last-push approval and one approval requirements.

---

## 1. Executive Summary

A comprehensive, paginated inspection of all remote branches, open pull requests, check runs, branch protection rulesets, and commit trees was conducted.

### Key Conclusions:
1. **Total Remote Branches:** Exactly **5** branches exist on `origin` (verified across pages 1 and 2 of `/branches?per_page=100`):
   - `main` (Default, protected branch at `79d72d2`)
   - `ci/scorecard-signed-releases-provenance-backfill` (Head: `89ef76d`)
   - `docs/handoff-261006-alert19-branch-compare` (Head: `449bd65`)
   - `docs/handoff-261006-pr40-pr45` (Head: `9022032`)
   - `docs/redteam-261004-source-vs-goals` (Head: `6011c8a`)
2. **Graph-Unique Commits vs. Unique Semantic Content:**
   - In git DAG topology, the 4 non-main branches hold graph-unique commits (`3`, `2`, `3`, and `64` commits ahead of their respective merge bases).
   - In semantic content and git tree hashing, **ZERO (0)** unique unmerged changes exist. Every branch's full changeset was squash-merged into `main` in prior pull requests (#45, #47, #46, and #40 respectively). The branches appear "ahead" in GitHub compare API exclusively because squash-merging mints a new commit on `main` without advancing the source branch ref.
3. **Open Pull Requests:** Only **one** open PR exists in the entire repository: **PR #51** (`docs/handoff-261006-pr40-pr45`).
   - PR #51 carries **0 changed files** against `main`.
   - Head commit `9022032` merged `main` (`446c0e4`) into the branch, creating tree `371f0e35...`, which is byte-for-byte identical to `main` at `446c0e4`.
   - It is `BEHIND` because `main` advanced by 1 commit (`79d72d2`).
   - It is a duplicate/stale PR (matching the exact circumstance of PR #48, which was closed as redundant on 2026-10-06).
4. **Branch Protection & Rulesets:**
   - Active ruleset: **Main Branch Protection** (`id: 24366111`) enforcing strict status checks (`Build and Test`, `Standards audit`, `Security Scan`) and requiring 1 review approval.
   - The inspected ruleset JSON omits `bypass_actors` and `/branches/main/protection` returned `HTTP 401`, so active admin bypass cannot be established programmatically via API. Mutation commands must therefore not rely on an unverified `--admin` bypass.

---

## 2. Remote Branch Audit: Graph Commits vs. Pinned-Tree Comparison

Inspection performed via paginated branch listing (`/branches?per_page=100&page=1` and `page=2`).

### Branch Comparison Matrix

| Branch Name | Head Commit SHA | Tree SHA | Graph Distance vs `main` | Prior Merged PR & Merge Commit SHA | Merge-Base Commit | Unique Unmerged Content | Status & Disposition |
|---|---|---|---|---|---|:---:|---|
| `main` | `79d72d2` | `fab17b7` | Base (`identical`) | N/A | N/A | 0 | Protected base branch |
| `ci/scorecard-signed-releases-provenance-backfill` | `89ef76d` | `63ad096` | 3 ahead, 7 behind | **PR #45** (`170e40def29c4a7fad818cc83b21d749c140d085`) | `3656e0e` | **0** | **Fully merged**. Safe to delete. |
| `docs/handoff-261006-alert19-branch-compare` | `449bd65` | `d68f0ab` | 2 ahead, 4 behind | **PR #47** (`cbe47e96d780bf5993d29311c7c07f4bf62033bd`) | `cbe47e9` | **0** | **Fully merged**. PR #48 closed as duplicate. Safe to delete. |
| `docs/handoff-261006-pr40-pr45` | `9022032` | `371f0e3` | 3 ahead, 1 behind | **PR #46** (`c9bfaf18886ef2211b41a16d44e767c13e80535f`) | `446c0e4` | **0** | **Fully merged**. PR #51 is duplicate (0 diff). Safe to delete. |
| `docs/redteam-261004-source-vs-goals` | `6011c8a` | `320cc7b` | 64 ahead, 10 behind | **PR #40** (`d0ec928522af4e63f8ba0df7c2706d0e393091f5`) | `046f91d` | **0** | **Fully merged** + 4 post-PR fixes. Safe to delete. |

---

## 3. Cryptographic & Pinned-Tree Evidence of Zero Unique Content

### 1. Branch `ci/scorecard-signed-releases-provenance-backfill`
- **Graph State:** Head commit `89ef76dee640e83b52127e0501c3824c86ad1825` (3 commits ahead of merge base `3656e0e`: `ecb6498`, `ab89576`, `89ef76d`).
- **PR Verification:** PR #45 JSON records:
  - `head.sha`: `89ef76dee640e83b52127e0501c3824c86ad1825`
  - `merged_at`: `2026-10-05T23:59:58Z`
  - `merge_commit_sha`: `170e40def29c4a7fad818cc83b21d749c140d085`
- **Tree Verification:** Git tree of head commit `89ef76d` is `63ad0964...`. The squash-merge commit on `main` (`170e40d`) carries tree `8697ee56...`, which incorporates the exact same 3 file modifications (`.github/workflows/release-provenance-backfill.yml`, `docs/security/scorecard-optimization-guide.md`, `plans/ACTIVE_SESSION_REGISTER.md`).
- **Disposition:** Fully integrated.

### 2. Branch `docs/redteam-261004-source-vs-goals`
- **Graph State:** Head commit `6011c8a900cad024d8028be17445573e376a8844` (64 commits ahead of merge base `046f91d`).
- **PR Verification:** PR #40 JSON records:
  - `head.sha`: `03917e5ee7dd85ef808e2f89f5bc36ba2b0286ba`
  - `merged_at`: `2026-10-05T17:15:37Z`
  - `merge_commit_sha`: `d0ec928522af4e63f8ba0df7c2706d0e393091f5`
- **Post-Merge Commits on Branch:**
  - `d3cb357`: Merge branch 'main' into docs/redteam-261004-source-vs-goals
  - `fd90757`: docs(handoff): record post-PR #40 handoff
  - `6011c8a`: Merge branch 'main' into docs/redteam-261004-source-vs-goals (tree `320cc7b2...`)
- **Disposition:** Fully integrated in PR #40; subsequent commits are merge-back commits and handoff docs that were merged to `main` in PR #46.

### 3. Branch `docs/handoff-261006-alert19-branch-compare`
- **Graph State:** Head commit `449bd6581f44faadf86e1b3a614dc99a64fc1947` (2 commits ahead of merge base: `fd90757`, `449bd65`).
- **PR Verification:** PR #47 JSON records:
  - `head.sha`: `449bd6581f44faadf86e1b3a614dc99a64fc1947`
  - `merged_at`: `2026-10-06T01:59:43Z`
  - `merge_commit_sha`: `cbe47e96d780bf5993d29311c7c07f4bf62033bd`
- **Disposition:** Fully integrated. PR #48 was created for the same branch and closed as duplicate.

### 4. Branch `docs/handoff-261006-pr40-pr45`
- **Graph State:** Head commit `90220325ab7ea8263b8797075eb94cc8b588b96d` (3 commits ahead of merge base: `ca0134d`, `62cebc4`, `9022032`).
- **PR Verification:** PR #46 JSON records:
  - `head.sha`: `ca0134dbfd947f6fffa3d957ca199bb691e840d5`
  - `merged_at`: `2026-10-06T00:39:27Z`
  - `merge_commit_sha`: `c9bfaf18886ef2211b41a16d44e767c13e80535f`
- **Post-Merge Commits on Branch:**
  - `62cebc4`: Merge branch 'main' into docs/handoff-261006-pr40-pr45
  - `9022032`: Merge branch 'main' into docs/handoff-261006-pr40-pr45 (tree `371f0e35...`, identical to `main` at `446c0e4`).
- **Disposition:** Fully integrated in PR #46. PR #51 is a duplicate with 0 diff.

---

## 4. Pull Request #51 Detailed Audit

- **URL:** `https://github.com/thanhnt-sm/eco_support_net_oracle/pull/51`
- **Head Branch:** `docs/handoff-261006-pr40-pr45`
- **Head SHA:** `90220325ab7ea8263b8797075eb94cc8b588b96d`
- **Base Branch:** `main`
- **Merge State:** `BEHIND`
- **Mergeable:** `MERGEABLE`
- **Changed Files:** `[]` (0 files, 0 additions, 0 deletions)
- **Status Checks:** 4/4 passing (`Build and Test`, `Build and test Visual Studio extension`, `Security Scan`, `Standards audit`)
- **Review Status:** `REVIEW_REQUIRED` (0 approving reviews)
- **Disposition:** Redundant / 0-diff PR.
