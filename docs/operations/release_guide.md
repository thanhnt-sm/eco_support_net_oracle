[Tiếng Việt](release_guide.vi.md) | [English](release_guide.md)

> Marketplace publication is temporarily paused (2026-10-15). Use `--no-marketplaces`;
> `--publish-marketplaces` / `PUBLISH_MARKETPLACES=true` fail before release side effects.
> See [legacy pause and separate manual route](../marketplace-publishing.md).
> Existing settings are preserved; this guide does not authorize credential/protection changes.

# Secure Release Guide

`tools/git-tools/dg-release` is the tag-release helper. It creates/pushes an annotated Git tag and waits for the GitHub **Release** workflow only when `DG_RELEASE_GITHUB_TOKEN` is supplied. It does not dispatch Marketplace publication. A tag push without monitoring is not evidence that release jobs succeeded.

The helper accepts `DG_RELEASE_GITHUB_TOKEN` from the environment or, for compatibility,
from `.release.env` when the environment value is absent. It reads that configuration file;
keep credentials out of the example below and prefer an approved session-only mechanism.
Browser Marketplace upload needs no PAT or GitHub dispatch token. See the
[browser route and acceptance gates](../marketplace-publishing.md).

## 1. Existing configuration and authentication

This guide does not authorize creating credentials or changing repository protections. Inspect the selected workflow and owner-approved setup before a real release.

- `release.yml` publishes through the existing `release` environment. Its name does not prove any reviewer/branch protection exists.
- The legacy `marketplace.yml` has no publisher jobs. The separate [manual Marketplace workflow](../marketplace-automation.md) also references `release`; do not create `marketplace-production` to follow this guide.
- NuGet uses `NUGET_USER` for Trusted Publishing when configured, with `NUGET_API_KEY` as fallback. If neither exists, the workflow warns and skips NuGet push: a green run is not proof of NuGet availability.
- `VSCE_PAT`/`VS_MARKETPLACE_PAT` belong to the separate opt-in Marketplace route, not `dg-release`. Browser upload requires no PAT. Follow that route's current authentication and acceptance gates; do not provision broad secrets merely to unblock a run.

### Create the local dispatch token

If API monitoring is required and the owner authorizes a dedicated token, create a fine-grained personal access token under **GitHub → Settings → Developer settings → Personal access tokens → Fine-grained tokens**.

- Resource owner: the repository owner.
- Repository access: **Only select repositories** → this repository only.
- Repository permissions: **Actions: Read and write**, **Contents: Read**.
- Expiration: 30 days or the shortest practical value.
- Do not grant Administration, Secrets, Workflows outside this repository, Packages, or organization-wide access.

In the currently paused helper path, this token observes GitHub workflows; Git tag push uses the existing Git transport credentials. No separate Marketplace dispatch is enabled. The workflow references NuGet/Marketplace credentials by secret name; this does not prove they are configured or prevent a user from storing copies elsewhere. Do not copy them to the workstation for browser upload.

Store the token in the current shell session only:

```bash
read -rsp 'GitHub release dispatcher token: ' DG_RELEASE_GITHUB_TOKEN; echo
export DG_RELEASE_GITHUB_TOKEN
```

PowerShell:

```powershell
$env:DG_RELEASE_GITHUB_TOKEN = Read-Host 'GitHub release dispatcher token' -AsSecureString |
  ConvertFrom-SecureString -AsPlainText
```

For PowerShell, prefer a password manager or Windows Credential Manager integration instead of persisting the token. Clear it after the release:

```bash
unset DG_RELEASE_GITHUB_TOKEN
```

```powershell
Remove-Item Env:DG_RELEASE_GITHUB_TOKEN
```

## 2. Configuration file workflow (no CLI options needed)

To release without remembering CLI options:

1. Copy the configuration template:
   ```bash
   cp .release.env.example .release.env
   ```
2. Open `.release.env` and configure the values:
   ```env
   RELEASE_TAG=v1.0.0
   PUBLISH_MARKETPLACES=false
   DRY_RUN=true
   CONFIRM_RELEASE=false
   ```
   *Security note*: `.release.env` is ignored by `.gitignore`; that is not encryption or a guarantee against accidental disclosure. Do not put token values in this example.

3. Run the helper to review this dry-run configuration:
   ```bash
   bash tools/git-tools/dg-release
   ```
   On Windows CMD / PowerShell:
   ```cmd
   tools\git-tools\dg-release.cmd
   ```
   Or using the Git suite:
   ```bash
   dg-git release
   ```

## 3. Preflight & dry-run verification

Before a real release, you can set `DRY_RUN=true` in `.release.env` or pass `--dry-run`:
```bash
bash tools/git-tools/dg-release --dry-run
```
The preflight verifies:
- Working tree is completely clean (uncommitted changes are blocked).
- Current branch matches `origin/HEAD` (fallback `main`) and the corresponding remote-tracking commit after fetch.
- Tag follows SemVer with `v` prefix.
- Tag does not already exist locally or on GitHub.
- JSON parser (`jq` or `python`) is available.
## 4. Start an authorized tag release
After the dry run passes and the owner has verified actual environment protections and required reviewers:

