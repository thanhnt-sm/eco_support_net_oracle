# RCA: VSIX packaging build fails on GitHub Actions (CS0518 in DataGuard.Analyzers)

Date: 2026-09-29 | Branch: feat/vs-extension-hardening @ 8a2df13 | PR #24
Failing jobs: CI run 36543481732 job 109324199830 ("Visual Studio VSIX Packaging Gate", ci.yml); Marketplace Extensions run 36543481722 job 109324199850 ("Package Visual Studio Extension", **marketplace.yml**, not release.yml).

## Executive summary

- **Impact**: every VSIX build on a clean runner fails. Three consumers of the same csproj target are red: ci.yml VSIX gate, marketplace.yml VS package job, release.yml `visual-studio-package` (same `BuildAnalyzers` target). `main` is red too, not just PR #24.
- **Root cause**: `src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj` target `BuildAnalyzers` (introduced in 9f82f3f, 2026-09-28 19:32 +07) invokes `<MSBuild Targets="Restore;Build">` on DataGuard.Analyzers and DataGuard.CodeFixes in a single task call. MSBuild evaluates the project instance once, *before* Restore writes `obj\*.nuget.g.props/targets`; Build then runs on that stale instance. For netstandard2.0, `netstandard.dll` + facades are delivered only by `NETStandard.Library.targets`, imported through `obj\DataGuard.Analyzers.csproj.nuget.g.targets` — so the compile has `Microsoft.CodeAnalysis.dll` (from `project.assets.json`, read at execution time) but no core references → CS0518/CS0234/CS0653. NuGet docs: "restore and build evaluations are run with different global properties ... `msbuild -t:restore,build` will have unpredictable and often incorrect behavior."
- **Premise correction**: 05a26d0 did **not** pass this gate. Run 36515883098 job 109237920811 failed at 03:11:00Z with the identical errors. The gate job was added in df442b9 (this branch) and has failed in all 4 runs. Marketplace VS package job last passed on main at 85248268 (2026-09-28T08:42Z), before `BuildAnalyzers` existed.
- **Why local passes**: `src/DataGuard.Analyzers/obj/*.nuget.g.*` already exist from earlier restores (timestamps 08:40 / 15:55 today), so the pre-restore evaluation already imports `NETStandard.Library.targets`. Not `/m:1` — repro fails at `-m:1` on clean obj and passes on warm obj.
- **Priority**: P1 (blocks PR gate, release and marketplace pipelines). Fix is a 12-line csproj change; no workflow change required.

## Timeline (UTC)

| When | Event |
|---|---|
| 09-28 08:42 | 85248268 main: marketplace VS package job green (last green). No `BuildAnalyzers` target yet. |
| 09-28 11:28 (+07 18:28) | cd138f2 adds `BuildAnalyzers` via `Exec dotnet build --no-restore -o obj\analyzers`. |
| 09-28 11:55 | 7e6f2323 marketplace: `error : BuildAnalyzers target failed: DataGuard.Analyzers.dll not found` (quote-escaping bug, `-o` path). |
| 09-28 12:16 | a671c689 marketplace: `error NETSDK1004: Assets file '...DataGuard.Analyzers\obj\project.assets.json' not found` → `MSB3073` — Exec `--no-restore` with nothing restoring Analyzers on a clean runner. |
| 09-28 12:32 (+07 19:32) | 9f82f3f replaces Exec with `<MSBuild Targets="Restore;Build">` ("prevent MSBuild node deadlocks"). Marketplace failure signature switches to CS0518 and stays there through 824b5599. |
| 09-29 02:28 (+07 09:28) | df442b9 adds ci.yml "Visual Studio VSIX Packaging Gate". |
| 09-29 02:43 / 03:09 / 08:24 / 08:33 | 8609e3ec, 05a26d0, 6d3eb67, 8a2df13: gate job fails, same signature every time. |

## Evidence

