param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '1.0.0',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist'),
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1') -Test }
$releaseOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $releaseOutput -Force | Out-Null
$binaryPath = Join-Path $PSScriptRoot 'dist\GlassPulse.exe'
if (-not (Test-Path -LiteralPath $binaryPath)) { throw 'Build GlassPulse.exe before packaging.' }
$packageFiles = @(
    $binaryPath,
    (Join-Path $PSScriptRoot 'LICENSE'),
    (Join-Path $PSScriptRoot 'QUICKSTART.zh-CN.md'),
    (Join-Path $PSScriptRoot 'README.md'),
    (Join-Path $PSScriptRoot 'README.zh-CN.md'),
    (Join-Path $PSScriptRoot 'pricing.json')
)
$zipPath = Join-Path $releaseOutput "GlassPulse-$Version-windows.zip"
# An explicit allowlist prevents runtime logs, local settings or credentials entering a release.
Compress-Archive -LiteralPath $packageFiles -DestinationPath $zipPath -Force
$checksumLines = @()
foreach ($releaseFile in @($binaryPath, $zipPath)) {
    $checksumLines += ('{0}  {1}' -f (Get-FileHash -LiteralPath $releaseFile -Algorithm SHA256).Hash.ToLowerInvariant(), (Split-Path -Leaf $releaseFile))
}
$checksumPath = Join-Path $releaseOutput 'SHA256SUMS.txt'
[System.IO.File]::WriteAllLines($checksumPath, $checksumLines, (New-Object System.Text.UTF8Encoding($false)))
Write-Output "Package: $zipPath"
Write-Output "Checksums: $checksumPath"
