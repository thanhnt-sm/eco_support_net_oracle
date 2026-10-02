# Journal: OpenSSF Scorecard Hardening Implementation Plan (TDD & Red-Team Validated)

**Date**: 2026-10-02 17:45  
**Component**: CI/CD Workflows, Supply Chain Security, Dockerfile, OpenSSF Scorecard  
**Status**: Plan Ready for Implementation  
**Plan**: `plans/261002-1738-openssf-scorecard-remediation/plan.md`  

---

## 1. Executive Summary

Authored a comprehensive 4-phase technical implementation plan to remediate all degraded OpenSSF Scorecard checks for repository `github.com/thanhnt-sm/eco_support_net_oracle`, projecting an aggregate score increase from **7.5 / 10** to **9.5 - 9.8 / 10** (Elite Supply Chain Tier).

The plan was constructed following strict Test-Driven Development (TDD: Red -> Green -> Verify) conventions and vetted through an adversarial Red-Team review (Assumption Destroyer, Failure Mode Analyst, Security Adversary) and critical post-plan validation.

---

## 2. Plan Architecture & Phases

1. **Phase 1: Dockerfile NuGet Lockfile Pinning** (`phase-01-dockerfile-nuget-lockfile-pinning.md`):
   - Copies `packages.lock.json` alongside `.csproj` for all 9 referenced projects before `dotnet restore`.
   - Adds `--locked-mode` to line 40 to enforce deterministic dependency resolution without network package floating.
   - Guarded by new TDD test `scripts/tests/test_dockerfile_pinning.py`.

2. **Phase 2: Nightly Installer Sigstore Signing & Provenance** (`phase-02-nightly-installer-sigstore-signing-and-provenance.md`):
   - Modernizes `.github/workflows/installers.yml` with `id-token: write` and `attestations: write`.
   - Signs all 5 nightly build artifacts (CLI zips, VSIXs) via keyless Sigstore cosign (`*.sigstore.json`).
   - Generates SLSA in-toto build provenance and attaches `dataguard-nightly.intoto.jsonl` to the GitHub release.
   - Enforces atomic publish sequence (Gate R-01) and `shopt -s nullglob` wildcard guards (Gate R-02).

3. **Phase 3: Release Workflow Signing & In-Toto Attestations** (`phase-03-release-workflow-signing-and-in-toto-attestations.md`):
   - Expands Cosign keyless signing in `release.yml` across CLI zips and VSIXs (parity with NuGet packages).
   - Stages and uploads `dataguard-${{ github.ref_name }}.intoto.jsonl` directly to GitHub Release assets via `gh release upload`.

4. **Phase 4: Policy Gates & Scorecard Governance** (`phase-04-policy-gates-and-scorecard-governance.md`):
   - Integrates automated CI policy linting in `scripts/check-workflow-policy.py`.
   - Documents step-by-step GitHub Repository Rulesets setup on `main` (natively readable by standard `GITHUB_TOKEN`, resolving `Branch-Protection: -1` to `10/10` without admin PATs).
   - Provides registration instructions for OpenSSF Best Practices badge on `bestpractices.dev` and embedding in `README.md` and `README.vi.md`.

---

## 3. Next Steps

Execute implementation via:
```bash
/ck:cook plans/261002-1738-openssf-scorecard-remediation --TDD
```
