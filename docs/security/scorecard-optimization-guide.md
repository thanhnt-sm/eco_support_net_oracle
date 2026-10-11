# OpenSSF Scorecard & Security Optimization Guide

> **Repository**: `thanhnt-sm/eco_support_net_oracle`  
> **Target**: Maximum OpenSSF Scorecard rating ([scorecard.dev](https://scorecard.dev/viewer/?uri=github.com/thanhnt-sm/eco_support_net_oracle))  
> **Current Baseline**: 7.9 / 10 at commit `3656e0e` (scorecard.dev scan 2026-10-05; weighted ceiling reachable from the repository alone: 8.1 after the provenance backfill below, the remaining checks need owner-side settings, time, or other contributors)  
> **Historical License Note**: Dual-licensed under GNU GPL v3.0-only and Commercial since release v0.3.0; earlier pre-v0.3.0 commits historically MIT (see `docs/legal/MIT-v0.1.0-v0.3.0.txt`).

---

## 1. Scorecard Breakdown & Optimization Status

| Check | Score | Status | Optimization & Path to Max |
|---|---|---|---|
| **Binary-Artifacts** | 10/10 | Met | No compiled binaries committed in git tree. |
| **Branch-Protection** | 8/10 | Action Needed (GitHub Settings) | `main` is protected by the ruleset **Main Branch Protection** (id 24366111), not a classic branch-protection rule. Two findings remain: the ruleset lists *Repository admin* as a bypass actor (`bypass_mode: always`, Scorecard: "settings apply to administrators is disabled") and `required_approving_review_count` is 1 (Scorecard wants 2). This finding is Code scanning alert #19 (first raised 2026-08-31 when `main` had no protection, closed while the check was skipped, reopened with the current text once the ruleset existed). See section 2. |
| **CI-Tests** | 10/10 | Met | All PRs executed via GitHub Actions CI suite (`ci.yml`). |
| **CII-Best-Practices** | 5/10 | In Progress (Self-Certification) | Passing tier certified (5/10). Advancing to Silver tier in `bestpractices.dev` elevates check to 10/10. Full questionnaire answers tracked in `docs/guides/openssf-best-practices-answers.md`. |
| **Code-Review** | 0/10 | External Requirement | Probe `codeApproved` over the last 20 changesets on `main`: a changeset counts when a GitHub review in state `APPROVED` (or the merge itself) comes from a login other than the PR author; bot-authored changesets (Dependabot) are skipped entirely so they neither help nor hurt. Every human changeset was authored *and* merged by `thanhnt-sm`, hence 0/20. Needs a second account that approves PRs before merge. |
| **Contributors** | 0/10 | Ecosystem Growth | Probe `contributorsFromOrgOrCompany`: counts distinct companies (profile *Company* field) and public GitHub organizations across contributors with at least 5 commits; 3 entities give 10/10, proportional below. The only human contributor has no company and no public organization on the profile, so the count is 0. |
| **Dangerous-Workflow** | 10/10 | Met | No unpinned script injections, untrusted checkout on `pull_request_target`. |
| **Dependency-Update-Tool** | 10/10 | Met | `.github/dependabot.yml` configured for NuGet, npm, and GitHub Actions. |
| **Fuzzing** | 10/10 | Met | Property-based fuzz tests in `tests/DataGuard.Core.Tests/SqlClassifierPropertyTests.cs`. |
| **License** | 10/10 | Met | FSF/OSI recognized GNU General Public License v3.0 (`LICENSE`). |
| **Maintained** | 0/10 | Temporal Gate | Repository created 2026-08-16; Scorecard returns 0 while it is younger than 90 days, i.e. until **2026-11-14**. After that it needs an average of 1 commit or issue update per week over the trailing 90 days (13 in total) for 10/10; the current cadence is far above that. |
| **Packaging** | 10/10 | Met | Automated packaging configured in `.github/workflows/ci.yml`. |
| **Pinned-Dependencies** | 10/10 | Met | All GitHub Actions pinned to full commit SHA; npm & NuGet dependencies locked. |
| **SAST** | 10/10 | Met | CodeQL workflow runs on `ci.yml` and `release.yml`; all recent commits evaluated. |
| **Security-Policy** | 10/10 | Met | Comprehensive `SECURITY.md` covering disclosure, timelines, and reporting. |
| **Signed-Releases** | 8/10 → 10/10 | Backfilled | Scorecard scores each of the last 5 releases: 8 when every asset is signed (`*.sigstore.json`), 10 when a provenance asset (`*.intoto.jsonl`) is also attached. `nightly` had both; v0.3.0–v0.3.2 only had signatures because `release.yml` attested the assets but only started uploading the bundle on 2026-10-05. The bundles were still in the repository attestation store, so `.github/workflows/release-provenance-backfill.yml` re-attached the genuine build-time bundles (see section 4). |
| **Token-Permissions** | 10/10 | Met | Top-level `contents: read` default; job-level least privilege throughout. |
| **Vulnerabilities** | 10/10 | Met | 0 OSV advisories; `@vscode/vsce` 4.0.0 in `src/DataGuard.VSCode` removed the transitive `braces` advisory `GHSA-vfj7-8cjw-p6xm`. |

---

## 2. GitHub Repository Settings Checklist

`main` is governed by the ruleset **Main Branch Protection** (Settings → Rules → Rulesets → Main Branch Protection, id 24366111). Scorecard already sees: deletion and force-push blocked, PR required, 1 approval, dismiss stale reviews, code-owner review, last-push approval, review-thread resolution, strict (up-to-date) required status checks `Build and Test`, `Standards audit`, `Security Scan`. Two settings keep the check at 8/10:

1. **Bypass list**: the ruleset grants *Repository admin* a bypass with mode *Always*. Scorecard reports this as "branch protection settings apply to administrators is disabled". Remove the bypass actor entirely: Scorecard's ruleset client treats *any* bypass actor, whatever its mode, as "not enforced for administrators".
2. **Required approvals**: raise *Required approvals* from 1 to **2** for the last tier of the check.

Both are owner decisions with a cost: with a single maintainer, 2 required approvals and no admin bypass make every merge wait for two other accounts. Scorecard scores the tiers in order and stops at the first incomplete one: item 2 alone lifts the check to 9/10, item 1 alone leaves it at 8/10, both give 10/10.

## 3. Human Collaboration & Community Strategy

- **Code-Review (0/10)**: Scorecard walks the last 20 changesets (PR merges) on `main`. A changeset is approved when an `APPROVED` review, or the merge, comes from a login other than the PR author; Dependabot changesets are skipped. All 20 current changesets were authored and merged by the same account. The fix is procedural, not in the repository: a second collaborator (the ruleset already requires 1 approval, which the admin bypass currently waives) approves each PR before merge. The score recovers gradually as reviewed merges push unreviewed ones out of the 20-changeset window.
- **Contributors (0/10)**: Scorecard needs contributors with at least 5 commits each whose GitHub profile lists a *Company* or who belong to a public GitHub organization; 3 distinct entities give 10/10, 1 gives 3/10. Setting the *Company* field on the maintainer's profile is the only lever available today; the rest needs outside contributors.

---

## 4. Signed-Releases: provenance backfill for v0.3.0–v0.3.2

**Root cause.** `release.yml` ran `actions/attest-build-provenance` for every tag since v0.3.0 (the attestation store holds one SLSA v1 bundle per release covering all 20 nupkg/vsix/zip subjects, signed from `refs/tags/<tag>` by `.github/workflows/release.yml`), but the step that copies the bundle to the release as `dataguard-<tag>.intoto.jsonl` only landed on 2026-10-05 (`0315a5b`). Scorecard looks for the asset, not the store, so the three published releases scored 8/10 each while `nightly` scored 10/10.

**Fix.** `.github/workflows/release-provenance-backfill.yml` (manual, input `tag`) downloads the release's attested assets, pulls their bundles from the attestation store with `gh attestation download`, de-duplicates them into one `.intoto.jsonl`, verifies every asset against it with `gh attestation verify --signer-workflow <repo>/.github/workflows/release.yml`, and uploads the file with `gh release upload --clobber`. It never mints a new attestation: an asset without build-time provenance is listed in the job summary and left alone, and a draft release is refused (drafts belong to `release.yml`). Run it once per affected tag; the next weekly scorecard.dev scan (Monday 02:30 UTC via `scorecard.yml`, or earlier on a push to `main`) picks the assets up.

**Keeping it at 10.** Every future tag gets the asset from `release.yml` → `publish-attestations`; `publish-release` refuses to publish a draft that has no `.intoto.jsonl`. Note the open blocker on that path: the 2026-10-05 dispatch for `v0.3.3` built, signed and attested everything but stopped at *Publish to NuGet.org* (nuget.org Trusted Publishing returned 401 "no matching trust policy owned by user", and the `NUGET_API_KEY` fallback 403 invalid/expired), so `v0.3.3` is still a draft. Until the NuGet credential is fixed on nuget.org, no new release is published, and the Signed-Releases window stays `nightly` + v0.3.0–v0.3.2.
