<#
.SYNOPSIS
  Asserts a DataGuard Visual Studio VSIX contains the bundled CLI, the Roslyn analyzers with the
  two assemblies they load (Contracts, SqlClassification), the package definition and a manifest
  whose version matches the expected one.

.DESCRIPTION
  Shared by .github/workflows/ci.yml and release.yml so both paths enforce the same gate.
  Exits 0 on success; exits 1 with a single clear message on any failure (missing file,
  unreadable zip, missing entry, missing/mismatched manifest version).

.EXAMPLE
  pwsh -File scripts/assert-vsix.ps1 -VsixPath src/DataGuard.VisualStudio/bin/Release/DataGuard.VisualStudio.vsix -ExpectedVersion 0.4.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $VsixPath,
    [Parameter(Mandatory = $true)] [string] $ExpectedVersion,
    [string] $SourceRoot = (Split-Path $PSScriptRoot -Parent),
    [switch] $RequireReleaseIdentity
)

$ErrorActionPreference = 'Stop'

# The analyzer references DataGuard.Contracts and DataGuard.SqlClassification at load time (the NuGet
# package bundles them for the same reason); without them VS cannot load DataGuard.Analyzers.dll.
$RequiredEntries = @(
    'cli/dataguard.exe',
    'DataGuard.Analyzers.dll',
    'DataGuard.CodeFixes.dll',
    'DataGuard.Contracts.dll',
    'DataGuard.SqlClassification.dll',
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

    # Validate the actual embedded legal bytes and the documented GPL version boundary.
    # Use the selected source's canonical notices; no license text is generated or rewritten.
    $policyScript = Join-Path $PSScriptRoot 'check-license-consistency.py'
    if (-not (Get-Command python -ErrorAction SilentlyContinue)) { Fail 'Python 3 is required for VSIX license policy inspection' }
    & python $policyScript --root $SourceRoot --vsix $vsix.FullName --expected-version $ExpectedVersion
    if ($LASTEXITCODE -ne 0) { Fail 'VSIX license/version policy inspection failed' }

    if ($RequireReleaseIdentity) {
        # Read PE resources/assembly metadata only; never execute binaries from an archive.
        $scratch = Join-Path ([System.IO.Path]::GetTempPath()) ([System.Guid]::NewGuid().ToString('N'))
        [System.IO.Directory]::CreateDirectory($scratch) | Out-Null
        try {
            $binaryNames = @('DataGuard.VisualStudio.dll', 'DataGuard.Analyzers.dll', 'DataGuard.CodeFixes.dll',
                             'DataGuard.Contracts.dll', 'DataGuard.SqlClassification.dll', 'cli/dataguard.exe')
            foreach ($name in $binaryNames) {
                $entry = $zip.Entries | Where-Object { $_.FullName.Replace('\', '/') -ceq $name } | Select-Object -First 1
                if ($entry.Length -le 0 -or $entry.Length -gt 512MB) { throw "Invalid release binary size: $name" }
                $destination = Join-Path $scratch ([System.IO.Path]::GetFileName($name))
                [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destination)
                $info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($destination)
                if (($info.ProductVersion -split '\+', 2)[0] -cne $ExpectedVersion -or
                    $info.FileMajorPart -ne ([version]$ExpectedVersion).Major -or
                    $info.FileMinorPart -ne ([version]$ExpectedVersion).Minor -or
                    $info.FileBuildPart -ne ([version]$ExpectedVersion).Build -or $info.FilePrivatePart -ne 0) {
                    throw "Release file/product version mismatch: $name"
                }
                if ($name.EndsWith('.dll')) {
                    $assembly = [System.Reflection.AssemblyName]::GetAssemblyName($destination)
                    # MinVer8 intentionally keeps assembly binding identity at major.0.0.0.
                    if ($assembly.Version -ne [version]"$(([version]$ExpectedVersion).Major).0.0.0") { throw "Release assembly version mismatch: $name" }
                }
            }
        } finally {
            Remove-Item -LiteralPath $scratch -Recurse -Force
        }
    }

    $sizeMb = [math]::Round($vsix.Length / 1MB, 1)
    Write-Host "assert-vsix: OK - $($vsix.Name) ($sizeMb MB), version $manifestVersion, $($entries.Count) entries, all $($RequiredEntries.Count) required entries present"
    exit 0
} finally {
    $zip.Dispose()
}
