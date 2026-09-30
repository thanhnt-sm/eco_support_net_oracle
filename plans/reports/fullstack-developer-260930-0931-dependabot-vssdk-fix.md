# Dependabot VSSDK fix: Windows-only `VSSDKBuildToolsAutoSetup` + Linux evaluation parity gate

Date: 2026-09-30 | Repo: eco_support_net_oracle | Branch: `fix/dependabot-vssdk-linux-evaluation` (from `origin/main` @ 070208b) | Commit: `c5df504` (not pushed) | Author: fullstack-developer
Input: `plans/reports/debugger-260930-0904-dependabot-nuget-failure.md` (Option B + recurrence-prevention step)

## Executed Phase
Single-phase fix, status **completed with two scoped deviations** (see Deviations).

## Files Modified (committed by explicit path, hooks ran and passed)
- `src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj` — gate + 2-line comment
- `.github/workflows/ci.yml` — new step in ubuntu `build-and-test` after "Restore dependencies"
- `CHANGELOG.md` — `[Unreleased] → Fixed` entry (first bullet)
- `.github/dependabot.yml` — untouched (Option B keeps Dependabot coverage of the VSIX projects)
- No lock-file edits. The 9 `M packages.lock.json` in the working tree pre-date this task and are CRLF-only (`git diff --stat` empty); left unstaged.

## Diff (csproj + ci.yml)
```diff
diff --git a/.github/workflows/ci.yml b/.github/workflows/ci.yml
--- a/.github/workflows/ci.yml
+++ b/.github/workflows/ci.yml
@@ -56,6 +56,14 @@ jobs:
       - name: Restore dependencies
         run: dotnet restore ${{ env.CROSS_PLATFORM_SOLUTION }} --locked-mode
 
+      # Dependabot expands the full DataGuard.sln on Linux (it ignores the slnf) and evaluates the
+      # VSIX project post-restore through this ProjectReference; no other Linux job does. A Windows-only
+      # import in the VSIX csproj (MSB4019, e.g. the mis-cased VSSDK targets) must fail here, not a week
+      # later in the Dependabot job. Evaluation only (BuildProjectReferences=false: the referenced project
+      # is evaluated, not compiled); locked mode keeps the committed lock files intact.
+      - name: Linux evaluation parity (Dependabot)
+        run: dotnet msbuild tests/DataGuard.VisualStudio.Tests/DataGuard.VisualStudio.Tests.csproj -restore -t:ResolveProjectReferences -p:Configuration=Release -p:RestoreLockedMode=true -p:BuildProjectReferences=false -nologo -v:minimal
+
       - name: Build solution
         run: dotnet build ${{ env.CROSS_PLATFORM_SOLUTION }} --configuration Release --no-restore
 
diff --git a/src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj b/src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj
--- a/src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj
+++ b/src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj
@@ -9,7 +9,9 @@
     <IncludeAssemblyInVSIXContainer>true</IncludeAssemblyInVSIXContainer>
     <IncludeDebugSymbolsInVSIXContainer>true</IncludeDebugSymbolsInVSIXContainer>
     <CopyBuildOutputToOutputDirectory>true</CopyBuildOutputToOutputDirectory>
-    <VSSDKBuildToolsAutoSetup>true</VSSDKBuildToolsAutoSetup>
+    <!-- Windows-only: Microsoft.VSSDK.BuildTools imports tools/vssdk/Microsoft.VSSDK.targets but ships
+         Microsoft.VsSDK.targets, so a Linux evaluation (Dependabot) fails with MSB4019 if this is unconditional. -->
+    <VSSDKBuildToolsAutoSetup Condition="'$(OS)' == 'Windows_NT'">true</VSSDKBuildToolsAutoSetup>
     <VsixDeployOnDebug>true</VsixDeployOnDebug>
     <DeployExtension Condition="'$(DeployExtension)' == ''">false</DeployExtension>
   </PropertyGroup>
```

## VSSDK-dependent targets in the csproj (post-fix line numbers) — behaviour when the VSSDK targets are NOT imported
Claim under test: with `VSSDKBuildToolsAutoSetup` false on Linux, `Microsoft.VsSDK.targets` is not imported, `CreateVsixContainer` stays empty (its `true` default is `Microsoft.VsSDK.targets:85`), and nothing in the csproj can fire.

