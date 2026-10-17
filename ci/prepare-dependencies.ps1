$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
foreach ($name in @('RUNNER_TEMP', 'NPM_CONFIG_CACHE', 'MOE4ALL_PNPM_STORE', 'MOE4ALL_NODE_ARCHIVE_ROOT')) {
    $value = [Environment]::GetEnvironmentVariable($name)
    if (-not $value -or -not [IO.Path]::IsPathRooted($value)) { throw "Missing absolute CI dependency path: $name" }
}
$runtime = Get-Content -LiteralPath (Join-Path $root 'harness\distribution\runtime-versions.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$archive = Join-Path $env:MOE4ALL_NODE_ARCHIVE_ROOT "node-v$($runtime.node)-win-x64.zip"
if (-not (Test-Path -LiteralPath $archive -PathType Leaf)) {
    Invoke-WebRequest -UseBasicParsing -Uri "https://nodejs.org/dist/v$($runtime.node)/node-v$($runtime.node)-win-x64.zip" -OutFile $archive
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $runtime.nodeWindowsX64Sha256) { throw 'Cached Node archive does not match the pinned SHA256.' }
$node = (Get-Command node.exe -ErrorAction Stop).Source
$npm = Join-Path (Split-Path -Parent $node) 'node_modules\npm\bin\npm-cli.js'
if ((& $node --version) -ne "v$($runtime.node)") { throw 'Unexpected CI Node version.' }
$cache = Get-Content -LiteralPath (Join-Path $root 'ci\cache.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ((& $node $npm --version) -ne $cache.npmVersion) { throw 'CI npm does not match its cache identity.' }
foreach ($name in @('build-tools', 'update-tools')) {
    $target = Join-Path $env:RUNNER_TEMP "agent-ci-fixtures\$name"
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    foreach ($file in @('package.json', 'package-lock.json')) {
        Copy-Item -LiteralPath (Join-Path $root "harness\distribution\$name\$file") -Destination (Join-Path $target $file) -Force
    }
    & $node $npm ci --prefix $target --ignore-scripts --no-audit --no-fund
    if ($LASTEXITCODE -ne 0) { throw "Frozen tool fixture failed: $name" }
}
$pnpm = Join-Path $env:RUNNER_TEMP 'agent-ci-fixtures\build-tools\node_modules\pnpm\bin\pnpm.cjs'
if ((& $node $pnpm --version) -ne $runtime.pnpm) { throw 'Unexpected frozen pnpm version.' }
& $node $pnpm --dir (Join-Path $root 'harness\dsh') fetch --frozen-lockfile --store-dir $env:MOE4ALL_PNPM_STORE --ignore-scripts
if ($LASTEXITCODE -ne 0) { throw 'Prefetching the frozen DSH dependency store failed.' }
Write-Host 'Frozen npm tools, pnpm registry downloads and verified Node ZIP are ready; no Agent or Engine was started.'
