---
status: completed
---

# Plan: Fix DataGuard Visual Studio Extension — VSIX Assembly Packaging

## Context

The DataGuard Visual Studio extension installs and loads correctly — menus appear under Tools > DataGuard, Options pages work, rules listing works — but **all CLI commands (validate, assess) crash** with `FileNotFoundException: System.Text.Json, Version=10.0.0.11`. The root cause: VSSDK's `GetVsixSourceItems` does not auto-include NuGet `PackageReference` DLLs in the `.vsix` archive for `net472` projects. The built VSIX contains only 5 DLLs (out of 112 in the build output), missing `System.Text.Json.dll` and its transitive dependency chain.

Evidence from `%APPDATA%\DataGuard\logs\dataguard-vs.log`:
```
[2026-09-16 10:43:18.733 UTC] [INFO] [DataGuard] Failed to start assess: Could not load file or assembly 'System.Text.Json, Version=10.0.0.11' ...
[2026-09-16 10:43:25.667 UTC] [INFO] [DataGuard] Failed to start validate: Could not load file or assembly 'System.Text.Json, Version=10.0.0.11' ...
```

## Approach

### Step 1: Add MSBuild target to include required NuGet assemblies in VSIX

Edit `src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj`. Add a `GetVsixSourceItems` target that captures all copy-local NuGet DLLs that VS does not natively provide.

The `RunCliAsync` method JIT-compiles and resolves `System.Text.Json` (used by `PublishSarifAsync` → `JsonDocument.Parse`). The CLR loads from the extension install dir; since `System.Text.Json.dll` isn't there, it throws before `Process.Start` even executes.

