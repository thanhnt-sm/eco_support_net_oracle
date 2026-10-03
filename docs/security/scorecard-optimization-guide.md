# OpenSSF Scorecard & Security Optimization Guide

> **Repository**: `thanhnt-sm/eco_support_net_oracle`  
> **Target**: Maximum OpenSSF Scorecard rating ([scorecard.dev](https://scorecard.dev/viewer/?uri=github.com/thanhnt-sm/eco_support_net_oracle))  
> **Current Baseline**: 7.8 / 10 (Target: $\ge 9.0$ automated max, path to 10.0)  
> **Historical License Note**: Dual-licensed under GNU GPL v3.0-only and Commercial since release v0.3.0; earlier pre-v0.3.0 commits historically MIT (see `docs/legal/MIT-v0.1.0-v0.3.0.txt`).

---

## 1. Scorecard Breakdown & Optimization Status

| Check | Score | Status | Optimization & Path to Max |
|---|---|---|---|
| **Binary-Artifacts** | 10/10 | Met | No compiled binaries committed in git tree. |
| **Branch-Protection** | 8/10 | Action Needed (GitHub Settings) | Requires enabling "Do not allow bypassing the above settings" (enforce on admins) and requiring 2 reviewers in GitHub repository branch protection rules for `main`. |
| **CI-Tests** | 10/10 | Met | All PRs executed via GitHub Actions CI suite (`ci.yml`). |
| **CII-Best-Practices** | 5/10 | In Progress (Self-Certification) | Passing tier certified (5/10). Advancing to Silver tier in `bestpractices.dev` elevates check to 10/10. Full questionnaire answers tracked in `docs/guides/openssf-best-practices-answers.md`. |
| **Code-Review** | 0/10 | External Requirement | OpenSSF Scorecard requires changesets merged to `main` via PRs reviewed and approved by human contributors (bot approvals are explicitly ignored). Requires second human collaborator. |
| **Contributors** | 0/10 | Ecosystem Growth | Requires contributors from 2 or more distinct companies or GitHub organizations over the preceding 30 days. |
| **Dangerous-Workflow** | 10/10 | Met | No unpinned script injections, untrusted checkout on `pull_request_target`. |
| **Dependency-Update-Tool** | 10/10 | Met | `.github/dependabot.yml` configured for NuGet, npm, and GitHub Actions. |
| **Fuzzing** | 10/10 | Met | Property-based fuzz tests in `tests/DataGuard.Core.Tests/SqlClassifierPropertyTests.cs`. |
| **License** | 10/10 | Met | FSF/OSI recognized GNU General Public License v3.0 (`LICENSE`). |
| **Maintained** | 0/10 | Temporal Gate | OpenSSF assigns 0 to repositories younger than 90 days. Flips to 10 automatically once age exceeds 90 days with continuous commits. |
| **Packaging** | 10/10 | Met | Automated packaging configured in `.github/workflows/ci.yml`. |
| **Pinned-Dependencies** | 10/10 | Met | All GitHub Actions pinned to full commit SHA; npm & NuGet dependencies locked. |
| **SAST** | 9/10 | Met / Refreshing | CodeQL workflow runs on `ci.yml` and `release.yml`. All 14 recent commits evaluated. |
| **Security-Policy** | 10/10 | Met | Comprehensive `SECURITY.md` covering disclosure, timelines, and reporting. |
| **Signed-Releases** | 8/10 | Met (Rolling Nightly + New Tags) | Sigstore cosign keyless signing and in-toto SLSA provenance generation active in `.github/workflows/installers.yml` and `.github/workflows/release.yml`. Older historic releases v0.3.0–v0.3.2 lack retrofitted provenance. |
| **Token-Permissions** | 10/10 | Met | Top-level `contents: read` default; job-level least privilege throughout. |
| **Vulnerabilities** | 9/10 $\rightarrow$ 10/10 | Resolved | Upgraded `@vscode/vsce` to 4.0.0 in `src/DataGuard.VSCode`, eliminating transitively nested `braces` v3.0.3 DoS advisory `GHSA-vfj7-8cjw-p6xm`. |

---

## 2. GitHub Repository Settings Checklist

To reach maximal branch protection score (10/10 on `Branch-Protection`):

1. Navigate to **GitHub Repository $\rightarrow$ Settings $\rightarrow$ Branches $\rightarrow$ Branch protection rules $\rightarrow$ `main`**.
2. Check **"Require a pull request before merging"**:
   - Require approvals: set to **2** (or minimum 1).
   - Check **"Dismiss stale pull request approvals when new commits are pushed"**.
   - Check **"Require review from Code Owners"**.
   - Check **"Require approval of the most recent push"**.
3. Check **"Require status checks to pass before merging"**:
   - Check **"Require branches to be up to date before merging"**.
   - Select required checks: `Build and Test`, `CodeQL Analysis`.
4. Check **"Do not allow bypassing the above settings"** (Enforce branch protection settings on administrators).
5. Uncheck **"Allow force pushes"** and **"Allow deletions"**.

---

## 3. Human Collaboration & Community Strategy

- **Code-Review (0/10)**: OpenSSF Scorecard evaluates merged pull requests from the last 30 commits. Merges made directly to `main` without PR or PRs merged without another human's formal review approval score 0. Solo developers must pair with at least one external collaborator who reviews and clicks **Approve** on PRs before merge.
- **Contributors (0/10)**: Welcoming contributions from developers with distinct organization affiliations (or employer affiliations listed on GitHub profile) fulfills this criterion.
