<#
.SYNOPSIS
Helper functions for creating and cleaning up self-signed test code signing certificates.
#>

function New-TestCodeSigningCert {
    [CmdletBinding()]
    param (
        [string]$Subject = "CN=DataGuard Test Code Signing",
        [string]$PfxPath
    )

    $ErrorActionPreference = 'Stop'

    if ([string]::IsNullOrWhiteSpace($PfxPath)) {
        $tempBase = [System.IO.Path]::GetTempFileName()
        Remove-Item -LiteralPath $tempBase -Force -ErrorAction SilentlyContinue
        $PfxPath = "$tempBase.pfx"
    }

    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $Subject `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -FriendlyName "DataGuard Test Cert" `
        -NotAfter (Get-Date).AddDays(1)

    $plainPassword = [System.Guid]::NewGuid().ToString("N") + "!Aa1"
    $secPassword = ConvertTo-SecureString $plainPassword -AsPlainText -Force

    Export-PfxCertificate -Cert $cert -FilePath $PfxPath -Password $secPassword | Out-Null

    return [PSCustomObject]@{
        Certificate    = $cert
        Thumbprint     = $cert.Thumbprint
        Subject        = $cert.Subject
        PfxPath        = (Resolve-Path -LiteralPath $PfxPath).Path
        Password       = $plainPassword
        SecurePassword = $secPassword
    }
}

function Remove-TestCodeSigningCert {
    [CmdletBinding()]
    param (
        [Parameter(ValueFromPipeline = $true)]
        [object]$CertInfo,
        [string]$Thumbprint,
        [string]$PfxPath
    )

    if ($null -ne $CertInfo) {
        if ($CertInfo.PSObject.Properties['Thumbprint']) {
            $Thumbprint = $CertInfo.Thumbprint
        }
        if ($CertInfo.PSObject.Properties['PfxPath']) {
            $PfxPath = $CertInfo.PfxPath
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($Thumbprint)) {
        $certPath = "Cert:\CurrentUser\My\$Thumbprint"
        if (Test-Path -LiteralPath $certPath) {
            Remove-Item -LiteralPath $certPath -DeleteKey -Force -ErrorAction SilentlyContinue
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($PfxPath) -and (Test-Path -LiteralPath $PfxPath)) {
        Remove-Item -LiteralPath $PfxPath -Force -ErrorAction SilentlyContinue
    }
}

Export-ModuleMember -Function New-TestCodeSigningCert, Remove-TestCodeSigningCert
