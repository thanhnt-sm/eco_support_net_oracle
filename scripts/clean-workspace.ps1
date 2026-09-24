<#
.SYNOPSIS
Cleans workspace caches, build outputs, and temporary artifacts across DataGuard.

.DESCRIPTION
Manages pre-build, post-build, and deep cleaning for Visual Studio and VS Code extension
build pipelines, test artifacts, and intermediate compiler staging files.

.PARAMETER Mode
The cleanup mode to execute:
  - PreBuild:  Cleans TestResults, trx/coverage, stale logs, obj/*/cli staging, and old VSIX artifacts.
  - PostBuild: Retains final VSIX in artifacts/, removes uncompressed obj/*/cli staging and temp containers.
  - Deep:      Full clean including all bin/obj, node_modules, nupkg, test results, and runs dotnet clean.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet('PreBuild', 'PostBuild', 'Deep')]
    [string]$Mode
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
Set-Location $repoRoot

Write-Host "================================================================================" -ForegroundColor Cyan
Write-Host " DataGuard Workspace Cache Cleanup - Mode: $Mode" -ForegroundColor Cyan
Write-Host "================================================================================" -ForegroundColor Cyan

$script:totalBytesFreed = 0
$script:totalDeletedItems = 0

function Remove-TargetItem {
    param(
        [string]$Path,
        [switch]$Critical
    )

    if (Test-Path -LiteralPath $Path) {
        $item = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
        if ($null -eq $item) { return }

        $fullPath = [System.IO.Path]::GetFullPath($Path)
        $fullRepoRoot = [System.IO.Path]::GetFullPath($repoRoot)
        $repoRootWithSlash = $fullRepoRoot.TrimEnd('\', '/') + '\'
        $pathWithSlash = $fullPath.TrimEnd('\', '/') + '\'
        $vsExpMefRoot = Join-Path $env:LOCALAPPDATA 'Microsoft\VisualStudio'
        $vsExpMefRootWithSlash = [System.IO.Path]::GetFullPath($vsExpMefRoot).TrimEnd('\', '/') + '\'

        $isRepoSub = $fullPath.Equals($fullRepoRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
                     $pathWithSlash.StartsWith($repoRootWithSlash, [System.StringComparison]::OrdinalIgnoreCase)
        $isVsExpSub = (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) -and
                      $pathWithSlash.StartsWith($vsExpMefRootWithSlash, [System.StringComparison]::OrdinalIgnoreCase)

        if (-not $isRepoSub -and -not $isVsExpSub) {
            Write-Warning "  [!] Path escapes repository root, skipping for safety: $Path"
            return
        }

        $bytes = 0
        $isReparsePoint = [bool]($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint)

        if ($isReparsePoint) {
            # Directory junction or symlink: delete only the link, NEVER traverse target
            if ($item.PSIsContainer) {
                [System.IO.Directory]::Delete($Path, $false)
            } else {
                [System.IO.File]::Delete($Path)
            }
        } elseif ($item.PSIsContainer) {
            if ($item.Name -ne "node_modules" -and $item.Name -ne ".git" -and $item.FullName -notmatch '\\obj\\cli$') {
                $files = Get-ChildItem -LiteralPath $Path -Recurse -File -Force -ErrorAction SilentlyContinue
                foreach ($f in $files) {
                    $bytes += $f.Length
                }
            }
            Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction SilentlyContinue
        } else {
            $bytes = $item.Length
            Remove-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
        }

        if (Test-Path -LiteralPath $Path) {
            if ($Critical) {
                throw "Critical locked path could not be deleted: $Path. Please terminate locking processes and retry."
            }
            Write-Warning "  [!] Locked or in-use path could not be deleted: $Path"
            return
        }

        $script:totalBytesFreed += $bytes
        $script:totalDeletedItems++
        $sizeStr = if ($bytes -ge 1GB) {
            "$([math]::Round($bytes / 1GB, 2)) GB"
        } elseif ($bytes -ge 1MB) {
            "$([math]::Round($bytes / 1MB, 2)) MB"
        } elseif ($bytes -ge 1KB) {
            "$([math]::Round($bytes / 1KB, 2)) KB"
        } else {
            "$bytes B"
        }

        Write-Host "  [-] $Path ($sizeStr)" -ForegroundColor DarkGray
    }
}

function Remove-PatternMatchingItems {
    param(
        [string]$RootDirectory,
        [string]$Filter,
        [switch]$DirectoriesOnly,
        [switch]$FilesOnly
    )

    if (-not (Test-Path -LiteralPath $RootDirectory)) { return }

    $items = Get-ChildItem -LiteralPath $RootDirectory -Filter $Filter -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object {
            $_.FullName -notmatch '\\(\.git|\.omo|\.omp|\.codex|node_modules)\\'
        }

    if ($DirectoriesOnly) {
        $items = $items | Where-Object { $_.PSIsContainer }
    } elseif ($FilesOnly) {
        $items = $items | Where-Object { -not $_.PSIsContainer }
    }

    foreach ($item in $items) {
        Remove-TargetItem -Path $item.FullName
    }
}

switch ($Mode) {
    'PreBuild' {
        Write-Host "`nPurging test results, coverage caches, and stale logs..." -ForegroundColor Yellow
        Remove-TargetItem -Path (Join-Path $repoRoot "TestResults")
        $testResultDirs = Get-ChildItem -LiteralPath $repoRoot -Recurse -Directory -Force -ErrorAction SilentlyContinue |
            Where-Object {
                $_.Name -eq "TestResults" -and
                $_.FullName -notmatch '\\(\.git|\.omo|\.omp|\.codex|node_modules)\\'
            }
        foreach ($dir in $testResultDirs) {
            Remove-TargetItem -Path $dir.FullName
        }

        Remove-TargetItem -Path (Join-Path $repoRoot "coverage")
        Remove-TargetItem -Path (Join-Path $repoRoot ".coverage")
        Remove-TargetItem -Path (Join-Path $repoRoot ".testcontainers")

        Remove-PatternMatchingItems -RootDirectory $repoRoot -Filter "*.trx" -FilesOnly
        Remove-PatternMatchingItems -RootDirectory $repoRoot -Filter "*.cobertura.xml" -FilesOnly
        Remove-PatternMatchingItems -RootDirectory $repoRoot -Filter "*.log" -FilesOnly

        Write-Host "Cleaning intermediate bundled CLI staging and compiler outputs..." -ForegroundColor Yellow
        Remove-TargetItem -Path (Join-Path $repoRoot "src\DataGuard.VisualStudio\obj\cli") -Critical
        Remove-TargetItem -Path (Join-Path $repoRoot "src\DataGuard.VisualStudio\cli")
        Remove-TargetItem -Path (Join-Path $repoRoot "src\DataGuard.VSCode\out") -Critical
        Remove-TargetItem -Path (Join-Path $repoRoot "src\DataGuard.VSCode\dist") -Critical
        Remove-TargetItem -Path (Join-Path $repoRoot "src\DataGuard.VSCode\server") -Critical

        Write-Host "Cleaning previous artifacts, source VSIXes, and sensitive reports..." -ForegroundColor Yellow
        Remove-PatternMatchingItems -RootDirectory (Join-Path $repoRoot "artifacts") -Filter "*.vsix" -FilesOnly
        Remove-PatternMatchingItems -RootDirectory (Join-Path $repoRoot "artifacts") -Filter "*.sha256" -FilesOnly
        Remove-PatternMatchingItems -RootDirectory (Join-Path $repoRoot "artifacts") -Filter "*.sarif" -FilesOnly
        Remove-PatternMatchingItems -RootDirectory (Join-Path $repoRoot "artifacts") -Filter "*.json" -FilesOnly
        Remove-PatternMatchingItems -RootDirectory (Join-Path $repoRoot "src\DataGuard.VSCode") -Filter "*.vsix" -FilesOnly
        Remove-PatternMatchingItems -RootDirectory (Join-Path $repoRoot "src\DataGuard.VSCode") -Filter "*.vsix.sha256" -FilesOnly
        Remove-PatternMatchingItems -RootDirectory (Join-Path $repoRoot "src\DataGuard.VisualStudio") -Filter "*.vsix" -FilesOnly
        Remove-PatternMatchingItems -RootDirectory (Join-Path $repoRoot "src\DataGuard.VisualStudio") -Filter "*.vsix.sha256" -FilesOnly
    }

    'PostBuild' {
        Write-Host "`nPurging intermediate uncompressed CLI staging while retaining final artifacts..." -ForegroundColor Yellow
        Remove-TargetItem -Path (Join-Path $repoRoot "src\DataGuard.VisualStudio\obj\cli") -Critical
        Remove-TargetItem -Path (Join-Path $repoRoot "src\DataGuard.VisualStudio\cli")
        Remove-TargetItem -Path (Join-Path $repoRoot "src\DataGuard.VSCode\out")
        Remove-TargetItem -Path (Join-Path $repoRoot "src\DataGuard.VSCode\dist")
        Remove-TargetItem -Path (Join-Path $repoRoot "src\DataGuard.VSCode\server")
        Remove-TargetItem -Path (Join-Path $repoRoot ".testcontainers")

        Write-Host "Purging sensitive scan reports from artifacts folder..." -ForegroundColor Yellow
        Remove-PatternMatchingItems -RootDirectory (Join-Path $repoRoot "artifacts") -Filter "*.sarif" -FilesOnly
        Remove-PatternMatchingItems -RootDirectory (Join-Path $repoRoot "artifacts") -Filter "*.json" -FilesOnly

        Write-Host "Purging source VSIX copies in source folders..." -ForegroundColor Yellow
        Remove-PatternMatchingItems -RootDirectory (Join-Path $repoRoot "src\DataGuard.VSCode") -Filter "*.vsix" -FilesOnly
        Remove-PatternMatchingItems -RootDirectory (Join-Path $repoRoot "src\DataGuard.VSCode") -Filter "*.vsix.sha256" -FilesOnly
        Remove-PatternMatchingItems -RootDirectory (Join-Path $repoRoot "src\DataGuard.VisualStudio") -Filter "*.vsix" -FilesOnly
        Remove-PatternMatchingItems -RootDirectory (Join-Path $repoRoot "src\DataGuard.VisualStudio") -Filter "*.vsix.sha256" -FilesOnly
    }

    'Deep' {
        Write-Host "`nExecuting dotnet clean..." -ForegroundColor Yellow
        try {
            & dotnet clean DataGuard.sln -c Release -v quiet | Out-Null
            if ($LASTEXITCODE -ne 0) { Write-Warning "dotnet clean (Release) returned non-zero exit code: $LASTEXITCODE" }
            & dotnet clean DataGuard.sln -c Debug -v quiet | Out-Null
            if ($LASTEXITCODE -ne 0) { Write-Warning "dotnet clean (Debug) returned non-zero exit code: $LASTEXITCODE" }
        } catch {
            Write-Warning "dotnet clean completed with notices: $_"
        }

        Write-Host "Wiping all bin and obj folders..." -ForegroundColor Yellow
        $binDirs = Get-ChildItem -LiteralPath $repoRoot -Recurse -Directory -Force -ErrorAction SilentlyContinue |
            Where-Object {
                ($_.Name -eq "bin" -or $_.Name -eq "obj") -and
                $_.FullName -notmatch '\\(\.git|\.omo|\.omp|\.codex|node_modules)\\'
            }
        foreach ($dir in $binDirs) {
            Remove-TargetItem -Path $dir.FullName
        }

        Write-Host "Wiping node_modules and npm build outputs..." -ForegroundColor Yellow
        Remove-TargetItem -Path (Join-Path $repoRoot "src\DataGuard.VSCode\node_modules")
        Remove-TargetItem -Path (Join-Path $repoRoot "src\DataGuard.VSCode\dist")
        Remove-TargetItem -Path (Join-Path $repoRoot "src\DataGuard.VSCode\out")
        Remove-TargetItem -Path (Join-Path $repoRoot "src\DataGuard.VSCode\server")
        Remove-TargetItem -Path (Join-Path $repoRoot "src\DataGuard.VSCode\.vscode-test")

        Write-Host "Wiping IDE and benchmark caches..." -ForegroundColor Yellow
        Remove-TargetItem -Path (Join-Path $repoRoot ".vs")
        Remove-TargetItem -Path (Join-Path $repoRoot "BenchmarkDotNet.Artifacts")
        if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
            $vsExpHives = Get-ChildItem -Path (Join-Path $env:LOCALAPPDATA "Microsoft\VisualStudio") -Directory -Filter "*Exp" -ErrorAction SilentlyContinue
            foreach ($hive in $vsExpHives) {
                $mefCache = Join-Path $hive.FullName "ComponentModelCache"
                if (Test-Path -LiteralPath $mefCache) {
                    Remove-TargetItem -Path $mefCache
                }
            }
        }

        Write-Host "Wiping test results, nupkg, and test hives..." -ForegroundColor Yellow
        Remove-TargetItem -Path (Join-Path $repoRoot "TestResults")
        $testResultDirs = Get-ChildItem -LiteralPath $repoRoot -Recurse -Directory -Force -ErrorAction SilentlyContinue |
            Where-Object {
                $_.Name -eq "TestResults" -and
                $_.FullName -notmatch '\\(\.git|\.omo|\.omp|\.codex)\\'
            }
        foreach ($dir in $testResultDirs) {
            Remove-TargetItem -Path $dir.FullName
        }

        $nupkgDirs = Get-ChildItem -LiteralPath $repoRoot -Recurse -Directory -Force -ErrorAction SilentlyContinue |
            Where-Object {
                $_.Name -eq "nupkg" -and
                $_.FullName -notmatch '\\(\.git|\.omo|\.omp|\.codex)\\'
            }
        foreach ($dir in $nupkgDirs) {
            Remove-TargetItem -Path $dir.FullName
        }

        Remove-TargetItem -Path (Join-Path $repoRoot "coverage")
        Remove-TargetItem -Path (Join-Path $repoRoot ".coverage")
        Remove-TargetItem -Path (Join-Path $repoRoot "sbom")
        Remove-TargetItem -Path (Join-Path $repoRoot ".testcontainers")
        Remove-TargetItem -Path (Join-Path $repoRoot "artifacts\nuget")
        Remove-TargetItem -Path (Join-Path $repoRoot "artifacts\nupkg")
        Remove-TargetItem -Path (Join-Path $repoRoot "artifacts\vscode")
        Remove-TargetItem -Path (Join-Path $repoRoot "artifacts\visualstudio")
        Remove-PatternMatchingItems -RootDirectory (Join-Path $repoRoot "artifacts") -Filter "*.vsix" -FilesOnly
        Remove-PatternMatchingItems -RootDirectory (Join-Path $repoRoot "artifacts") -Filter "*.sha256" -FilesOnly
        Remove-PatternMatchingItems -RootDirectory (Join-Path $repoRoot "artifacts") -Filter "*.sarif" -FilesOnly
        Remove-PatternMatchingItems -RootDirectory (Join-Path $repoRoot "artifacts") -Filter "*.json" -FilesOnly
        Remove-PatternMatchingItems -RootDirectory $repoRoot -Filter "*.trx" -FilesOnly
        Remove-PatternMatchingItems -RootDirectory $repoRoot -Filter "*.cobertura.xml" -FilesOnly
        Remove-PatternMatchingItems -RootDirectory $repoRoot -Filter "*.log" -FilesOnly
        Remove-PatternMatchingItems -RootDirectory $repoRoot -Filter "*.tsbuildinfo" -FilesOnly
    }
}

$freedDisplay = if ($script:totalBytesFreed -ge 1GB) {
    "$([math]::Round($script:totalBytesFreed / 1GB, 2)) GB"
} else {
    "$([math]::Round($script:totalBytesFreed / 1MB, 2)) MB"
}

Write-Host "`n--------------------------------------------------------------------------------" -ForegroundColor Cyan
Write-Host " Cleanup completed successfully!" -ForegroundColor Green
Write-Host "   Mode         : $Mode" -ForegroundColor White
Write-Host "   Items Purged : $script:totalDeletedItems" -ForegroundColor White
Write-Host "   Disk Freed   : $freedDisplay" -ForegroundColor Green
Write-Host "--------------------------------------------------------------------------------`n" -ForegroundColor Cyan
