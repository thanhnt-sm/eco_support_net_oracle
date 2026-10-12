---
phase: 2
title: "Automation and security fixes"
status: pending
priority: P1
effort: "4h"
dependencies: [1]
---

# Phase 2: Automation and security fixes

## Overview
Repair confirmed failures only. Push CI has 11 successes; schedule has 10 successes and expected docker-smoke skip. CodeQL analysis has zero findings, but explicit paginated open query finds Scorecard alert #19 BranchProtectionID. NuGet actual OIDC publication remains unverified; dry-run skips it.
## Requirements
- Functional: Ensure all 8 GitHub Actions workflows (`ci.yml`, `release.yml`, `build_release.yml`, `installers.yml`, `marketplace.yml`, `scorecard.yml`, `standards-audit.yml`, `release-provenance-backfill.yml`) execute cleanly on triggers without artificial workarounds or suppressed tests.
- Non-functional:
  - **Consistency Gate:** If any pattern, signature, or check is modified in one workflow or file, grep every other call site or duplicate copy across the entire repository to ensure identical updates. Partial fixes are rejected.
  - **Scope Gate:** If any diff exceeds the minimal issue-resolving change, confirm behavior outside the reported issue remains unchanged. Prefer the smallest correct diff over broad rewrites.
  - **Verification Gate:** Run the issue's full test module or file, not only the expected-to-flip test. Any sibling-test-breaking change is rejected.
  - **Immutable Verifiers:** Do not alter test expectations, soften thresholds, bypass allow-lists, or delete tests to force green CI.

## Architecture
- All CI jobs enforce least privilege (`contents: read` floor with granular job permissions).
- Workflow policy checks are enforced via `scripts/check-workflow-policy.py` and `scripts/tests/test_check_workflow_policy.py`.
- Licence compliance is enforced via `scripts/check-nuget-licences.py` against `scripts/allowed-licences.txt`.
- DataGuard dual licensing is enforced via `scripts/check-license-consistency.py`.

## Related Code Files
- Inspect/Modify (conditional upon failure reproduction):
  - `.github/workflows/ci.yml`
  - `.github/workflows/release.yml`
  - `.github/workflows/marketplace.yml`
  - `.github/workflows/scorecard.yml`
  - `.github/workflows/standards-audit.yml`
  - `scripts/check-workflow-policy.py`
  - `scripts/check-nuget-licences.py`
  - `scripts/check-license-consistency.py`
  - `scripts/allowed-licences.txt`
- Verification Suite:
  - `scripts/tests/test_check_workflow_policy.py`
  - `scripts/tests/test_check_nuget_licences.py`
  - `scripts/tests/test_check_license_consistency.py`
  - `DataGuard.CrossPlatform.slnf`

## Implementation Steps
0. Resolve owner prerequisites explicitly: configure nuget.org Trusted Publisher to match repository, release.yml and release environment; verify on an authorized real release without creating arbitrary tags/versions. For alert #19, obtain independent reviewer access and owner-approved removal of always-admin bypass while preserving all review/check rules, then run Scorecard and inspect the alert. Never claim policy acceptance is remediation. If Dependabot alerts remain disabled, record that limitation and ask owner before enabling security settings. Do not change settings during planning.
   - Confirmed publisher repair: `marketplace.yml` job `publish-visualstudio` needs job-level contents: read and attestations: read plus a PowerShell verification step before publishing. Require exactly one expected VSIX and its checksum; compare SHA-256, invoke `gh attestation verify` for the repository and fail on nonzero `$LASTEXITCODE` (PowerShell ErrorActionPreference alone does not ensure native failure). Pass only verified absolute path to VsixPublisher. Reuse VS Code publisher's fail-closed pattern; compare all publishing copies before edit. Run full scripts/tests module unchanged plus isolated smoke with valid/checksum-mismatch/missing-artifact/failed-attestation cases without Marketplace PAT or network publication. Actual publishing still requires protected environment credentials; never dispatch publish to test gates.
1. **Reproduce Baseline Locally:**
   - Execute workflow policy check locally:
     ```bash
     python3 -m unittest discover -s scripts/tests -v
     python3 scripts/check-workflow-policy.py
     python3 scripts/check-nuget-licences.py
     python3 scripts/check-license-consistency.py
     ```
   - Execute solution build and full test modules without weakening any assertion:
     ```bash
     dotnet restore DataGuard.CrossPlatform.slnf --locked-mode
     dotnet build DataGuard.CrossPlatform.slnf --configuration Release --no-restore
     dotnet test DataGuard.CrossPlatform.slnf --configuration Release --no-build --filter "Category!=LiveDb"
     ```
2. **Triaging Latent / New Failures:**
   - If a workflow failure is detected, inspect the exact failing step via `gh run view <id> --log-failed`.
   - Distinguish environment/network transient errors (e.g. NuGet feed timeout) from deterministic defects.
   - For deterministic failures (e.g. dependency licence mismatch, policy violation):
     - Identify root cause.
     - Prepare minimal diff resolving ONLY the root cause.
3. **Mandatory Consistency Check:**
   - Use the repository grep tool across source, workflows, scripts, tests and duplicated shipped assets for each changed pattern; record every match and disposition.
   - Verify every identical usage is updated in lockstep.
4. **Mandatory Scope & Verification Check:**
   - Run `git diff` to confirm zero collateral modifications.
   - Run the entire test suite (`dotnet test` for the complete test project, `python3 -m unittest` for scripts) to confirm all sibling tests pass.

## Success Criteria
- [ ] `python3 -m unittest discover -s scripts/tests -v` passes 100%.
- [ ] `python3 scripts/check-workflow-policy.py` passes with zero violations.
- [ ] `python3 scripts/check-nuget-licences.py` passes with zero allow-list violations.
- [ ] `python3 scripts/check-license-consistency.py` passes with zero license discrepancies.
- [ ] All CI test projects pass with >=60.0% code coverage maintained without test tampering.
- [ ] Consistency check verified: all matching patterns updated across the repository.
- [ ] Scope check verified: minimal diff, no unrelated behavioral changes.
- [ ] Verification check verified: full test files pass without sibling regressions.
- [ ] NuGet genuine OIDC login and actual publishing proven on authorized release; otherwise explicitly blocked.
- [ ] Scorecard alert #19 resolved with new analysis evidence, or explicit owner risk decision recorded as unresolved acceptance exception.
- [ ] Visual Studio publisher refuses checksum mismatch and failed/missing provenance before VsixPublisher; full existing policy test module and isolated PowerShell smoke pass without publishing.

## Risk Assessment
- **Risk:** Modifying workflow YAML without validating policy scripts triggers policy gate failures in CI.
  - **Mitigation:** Always run `python3 scripts/check-workflow-policy.py` before any workflow commit.
- **Risk:** Updating dependencies introduces unapproved licences (e.g., historical `BlueOak-1.0.0` or `FluentAssertions 8+`).
  - **Mitigation:** Validate `scripts/check-nuget-licences.py` and `scripts/allowed-licences.txt` with explicit legal justification.
