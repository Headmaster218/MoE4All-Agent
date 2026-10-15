[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$HarnessRoot,
    [Parameter(Mandatory = $true)][string]$NodeExecutable
)

$ErrorActionPreference = 'Stop'
$source = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\distribution\update-tools'))
$target = Join-Path ([IO.Path]::GetFullPath($HarnessRoot)) 'runtime\update-tools'
$npm = Join-Path (Split-Path -Parent $NodeExecutable) 'node_modules\npm\bin\npm-cli.js'
if (-not (Test-Path -LiteralPath $npm -PathType Leaf)) { throw 'The bundled npm runtime is required to prepare update tools.' }
New-Item -ItemType Directory -Path $target -Force | Out-Null
foreach ($name in @('package.json', 'package-lock.json')) {
    Copy-Item -LiteralPath (Join-Path $source $name) -Destination (Join-Path $target $name) -Force
}
& $NodeExecutable $npm ci --prefix $target --ignore-scripts --no-audit --no-fund
if ($LASTEXITCODE -ne 0) { throw 'Installing the frozen capability update tools failed.' }
Write-Host "Capability update tools: $target"
