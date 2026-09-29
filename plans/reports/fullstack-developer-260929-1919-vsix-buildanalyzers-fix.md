# Fix: VSIX packaging CS0518 — split Restore/Build in `BuildAnalyzers`

Date: 2026-09-29 | Branch: feat/vs-extension-hardening @ 8a2df13 (uncommitted) | RCA: `plans/reports/debugger-260929-1905-vsix-packaging-ci-failure.md`

## Change

Single file, per ownership: `src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj`. Each `<MSBuild Targets="Restore;Build">` in `BuildAnalyzers` split into a Restore call (unique `MSBuildRestoreSessionId` global so its evaluation is not reused) followed by a Build call with the normal properties. No `RemoveProperties`, no workflow change, no lock-file change. Comment updated with the why. LF endings, no BOM (verified before/after: first bytes `3c 50 72`, zero CR, trailing `0a`).

```diff
diff --git a/src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj b/src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj
index f514766..69aa97e 100644
--- a/src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj
+++ b/src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj
@@ -63,16 +63,30 @@
     BuildAnalyzers: compile DataGuard.Analyzers and DataGuard.CodeFixes
     so they can be included as VSIXSourceItem without a ProjectReference (which would violate
     the zero-ProjectReference VSIX architecture).
+
+    Restore and Build must be separate MSBuild task invocations with different global properties.
+    A single Targets="Restore;Build" call evaluates the project instance once, before Restore writes
+    obj\*.nuget.g.props/targets, so NETStandard.Library.targets (netstandard.dll + facades) is never
+    imported and csc fails with CS0518/CS0234/CS0653 on a clean checkout (every CI run). Local builds
+    only pass because obj\ is already warm. The unique MSBuildRestoreSessionId forces a fresh
+    evaluation for Restore, mirroring what `msbuild -restore` does internally; the Build call then
+    evaluates again with the normal properties and sees the generated imports.
   -->
   <Target Name="BuildAnalyzers"
           Condition="'$(CreateVsixContainer)' == 'true'"
           BeforeTargets="IncludeAnalyzersInVsix">
+    <MSBuild Projects="$(MSBuildThisFileDirectory)..\DataGuard.Analyzers\DataGuard.Analyzers.csproj"
+             Properties="Configuration=$(Configuration);MSBuildRestoreSessionId=$([System.Guid]::NewGuid())"
+             Targets="Restore" />
     <MSBuild Projects="$(MSBuildThisFileDirectory)..\DataGuard.Analyzers\DataGuard.Analyzers.csproj"
              Properties="Configuration=$(Configuration)"
-             Targets="Restore;Build" />
+             Targets="Build" />
+    <MSBuild Projects="$(MSBuildThisFileDirectory)..\DataGuard.CodeFixes\DataGuard.CodeFixes.csproj"
+             Properties="Configuration=$(Configuration);MSBuildRestoreSessionId=$([System.Guid]::NewGuid())"
+             Targets="Restore" />
     <MSBuild Projects="$(MSBuildThisFileDirectory)..\DataGuard.CodeFixes\DataGuard.CodeFixes.csproj"
              Properties="Configuration=$(Configuration)"
-             Targets="Restore;Build" />
+             Targets="Build" />
   </Target>
```

## Verification method

