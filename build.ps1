param([switch]$Test)
$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $frameworkRoot 'csc.exe'))) {
    $frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$compiler = Join-Path $frameworkRoot 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Windows with .NET Framework 4.8 is required.' }
$distDirectory = Join-Path $repoRoot 'dist'
$buildDirectory = Join-Path $repoRoot 'build'
New-Item -ItemType Directory -Force -Path $distDirectory, $buildDirectory | Out-Null
$coreFiles = @('Json.cs','Usage.cs','Pricing.cs','LogReader.cs','Settings.cs') | ForEach-Object { Join-Path $repoRoot "src\$_" }
$references = @('PresentationCore.dll','PresentationFramework.dll','WindowsBase.dll') | ForEach-Object { '/r:' + (Join-Path $frameworkRoot "WPF\$_") }
$references += @('/r:System.Xaml.dll','/r:System.Web.Extensions.dll','/r:System.Windows.Forms.dll','/r:System.Drawing.dll')
$metadataRoot = Join-Path $env:WINDIR 'System32\WinMetadata'
$runtimeFacadeRoot = Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL\System.Runtime'
$runtimeFacade = Get-ChildItem -LiteralPath $runtimeFacadeRoot -Filter 'System.Runtime.dll' -Recurse | Select-Object -First 1 -ExpandProperty FullName
$references += @('/r:System.Numerics.dll',
    ('/r:' + (Join-Path $frameworkRoot 'System.Numerics.Vectors.dll')),
    ('/r:' + (Join-Path $frameworkRoot 'System.Runtime.WindowsRuntime.dll')),
    ('/r:' + (Join-Path $metadataRoot 'Windows.Foundation.winmd')),
    ('/r:' + (Join-Path $metadataRoot 'Windows.UI.winmd')),
    ('/r:' + $runtimeFacade))
$sourceFiles = Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName
$exePath = Join-Path $distDirectory 'GlassPulse.exe'
$arguments = @('/nologo','/utf8output','/warnaserror+','/optimize+','/target:winexe','/platform:anycpu',"/out:$exePath",
    "/win32manifest:$repoRoot\src\app.manifest", "/win32icon:$repoRoot\assets\GlassPulse.ico",
    "/resource:$repoRoot\src\Widget.xaml,GlassPulse.Widget.xaml",
    "/resource:$repoRoot\src\Settings.xaml,GlassPulse.Settings.xaml",
    "/resource:$repoRoot\pricing.json,GlassPulse.pricing.json") + $references + $sourceFiles
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination $distDirectory
Copy-Item -LiteralPath (Join-Path $repoRoot 'pricing.json') -Destination (Join-Path $distDirectory 'pricing.reference.json')
if (Test-Path -LiteralPath (Join-Path $repoRoot 'QUICKSTART.zh-CN.md')) {
    Copy-Item -LiteralPath (Join-Path $repoRoot 'QUICKSTART.zh-CN.md') -Destination $distDirectory
}
if ($Test) {
    $testExe = Join-Path $buildDirectory 'GlassPulse.Tests.exe'
    & $compiler /nologo /utf8output /warnaserror+ /target:exe "/out:$testExe" /r:System.Web.Extensions.dll @coreFiles (Join-Path $repoRoot 'tests\CoreTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
    & $testExe (Join-Path $repoRoot 'pricing.json')
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    $windowTest = Join-Path $buildDirectory 'GlassPulse.WindowTests.exe'
    $windowArgs = @('/nologo','/utf8output','/warnaserror+','/target:exe','/main:GlassPulse.Tests.WindowTests',"/out:$windowTest",
        "/win32manifest:$repoRoot\src\app.manifest",
        "/resource:$repoRoot\src\Widget.xaml,GlassPulse.Widget.xaml",
        "/resource:$repoRoot\src\Settings.xaml,GlassPulse.Settings.xaml") + $references + $sourceFiles + (Join-Path $repoRoot 'tests\WindowTests.cs')
    & $compiler @windowArgs
    if ($LASTEXITCODE -ne 0) { throw 'Native window test build failed.' }
    & $windowTest (Join-Path $repoRoot 'pricing.json') (Join-Path $buildDirectory 'window-test-data')
    if ($LASTEXITCODE -ne 0) { throw 'Native window region tests failed.' }

}
$built = Get-Item -LiteralPath $exePath
Write-Output ("Built {0} ({1:N0} KB)" -f $built.FullName, ($built.Length / 1KB))
