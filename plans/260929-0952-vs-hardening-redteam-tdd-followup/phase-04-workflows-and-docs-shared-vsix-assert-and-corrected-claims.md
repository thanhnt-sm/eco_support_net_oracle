---
phase: 4
title: "Workflows and docs: shared VSIX assert and corrected claims"
status: pending
priority: P2
effort: "2h"
dependencies: [2, 3]
---

# Phase 4: Workflows and docs: shared VSIX assert and corrected claims

## Overview
Make the release path enforce the same VSIX gate as CI, stop hosting fork-built unsigned VSIX artifacts, and correct every documentation claim the red-team fact-check failed. Closes findings 7, 9 (docs half), 12, plus the three validate-pass failures.

## Requirements
- Functional:
  - Extract the CI "Assert VSIX contents" PowerShell into `scripts/assert-vsix.ps1 -VsixPath <path> -ExpectedVersion <v>`; `ci.yml` and `release.yml` both call it after their MSBuild step. Required entries: `cli/dataguard.exe`, `DataGuard.Analyzers.dll`, `DataGuard.CodeFixes.dll`, `extension.vsixmanifest`, `DataGuard.VisualStudio.dll`, `DataGuard.VisualStudio.pkgdef`.
  - `release.yml` `visual-studio-package` job runs `dotnet test tests/DataGuard.VisualStudio.Tests --configuration Release` before packaging.
  - `ci.yml` VSIX upload step gets `if: github.event_name != 'pull_request' || github.event.pull_request.head.repo.full_name == github.repository`; artifact name includes the head SHA; the job still builds on fork PRs.
  - Docs: `SECURITY.md`/`SECURITY.vi.md` and `docs/USAGE.md` scope the ide-safe claim to `validate`/`assess` and list `snapshot`, `baseline`, `verify-shape` as live-database commands with confirmation; document `--allow-env-connection`, the `ide-safe: active` handshake, the baseline warning, and the removal of NuGet install guidance (with the note that `DataGuard.Cli` is not yet published; owner to reserve the ID).
  - Plan hygiene in `plans/260929-0835-vs-extension-hardening/`: phase-02 test-file name and "exits 2" → "exits 1"; phase-03 `timeout-minutes` 30 → 40.
  - CHANGELOG entries for Phases 1–3 under Security/Fixed/Changed.
- Non-functional: both workflows parse (`python -c "import yaml…"`); actionlint if available.

## Related Code Files
- Create: `scripts/assert-vsix.ps1`
- Modify: `.github/workflows/ci.yml`, `.github/workflows/release.yml`, `SECURITY.md`, `SECURITY.vi.md`, `docs/USAGE.md`, `CHANGELOG.md`, `plans/260929-0835-vs-extension-hardening/phase-02-*.md`, `phase-03-*.md`

## Implementation Steps (tests first)
1. **Tests Before**: a workflow policy test (`tests/git-tools` or a Python check under `scripts/`) asserting every `msbuild … DataGuard.VisualStudio.csproj` step in both workflows is followed by `scripts/assert-vsix.ps1`, and every `upload-artifact` reachable from `pull_request` carries a same-repo `if:` — fails today for `release.yml` and the CI upload.
2. **Implement** the workflow and doc changes.
3. **Tests After**: run `scripts/assert-vsix.ps1` locally against the packaging-build VSIX (must pass) and against a tampered copy with `cli/dataguard.exe` removed (must fail).
4. **Regression Gate**: YAML parse; `./scripts/verify_docs_sync.sh`.

## Success Criteria
- [ ] One assert script used by both workflows; release runs VS unit tests
- [ ] Fork PRs build the VSIX but never publish it
- [ ] Docs match shipped behaviour for both hosts; validate-pass failures corrected

## Risk Assessment
- `release.yml` tag builds are slower by the VS test run (~1 min) — acceptable.
