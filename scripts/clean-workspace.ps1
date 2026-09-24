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

[CmdletBinding(DefaultParameterSetName = 'ModeParam')]
param(
    [Parameter(ParameterSetName = 'ModeParam', Position = 0)]
    [ValidateSet('PreBuild', 'PostBuild', 'Deep')]
    [string]$Mode = 'PreBuild',

    [Parameter(ParameterSetName = 'PreSwitch')]
    [switch]$Pre,

    [Parameter(ParameterSetName = 'PostSwitch')]
    [switch]$Post,

    [Parameter(ParameterSetName = 'DeepSwitch')]
    [switch]$Deep,

    [Alias("skip-vscode")]
    [switch]$SkipVSCode,
    [Alias("skip-visualstudio")]
    [switch]$SkipVisualStudio
)

switch ($PSCmdlet.ParameterSetName) {
    'PreSwitch'  { $Mode = 'PreBuild' }
    'PostSwitch' { $Mode = 'PostBuild' }
    'DeepSwitch' { $Mode = 'Deep' }
}

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$originalLocation = Get-Location
Set-Location $repoRoot
try {
    [System.IO.Directory]::SetCurrentDirectory($repoRoot)
} catch {}
$script:totalBytesFreed = 0
$script:totalDeletedItems = 0
$script:criticalFailures = @()

Write-Host "================================================================================" -ForegroundColor Cyan
Write-Host " DataGuard Workspace Cache Cleanup - Mode: $Mode" -ForegroundColor Cyan
Write-Host "================================================================================" -ForegroundColor Cyan
function Test-IsStaleTempDirectory {
    param(
        [System.IO.DirectoryInfo]$Dir,
        [System.DateTime]$NowUtc,
        [int]$ThresholdMinutes = 15
    )
    $lastWrite = $Dir.LastWriteTimeUtc
    $creation = $Dir.CreationTimeUtc
    $latest = if ($lastWrite -gt $creation) { $lastWrite } else { $creation }
    if (($NowUtc - $latest).TotalMinutes -lt $ThresholdMinutes) {
        return $false
    }
    $newestChild = Get-ChildItem -LiteralPath $Dir.FullName -Recurse -File -Force -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if ($null -ne $newestChild -and $newestChild.LastWriteTimeUtc -gt $latest) {
        $latest = $newestChild.LastWriteTimeUtc
    }
    return (($NowUtc - $latest).TotalMinutes -ge $ThresholdMinutes)
}

