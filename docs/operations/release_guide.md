[Tiếng Việt](release_guide.vi.md) | [English](release_guide.md)

# Secure Release Guide

`tools/git-tools/dg-release` is the single explicit release entry point. It creates one immutable annotated Git tag, waits for the GitHub **Release** workflow to finish, then optionally dispatches the protected Marketplace publication workflow.

It never accepts a secret on the command line, never reads a secret file, and never prints credentials.

## 1. One-time GitHub configuration

### Create a protected Marketplace environment

In **Settings → Environments**, create `marketplace-production`.

- Require a maintainer reviewer.
- Restrict deployment to protected tags matching `v*` if the repository plan supports it.
- Do not add a bypass user unless there is a documented incident process.

The Marketplace workflow already targets this environment for both publishing jobs. Packaging runs may complete, but publication pauses for approval.

### Add Actions repository secrets

In **Settings → Secrets and variables → Actions**, add these repository secrets. Never put real values in `.env`, `.dg-git.yml`, source files, issue comments, or shell history.

| Secret | Required by | Value and least-privilege guidance |
|---|---|---|
| `NUGET_USER` | NuGet Trusted Publishing | NuGet.org profile name. Preferred authentication path; no API key is used when the trust policy is configured. |
| `NUGET_API_KEY` | NuGet fallback only | NuGet.org scoped key restricted to DataGuard package IDs and push permission. Set a short expiration; remove it after Trusted Publishing is working. |
| `VSCE_PAT` | VS Code Marketplace | Azure DevOps Marketplace PAT with only **Marketplace Manage** for the DataGuard publisher. |
| `VS_MARKETPLACE_PAT` | Visual Studio Marketplace | Visual Studio Marketplace publisher PAT with publish access only. |

Configure NuGet Trusted Publishing for this repository and `.github/workflows/release.yml` before making the first production release. The workflow falls back to `NUGET_API_KEY` only when OIDC login cannot provide a key.

### Create the local dispatch token

Create a fine-grained personal access token under **GitHub → Settings → Developer settings → Personal access tokens → Fine-grained tokens**.

- Resource owner: the repository owner.
- Repository access: **Only select repositories** → this repository only.
- Repository permissions: **Actions: Read and write**, **Contents: Read**.
- Expiration: 30 days or the shortest practical value.
- Do not grant Administration, Secrets, Workflows outside this repository, Packages, or organization-wide access.

The token only starts/observes GitHub workflows. NuGet and Marketplace credentials remain GitHub Actions secrets and never exist on the workstation.

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
   PUBLISH_MARKETPLACES=true
   DG_RELEASE_GITHUB_TOKEN=github_pat_...
   DRY_RUN=false
   CONFIRM_RELEASE=true
   ```
   *Security note*: `.release.env` is strictly ignored by `.gitignore`. It is never committed to GitHub.

3. Run 1 command to execute the entire release:
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
- Current branch is `main` and matches `origin/main`.
- Tag follows SemVer with `v` prefix.
- Tag does not already exist locally or on GitHub.
- JSON parser (`jq` or `python`) is available.
## 4. Publish to every supported platform
After the dry run passes and the protected environment reviewers are available:

```bash
bash tools/git-tools/dg-release \
  --tag v1.2.3 \
  --publish-marketplaces \
  --yes
```

The command performs this ordered sequence:

1. Creates and pushes immutable annotated tag `v1.2.3`.
2. Waits for `release.yml` to build, test, scan, package, sign, generate SBOMs, publish NuGet packages, create the GitHub Release, attest packages, and push GHCR images.
3. Only when that workflow succeeds, dispatches `marketplace.yml` with `publish=true` for the same immutable tag.
4. Waits until Marketplace packaging/publishing completes. The `marketplace-production` environment requires its configured approval.

Use `--timeout-seconds 10800` for an intentionally longer three-hour wait. The default is two hours.

## 5. Safer variants
Release NuGet/GitHub/GHCR only; do not publish extensions:

```bash
bash tools/git-tools/dg-release --tag v1.2.3 --yes
```

This still triggers the Release workflow, but does not dispatch Marketplace publication.

Dry-run never creates a tag, dispatches a workflow, publishes a package, creates a release, or pushes an image.

## 6. Failure handling
- **Preflight failure:** fix the reported local state. No remote state changed.
- **Release workflow failure after tag push:** do not reuse, delete, or force-move the tag. Fix the issue, increment the SemVer version, and release a new immutable tag.
- **Marketplace failure:** the core release is already published. Fix the Marketplace-specific issue, then manually re-run the same `Marketplace Extensions` workflow for the same tag and `publish=true`; do not create a new package version solely for a VSIX retry.
- **Credential exposure:** immediately revoke the exposed credential at its issuer, rotate the matching GitHub Actions secret, inspect GitHub audit logs, and scan the repository history before retrying.

## 6. What the tool cannot and must not automate locally

`dg-release` does not receive NuGet, VS Code Marketplace, Visual Studio Marketplace, or GitHub Release write credentials. Those actions execute only on GitHub-hosted runners with scoped secrets, OIDC, build attestations, and protected environments.
