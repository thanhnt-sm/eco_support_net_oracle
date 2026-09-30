# Build Installers workflow (`installers.yml`) — implementation report

Date: 2026-09-29 · Branch: `feat/installers-workflow` (from `origin/main` @ `4bea2f3`, PR #24 merged) · Commit: `5a9439b` (single commit, amended once after advisor review; not pushed)
Report path follows the lead's request (`-2144-`); the hook-computed name was `fullstack-developer-260929-2146-{slug}.md`. This report file is **untracked** on the branch (written after the commit) — commit or discard it as you see fit.

## Design

`.github/workflows/installers.yml`, name **Build Installers**.

| Item | Decision |
| --- | --- |
| Triggers | `push` to `main` (paths-ignore `**/*.md`, `docs/**`, `plans/**`); `workflow_dispatch` with `version` (string, default empty) and `publish_prerelease` (boolean, default true) |
| Version | `resolve-version` job (ubuntu): input if given, else `<ExtensionVersion.Fallback>-nightly.<yyyyMMdd>.<run_number>` (Fallback read from `ExtensionVersion.cs` with ci.yml's regex). Validated at the boundary: SemVer without `v`, no whitespace/`/`, **no `+build` metadata** (GitHub rewrites `+` in asset names, which would break the `.sha256` files). Outputs `version`, `tag` (hardcoded `nightly`), `plain_semver`, `short_sha` |
| `cli` (ubuntu, matrix win-x64/linux-x64/osx-arm64, fail-fast false) | release.yml `cli-package` recipe: `fetch-depth: 0`, framework-dependent `dotnet publish` with `-p:PublishBundledCli=true -p:NuGetLockFilePath=obj/cli-publish-<rid>.packages.lock.json -p:RestoreLockedMode=false`, **plus `-p:MinVerVersionOverride=<v>`** (see dry-run finding below), zip `dataguard-<v>-<rid>.zip` + `.sha256` |
| `vscode` (ubuntu) | setup-node 24 + npm cache, **setup-dotnet** (needed by `prepare-lsp`), `npm ci`, `npm test`, `npm pkg set version=<v>`, `npm run package -- --out …/dataguard-vscode-<v>.vsix` (+ `.sha256`). `--out` is appended to the script string so `prepare-lsp` still runs first |
| `visualstudio` (windows) | restore tests `--locked-mode` → `dotnet test tests/DataGuard.VisualStudio.Tests/... --configuration Release --no-restore` → manifest + `ExtensionVersion.Fallback` patched **only if `plain_semver == 'true'`** (VSIX manifests reject prerelease strings) → expected version re-read from `ExtensionVersion.cs` → vswhere → **exact ci.yml MSBuild line** (`/restore /t:Build /p:Configuration=Release /p:CreateVsixContainer=true /p:DeployExtension=false /m /nologo /v:minimal`) → `& ./scripts/assert-vsix.ps1 -VsixPath … -ExpectedVersion …` → staged as `DataGuard.VisualStudio-<v>.vsix` + `.sha256` (lowercase hash, two spaces, exact file name, no trailing newline → `sha256sum -c` compatible) |
| Uploads | `actions/upload-artifact@043fb46d…` (v7.0.1, same pin), `if-no-files-found: error`, `retention-days: 30` |
| `publish` (ubuntu, `needs` all, `permissions: contents: write`, `if: github.event_name == 'push' \|\| inputs.publish_prerelease`) | download `dataguard-*` merged → **Verify assets** (exactly 5 payloads, `sha256sum -c` on every `.sha256`) → release notes (`body.md`: file table, verify commands, install steps, manifest-version caveat, ref/sha/run link) → `gh release delete nightly --yes --cleanup-tag` if present, delete a dangling `nightly` tag via API if present, `gh release create nightly --prerelease --target <sha> --title "Nightly build <v> (<short sha>)" --notes-file body.md <assets>` → verify the tag now points at `github.sha`. Uses `gh` CLI with `GH_TOKEN: ${{ github.token }}` (no workflow in the repo uses softprops). `v*` tags and release.yml untouched |
| Security | top-level `permissions: contents: read`; only `publish` elevates. All actions pinned by the same SHAs as ci.yml/release.yml (checkout v7.0.1, setup-dotnet v6.0.0, setup-node v7.0.0, cache v6.1.0, upload-artifact v7.0.1, download-artifact v8.0.1). Inputs enter `run` via `env:` only |
| Concurrency | `installers-${{ github.ref }}`, `cancel-in-progress: true` |

## Files (commit `5a9439b`)

- `.github/workflows/installers.yml` — new (473 lines)
- `scripts/check-workflow-policy.py` — `WORKFLOWS` now lists `installers.yml`; docstring updated (rule (a) VSIX-assert now enforced on it; (b) has no PR trigger; (c) stays release.yml-scoped)
- `scripts/tests/test_check_workflow_policy.py` — 2 cases pin the installers shape (`push, workflow_dispatch` + unguarded upload passes; MSBuild without assert fails (a)) → 16 tests
- `docs/USAGE.md` — "Nightly installers / Bản build nightly" subsection under CLI install (bilingual: where to download, sha256 verification, VS Code `--force`, VS uninstall-first caveat)
- `CHANGELOG.md` — `[Unreleased]` → `### Added` line

Not touched: `packages.lock.json` (9 pre-existing local edits left as found), `.claude/`, `release.yml`, `build_release.yml`.

## Validation (verbatim last lines)

```
$ python -c "import yaml,sys; yaml.safe_load(open('.github/workflows/installers.yml'))"
yaml ok
$ actionlint .github/workflows/installers.yml
actionlint ok            (exit 0, no findings; actionlint 1.7.12)
$ python scripts/check-workflow-policy.py
check-workflow-policy: OK (VSIX assert, fork-PR upload guard, release VS tests)
$ python -m unittest discover -s scripts/tests
Ran 16 tests in 0.031s

OK
```

## Local dry-runs (scratchpad `…/installers-dryrun`)

1. **CLI linux-x64** — `dotnet publish … -p:PublishBundledCli=true -p:MinVerVersionOverride=0.2.3-nightly.20260929.1 -p:Version=… -p:NuGetLockFilePath=obj/cli-publish-linux-x64.packages.lock.json -p:RestoreLockedMode=false` → exit 0, 96 files, `dataguard` + `dataguard.dll`; `deps.json` = `dataguard/0.2.3-nightly.20260929.1`, ProductVersion `0.2.3-nightly.20260929.1+4bea2f3…`. Zip + `sha256sum -c` → `dataguard-0.2.3-nightly.20260929.1-linux-x64.zip: OK`.
2. **VS Code** — `npm pkg set version=0.2.3-nightly.20260929.1 && npm run package -- --out <scratch>/dataguard-vscode-….vsix` → `DONE Packaged … (346 files, 610.89 KB)`; vsce 3.9.2 accepts the prerelease suffix; VSIX contains `server/DataGuard.LanguageServer.dll`. `package.json`/`package-lock.json` restored afterwards. `sha256sum -c` → OK.
3. **Visual Studio** — VS 18 Enterprise MSBuild, exact ci.yml command → `DataGuard.VisualStudio.vsix`; `assert-vsix: OK - DataGuard.VisualStudio.vsix (48.1 MB), version 0.2.3, 22 entries, all 6 required entries present`; staged `DataGuard.VisualStudio-0.2.3-nightly.20260929.1.vsix` + `.sha256`, and the Windows-written checksum verifies with GNU `sha256sum -c` → OK. `VS DRYRUN DONE`, `EXIT=0`.

`git status` after all dry-runs == snapshot before them (+ the 5 intended files only).

## What the runner will do differently from local

- Runner uses .NET SDK `9.0.x`; local was SDK 10.0.401 (no `global.json`), targeting `net9.0` — same TFM, different SDK.
- Runner runs `npm ci` and `npm test` before packaging; locally only `npm run package` was exercised (deps already installed).
- Runner `sha256sum` writes `hash  name` (two spaces); Git Bash locally wrote `hash *name` (binary marker). Both verify with `sha256sum -c`.
- Runner `windows-latest` uses whatever MSBuild vswhere resolves on the runner image; local used VS 18 Enterprise. The `visualstudio` job additionally runs the VS unit tests first — not run locally (identical to ci.yml/release.yml steps).
- `publish` job (gh release delete/create, tag verification) not exercisable locally; it is `gh` CLI logic mirrored from release.yml's create-github-release job. First run creates `nightly` from scratch (no `nightly` tag exists on origin today).
- `cancel-in-progress` can interrupt a run between "delete old nightly" and "create new nightly", leaving no `nightly` release until the next successful run (self-healing).

## Findings for the lead (outside my ownership, not changed)

1. **`-p:Version` alone does not stamp the CLI** — MinVer (`MinVer.targets` line 85) recomputes and sets `Version` at build time; on a fresh build with only `-p:Version=0.2.3-nightly…` the binaries came out `0.2.3-alpha.0.56`. `MinVerVersionOverride` fixes it (verified). release.yml's `cli-package` job passes only `-p:Version`, so its release zips are likely stamped with MinVer's computed value rather than the tag version (the tag itself makes MinVer compute the right value when the tag is on the built commit, so this may be benign on tag pushes but not on `workflow_dispatch` with a bare `tag` input). Worth a one-line follow-up in release.yml.
2. release.yml and marketplace.yml package the VS Code VSIX with `npx @vscode/vsce package` directly, **skipping `prepare-lsp`**, so those VSIXs do not contain `server/DataGuard.LanguageServer.dll` that `extension.ts:287` loads. The nightly VSIX (via `npm run package`) does. Probably a latent bug in the release path.
3. marketplace.yml's Visual Studio job builds the VSIX without `CreateVsixContainer=true` and without `assert-vsix.ps1`; the policy script does not cover it (only ci/release/installers).
4. release.yml's "Set VSIX manifest version" step throws `ExtensionVersion.Fallback constant not found` whenever the release version **equals** the committed Fallback (the `-replace` is a no-op, so `$updated -eq $content`). E.g. tagging `v0.2.3` while `Fallback = "0.2.3"` fails the VS package job. installers.yml checks presence with `-notmatch` first and replaces unconditionally (fixed in the amend); release.yml still has the original guard.

## Unresolved questions

- Should `publish` refuse to overwrite `nightly` when dispatched from a non-`main` ref? Currently allowed (ref name is recorded in the release body).

## Follow-up (coordinator request) — commit `0da13f6`, not pushed

Ownership extended to `release.yml` and `marketplace.yml`; the four findings above applied as a second commit
(`ci(release): stamp CLI with the tag version, bundle the LSP server, assert marketplace VSIX`, trailers verified).
Triggers, pins and publish logic unchanged.

| # | File | Change |
| --- | --- | --- |
| 1 | `release.yml` `cli-package` | `-p:MinVerVersionOverride="$RELEASE_VERSION"` added alongside `-p:Version` (+ job comment explaining why) |
| 2 | `release.yml` `vscode-package` | new `Setup .NET` step (same setup-dotnet pin, `DOTNET_VERSION`) — `prepare-lsp` runs `dotnet publish`; `Package VSIX` step now `set -euo pipefail` → `npm pkg set version` → **`npm run prepare-lsp`** → `npx @vscode/vsce package --out …` (output path and sha256 line unchanged) |
| 2 | `marketplace.yml` `vscode-package` | same `npm run prepare-lsp` before vsce (`Setup .NET for SBOM` already provided the SDK); output/sha256 unchanged |
| 3 | `release.yml` `Set VSIX manifest version` | presence check `if ($content -notmatch 'Fallback = "[^"]+"') { throw … }` then unconditional replace (no more throw when release version == committed Fallback) |
| 4 | `marketplace.yml` `visual-studio-package` | MSBuild line is now ci.yml's (`/restore /t:Build /p:Configuration=Release /p:CreateVsixContainer=true /p:DeployExtension=false /m /nologo /v:minimal`); single step split into **Restore and package VSIX** → new **Assert VSIX contents** (`& ./scripts/assert-vsix.ps1 -VsixPath $vsix.FullName -ExpectedVersion $env:VERSION`, VERSION = `steps.version.outputs.value`, the version patched into the manifest) → **Stage VSIX and checksum** (unchanged copy + sha256) |
| 4 | `marketplace.yml` both VSIX uploads | `if: ${{ github.event_name != 'pull_request' \|\| github.event.pull_request.head.repo.full_name == github.repository }}` added — **required** by policy rule (b) once marketplace.yml is in `WORKFLOWS` (it has a `pull_request` trigger and both upload names contain `vsix`); same guard as ci.yml. Not a trigger/publish change: fork PRs still build/test/assert, they just no longer publish an unsigned VSIX artifact |
| 4 | `scripts/check-workflow-policy.py` | `WORKFLOWS` = ci, release, installers, marketplace (one per line); docstring updated |
| 4 | `scripts/tests/test_check_workflow_policy.py` | +2 cases: marketplace shape (`push, pull_request, workflow_dispatch`, guarded upload) passes; unguarded upload on that shape fails (b) → 18 tests |

Diff stat: `marketplace.yml +26/-1`, `release.yml +16/-2`, `check-workflow-policy.py +7/-2`, `test_check_workflow_policy.py +4/-0`.

### Validation (verbatim)

```
$ python -c "import yaml; [yaml.safe_load(open(f)) for f in (release, marketplace, installers)]"
.github/workflows/release.yml ok
.github/workflows/marketplace.yml ok
.github/workflows/installers.yml ok
$ actionlint .github/workflows/release.yml .github/workflows/marketplace.yml .github/workflows/installers.yml
actionlint ok            (exit 0, no findings)
$ python scripts/check-workflow-policy.py
check-workflow-policy: OK (VSIX assert, fork-PR upload guard, release VS tests)
$ python -m unittest discover -s scripts/tests
Ran 18 tests in 0.034s

OK
```

Not dry-run locally (no new build recipe): the release/marketplace changes reuse commands already exercised above
(`npm run prepare-lsp` via `npm run package`; the ci.yml MSBuild line + `assert-vsix.ps1`; `MinVerVersionOverride`).
Working tree after both commits: only the nine pre-existing `packages.lock.json` edits (untouched, in neither commit) and this untracked report.

Note for the lead: marketplace.yml's `Derive VSIX version` accepts a prerelease/`+` suffix and patches it into the
manifest; a VSIX manifest rejects that, so a dispatch with such a tag fails at MSBuild — pre-existing behaviour, not changed here.
