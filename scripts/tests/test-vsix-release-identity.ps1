# Offline synthetic PE/VSIX regression. This does not build or install the Windows extension.
[CmdletBinding()]
param([string] $SourceRoot = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent))
$ErrorActionPreference = 'Stop'
$checker = Join-Path (Split-Path $PSScriptRoot -Parent) 'assert-vsix.ps1'
$pwsh = (Get-Process -Id $PID).Path
$scratch = Join-Path ([System.IO.Path]::GetTempPath()) ([System.Guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($scratch) | Out-Null
try {
    # SDK-built fixtures carry real PE version resources; Add-Type alone need not emit them.
    [System.IO.File]::WriteAllText((Join-Path $scratch 'Fixture.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>')
    [System.IO.File]::WriteAllText((Join-Path $scratch 'Fixture.cs'), 'public class Fixture { }')
    foreach ($fixture in @('0.4.0', '0.0.0', 'binding')) {
        $version = if ($fixture -eq 'binding') { '0.4.0' } else { $fixture }
        $bindingVersion = if ($fixture -eq 'binding') { '1.0.0.0' } else { '0.0.0.0' }
        $output = Join-Path $scratch "out-$fixture"
        & dotnet build (Join-Path $scratch 'Fixture.csproj') --configuration Release --output $output `
            "-p:AssemblyVersion=$bindingVersion" "-p:FileVersion=$version.0" "-p:InformationalVersion=$version+fixture"
        if ($LASTEXITCODE -ne 0) { throw 'Failed to compile version fixture' }
        Copy-Item (Join-Path $output 'Fixture.dll') (Join-Path $scratch "$fixture.dll")
    }
    foreach ($scenario in @('matching', 'tagless-cli', 'wrong-analyzer', 'wrong-binding', 'invalid-binary')) {
        $folder = Join-Path $scratch $scenario
        [System.IO.Directory]::CreateDirectory((Join-Path $folder 'cli')) | Out-Null
        $manifest = '<PackageManifest xmlns="http://schemas.microsoft.com/developer/vsx-schema/2011"><Metadata><Identity Version="0.4.0" /><License>LICENSE.txt</License></Metadata></PackageManifest>'
        [System.IO.File]::WriteAllText((Join-Path $folder 'extension.vsixmanifest'), $manifest)
        [System.IO.File]::WriteAllText((Join-Path $folder 'DataGuard.VisualStudio.pkgdef'), 'synthetic')
        Copy-Item (Join-Path $SourceRoot 'LICENSE') (Join-Path $folder 'LICENSE.txt')
        foreach ($notice in @('THIRD-PARTY-NOTICES.md', 'ADDITIONAL-PERMISSIONS.md')) {
            Copy-Item (Join-Path $SourceRoot "docs/legal/$notice") (Join-Path $folder $notice)
        }
        foreach ($name in @('DataGuard.VisualStudio.dll', 'DataGuard.Analyzers.dll', 'DataGuard.CodeFixes.dll',
                            'DataGuard.Contracts.dll', 'DataGuard.SqlClassification.dll', 'cli/dataguard.exe')) {
            $version = '0.4.0'
            if (($scenario -eq 'tagless-cli' -and $name -eq 'cli/dataguard.exe') -or
                ($scenario -eq 'wrong-analyzer' -and $name -eq 'DataGuard.Analyzers.dll')) { $version = '0.0.0' }
            if ($scenario -eq 'wrong-binding' -and $name -eq 'DataGuard.Analyzers.dll') { $version = 'binding' }
            Copy-Item (Join-Path $scratch "$version.dll") (Join-Path $folder $name)
        }
        if ($scenario -eq 'invalid-binary') {
            [System.IO.File]::WriteAllText((Join-Path $folder 'DataGuard.VisualStudio.dll'), 'not a PE binary')
        }
        $package = Join-Path $scratch "$scenario.vsix"
        [System.IO.Compression.ZipFile]::CreateFromDirectory($folder, $package)
        & $pwsh -NoProfile -File $checker -VsixPath $package -ExpectedVersion '0.4.0' -SourceRoot $SourceRoot -RequireReleaseIdentity
        $result = $LASTEXITCODE
        if (($scenario -eq 'matching' -and $result -ne 0) -or ($scenario -ne 'matching' -and $result -eq 0)) {
            throw "Unexpected identity gate result for ${scenario}: $result"
        }
    }
    Write-Host 'VSIX synthetic identity regressions passed (not Windows installation proof).'
} finally {
    Remove-Item -LiteralPath $scratch -Recurse -Force
}

# The final negative fixture intentionally leaves a nonzero native exit code.
exit 0
