# Execution Ledger: GitHub Jobs Scans and Main Integration

**Repository:** `thanhnt-sm/eco_support_net_oracle`
**Execution Date:** 2026-10-13
**Base Commit on `main`:** `79d72d2acff0cf5314ad875c9accfbfff6bfed44`
**Plan Reference:** `plans/2026-10-13-github-jobs-scans-main-integration/plan.md`

---

## 1. Remote GitHub Actions Workflows & Jobs (Complete Paginated Inventory)

Pagination returned **981 workflow runs**. The collection did not assert the API's total_count; this is an observed inventory, not proof that all historical runs were available.

The following run conclusions were observed on `main` HEAD (`79d72d2`). Job counts and upload details in the Notes column originate from prior research, not fresh job-level inspection in this execution.

| Workflow | Event | Run ID | Head SHA | Status / Conclusion | Notes |
|---|---|---|---|---|---|
| **CI** | schedule | `38258474397` | `79d72d2` | completed / `success` | 10 jobs succeeded; 1 job skipped (`docker-smoke`, scheduled trigger filter). |
| **Scorecard** | schedule | `38230560166` | `79d72d2` | completed / `success` | Scorecard scan completed, SARIF uploaded. |
| **CI** | push | `38159541059` | `79d72d2` | completed / `success` | 11/11 jobs passed (`build-and-test`, `live-db-integration`, `vscode-extension`, etc.). |
| **Marketplace Extensions** | push | `38159541028` | `79d72d2` | completed / `success` | Packaged extensions verified. |
| **Scorecard** | push | `38159541126` | `79d72d2` | completed / `success` | Scored and uploaded. |
| **Standards audit** | push | `38159541109` | `79d72d2` | completed / `success` | Audit verified clean. |

### Historical Workflow Runs Summary (981 Total Runs)
- **CI**: 285 runs (latest on `main`: `38258474397` success).
- **Marketplace Extensions**: 233 runs (latest: `38159541028` success).
- **Standards audit**: 205 runs (latest: `38159541109` success).
- **Scorecard**: 147 runs (latest: `38230560166` success).
- **Build Installers**: 31 runs (latest: `38150937952` success).
- **Release**: 24 runs (latest: `37397905787` success).
- **Release provenance backfill**: 4 runs (latest: `37388913194` success).
- **Build Release Artifacts**: 3 runs.
- **Other historical workflow names**: 46 runs, including retired workflows and managed Dependabot runs.

### External Prerequisite Note: NuGet OIDC Publishing
- The release dry-run (run `37397905787`) passed, but `.github/workflows/release.yml:755` skips `Publish to NuGet.org` when `dry_run: true`.
- Live OIDC publishing to nuget.org requires repository owner configuration of the trusted publisher policy on nuget.org. It remains an explicit external prerequisite.

---

## 2. Security & Code Scanning Baseline (Full Paginated Inventory)

Code scanning alerts and main analyses were paginated. Dependabot returned an explicit feature blocker; secret scanning was checked separately with a successful empty response.

