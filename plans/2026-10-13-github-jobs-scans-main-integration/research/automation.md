# GitHub Workflows, Automated Jobs & Security Scanning Audit Report

**Repository:** `thanhnt-sm/eco_support_net_oracle`
**Base Commit (`main`):** `79d72d2acff0cf5314ad875c9accfbfff6bfed44` (tree `fab17b74f04cfdb81dc2f6155efa2d803b3fa58c`)
**Audit Date:** 2026-10-13
**Target File:** `plans/2026-10-13-github-jobs-scans-main-integration/research/automation.md`
**Execution Posture & Capability Status:**
- **Research Posture:** Read-only inspection (no code, test, workflow, or remote mutations; `global.json` preserved untouched).
- **Security baseline correction:** Default first-page alert response contained 30 entries, not the entire inventory. Explicit paginated open-alert query returns #19 (`BranchProtectionID`, Scorecard). CodeQL analysis 1936690091 on SHA 79d72d2 has zero findings across 57 rules. Secret scanning open query returns zero. Dependabot alerts endpoint returns 403: disabled.

---

## 1. Executive Summary

A paginated, evidence-backed inspection of GitHub Actions workflows, workflow run histories, check runs, step annotations, and repository documentation was completed.

### Key Conclusions:
1. **Current Pipeline Health on `main` HEAD (`79d72d2`):**
   - **Push Run 38159541059:** All 11 CI jobs completed with conclusion `success`.
   - **Scheduled Run 38258474397:** 10 jobs completed with conclusion `success` (`build-and-test`, `live-db-integration`, `vscode-extension`, `scripts-tests`, `visual-studio-build-and-test`, `visual-studio-vsix-package`, `security-scan`, `generate-sbom`, `benchmark`, `codeql`), and 1 job was **skipped** (`docker-smoke`, because the scheduled cron trigger does not satisfy the workflow `if` condition requiring push to main, PR, or dispatch).
   - Across all 30 check runs on `79d72d2`, all executed checks have terminal conclusion `success`; none failed.
