[CmdletBinding()]
param (
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $OutputPath = Join-Path $scriptDir "fixtures/minimal-unsigned.vsix"
}

$outputDir = Split-Path -Parent $OutputPath
if (-not (Test-Path -LiteralPath $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

if (Test-Path -LiteralPath $OutputPath) {
    Remove-Item -LiteralPath $OutputPath -Force
}

$contentTypesXml = @'
<?xml version="1.0" encoding="utf-8"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="vsixmanifest" ContentType="text/xml" />
</Types>
'@

$vsixManifestXml = @'
<?xml version="1.0" encoding="utf-8"?>
<PackageManifest Version="2.0.0" xmlns="http://schemas.microsoft.com/developer/vsx-schema/2011">
  <Metadata>
    <Identity Id="DataGuard.TestExtension" Version="0.1.0" Language="en-US" Publisher="DataGuard" />
    <DisplayName>DataGuard Test Extension</DisplayName>
    <Description>Fixture for VSIX signing and verification tests</Description>
  </Metadata>
  <Installation>
    <InstallationTarget Id="Microsoft.VisualStudio.Community" Version="[17.0, 19.0)" />
  </Installation>
  <Dependencies />
  <Prerequisites />
</PackageManifest>
'@

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$zip = [System.IO.Compression.ZipFile]::Open($OutputPath, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    # 1. Add [Content_Types].xml
    $ctEntry = $zip.CreateEntry("[Content_Types].xml", [System.IO.Compression.CompressionLevel]::Optimal)
    $writer = New-Object System.IO.StreamWriter($ctEntry.Open(), [System.Text.Encoding]::UTF8)
    try {
        $writer.Write($contentTypesXml)
    } finally {
        $writer.Dispose()
    }

    # 2. Add extension.vsixmanifest
    $manEntry = $zip.CreateEntry("extension.vsixmanifest", [System.IO.Compression.CompressionLevel]::Optimal)
    $writer = New-Object System.IO.StreamWriter($manEntry.Open(), [System.Text.Encoding]::UTF8)
    try {
        $writer.Write($vsixManifestXml)
    } finally {
        $writer.Dispose()
    }
} finally {
    $zip.Dispose()
}

Write-Host "Created minimal unsigned fixture at: $OutputPath" -ForegroundColor Green
