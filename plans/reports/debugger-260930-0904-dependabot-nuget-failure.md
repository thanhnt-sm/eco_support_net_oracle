# RCA: Dependabot "nuget in /." fails with `dependency_file_not_found`

Date: 2026-09-30 | Repo: eco_support_net_oracle, main @ 070208b | Author: debugger (read-only)

## Executive summary

- **Impact:** weekly Dependabot NuGet job has failed every run since 2026-09-23 (runs 35808894366 on 406ab48, 36658191140 on 4bea2f3). No NuGet version/security PRs for any of the 26 projects for 2 weeks. The github-actions job is unaffected.
- **Root cause:** `da24dad` (2026-09-18) added `src/DataGuard.VisualStudio` and `tests/DataGuard.VisualStudio.Tests` to `DataGuard.sln`. Dependabot expands the whole `.sln` (it ignores `DataGuard.CrossPlatform.slnf`). When it evaluates the VSIX project on Linux *after* restore, the `Microsoft.VSSDK.BuildTools` 18.5.38461 package imports `tools/vssdk/Microsoft.VSSDK.targets`, but the nupkg ships the file as `Microsoft.VsSDK.targets`. Case-sensitive FS → MSB4019 → dependabot-core maps "imported project ... was not found" to `MissingFileException` = `dependency_file_not_found`, and one failed project aborts the whole workspace.
- **Latent enabler:** `ba68892` (2026-08-20) set `<VSSDKBuildToolsAutoSetup>true</VSSDKBuildToolsAutoSetup>`, the only gate on that import. Dormant until the project entered the `.sln`.
- **Upstream:** dependabot-core issue #11522 "Unable to update Visual Studio SDK project" (open since 2025-02-07, no maintainer response) — identical error class, same package.
- **Fix (minimal, 1 line):** gate `VSSDKBuildToolsAutoSetup` on `'$(OS)' == 'Windows_NT'` in `src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj`. Keeps NuGet updates for all projects including the VSIX ones (which carry the MessagePack CVE pin). Fallback: `directories:` list in `dependabot.yml`.
- **Priority:** P2 — no prod impact, but dependency/security update pipeline is dark.

## Timeline

| Date | Event | Evidence |
|---|---|---|
| 2026-08-20 | `ba68892` sets `VSSDKBuildToolsAutoSetup=true` in VS csproj | `git log -S'VSSDKBuildToolsAutoSetup'` |
| 2026-09-16 / 09-17 | Dependabot nuget runs green (b48345a, da4ce6c) | run 35176401154: 22 "Performing single restore" lines, none for `DataGuard.VisualStudio*` |
| 2026-09-17 | At da4ce6c the VS csproj exists but is **not** in `DataGuard.sln` | `git show da4ce6c:DataGuard.sln \| grep -i visualstudio` → only `VisualStudioVersion` header lines |
| 2026-09-18 | `da24dad` adds both VS projects to `DataGuard.sln` (+30 lines), creates `tests/DataGuard.VisualStudio.Tests` | `git show da24dad -- DataGuard.sln` |
| 2026-09-20 | `bfea5ae` adds `Microsoft.NETFramework.ReferenceAssemblies` + regenerates lock files | eliminated, see below |
| 2026-09-21 | `823b122` splits CI gates, introduces `DataGuard.CrossPlatform.slnf` (VS projects excluded) | slnf content |
| 2026-09-23 02:06 | First red run (406ab48). VS project restores OK, test project fails | run 35808894366 lines 215, 382-383 |
| 2026-09-24 | `484e77e` adds `<Properties>BuildingForTesting=...</Properties>` to the ProjectReference | eliminated, see below |
| 2026-09-29 | `ce20e3a` splits Restore/Build in `BuildAnalyzers` | eliminated, see below |
| 2026-09-30 02:07 | Second red run (4bea2f3), identical failure | run 36658191140 |

## Evidence

### 1. The error names the exact missing file (run 36658191140, raw log)

