# Extension Build Guide

## Overview

Build the VS Code and Visual Studio installation packages locally, then stage the signed-by-hash outputs in `artifacts/` for manual testing. This guide does not publish to either marketplace.

## Prerequisites

- .NET SDK capable of building the repository projects.
- Node.js and npm.
- Visual Studio with MSBuild and the Visual Studio SDK workload, so `vswhere.exe` can locate `MSBuild.exe`.
- Run every command from the repository root: `D:\100.Software\Github\eco_support_net_oracle`.

## VS Code VSIX

```powershell
Set-Location D:\100.Software\Github\eco_support_net_oracle\src\DataGuard.VSCode
npm ci
npm test
npm run package
```

`npm run package` first publishes the Release language server into `server/` with its `manifest.json` integrity hash, then creates the VSIX.

Stage the package and checksum:

```powershell
Set-Location D:\100.Software\Github\eco_support_net_oracle
$version = (Get-Content src\DataGuard.VSCode\package.json -Raw | ConvertFrom-Json).version
$source = "src\DataGuard.VSCode\dataguard-vscode-$version.vsix"
$destination = "artifacts\vscode\dataguard-vscode-$version.vsix"

New-Item -ItemType Directory -Force artifacts\vscode | Out-Null
Copy-Item -LiteralPath $source -Destination $destination -Force
$hash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$destination.sha256" -Value "$hash  $(Split-Path -Leaf $destination)" -NoNewline
```

### Installing the VS Code Package

> [!IMPORTANT]
> **Do NOT double-click the VS Code `.vsix` file in Windows Explorer.** Windows associates `.vsix` files with Visual Studio's installer (`VSIXInstaller.exe`), which strictly rejects VS Code extensions with `VSIXInstaller.NoApplicableSKUsException`.

Install using either of the following methods:
1. **Command Line (CLI)**:
   ```powershell
   code --install-extension artifacts/vscode/dataguard-vscode-<version>.vsix
   ```
2. **VS Code GUI**: Open VS Code -> `Ctrl + Shift + X` (Extensions) -> click the `...` menu (Views and More Actions) -> select **Install from VSIX...**, select the file, and reload the window.

## Visual Studio VSIX

Use the Visual Studio MSBuild selected by `vswhere`; do not substitute `dotnet build` for the release-style VSIX build.

```powershell
Set-Location D:\100.Software\Github\eco_support_net_oracle
$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($msbuild)) { throw "MSBuild.exe was not found by vswhere." }

& $msbuild src\DataGuard.VisualStudio\DataGuard.VisualStudio.csproj `
  /t:Rebuild `
  /p:CreateVsixContainer=true `
  /p:Configuration=Release `
  /restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
```

Stage the package and checksum:

```powershell
$version = (Get-Content src\DataGuard.VSCode\package.json -Raw | ConvertFrom-Json).version
$source = "src\DataGuard.VisualStudio\bin\Release\net472\DataGuard.VisualStudio.vsix"
$destination = "artifacts\visualstudio\dataguard-visualstudio-$version.vsix"

New-Item -ItemType Directory -Force artifacts\visualstudio | Out-Null
Copy-Item -LiteralPath $source -Destination $destination -Force
$hash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$destination.sha256" -Value "$hash  $(Split-Path -Leaf $destination)" -NoNewline
```

Install with **Extensions > Manage Extensions > Install from VSIX**, then restart Visual Studio.

## Expected Outputs

```text
artifacts/vscode/dataguard-vscode-<version>.vsix
artifacts/vscode/dataguard-vscode-<version>.vsix.sha256
artifacts/visualstudio/dataguard-visualstudio-<version>.vsix
artifacts/visualstudio/dataguard-visualstudio-<version>.vsix.sha256
```

## Troubleshooting

| Symptom | Resolution |
| --- | --- |
| `VSIXInstaller.NoApplicableSKUsException` when installing VS Code extension | Caused by double-clicking the file in Windows, opening Visual Studio VSIX Installer. Install via CLI (`code --install-extension artifacts/vscode/...`) or via VS Code menu **Install from VSIX...**. |
| `VSIXInstaller.NoApplicableSKUsException` when installing Visual Studio extension | Verify the manifest `source.extension.vsixmanifest` targets your edition (`Community`, `Professional`, `Enterprise`) and version range (e.g. `[17.0,19.0)` for VS 2022 and VS 2026). |
| Script invocation path loses backslash (`scriptsbuild-extensions.ps1`) | Shell/bash eats backslashes. Use forward slashes (`scripts/build-extensions.ps1`) or quote the path (`"-File 'scripts\build-extensions.ps1'"`). |
| `vswhere.exe was not found` | Install Visual Studio or Build Tools with the MSBuild and Visual Studio SDK workloads. |
| `MSBuild.exe was not found by vswhere` | Add the MSBuild component through Visual Studio Installer, then retry. |
| VSIX is blocked during installation | Close the target IDE and verify the `.sha256` value before retrying. |
| VS Code package lacks the language server | Run `npm run package`, not `npx vsce package`; the npm script runs `prepare-lsp`. |
