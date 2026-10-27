param([ValidatePattern('^[A-Za-z0-9_.-]+$')][string]$RepositoryName = 'GlassPulse')
$ErrorActionPreference = 'Stop'
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'GitHub CLI is required: https://cli.github.com/ . Sign in with gh auth login, then rerun this script.' }
Push-Location $PSScriptRoot
try {
    & gh auth status
    if ($LASTEXITCODE -ne 0) { throw 'Sign in with gh auth login before publishing.' }
    $profile = (& gh api user | ConvertFrom-Json)
    if ($LASTEXITCODE -ne 0 -or -not $profile.login) { throw 'Could not resolve the signed-in GitHub account.' }
    if (@(& git status --porcelain).Count -gt 0) { throw 'Commit the reviewed source before publishing.' }
    & (Join-Path $PSScriptRoot 'build.ps1') -Test
    & (Join-Path $PSScriptRoot 'package.ps1') -SkipBuild
    $repository = "$($profile.login)/$RepositoryName"
    # Creation deliberately fails for an existing repository; this script never overwrites another project.
    & gh repo create $repository --public --description 'A lightweight glass Windows desktop meter for Codex and Claude Code weekly tokens, API-equivalent cost, and expandable analytics.' --source . --remote origin
    if ($LASTEXITCODE -ne 0) { throw 'Repository creation failed. Check the name and account permissions.' }
    & git push --set-upstream origin HEAD:main
    if ($LASTEXITCODE -ne 0) { throw 'Source push failed.' }
    & gh repo edit $repository --default-branch main --add-topic windows --add-topic codex --add-topic claude-code --add-topic token-usage --add-topic wpf
    if ($LASTEXITCODE -ne 0) { throw 'Source was pushed, but repository metadata needs attention.' }
    $head = (& git rev-parse HEAD).Trim()
    $runId = $null
    for ($attempt = 0; $attempt -lt 12; $attempt++) {
        $runs = @(& gh run list --repo $repository --workflow windows.yml --branch main --limit 5 --json databaseId,headSha | ConvertFrom-Json)
        $matching = $runs | Where-Object { $_.headSha -eq $head } | Select-Object -First 1
        if ($matching) { $runId = $matching.databaseId; break }
        Start-Sleep -Seconds 5
    }
    if (-not $runId) { throw "Source published at https://github.com/$repository . Wait for Windows CI before tagging v1.2.0." }
    & gh run watch $runId --repo $repository --exit-status
    if ($LASTEXITCODE -ne 0) { throw 'Source published; CI failed. Fix the failure before creating a release.' }
    & git tag v1.2.0
    if ($LASTEXITCODE -ne 0) { throw 'Could not create v1.2.0 tag.' }
    & git push origin v1.2.0
    if ($LASTEXITCODE -ne 0) { throw 'Could not push the release tag.' }
    Write-Output "Published source: https://github.com/$repository"
    Write-Output "The tagged-release workflow will publish the executable, ZIP and checksums."
} finally { Pop-Location }
