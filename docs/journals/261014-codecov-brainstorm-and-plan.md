---
title: "Codecov Free-Tier Brainstorm, TypeSafe Judge Diagnostics, and Implementation Plan"
date: 2026-10-14
author: agent
tags: [codecov, ci, brainstorm, typesafe, openssf, plan]
---

# Journal: Codecov Free-Tier Integration & TypeSafe Gate Analysis (2026-10-14)

## Context
User requested integrating `thanhnt-sm/eco_support_net_oracle` with Codecov (`https://app.codecov.io/github/thanhnt-sm/eco_support_net_oracle/new`), researching online best practices for all free open-source features, adding coverage telemetry/badges/graphs to `README.md`, and diagnosing why `typesafe-planner.ts` emitted `meets_criteria=0.00` with `Deliverable verification rejected: implementation is evading work, malicious, or stubs out needed functionality` on intermediate `todo(op="done")` calls.

## What Happened
1. **Repository Scouting (`ck:scout`)**:
   - Mapped `.github/workflows/ci.yml`, `.github/workflows/standards-audit.yml`, `scripts/check-workflow-policy.py`, `scripts/anti_garbage_guard.sh`, `scripts/preflight_agent_check.sh`, and `src/DataGuard.VSCode/package.json`.
   - Identified critical repo constraints: 40-char SHA pinning on all GitHub Actions, root-file allowlist (requiring `.github/codecov.yml` instead of root `codecov.yml`), fail-fast `dotnet test` requirement (no `continue-on-error: true`), and locked NuGet/npm dependencies.
2. **Codecov Best-Practices Research (`ck:research`, `ck:docs-seeker`)**:
   - Verified `codecov/codecov-action@v7.1.1` (`303a32d7a59b442fa8d48b6a1cc6825c09c847a5`) supports OIDC tokenless auth (`use_oidc: true` with automatic fork PR tokenless fallback) and consolidated Test Analytics (`report_type: test_results`, deprecating `codecov/test-results-action`).
3. **TypeSafe System One, JEV & `ck:advisor` Evaluation**:
   - Explicitly invoked `xd://typesafe_judge` (`scope_completeness = 0.95`, `recommended_tier = Option B` with `0.97` confidence), `xd://typesafe_advisor` (`AUTO_APPROVE`, `confidence = 0.98`), `xd://typesafe_expert_review`, and `ck:advisor` subagent (`CodecovAdvisorReview`, score `9.4/10`).
   - Diagnosed `C:/Users/thant/Projects/omp_extension/typesafe-planner.ts` and `src/cook-expert-judge.ts`:
     - `typesafe-planner.ts` intercepts `ask` and `todo(op="done")` silently inside the extension hook (`pi.on("tool_call")`).
     - `runPhaseMacroCheck` (`src/cook-expert-judge.ts:253-277`) only sends 3 `choice` questions (`deliverables_verdict`, `architectural_drift`, `scope_mode`) and never requests `meets_criteria` (`noul`), so `rawMeetsCriteria` is `undefined`. When `deliverables_verdict === "REJECT"` (triggered when intermediate research/clarification tasks are marked done before files are written, or while `plan-cli.cjs` stub files with `TBD.` exist on disk), line 336 hardcodes `meetsCriteriaNoul = 0.0` and line 360 hardcodes the fallback string `"Deliverable verification rejected: implementation is evading work, malicious, or stubs out needed functionality."`
4. **Plan Creation (`ck:plan`)**:
   - Created 3-phase implementation plan at `plans/261014-codecov-integration/plan.md`.

5. **Codecov UI & GitHub Secrets Activation (`computer` + `gh`)**:
   - Inspected `https://app.codecov.io/github/thanhnt-sm/eco_support_net_oracle/config/general` in Chrome (`Profile 1` / `testsurvey`), confirming `Default Branch = main` and `Global Upload Tokenless = ENABLED`.
   - Set `CODECOV_TOKEN` in both GitHub Actions Repository Secrets and Dependabot Secrets via `gh secret set`.
   - Uploaded baseline Cobertura coverage (`dotnet-unit`) for `main` (`3975c06`), flipping the Codecov v2 API status to `active: true`, `activated: true` with **64.08% coverage** (`13,219 / 20,626` lines across `171` files) and live Sunburst visualization.

## Decisions
- Selected **Option B (Full Free-Tier Suite)**:
  - `.github/codecov.yml` with 3 flags (`dotnet-unit`, `live-db`, `vscode-ext`) + 6 components + GitHub Checks annotations + PR comment layout.
  - Zero-dependency `scripts/trx_to_junit.py` + Node 22 built-in `--experimental-test-coverage` (`lcov` + `junit` reporters) to keep `packages.lock.json` and `package-lock.json` untouched.
  - Bilingual `README.md` and `README.vi.md` updates with header badge, Component/Flag matrix, and Sunburst/Grid SVG graphs.

## Next
- Execute `/ck:cook D:/100.Software/Github/eco_support_net_oracle/plans/261014-codecov-integration/plan.md` to implement all 3 phases.