```
2026-09-30T02:07:35.2624116Z updater | INFO Performing single restore for project /home/dependabot/dependabot-updater/repo/tests/DataGuard.VisualStudio.Tests/DataGuard.VisualStudio.Tests.csproj
2026-09-30T02:07:37.7986101Z updater | ERROR Error type: dependency_file_not_found
2026-09-30T02:07:37.7987157Z - message: Exception of type 'NuGetUpdater.Core.MissingFileException' was thrown.
2026-09-30T02:07:37.7988663Z - file-path: /home/dependabot/.nuget/packages/microsoft.vssdk.buildtools/18.5.38461/tools/vssdk/Microsoft.VSSDK.targets
```

The `- message` / `- file-path` lines are not prefixed with `updater |`, so a `grep "updater |"` hides them.

### 2. Dependabot enumerates via `DataGuard.sln`, not the slnf (run 36658191140, lines 21-48)

```
INFO   Discovering projects beneath [.].
INFO     Entry points found: /home/dependabot/dependabot-updater/repo/DataGuard.sln
INFO     Expanding solution: /home/dependabot/dependabot-updater/repo/DataGuard.sln:
...
INFO       Expanded project: .../tests/DataGuard.VisualStudio.Tests/DataGuard.VisualStudio.Tests.csproj
INFO       Expanded project: .../src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj
```

dependabot-core `DiscoveryWorker.FindEntryPoints()` handles `.sln`, `.slnx`, `.proj`, `.csproj`, `.fsproj`, `.vbproj` only — `.slnf` is not supported. One project with `IsSuccess == false` makes the whole `WorkspaceDiscoveryResult` fail.

### 3. Green run never touched the VS projects (run 35176401154)

22 `Performing single restore` lines: Analyzers, Build, Cli, CodeFixes, Contracts, Core, Host, LanguageServer, MySql, Observability x3, Oracle, PostgreSql, SqlClassification, SqlServer, Analyzers.Tests, CodeFixes.Tests, Core.Tests, GoldenCorpus.Tests, Observability.Tests. No `VisualStudio`. Consistent with the `.sln` not containing them at da4ce6c.

### 4. Package casing mismatch (local NuGet cache, `~/.nuget/packages/microsoft.vssdk.buildtools/18.5.38461`)

`unzip -l microsoft.vssdk.buildtools.18.5.38461.nupkg`:
```
build/Microsoft.VSSDK.BuildTools.props
build/Microsoft.VSSDK.BuildTools.targets
tools/vssdk/Microsoft.VsSDK.targets          <-- "VsSDK" (lowercase s)
```

`build/Microsoft.VSSDK.BuildTools.targets` line 59:
```xml
<Import Project="$(VSToolsPath)\vssdk\Microsoft.VSSDK.targets" Condition=" '$(VsSdkBuildTargetsImported)' != 'true' AND '$(VSSDKBuildToolsAutoSetup)' == 'true' " />
```
`build/Microsoft.VSSDK.BuildTools.props`:
```xml
<VSToolsPath>$(ThisPackageDirectory)\tools</VSToolsPath>
<VSSDKBuildToolsAutoSetup Condition=" '$(VSSDKBuildToolsAutoSetup)' == '' ">false</VSSDKBuildToolsAutoSetup>
```
Resolves to `.../tools/vssdk/Microsoft.VSSDK.targets` — exactly the `file-path` in the error. Windows (case-insensitive) finds `Microsoft.VsSDK.targets`; Linux does not. Import fires only because the csproj sets `VSSDKBuildToolsAutoSetup=true` (package default is `false`).

### 5. dependabot-core mapping MSB4019 → `dependency_file_not_found`

`NuGetUpdater.Core/Utilities/MSBuildHelper.cs`:
```csharp
internal static string? GetMissingFile(string output)
{
    var missingFilePatterns = new[]
    {
        new Regex(@"The imported project \""(?<FilePath>.*)\"" was not found"),
        new Regex(@"The imported file \""(?<FilePath>.*)\"" does not exist"),
    };
    ...
}
private static void ThrowOnMissingFile(string output)
{
    var missingFile = GetMissingFile(output);
    if (missingFile is not null) throw new MissingFileException(missingFile);
}
```
`MissingFileException` is reported as job error type `dependency_file_not_found` (`Run/ApiModel/JobErrorBase.cs`).

