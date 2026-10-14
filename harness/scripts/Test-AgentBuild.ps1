$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$fixture = Join-Path $root ('artifacts\build-switch-test-' + [guid]::NewGuid().ToString('N') + '\harness')
$scripts = Join-Path $fixture 'scripts'
New-Item -ItemType Directory -Path $scripts, (Join-Path $fixture 'distribution') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Build-Agent.ps1') -Destination (Join-Path $scripts 'Build-Agent.ps1')
$utf8 = New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllText((Join-Path $fixture 'distribution\agent-release.json'), ('{"dshCommit":"' + ('a' * 40) + '"}'), $utf8)
[IO.File]::WriteAllText((Join-Path $scripts 'Initialize-Agent.ps1'), 'param($StateRoot, $EngineEndpoint)', $utf8)
[IO.File]::WriteAllText((Join-Path $scripts 'Build-DevHarness.ps1'), @'
param([switch]$SkipInitialize, $ProfileDir, [switch]$Release, [switch]$UpdateProfileLock)
[pscustomobject]@{ release = $Release.IsPresent; update = $UpdateProfileLock.IsPresent; commit = $env:DSH_CLIENT_COMMIT_HASH }
'@, $utf8)
$releaseResult = & (Join-Path $scripts 'Build-Agent.ps1') -Release
if (-not $releaseResult.release -or $releaseResult.update -or $releaseResult.commit -ne ('a' * 40)) { throw 'Release build flags or source provenance were lost.' }
$updateResult = & (Join-Path $scripts 'Build-Agent.ps1') -UpdateProfileLock
if ($updateResult.release -or -not $updateResult.update) { throw 'Lock maintenance flags were lost.' }
Write-Host 'Release switches and manifest provenance passed.'