| Line | Element (quoted) | Why inert without VSSDK targets |
|---|---|---|
| 29 | `<ProjectCapability Include="CreateVsixContainer" />` | plain item |
| 38-40 | `<VSCTCompile Include="Commands\Menus.vsct">` | plain item; the `VSCTCompile` target lives in VSSDK targets |
| 44 | `<Target Name="IncludeNuGetDepsInVsix" AfterTargets="GetVsixSourceItems">` | **not** gated on `CreateVsixContainer`; hangs off undefined `GetVsixSourceItems` (MSBuild ignores Before/AfterTargets that name undefined targets) |
| 77-79 | `<Target Name="BuildAnalyzers" Condition="'$(CreateVsixContainer)' == 'true'" BeforeTargets="IncludeAnalyzersInVsix">` | gated; hangs off an own-file target that is itself gated |
| 94-97 | `<Target Name="IncludeAnalyzersInVsix" Condition="'$(CreateVsixContainer)' == 'true'" BeforeTargets="GetVsixSourceItems" DependsOnTargets="BuildAnalyzers">` | gated + undefined `GetVsixSourceItems` |
| 105-107 | `<Target Name="PublishDataGuardCli" Condition="'$(CreateVsixContainer)' == 'true'" BeforeTargets="IncludeBundledCliInVsix">` | gated; the only place `NuGetLockFilePath=obj\cli-publish.packages.lock.json` appears (inside its `Exec`), so it never touches the plain restore |
| 115-118 | `<Target Name="IncludeBundledCliInVsix" Condition="'$(CreateVsixContainer)' == 'true'" BeforeTargets="GetVsixSourceItems" DependsOnTargets="PublishDataGuardCli">` | gated + undefined `GetVsixSourceItems` |
| 128 | `<Target Name="CleanBundledCli" AfterTargets="Clean">` | **not** gated and `Clean` exists without VSSDK — harmless (`RemoveDir` under `Exists(...)`, only on `Clean`) |
| 132 | `<Target Name="CleanBundledCliAfterVsixPackaging" AfterTargets="CreateVsixContainer" Condition="'$(CreateVsixContainer)' == 'true'">` | gated + undefined `CreateVsixContainer` target |

So the report's claim holds with two precisions: line 44 is not gated (undefined AfterTargets only) and line 128 hangs off a real target but is harmless.

Package chain (local cache `microsoft.vssdk.buildtools/18.5.38461`):
- `build/Microsoft.VSSDK.BuildTools.props:11` `<VSSDKBuildToolsAutoSetup Condition=" '$(VSSDKBuildToolsAutoSetup)' == '' ">false</VSSDKBuildToolsAutoSetup>`
- `build/Microsoft.VSSDK.BuildTools.targets:59` `<Import Project="$(VSToolsPath)\vssdk\Microsoft.VSSDK.targets" Condition=" '$(VsSdkBuildTargetsImported)' != 'true' AND '$(VSSDKBuildToolsAutoSetup)' == 'true' " />`
- `tools/vssdk/` contains `Microsoft.VsSDK.targets` (no `Microsoft.VSSDK.targets`).
`nuget.g.props` (BuildTools.props) is imported before the csproj body, so on Windows the csproj still wins with `true`; on Linux the csproj element is skipped and the package default `false` stands.

## Lock-file / bundled-CLI question (task item 2)
- Plain restore of the VS csproj uses the committed `src/DataGuard.VisualStudio/packages.lock.json`: `-getProperty:NuGetLockFilePath` → `""` before and after the fix. `obj\cli-publish.packages.lock.json` is passed only to the nested `dotnet publish` inside `PublishDataGuardCli`.
- `PublishDataGuardCli` does not fire during `ResolveProjectReferences`: `Condition="'$(CreateVsixContainer)' == 'true'"` (empty on Linux) and reachable only via `IncludeBundledCliInVsix → GetVsixSourceItems` (undefined). Empirical: after the parity command with `-p:OS=Unix`, `src/DataGuard.VisualStudio/obj/cli/` does not exist and `git diff --stat` is unchanged. **No `-p:PublishBundledCli=false` needed.**
- `RestoreLockedMode=true` rewrote no committed lock file locally (`git diff --stat` identical before/after every run). Locked-mode restore of both VSIX projects on ubuntu is already proven viable by the `security-scan` and `generate-sbom` jobs (`dotnet restore DataGuard.sln --locked-mode`).