### Failing CI gate (8a2df13, job 109324199830)
```
08:33:38.0005989Z Image: windows-2025-vs2026
08:33:38.0006543Z Version: 20260922.246.2
08:33:55.2954298Z dotnet-install: .NET Core Runtime with version '10.0.12' is already installed.
08:33:56.3155949Z dotnet-install: .NET Core SDK with version '9.0.318' is already installed.
08:33:58.7173350Z   MSBUILD: C:\Program Files\Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\MSBuild.exe
08:33:58.7100975Z ##[group]Run & $env:MSBUILD src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj /restore /t:Build /p:Configuration=Release /p:CreateVsixContainer=true /p:DeployExtension=false /m /nologo /v:minimal
08:34:08.2467224Z   Determining projects to restore...                      <- outer /restore (VS csproj only)
08:34:48.6171987Z   Restored ...\DataGuard.VisualStudio.csproj (in 37.39 sec).
08:35:06.0209680Z   Determining projects to restore...                      <- inner BuildAnalyzers Restore target
08:35:10.6575687Z   Restored ...\DataGuard.SqlClassification.csproj (in 3.98 sec).
08:35:10.6578211Z   Restored ...\DataGuard.Contracts.csproj (in 3.98 sec).
08:35:10.6678300Z   Restored ...\DataGuard.Analyzers.csproj (in 3.99 sec).
08:35:11.3291080Z   DataGuard.SqlClassification -> ...\bin\Release\netstandard2.0\DataGuard.SqlClassification.dll   <- fresh evaluation, OK
08:35:11.9007384Z   DataGuard.Contracts -> ...\bin\Release\netstandard2.0\DataGuard.Contracts.dll                   <- fresh evaluation, OK
08:35:12.2086400Z ##[error]...\DataGuard.Analyzers\Analyzers.cs(468,24): error CS0518: Predefined type 'System.String' is not defined or imported
08:35:12.2275544Z ##[error]...\Analyzers.cs(502,2): error CS0653: Cannot apply attribute class 'DiagnosticAnalyzer' because it is abstract
08:35:12.2346387Z ##[error]...\Analyzers.cs(363,44): error CS0619: 'ReadOnlySpan<char>' is obsolete: 'Types with embedded references are not supported in this version of your compiler.'
08:35:12.5091984Z ##[error]Process completed with exit code 1.
```
No `dotnet restore/build/test` step precedes msbuild in this job (steps: checkout, setup-dotnet [already installed], vswhere, msbuild). No NU1xxx, no "Assets file ... doesn't have a target", no NETSDK errors.

### Failing marketplace job (job 109324199850, from run-logs archive)
```
08:34:01 ##[group]Run & $env:MSBUILD "$env:VISUAL_STUDIO_DIR/DataGuard.VisualStudio.csproj" /restore /t:Build /p:Configuration=Release /m
08:35:05.1056547Z          Determining projects to restore...
08:35:09.1224435Z          Restored ...\DataGuard.Analyzers\DataGuard.Analyzers.csproj (in 3.35 sec).
08:35:10.7850327Z      2>...\Analyzers.cs(502,2): error CS0653: Cannot apply attribute class 'DiagnosticAnalyzer' because it is abstract
08:35:10.9322672Z     346 Error(s)        (482 x CS0518, 44 x CS0234)
```
Steps before msbuild: checkout, verify ref, derive version, patch manifest, vswhere. No dotnet step. (`gh run view --log` truncates this job at ~1276 lines; the archive has 2182.)

### 05a26d0 gate (job 109237920811) — the run claimed to be green
```
03:09:25.6705323Z Version: 20260922.246.2      (same image)
03:10:58.5177317Z   Restored ...\DataGuard.Analyzers.csproj (in 2.67 sec).
03:11:00.0411262Z ##[error]...\obj\Release\netstandard2.0\.NETStandard,Version=v2.0.AssemblyAttributes.cs(4,46): error CS0234: ... 'TargetFrameworkAttribute' does not exist in the namespace 'System.Runtime.Versioning'
03:11:00.0610953Z ##[error]...\Analyzers.cs(12,19): error CS0234: The type or namespace name 'RegularExpressions' does not exist in the namespace 'System.Text'
```
`gh run list --workflow ci.yml --branch feat/vs-extension-hardening`: 8609e3ec, 05a26d03, 6d3eb674, 8a2df13e — all `failure`, and in each the only failing job is "Visual Studio VSIX Packaging Gate" ("Visual Studio Build and Test" is green — likely source of the confusion).

### Mechanism proof (local NuGet cache + obj)
`~/.nuget/packages/netstandard.library/2.0.3/build/netstandard2.0/NETStandard.Library.targets`:
```xml
<When Condition="'$(TargetFrameworkIdentifier)' == '.NETStandard'">
  <ItemGroup>
    <Reference Include="$(MSBuildThisFileDirectory)ref\netstandard.dll"> ...
    <Reference Include="$(MSBuildThisFileDirectory)ref\Microsoft.Win32.Primitives.dll;...ref\System.Text.RegularExpressions.dll;...ref\mscorlib.dll;...">
```
`src/DataGuard.Analyzers/obj/DataGuard.Analyzers.csproj.nuget.g.targets` line 4:
```xml
<Import Project="$(NuGetPackageRoot)netstandard.library\2.0.3\build\netstandard2.0\NETStandard.Library.targets" Condition="Exists(...)" />
```
That file is created by Restore. A project instance evaluated before it exists never imports it → no `netstandard.dll`, no facades → exactly the observed error set. `Microsoft.CodeAnalysis.dll` still resolves because `ResolvePackageAssets` reads `project.assets.json` at execution time (hence CS0653 rather than "DiagnosticAnalyzer not found").

