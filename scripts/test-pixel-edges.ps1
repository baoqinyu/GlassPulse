param([string]$Executable=(Join-Path (Split-Path -Parent $PSScriptRoot) 'dist\GlassPulse.exe'),[string]$OutputDirectory=(Join-Path (Split-Path -Parent $PSScriptRoot) 'build\pixel-results'),[switch]$AllowUnavailable)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if(-not (Test-Path -LiteralPath (Join-Path $framework 'csc.exe'))){$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'}
$build=Join-Path $repoRoot 'build'
New-Item -ItemType Directory -Force -Path $build | Out-Null
$probe=Join-Path $build 'GlassPulse.PixelProbe.exe'
$refs=@('PresentationCore.dll','PresentationFramework.dll','WindowsBase.dll')|ForEach-Object{'/r:'+(Join-Path $framework "WPF\$_")}
$refs+=@('/r:System.Xaml.dll','/r:System.Web.Extensions.dll','/r:System.Drawing.dll')
& (Join-Path $framework 'csc.exe') /nologo /utf8output /warnaserror+ /target:exe "/out:$probe" "/win32manifest:$repoRoot\src\app.manifest" @refs (Join-Path $repoRoot 'tests\PixelProbe.cs')
if($LASTEXITCODE -ne 0){throw 'Screen-pixel probe build failed'}
& $probe ([IO.Path]::GetFullPath($Executable)) ([IO.Path]::GetFullPath($OutputDirectory))
$probeExit=$LASTEXITCODE
if($probeExit -eq 77 -and $AllowUnavailable){Write-Output 'Screen-pixel check skipped: no synthetic interactive desktop was available.';exit 0}
if($probeExit -ne 0){throw 'Actual screen-pixel check failed; inspect the local synthetic results.'}