Live working tree not disturbed (other agents building in it). Scratch copy = `git archive HEAD | tar -x` (same content CI checks out; excludes the other agent's uncommitted `.cs` edits) at `<scratch>` = `C:\Users\thant\AppData\Local\Temp\claude\D--100-Software-Github-eco-support-net-oracle\272bdb39-e615-4af1-b161-265b271d9d26\scratchpad\vsix-repro`. Every `src/**/obj` deleted before each run (0 remaining, checked). Command = exact ci.yml gate line, run through PowerShell so switches stay literal; toolchain = `C:\Program Files\Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\MSBuild.exe` (VS 18 Enterprise, same as runner), dotnet SDK 10.0.401 on PATH.

```
& MSBuild.exe src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj /restore /t:Build /p:Configuration=Release /p:CreateVsixContainer=true /p:DeployExtension=false /m /nologo /v:minimal
```

### RED — original csproj (HEAD), clean obj → EXIT=1

Log: `<scratchpad>\vsix-repro-red.log` (364 lines). Error histogram: 241 x CS0518, 71 x CS0012, 22 x CS0234, 10 x CS0246, 1 x CS0619, 1 x CS0653 — identical shape to CI job 109324199830 and RCA repro A. Markers:

```
  Determining projects to restore...
  Restored <scratch>\src\DataGuard.VisualStudio\DataGuard.VisualStudio.csproj (in 3.53 sec).
  Determining projects to restore...
  Restored <scratch>\src\DataGuard.SqlClassification\DataGuard.SqlClassification.csproj (in 169 ms).
  Restored <scratch>\src\DataGuard.Contracts\DataGuard.Contracts.csproj (in 169 ms).
  Restored <scratch>\src\DataGuard.Analyzers\DataGuard.Analyzers.csproj (in 191 ms).
  DataGuard.Contracts -> <scratch>\src\DataGuard.Contracts\bin\Release\netstandard2.0\DataGuard.Contracts.dll
  DataGuard.SqlClassification -> <scratch>\src\DataGuard.SqlClassification\bin\Release\netstandard2.0\DataGuard.SqlClassification.dll
<scratch>\src\DataGuard.Analyzers\Analyzers.cs(468,24): error CS0518: Predefined type 'System.String' is not defined or imported [<scratch>\src\DataGuard.Analyzers\DataGuard.Analyzers.csproj]
<scratch>\src\DataGuard.Analyzers\Analyzers.cs(502,2): error CS0653: Cannot apply attribute class 'DiagnosticAnalyzer' because it is abstract [<scratch>\src\DataGuard.Analyzers\DataGuard.Analyzers.csproj]
<scratch>\src\DataGuard.Analyzers\Analyzers.cs(363,44): error CS0619: 'ReadOnlySpan<char>' is obsolete: 'Types with embedded references are not supported in this version of your compiler.' [<scratch>\src\DataGuard.Analyzers\DataGuard.Analyzers.csproj]
```

Last 5 lines verbatim (path-shortened):

```
<scratch>\src\DataGuard.Analyzers\Analyzers.cs(531,26): error CS0518: Predefined type 'System.Void' is not defined or imported [<scratch>\src\DataGuard.Analyzers\DataGuard.Analyzers.csproj]
<scratch>\src\DataGuard.Analyzers\Analyzers.cs(505,29): error CS0012: The type 'Object' is defined in an assembly that is not referenced. You must add a reference to assembly 'netstandard, Version=2.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51'. [<scratch>\src\DataGuard.Analyzers\DataGuard.Analyzers.csproj]
<scratch>\src\DataGuard.Analyzers\Analyzers.cs(505,29): error CS0518: Predefined type 'System.Object' is not defined or imported [<scratch>\src\DataGuard.Analyzers\DataGuard.Analyzers.csproj]
<scratch>\src\DataGuard.Analyzers\Analyzers.cs(806,48): error CS0012: The type 'IEquatable<>' is defined in an assembly that is not referenced. You must add a reference to assembly 'netstandard, Version=2.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51'. [<scratch>\src\DataGuard.Analyzers\DataGuard.Analyzers.csproj]
<scratch>\src\DataGuard.Analyzers\Analyzers.cs(863,96): error CS0012: The type 'IEquatable<>' is defined in an assembly that is not referenced. You must add a reference to assembly 'netstandard, Version=2.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51'. [<scratch>\src\DataGuard.Analyzers\DataGuard.Analyzers.csproj]
EXIT=1
```

### GREEN — fixed csproj overlaid, obj AND bin wiped again first (red run had warmed obj) → EXIT=0

Log: `<scratchpad>\vsix-repro-green.log` (74 lines, 0 errors; only MinVer/SourceLink "not a git working directory" warnings, expected because `git archive` output has no `.git`). Both analyzer projects restore then build in separate evaluations:

```
  Determining projects to restore...
  Restored <scratch>\src\DataGuard.Contracts\DataGuard.Contracts.csproj (in 186 ms).
  Restored <scratch>\src\DataGuard.SqlClassification\DataGuard.SqlClassification.csproj (in 186 ms).
  Restored <scratch>\src\DataGuard.Analyzers\DataGuard.Analyzers.csproj (in 211 ms).
  DataGuard.Contracts -> <scratch>\src\DataGuard.Contracts\bin\Release\netstandard2.0\DataGuard.Contracts.dll
  DataGuard.SqlClassification -> <scratch>\src\DataGuard.SqlClassification\bin\Release\netstandard2.0\DataGuard.SqlClassification.dll
  DataGuard.Analyzers -> <scratch>\src\DataGuard.Analyzers\bin\Release\netstandard2.0\DataGuard.Analyzers.dll
  Determining projects to restore...
  Restored <scratch>\src\DataGuard.CodeFixes\DataGuard.CodeFixes.csproj (in 1.55 sec).
  DataGuard.CodeFixes -> <scratch>\src\DataGuard.CodeFixes\bin\Release\netstandard2.0\DataGuard.CodeFixes.dll
```

Last lines verbatim (path-shortened):

```
    DataGuard.SqlServer.Adapter -> <scratch>\src\DataGuard.SqlServer.Adapter\bin\Release\net9.0\DataGuard.SqlServer.Adapter.dll
    DataGuard.Oracle.Adapter -> <scratch>\src\DataGuard.Oracle.Adapter\bin\Release\net9.0\DataGuard.Oracle.Adapter.dll
    DataGuard.PostgreSql.Adapter -> <scratch>\src\DataGuard.PostgreSql.Adapter\bin\Release\net9.0\DataGuard.PostgreSql.Adapter.dll
    DataGuard.MySql.Adapter -> <scratch>\src\DataGuard.MySql.Adapter\bin\Release\net9.0\DataGuard.MySql.Adapter.dll
    DataGuard.Cli -> <scratch>\src\DataGuard.Cli\bin\Release\net9.0\win-x64\dataguard.dll
    DataGuard.Cli -> <scratch>\src\DataGuard.VisualStudio\obj\cli\
  DataGuard.VisualStudio -> <scratch>\src\DataGuard.VisualStudio\bin\Release\net472\DataGuard.VisualStudio.vsix
EXIT=0
```

### assert-vsix (ci.yml "Assert VSIX contents" step, same script)

```
PS> ./scripts/assert-vsix.ps1 -VsixPath <scratch>\src\DataGuard.VisualStudio\bin\Release\net472\DataGuard.VisualStudio.vsix -ExpectedVersion 0.2.3
assert-vsix: OK - DataGuard.VisualStudio.vsix (48.1 MB), version 0.2.3, 22 entries, all 6 required entries present
ASSERT_EXIT=0
```

### Real tree

- `dotnet format whitespace DataGuard.sln --verify-no-changes --no-restore` → exit 0 (only the usual "warnings while loading workspace" notice). `--no-restore` added so format could not rewrite lock files; md5 of all tracked `packages.lock.json` identical before/after.
- `git status` for owned file: only `src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj` modified by this task (+16/-2). Nothing committed.

## Docs

RCA asked to update any doc describing the `Restore;Build` shape. Grep of `docs/`, `CHANGELOG.md`, `README*.md`, `AGENTS.md`, `rules/`: the only hits are `docs/03-components/tooling/visual-studio-extension.md:244` and its `.vi.md` twin, which name `BuildAnalyzers` but do not describe the Restore/Build mechanics — no edit required, and both are outside this task's ownership. A CHANGELOG line for the fix is left to the committer (out of ownership).

## Follow-ups (not in scope)

1. RCA recurrence item: add a `scripts/check-workflow-policy.py` rule rejecting `Targets="Restore;` in `*.csproj`/`*.targets`.
2. `main` carries the same bug since 9f82f3f; merge PR #24 or cherry-pick this csproj hunk.
3. release.yml `visual-studio-package` shares the target; confirm on next tag.

**Status:** DONE
**Summary:** Split `BuildAnalyzers` into Restore (unique `MSBuildRestoreSessionId`) + Build calls for both analyzer projects, exactly per RCA; red/green reproduced from a clean `git archive HEAD` copy with the exact ci.yml command and VS 18 MSBuild (241 x CS0518 before, VSIX + `assert-vsix: OK ... version 0.2.3` after); `dotnet format` clean, lock files untouched, csproj stays LF/no-BOM.
**Concerns/Blockers:** None. Not committed per instructions; CHANGELOG entry and the policy-check rule are outside ownership.
