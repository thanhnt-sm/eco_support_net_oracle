param(
    [string] $RootSuffix = "DataGuardCliVerify",
    [int] $TimeoutSeconds = 90
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Invoke-WithRetry {
    param([scriptblock]$ScriptBlock, [int]$TimeoutSeconds = 90)
    $old = $ErrorActionPreference
    $ErrorActionPreference = "Continue" # So COM errors can be caught
    $start = [DateTime]::UtcNow
    while (($([DateTime]::UtcNow) - $start).TotalSeconds -lt $TimeoutSeconds) {
        try {
            $result = & $ScriptBlock
            $ErrorActionPreference = $old
            Write-Output -NoEnumerate $result
            return
        } catch {
            Write-Host "COM Error: $_"
            Start-Sleep -Milliseconds 500
            continue
        }
    }
    $ErrorActionPreference = $old
    throw "COM operation timed out due to RPC_E_CALL_REJECTED."
}


if ([string]::IsNullOrWhiteSpace($RootSuffix) -or $RootSuffix -eq "Exp") {
    Write-Error "RootSuffix cannot be empty or 'Exp'."
    exit 1
}
$dte = $null
$vsixDeploymentPath = $null
$verificationRoot = $null
$dteProcess = $null
$toolInstalled = $false

$results = [ordered]@{
    "Isolated deployment" = "FAIL"
    "Missing-CLI commands" = "FAIL"
    "No temporary directory" = "FAIL"
    "Global-tool start" = "FAIL"
    "Nonexistent custom path & .cmd rejection" = "FAIL"
}

# Ensure clean state to track our test process
$preExistingDevenvs = Get-Process devenv -ErrorAction SilentlyContinue

$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"

$instanceId = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property instanceId
$devenv = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.CoreEditor -find "Common7\IDE\devenv.exe" | Select-Object -First 1
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1

if (-not $instanceId -or -not (Test-Path $devenv) -or -not (Test-Path $msbuild)) {
    Write-Error "Could not resolve Visual Studio instance, devenv.exe or MSBuild.exe."
    exit 1
}


try {
    Write-Host "Building and deploying to isolated suffix $RootSuffix..."
    
    $msbuildArgs = @(
        "src\DataGuard.VisualStudio\DataGuard.VisualStudio.csproj",
        "/t:Rebuild",
        "/restore",
        "/p:Configuration=Release",
        "/p:CreateVsixContainer=true",
        "/p:DeployExtension=true",
        "/p:DeployTargetInstanceId=$instanceId",
        "/p:VSSDKTargetPlatformRegRootSuffix=$RootSuffix"
    )
    $msbuildOutput = & $msbuild $msbuildArgs 2>&1
    
    $vsixDeploymentLine = $msbuildOutput | Where-Object { $_ -match 'VsixDeploymentPath\s*=\s*(.+)' }
    if ($vsixDeploymentLine) {
        $line = if ($vsixDeploymentLine -is [array]) { $vsixDeploymentLine[0] } else { $vsixDeploymentLine }
        if ($line -match 'VsixDeploymentPath\s*=\s*(.+)') {
            $vsixDeploymentPath = $matches[1].Trim()
        }
    }

    if ($LASTEXITCODE -ne 0 -or -not (Test-Path "src\DataGuard.VisualStudio\bin\Release\net472\DataGuard.VisualStudio.vsix") -or -not $vsixDeploymentPath -or -not $vsixDeploymentPath.StartsWith("$env:LOCALAPPDATA\Microsoft\VisualStudio", [StringComparison]::OrdinalIgnoreCase)) {
        Write-Host "MSBuild Output:"
        $msbuildOutput | Write-Host
        Write-Error "Build or isolated deployment failed. VsixDeploymentPath: $vsixDeploymentPath"
        exit 1
    }
    
    $results["Isolated deployment"] = "PASS"

    $verificationRoot = Join-Path $env:TEMP ("DataGuard-VSIX-Verification-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $verificationRoot | Out-Null
    
    $slnPath = Join-Path $verificationRoot "DataGuardCliVerify.sln"
    New-Item -ItemType File -Path $slnPath -Value "Microsoft Visual Studio Solution File, Format Version 12.00" | Out-Null
    
    $ymlContent = @"
ground_truth_mode: snapshot
snapshot_file_path: .dataguard-snapshot.json
"@
    Set-Content -Path (Join-Path $verificationRoot ".dataguard.yml") -Value $ymlContent
    
    $missingLogDir = Join-Path $verificationRoot "missing-log"
    $startedLogDir = Join-Path $verificationRoot "started-log"
    $customPathLogDir = Join-Path $verificationRoot "custom-path-log"
    
    New-Item -ItemType Directory -Path $missingLogDir | Out-Null
    New-Item -ItemType Directory -Path $startedLogDir | Out-Null
    New-Item -ItemType Directory -Path $customPathLogDir | Out-Null
    
    if (Get-Command dataguard -ErrorAction SilentlyContinue) {
        Write-Error "dataguard executable already found in PATH. Please remove it before running this test."
        exit 1
    }
    $dotnetTools = dotnet tool list --global 2>&1
    if ($dotnetTools -match "dataguard\.cli") {
        Write-Error "DataGuard.Cli tool already installed globally. Please uninstall it before running this test."
        exit 1
    }
    if ($env:DATAGUARD_CLI_PATH -and (Test-Path $env:DATAGUARD_CLI_PATH)) {
        Write-Error "DATAGUARD_CLI_PATH points to an existing file. Please unset it or remove the file."
        exit 1
    }
    $fallbackPaths = @(
        Join-Path $env:USERPROFILE ".dotnet\tools\dataguard.exe"
        Join-Path ${env:ProgramFiles} "DataGuard\dataguard.exe"
        Join-Path $env:LOCALAPPDATA "Programs\DataGuard\dataguard.exe"
    )
    foreach ($fb in $fallbackPaths) {
        if (Test-Path $fb) {
            Write-Error "dataguard executable found at fallback path $fb. Please remove it before running this test."
            exit 1
        }
    }
    
    $tempDataGuardPath = Join-Path ([IO.Path]::GetTempPath()) "DataGuard"
    $preTempDirs = @()
    if (Test-Path $tempDataGuardPath) {
        $preTempDirs = @(Get-ChildItem -Path $tempDataGuardPath -Directory | Select-Object -ExpandProperty Name)
    }

    Write-Host "Launching devenv.exe /RootSuffix $RootSuffix..."
    $slnPath = Join-Path $verificationRoot "DataGuardCliVerify.sln"
    $dteProcess = Start-Process -FilePath $devenv -ArgumentList "/RootSuffix", $RootSuffix, $slnPath -PassThru
    
    Write-Host "Launched devenv with PID $($dteProcess.Id)."
    
    $rotCode = @"
using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

public class RotHelper {
    [DllImport("ole32.dll")]
    private static extern int GetRunningObjectTable(uint reserved, out IRunningObjectTable pprot);
    [DllImport("ole32.dll")]
    private static extern int CreateBindCtx(uint reserved, out IBindCtx ppbc);
    
    public static object GetDTE(int processId, int timeoutSeconds) {
        string monikerName = "!VisualStudio.DTE.18.0:" + processId;
        DateTime start = DateTime.Now;
        while ((DateTime.Now - start).TotalSeconds < timeoutSeconds) {
            IRunningObjectTable rot = null;
            IEnumMoniker enumMoniker = null;
            try {
                if (GetRunningObjectTable(0, out rot) == 0) {
                    rot.EnumRunning(out enumMoniker);
                    enumMoniker.Reset();
                    IMoniker[] monikers = new IMoniker[1];
                    IntPtr fetched = IntPtr.Zero;
                    while (enumMoniker.Next(1, monikers, fetched) == 0) {
                        IBindCtx bindCtx = null;
                        CreateBindCtx(0, out bindCtx);
                        string name;
                        monikers[0].GetDisplayName(bindCtx, null, out name);
                        if (name == monikerName) {
                            object obj;
                            rot.GetObject(monikers[0], out obj);
                            return obj;
                        }
                    }
                }
            } catch {}
            finally {
                if (enumMoniker != null) Marshal.ReleaseComObject(enumMoniker);
                if (rot != null) Marshal.ReleaseComObject(rot);
            }
            System.Threading.Thread.Sleep(1000);
        }
        return null;
    }
}
"@
    Add-Type -TypeDefinition $rotCode
    
    Write-Host "Attaching to DTE..."
    $dte = [RotHelper]::GetDTE($dteProcess.Id, $TimeoutSeconds)
    if (-not $dte) { throw "Failed to attach to DTE for PID $($dteProcess.Id) within $TimeoutSeconds seconds." }
    
    Write-Host "Waiting for Visual Studio to initialize extensions (20s)..."
    Start-Sleep -Seconds 20
    Write-Host "Setting DTE Properties..."
    $properties = $null
    $startWait = [DateTime]::UtcNow
    while (($([DateTime]::UtcNow) - $startWait).TotalSeconds -lt $TimeoutSeconds) {
        try {
            $properties = $dte.Properties("DataGuard", "General")
            break
        } catch {
            Start-Sleep -Seconds 1
        }
    }
    if (-not $properties) { throw "Timed out waiting for DataGuard properties." }
    
    Invoke-WithRetry {
        $properties.Item("EnableDetailedLogging").Value = $true
        $properties.Item("CustomLogDirectory").Value = $missingLogDir
        $properties.Item("CustomCliPath").Value = ""
    }

    Write-Host "Executing missing-CLI validations (RunValidation)..."
    $expectedMsg = "[DataGuard] CLI executable was not found. Install it with 'dotnet tool install -g DataGuard.Cli', restart Visual Studio, or set Tools > Options > DataGuard > General > Custom CLI Executable Path to dataguard.exe."

    Invoke-WithRetry { $dte.ExecuteCommand("Tools.RunValidation") }
    
    $startPoll = [DateTime]::UtcNow
    $found = $false
    while (([DateTime]::UtcNow - $startPoll).TotalSeconds -lt 25) {
        if (Test-Path $missingLogDir) {
            $logFiles = @(Get-ChildItem -Path $missingLogDir -Filter "*.log")
            if ($logFiles.Count -gt 0) {
                $content = Get-Content $logFiles[0].FullName -Raw -ErrorAction SilentlyContinue
                if ($content -match "Failed to start") { throw "Found 'Failed to start' in log." }
                if ($content -match [regex]::Escape($expectedMsg)) { $found = $true; break }
            }
        }
        Start-Sleep -Seconds 1
    }
    if (-not $found) { Write-Error "Did not find expected missing CLI message for RunValidation." }

    Remove-Item (Join-Path $missingLogDir "*.log") -Force -ErrorAction SilentlyContinue

    Write-Host "Executing missing-CLI validations (AssessWorkspace)..."
    Invoke-WithRetry { $dte.ExecuteCommand("Tools.AssessWorkspace") }
    
    $startPoll = [DateTime]::UtcNow
    $found = $false
    while (([DateTime]::UtcNow - $startPoll).TotalSeconds -lt 25) {
        if (Test-Path $missingLogDir) {
            $logFiles = @(Get-ChildItem -Path $missingLogDir -Filter "*.log")
            if ($logFiles.Count -gt 0) {
                $content = Get-Content $logFiles[0].FullName -Raw -ErrorAction SilentlyContinue
                if ($content -match "Failed to start") { throw "Found 'Failed to start' in log." }
                if ($content -match [regex]::Escape($expectedMsg)) { $found = $true; break }
            }
        }
        Start-Sleep -Seconds 1
    }
    if (-not $found) { Write-Error "Did not find expected missing CLI message for AssessWorkspace." }


    $results["Missing-CLI commands"] = "PASS"

    $hasNewDirs = $false
    if (Test-Path $tempDataGuardPath) {
        $postTempDirs = @(Get-ChildItem -Path $tempDataGuardPath -Directory | Select-Object -ExpandProperty Name)
        $newDirs = Compare-Object $preTempDirs $postTempDirs | Where-Object { $_.SideIndicator -eq "=>" }
        if ($newDirs) {
            Write-Error "Found new unexpected directories in ${tempDataGuardPath}: $($newDirs | Out-String)"
            $hasNewDirs = $true
        }
    }
    if (-not $hasNewDirs) {
        $results["No temporary directory"] = "PASS"
    }

    Write-Host "Packing and installing DataGuard.Cli globally..."
    & dotnet pack src\DataGuard.Cli/DataGuard.Cli.csproj -c Release --output "$verificationRoot\packages" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "dotnet pack failed with exit code $LASTEXITCODE" }
    
    $nupkg = @(Get-ChildItem "$verificationRoot\packages" -Filter "DataGuard.Cli.*.nupkg") | Select-Object -First 1
    if (-not $nupkg) { throw "Could not find packed nupkg" }
    $version = $nupkg.Name -replace 'DataGuard\.Cli\.', '' -replace '\.nupkg', ''
    
    & dotnet tool install --global --add-source "$verificationRoot\packages" DataGuard.Cli --version $version | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "dotnet tool install failed with exit code $LASTEXITCODE" }
    $dteProcess = Start-Process -FilePath $devenv -ArgumentList "/RootSuffix", $RootSuffix, $slnPath -PassThru
    $dte = [RotHelper]::GetDTE($dteProcess.Id, $TimeoutSeconds)
    Write-Host "Relaunching DTE to pick up global tool..."
    Invoke-WithRetry { $dte.Quit() }
    $dteProcess.WaitForExit(10000)
    if (-not $dteProcess.HasExited) { Stop-Process -Id $dteProcess.Id -Force }
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($dte) | Out-Null
    $dteProcess = Start-Process -FilePath $devenv -ArgumentList "/RootSuffix", $RootSuffix, $slnPath -PassThru
    $dte = [RotHelper]::GetDTE($dteProcess.Id, $TimeoutSeconds)
    if (-not $dte) { throw "Failed to re-attach to DTE" }
    Write-Host "Waiting for Visual Studio to initialize extensions (20s)..."
    Start-Sleep -Seconds 20
    
    $properties = $null
    $startWait = [DateTime]::UtcNow
    while (($([DateTime]::UtcNow) - $startWait).TotalSeconds -lt $TimeoutSeconds) {
        try {
            $properties = $dte.Properties("DataGuard", "General")
            break
        } catch {
            Start-Sleep -Seconds 1
        }
    }
    if (-not $properties) { throw "Timed out waiting for DataGuard properties." }
    Invoke-WithRetry {
        $properties.Item("EnableDetailedLogging").Value = $true
        $properties.Item("CustomLogDirectory").Value = $startedLogDir
        $properties.Item("CustomCliPath").Value = ""
    }
    
    Write-Host "Executing global tool validation..."
    Invoke-WithRetry { $dte.ExecuteCommand("Tools.RunValidation") }
    $startPoll = [DateTime]::UtcNow
    $startedFound = $false
    $missingCliFound = $false
    while (([DateTime]::UtcNow - $startPoll).TotalSeconds -lt 25) {
        if (Test-Path $startedLogDir) {
            $logFiles = Get-ChildItem -Path $startedLogDir -Filter "*.log"
            foreach ($logFile in $logFiles) {
                $content = Get-Content $logFile.FullName -Raw
                if ($content -match "\[DataGuard\] validate started\.") { $startedFound = $true }
                if ($content -match [regex]::Escape($expectedMsg)) { $missingCliFound = $true }
            }
        }
        if ($startedFound) { break }
        Start-Sleep -Seconds 1
    }
    if (-not $startedFound) { Write-Error "Global tool test: Did not find 'validate started.' in log." }
    if ($missingCliFound) { Write-Error "Global tool test: Found missing CLI message, which should be absent." }
    
    $results["Global-tool start"] = "PASS"

    Write-Host "Uninstalling global tool for custom path tests..."
    & dotnet tool uninstall --global DataGuard.Cli | Out-Null
    $toolInstalled = $false
    Write-Host "Executing custom CLI path tests..."
    Invoke-WithRetry {
        $properties.Item("CustomLogDirectory").Value = $customPathLogDir
        $properties.Item("CustomCliPath").Value = "$verificationRoot\missing\dataguard.exe"
    }
    Invoke-WithRetry { $dte.ExecuteCommand("Tools.RunValidation") }
    
    $cmdPath = Join-Path $verificationRoot "not-a-launcher.cmd"
    Set-Content -Path $cmdPath -Value "@echo off"
    Invoke-WithRetry { $properties.Item("CustomCliPath").Value = $cmdPath }
    Invoke-WithRetry { $dte.ExecuteCommand("Tools.RunValidation") }
    $startPoll = [DateTime]::UtcNow
    $foundCount = 0
    $failedToStartFound = $false
    while (([DateTime]::UtcNow - $startPoll).TotalSeconds -lt 25) {
        $foundCount = 0
        $failedToStartFound = $false
        if (Test-Path $customPathLogDir) {
            $logFiles = Get-ChildItem -Path $customPathLogDir -Filter "*.log"
            foreach ($logFile in $logFiles) {
                $content = Get-Content $logFile.FullName -Raw
                if ($content -match "Failed to start") { $failedToStartFound = $true }
                $foundCount += ([regex]::Matches($content, [regex]::Escape($expectedMsg))).Count
            }
        }
        if ($foundCount -ge 2 -and -not $failedToStartFound) { break }
        Start-Sleep -Seconds 1
    }
    if ($foundCount -lt 2) { Write-Error "Custom path test: Did not find missing CLI message twice. Found $foundCount times." }
    if ($failedToStartFound) { Write-Error "Custom path test: Found 'Failed to start' in log." }
    
    if (Test-Path $tempDataGuardPath) {
        $postTempDirs = @(Get-ChildItem -Path $tempDataGuardPath -Directory | Select-Object -ExpandProperty Name)
        $newDirs = Compare-Object $preTempDirs $postTempDirs | Where-Object { $_.SideIndicator -eq "=>" }
        if ($newDirs) { Write-Error "Custom path test: Found new unexpected directories in $tempDataGuardPath." }
    }
    
    $results["Nonexistent custom path & .cmd rejection"] = "PASS"

    Write-Host "✅ All validations completed."

} finally {
    Write-Host "`n--- Teardown ---"
    if ($dte) {
        try { Invoke-WithRetry -ScriptBlock { $dte.Quit() } -TimeoutSeconds 5 } catch {}
    }
    if ($dteProcess -and -not $dteProcess.HasExited) {
        $dteProcess.WaitForExit(5000)
        if (-not $dteProcess.HasExited) {
            Stop-Process -Id $dteProcess.Id -Force -ErrorAction SilentlyContinue
        }
    }
    if ($toolInstalled) {
        Write-Host "Uninstalling global DataGuard.Cli tool..."
        Start-Process dotnet -ArgumentList "tool uninstall --global DataGuard.Cli" -Wait -NoNewWindow
    }
    if ($verificationRoot -and (Test-Path $verificationRoot)) {
        Write-Host "Removing temp files..."
        Remove-Item -Path $verificationRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
    if ($vsixDeploymentPath -and (Test-Path $vsixDeploymentPath)) {
        Write-Host "Removing isolated extension deployment: $vsixDeploymentPath"
        Remove-Item -Path $vsixDeploymentPath -Recurse -Force -ErrorAction SilentlyContinue
    }
    if ($RootSuffix -and $RootSuffix -ne "Exp") {
        Write-Host "Removing isolated registry keys for suffix _$RootSuffix..."
        $hkcuRoot = "HKCU:\Software\Microsoft\VisualStudio"
        if (Test-Path $hkcuRoot) {
            Get-ChildItem -Path $hkcuRoot | Where-Object { (Split-Path $_.PSPath -Leaf) -like "*_$RootSuffix" } | ForEach-Object {
                Remove-Item -Path $_.PSPath -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }
    
    Write-Host "`n=== Verification Results ==="
    $results.GetEnumerator() | ForEach-Object {
        $status = if ($_.Value -eq "PASS") { "[PASS]" } else { "[FAIL]" }
        Write-Host "$status $($_.Name)"
    }
}