### A. Code Scanning Alerts (97 Total Paginated Alerts)
- **Open Alerts:** Exactly **1** open alert (Alert #19).
- **Fixed Alerts:** 84 alerts.
- **Dismissed Alerts:** 12 alerts.

#### Open Alert #19 Breakdown:
- **Rule ID:** `BranchProtectionID` (`Branch-Protection`)
- **Tool:** `Scorecard`
- **Category:** `supply-chain/branch-protection`
- **Created:** 2026-08-21 (updated 2026-10-02)
- **Finding Detail:** `score is 8: branch protection is not maximal on development and all release branches: Warn: 'branch protection settings apply to administrators' is disabled on branch 'main'; Warn: required approving review count is 1 on branch 'main'`.
- **Classification:** External GitHub repository settings constraint. Ruleset `24366111` ("Main Branch Protection") defines 1 required approval and includes an admin bypass. Modifying this requires an owner decision in GitHub repo settings (documented in `docs/security/scorecard-optimization-guide.md`), not a repository code change.

### B. Code Scanning Analyses (601 Total Paginated Analyses on `main`)
- **CodeQL (`csharp`):** Latest analysis on `79d72d2` (`2026-10-12T13:54:56Z`) completed with **0 findings** across 57 rules.
- **Scorecard:** Latest analysis on `79d72d2` (`2026-10-12T09:33:05Z`) returned 2 results (including Alert #19).

### C. Dependabot & Secret Scanning
- **Dependabot Alerts:** Disabled at repository level (HTTP 403 returned by `/dependabot/alerts`).
- **Secret Scanning Alerts:** Verified via isolated query (`gh api repos/thanhnt-sm/eco_support_net_oracle/secret-scanning/alerts`): returned `[]` (exit code 0, exactly 0 secret scanning alerts).

---

## 3. Pull Request Resolution

- **PR #51 (`docs/handoff-261006-pr40-pr45`):**
  - **Head SHA:** `90220325ab7ea8263b8797075eb94cc8b588b96d`
  - **Diff against `main`:** 0 changed files (`files: []`).
  - **Root Cause of Redundancy:** Changes were already integrated into `main` in PR #46 (`c9bfaf18886ef2211b41a16d44e767c13e80535f`).
  - **Action Taken:** Appended duplicate closure comment and executed `gh pr close 51`.
  - **Post-State:** `CLOSED`. Full paginated query confirms **0 open PRs** remain across the repository.

---

## 4. Remote Branch Audit & Pinned-Tree Cryptographic Verification

A paginated query identified exactly 5 remote branches. The comparisons below cover pinned historical commits and affected paths, not all residual changes against current main; they do not authorize branch deletion.

| Remote Branch | Head SHA | Merged in PR | Merge Commit on `main` | Tree / Diff Verification against Merge Commit | Disposition |
|---|---|---|---|---|---|
| `origin/ci/scorecard-signed-releases-provenance-backfill` | `89ef76d` | **PR #45** | `170e40def29c4a7fad818cc83b21d749c140d085` | 3 files modified relative to base `3656e0e`. `git diff 89ef76d 170e40d -- <files>`: **IDENTICAL (0 diff)**. | Merged; safe to delete upon owner authorization. |
| `origin/docs/handoff-261006-alert19-branch-compare` | `449bd65` | **PR #47** | `cbe47e96d780bf5993d29311c7c07f4bf62033bd` | Merge base is `cbe47e9`. 0 files modified relative to base. `git diff 449bd65 cbe47e9`: **IDENTICAL (0 diff)**. | Merged; safe to delete upon owner authorization. |
| `origin/docs/handoff-261006-pr40-pr45` | `9022032` | **PR #46** | `c9bfaf18886ef2211b41a16d44e767c13e80535f` | Tip `9022032` is a merge of `main` @ `446c0e4`. `git diff 9022032 446c0e4`: **IDENTICAL (0 diff)**. | Merged; safe to delete upon owner authorization. |
| `origin/docs/redteam-261004-source-vs-goals` | `6011c8a` | **PR #40** | `d0ec928522af4e63f8ba0df7c2706d0e393091f5` | 407 files modified relative to base `046f91d`. `git diff 6011c8a d0ec928 -- <files>`: **IDENTICAL (0 diff)**. | Merged; safe to delete upon owner authorization. |

No remote branch deletion is planned. Retained branches require refreshed current-main comparisons before any future deletion decision; the historical dispositions above are not deletion gates.

---

## 5. Local Verification Baseline & Verification Blockers

### A. Python Verification Suites
- `python3 -m unittest discover -s scripts/tests -v`: **101/101 tests passed**.
- `python3 scripts/check-workflow-policy.py`: **Passed** (VSIX assert, fork-PR upload guard, release VS tests, supply-chain signing, Dockerfile lock, publish-after-test).
- `python3 scripts/check-nuget-licences.py`: **Passed** (326 NuGet packages, 9 npm packages verified against `allowed-licences.txt`).
- `python3 scripts/check-license-consistency.py`: **Passed** (222 documentation files scanned, 6 copies identical).

### B. .NET Test Modules & Windows Environment Blockers
- **`DataGuard.Observability.Tests`**: 38/38 passed.
- **`DataGuard.Analyzers.Tests`**: 97/97 passed.
- **`DataGuard.GoldenCorpus.Tests`**: 61/61 passed.
- **`DataGuard.Core.Tests`**: 1770 passed, 1 failed.
  - *Failed Test:* `CliExitCodeTests.ManualAssemblyPathFromConfig_RequiresAllowAssemblyFromConfig`
  - *Root Cause:* Test generates temporary path on Windows containing backslashes (e.g. `C:\Users\thant\...`) formatted into a double-quoted YAML scalar without backslash escaping (`\U`, `\t` treated as invalid YAML escape sequence by YamlDotNet).
  - *Disposition:* Recorded as an environmental verifier defect. In accordance with Phase 1 Step 6 and the Immutable Test Invariant, test files are not modified without an explicit user decision.
- **`DataGuard.CodeFixes.Tests`**: 32 passed, 5 failed.
  - *Failed Tests:* `DataGuardCodeFixProvider_Dg001FixAll_SuppressesEveryCallSite`, `SkipContractCheckFixProvider_Dg001FromGenerator_EmitsCompilableReasonAttribute`, `NamingConventionFixProvider_Dg006_AddsExplicitColumnAttribute`, `AddMaxLengthAttributeFixProvider_Dg009_AddsVerifiedMaxLength`, `DataGuardCodeFixProvider_Dg001FromGenerator_AddsSkipContractCheckWithReason`.
  - *Observed Failure:* Expected LF (`\n`) versus generated CRLF (`\r\n`) in inserted attributes on Windows. Production formatter root cause has not yet been investigated.
  - *Disposition:* Platform line-ending discrepancy. Recorded for clean worktree handling.

### C. Workspace Preservation
- `global.json` modification remains preserved, unstaged, and untouched.
