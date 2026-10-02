# Journal: OpenSSF Scorecard Root Cause Investigation and Remediation Architecture

**Date**: 2026-10-02 17:30  
**Component**: CI/CD Workflows, Supply Chain Security, Dockerfile, OpenSSF Scorecard  
**Status**: Brainstorm & Investigation Completed; Remediation Architecture Designed  
**Report**: `plans/reports/brainstorm-openssf-scorecard-remediation-261002-1728.md`  

---

## 1. Executive Summary

Investigated the OpenSSF Scorecard posture for repository `github.com/thanhnt-sm/eco_support_net_oracle` (Scorecard `v5.5.0`, current aggregate score: **7.5 / 10**).
While 10 critical checks achieved perfect 10/10 scores (SAST, Fuzzing, Dangerous-Workflow, Token-Permissions, Packaging, CI-Tests, License, Vulnerabilities, Binary-Artifacts, Dependency-Update-Tool), 7 checks were degraded or in error state.

Identified root causes across all degraded checks, evaluated trade-offs under KISS/YAGNI/DRY, and established a remediation architecture to achieve an elite supply-chain score (**9.5 - 9.8 / 10**).

---

## 2. Key Findings & Root Causes

1. **Branch-Protection (-1, Internal Error)**:
   - Default `GITHUB_TOKEN` in `.github/workflows/scorecard.yml` cannot query classic branch protection rules without administrative permissions.
   - Decision: Migrate branch protection to **GitHub Repository Rulesets** (`Settings` -> `Rules` -> `Rulesets`), which `GITHUB_TOKEN` reads natively without requiring high-privilege PATs in CI secrets.

2. **Signed-Releases (5 / 10)**:
   - Rolling `nightly` release created by `installers.yml` pushes completely unsigned binaries (CLI zips, VSIXs).
   - `release.yml` only signs `.nupkg` files via Cosign and omits CLI zips and VSIXs.
   - GitHub Artifact Attestations (`actions/attest@v4`) writes only to GitHub's internal database API and does not attach a physical `.intoto.jsonl` provenance bundle file to GitHub Release assets (OpenSSF Scorecard issue #4080).
   - Decision: Sign all release assets (nightly & stable) with keyless Sigstore cosign, and attach `.intoto.jsonl` provenance bundle files to release assets.

3. **Pinned-Dependencies (9 / 10)**:
   - `Dockerfile:40` runs `dotnet restore` without copying `packages.lock.json` and without `--locked-mode`.
   - Decision: Copy lockfiles into the Docker build stage and pass `--locked-mode` to `dotnet restore`.

4. **Code-Review (0 / 10)**:
   - 0/14 changesets had 2nd-party review approval before merging to `main`. Addressed by requiring PR approvals in GitHub Rulesets.

5. **CII-Best-Practices (0 / 10)**:
   - Missing self-certification badge from `bestpractices.dev`.

6. **Maintained (0 / 10) & Contributors (0 / 10)**:
   - `Maintained` is a temporal restriction (<90 days old); auto-resolves to 10 once age passes 90 days.
   - `Contributors` is an organic metric reflecting multi-organization contributor distribution.

---

## 3. Next Steps

1. Transition to `/ck:plan` to schedule implementation of Dockerfile lockfile pinning and workflow signing enhancements.
2. Execute manual configuration of GitHub Repository Rulesets on `main`.
3. Register the project on `bestpractices.dev` to attain passing CII badge.
