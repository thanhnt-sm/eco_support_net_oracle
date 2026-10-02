# Test for Phase 3: sign-vsix.ps1 functionality and build-extensions.ps1 integration
$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptDir "../../..")).Path
$signVsixScript = Join-Path $repoRoot "scripts/sign-vsix.ps1"
$verifyScript = Join-Path $repoRoot "scripts/verify-vsix-signature.ps1"
$buildExtScript = Join-Path $repoRoot "scripts/build-extensions.ps1"
$fixturePath = Join-Path $scriptDir "fixtures/minimal-unsigned.vsix"
$helpersPath = Join-Path $scriptDir "helpers.psm1"

Import-Module -Name $helpersPath -Force

Write-Host "=== [Phase 3 Test] Verifying sign-vsix.ps1 and build-extensions.ps1 ===" -ForegroundColor Cyan

$powerShellExe = if ($PSVersionTable.PSEdition -eq 'Core') { 'pwsh' } else { 'powershell.exe' }

# Test 1: Calling sign-vsix.ps1 with non-existent cert must fail
Write-Host "Test 1: sign-vsix with non-existent CertificatePath must fail..." -ForegroundColor Cyan
$proc1 = Start-Process -FilePath $powerShellExe -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$signVsixScript`" -VsixPath `"$fixturePath`" -CertificatePath `"C:\does_not_exist_cert.pfx`"" -NoNewWindow -Wait -PassThru
if ($proc1.ExitCode -eq 0) {
    throw "Expected sign-vsix.ps1 to fail with non-existent cert, but got exit code 0."
}
Write-Host "Test 1 Passed." -ForegroundColor Green

# Test 2: Calling sign-vsix.ps1 without cert or thumbprint must fail
Write-Host "Test 2: sign-vsix without any credentials must fail..." -ForegroundColor Cyan
$proc2 = Start-Process -FilePath $powerShellExe -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$signVsixScript`" -VsixPath `"$fixturePath`"" -NoNewWindow -Wait -PassThru
if ($proc2.ExitCode -eq 0) {
    throw "Expected sign-vsix.ps1 to fail when no cert/thumbprint is provided, but got exit code 0."
}
Write-Host "Test 2 Passed." -ForegroundColor Green

# Test 3: Sign fixture using sign-vsix.ps1 with PFX and PasswordEnv
Write-Host "Test 3: sign-vsix with valid PFX and PasswordEnv..." -ForegroundColor Cyan
$testPkg = Join-Path $scriptDir "fixtures/phase3-test.vsix"
Copy-Item -LiteralPath $fixturePath -Destination $testPkg -Force
$certInfo = New-TestCodeSigningCert -Subject "CN=DataGuard Phase3 Test"

try {
    $env:DATAGUARD_TEST_CERT_PASS = $certInfo.Password
    $proc3 = Start-Process -FilePath $powerShellExe -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$signVsixScript`" -VsixPath `"$testPkg`" -CertificatePath `"$($certInfo.PfxPath)`" -CertificatePasswordEnv DATAGUARD_TEST_CERT_PASS -NoTimestamp" -NoNewWindow -Wait -PassThru
    if ($proc3.ExitCode -ne 0) {
        throw "sign-vsix.ps1 failed with exit code $($proc3.ExitCode)"
    }

    # Verify signature
    $procVerify = Start-Process -FilePath $powerShellExe -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$verifyScript`" -VsixPath `"$testPkg`" -ExpectedThumbprint `"$($certInfo.Thumbprint)`"" -NoNewWindow -Wait -PassThru
    if ($procVerify.ExitCode -ne 0) {
        throw "verify-vsix-signature.ps1 failed on package signed by sign-vsix.ps1 (exit code $($procVerify.ExitCode))"
    }
    Write-Host "Test 3 Passed." -ForegroundColor Green
} finally {
    Remove-TestCodeSigningCert -CertInfo $certInfo
    Remove-Item -LiteralPath $testPkg -Force -ErrorAction SilentlyContinue
    Remove-Item env:DATAGUARD_TEST_CERT_PASS -ErrorAction SilentlyContinue
}

# Test 4: build-extensions.ps1 fail-fast when signing requested with non-existent cert
Write-Host "Test 4: build-extensions.ps1 fail-fast on invalid signing configuration..." -ForegroundColor Cyan
$proc4 = Start-Process -FilePath $powerShellExe -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$buildExtScript`" -SkipVSCode -SignVsix -CertificatePath `"C:\does_not_exist_cert.pfx`"" -NoNewWindow -Wait -PassThru
if ($proc4.ExitCode -eq 0) {
    throw "Expected build-extensions.ps1 to fail with non-existent cert, but got exit code 0."
}
Write-Host "Test 4 Passed." -ForegroundColor Green
# Test 5: sign-vsix with wrong password must fail
Write-Host "Test 5: sign-vsix with wrong password must fail..." -ForegroundColor Cyan
$certInfo5 = New-TestCodeSigningCert -Subject "CN=DataGuard Test 5"
try {
    $proc5 = Start-Process -FilePath $powerShellExe -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$signVsixScript`" -VsixPath `"$fixturePath`" -CertificatePath `"$($certInfo5.PfxPath)`" -CertificatePassword `"WRONG_PASS`" -NoTimestamp" -NoNewWindow -Wait -PassThru
    if ($proc5.ExitCode -eq 0) {
        throw "Expected sign-vsix.ps1 to fail with invalid password, but got exit code 0."
    }
    Write-Host "Test 5 Passed." -ForegroundColor Green
} finally {
    Remove-TestCodeSigningCert -CertInfo $certInfo5
}

# Test 6: sign-vsix with non-existent thumbprint must fail
Write-Host "Test 6: sign-vsix with non-existent thumbprint must fail..." -ForegroundColor Cyan
$proc6 = Start-Process -FilePath $powerShellExe -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$signVsixScript`" -VsixPath `"$fixturePath`" -CertificateThumbprint `"0000000000000000000000000000000000000000`" -NoTimestamp" -NoNewWindow -Wait -PassThru
if ($proc6.ExitCode -eq 0) {
    throw "Expected sign-vsix.ps1 to fail with non-existent thumbprint, but got exit code 0."
}
Write-Host "Test 6 Passed." -ForegroundColor Green

Write-Host "=== [Phase 3 Test] All tests passed ===" -ForegroundColor Green