### Reproduction (scratch copy: Analyzers + Contracts + SqlClassification + root props/targets/.editorconfig; MSBuild 18.10.1.42706, same as runner's VS 18 Enterprise)
driver.proj = the exact csproj pattern:
```xml
<MSBuild Projects="src\DataGuard.Analyzers\DataGuard.Analyzers.csproj" Properties="Configuration=Release" Targets="Restore;Build" />
```
| Run | obj state | Result |
|---|---|---|
| A: `Restore;Build` one task, `-m` | clean | exit 1 — **241 x CS0518, 22 x CS0234, 1 x CS0653** (Contracts/SqlClassification built fine, Analyzers failed — same shape as CI) |
| A: same, `-m:1` | clean | exit 1 — 241 x CS0518, 22 x CS0234 (node count irrelevant) |
| A: same, `-m:1` | warm (second run) | exit 0 — `DataGuard.Analyzers -> ...` (this is the local-dev situation) |
| B: split Restore (unique `MSBuildRestoreSessionId`) then Build | clean | exit 0 |

### Eliminated hypotheses
| Hypothesis | Evidence against |
|---|---|
| Runner image / MSBuild update between runs | Every run since 2026-09-28 (green 85248268, red 7e6f2323 … 8a2df13) prints `Image: windows-2025-vs2026 / Version: 20260922.246.2`; MSBuild 18.x Enterprise in all. |
| SDK-10 `dotnet restore` before full-MSBuild build | No dotnet step precedes msbuild in either job; setup-dotnet only reports "already installed". |
| Lock-file / RestoreLockedMode / NU1xxx | Inner restore succeeds ("Restored DataGuard.Analyzers.csproj"); zero NU1xxx lines (the 8 grep hits are `csc /nowarn:...1701,1702` fragments); `git diff 05a26d0 HEAD -- '**/packages.lock.json'` is empty; the 9 working-tree lockfile diffs are local CRLF churn, not on CI. |
| Workflow changes in this PR (policy check, setup-dotnet pin, assert-vsix) | `git diff 05a26d0 HEAD --stat -- .github` lists only ci.yml and release.yml — **marketplace.yml is byte-identical between 05a26d0 and HEAD, yet its VS package job fails with the same 346 errors**. In ci.yml the diff touches only post-msbuild steps; the gate's msbuild line is unchanged context; the policy check runs in build-and-test, not the gate. |
| Tree changes to Analyzers/props | `git diff 05a26d0 HEAD --stat -- src/DataGuard.Analyzers Directory.Build.props global.json src/DataGuard.VisualStudio/*.csproj` is empty; no `global.json`, no `Directory.Packages.props`. |
| `/m` node deadlock (the 9f82f3f rationale) | Repro fails identically at `-m:1`; the pre-9f82f3f Exec variant failed with NETSDK1004 (nothing restored Analyzers), not a hang — the "deadlock" is unevidenced. |

## Fix proposal (csproj only; fixes ci.yml gate, marketplace.yml and release.yml VS package jobs at once)

Do what MSBuild's own `-restore` switch does: run Restore under a unique global property so its evaluation is not reused, then Build with the normal properties. Apply to **both** calls (CodeFixes has the identical bug and will surface it as soon as Analyzers is fixed).

