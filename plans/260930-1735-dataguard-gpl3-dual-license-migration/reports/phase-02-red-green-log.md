# Phase 2 RED -> GREEN log (2026-09-30, branch feat/gpl3-dual-license)

Env: Git Bash, dotnet 10.0.401, Python 3.13, VS 18 MSBuild. Docker daemon down (deferred to P6).

| # | Test | RED (before) | GREEN (after) |
|---|------|--------------|---------------|
| 1 | Sweep MIT (`git grep -nIE '\bMIT\b'` over the FILE_LIST) | 46 lines (README x4, docs/PRODUCT, architecture, grants, rules, ...) | `python3 scripts/check-license-consistency.py` -> OK, 193 files scanned, 0 hits outside ALLOWED_MENTIONS |
| 2 | Nuspec | 16 files in `pack -o`; Cli/Core/4 adapters `MIT`, rest no `<license>`; 15 nupkg + 1 snupkg (Host warns not packable, pre-existing) | 13 nupkg (+1 snupkg); each exactly 1 `<license type="expression">`; 12 = GPL-3.0-only, Contracts = MIT; Contracts nupkg GPL/COPYING entries = 0 |
| 3 | Notices in artifacts | publish dir notice files = 0; v0.3.0 zip/nupkg/nightly VSIX = 0 (P1) | publish dir = 3; Cli nupkg = 6 (3 at root + 3 under tools/net9.0/any); VS VSIX (fresh MSBuild build) has LICENSE.txt (GPL, 35149 B) + THIRD-PARTY-NOTICES.md + ADDITIONAL-PERMISSIONS.md. VS Code VSIX: **not built** (npm/vsce), notices sit in package root, `.vscodeignore` has no `*.md` exclusion. Docker: **not exercised** (daemon down); Dockerfile LABELs added, published label = release.yml (P6) |
| 4 | Unit tests of the gate | `unittest` ImportError (script absent) | 17 tests OK (cases a-i + FILE_LIST, copies, metadata, GPL text, npm) |
| 5 | Analyzer packaging (D4) `scripts/verify-analyzer-packaging.sh` (Exe consumer, fresh NUGET_PACKAGES) | FAIL: `lib/` entries 2 (expected 0); consumer bin has Analyzers.dll + SqlClassification.dll (2, expected 0). Layout 3 DLLs ok, build ok, csc loads analyzer ok | PASS: lib/ = 0, 3 DLLs in analyzers/dotnet/cs, build green, csc gets `/analyzer:...DataGuard.Analyzers.dll`, output has 0 Analyzers/SqlClassification DLL and 1 Contracts.dll. **D4 GREEN**, no fallback |
| 6 | Whole phase | gate: 51 problems | see below |

Note on test 5: the phase text says `dotnet new classlib`; a class library never copies package dependencies to `bin/`, so the RED would have been vacuous. The consumer is an Exe.

## GREEN commands
- `python3 scripts/check-license-consistency.py` -> OK
- `python3 -m unittest discover -s scripts/tests` -> 44 tests OK
- `python3 scripts/check-nuget-licences.py` -> OK (281 NuGet + 8 npm; no first-party package leaked into the graph, allow-list unchanged)
- `python3 scripts/check-workflow-policy.py` -> OK
- `./scripts/verify_docs_sync.sh` -> OK (runs the gate)
- `dotnet restore DataGuard.CrossPlatform.slnf --locked-mode` -> OK after lock updates
- `dotnet build DataGuard.CrossPlatform.slnf -c Release --no-restore` -> 0 warnings, 0 errors
- Tests: Analyzers.Tests 13, CodeFixes.Tests 24, GoldenCorpus.Tests 28, Core.Tests 898 -> all pass
- VSIX: `MSBuild DataGuard.VisualStudio.csproj -restore -t:Build -p:Configuration=Release -p:CreateVsixContainer=true` OK; `VsixAnalyzerPackagingTests` 3/3 pass against that fresh VSIX (exercised, not vacuous)
- Cli build/publish output still contains DataGuard.SqlClassification.dll (it arrives through DataGuard.Analyzers `CopyLocalLockFileAssemblies=true`), so no direct ProjectReference needed.

## GPL text provenance
`curl https://www.gnu.org/licenses/gpl-3.0.txt` -> 35149 B, sha256 `3972dc9744f6499f0f9b2dbf76696f2ae7ad8af9b23dde66d6af86c9dfb36986`.
Independent copy: SPDX license-list-data `GPL-3.0-only.txt` (34674 B, normalised typography). Token-level diff: 5644 vs 5644 words; only differences are curly quotes / (C) glyph and one appendix URL (`licenses/why-not-lgpl.html` vs `philosophy/why-not-lgpl.html`). gnu.org file committed as LICENSE (it carries the how-to-apply appendix GitHub expects).

## Nightly (last MIT) snapshot
Pasted into CHANGELOG (published 2026-09-30T07:09:25Z, 10 assets, `0.3.0-nightly.20260930.11`).

## Unresolved / not exercised
See final report.