2. **Paginated Historical Failure Analysis:**
   - Multiple pages of failed runs (`/actions/runs?status=failure&per_page=30`, pages 1 and 2, spanning 60 historical failures back to Sept 28, 2026) were inspected.
   - Recent historical failures on October 5–11, 2026:
     - **CI Run 284 (38099379185, commit `600a32a`):** Job `Live DB Integration` step 7 exited with failure. Subsequent push run 288 and scheduled run 289 on `79d72d2` passed the same step.
     - **CI Run 282 (37560442889, branch `dependabot/.../npm-eac8022a8b` @ `11be89b`):** Job `Build and Test` step 8 failed because `minimatch 10` carried `BlueOak-1.0.0` (unlisted in `scripts/allowed-licences.txt`). Resolved by commit `600a32a` (PR #50), and PR #49 (`446c0e4`) was merged.
     - **Release Run 24 (37299220786, commit `3656e0e`):** Job `Publish to NuGet.org` failed at OIDC token exchange (HTTP 401: no matching trust policy configured on nuget.org). Subsequent release run 37397905787 with `dry_run: true` succeeded across compile, packaging, and signing steps, but `.github/workflows/release.yml:755` (`if: ${{ github.event_name == 'push' || inputs.dry_run == false }}`) **skips** `Publish to NuGet.org` on `dry_run: true`. Therefore, actual NuGet OIDC publishing remains an **unresolved external prerequisite** requiring nuget.org trust-policy or secret configuration by the owner.
     - **Red-Team Branch Runs 258/259 (commits `7809843`/`03917e5`):** Failed on transitive `System.Private.Uri 4.3.0` vulnerability (NU1902/NU1903). Fixed in `03917e5` by explicit pinning to 4.3.2, squashed in PR #40 (`d0ec928`).
3. **Security Posture & Code Scanning:**
   - Authenticated paginated `code-scanning/alerts?state=open&per_page=100` returns one open alert: #19, BranchProtectionID. Previous zero-open claim is retracted.
   - CodeQL (`language:csharp`) and Scorecard SARIF uploads pass cleanly in check runs.
   - OpenSSF Scorecard Alert #19 (`Branch-Protection` 8/10) is documented in `plans/ACTIVE_SESSION_REGISTER.md` and `docs/security/scorecard-optimization-guide.md`. It reflects the single-maintainer project trade-off: `Main Branch Protection` (`id: 24366111`) maintains an admin bypass actor and 1 required approving review to prevent self-merge deadlocks.
4. **Executed CI versus unverified publishing paths:**
   - Current executed CI success justifies no speculative CI rewrite; it does not prove publish-only paths safe. Review confirmed publish-visualstudio lacks the checksum/provenance gate present in publish-vscode (marketplace.yml:360-378 versus 391-420).
   - Live NuGet.org publishing cannot be claimed resolved from dry-run passes and remains an open external configuration prerequisite.
   - Authenticated ruleset 24366111 confirms RepositoryRole actor 5 has always bypass, one approval, code-owner review, last-push approval and strict required checks. Removing bypass requires a genuine independent reviewer path; do not relax review requirements to compensate. Alert #19 remains unresolved pending owner governance decision and subsequent Scorecard evidence.
   - Authenticated failed log 37299220786 also proves fallback API-key push failed HTTP 403 (invalid/expired or missing package permission). Check policy-creator username and fallback key package scopes separately; no dry-run proves either fixed.

---

## 2. Inventory of Known Workflows

The repository contains exactly 8 committed workflow definition files in `.github/workflows/` and 1 GitHub-managed dynamic workflow:

| Workflow Name | File Path | Workflow ID | Triggers | Permissions Declared | Primary Jobs & Invariants |
|---|---|---|---|---|---|
| **CI** | `.github/workflows/ci.yml` | `335739117` | `push: [main, develop]`, `pull_request: [main, develop]`, `workflow_dispatch`, `schedule: [0 6 * * 1]` | `contents: read` (top-level); individual jobs declare required permissions | `build-and-test`, `live-db-integration`, `vscode-extension`, `scripts-tests`, `visual-studio-build-and-test`, `visual-studio-vsix-package`, `security-scan`, `generate-sbom`, `benchmark`, `docker-smoke`, `codeql` |
| **Release** | `.github/workflows/release.yml` | `337693226` | `push: tags: ['v*']`, `workflow_dispatch` (inputs: `dry_run`, `version_tag`) | `contents: write`, `id-token: write`, `packages: write`, `attestations: write` | Tag/dry-run release pipeline: builds all artifacts, signs with cosign, publishes to NuGet/GHCR/GitHub Releases, publishes VS Code/VS extensions (with dry-run guards) |
| **Build Release Artifacts** | `.github/workflows/build_release.yml` | `351403747` | workflow_dispatch (version/include_docker) | contents: read | Packaging only; does not attest or push container |
| **Build Installers** | `.github/workflows/installers.yml` | `370728828` | main push with docs/plans path exclusions; dispatch | contents: read floor; publish elevates | CLI RID zips, VSIXs, rolling nightly; no versioned tags or MSI/PKG |
| **Marketplace Extensions** | `.github/workflows/marketplace.yml` | `338581363` | main push/PR; dispatch | contents: read floor; jobs elevate | Tests, VSIX packaging/attestation; manual publishers require separate verification review |
| **Scorecard** | `.github/workflows/scorecard.yml` | `339091434` | branch_protection_rule; Monday 02:30 UTC; main push; dispatch | read-all floor; analysis elevates security-events/id-token | Scorecard analysis and SARIF |
| **Standards audit** | `.github/workflows/standards-audit.yml` | `339091435` | main push; pull_request; dispatch | read-all | Community/security artifacts, pinned action SHAs and permissions declarations |
| **Release provenance backfill** | `.github/workflows/release-provenance-backfill.yml` | `375896273` | `workflow_dispatch` (input: `tag`) | `contents: write`, `attestations: write`, `id-token: write` | Attaches SLSA build-provenance bundle to an already-published historical GitHub release |
| **Dependabot Updates** | Dynamic (GitHub managed) | `338162636` | Configured via `.github/dependabot.yml` | Inherited GitHub App permissions | Scans ecosystem package managers (`nuget`, `npm`, `github-actions`, `docker`) |