`src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj`:
```diff
   <Target Name="BuildAnalyzers"
           Condition="'$(CreateVsixContainer)' == 'true'"
           BeforeTargets="IncludeAnalyzersInVsix">
+    <!-- Restore and Build must be separate MSBuild invocations with different global properties:
+         a single Targets="Restore;Build" call evaluates the project before restore writes
+         obj\*.nuget.g.targets, so NETStandard.Library.targets (netstandard.dll + facades) is never
+         imported and the build fails with CS0518 on a clean checkout (CI). The unique
+         MSBuildRestoreSessionId mirrors what `msbuild -restore` does internally. -->
     <MSBuild Projects="$(MSBuildThisFileDirectory)..\DataGuard.Analyzers\DataGuard.Analyzers.csproj"
-             Properties="Configuration=$(Configuration)"
-             Targets="Restore;Build" />
+             Properties="Configuration=$(Configuration);MSBuildRestoreSessionId=$([System.Guid]::NewGuid())"
+             Targets="Restore" />
+    <MSBuild Projects="$(MSBuildThisFileDirectory)..\DataGuard.Analyzers\DataGuard.Analyzers.csproj"
+             Properties="Configuration=$(Configuration)"
+             Targets="Build" />
     <MSBuild Projects="$(MSBuildThisFileDirectory)..\DataGuard.CodeFixes\DataGuard.CodeFixes.csproj"
-             Properties="Configuration=$(Configuration)"
-             Targets="Restore;Build" />
+             Properties="Configuration=$(Configuration);MSBuildRestoreSessionId=$([System.Guid]::NewGuid())"
+             Targets="Restore" />
+    <MSBuild Projects="$(MSBuildThisFileDirectory)..\DataGuard.CodeFixes\DataGuard.CodeFixes.csproj"
+             Properties="Configuration=$(Configuration)"
+             Targets="Build" />
   </Target>
```
Notes for the implementer:
- Keep exactly this tested form; do not add `RemoveProperties` — the task already inherits the parent's `/p:CreateVsixContainer=true /p:DeployExtension=false` globals and that behavior is unchanged.
- No workflow change needed. A pre-`dotnet restore` step in ci.yml/marketplace.yml would mask the csproj bug and leave clean local builds broken; rejected.
- Reverting to `Exec dotnet build --no-restore` is not viable: it failed on CI with NETSDK1004 (a671c689) for the same reason (nothing restores Analyzers on a clean runner).
- Update `docs/` note/CHANGELOG line that describes BuildAnalyzers if it mentions the Restore;Build shape.

## Verification

1. Local (implementer): from repo root in PowerShell, `Remove-Item -Recurse -Force src\DataGuard.Analyzers\obj, src\DataGuard.CodeFixes\obj, src\DataGuard.Contracts\obj, src\DataGuard.SqlClassification\obj`, then run the exact ci.yml line: `& "<MSBuild.exe>" src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj /restore /t:Build /p:Configuration=Release /p:CreateVsixContainer=true /p:DeployExtension=false /m /nologo /v:minimal`. Must fail with CS0518 before the fix and produce the VSIX after.
2. CI: push → "Visual Studio VSIX Packaging Gate" (ci.yml) and "Package Visual Studio Extension" (marketplace.yml) must go green; the existing "Assert VSIX contents" step already checks `DataGuard.Analyzers.dll` / `DataGuard.CodeFixes.dll` are inside the VSIX, so no new assertion is needed. release.yml `visual-studio-package` is fixed by the same change (validate on next tag).
3. No new guard needed: every CI run is a clean checkout, which is exactly the condition that exposes this bug.

## Recurrence prevention

- Monitoring gap: none — the gate did its job. The gap was interpretive: "Visual Studio Build and Test" green was read as the gate passing. Check the specific job name before declaring a run green.
- Design: any future `<MSBuild Targets="Restore;...">` in this repo must use the split pattern; consider a `scripts/check-workflow-policy.py` rule that greps csproj files for `Targets="Restore;`.
- `main` carries the same bug (since 9f82f3f); merge PR #24 with the fix or cherry-pick it to main.

## Unresolved questions

1. 9f82f3f cites "MSBuild node deadlocks in GitHub Actions" for moving from Exec to the MSBuild task. No run evidences a hang (a671c689 failed fast with NETSDK1004). Was there an unreported local observation, or was the deadlock inferred from the NETSDK1004 failure?
2. release.yml's `visual-studio-package` shares the `BuildAnalyzers` target and therefore the bug, but was not exercised by these runs (they are marketplace.yml runs); verify on the next tag.

**Status:** DONE
**Summary:** Root cause is `BuildAnalyzers` calling `<MSBuild Targets="Restore;Build">` in one task (9f82f3f): the project instance is evaluated before restore writes `obj\*.nuget.g.targets`, so `NETStandard.Library.targets` (netstandard.dll + facades) is never imported and csc fails with CS0518/CS0234/CS0653 on every clean checkout; reproduced locally on clean obj at `-m` and `-m:1`, and fixed by splitting Restore (unique `MSBuildRestoreSessionId`) from Build. Local passes only because `obj/*.nuget.g.*` already exist.
**Concerns/Blockers:** Premise is wrong — 05a26d0 never passed this gate (job 109237920811 failed identically at 03:11Z); the gate has failed on all 4 branch runs and main's marketplace VS job has been red since 2026-09-28 11:55Z. Runner image is identical across all green and red runs, so nothing environmental changed.