### 6. Why `src/DataGuard.VisualStudio` passes but `tests/DataGuard.VisualStudio.Tests` fails

Run 36658191140 lines 234-247: the standalone VS project step succeeds and emits 14 `Re-added SDK managed package ... [src/DataGuard.VisualStudio/...]` lines. The VSSDK import lives in the generated `obj/DataGuard.VisualStudio.csproj.nuget.g.targets` (verified locally: it contains `Import Project="$(NuGetPackageRoot)microsoft.vssdk.buildtools\18.5.38461\build\Microsoft.VSSDK.BuildTools.targets"`), which exists only after restore. The standalone step evaluates the project once on a cold clone (no `obj/`), so the import chain is absent. The test project's step evaluates the VS project as a `ProjectReference` after restore has written `nuget.g.targets` → import chain present → MSB4019 at evaluation time. The same error would hit any Linux post-restore evaluation (e.g. `dotnet build`, `msbuild -restore`) of the VS project.

### 7. Upstream known issue

dependabot-core #11522 (open, 2025-02-07, 0 comments):
```
Civ6ModBuddyAlt.csproj(212,3): error MSB4019: The imported project "/home/dependabot/.nuget/packages/microsoft.vssdk.buildtools/17.10.2179/tools/VSSDK/Microsoft.VsSDK.targets" was not found.
```
Same package, same casing class (17.10 used dir `VSSDK`/file `VsSDK`; 18.5 uses dir `vssdk` and asks for `VSSDK.targets`). Microsoft has not shipped a casing-consistent package.

## Hypotheses tested

| # | Hypothesis | Verdict | Why |
|---|---|---|---|
| H1 | Windows-only VSSDK import fails on Linux | **Confirmed** | Error `file-path` is the VSSDK targets; nupkg casing mismatch proven; import gate is `VSSDKBuildToolsAutoSetup` set by repo |
| H2 | Lock-file settings (`bfea5ae`, `RestorePackagesWithLockFile`, locked mode) | Eliminated | Error names a `.targets` file, not a lock file; VS project with identical lock settings restores fine; no `RestoreLockedMode`/`NuGetLockFilePath` in props |
| H3 | `<Properties>` metadata on the ProjectReference (`484e77e`) | Eliminated | First red run (406ab48, 2026-09-23) predates `484e77e` (2026-09-24) and had a plain `<ProjectReference>`; failure identical |
| H4 | `BuildAnalyzers` split (`ce20e3a`) | Eliminated | Commit is 2026-09-29, after first red; target is gated on `CreateVsixContainer=='true'` and runs at build time, whereas MSB4019 is an evaluation-time error |
| H5 | Missing `nuget.config` / `Directory.Packages.props` / `global.json` | Eliminated | None existed in green runs either; log shows `No global.json file found` in both green and red; `Directory.Build.props` unchanged in window |
| H6 | Drift between 4bea2f3 and main 070208b | Eliminated | `git log 4bea2f3..070208b -- src/DataGuard.VisualStudio tests/DataGuard.VisualStudio.Tests DataGuard.sln .github/dependabot.yml Directory.Build.props` is empty |

## Fix proposal

### Option B (recommended): make the VSIX project evaluate cleanly on non-Windows

File: `src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj`

```diff
-    <VSSDKBuildToolsAutoSetup>true</VSSDKBuildToolsAutoSetup>
+    <!-- VSSDK.BuildTools imports tools\vssdk\Microsoft.VSSDK.targets, but the nupkg ships
+         Microsoft.VsSDK.targets; on case-sensitive FS (Dependabot's Linux updater) that is
+         MSB4019 -> dependency_file_not_found (dependabot-core #11522). VSIX packaging is
+         Windows-only anyway, so only auto-import the VSSDK targets there. -->
+    <VSSDKBuildToolsAutoSetup Condition="'$(OS)' == 'Windows_NT'">true</VSSDKBuildToolsAutoSetup>
```

