$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$fixture = Join-Path $root ('artifacts\profile-payload-tests\' + [guid]::NewGuid().ToString('N'))
$harness = Join-Path $fixture 'harness'
New-Item -ItemType Directory -Path (Join-Path $harness 'runtime'), (Join-Path $harness 'distribution'), (Join-Path $harness 'bootstrap') -Force | Out-Null
$utf8 = New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllText((Join-Path $harness 'distribution\agent-release.json'), '{"product":"moe4all-agent"}', $utf8)
[IO.File]::WriteAllBytes((Join-Path $harness 'bootstrap\node.exe'), [byte[]]@(0))
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archives = @()
foreach ($group in @(
    @{root='runtime\dsh';file='runtime\dsh-data.zip'},
    @{root='runtime\node-v24.9.0-win-x64';file='runtime\node-data.zip'},
    @{root='distribution\prebuilt-state';file='distribution\profile-data.zip'}
)) {
    $source = Join-Path $fixture ($group.root -replace '[\\/]', '-')
    New-Item -ItemType Directory -Path $source -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $source 'value.txt'), 'old runtime', $utf8)
    $archive = Join-Path $harness $group.file
    [IO.Compression.ZipFile]::CreateFromDirectory($source, $archive)
    $archives += [ordered]@{root=$group.root;file=$group.file;files=1;sha256=(Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()}
}
[IO.File]::WriteAllText((Join-Path $harness 'runtime\runtime-payload.json'), (([ordered]@{schema=1;archives=$archives} | ConvertTo-Json -Depth 5)), $utf8)
$seed = Join-Path $harness 'distribution\prebuilt-state'
New-Item -ItemType Directory -Path $seed -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $seed 'value.txt'), 'new plugin runtime', $utf8)
& (Join-Path $PSScriptRoot 'Pack-AgentRuntime.ps1') -HarnessRoot $harness -RefreshProfile
$actual = Get-Content (Join-Path $harness 'runtime\runtime-payload.json') -Raw | ConvertFrom-Json
foreach ($entry in $archives | Where-Object {$_.root -ne 'distribution\prebuilt-state'}) {
    if ((Get-FileHash (Join-Path $harness $entry.file) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256) { throw 'An unchanged DSH/Node payload was rewritten.' }
}
$profile = @($actual.archives | Where-Object {$_.root -eq 'distribution\prebuilt-state'})
if ($profile.Count -ne 1 -or $profile[0].sha256 -eq $archives[2].sha256) { throw 'Profile payload did not refresh.' }
$extract = Join-Path $fixture 'verified'
[IO.Compression.ZipFile]::ExtractToDirectory((Join-Path $harness $profile[0].file), $extract)
if ([IO.File]::ReadAllText((Join-Path $extract 'value.txt')) -ne 'new plugin runtime') { throw 'Old plugin runtime survived the refresh.' }
Write-Host 'Profile payload refresh passed: new plugins, unchanged verified DSH/Node payloads.'