function Remove-TargetItem {
    param(
        [string]$Path,
        [switch]$Critical
    )

    if (Test-Path -LiteralPath $Path) {
        $item = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
        if ($null -eq $item) { return }
        if ($item.Name -eq ".git") {
            Write-Warning "  [!] Refusing to delete .git repository metadata: $Path"
            return
        }

        $sep = [System.IO.Path]::DirectorySeparatorChar
        $fullPath = [System.IO.Path]::GetFullPath($Path)
        $fullRepoRoot = [System.IO.Path]::GetFullPath($repoRoot)
        $repoRootWithSlash = $fullRepoRoot.TrimEnd('\', '/') + $sep
        $pathWithSlash = $fullPath.TrimEnd('\', '/') + $sep

        $isRepoSub = (-not $fullPath.Equals($fullRepoRoot, [System.StringComparison]::OrdinalIgnoreCase)) -and
                     $pathWithSlash.StartsWith($repoRootWithSlash, [System.StringComparison]::OrdinalIgnoreCase)
        $isVsExpSub = $false
        if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
            $localAppDataFull = [System.IO.Path]::GetFullPath($env:LOCALAPPDATA).TrimEnd('\', '/')
            $localAppDataWithSlash = $localAppDataFull + $sep
            if ($pathWithSlash.StartsWith($localAppDataWithSlash, [System.StringComparison]::OrdinalIgnoreCase)) {
                $relativeExp = $fullPath.Substring($localAppDataFull.Length).TrimStart('\', '/')
                $isVsExpSub = $relativeExp -match '^Microsoft[\\/]VisualStudio[\\/][^\\/]+Exp([\\/].*)?$'
            }
        }
        $isTempSub = $false
        $tempRoot = [System.IO.Path]::GetTempPath()
        if (-not [string]::IsNullOrWhiteSpace($tempRoot)) {
            $tempRootFull = [System.IO.Path]::GetFullPath($tempRoot).TrimEnd('\', '/')
            $tempRootWithSlash = $tempRootFull + $sep
            if ($pathWithSlash.StartsWith($tempRootWithSlash, [System.StringComparison]::OrdinalIgnoreCase)) {
                $subPart = $fullPath.Substring($tempRootFull.Length).TrimStart('\', '/')
                if ($subPart -match '^(DataGuard([\\/].*)?$|dataguard-.*)') {
                    $isTempSub = $true
                }
            }
        }
        if (-not $isRepoSub -and -not $isVsExpSub -and -not $isTempSub) {
            Write-Warning "  [!] Path escapes repository root, skipping for safety: $Path"
            return
        }

        $bytes = 0
        $isReparsePoint = [bool]($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint)

        if ($isReparsePoint) {
            # Directory junction or symlink: delete only the link, NEVER traverse target
            if ($item.PSIsContainer) {
                try {
                    if (($item.Attributes -band [System.IO.FileAttributes]::ReadOnly) -ne 0) {
                        $item.Attributes = $item.Attributes -band (-bnot [System.IO.FileAttributes]::ReadOnly)
                    }
                    [System.IO.Directory]::Delete($fullPath, $false)
                } catch {
                    try {
                        [System.IO.File]::Delete($fullPath)
                    } catch {}
                }
            } else {
                try {
                    if (($item.Attributes -band [System.IO.FileAttributes]::ReadOnly) -ne 0) {
                        $item.Attributes = $item.Attributes -band (-bnot [System.IO.FileAttributes]::ReadOnly)
                    }
                    [System.IO.File]::Delete($fullPath)
                } catch {}
            }
        } elseif ($item.PSIsContainer) {
            # Safely unlink any nested directory junctions without traversing into their targets
            try {
                $dirInfo = New-Object System.IO.DirectoryInfo($fullPath)
                $stack = New-Object System.Collections.Generic.Stack[System.IO.DirectoryInfo]
                $stack.Push($dirInfo)
                while ($stack.Count -gt 0) {
                    $current = $stack.Pop()
                    $subDirs = $null
                    try { $subDirs = $current.EnumerateDirectories() } catch { continue }
                    foreach ($sub in $subDirs) {
                        try {
                            if (($sub.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                                if (($sub.Attributes -band [System.IO.FileAttributes]::ReadOnly) -ne 0) {
                                    $sub.Attributes = $sub.Attributes -band (-bnot [System.IO.FileAttributes]::ReadOnly)
                                }
                                [System.IO.Directory]::Delete($sub.FullName, $false)
                            } else {
                                $stack.Push($sub)
                            }
                        } catch {}
                    }
                }
            } catch {}

            # Clear ReadOnly/Hidden attributes on root directory and descendants to avoid silent deletion failure
            try {
                if (($item.Attributes -band [System.IO.FileAttributes]::ReadOnly) -ne 0) {
                    $item.Attributes = $item.Attributes -band (-bnot [System.IO.FileAttributes]::ReadOnly)
                }
                Get-ChildItem -LiteralPath $fullPath -Recurse -Force -ErrorAction SilentlyContinue | ForEach-Object {
                    if (($_.Attributes -band [System.IO.FileAttributes]::ReadOnly) -ne 0) {
                        $_.Attributes = $_.Attributes -band (-bnot [System.IO.FileAttributes]::ReadOnly)
                    }
                }
            } catch {}

            if ($item.Name -ne "node_modules" -and $item.Name -ne ".git" -and $item.FullName -notmatch '[\\/]obj[\\/]cli$') {
                $files = Get-ChildItem -LiteralPath $fullPath -Recurse -File -Force -ErrorAction SilentlyContinue
                foreach ($f in $files) {
                    $bytes += $f.Length
                }
            }
            Remove-Item -LiteralPath $fullPath -Recurse -Force -ErrorAction SilentlyContinue
        } else {
            $bytes = $item.Length
            try {
                if (($item.Attributes -band [System.IO.FileAttributes]::ReadOnly) -ne 0) {
                    $item.Attributes = $item.Attributes -band (-bnot [System.IO.FileAttributes]::ReadOnly)
                }
            } catch {}
            Remove-Item -LiteralPath $fullPath -Force -ErrorAction SilentlyContinue
        }
        if (Test-Path -LiteralPath $Path) {
            if ($Critical) {
                $script:criticalFailures += $Path
                Write-Warning "  [!] Critical locked path could not be deleted: $Path"
            } else {
                Write-Warning "  [!] Locked or in-use path could not be deleted: $Path"
            }
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
function Test-ExcludedPath {
    param(
        [string]$FullName,
        [string]$Root
    )
    $rootFull = [System.IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    $relPath = if ($FullName.Length -gt $rootFull.Length) {
        $FullName.Substring($rootFull.Length)
    } else {
        $FullName
    }
    return ($relPath -match '^[\\/](\.git|\.omo|\.omp|\.codex|node_modules)([\\/]|$)' -or
            $relPath -match '[\\/](\.git|\.omo|\.omp|\.codex|node_modules)([\\/]|$)')
}

function Remove-PatternMatchingItems {
    param(
        [string]$RootDirectory,
        [string[]]$Filter,
        [switch]$DirectoriesOnly,
        [switch]$FilesOnly
    )

    if (-not (Test-Path -LiteralPath $RootDirectory)) { return }

    foreach ($f in $Filter) {
        $items = Get-ChildItem -LiteralPath $RootDirectory -Filter $f -Recurse -Force -ErrorAction SilentlyContinue |
            Where-Object { -not (Test-ExcludedPath -FullName $_.FullName -Root $RootDirectory) }

        if ($DirectoriesOnly) {
            $items = $items | Where-Object { $_.PSIsContainer }
        } elseif ($FilesOnly) {
            $items = $items | Where-Object { -not $_.PSIsContainer }
        }

        foreach ($item in $items) {
            Remove-TargetItem -Path $item.FullName
        }
    }
}

switch ($Mode) {
    'PreBuild' {
        Write-Host "`nPurging test results, coverage caches, and stale logs..." -ForegroundColor Yellow
        $tempRoot = [System.IO.Path]::GetTempPath()
        if (-not [string]::IsNullOrWhiteSpace($tempRoot) -and (Test-Path -LiteralPath $tempRoot)) {
            $now = [System.DateTime]::UtcNow
            $dgRoot = Join-Path $tempRoot "DataGuard"
            if (Test-Path -LiteralPath $dgRoot) {
            $staleDgDirs = Get-ChildItem -LiteralPath $dgRoot -Directory -Force -ErrorAction SilentlyContinue |
                Where-Object { Test-IsStaleTempDirectory -Dir $_ -NowUtc $now -ThresholdMinutes 15 }
                foreach ($sd in $staleDgDirs) {
                    Remove-TargetItem -Path $sd.FullName
                }
                $remainingDg = Get-ChildItem -LiteralPath $dgRoot -Force -ErrorAction SilentlyContinue
                if ($null -eq $remainingDg -or $remainingDg.Count -eq 0) {
                    Remove-TargetItem -Path $dgRoot
                }
            }
            $staleTempDirs = Get-ChildItem -LiteralPath $tempRoot -Directory -Force -ErrorAction SilentlyContinue |
                Where-Object {
                    if ($_.Name -like 'dataguard-*') {
                        Test-IsStaleTempDirectory -Dir $_ -NowUtc $now -ThresholdMinutes 15
                    } else {
                        $false
                    }
                }
            foreach ($td in $staleTempDirs) {
                Remove-TargetItem -Path $td.FullName
            }
        }
        $nupkgDirs = Get-ChildItem -LiteralPath $repoRoot -Recurse -Directory -Force -ErrorAction SilentlyContinue |
            Where-Object {
                $_.Name -eq "nupkg" -and
                -not (Test-ExcludedPath -FullName $_.FullName -Root $repoRoot)
            }
        foreach ($dir in $nupkgDirs) {
            Remove-TargetItem -Path $dir.FullName
        }
        Remove-TargetItem -Path (Join-Path $repoRoot "artifacts\nupkg")
        Remove-PatternMatchingItems -RootDirectory $repoRoot -Filter @("*.nupkg", "*.snupkg") -FilesOnly

        Remove-TargetItem -Path (Join-Path $repoRoot "TestResults")
        $testResultDirs = Get-ChildItem -LiteralPath $repoRoot -Recurse -Directory -Force -ErrorAction SilentlyContinue |
            Where-Object {
                $_.Name -eq "TestResults" -and
                -not (Test-ExcludedPath -FullName $_.FullName -Root $repoRoot)
            }
        foreach ($dir in $testResultDirs) {
            Remove-TargetItem -Path $dir.FullName
        }

        Remove-TargetItem -Path (Join-Path $repoRoot "coverage")
        Remove-TargetItem -Path (Join-Path $repoRoot ".coverage")
        Remove-TargetItem -Path (Join-Path $repoRoot ".testcontainers")

        Remove-PatternMatchingItems -RootDirectory $repoRoot -Filter @("*.trx", "*.cobertura.xml", "*.log", "*.tsbuildinfo") -FilesOnly

        Write-Host "Cleaning intermediate bundled CLI staging and compiler outputs..." -ForegroundColor Yellow
        $vsCritical = -not $SkipVisualStudio
        $vscCritical = -not $SkipVSCode
        Remove-TargetItem -Path (Join-Path $repoRoot "src\DataGuard.VisualStudio\obj\cli") -Critical:$vsCritical
        Remove-TargetItem -Path (Join-Path $repoRoot "src\DataGuard.VisualStudio\cli")
        Remove-TargetItem -Path (Join-Path $repoRoot "src\DataGuard.VSCode\out") -Critical:$vscCritical
        Remove-TargetItem -Path (Join-Path $repoRoot "src\DataGuard.VSCode\dist") -Critical:$vscCritical
        Remove-TargetItem -Path (Join-Path $repoRoot "src\DataGuard.VSCode\server") -Critical:$vscCritical
        if (-not $SkipVSCode) {
            Remove-TargetItem -Path (Join-Path $repoRoot "src\DataGuard.VSCode\.vscode-test")
        }

        Write-Host "Cleaning previous artifacts, source VSIXes, and sensitive reports..." -ForegroundColor Yellow
        Remove-PatternMatchingItems -RootDirectory (Join-Path $repoRoot "artifacts") -Filter @("*.vsix", "*.sha256", "*.sarif", "*summary*.json", "*report*.json", "*scan*.json") -FilesOnly
        Remove-PatternMatchingItems -RootDirectory (Join-Path $repoRoot "src\DataGuard.VSCode") -Filter @("*.vsix", "*.vsix.sha256") -FilesOnly
        Remove-PatternMatchingItems -RootDirectory (Join-Path $repoRoot "src\DataGuard.VisualStudio") -Filter @("*.vsix", "*.vsix.sha256") -FilesOnly
    }

    'PostBuild' {
        Write-Host "`nPurging intermediate uncompressed CLI staging while retaining final artifacts..." -ForegroundColor Yellow
        $tempRoot = [System.IO.Path]::GetTempPath()
        if (-not [string]::IsNullOrWhiteSpace($tempRoot) -and (Test-Path -LiteralPath $tempRoot)) {
            $now = [System.DateTime]::UtcNow
            $dgRoot = Join-Path $tempRoot "DataGuard"
            if (Test-Path -LiteralPath $dgRoot) {
            $staleDgDirs = Get-ChildItem -LiteralPath $dgRoot -Directory -Force -ErrorAction SilentlyContinue |
                Where-Object { Test-IsStaleTempDirectory -Dir $_ -NowUtc $now -ThresholdMinutes 15 }
                foreach ($sd in $staleDgDirs) {
                    Remove-TargetItem -Path $sd.FullName
                }
                $remainingDg = Get-ChildItem -LiteralPath $dgRoot -Force -ErrorAction SilentlyContinue
                if ($null -eq $remainingDg -or $remainingDg.Count -eq 0) {
                    Remove-TargetItem -Path $dgRoot
                }
            }
            $staleTempDirs = Get-ChildItem -LiteralPath $tempRoot -Directory -Force -ErrorAction SilentlyContinue |
                Where-Object {
                    if ($_.Name -like 'dataguard-*') {
                        Test-IsStaleTempDirectory -Dir $_ -NowUtc $now -ThresholdMinutes 15
                    } else {
                        $false
                    }
                }
            foreach ($td in $staleTempDirs) {
                Remove-TargetItem -Path $td.FullName
            }
        }

        if (-not $SkipVisualStudio) {
            Remove-TargetItem -Path (Join-Path $repoRoot "src\DataGuard.VisualStudio\obj\cli") -Critical
            Remove-TargetItem -Path (Join-Path $repoRoot "src\DataGuard.VisualStudio\cli")
        }
        if (-not $SkipVSCode) {
            Remove-TargetItem -Path (Join-Path $repoRoot "src\DataGuard.VSCode\out")
            Remove-TargetItem -Path (Join-Path $repoRoot "src\DataGuard.VSCode\dist")
            Remove-TargetItem -Path (Join-Path $repoRoot "src\DataGuard.VSCode\server")
        }
        Remove-TargetItem -Path (Join-Path $repoRoot ".testcontainers")

        Write-Host "Purging sensitive scan reports from artifacts folder..." -ForegroundColor Yellow
        Remove-PatternMatchingItems -RootDirectory (Join-Path $repoRoot "artifacts") -Filter @("*.sarif", "*summary*.json", "*report*.json", "*scan*.json") -FilesOnly
        Remove-PatternMatchingItems -RootDirectory (Join-Path $repoRoot "src\DataGuard.VSCode") -Filter @("*.vsix", "*.vsix.sha256") -FilesOnly
        Remove-PatternMatchingItems -RootDirectory (Join-Path $repoRoot "src\DataGuard.VisualStudio") -Filter @("*.vsix", "*.vsix.sha256") -FilesOnly
    }

    'Deep' {
        Write-Host "`nShutting down build servers and executing dotnet clean..." -ForegroundColor Yellow
        $tempRoot = [System.IO.Path]::GetTempPath()
        if (-not [string]::IsNullOrWhiteSpace($tempRoot) -and (Test-Path -LiteralPath $tempRoot)) {
            $now = [System.DateTime]::UtcNow
            $dgRoot = Join-Path $tempRoot "DataGuard"
            if (Test-Path -LiteralPath $dgRoot) {
            $staleDgDirs = Get-ChildItem -LiteralPath $dgRoot -Directory -Force -ErrorAction SilentlyContinue |
                Where-Object { Test-IsStaleTempDirectory -Dir $_ -NowUtc $now -ThresholdMinutes 15 }
                foreach ($sd in $staleDgDirs) {
                    Remove-TargetItem -Path $sd.FullName
                }
                $remainingDg = Get-ChildItem -LiteralPath $dgRoot -Force -ErrorAction SilentlyContinue
                if ($null -eq $remainingDg -or $remainingDg.Count -eq 0) {
                    Remove-TargetItem -Path $dgRoot
                }
            }
            $staleTempDirs = Get-ChildItem -LiteralPath $tempRoot -Directory -Force -ErrorAction SilentlyContinue |
                Where-Object {
                    if ($_.Name -like 'dataguard-*') {
                        Test-IsStaleTempDirectory -Dir $_ -NowUtc $now -ThresholdMinutes 15
                    } else {
                        $false
                    }
                }
            foreach ($td in $staleTempDirs) {
                Remove-TargetItem -Path $td.FullName
            }
        }

        try {
            & dotnet build-server shutdown | Out-Null
        } catch {}
        try {
            & dotnet clean DataGuard.sln -c Release -v quiet | Out-Null
            if ($LASTEXITCODE -ne 0) { Write-Warning "dotnet clean (Release) returned non-zero exit code: $LASTEXITCODE" }
            & dotnet clean DataGuard.sln -c Debug -v quiet | Out-Null
            if ($LASTEXITCODE -ne 0) { Write-Warning "dotnet clean (Debug) returned non-zero exit code: $LASTEXITCODE" }
        } catch {
            Write-Warning "dotnet clean completed with notices: $_"
        }
        try {
            & dotnet nuget locals http-cache --clear | Out-Null
            & dotnet nuget locals temp --clear | Out-Null
        } catch {}
        Write-Host "Wiping all bin and obj folders..." -ForegroundColor Yellow
        $binDirs = Get-ChildItem -LiteralPath $repoRoot -Recurse -Directory -Force -ErrorAction SilentlyContinue |
            Where-Object {
                ($_.Name -eq "bin" -or $_.Name -eq "obj") -and
                -not (Test-ExcludedPath -FullName $_.FullName -Root $repoRoot)
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

        Write-Host "Wiping IDE, format, and benchmark caches..." -ForegroundColor Yellow
        Remove-TargetItem -Path (Join-Path $repoRoot ".vs")
        Remove-TargetItem -Path (Join-Path $repoRoot ".format")
        Remove-TargetItem -Path (Join-Path $repoRoot ".cache")
        Remove-TargetItem -Path (Join-Path $repoRoot "BenchmarkDotNet.Artifacts")
        if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
            $vsExpHives = Get-ChildItem -LiteralPath (Join-Path $env:LOCALAPPDATA "Microsoft\VisualStudio") -Directory -Filter "*Exp" -ErrorAction SilentlyContinue
            foreach ($hive in $vsExpHives) {
                $mefCache = Join-Path $hive.FullName "ComponentModelCache"
                if (Test-Path -LiteralPath $mefCache) {
                    Remove-TargetItem -Path $mefCache
                }
                $privReg = Join-Path $hive.FullName "privateregistry.bin"
                if (Test-Path -LiteralPath $privReg) {
                    Remove-TargetItem -Path $privReg
                }
            }
        }

        Write-Host "Wiping test results, nupkg, and test hives..." -ForegroundColor Yellow
        Remove-TargetItem -Path (Join-Path $repoRoot "TestResults")
        $testResultDirs = Get-ChildItem -LiteralPath $repoRoot -Recurse -Directory -Force -ErrorAction SilentlyContinue |
            Where-Object {
                $_.Name -eq "TestResults" -and
                -not (Test-ExcludedPath -FullName $_.FullName -Root $repoRoot)
            }
        foreach ($dir in $testResultDirs) {
            Remove-TargetItem -Path $dir.FullName
        }

        $nupkgDirs = Get-ChildItem -LiteralPath $repoRoot -Recurse -Directory -Force -ErrorAction SilentlyContinue |
            Where-Object {
                $_.Name -eq "nupkg" -and
                $_.FullName -notmatch '[\\/](\.git|\.omo|\.omp|\.codex|node_modules)[\\/]'
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
        Remove-PatternMatchingItems -RootDirectory (Join-Path $repoRoot "artifacts") -Filter @("*.vsix", "*.sha256", "*.sarif", "*summary*.json", "*report*.json", "*scan*.json") -FilesOnly
        Remove-PatternMatchingItems -RootDirectory $repoRoot -Filter @("*.trx", "*.cobertura.xml", "*.log", "*.tsbuildinfo") -FilesOnly
    }
}

if ($script:criticalFailures.Count -gt 0) {
    Set-Location $originalLocation
    throw "Critical locked paths could not be deleted:`n" + ($script:criticalFailures -join "`n") + "`nPlease terminate locking processes and retry."
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
Set-Location $originalLocation
