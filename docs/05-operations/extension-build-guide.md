# Extension Build Guide

## Overview

Build the VS Code and Visual Studio installation packages locally, then stage the packages and their SHA-256 checksums in `artifacts/` for manual testing. This guide does not publish to either marketplace.

A SHA-256 checksum is not a digital signature. A local build does not establish Marketplace availability or editor acceptance. VS Code and its locked language client both declare `^1.91.0`; the source metadata mismatch is resolved, while installed-editor acceptance remains pending. See [release gates](../marketplace-publishing.md).

## Prerequisites

- .NET 9 SDK selected by `global.json`.
- Node 22 and npm.
- Visual Studio with MSBuild and the Visual Studio SDK workload, so `vswhere.exe` can locate `MSBuild.exe`.
- Examples use `C:\work\eco_support_net_oracle`; replace it with your checkout path. Start from the repository root.

## Automated local build

On Windows, run `.\scripts\build-extensions.ps1` from the repository root, or its `scripts\build-extensions.bat` wrapper. The helper builds/tests the VS Code package, builds the Visual Studio package and stages checksums. It does not replace installed-editor acceptance or publish a listing. The manual commands below expose the underlying steps.

## VS Code VSIX

```powershell
Set-Location C:\work\eco_support_net_oracle\src\DataGuard.VSCode
npm ci
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
npm test
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
npm run package
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
```

`npm run package` first publishes the Release language server into `server/` with its `manifest.json` integrity hash, then creates the VSIX.

Stage the package and checksum:

```powershell
Set-Location C:\work\eco_support_net_oracle
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
Set-Location C:\work\eco_support_net_oracle
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

Close Visual Studio and launch the Visual Studio VSIX with its VSIX installer, then restart the target IDE. Do not use that installer for the VS Code package.

### Optional VSIX Digital Signing (Visual Studio)

By default, the Visual Studio VSIX is built unsigned (`Digital Signature: None`).
When a release owner or developer has a Code Signing certificate (`.pfx`) or a certificate in the Windows Certificate Store, automated VSIX digital signing can be enabled:

#### 1. Automated Signing via Batch / CMD
```cmd
rem Option 1: Using a PFX file and an existing session environment variable:
rem Supply DATAGUARD_VSIX_CERT_PASSWORD through an approved session secret mechanism before this command.
scripts\build-extensions.bat -CertificatePath "C:\path\to\cert.pfx"

rem Option 2: Using a SHA-1 thumbprint from the Windows Certificate Store (CurrentUser\My or LocalMachine\My):
scripts\build-extensions.bat -CertificateThumbprint "THUMBPRINT_HEX"
```

#### 2. Automated Signing via PowerShell
```powershell
# Using a PFX file:
# Supply DATAGUARD_VSIX_CERT_PASSWORD through an approved session secret mechanism; do not type a literal password here.
.\scripts\build-extensions.ps1 -CertificatePath 'C:\path\to\cert.pfx'

# Using Windows Certificate Store:
.\scripts\build-extensions.ps1 -CertificateThumbprint 'THUMBPRINT_HEX'
```

#### 3. Standalone Signing of an Existing VSIX
```powershell
.\scripts\sign-vsix.ps1 -VsixPath "artifacts\visualstudio\dataguard-visualstudio-0.3.0.vsix" -CertificatePath "C:\path\to\cert.pfx" -CertificatePasswordEnv DATAGUARD_VSIX_CERT_PASSWORD
```

#### 4. Verifying VSIX Digital Signatures
```powershell
.\scripts\verify-vsix-signature.ps1 -VsixPath "artifacts\visualstudio\dataguard-visualstudio-0.3.0.vsix" -ExpectedThumbprint "THUMBPRINT_HEX"
```

> [!NOTE]
> **Checksum after signing**: `build-extensions.ps1` writes the checksum after optional signing. Standalone `sign-vsix.ps1` does not update the sidecar: recalculate it after signing, then repeat byte/hash acceptance before distribution.

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
| `VSIXInstaller.NoApplicableSKUsException` when installing Visual Studio extension | Verify the manifest `source.extension.vsixmanifest` targets your edition (`Community`, `Professional`, `Enterprise`) and version range (`[17.12,19.0)`: VS 2022 17.12+ and VS 2026; 17.0–17.11 are excluded because their Roslyn refuses the bundled analyzer). |
| Script invocation path loses backslash (`scriptsbuild-extensions.ps1`) | Shell/bash eats backslashes. Use forward slashes (`scripts/build-extensions.ps1`) or quote the path (`"-File 'scripts\build-extensions.ps1'"`). |
| `vswhere.exe was not found` | Install Visual Studio or Build Tools with the MSBuild and Visual Studio SDK workloads. |
| `MSBuild.exe was not found by vswhere` | Add the MSBuild component through Visual Studio Installer, then retry. |
| VSIX is blocked during installation | Close the target IDE and verify the `.sha256` value before retrying. |
| VS Code package lacks the language server | Run `npm run package`, not `npx vsce package`; the npm script runs `prepare-lsp`. |
