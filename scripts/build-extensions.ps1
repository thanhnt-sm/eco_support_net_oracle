[CmdletBinding()]
param (
    [string]$Configuration = 'Release',
    [switch]$SkipVSCode,
    [switch]$SkipVisualStudio
)

<#
.SYNOPSIS
Builds the VS Code and Visual Studio extensions as described in docs/05-operations/extension-build-guide.vi.md

.DESCRIPTION
This script compiles and packages the DataGuard extensions for both VS Code and Visual Studio.
It puts the final .vsix files and their SHA-256 hashes in the artifacts/ directory.
#>

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$originalLocation = Get-Location

Write-Host "Starting DataGuard Extension Build Process..." -ForegroundColor Cyan

# Resolve package version safely for artifact naming
$version = "0.2.3"
$vscodePkgJson = Join-Path $repoRoot "src\DataGuard.VSCode\package.json"
if (Test-Path -LiteralPath $vscodePkgJson) {
    try {
        $parsedJson = Get-Content -LiteralPath $vscodePkgJson -Raw | ConvertFrom-Json
        if (-not [string]::IsNullOrWhiteSpace($parsedJson.version)) {
            $version = [string]$parsedJson.version
        }
    } catch {}
}

# 0. Pre-build cache & artifact cleanup
& "$PSScriptRoot\clean-workspace.ps1" -Mode PreBuild
try {
    # 1. Build VS Code Extension
    if (-not $SkipVSCode) {
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
Remove-Item -LiteralPath $vscodeSource -Force -ErrorAction SilentlyContinue
$hashVscode = (Get-FileHash -LiteralPath $vscodeDest -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$vscodeDest.sha256" -Value "$hashVscode  $(Split-Path -Leaf $vscodeDest)" -NoNewline

        Write-Host "VS Code build completed: $vscodeDest" -ForegroundColor Green
    } else {
        Write-Host "`n[1/2] Skipping VS Code Extension build as requested." -ForegroundColor DarkGray
    }

    # 2. Build Visual Studio Extension
    if (-not $SkipVisualStudio) {
        Write-Host "`n[2/2] Building Visual Studio Extension..." -ForegroundColor Yellow
$msbuild = $env:MSBUILD
if ([string]::IsNullOrWhiteSpace($msbuild) -or -not (Test-Path $msbuild)) {
    $vswherePaths = @(
        (Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"),
        (Join-Path $env:ProgramFiles "Microsoft Visual Studio\Installer\vswhere.exe")
    )
    $vswhere = $vswherePaths | Where-Object { Test-Path $_ } | Select-Object -First 1

    if (-not [string]::IsNullOrWhiteSpace($vswhere)) {
        $msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
    }
    if ([string]::IsNullOrWhiteSpace($msbuild)) {
        $cmd = Get-Command msbuild -ErrorAction SilentlyContinue
        if ($null -ne $cmd) { $msbuild = $cmd.Source }
    }
}

if ([string]::IsNullOrWhiteSpace($msbuild) -or -not (Test-Path $msbuild)) {
    throw "MSBuild was not found via env:MSBUILD, vswhere, or PATH. Please ensure Visual Studio or Build Tools with MSBuild is installed."
}
Write-Host "Using MSBuild: $msbuild"
& $msbuild src\DataGuard.VisualStudio\DataGuard.VisualStudio.csproj `
  /t:Rebuild `
  /p:CreateVsixContainer=true `
  /p:Configuration=$Configuration `
  /restore

if ($LASTEXITCODE -ne 0) { throw "Visual Studio extension build failed with exit code $LASTEXITCODE" }

    $vsSource = "src\DataGuard.VisualStudio\bin\$Configuration\net472\DataGuard.VisualStudio.vsix"
    $vsDestDir = "artifacts\visualstudio"

    $manifestPath = Join-Path $repoRoot "src\DataGuard.VisualStudio\source.extension.vsixmanifest"
    [xml]$vsManifest = Get-Content $manifestPath
    $vsVersion = $vsManifest.PackageManifest.Metadata.Identity.Version
    if ([string]::IsNullOrWhiteSpace($vsVersion)) {
        $vsVersion = $version
    }
    $vsDest = "$vsDestDir\dataguard-visualstudio-$vsVersion.vsix"
if (-not (Test-Path $vsSource)) {
    throw "Failed to find Visual Studio vsix at $vsSource"
}

New-Item -ItemType Directory -Force $vsDestDir | Out-Null
Copy-Item -LiteralPath $vsSource -Destination $vsDest -Force
Remove-Item -LiteralPath $vsSource -Force -ErrorAction SilentlyContinue
$hashVs = (Get-FileHash -LiteralPath $vsDest -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$vsDest.sha256" -Value "$hashVs  $(Split-Path -Leaf $vsDest)" -NoNewline

        Write-Host "Visual Studio build completed: $vsDest" -ForegroundColor Green
    } else {
        Write-Host "`n[2/2] Skipping Visual Studio Extension build as requested." -ForegroundColor DarkGray
    }
}
finally {
    # 3. Post-build intermediate staging cleanup (always executed)
    try {
        & "$PSScriptRoot\clean-workspace.ps1" -Mode PostBuild
    } catch {
        Write-Warning "PostBuild cleanup encountered a notice: $_"
    }
    Set-Location $originalLocation
}

Write-Host "`nAll builds completed successfully! Artifacts are located in the '$repoRoot\artifacts\' directory." -ForegroundColor Cyan
