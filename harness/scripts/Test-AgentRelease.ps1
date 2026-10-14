[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Archive,
    [string]$OutputDirectory,
    [switch]$Desktop
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root 'artifacts\release-smoke' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
$zip = [IO.Path]::GetFullPath($Archive)
$sum = Get-Content -LiteralPath ($zip + '.sha256') -Raw
if ($sum -notmatch ('^([a-f0-9]{64})\s+\*?' + [regex]::Escape([IO.Path]::GetFileName($zip)) + '\s*$')) { throw 'Invalid archive checksum file.' }
if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne $Matches[1]) { throw 'Archive checksum mismatch.' }
if (Get-NetTCPConnection -LocalPort 17373 -State Listen -ErrorAction SilentlyContinue) { throw 'Port 17373 is already in use; no existing service will be stopped.' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$extract = Join-Path $output ('fresh path with spaces ' + [char]0x4e2d + [char]0x6587 + '-' + [guid]::NewGuid().ToString('N'))
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory($zip, $extract)
$install = Join-Path $extract ([IO.Path]::GetFileNameWithoutExtension($zip))
$report = @()
$qa = Join-Path $output 'qa-tools'
foreach ($phase in @('fresh', 'relocated')) {
    if ($phase -eq 'relocated') {
        $target = Join-Path $output ('moved-' + [guid]::NewGuid().ToString('N'))
        $resolved = [IO.Path]::GetFullPath($install)
        if (-not $resolved.StartsWith($output + '\', [StringComparison]::OrdinalIgnoreCase) -or
            -not $target.StartsWith($output + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Relocation escaped test output.' }
        Move-Item -LiteralPath $install -Destination $target
        $install = $target
    }
    $options = @{ FilePath = (Join-Path $install 'MoE4All.exe'); PassThru = $true; WorkingDirectory = $install }
    if (-not $Desktop) { $options.ArgumentList = '--headless'; $options.WindowStyle = 'Hidden' }
    $process = Start-Process @options
    try {
        $deadline = [DateTime]::UtcNow.AddMinutes(8)
        for (;;) {
            $process.Refresh()
            if ($process.HasExited) { throw "Launcher exited: $($process.ExitCode). See $install\data\logs\launcher.log" }
            try { $response = Invoke-WebRequest -UseBasicParsing 'http://127.0.0.1:17373/' -TimeoutSec 5 } catch { $response = $null }
            if ($response -and $response.StatusCode -eq 200) { break }
            if ([DateTime]::UtcNow -gt $deadline) { throw 'Agent startup timed out.' }
            Start-Sleep -Seconds 1
        }
        $node = Join-Path $install 'node.exe'
        if ($phase -eq 'fresh') {
            New-Item -ItemType Directory -Path $qa -Force | Out-Null
            foreach ($file in @('package.json', 'package-lock.json')) {
                Copy-Item -LiteralPath (Join-Path $root "harness\distribution\build-tools\$file") -Destination (Join-Path $qa $file) -Force
            }
            $npm = (Get-ChildItem (Join-Path $install 'harness\runtime') -Filter npm-cli.js -Recurse | Select-Object -First 1).FullName
            if (-not $npm) { throw 'Bundled npm CLI is missing.' }
            & $node $npm ci --prefix $qa --ignore-scripts --no-audit --no-fund
            if ($LASTEXITCODE -ne 0) { throw 'Installing locked QA tools failed.' }
        }
        $oldQa = $env:MOE4ALL_QA_MODULES
        try {
            $env:MOE4ALL_QA_MODULES = $qa
            & $node (Join-Path $PSScriptRoot 'Test-AgentClient.mjs') 'http://127.0.0.1:17373/' (Join-Path $output "$phase.png")
            if ($LASTEXITCODE -ne 0) { throw "Client/plugin loading failed in $phase startup." }
        } finally { $env:MOE4ALL_QA_MODULES = $oldQa }
        $report += [ordered]@{ phase = $phase; install = $install; status = 'passed'; archiveSha256 = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant() }
    } finally {
        if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
        $process.WaitForExit()
        Start-Sleep -Seconds 2
    }
}
[IO.File]::WriteAllText((Join-Path $output 'startup.json'), ($report | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
Write-Host "Cold and relocated ZIP startup passed. Final install: $install"
