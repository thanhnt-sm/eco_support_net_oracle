# Phase 03 — CI VSIX packaging gate

## Context
`.github/workflows/ci.yml` job `visual-studio-build-and-test` (`:107-125`) only runs `dotnet test` with `CreateVsixContainer=false`; CreatePkgDef/packaging failures surfaced only in `release.yml:411-422`.

## Overview
Priority: High · Status: done

## Requirements
- New job `visual-studio-vsix-package` on `windows-latest`: locate MSBuild via vswhere, `msbuild src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj /restore /t:Build /p:Configuration=Release /p:CreateVsixContainer=true /p:DeployExtension=false /m`.
- Assert the produced VSIX contains `cli/dataguard.exe`, `DataGuard.Analyzers.dll`, `DataGuard.CodeFixes.dll`, `extension.vsixmanifest`; assert manifest `Identity Version` matches `ExtensionVersion.Fallback` constant in source (grep).
- Upload the VSIX as a CI artifact (retention 7 days).
- Runs on push/PR like other jobs; `timeout-minutes: 30` (self-contained publish is slow).

## Related files
Modify: `.github/workflows/ci.yml`.

## Steps
1. Add job after `visual-studio-build-and-test` reusing the checkout/setup-dotnet pinned SHAs already in the file.
2. PowerShell step for entry assertions with `System.IO.Compression.ZipFile`.
3. Validate YAML (`actionlint` if available; otherwise `python -c "import yaml; yaml.safe_load(open('.github/workflows/ci.yml'))"`).

## Todo
- [x] job added  - [x] YAML validated

## Success criteria
Job present, YAML valid, uses only SHA-pinned actions already in the file.

## Risks
windows-latest MSBuild version drift; mitigated by vswhere `-latest -products *`.
