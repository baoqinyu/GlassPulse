$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$homePath = [Environment]::GetFolderPath('UserProfile')
$hostName = [Environment]::MachineName
$failures = @()
$extensions = @('.md','.cs','.xaml','.json','.yml','.ps1','.svg','.manifest')
foreach ($name in (& git -C $repoRoot ls-files)) {
    $path = Join-Path $repoRoot $name
    if (-not (Test-Path -LiteralPath $path) -or [System.IO.Path]::GetExtension($name) -notin $extensions) { continue }
    $text = [System.IO.File]::ReadAllText($path)
    if ($homePath.Length -gt 5 -and ($text.IndexOf($homePath,[StringComparison]::OrdinalIgnoreCase) -ge 0 -or $text.IndexOf($homePath.Replace('\','/'),[StringComparison]::OrdinalIgnoreCase) -ge 0)) { $failures += "$name contains a real user-home path" }
    if ($hostName.Length -gt 5 -and $text.IndexOf($hostName,[StringComparison]::OrdinalIgnoreCase) -ge 0) { $failures += "$name contains the build workstation name" }
    if ($text -match '\b[A-Za-z]:[\\/]') { $failures += "$name contains an absolute drive example" }
    if ($text -match '(ghp_|github_pat_|sk-proj-)[A-Za-z0-9_\-]{15,}') { $failures += "$name contains a possible credential" }
}
if ($failures.Count -gt 0) { throw ($failures -join [Environment]::NewLine) }
Write-Output 'Public-content check passed: no workstation paths, host names or credential patterns.'