```bash
bash tools/git-tools/dg-release \
  --tag v1.2.3 \
  --no-marketplaces \
  --yes
```

The command performs this ordered sequence:

1. Creates and pushes immutable annotated tag `v1.2.3`.
2. With the monitoring token, waits for `release.yml`; otherwise returns after pushing the tag. The workflow contains build/test/sign/SBOM, GitHub Release, NuGet and GHCR jobs, subject to their gates and available credentials.
3. Marketplace publication is paused; no Marketplace dispatch occurs.

Use `--timeout-seconds 10800` for an intentionally longer three-hour wait. The default is two hours.

## 5. Safer variants
Release NuGet/GitHub/GHCR only; do not publish extensions:

```bash
bash tools/git-tools/dg-release --tag v1.2.3 --yes
```

This still triggers the Release workflow, but does not dispatch Marketplace publication.

## 6. Manual artifact handoff (no publish)

Use `.github/workflows/build_release.yml` when a versioned installer bundle is needed without creating a GitHub Release or publishing to NuGet, VS Code Marketplace, Visual Studio Marketplace, or GHCR.

Run it from the GitHub Actions UI, or with:

```bash
gh workflow run build_release.yml -f version=1.2.3
gh workflow run build_release.yml -f version=1.2.3 -f include_docker=true
```

The version must be SemVer without a `v` prefix. The workflow builds from the revision where it is dispatched; it does not infer a tag or branch. Download the versioned artifacts, including `dataguard-installers-1.2.3`, within the 30-day retention period. The aggregate contains portable CLI archives, both VSIX packages, NuGet packages, sidecar checksums, `SHA256SUMS.txt`, and `release-manifest.json`. Verify checksums before installation. The optional Docker job produces an OCI archive only; it does not log in to or push a registry.

This is separate from the production tag release path above: `dg-release` remains the explicit entry point for publishing releases.

The helper `--dry-run` does not create/push the new tag or dispatch/publish. Its preflight still fetches remote refs/tags (with prune/force) and checks remote tag existence; it is not offline or free of local ref updates. This differs from `release.yml` hosted dry-run, which builds/signs and uploads artifacts. Do not bypass fetch checks to claim release readiness.

## 6. Failure handling
- **Preflight failure:** fix the reported local state. No remote state changed.
- **Release workflow failure after tag push:** do not reuse, delete, or force-move the tag. Fix the issue, increment the SemVer version, and release a new immutable tag.
- **Legacy Marketplace paused:** the helper rejects Marketplace enablement. Use the separate manual runbook only with its explicit publication authorization and accepted artifacts.
- **Credential exposure:** immediately revoke the exposed credential at its issuer, rotate the matching GitHub Actions secret, inspect GitHub audit logs, and scan the repository history before retrying.

## 6. What the tool cannot and must not automate locally

`dg-release` does not receive NuGet, VS Code Marketplace, Visual Studio Marketplace, or GitHub Release write credentials. Those release actions are configured for GitHub-hosted runners with secret/OIDC references and attestations. An environment name in YAML does not prove configured protection; verify actual reviewers and branch restrictions before relying on approval gates.

## Rolling nightly versus Marketplace

`installers.yml` can publish a rolling GitHub nightly on qualifying main pushes and defaults
to publication on manual dispatch. It deletes/recreates the nightly release and tag; it does
not publish to Marketplace. Use `publish_prerelease=false` for artifact-only dispatch.
See [triggers, path exclusions and immutable-artifact requirements](../marketplace-publishing.md#rolling-nightly-is-a-different-publication-channel).

## Evidence required after a release

The helper reports each channel using the selected Release push run's exact tag/SHA and run attempt. `succeeded` requires a completed successful job and its publication step; `skipped` means an explicitly skipped job/step (or the Marketplace pause); `failed` means failure/cancellation/timeout; `unverified` means missing, ambiguous, incomplete or unavailable evidence. NuGet's green job with a skipped push is reported as `skipped`. GitHub Release uses the final publication step, not draft creation. Provenance refers to the attestation generation step, not independent verification of the release bundle.

Exit code `0` preserves trigger-only compatibility: without a token it confirms tag submission only. A completed successful workflow may still contain `skipped` or `unverified` channels. Git/API/JSON errors, monitoring timeout, unsuccessful workflow conclusion or a failed channel return nonzero (`1` for monitoring/reporting errors). `dg-git release` forwards this code; the Windows launcher still delegates to Bash. No all-platform publication success banner is emitted.

Jobs are read from the selected run attempt with a 100-job page. If the response is incomplete (including more jobs than returned), channels remain `unverified`; renamed or duplicate required jobs/steps also cannot prove success. A monitoring timeout leaves channels `unverified`, because failure to observe completion is not proof of a failed publication. Inspect workflow URLs, artifact signatures and each registry/listing separately. Synthetic tests do not exercise authenticated tag push, hosted signing, registry availability or editor acceptance. Marketplace publication remains paused.
