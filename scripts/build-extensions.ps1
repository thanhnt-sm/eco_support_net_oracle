<#
.SYNOPSIS
Builds the VS Code and Visual Studio extensions as described in docs/05-operations/extension-build-guide.vi.md

.DESCRIPTION
This script compiles and packages the DataGuard extensions for both VS Code and Visual Studio.
It puts the final .vsix files and their SHA-256 hashes in the artifacts/ directory.
#>

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent

Write-Host "Starting DataGuard Extension Build Process..." -ForegroundColor Cyan


# 0. Pre-build cache & artifact cleanup
& "$PSScriptRoot\clean-workspace.ps1" -Mode PreBuild
# 1. Build VS Code Extension
Write-Host "`n[1/2] Building VS Code Extension..." -ForegroundColor Yellow
Set-Location (Join-Path $repoRoot "src\DataGuard.VSCode")

Write-Host "Running npm ci..."
npm ci
if ($LASTEXITCODE -ne 0) { throw "npm ci failed with exit code $LASTEXITCODE" }

Write-Host "Running npm test..."
npm test
if ($LASTEXITCODE -ne 0) { throw "npm test failed with exit code $LASTEXITCODE" }

Write-Host "Running npm run package..."
npm run package
if ($LASTEXITCODE -ne 0) { throw "npm run package failed with exit code $LASTEXITCODE" }

Set-Location $repoRoot
$version = (Get-Content src\DataGuard.VSCode\package.json -Raw | ConvertFrom-Json).version
$vscodeSource = "src\DataGuard.VSCode\dataguard-vscode-$version.vsix"
$vscodeDestDir = "artifacts\vscode"
$vscodeDest = "$vscodeDestDir\dataguard-vscode-$version.vsix"

if (-not (Test-Path $vscodeSource)) {
    throw "Failed to find VS Code vsix at $vscodeSource"
}

New-Item -ItemType Directory -Force $vscodeDestDir | Out-Null
Copy-Item -LiteralPath $vscodeSource -Destination $vscodeDest -Force
$hashVscode = (Get-FileHash -LiteralPath $vscodeDest -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$vscodeDest.sha256" -Value "$hashVscode  $(Split-Path -Leaf $vscodeDest)" -NoNewline

Write-Host "VS Code build completed: $vscodeDest" -ForegroundColor Green


# 2. Build Visual Studio Extension
Write-Host "`n[2/2] Building Visual Studio Extension..." -ForegroundColor Yellow
$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"

if (-not (Test-Path $vswhere)) {
    throw "vswhere.exe not found at $vswhere. Please ensure Visual Studio Installer is present."
}

$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($msbuild)) { 
    throw "MSBuild.exe was not found by vswhere. Please ensure Visual Studio with MSBuild is installed." 
}

Write-Host "Using MSBuild: $msbuild"
& $msbuild src\DataGuard.VisualStudio\DataGuard.VisualStudio.csproj `
  /t:Rebuild `
  /p:CreateVsixContainer=true `
  /p:Configuration=Release `
  /restore

if ($LASTEXITCODE -ne 0) { throw "Visual Studio extension build failed with exit code $LASTEXITCODE" }

$vsSource = "src\DataGuard.VisualStudio\bin\Release\net472\DataGuard.VisualStudio.vsix"
$vsDestDir = "artifacts\visualstudio"
$vsDest = "$vsDestDir\dataguard-visualstudio-$version.vsix"

if (-not (Test-Path $vsSource)) {
    throw "Failed to find Visual Studio vsix at $vsSource"
}

New-Item -ItemType Directory -Force $vsDestDir | Out-Null
Copy-Item -LiteralPath $vsSource -Destination $vsDest -Force
$hashVs = (Get-FileHash -LiteralPath $vsDest -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$vsDest.sha256" -Value "$hashVs  $(Split-Path -Leaf $vsDest)" -NoNewline

Write-Host "Visual Studio build completed: $vsDest" -ForegroundColor Green

# 3. Post-build intermediate staging cleanup
& "$PSScriptRoot\clean-workspace.ps1" -Mode PostBuild

Write-Host "`nAll builds completed successfully! Artifacts are located in the '$repoRoot\artifacts\' directory." -ForegroundColor Cyan
