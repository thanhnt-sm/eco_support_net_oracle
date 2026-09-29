# Baseline Build + Test — DataGuard (2026-09-29 08:38–08:56)

Scope: explicit command list from parent (no diff-aware mapping). No source modified by tester. No commits.
Runner: dotnet SDK 10.0.401; MSBuild `C:\Program Files\Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\MSBuild.exe` (VS 18 Enterprise). Shell: Git Bash.

## Tree state at start (HEAD 5d0acc2, dirty)

```
 M src/{Analyzers,Cli,Contracts,Core,MySql.Adapter,Oracle.Adapter,PostgreSql.Adapter,SqlClassification,SqlServer.Adapter}/packages.lock.json  (+337/-3)
 M src/DataGuard.VisualStudio/DataGuardPackage.cs   (+1)
 M tests/DataGuard.VisualStudio.Tests/RuleInventoryTests.cs (+2/-1)
?? FIX_DATAGUARDVISUALSTUDIO_BUILD_PLAN.md, docs/journals/260929-0830-fix-dataguard-visualstudio-build.md
```

**Caveat — tree moved during the run.** Between 08:43:09 and 08:44:19 another session modified `.github/workflows/ci.yml` (+64, new `visual-studio-vsix-package` job), `CHANGELOG.md`, `README.md`, `SECURITY.md`, `SECURITY.vi.md`, `docs/USAGE.md`, `src/DataGuard.Cli/Program.cs` (+43, `--ide-safe` option). Steps 1–4 ran on the pre-edit tree; step 5 (MSBuild VSIX, 08:47–08:48) compiled the edited `Program.cs` into `cli/dataguard.exe`. Baseline is therefore split across two tree states.

## Results

| # | Command | Exit | Wall | Result |
|---|---|---|---|---|
| 1a | `dotnet restore DataGuard.sln --locked-mode` | 1 | 3s | **NU1004 x15** on 9 projects (see below) |
| 1b | `dotnet restore DataGuard.sln` | 0 | 4s | OK. Side effect: rewrote the 9 lock files back to HEAD content (git then showed them M with empty diff — line-ending only) |
| 2 | `dotnet build DataGuard.sln -c Release --no-restore` | 0 | 56s | **0 warnings, 0 errors**. Log line 49: `DataGuard.VisualStudio -> bin\Release\net472\DataGuard.VisualStudio.vsix` — packaging + `PublishDataGuardCli` ran (lock files re-dirtied 08:41:01) |
| 3 | `dotnet test tests/DataGuard.VisualStudio.Tests -c Release` | 0 | 15s | **60 passed, 0 failed, 1 skipped** / 61 (net472). Rebuilds VS project with `CreateVsixContainer=false;GeneratePkgDefFile=false` (ProjectReference `<Properties>`) — this replaced the step-2 .vsix; none present after step 3 |
| 4a | `dotnet test tests/DataGuard.Core.Tests -c Release --no-build` | 0 | 229s | **814 passed, 0 failed, 0 skipped** (test duration 3m40s) |
| 4b | `dotnet test tests/DataGuard.Analyzers.Tests -c Release --no-build` | 0 | 12s | **13 passed, 0 failed** |
| 4c | `dotnet test tests/DataGuard.CodeFixes.Tests -c Release --no-build` | 0 | 16s | **24 passed, 0 failed** |
| 5a | MSBuild VSIX with `/restore /t:Build ...` (slash switches) | 1 | 1s | MSB1008 — Git Bash MSYS path-converted `/restore`; not a project fault. The same slash form works in pwsh (as ci.yml uses) |
| 5b | MSBuild VSIX with `-restore -t:Build -p:Configuration=Release -p:CreateVsixContainer=true -p:DeployExtension=false -m` | 0 | 58s | **0 warnings, 0 errors**, VSIX produced |

Totals: **911 tests run, 911 passed, 0 failed, 1 skipped.** Not run (out of requested scope): `tests/DataGuard.GoldenCorpus.Tests`, `tests/DataGuard.Observability.Tests`.

Skipped test: `NavigationTests` `[Fact(Skip = "VS SDK integration — requires experimental instance (IVsWindowFrame, IVsTextView, VsShellUtilities)")]` (tests/DataGuard.VisualStudio.Tests/NavigationTests.cs:41) — intentional, pre-existing.

## Step 1a verbatim (trimmed, representative)

```
src\DataGuard.Core\DataGuard.Core.csproj : error NU1004: The package references have changed for net9.0. Lock file's package references: ... Microsoft.NET.ILLink.Tasks:[9.0.20, ) ... project's package references: <same list without ILLink.Tasks> ... restore can't be run in locked mode.
src\DataGuard.Core\DataGuard.Core.csproj : error NU1004: The project's runtime identifiers have changed from. Project's runtime identifiers: , lock file's runtime identifiers win-x64.
```
Same pair for: Analyzers (RID only), Cli, Contracts (RID only), Core, MySql.Adapter, Oracle.Adapter, PostgreSql.Adapter, SqlClassification (RID only), SqlServer.Adapter. 13/23 projects restored fine.

