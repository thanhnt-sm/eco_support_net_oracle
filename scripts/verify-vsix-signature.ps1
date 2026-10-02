<#
.SYNOPSIS
Verifies the OPC digital signature of a Visual Studio VSIX package.

.DESCRIPTION
Checks whether a given .vsix package has a valid Open Packaging Conventions (OPC)
digital signature, verifies package integrity against tampering, and optionally
validates that the signing certificate matches an expected thumbprint or subject.

.PARAMETER VsixPath
Path to the .vsix package to verify.

.PARAMETER ExpectedThumbprint
Optional SHA-1 thumbprint of the expected signing certificate.

.PARAMETER ExpectedSubject
Optional subject substring expected in the signing certificate.
#>

[CmdletBinding()]
param (
    [Parameter(Mandatory = $true, Position = 0)]
    [Alias("Path")]
    [string]$VsixPath,

    [Parameter(Mandatory = $false)]
    [string]$ExpectedThumbprint,

    [Parameter(Mandatory = $false)]
    [string]$ExpectedSubject
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $VsixPath)) {
    Write-Error "VSIX file not found: $VsixPath"
    exit 1
}

$resolvedVsixPath = (Resolve-Path -LiteralPath $VsixPath).Path

try {
    Add-Type -AssemblyName WindowsBase -ErrorAction SilentlyContinue
} catch {}

$package = $null
try {
    try {
        $package = [System.IO.Packaging.Package]::Open(
            $resolvedVsixPath,
            [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::Read,
            [System.IO.FileShare]::Read
        )
    } catch {
        Write-Error "Failed to open VSIX package as an OPC archive: $($_.Exception.Message)"
        exit 1
    }

    $sigManager = New-Object System.IO.Packaging.PackageDigitalSignatureManager($package)

    if (-not $sigManager.IsSigned) {
        Write-Error "VSIX package is not signed (no digital signature found): $resolvedVsixPath"
        exit 1
    }

    $verifyResult = $sigManager.VerifySignatures($true)
    if ($verifyResult -ne [System.IO.Packaging.VerifyResult]::Success) {
        Write-Error "VSIX digital signature is invalid or tampered: $verifyResult"
        exit 1
    }

    $signatures = @($sigManager.Signatures)
    if ($signatures.Count -eq 0) {
        Write-Error "Package reported IsSigned=true but no signatures were found."
        exit 1
    }

    # Verify ExpectedThumbprint if supplied
    if (-not [string]::IsNullOrWhiteSpace($ExpectedThumbprint)) {
        $cleanExpected = $ExpectedThumbprint.Replace(" ", "").Replace(":", "").ToUpperInvariant()
        $matched = $false
        foreach ($sig in $signatures) {
            $signerThumbprint = $sig.Signer.Thumbprint.ToUpperInvariant()
            if ($signerThumbprint -eq $cleanExpected) {
                $matched = $true
                break
            }
        }
        if (-not $matched) {
            $actualThumbprints = ($signatures | ForEach-Object { $_.Signer.Thumbprint }) -join ", "
            Write-Error "Certificate thumbprint mismatch. Expected: '$cleanExpected', Found: [$actualThumbprints]"
            exit 1
        }
    }

    # Verify ExpectedSubject if supplied
    if (-not [string]::IsNullOrWhiteSpace($ExpectedSubject)) {
        $matched = $false
        foreach ($sig in $signatures) {
            if ($sig.Signer.Subject -like "*$ExpectedSubject*") {
                $matched = $true
                break
            }
        }
        if (-not $matched) {
            $actualSubjects = ($signatures | ForEach-Object { $_.Signer.Subject }) -join " | "
            Write-Error "Certificate subject mismatch. Expected substring: '$ExpectedSubject', Found: [$actualSubjects]"
            exit 1
        }
    }

    Write-Host "VSIX digital signature is VALID: $resolvedVsixPath" -ForegroundColor Green
    foreach ($sig in $signatures) {
        Write-Host "  Signer: $($sig.Signer.Subject)" -ForegroundColor Cyan
        Write-Host "  Thumbprint: $($sig.Signer.Thumbprint)" -ForegroundColor Cyan
        Write-Host "  Signing Time: $($sig.SigningTime)" -ForegroundColor DarkGray
    }

    exit 0
} finally {
    if ($null -ne $package) {
        $package.Close()
    }
}