Required NuGet packages to include (these are NOT shipped in VS's extension probing path):
- `System.Text.Json` — SARIF parsing via `JsonDocument`
- `System.Text.Encodings.Web` — transitive dep of System.Text.Json (already in VSIX by accident via VSSDK heuristic, but must be explicit)
- `System.Memory` — transitive; Span<T> polyfill for net472
- `System.Buffers` — transitive; ArrayPool backing
- `System.Runtime.CompilerServices.Unsafe` — transitive; Unsafe utilities
- `System.Numerics.Vectors` — transitive; SIMD backing
- `System.Threading.Tasks.Extensions` — transitive; ValueTask polyfill

Target to add before `</Project>`:
```xml
<Target Name="IncludeNuGetDepsInVsix" AfterTargets="GetVsixSourceItems">
  <ItemGroup>
    <VSIXSourceItem Include="@(ReferenceCopyLocalPaths)"
                    Condition="'%(ReferenceCopyLocalPaths.NuGetPackageId)' == 'System.Text.Json'
                            OR '%(ReferenceCopyLocalPaths.NuGetPackageId)' == 'System.Text.Encodings.Web'
                            OR '%(ReferenceCopyLocalPaths.NuGetPackageId)' == 'System.Memory'
                            OR '%(ReferenceCopyLocalPaths.NuGetPackageId)' == 'System.Buffers'
                            OR '%(ReferenceCopyLocalPaths.NuGetPackageId)' == 'System.Numerics.Vectors'
                            OR '%(ReferenceCopyLocalPaths.NuGetPackageId)' == 'System.Runtime.CompilerServices.Unsafe'
                            OR '%(ReferenceCopyLocalPaths.NuGetPackageId)' == 'System.Threading.Tasks.Extensions'" />
  </ItemGroup>
</Target>
```

No existing pattern to reuse; the csproj has no MSBuild targets today. `System.IO.Hashing.dll` and `System.Text.Encodings.Web.dll` already appear in the current VSIX by an VSSDK internal heuristic — the explicit target supersedes and ensures deterministic inclusion.

### Step 2: Add binding redirect for System.Text.Json version unification

VS or other extensions may load an older `System.Text.Json`. The package pins version `10.0.11` (assembly version `10.0.0.11`). Without a binding redirect, assembly load could fail if a transitive reference requests a lower version.

Create `src/DataGuard.VisualStudio/app.config`:
```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <runtime>
    <assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1">
      <dependentAssembly>
        <assemblyIdentity name="System.Text.Json" publicKeyToken="cc7b13ffcd2ddd51" culture="neutral" />
        <bindingRedirect oldVersion="0.0.0.0-10.0.0.11" newVersion="10.0.0.11" />
      </dependentAssembly>
      <dependentAssembly>
        <assemblyIdentity name="System.Text.Encodings.Web" publicKeyToken="cc7b13ffcd2ddd51" culture="neutral" />
        <bindingRedirect oldVersion="0.0.0.0-10.0.0.0" newVersion="10.0.0.0" />
      </dependentAssembly>
      <dependentAssembly>
        <assemblyIdentity name="System.Runtime.CompilerServices.Unsafe" publicKeyToken="b03f5f7f11d50a3a" culture="neutral" />
        <bindingRedirect oldVersion="0.0.0.0-6.0.0.0" newVersion="6.0.0.0" />
      </dependentAssembly>
      <dependentAssembly>
        <assemblyIdentity name="System.Memory" publicKeyToken="cc7b13ffcd2ddd51" culture="neutral" />
        <bindingRedirect oldVersion="0.0.0.0-4.0.1.2" newVersion="4.0.1.2" />
      </dependentAssembly>
    </assemblyBinding>
  </runtime>
</configuration>
```

Exact assembly versions must be confirmed from the DLLs in `bin/Release/net472/` at build time. The `publicKeyToken` values above are the standard Microsoft keys for these packages.

Contingency: if VS 18 (2025) ships `System.Text.Json` in its private assemblies at a different version, a version conflict could occur. The binding redirect handles downward compatibility. If VS ships version > 10.0.0.11, the redirect is harmless (won't match higher versions). If VS ships a lower version and loads it first, the redirect ensures our version wins for our assembly.

### Step 3: Rebuild VSIX and verify contents

Rebuild using MSBuild (not `dotnet build` — `dotnet build` doesn't produce VSIX for old-style VSSDK projects):
```powershell
$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
& $msbuild src\DataGuard.VisualStudio\DataGuard.VisualStudio.csproj /t:Rebuild /p:CreateVsixContainer=true /p:Configuration=Release /restore
```

Verify VSIX contains the required DLLs:
```powershell
python -c "import zipfile; z=zipfile.ZipFile('src/DataGuard.VisualStudio/bin/Release/net472/DataGuard.VisualStudio.vsix'); [print(n) for n in sorted(z.namelist()) if n.endswith('.dll')]"
```

Expected: `System.Text.Json.dll`, `System.Text.Encodings.Web.dll`, `System.Memory.dll`, `System.Buffers.dll`, `System.Runtime.CompilerServices.Unsafe.dll`, `System.Numerics.Vectors.dll`, `System.Threading.Tasks.Extensions.dll` must all appear alongside `DataGuard.VisualStudio.dll`.

### Step 4: Install and test in VS Experimental Instance

```powershell
# Reset experimental instance
& "${env:VSINSTALLDIR}Common7\IDE\devenv.exe" /RootSuffix Exp /ResetSettings
# Install VSIX
& VSIXInstaller.exe /e:Exp src\DataGuard.VisualStudio\bin\Release\net472\DataGuard.VisualStudio.vsix
# Launch experimental instance
& "${env:VSINSTALLDIR}Common7\IDE\devenv.exe" /RootSuffix Exp
```

Or use existing `tools/verify-vs-cli-launch.ps1` which automates isolated deployment and command verification. The script already:
- Creates an isolated RootSuffix (`DataGuardCliVerify`)
- Deploys the VSIX
- Launches devenv via COM automation
- Executes commands and checks Error List

Test scenario (manual fallback): Open a solution → Tools > DataGuard > Run Validation → expect output in DataGuard output pane showing completion message with exit code (not the `System.Text.Json` crash).

### Step 5: Update artifact build script

`scripts/build-extensions.ps1` already uses MSBuild with `/p:CreateVsixContainer=true`. The VSIX content fix is purely in the `.csproj` target — no script changes needed. But the script copies output to `artifacts/visualstudio/`. After rebuild, re-run:
```powershell
.\scripts\build-extensions.ps1
```
Verify artifact: `artifacts/visualstudio/dataguard-visualstudio-*.vsix` must contain `System.Text.Json.dll`.

## Critical files & anchors

| File | Symbol/Region | Reason |
|---|---|---|
| `src/DataGuard.VisualStudio/DataGuard.VisualStudio.csproj` | `</Project>` (line 39) | Add `IncludeNuGetDepsInVsix` target before closing tag |
| `src/DataGuard.VisualStudio/DataGuardPackage.cs` | `PublishSarifAsync` (line 447) | Consumer of `System.Text.Json`; JIT triggers the crash in `RunCliAsync` |
| `src/DataGuard.VisualStudio/app.config` | NEW FILE | Binding redirects for version unification |

## Verification

1. **VSIX content check** (automated, no VS needed):
   ```
   python -c "import zipfile; z=zipfile.ZipFile('src/DataGuard.VisualStudio/bin/Release/net472/DataGuard.VisualStudio.vsix'); names=z.namelist(); assert 'System.Text.Json.dll' in names, f'MISSING System.Text.Json.dll; got {[n for n in names if n.endswith(\".dll\")]}'"
   ```
   Expected: no assertion error.

2. **Functional test** (requires VS): Open VS Experimental Instance with extension installed → Tools > DataGuard > Assess Workspace on any solution → Output pane shows `[DataGuard] assess completed in ... ms with exit code ...` instead of `Failed to start assess: Could not load file or assembly 'System.Text.Json'`.

3. **Log verification**: Check `%APPDATA%\DataGuard\logs\dataguard-vs.log` — no `System.Text.Json` `FileNotFoundException` entries after the fix timestamp.

## Assumptions & contingencies

- **Binding redirect exact versions**: The assembly versions in `app.config` are based on `System.Text.Json 10.0.11` package. If the actual DLL assembly version differs, implementer must inspect `System.Text.Json.dll` in the build output (`ildasm /metadata` or `[System.Reflection.AssemblyName]::GetAssemblyName(path).Version`) and adjust `newVersion` accordingly.
- **VS 18 assembly conflicts**: If VS 2025 (v18) loads a different `System.Text.Json` version first into the AppDomain, a `FileLoadException` could replace the `FileNotFoundException`. Binding redirect resolves this. If it persists, fallback: remove `System.Text.Json` dependency entirely and parse SARIF with `Newtonsoft.Json` (already in VS's probing path via `Newtonsoft.Json.dll`).
