[CmdletBinding()]
param (
    [switch]$VerboseOutput
)

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptDir "../../..")).Path
$helpersPath = Join-Path $scriptDir "helpers.psm1"
$verifyScript = Join-Path $repoRoot "scripts/verify-vsix-signature.ps1"
$fixturePath = Join-Path $scriptDir "fixtures/minimal-unsigned.vsix"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  DataGuard VSIX Digital Signature Test Suite (TDD)       " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

if (-not (Test-Path -LiteralPath $fixturePath)) {
    Write-Host "Fixture minimal-unsigned.vsix not found. Generating..." -ForegroundColor Yellow
    & (Join-Path $scriptDir "create-minimal-fixture.ps1") -OutputPath $fixturePath
}

Import-Module -Name $helpersPath -Force

$passCount = 0
$failCount = 0

function Assert-Test {
    param (
        [string]$TestName,
        [scriptblock]$Action
    )

    Write-Host "`n[TEST] $TestName..." -NoNewline
    try {
        & $Action
        Write-Host " [PASS]" -ForegroundColor Green
        $script:passCount++
    } catch {
        Write-Host " [FAIL]" -ForegroundColor Red
        Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
        $script:failCount++
    }
}

# Ensure verify script exists
if (-not (Test-Path -LiteralPath $verifyScript)) {
    Write-Host "FATAL: verify-vsix-signature.ps1 not found at $verifyScript" -ForegroundColor Red
    exit 1
}

$tempSignedVsix = Join-Path $scriptDir "fixtures/temp-signed.vsix"
$tempTamperedVsix = Join-Path $scriptDir "fixtures/temp-tampered.vsix"
$certInfo = $null

try {
    # Setup test cert and signed package
    Write-Host "`nSetting up test code-signing certificate..." -ForegroundColor DarkGray
    $certInfo = New-TestCodeSigningCert -Subject "CN=DataGuard Suite Test Cert"

    Copy-Item -LiteralPath $fixturePath -Destination $tempSignedVsix -Force
    OpenVsixSignTool sign --file-digest sha256 -c $certInfo.PfxPath -p $certInfo.Password $tempSignedVsix | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to sign temp-signed.vsix with OpenVsixSignTool (exit code $LASTEXITCODE)"
    }

    # Helper to invoke verify script in child process and return exit code
    $invokeVerify = {
        param([string]$arguments)
        $powerShellExe = if ($PSVersionTable.PSEdition -eq 'Core') { 'pwsh' } else { 'powershell.exe' }
        $cmd = "-NoProfile -ExecutionPolicy Bypass -File `"$verifyScript`" $arguments"
        $proc = Start-Process -FilePath $powerShellExe -ArgumentList $cmd -NoNewWindow -Wait -PassThru
        return $proc.ExitCode
    }

    # Case 1: Unsigned VSIX -> verify-vsix-signature.ps1 must return exit code 1
    Assert-Test "Case 1: Unsigned VSIX must be rejected" {
        $code = & $invokeVerify "-VsixPath `"$fixturePath`""
        if ($code -eq 0) {
            throw "Expected non-zero exit code for unsigned VSIX, but got 0."
        }
    }

    # Case 2: Signed VSIX with matching thumbprint -> verify-vsix-signature.ps1 must return exit code 0
    Assert-Test "Case 2: Signed VSIX with matching thumbprint must pass" {
        $code = & $invokeVerify "-VsixPath `"$tempSignedVsix`" -ExpectedThumbprint `"$($certInfo.Thumbprint)`""
        if ($code -ne 0) {
            throw "Expected exit code 0 for valid signed VSIX, but got $code."
        }
    }

    # Case 3: Tampered signed VSIX -> verify-vsix-signature.ps1 must return exit code 1
    Assert-Test "Case 3: Tampered signed VSIX must be rejected" {
        Copy-Item -LiteralPath $tempSignedVsix -Destination $tempTamperedVsix -Force
        $bytes = [System.IO.File]::ReadAllBytes($tempTamperedVsix)
        $bytes[200] = [byte]($bytes[200] -bxor 0xFF)
        [System.IO.File]::WriteAllBytes($tempTamperedVsix, $bytes)

        $code = & $invokeVerify "-VsixPath `"$tempTamperedVsix`""
        if ($code -eq 0) {
            throw "Expected non-zero exit code for tampered VSIX, but got 0."
        }
    }

    # Case 4: Mismatched thumbprint -> verify-vsix-signature.ps1 must return exit code 1
    Assert-Test "Case 4: Signed VSIX with mismatched thumbprint must be rejected" {
        $bogusThumbprint = "0000000000000000000000000000000000000000"
        $code = & $invokeVerify "-VsixPath `"$tempSignedVsix`" -ExpectedThumbprint `"$bogusThumbprint`""
        if ($code -eq 0) {
            throw "Expected non-zero exit code for mismatched thumbprint, but got 0."
        }
    }
} finally {
    Write-Host "`nCleaning up test artifacts and certificate..." -ForegroundColor DarkGray
    if ($null -ne $certInfo) {
        Remove-TestCodeSigningCert -CertInfo $certInfo
    }
    Remove-Item -LiteralPath $tempSignedVsix -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $tempTamperedVsix -Force -ErrorAction SilentlyContinue
}

Write-Host "`n----------------------------------------------------------" -ForegroundColor Cyan
$resultColor = if ($failCount -eq 0) { 'Green' } else { 'Red' }
Write-Host "Test Results: $passCount passed, $failCount failed." -ForegroundColor $resultColor
Write-Host "----------------------------------------------------------" -ForegroundColor Cyan

if ($failCount -gt 0) {
    exit 1
}
exit 0