## Windows simulation (task item 3c)
`-p:OS=Unix` does override `$(OS)` under `dotnet msbuild` (`-getProperty:OS` → `Unix`).

| csproj | Command | `VSSDKBuildToolsAutoSetup` | `CreateVsixContainer` | `VsSdkBuildTargetsImported` | exit |
|---|---|---|---|---|---|
| origin/main (unfixed) | VS project `-restore -t:ResolveProjectReferences -p:Configuration=Release -p:RestoreLockedMode=true -p:OS=Unix` | `true` | `true` | `true` | 0 |
| fixed | same | `false` | `""` | `""` | 0 |
| fixed | Tests project: CI parity command + `-p:OS=Unix` | n/a | n/a | n/a | 0 ("All projects are up-to-date for restore", no compile) |

- With the fix the gate flips as designed (import skipped, VSIX defaults absent).
- **MSB4019 cannot be reproduced on Windows**: the unfixed row shows `VsSdkBuildTargetsImported=true`, exit 0 — NTFS resolves `Microsoft.VSSDK.targets` to the shipped `Microsoft.VsSDK.targets` case-insensitively. No Docker daemon / WSL distro with dotnet on this workstation. The ubuntu CI step is the proof, per the task's fallback clause.
- **Gate sensitivity proven another way (post-restore-only fault, cold `obj/`)**: deleted `src/DataGuard.VisualStudio/obj` and `tests/DataGuard.VisualStudio.Tests/obj` (cold-clone simulation), temporarily appended `<Import Project="$(MSBuildThisFileDirectory)does-not-exist-post-restore.targets" Condition="'$(NuGetPackageRoot)' != ''" />` to the VS csproj (`NuGetPackageRoot` is defined only by `nuget.g.props`, i.e. only after restore — same shape as the real `nuget.g.targets → BuildTools.targets → VSSDK` chain, invisible to the restore-phase evaluation), and ran the exact CI parity command. Output: `Restored ...Tests.csproj`, `Restored ...VisualStudio.csproj`, then `DataGuard.VisualStudio.csproj(135,3): error MSB4019: The imported project "...does-not-exist-post-restore.targets" was not found`, exit 1. So with `BuildProjectReferences=false` the VS project is still re-evaluated post-restore through the Tests ProjectReference — the Dependabot failure path (debugger report §6). Fixed csproj restored afterwards; `git diff --stat` empty (working tree == HEAD `c5df504`). An earlier run with an unconditional missing import also failed (exit 1) but was phase-ambiguous; this gated variant is the one that counts.

## Windows regression checks (task items 3a, 3b)
- `dotnet build src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj -c Release` → 0 warnings, 0 errors (81 s; full VSIX path incl. bundled-CLI publish, unchanged).
- `dotnet test tests/DataGuard.VisualStudio.Tests -c Release` → **Passed 155, Failed 0, Skipped 1** (156 total, net472).
- VS 18 MSBuild `-restore -t:Build -p:Configuration=Release -p:CreateVsixContainer=true -p:DeployExtension=false -m:1` (after `rm -rf bin/Release`) → `bin/Release/net472/DataGuard.VisualStudio.vsix` produced; `scripts/assert-vsix.ps1` → `assert-vsix: OK - DataGuard.VisualStudio.vsix (48.1 MB), version 0.2.3, 22 entries, all 6 required entries present`.

## Static gates (task item 4)
- `python scripts/check-workflow-policy.py` → `OK (VSIX assert, fork-PR upload guard, release VS tests)`
- `python -m unittest discover -s scripts/tests` → `Ran 18 tests ... OK`
- `actionlint .github/workflows/ci.yml` → clean (exit 0)
- YAML parse (PyYAML) → OK; new step sits between "Restore dependencies" and "Build solution"
- `dotnet format whitespace DataGuard.sln --verify-no-changes` → exit 0 (also re-run by the pre-commit hook)
- LF endings: `ci.yml` and the csproj have 0 CR bytes, `git ls-files --eol` → `i/lf w/lf`. `CHANGELOG.md` is `i/lf w/crlf attr eol=lf` (pre-existing working-tree state); the new line kept CRLF to match its neighbours; git normalised to LF on add.
- Pre-commit hook (`dotnet format whitespace`, `anti_garbage_guard.sh`, `verify_docs_sync.sh`) passed; not bypassed.

