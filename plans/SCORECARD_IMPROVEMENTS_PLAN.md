# Context
The project currently receives a score of `0/10` on several OpenSSF Scorecard metrics: `Code-Review`, `Maintained`, `CII-Best-Practices`, and `Contributors`. The goal is to address the root causes of these scores to maximize the Scorecard evaluation. Many of these require GitHub configuration or time rather than code changes.

# Approach

1. **Code-Review (Score: 0/10)**
   * **Finding:** Scorecard requires explicit human reviews on the last ~30 merged PRs. Trust-based workflows (self-merge, admin bypass) are insufficient and represent a severe security gap.
   * **Action (System-Enforced Branch Protection):** Configure the following strict rules for `main`:
     1. **Require a pull request before merging** with `Require approvals` set to `1`.
     2. **Require review from Code Owners** (enforces the `.github/CODEOWNERS` routing).
     3. **Dismiss stale pull request approvals when new commits are pushed** (prevents malicious post-approval commits).
     4. **Require branches to be up to date before merging** (prevents stale PRs from breaking `main`).
     5. **Do not allow bypassing the above settings** (locks out Admins/Repo Owners from self-merging).
     6. **Require status checks to pass before merging**: Set to exact job names `Build and Test` and `CodeQL Analysis`.
   * **Code change:** None. (Requires GitHub Settings configuration).

2. **Maintained (Score: 0/10)**
   * **Finding:** Scorecard automatically flags any project created within the last 90 days with a `0` score, regardless of activity volume. 
   * **Action (Operational):** Continue committing and resolving issues. The score will automatically normalize to 10/10 once the repository crosses the 90-day age mark and maintains a rolling 90-day activity window.
   * **Code change:** None. This is a strict time-bound metric.

3. **CII-Best-Practices (Score: 0/10)**
   * **Finding:** The project lacks participation in the OpenSSF Best Practices Badge Program. (The repo already has `SECURITY.md` and `CONTRIBUTING.md`, which helps answer the questionnaire).
   * **Action (External):** Register the repository at [bestpractices.dev](https://www.bestpractices.dev). Complete the initial questionnaire to reach the "In Progress" tier (bumps score to `2/10`), then fulfill the requirements to reach the "Passing" tier (`5/10`) and higher.
   * **Action (Codebase):** The `README.md` already contains the OpenSSF Best Practices badge wrapped in an HTML comment (`<!-- ... -->`). This is correctly staged.
   * **Target:** `README.md` (Update the `<YOUR_PROJECT_ID>` placeholder after registration).

4. **Contributors (Score: 0/10)**
   * **Finding:** Scorecard checks if commit authors in the last 30 commits belong to at least 3 distinct organizations based on the public `Company` metadata in their GitHub profiles. Each organization must have at least 5 commits.
   * **Action (Operational):** Ensure core contributors have the `Company` field populated in their public GitHub profiles. 
   * **Code change:** None. This is a demographic metric. *Note: For a solo developer or single-organization project, it is impossible to meet the 3-company/5-commit threshold. This score will remain 0/10 until multi-org collaboration occurs.*


5. **Automated Monitoring & Routing (Score: N/A - Operational Integrity)**
   * **Finding:** Scorecard metrics require continuous automated monitoring, and review workflows require structured routing.
   * **Action (CODEOWNERS):** Verify the existing `.github/CODEOWNERS` file accurately maps repository directories to designated reviewers, enforcing the required Code Owner review rule.
   * **Action (GitHub Actions):** Verify the existing `.github/workflows/scorecard.yml` workflow. It is already configured to run via a Cron job and push events. Ensure it successfully uploads SARIF reports to the GitHub Security tab.
# Critical files & anchors
* `README.md`: Locate `[![OpenSSF Scorecard]` in the header area. Append the CII-Best-Practices badge placeholder directly after it.

# Verification
* **Operational Checks:** Validate branch protection rules are active in GitHub Settings -> Branches.
* **Local UI Check:** Run the `scorecard` CLI tool locally (`scorecard --repo github.com/thanhnt-sm/eco_support_net_oracle`) to verify the CLI can detect the updated `README.md` badge.

# Assumptions & contingencies
* **Assumption:** You have admin access to the repository to configure Branch Protection and Actions.
* **Contingency (CII Badge):** The `README.md` update uses a placeholder (`<YOUR_PROJECT_ID>`) inside an HTML comment that must be replaced post-registration at bestpractices.dev.
* **Contingency (Solo-Dev Deadlock):** Because Admin-bypass is strictly prohibited, a solo developer is physically locked out of merging code. **Decision:** The project will formally recruit a trusted secondary human maintainer to perform cross-approvals. This preserves the System-Enforced security posture and satisfies the OpenSSF Code-Review human consensus metric.
* **Contingency (Contributors):** The requirement of 3 distinct companies with 5 commits each remains impossible for a single organization. This specific sub-score (0/10) is an accepted limitation.

## Red Team Review

### Session — 2026-10-03
**Findings:** 5 (5 accepted, 0 rejected)
**Severity breakdown:** 3 Critical, 2 High, 0 Medium

| # | Finding | Severity | Disposition | Applied To |
|---|---------|----------|-------------|------------|
| 1 | Missing Code Owner Approval Enforcement | Critical | Accept | Code-Review |
| 2 | Invalid CI job names for status checks | Critical | Accept | Code-Review |
| 3 | Bot approvals invalidate Scorecard metric | Critical | Accept | Contingencies |
| 4 | Ignorance of existing workflows & CODEOWNERS | High | Accept | Monitoring / README |
| 5 | Missing Up-To-Date branches requirement | High | Accept | Code-Review |

## Validation Log

### Session N - 2026-10-03
**Question:** Enforcing strict branch protection (blocking admins) will completely prevent you from merging your own PRs. Since OSSF Scorecard ignores bots, how will you handle the approval process to continue development?
**Decision:** Recruit Human Collaborator (I will invite a trusted secondary maintainer immediately to perform reviews).
**Impact:** The plan's contingency was updated to formally require recruiting a second maintainer, rejecting the use of bots or accepting a 0/10 Code-Review score.