Why it is safe:
- Evaluation order: `nuget.g.props` (BuildTools.props defaults `AutoSetup=false` only if empty) → csproj body → `nuget.g.targets` (line 59 import gated on `=='true'`). On Windows the property is `true` exactly as today; CI VSIX gate (`windows-latest`, `msbuild ... /restore /t:Build /p:CreateVsixContainer=true`) and `dotnet test tests/DataGuard.VisualStudio.Tests` on Windows are unchanged.
- On Linux the import is skipped. Everything else in the csproj that depends on VSSDK targets is inert without them: `BeforeTargets="IncludeAnalyzersInVsix"` / `AfterTargets="GetVsixSourceItems"` / `AfterTargets="CreateVsixContainer"` referencing undefined targets are ignored by MSBuild; `VSCTCompile`, `ProjectCapability` are plain items; `BuildAnalyzers`, `IncludeAnalyzersInVsix`, `PublishDataGuardCli`, `IncludeBundledCliInVsix` are all `Condition="'$(CreateVsixContainer)' == 'true'"` and `CreateVsixContainer` stays empty (its default `true` comes from the skipped VSSDK block).
- The ubuntu `dotnet restore DataGuard.sln --locked-mode` CI steps are restore-only and unaffected.
- Other build imports pulled in post-restore (`Microsoft.VsSDK.CompatibilityAnalyzer.*`, `Microsoft.VisualStudio.SDK.Analyzers.targets`, `Setup.Configuration.Interop.targets`, `Threading.Analyzers.targets`, `System.Text.Json.targets`, `NETFramework.ReferenceAssemblies.net472.targets`) contain no further `<Import>` of package-internal paths (checked in local cache). `Microsoft.VsSDK.CompatibilityAnalyzer.targets` has a `UsingTask AssemblyFile="$(VsSDKCompatibilityAnalyzerAssembly)"`, which is inert until that task is invoked — `Restore`/`ResolveProjectReferences`/`GenerateBuildDependencyFile` do not invoke it.
- Keeps Dependabot coverage for `Microsoft.VisualStudio.SDK`, `Microsoft.VSSDK.BuildTools`, `MessagePack` (CVE pin), `System.Text.Json` in the VSIX project — Option A would silently drop these.

### Option A (fallback if B fails on Linux): scope Dependabot to the cross-platform projects

File: `.github/dependabot.yml` — replace `directory: "/"` in the nuget entry with:
```yaml
    directories:
      - "/src/DataGuard.Analyzers"
      - "/src/DataGuard.Build"
      - "/src/DataGuard.Cli"
      - "/src/DataGuard.CodeFixes"
      - "/src/DataGuard.Contracts"
      - "/src/DataGuard.Core"
      - "/src/DataGuard.Host"
      - "/src/DataGuard.LanguageServer"
      - "/src/DataGuard.MySql.Adapter"
      - "/src/DataGuard.Observability"
      - "/src/DataGuard.Observability.AspNetCore"
      - "/src/DataGuard.Observability.Messaging"
      - "/src/DataGuard.Oracle.Adapter"
      - "/src/DataGuard.PostgreSql.Adapter"
      - "/src/DataGuard.SqlClassification"
      - "/src/DataGuard.SqlServer.Adapter"
      - "/tests/DataGuard.Analyzers.Tests"
      - "/tests/DataGuard.CodeFixes.Tests"
      - "/tests/DataGuard.Core.Tests"
      - "/tests/DataGuard.GoldenCorpus.Tests"
      - "/tests/DataGuard.Observability.Tests"
```
Do **not** use `"/src/*"` globs: they would include `src/DataGuard.VisualStudio` (same failure) and `src/DataGuard.VSCode` (no NuGet manifest → its own `dependency_file_not_found`). Cost: 21 entries to maintain, VSIX packages unmonitored, `Directory.Build.props` packages updated via every project dir. `groups` works across `directories`.

Not viable: `ignore:` (applied after discovery, which is what fails); pointing `directory` at the `.slnf` (unsupported file type); `exclude-paths` (not in the options reference).

### Recurrence prevention (design gap)

CI splits gates by platform via `DataGuard.CrossPlatform.slnf`, so **no Linux job ever evaluates the VS projects post-restore**, while Dependabot expands the full `DataGuard.sln` and ignores the slnf. Any future Windows-only import breaks Dependabot a week later with no earlier signal. Add to the ubuntu `ci.yml` job (after the `dotnet restore DataGuard.sln --locked-mode` step):