## VSIX artifact (step 5b)

- Path: `src/DataGuard.VisualStudio/bin/Release/net472/DataGuard.VisualStudio.vsix`
- Size: **50,457,280 bytes** (48.1 MiB), 22 entries
- Required entries: `cli/dataguard.exe` PRESENT (126,640,943 bytes uncompressed, self-contained single-file), `DataGuard.Analyzers.dll` PRESENT (43,008), `DataGuard.CodeFixes.dll` PRESENT (40,448)
- Other entries: `[Content_Types].xml`, `catalog.json`, `manifest.json`, `extension.vsixmanifest`, `DataGuard.VisualStudio.dll`, `DataGuard.VisualStudio.pkgdef`, `LICENSE.txt`, `Microsoft.Bcl.AsyncInterfaces.dll`, `Microsoft.VisualStudio.Extensibility.Editor.Contracts.dll`, `Microsoft.VisualStudio.Linux.ConnectionManager.Store.dll`, `System.{Buffers,IO.Hashing,IO.Pipelines,Memory,Numerics.Vectors,Runtime.CompilerServices.Unsafe,Text.Encodings.Web,Text.Json,Threading.Tasks.Extensions}.dll`
- `dotnet build DataGuard.sln` locally ALSO produces this .vsix (CreateVsixContainer defaults to true; only `BuildingForTesting=true` turns it off, csproj:7). CI's test job builds with it off, which is why CreatePkgDef failures only surfaced in packaging — matches the rationale in the new ci.yml job.

## Critical finding: lock-file drift is caused by VSIX packaging builds

`src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj:89-94` target `PublishDataGuardCli` (Condition `CreateVsixContainer=='true'`) runs
`dotnet publish ..\DataGuard.Cli\DataGuard.Cli.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true ...`
whenever the VS project is built with packaging on — which includes a plain local `dotnet build DataGuard.sln` (see step 2 log lines 47-49). That publish restores the CLI's closure with RID `win-x64`, rewriting 9 `packages.lock.json` files (adds `net9.0/win-x64` section + `Microsoft.NET.ILLink.Tasks`).

Evidence: 6 CLI-closure lock files (Cli, Core, 4 adapters) mtime 08:41:01 = inside step 2; the 3 files that are direct ProjectReferences of the VS project (Analyzers, Contracts, SqlClassification — RID-only diff) mtime 08:47:32 = inside step 5b's `-restore`. Diff content matches the NU1004 message exactly. The test build (step 3) does not trigger it (CreateVsixContainer=false).

Consequence: any `--locked-mode` restore run after a packaging build on this machine fails NU1004 — and lock files with the RID section fail locked-mode because the 9 projects don't declare `win-x64` (step 1a). Committing them as-is is not a fix (that is the state d3fa8d4 reverted). Options for the parent (none applied, none tested here):
1. Stop the publish from touching lock files — e.g. publish with `--no-restore` after a dedicated restore into a separate `RestorePackagesPath`/lock path, or pass `-p:RestorePackagesWithLockFile=false` to the publish Exec (**unverified hypothesis**: whether NuGet ignores an existing lock file when this is explicitly false needs a test).
2. Declare `<RuntimeIdentifiers>win-x64</RuntimeIdentifiers>` in the 9 closure projects so plain restore and the RID publish produce identical lock content, then commit the regenerated lock files.

## Side effects of this run (none to source)

- Step 1b overwrote the 9 dirty lock files with HEAD content; step 2 re-dirtied them identically via `PublishDataGuardCli`. Net: lock files now show the same +337/-3 diff as at start. `DataGuardPackage.cs` / `RuleInventoryTests.cs` untouched by tester.
- Build outputs under `bin/`, `obj/` only. VSIX from step 5b is present at the path above.

## Performance

- Core.Tests: 814 tests in 3m40s wall — slow relative to the others; no per-test timing captured (default verbosity). Worth a `--logger trx` pass to find the long tail if CI time matters.
- Full sequence wall ~7.5 min excluding restore retries.

## Flaky/Quarantined

None this run — 0 failures, no reruns needed. `tests/.quarantine.json` not present.

## Unresolved questions

1. Who edited ci.yml/Program.cs/etc. at 08:43–08:44 — is the parent already mid-fix? Baseline for step 5 includes those edits.
2. Lock-file loop: option 1 or 2 above? Parent decision; either way the CI `visual-studio-vsix-package` job will hit the same rewrite on windows-latest (harmless there since nothing is committed, but `--locked-mode` steps after it would fail).
3. GoldenCorpus.Tests / Observability.Tests not requested — run before shipping if the fix touches Core/Cli.

**Status:** DONE_WITH_CONCERNS
