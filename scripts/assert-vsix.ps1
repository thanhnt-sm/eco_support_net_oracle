<#
.SYNOPSIS
  Asserts a DataGuard Visual Studio VSIX contains the bundled CLI, the Roslyn analyzers, the
  package definition and a manifest whose version matches the expected one.

.DESCRIPTION
  Shared by .github/workflows/ci.yml and release.yml so both paths enforce the same gate.
  Exits 0 on success; exits 1 with a single clear message on any failure (missing file,
  unreadable zip, missing entry, missing/mismatched manifest version).

.EXAMPLE
  pwsh -File scripts/assert-vsix.ps1 -VsixPath src/DataGuard.VisualStudio/bin/Release/DataGuard.VisualStudio.vsix -ExpectedVersion 0.3.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $VsixPath,
    [Parameter(Mandatory = $true)] [string] $ExpectedVersion
)

$ErrorActionPreference = 'Stop'

$RequiredEntries = @(
    'cli/dataguard.exe',
    'DataGuard.Analyzers.dll',
    'DataGuard.CodeFixes.dll',
    'extension.vsixmanifest',
    'DataGuard.VisualStudio.dll',
    'DataGuard.VisualStudio.pkgdef'
)

function Fail([string] $Message) {
    Write-Host "assert-vsix: FAIL - $Message"
    exit 1
}

if ([string]::IsNullOrWhiteSpace($ExpectedVersion)) { Fail 'ExpectedVersion is empty' }
if (-not (Test-Path -LiteralPath $VsixPath -PathType Leaf)) { Fail "VSIX not found at '$VsixPath'" }

$vsix = Get-Item -LiteralPath $VsixPath
Add-Type -AssemblyName System.IO.Compression.FileSystem

try {
    $zip = [System.IO.Compression.ZipFile]::OpenRead($vsix.FullName)
} catch {
    Fail "cannot open '$VsixPath' as a zip archive: $($_.Exception.Message)"
}

try {
    # Normalise separators so a Windows-built archive with backslash entry names still matches.
    $entries = @($zip.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
    $missing = @($RequiredEntries | Where-Object { $entries -notcontains $_ })
    if ($missing.Count -gt 0) {
        Fail "VSIX '$($vsix.Name)' is missing required entries: $($missing -join ', '). Entries present: $($entries -join ', ')"
    }

    $manifestEntry = $zip.Entries | Where-Object { $_.FullName.Replace('\', '/') -eq 'extension.vsixmanifest' } | Select-Object -First 1
    $reader = New-Object System.IO.StreamReader($manifestEntry.Open())
    try { $manifestText = $reader.ReadToEnd() } finally { $reader.Dispose() }

    try { [xml] $manifest = $manifestText } catch { Fail "extension.vsixmanifest is not valid XML: $($_.Exception.Message)" }
    $manifestVersion = $manifest.PackageManifest.Metadata.Identity.Version
    if ([string]::IsNullOrWhiteSpace($manifestVersion)) { Fail 'extension.vsixmanifest has no PackageManifest/Metadata/Identity/@Version' }
    if ($manifestVersion -ne $ExpectedVersion) {
        Fail "manifest version '$manifestVersion' does not match expected version '$ExpectedVersion'"
    }

    $sizeMb = [math]::Round($vsix.Length / 1MB, 1)
    Write-Host "assert-vsix: OK - $($vsix.Name) ($sizeMb MB), version $manifestVersion, $($entries.Count) entries, all $($RequiredEntries.Count) required entries present"
    exit 0
} finally {
    $zip.Dispose()
}