```yaml
      - name: VSIX projects must evaluate on Linux (Dependabot parity)
        run: |
          dotnet msbuild src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj -restore -t:ResolveProjectReferences -p:Configuration=Release -p:RestoreLockedMode=true -nologo -v:minimal
          dotnet msbuild tests/DataGuard.VisualStudio.Tests/DataGuard.VisualStudio.Tests.csproj -restore -t:ResolveProjectReferences -p:Configuration=Release -p:RestoreLockedMode=true -nologo -v:minimal
```
`-restore` re-evaluates after restore, which is the path Dependabot hits. The standalone VS project command is the stronger repro: it forces the post-restore re-evaluation that Dependabot's standalone step never performed. `-p:RestoreLockedMode=true` matches the `--locked-mode` restore step above it (`Directory.Build.props` sets `RestorePackagesWithLockFile=true`; an unlocked restore would rewrite `packages.lock.json` in the runner tree and could mask an NU1004). Expected: fails with MSB4019 on `ubuntu-latest` today, passes after Option B. The target choice may need one iteration (`ResolveProjectReferences` is what Dependabot's `ProjectReferenceBuildTargets` runs; the error is evaluation-time, so any target with `-restore` should reproduce it).

## Verification

1. **Local Windows (sanity only):** `dotnet build tests/DataGuard.VisualStudio.Tests/DataGuard.VisualStudio.Tests.csproj -c Release` and the CI VSIX command still pass. Windows cannot reproduce the bug (case-insensitive FS), so this only proves no regression.
2. **Linux (proves the fix):** push the branch — the new ubuntu CI step above is the reproduction. Docker Desktop daemon is not running on this workstation, so no local Linux check was possible.
3. **Dependabot itself:** there is no REST/`gh` endpoint to trigger a Dependabot version-update job. Options: (a) GitHub UI: repo → Insights → Dependency graph → Dependabot → "Recent update jobs" on the nuget entry → "Check for updates"; (b) any push that changes `.github/dependabot.yml` on the default branch triggers an immediate run — evidenced in this repo: `f8adbc3` (touches `dependabot.yml`, 2026-08-19 22:23 UTC) → run 32308651888 at 22:24 UTC; `da4ce6c` (merge of PR #16 containing `9bcf2aa`, which touches `dependabot.yml`, 2026-09-17 02:58 UTC) → off-schedule run 35176401154 at 02:58 UTC. Option A does this by construction; for Option B add a comment-only edit to the file in the same PR; (c) otherwise wait for the weekly schedule (Wednesdays ~02:05 UTC; all scheduled runs 08-26 through 09-30 fell on Wednesdays). Success = job "nuget in /." conclusion `success` and the log contains `Performing single restore ... DataGuard.VisualStudio.Tests.csproj` followed by `Re-added SDK managed package` lines instead of `ERROR`.

## Unresolved questions

1. After Option B, Dependabot enters the update phase for the two VSIX projects on Linux for the first time; a follow-on failure there (e.g. `Microsoft.VisualStudio.SDK` meta-package resolution quirks) cannot be excluded from this evidence. The CI parity step is what would surface it before the weekly run.
2. Whether `-t:ResolveProjectReferences` is the minimal target that reproduces MSB4019 in CI — the error is evaluation-time, so any target with `-restore` should do; confirm on first run.

**Status:** DONE
**Summary:** Dependabot fails because `da24dad` put the VSIX projects into `DataGuard.sln`; Dependabot expands the full sln on Linux and the `Microsoft.VSSDK.BuildTools` import `Microsoft.VSSDK.targets` vs shipped `Microsoft.VsSDK.targets` casing mismatch (gated only by the repo's `VSSDKBuildToolsAutoSetup=true`) yields MSB4019 → `dependency_file_not_found`. One-line fix: `<VSSDKBuildToolsAutoSetup Condition="'$(OS)' == 'Windows_NT'">true</VSSDKBuildToolsAutoSetup>`.
**Concerns/Blockers:** Fix unverified on Linux (no Docker daemon here); recommend the ubuntu CI parity step as both proof and regression gate.
