---
phase: 4
title: "Policy Gates and Scorecard Governance"
status: completed
priority: P2
effort: "2h"
dependencies: [1, 2, 3]
---

# Phase 4: Policy Gates and Scorecard Governance

## Overview
Lock in OpenSSF Scorecard supply-chain hardening by establishing automated CI policy gates in `scripts/check-workflow-policy.py`, configuring GitHub Repository Rulesets for branch protection, and registering the OpenSSF Best Practices badge on `bestpractices.dev`.

## Requirements
- **Functional Requirements**:
  - `scripts/check-workflow-policy.py` MUST enforce:
    - All GitHub Actions in `.github/workflows/*.yml` are pinned by 40-character commit SHA with `# vX.Y.Z` comment.
    - `Dockerfile` enforces `--locked-mode` on all `dotnet restore` calls.
    - `installers.yml` and `release.yml` enforce Cosign keyless signing and in-toto provenance generation.
  - Provide verified instructions for configuring **GitHub Repository Rulesets** on the default branch (`main`) so that Scorecard's `Branch-Protection` check passes without requiring high-privilege PATs.
  - Document registration on `https://www.bestpractices.dev/` and add the OpenSSF Best Practices badge to `README.md` and `README.vi.md`.
  - Trigger and verify the automated Scorecard audit workflow (`.github/workflows/scorecard.yml`).
- **Non-functional Requirements**:
  - Zero admin-scoped Personal Access Tokens in repository secrets.
  - All CI gates run within standard Ubuntu GitHub Actions runners in <10 seconds.

## Architecture
```
Repository Governance Layer
├── CI Policy Gate (scripts/check-workflow-policy.py)
│   ├── Enforces Dockerfile --locked-mode
│   ├── Enforces Action SHA pinning (100%)
│   └── Enforces Cosign + In-Toto release signing
├── GitHub Repository Rulesets (Settings -> Rules -> Rulesets)
│   ├── Target: default branch (main)
│   ├── Pull Request: 1 approval
│   ├── Status Checks: build-and-test, standards-audit
│   └── GITHUB_TOKEN read access (Scorecard Branch-Protection -> 10/10)
└── OpenSSF Best Practices (bestpractices.dev)
    └── README.md & README.vi.md Badge (CII-Best-Practices -> Passing/10)
```

## Related Code Files
- Modify: `scripts/check-workflow-policy.py` (add Dockerfile locked-mode & signing policy checks)
- Modify: `scripts/tests/test_check_workflow_policy.py` (add regression tests)
- Modify: `README.md` & `README.vi.md` (embed OpenSSF Best Practices badge)
- Modify: `docs/05-operations/runbook.md` (add Scorecard verification instructions)

## Implementation Steps (TDD: Red -> Green -> Verify)

### 1. Step 1 (RED): Add CI Policy Gate Assertions
In `scripts/tests/test_check_workflow_policy.py`, add:
```python
def test_dockerfile_locked_mode_enforced_by_policy(self):
    # Verify policy script catches unpinned or missing --locked-mode in Dockerfile
    ...

def test_workflow_signing_enforced_by_policy(self):
    # Verify policy script enforces cosign signing step in installers.yml and release.yml
    ...
```
Run test:
```bash
python3 -m unittest scripts/tests/test_check_workflow_policy.py
```
**Expected Outcome**: **FAIL (RED)** — policy script does not currently check Dockerfile or release signing patterns.

### 2. Step 2 (GREEN): Update `scripts/check-workflow-policy.py`
Add checks to `scripts/check-workflow-policy.py`:
1. Check `Dockerfile` for `dotnet restore` without `--locked-mode`.
2. Check `.github/workflows/installers.yml` for `cosign sign-blob` and `attest-build-provenance`.
3. Check `.github/workflows/release.yml` for `cosign sign-blob` and `attest-build-provenance`.
Re-run test:
```bash
python3 -m unittest scripts/tests/test_check_workflow_policy.py
```
**Expected Outcome**: **PASS (GREEN)**.

### 3. Step 3 (GOVERNANCE): Configure GitHub Repository Rulesets
1. In GitHub repo, go to **Settings** → **Rules** → **Rulesets** → **New ruleset**:
   - **Ruleset Name**: `Main Branch Protection`
   - **Enforcement status**: `Active`
   - **Target**: `Include default branch`
   - **Branch rules**:
     - Check: `Require a pull request before merging` (Required approvals: 1, Dismiss stale pull request approvals on new commits).
     - Check: `Require status checks to pass before merging` (Required: `build-and-test`, `standards-audit`).
     - Check: `Block force pushes`.
     - Check: `Require signed commits`.
2. Under **Settings** → **Branches**, delete the legacy Classic branch protection rule.
3. This allows the default `GITHUB_TOKEN` in `scorecard.yml` to query rulesets via REST/GraphQL API without errors.

### 4. Step 4 (BADGE): OpenSSF Best Practices Registration
1. Visit `https://www.bestpractices.dev/en/projects`.
2. Submit `thanhnt-sm/eco_support_net_oracle`.
3. Complete the passing criteria checklist:
   - Basics: Open source license (GPL-3.0), Git version control.
   - Change Control: Git commit history, release notes in CHANGELOG.
   - Reporting: SECURITY.md disclosure policy, issue tracker.
   - Quality: Automated test suite, CI running tests on all PRs.
   - Security: CodeQL SAST scanning, Dependabot automated updates.
4. Copy the generated project badge URL and embed into `README.md` and `README.vi.md`:
   ```markdown
   [![OpenSSF Best Practices](https://www.bestpractices.dev/projects/<ID>/badge)](https://www.bestpractices.dev/projects/<ID>)
   ```

### 5. Step 5 (VERIFY): Scorecard Dispatch & Audit Verification
Trigger a fresh run of the Scorecard workflow via GitHub CLI or Actions UI:
```bash
gh workflow run scorecard.yml --ref main
```
Observe Scorecard execution logs:
- `Branch-Protection`: Evaluated via Rulesets (10/10).
- `Signed-Releases`: Releases carry `.sigstore.json` and `.intoto.jsonl` (10/10).
- `Pinned-Dependencies`: Dockerfile locked mode verified (10/10).
- `CII-Best-Practices`: Badge detected (Passing / 5-10/10).
- **Projected Total Score**: **9.5 - 9.8 / 10**.

## Success Criteria
- [x] `scripts/check-workflow-policy.py` passes cleanly on all files.
- [x] Entire Python test suite passes (`python3 -m unittest discover -s scripts/tests`).
- [x] GitHub Repository Rulesets active on `main`.
- [x] OpenSSF Best Practices badge displayed in `README.md` and `README.vi.md`.
- [x] Scorecard REST API reflects updated score (7.8/10 baseline; all automations 10/10).
## Risk Assessment
- **Risk**: Rulesets require 1 PR approval, potentially blocking solo maintainer merges if no collaborator is available.
- **Mitigation**: In Rulesets, the repository administrator can configure "Bypass list" for emergencies, or use a designated bot/collaborator account for PR reviews.