## Deviations from the task text
1. **Parity step runs ONE command (Tests project), not two.** `scripts/check-workflow-policy.py` rule (a) is a substring test (`is_vs_msbuild_step`: `"DataGuard.VisualStudio.csproj" in run text and no "uses"`) that demands an effective `scripts/assert-vsix.ps1` step later in the same job. The standalone `dotnet msbuild src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj ...` line fails that rule, and the policy check is the *first* step of `build-and-test`, so the whole job would go red before the gate ran. `DataGuard.VisualStudio.Tests.csproj` does not contain the substring. I did not dodge the rule by hiding the path (env var / glob / directory argument). The Tests-project command is the literal Dependabot failure path (VS project evaluated post-restore as a ProjectReference) and was shown above to catch MSB4019.
   - Gap this leaves: the Tests ProjectReference passes `BuildingForTesting=true;CreateVsixContainer=false;DeployExtension=false;GeneratePkgDefFile=false` into the VS project, so a future Windows-only import conditioned on those properties would evade the gate; the standalone command would catch it. To add it, `scripts/check-workflow-policy.py` (outside my ownership) needs a one-line narrowing of `is_vs_msbuild_step` — e.g. exempt steps whose text contains `-t:ResolveProjectReferences`, or require `/t:Build` / `CreateVsixContainer=true` — plus a fixture in `scripts/tests/test_check_workflow_policy.py`.
2. **Added `-p:BuildProjectReferences=false`.** Without it, `ResolveProjectReferences` on the Tests project *compiles* the VS project (`DataGuard.VisualStudio -> ...\DataGuard.VisualStudio.dll` appeared in the first run), which would make the ubuntu job build a net472 VSIX project on Linux for the first time — a broader, untested surface than "evaluation parity" and a source of unrelated failures. With the flag the referenced project is still fully evaluated post-restore (`GetTargetFrameworks` / `GetTargetPath` via the MSBuild task), which is where MSB4019 surfaces; proven above with the cold-`obj/`, post-restore-only injected import (exit 1). Observational: the ubuntu job will download the VS SDK package graph on the first NuGet cache miss (the cache key already hashes `**/*.csproj` and `**/packages.lock.json`).

## Not verified here
- The actual Linux run: the first CI run of this branch on `ubuntu-latest` proves (a) the step passes with the fix and (b) locked-mode `-restore` of the two VSIX projects behaves on Linux as the `.sln` locked restore already does. Debugger report unresolved question 1 (Dependabot's *update* phase for the VSIX projects on Linux) stays open until the next Dependabot run (Wednesday ~02:05 UTC, or "Check for updates" in the UI).
- `-t:ResolveProjectReferences` as minimal target: sufficient locally (injected-import test); no iteration needed.

## Unresolved questions
1. Does the lead want the standalone VS-project command too (needs `scripts/check-workflow-policy.py` + test fixture change, outside this ownership)?
2. Should a comment-only edit to `.github/dependabot.yml` be added to trigger an immediate Dependabot run on merge (debugger report Verification §3b)? Not done — `dependabot.yml` was "only if needed".

**Status:** DONE_WITH_CONCERNS
**Summary:** `VSSDKBuildToolsAutoSetup` is now Windows-only in the VSIX csproj; ubuntu CI gained a locked-mode, evaluation-only "Linux evaluation parity (Dependabot)" step; Windows build / tests (155 + 1 skip) / VSIX packaging + assert-vsix all green; committed as `c5df504` on `fix/dependabot-vssdk-linux-evaluation`, not pushed.
**Concerns/Blockers:** (1) parity step runs only the Tests-project command because the workflow-policy substring rule rejects the standalone VS-project command — narrowing that rule is outside my ownership; (2) added `-p:BuildProjectReferences=false` so the gate evaluates rather than compiles the VSIX project on Linux; (3) MSB4019 itself is unreproducible on Windows — the first ubuntu CI run is the proof.
