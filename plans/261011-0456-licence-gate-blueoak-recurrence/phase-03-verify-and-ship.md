---
phase: 3
title: "Verification, CHANGELOG, ship, rebase PR #49"
status: in-progress
priority: P1
effort: "1h + CI time"
dependencies: [1, 2] # Confirmed: validation Q1=Yes
---

# Phase 3: Verify and ship

## Overview
Prove the whole change end-to-end, including the redistribution obligation, record it in the CHANGELOG, then (only on explicit user command) open the PR and get Dependabot PR #49 re-run on top of it.

## Related Code Files
<!-- Updated: Validation Session 1 - Q1 (SPDX OR parser) and Q4 (Delete LICENCE_GATE_PLAN.md) confirmed -->
- Modify: `CHANGELOG.md` — under `## [Unreleased]` (line 6), in a `### Fixed` subsection: one entry for the licence gate (BlueOak-1.0.0 and Zlib admitted, SPDX OR expression semantics implemented, NOTICES §3 updated, admission rule recorded in `scripts/allowed-licences.txt`). Follow the style of the entry at `CHANGELOG.md:83`.
- Delete: repo-root `LICENCE_GATE_PLAN.md` (untracked draft, superseded by this plan; `rules/workspace_governance.md` keeps plans under `plans/`).

## Verification (each with expected output)
| # | Command | Expected |
|---|---|---|
| V1 | `python3 -m unittest discover -s scripts/tests` | `Ran 92+N tests` `OK` |
| V2 | `python3 scripts/check-nuget-licences.py` (main lockfile) | `licence gate: OK`, exit 0 |
| V3 | `python3 scripts/check-nuget-licences.py --npm-lock /tmp/pr49-package-lock.json` | `9 production npm packages`, `licence gate: OK`, exit 0 |
| V4 | `python3 scripts/check-license-consistency.py` | `6 copies identical`, exit 0 |
| V5 | `git worktree add /tmp/pr49 origin/dependabot/npm_and_yarn/src/DataGuard.VSCode/npm-eac8022a8b && cd /tmp/pr49/src/DataGuard.VSCode && npm ci && npx --no-install vsce ls \| grep -E '^node_modules/minimatch/(LICENSE|package.json)'` | `node_modules/minimatch/LICENSE.md` listed, which proves the Blue Oak notice ships in the VSIX. Afterwards `git worktree remove /tmp/pr49` |
| V6 | `./scripts/verify_docs_sync.sh` | exit 0 (docs/CHANGELOG touched) |
| V7 | `python3 scripts/check-workflow-policy.py` | OK (no workflow change expected; confirms no accidental drift) |
| V8 | `git diff --stat` (tracked) + `git status --short` (untracked) | Tracked diff limited to: `scripts/allowed-licences.txt`, `scripts/check-nuget-licences.py`, `scripts/tests/test_check_nuget_licences.py`, 3× `THIRD-PARTY-NOTICES.md`, `CHANGELOG.md`. Untracked/staged paths: only `plans/261011-0456-licence-gate-blueoak-recurrence/` (`LICENCE_GATE_PLAN.md` deleted). |

If V5 shows the licence file missing (for example vsce excludes `*.md`), escalate: the NOTICES must then carry the full Blue Oak text. This is a STOP for the user, not a silent change.

## Ship steps (each needs an explicit user command)
1. Deterministic gate J4 (verify V8 matches exact scope), then advisor A3 (review evidence table V1–V8).
2. On a user command: `git switch -c fix/licence-gate-blueoak`, then stage only the V8 files, then `dg-git commit -m "fix(ci): admit BlueOak-1.0.0 and honour SPDX OR in licence gate"`. Pre-commit hooks run; never `--no-verify`.
3. On a user command: push, then `gh pr create` with the evidence table V1–V5 in the body.
4. After CI on the fix PR is green and it is merged: comment `@dependabot rebase` on PR #49, on a user command.
5. PR #49 CI step **NuGet/npm licence allow-list** must print `licence gate: OK`. Record the run id.

## Success Criteria
- [x] V1–V8 pass with outputs recorded.
- [ ] CI green on the fix PR. PR #49 rebased and green on the licence step.
- [ ] J4, A3, J5 passed.

## Risk Assessment
- PR #49 has another failing job unrelated to licences → out of scope. Report it; do not fold it in.
- Dependabot cannot rebase if PR #49 has been edited manually → close it and let Dependabot recreate it (user decision).
- SDK requirement: `global.json` pins `9.0.100` (`latestFeature`). Local machine must resolve a 9.0.x SDK (e.g. `9.0.318`), otherwise `dotnet list` aborts before npm evaluation runs.

## Execution status
- **V1–V8 Verification: PASS**
  - V1: `python3 -m unittest discover -s scripts/tests` → `Ran 101 tests OK` (92 baseline + 9 new).
  - V2: `python3 scripts/check-nuget-licences.py` (main lockfile) → `licence gate: OK`, exit 0.
  - V3: `python3 scripts/check-nuget-licences.py --npm-lock /tmp/pr49-package-lock.json` → `9 production npm packages`, `licence gate: OK`, exit 0.
  - V4: `python3 scripts/check-license-consistency.py` → `6 copies identical`, exit 0.
  - V5: `vsce ls` lists `node_modules/minimatch/LICENSE.md` (Blue Oak notice confirmed redistributed in VSIX).
  - V6: `./scripts/verify_docs_sync.sh` → exit 0.
  - V7: `python3 scripts/check-workflow-policy.py` → OK, exit 0.
  - V8: `git diff --stat` + `git status --short` → diff strictly limited to allowed 7 files (`scripts/allowed-licences.txt`, `scripts/check-nuget-licences.py`, `scripts/tests/test_check_nuget_licences.py`, 3× `THIRD-PARTY-NOTICES.md`, `CHANGELOG.md`).
  - Independent reviewer: 0 critical; tester: 8/8 PASS.
- **Ship steps pending explicit user command:**
  - Commit (`dg-git commit`): pending user command.
  - Push branch (`fix/licence-gate-blueoak`): pending user command.
  - Open PR (`gh pr create`): pending user command.
  - CI green on fix PR: pending PR creation and CI run.
  - `@dependabot rebase` on PR #49: pending merge of fix PR and user command.
  - Advisor checkpoint A3: pending user activation (`/advisor on`).
  - Control gate J5: pending PR #49 CI run.
