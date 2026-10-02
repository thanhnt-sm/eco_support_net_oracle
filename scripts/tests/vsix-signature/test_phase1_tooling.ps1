# Test for Phase 1: Tooling resolution and certificate helpers
$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$modulePath = Join-Path $scriptDir "helpers.psm1"
$repoRoot = (Resolve-Path (Join-Path $scriptDir "../../..")).Path
$signVsixScript = Join-Path $repoRoot "scripts/sign-vsix.ps1"

Write-Host "=== [Phase 1 Test] Verifying module and helper existence ===" -ForegroundColor Cyan

if (-not (Test-Path -LiteralPath $modulePath)) {
    throw "helpers.psm1 not found at $modulePath"
}

Import-Module -Name $modulePath -Force

Write-Host "Testing New-TestCodeSigningCert..." -ForegroundColor Cyan
$certInfo = New-TestCodeSigningCert -Subject "CN=DataGuard Phase1 Test Cert"

try {
    if (-not $certInfo) { throw "New-TestCodeSigningCert returned null" }
    if ([string]::IsNullOrWhiteSpace($certInfo.Thumbprint)) { throw "Thumbprint is empty" }
    if (-not (Test-Path -LiteralPath $certInfo.PfxPath)) { throw "PFX file was not created at $($certInfo.PfxPath)" }
    if ([string]::IsNullOrWhiteSpace($certInfo.Password)) { throw "Cert password is empty" }

    Write-Host "Cert successfully created: Thumbprint=$($certInfo.Thumbprint)" -ForegroundColor Green
} finally {
    Write-Host "Cleaning up test cert..." -ForegroundColor Cyan
    Remove-TestCodeSigningCert -CertInfo $certInfo
    if (Test-Path -LiteralPath $certInfo.PfxPath) {
        throw "PFX file still exists after Remove-TestCodeSigningCert: $($certInfo.PfxPath)"
    }
    $certInStore = Get-Item "Cert:\CurrentUser\My\$($certInfo.Thumbprint)" -ErrorAction SilentlyContinue
    if ($null -ne $certInStore) {
        throw "Cert still exists in Windows Certificate Store: $($certInfo.Thumbprint)"
    }
    Write-Host "Cleanup verified clean." -ForegroundColor Green
}

Write-Host "Testing Resolve-VsixSignTool from sign-vsix.ps1..." -ForegroundColor Cyan
if (-not (Test-Path -LiteralPath $signVsixScript)) {
    throw "sign-vsix.ps1 not found at $signVsixScript"
}

# Dot-source or inspect Resolve-VsixSignTool
$tool = & $signVsixScript -ResolveOnly
if ([string]::IsNullOrWhiteSpace($tool)) {
    throw "Resolve-VsixSignTool returned empty path"
}
Write-Host "Resolved VSIX signing tool: $tool" -ForegroundColor Green
Write-Host "=== [Phase 1 Test] All checks passed ===" -ForegroundColor Green
