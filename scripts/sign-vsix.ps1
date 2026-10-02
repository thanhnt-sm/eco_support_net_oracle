<#
.SYNOPSIS
Digitally signs a Visual Studio VSIX package using OpenVsixSignTool or VsixSignTool.

.DESCRIPTION
Signs a .vsix package using a PFX file or a certificate from the Windows Certificate Store.
Automatically detects OpenVsixSignTool (via PATH or ~/.dotnet/tools) or VsixSignTool.exe
(via Visual Studio SDK / vswhere).

.PARAMETER VsixPath
Path to the .vsix package to sign.

.PARAMETER CertificatePath
Path to the .pfx or .p12 certificate file.

.PARAMETER CertificatePasswordEnv
Name of the environment variable containing the PFX password (default: DATAGUARD_VSIX_CERT_PASSWORD).

.PARAMETER CertificatePassword
Direct password for the PFX file (recommended to use CertificatePasswordEnv instead to avoid CLI history leaks).

.PARAMETER CertificateThumbprint
SHA-1 thumbprint of the certificate in the Windows Certificate Store (CurrentUser\My or LocalMachine\My).

.PARAMETER TimestampServer
RFC 3161 timestamp server URL (default: http://timestamp.digicert.com).

.PARAMETER NoTimestamp
Switch to skip timestamping (useful for offline builds or test fixtures).

.PARAMETER Force
Switch to overwrite any existing digital signature in the package.

.PARAMETER ResolveOnly
Switch to only resolve and return the path of the signing tool without performing signing.
#>

[CmdletBinding()]
param (
    [Parameter(Mandatory = $false, Position = 0)]
    [Alias("Path")]
    [string]$VsixPath,

    [Parameter(Mandatory = $false)]
    [string]$CertificatePath,

    [Parameter(Mandatory = $false)]
    [string]$CertificatePasswordEnv = 'DATAGUARD_VSIX_CERT_PASSWORD',

    [Parameter(Mandatory = $false)]
    [string]$CertificatePassword,

    [Parameter(Mandatory = $false)]
    [string]$CertificateThumbprint,

    [Parameter(Mandatory = $false)]
    [string]$TimestampServer = 'http://timestamp.digicert.com',

    [Parameter(Mandatory = $false)]
    [switch]$NoTimestamp,

    [Parameter(Mandatory = $false)]
    [switch]$Force,

    [Parameter(Mandatory = $false)]
    [switch]$ResolveOnly
)

$ErrorActionPreference = 'Stop'

function Resolve-VsixSignTool {
    [CmdletBinding()]
    param ()

    # 1. Check OpenVsixSignTool in PATH
    $cmd = Get-Command OpenVsixSignTool -ErrorAction SilentlyContinue
    if ($null -ne $cmd) {
        return $cmd.Source
    }

    # 2. Check ~/.dotnet/tools/OpenVsixSignTool.exe directly
    if ($null -ne $env:USERPROFILE) {
        $dotnetToolPath = Join-Path $env:USERPROFILE ".dotnet\tools\OpenVsixSignTool.exe"
        if (Test-Path -LiteralPath $dotnetToolPath) {
            return $dotnetToolPath
        }
    }

    # 3. Check Visual Studio SDK VsixSignTool via vswhere
    $vswherePaths = @()
    if ($null -ne ${env:ProgramFiles(x86)}) {
        $vswherePaths += Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    }
    if ($null -ne $env:ProgramFiles) {
        $vswherePaths += Join-Path $env:ProgramFiles "Microsoft Visual Studio\Installer\vswhere.exe"
    }
    $vswhere = $vswherePaths | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

    if (-not [string]::IsNullOrWhiteSpace($vswhere)) {
        $vsInstallPaths = & $vswhere -all -prerelease -property installationPath
        foreach ($vsPath in $vsInstallPaths) {
            if (-not [string]::IsNullOrWhiteSpace($vsPath) -and (Test-Path -LiteralPath $vsPath)) {
                $candidates = Get-ChildItem -Path $vsPath -Filter "VsixSignTool.exe" -Recurse -File -ErrorAction SilentlyContinue
                if ($candidates) {
                    return $candidates[0].FullName
                }
            }
        }
    }

    throw "VSIX signing tool not found. Please install OpenVsixSignTool via 'dotnet tool install -g OpenVsixSignTool' or install the Visual Studio SDK component."
}

$toolPath = Resolve-VsixSignTool

if ($ResolveOnly) {
    return $toolPath
}

# Validation: VsixPath
if ([string]::IsNullOrWhiteSpace($VsixPath)) {
    throw "Parameter -VsixPath is required for signing."
}
if (-not (Test-Path -LiteralPath $VsixPath)) {
    throw "Target VSIX file does not exist: $VsixPath"
}
$resolvedVsix = (Resolve-Path -LiteralPath $VsixPath).Path

# Validation: Credential source
$hasCertPath = -not [string]::IsNullOrWhiteSpace($CertificatePath)
$hasThumbprint = -not [string]::IsNullOrWhiteSpace($CertificateThumbprint)

if (-not $hasCertPath -and -not $hasThumbprint) {
    throw "No signing credentials provided. Either -CertificatePath (for a .pfx file) or -CertificateThumbprint (for Certificate Store) must be specified."
}

if ($hasCertPath) {
    if (-not (Test-Path -LiteralPath $CertificatePath)) {
        throw "Certificate file does not exist: $CertificatePath"
    }
    $resolvedCertPath = (Resolve-Path -LiteralPath $CertificatePath).Path

    # Resolve password: direct param > environment variable
    $effectivePassword = $CertificatePassword
    if ([string]::IsNullOrWhiteSpace($effectivePassword)) {
        if (-not [string]::IsNullOrWhiteSpace($CertificatePasswordEnv)) {
            $envVar = Get-Item "env:$CertificatePasswordEnv" -ErrorAction SilentlyContinue
            if ($null -ne $envVar) {
                $effectivePassword = $envVar.Value
            }
        }
    }
} else {
    # Pre-validate CertificateThumbprint in Windows Certificate Store
    $cleanThumb = $CertificateThumbprint.Replace(" ", "").Replace(":", "").ToUpperInvariant()
    $storeCert = Get-Item "Cert:\CurrentUser\My\$cleanThumb" -ErrorAction SilentlyContinue
    if ($null -eq $storeCert) {
        $storeCert = Get-Item "Cert:\LocalMachine\My\$cleanThumb" -ErrorAction SilentlyContinue
    }
    if ($null -eq $storeCert) {
        throw "Certificate with thumbprint '$CertificateThumbprint' not found in Cert:\CurrentUser\My or Cert:\LocalMachine\My."
    }
}

$isVsixSignToolExe = (Split-Path -Leaf $toolPath) -like "VsixSignTool.exe"

Write-Host "Signing VSIX package: $resolvedVsix" -ForegroundColor Cyan
Write-Host "Using tool: $toolPath" -ForegroundColor DarkGray

$importedTempCert = $null
$store = $null
$signingThumbprint = $null

try {
    if ($hasCertPath) {
        # Security enhancement: Temporarily import PFX into CurrentUser\My to sign via thumbprint.
        # This completely prevents leaking the certificate password via CLI process arguments.
        $importFlags = [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::Exportable -bor `
                       [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::PersistKeySet
        try {
            $importedTempCert = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
                $resolvedCertPath,
                $effectivePassword,
                $importFlags
            )
        } catch {
            throw "Failed to open certificate at '$CertificatePath': The password is invalid or certificate file is corrupted ($($_.Exception.Message))."
        }

        $store = [System.Security.Cryptography.X509Certificates.X509Store]::new("My", "CurrentUser")
        $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
        $store.Add($importedTempCert)
        $signingThumbprint = $importedTempCert.Thumbprint
    } else {
        $signingThumbprint = $CertificateThumbprint.Replace(" ", "").Replace(":", "").ToUpperInvariant()
    }

    $toolArgs = @("sign")

    if ($isVsixSignToolExe) {
        # VsixSignTool.exe CLI syntax (using /sha1 to avoid CLI password exposure)
        $toolArgs += @("/fd", "sha256")
        $toolArgs += @("/sha1", $signingThumbprint)

        if (-not $NoTimestamp -and -not [string]::IsNullOrWhiteSpace($TimestampServer)) {
            $toolArgs += @("/tr", $TimestampServer, "/td", "sha256")
        }
        if ($Force) {
            $toolArgs += "/f"
        }
        $toolArgs += $resolvedVsix
    } else {
        # OpenVsixSignTool CLI syntax (using --sha1 to avoid CLI password exposure)
        $toolArgs += @("--file-digest", "sha256")
        $toolArgs += @("--sha1", $signingThumbprint)

        if (-not $NoTimestamp -and -not [string]::IsNullOrWhiteSpace($TimestampServer)) {
            $toolArgs += @("-t", $TimestampServer, "-ta", "sha256")
        }
        if ($Force) {
            $toolArgs += "-f"
        }
        $toolArgs += $resolvedVsix
    }

    & $toolPath @toolArgs
    if ($LASTEXITCODE -ne 0) {
        throw "VSIX signing failed with exit code $LASTEXITCODE"
    }
} finally {
    # Secure cleanup: Remove temporary cert from store, purge private key container, and reset cert
    if ($null -ne $importedTempCert) {
        $tempThumb = $importedTempCert.Thumbprint
        try {
            if ($null -ne $store) {
                $store.Remove($importedTempCert)
            }
        } catch {}
        if ($null -ne $store) {
            $store.Close()
            if ($store -is [System.IDisposable]) {
                $store.Dispose()
            }
        }
        if (-not [string]::IsNullOrWhiteSpace($tempThumb)) {
            $certStoreItem = "Cert:\CurrentUser\My\$tempThumb"
            if (Test-Path -LiteralPath $certStoreItem) {
                Remove-Item -LiteralPath $certStoreItem -DeleteKey -Force -ErrorAction SilentlyContinue
            }
        }
        try {
            $importedTempCert.Reset()
        } catch {}
    }
}

Write-Host "VSIX package signed successfully: $resolvedVsix" -ForegroundColor Green
