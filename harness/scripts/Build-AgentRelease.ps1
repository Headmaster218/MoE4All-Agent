[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^(?:release-|agent-v)[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version,
    [string]$OutputDirectory,
    [string]$EngineExecutable,
    [string]$PrebuiltHarnessRoot,
    [string]$BaseArchive,
    [string]$RefreshProfileFromHarness
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root 'artifacts\agent-release' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
$engineExe = $null
$engineVersion = $null
$engineTag = $null
if ($RefreshProfileFromHarness -and -not $BaseArchive) { throw 'Profile refresh requires a verified base archive.' }
$assetVersion = $Version -replace '^release-', 'v'
$name = "MoE4All-Agent-Windows-x64-$assetVersion"
$stage = Join-Path $output $name
$archive = Join-Path $output "$name.zip"
$checksum = "$archive.sha256"
# Dependency lifecycle shells still require a short working directory on Windows.
$deploy = Join-Path $output 'dsh-deploy'
if ((Test-Path -LiteralPath $stage) -or (Test-Path -LiteralPath $archive) -or (Test-Path -LiteralPath $deploy)) {
    throw "Release output already exists: $name"
}
if ($EngineExecutable) {
    $engineExe = [IO.Path]::GetFullPath($EngineExecutable)
    if (-not (Test-Path -LiteralPath $engineExe -PathType Leaf)) { throw "Engine executable not found: $engineExe" }
    $engineOutput = ((& $engineExe --version 2>$null) -join ' ').Trim()
    if ($LASTEXITCODE -ne 0 -or $engineOutput -notmatch '^infr\s+([0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?)$') {
        throw "Invalid Engine version: $engineOutput"
    }
    $engineVersion = $Matches[1]
    $engineTag = "v$engineVersion"
}
$buildConfig = Get-Content -LiteralPath (Join-Path $root 'harness\distribution\agent-build.json') -Raw | ConvertFrom-Json
if ($Version -ne "release-$($buildConfig.version)") { throw 'Release tag does not match agent-build.json.' }

function Get-RelativePath([string]$base, [string]$path) {
    $basePath = [IO.Path]::GetFullPath($base).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $pathValue = [IO.Path]::GetFullPath($path)
    $baseUri = New-Object Uri($basePath)
    $pathUri = New-Object Uri($pathValue)
    return [Uri]::UnescapeDataString($baseUri.MakeRelativeUri($pathUri).ToString()).Replace('/', '\')
}

function Copy-ProfileDependencyPins([string]$sourceProfile, [string]$targetProfile) {
    $source = Get-Content (Join-Path $sourceProfile 'package.json') -Raw | ConvertFrom-Json
    $targetPath = Join-Path $targetProfile 'package.json'
    $target = Get-Content $targetPath -Raw | ConvertFrom-Json
    foreach ($dependency in $target.dependencies.PSObject.Properties) {
        $pin = $source.dependencies.PSObject.Properties[$dependency.Name]
        if ($null -eq $pin) { throw "Missing bundled plugin dependency: $($dependency.Name)" }
        $dependency.Value = $pin.Value
    }
    [IO.File]::WriteAllText($targetPath, (($target | ConvertTo-Json -Depth 12) + "`n"), (New-Object Text.UTF8Encoding($false)))
}

$status = & git -C $root status --porcelain --untracked-files=all
if ($LASTEXITCODE -ne 0 -or $status) { throw 'Commit tracked Agent changes before building a release.' }
$rootCommit = (& git -C $root rev-parse HEAD).Trim()
$paths = & git -C $root config --file (Join-Path $root '.gitmodules') --get-regexp '^submodule\..*\.path$'
if ($LASTEXITCODE -ne 0) { throw 'Cannot read Agent submodules.' }
$submodules = @($paths | ForEach-Object { ($_ -split '\s+', 2)[1] })
foreach ($relative in $submodules) {
    $source = Join-Path $root $relative
    $pin = (& git -C $root ls-tree HEAD $relative) -join ''
    if ($LASTEXITCODE -ne 0 -or $pin -notmatch '^160000 commit ([0-9a-f]{40})\s') {
        throw "Missing committed submodule pin: $relative"
    }
    $expected = $Matches[1]
    $actual = (& git -C $source rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $actual -ne $expected) { throw "Submodule does not match its pin: $relative" }
    $dirty = & git -C $source status --porcelain --untracked-files=all
    if ($LASTEXITCODE -ne 0 -or $dirty) { throw "Commit tracked submodule changes: $relative" }
}

# A launcher-only hotfix can reuse a verified integrated runtime when all runtime source pins match.
if ($BaseArchive) {
    if ($PrebuiltHarnessRoot) { throw 'BaseArchive and PrebuiltHarnessRoot cannot be combined.' }
    $baseZip = [IO.Path]::GetFullPath($BaseArchive)
    $baseSum = "$baseZip.sha256"
    if (-not (Test-Path -LiteralPath $baseSum -PathType Leaf)) { throw 'The base archive checksum is missing.' }
    $sumPattern = '(?im)^([a-f0-9]{64})\s+\*?' + [regex]::Escape([IO.Path]::GetFileName($baseZip)) + '\s*$'
    if ((Get-Content -LiteralPath $baseSum -Raw -Encoding UTF8) -notmatch $sumPattern) {
        throw 'The base checksum does not name the expected archive.'
    }
    $expectedHash = $Matches[1]
    if ((Get-FileHash -LiteralPath $baseZip -Algorithm SHA256).Hash -ne $expectedHash) {
        throw 'The base archive SHA256 does not match.'
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $baseReader = [IO.Compression.ZipFile]::OpenRead($baseZip)
    try {
        $baseTop = [IO.Path]::GetFileNameWithoutExtension($baseZip)
        foreach ($entry in $baseReader.Entries) {
            $parts = $entry.FullName -split '[\\/]'
            if ($parts[0] -ne $baseTop -or $parts -contains '..' -or $entry.FullName.Contains(':')) {
                throw "Unexpected base archive path: $($entry.FullName)"
            }
        }
        $baseManifestEntries = @($baseReader.Entries | Where-Object { $_.FullName.Replace('\', '/') -ceq "$baseTop/harness/distribution/agent-release.json" })
        if ($baseManifestEntries.Count -ne 1) { throw 'The base release manifest is missing or duplicated.' }
        $baseManifestEntry = $baseManifestEntries[0]
        $manifestReader = New-Object IO.StreamReader($baseManifestEntry.Open())
        try { $baseManifest = $manifestReader.ReadToEnd() | ConvertFrom-Json } finally { $manifestReader.Dispose() }
        $baseCommit = [string]$baseManifest.sourceCommit
        if ($baseManifest.schema -ne 2 -or $baseManifest.product -ne 'moe4all-agent' -or
            $baseManifest.packageKind -ne 'integrated' -or $baseManifest.engineVersion -ne $engineTag -or
            $baseCommit -notmatch '^[0-9a-f]{40}$' -or $baseTop -ne ("MoE4All-Agent-Windows-x64-" + ($baseManifest.version -replace '^release-', 'v'))) {
            throw 'The base archive is not a matching integrated Agent release.'
        }
    } finally { $baseReader.Dispose() }
    . (Join-Path $PSScriptRoot 'AgentUpdateVersion.ps1')
    if (-not (Test-AgentVersionNewer $Version ([string]$baseManifest.version))) {
        throw 'The hotfix release must be newer than the base release.'
    }
    & git -C $root cat-file -e ($baseCommit + '^{commit}')
    if ($LASTEXITCODE -ne 0) { throw 'The base source commit is not available for verification.' }
    $runtimePaths = if ($RefreshProfileFromHarness) {
        @('harness/dsh', 'harness/distribution/runtime-versions.json', 'harness/scripts/Build-Agent.ps1', 'harness/distribution/update-tools')
    } else { @($submodules) + @('harness/distribution/profile', 'harness/distribution/update-tools') }
    & git -C $root diff --quiet $baseCommit HEAD -- @runtimePaths
    if ($LASTEXITCODE -ne 0) { throw 'Runtime submodules or profile changed; perform a full release build.' }

    $baseExtract = Join-Path $output "$name-base"
    if (Test-Path -LiteralPath $baseExtract) { throw "Base extraction already exists: $baseExtract" }
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    [IO.Compression.ZipFile]::ExtractToDirectory($baseZip, $baseExtract)
    Move-Item -LiteralPath (Join-Path $baseExtract $baseTop) -Destination $stage
    [IO.Directory]::Delete($baseExtract, $false)
    $stageHarness = Join-Path $stage 'harness'
    if (Test-Path -LiteralPath (Join-Path $stageHarness 'agent-state')) {
        throw 'The base archive contains user runtime state.'
    }
    if ($engineExe) {
    $bundledEnginePath = Join-Path $stageHarness "runtime\engine\$engineTag\infr.exe"
    if ((Get-FileHash -LiteralPath $bundledEnginePath -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $engineExe -Algorithm SHA256).Hash) {
        throw 'The reused Engine differs from the selected release executable.'
    }
    }
    foreach ($scriptName in @('AgentUpdateVersion.ps1', 'Check-AgentUpdate.ps1', 'Prepare-BundledRuntime.ps1', 'Run-Agent.ps1')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $scriptName) -Destination (Join-Path $stageHarness "scripts\$scriptName") -Force
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Prepare-BundledRuntime.cjs') -Destination (Join-Path $stageHarness 'scripts\Prepare-BundledRuntime.cjs') -Force
    foreach ($scriptName in @('Restart-Agent.ps1', 'Extract-AgentUpdate.cjs')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $scriptName) -Destination (Join-Path $stageHarness "scripts\$scriptName") -Force
    }
    Copy-Item -LiteralPath (Join-Path $root 'harness\capabilities') -Destination $stageHarness -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'vendor') -Destination (Join-Path $stageHarness 'scripts\vendor') -Recurse -Force
    foreach ($launcherSource in @('MoE4All.cs', 'CapabilityCenter.cs', 'DesktopPicker.cs', 'NativeDialogTheme.cs', 'DesktopChrome.cs', 'DesktopChrome.js', 'DesktopWindow.cs', 'MoE4All.exe.manifest', 'MoE4All.exe.config', 'Startup.html')) {
        Copy-Item -LiteralPath (Join-Path $root "harness\launcher\$launcherSource") -Destination (Join-Path $stageHarness "launcher\$launcherSource") -Force
    }
    $stageLauncherVendor = Join-Path $stageHarness 'launcher\vendor'
    if (Test-Path -LiteralPath $stageLauncherVendor) { Remove-Item -LiteralPath $stageLauncherVendor -Recurse -Force }
    Copy-Item -LiteralPath (Join-Path $root 'harness\launcher\vendor') -Destination $stageLauncherVendor -Recurse -Force
    & (Join-Path $PSScriptRoot 'Build-AgentLauncher.ps1') -OutputPath (Join-Path $stage 'MoE4All.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Building the hotfix launcher failed.' }
    $baseVersion = [string]$baseManifest.version
    $baseManifest.version = $Version
    $baseManifest.sourceCommit = $rootCommit
    $runtimeSourceCommit = $baseCommit
    if ($baseManifest.runtimeSourceCommit) { $runtimeSourceCommit = [string]$baseManifest.runtimeSourceCommit }
    $baseManifest | Add-Member -NotePropertyName runtimeSourceCommit -NotePropertyValue $runtimeSourceCommit -Force
    $baseManifest | Add-Member -NotePropertyName basePackageVersion -NotePropertyValue $baseVersion -Force
    [IO.File]::WriteAllText((Join-Path $stageHarness 'distribution\agent-release.json'),
        (($baseManifest | ConvertTo-Json) + "`n"), (New-Object Text.UTF8Encoding($false)))
    if ($RefreshProfileFromHarness) {
        $profileHarness = [IO.Path]::GetFullPath($RefreshProfileFromHarness)
        $profileSource = Join-Path $profileHarness 'agent-state\home\profiles\web'
        $profileNode = Join-Path $profileHarness "runtime\node-v$($baseManifest.runtimeVersions.node)-win-x64\node.exe"
        $canonicalLock = Join-Path $root 'harness\distribution\profile\pnpm-lock.yaml'
        if ((Get-FileHash (Join-Path $profileHarness 'distribution\profile\pnpm-lock.yaml') -Algorithm SHA256).Hash -ne
            (Get-FileHash $canonicalLock -Algorithm SHA256).Hash) { throw 'The refreshed profile uses a different dependency lock.' }
        & $profileNode (Join-Path $PSScriptRoot 'Verify-AgentProfile.cjs') $profileHarness $profileSource
        if ($LASTEXITCODE -ne 0) { throw 'Refreshed plugins do not match the committed archives.' }
        foreach ($relative in $submodules) {
            if ((& git -C (Join-Path $profileHarness ($relative -replace '^harness/', '')) rev-parse HEAD).Trim() -ne
                ((& git -C (Join-Path $root $relative) rev-parse HEAD).Trim())) { throw "Refreshed source pin differs: $relative" }
        }
        $profileInput = Join-Path $output ($name + '-profile-input')
        $profileArchive = Join-Path $output ($name + '-profile-input.zip')
        & git -c core.autocrlf=false -C $root archive --format=zip "--output=$profileArchive" HEAD harness/distribution/profile harness/distribution/agent-build.json
        if ($LASTEXITCODE -ne 0) { throw 'Archiving the refreshed profile failed.' }
        [IO.Compression.ZipFile]::ExtractToDirectory($profileArchive, $profileInput)
        $oldTemplate = [IO.Path]::GetFullPath((Join-Path $stageHarness 'distribution\profile'))
        if (-not $oldTemplate.StartsWith($stageHarness + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Profile template escapes staging.' }
        Remove-Item -LiteralPath $oldTemplate -Recurse -Force
        Copy-Item -LiteralPath (Join-Path $profileInput 'harness\distribution\profile') -Destination $oldTemplate -Recurse
        Copy-Item -LiteralPath (Join-Path $profileInput 'harness\distribution\agent-build.json') -Destination (Join-Path $stageHarness 'distribution\agent-build.json') -Force
        & (Join-Path $root 'harness\scripts\Initialize-Agent.ps1') -StateRoot (Join-Path $stageHarness 'agent-state')
        $targetProfile = Join-Path $stageHarness 'agent-state\home\profiles\web'
        & robocopy (Join-Path $profileSource 'node_modules') (Join-Path $targetProfile 'node_modules') /E /XJ /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -ge 8) { throw 'Copying refreshed plugin modules failed.' }
        Copy-ProfileDependencyPins $profileSource $targetProfile
        Copy-Item -LiteralPath (Join-Path $profileSource 'pnpm-lock.yaml') -Destination (Join-Path $targetProfile 'pnpm-lock.yaml') -Force
        & $profileNode (Join-Path $PSScriptRoot 'Verify-AgentProfile.cjs') $profileHarness $targetProfile
        if ($LASTEXITCODE -ne 0) { throw 'Copied profile failed plugin runtime verification.' }
        $seed = [IO.Path]::GetFullPath((Join-Path $stageHarness 'distribution\prebuilt-state'))
        if (-not $seed.StartsWith($stageHarness + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Profile seed escapes staging.' }
        if (Test-Path -LiteralPath $seed) { Remove-Item -LiteralPath $seed -Recurse -Force }
        Move-Item -LiteralPath (Join-Path $stageHarness 'agent-state') -Destination $seed
        $baseManifest.PSObject.Properties.Remove('runtimeSourceCommit')
        $baseManifest | Add-Member -NotePropertyName dshRuntimeSourceCommit -NotePropertyValue $runtimeSourceCommit -Force
        $baseManifest | Add-Member -NotePropertyName profileSourceCommit -NotePropertyValue $rootCommit -Force
        $baseManifest.profileLockSha256 = (Get-FileHash (Join-Path $stageHarness 'distribution\profile\pnpm-lock.yaml') -Algorithm SHA256).Hash.ToLowerInvariant()
        $baseManifest.submodules = @($submodules | ForEach-Object { [ordered]@{ path=$_; commit=(& git -C (Join-Path $root $_) rev-parse HEAD).Trim() } })
        [IO.File]::WriteAllText((Join-Path $stageHarness 'distribution\agent-release.json'),
            (($baseManifest | ConvertTo-Json -Depth 8) + "`n"), (New-Object Text.UTF8Encoding($false)))
    }
    & (Join-Path $PSScriptRoot 'Pack-AgentRuntime.ps1') -HarnessRoot $stageHarness -RefreshProfile:([bool]$RefreshProfileFromHarness)
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $archive, [IO.Compression.CompressionLevel]::Optimal, $true)
    $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($checksum, "$hash  $([IO.Path]::GetFileName($archive))`n", (New-Object Text.UTF8Encoding($false)))
    Write-Host "Agent release: $archive"
    Write-Host "SHA256: $hash"
    $global:LASTEXITCODE = 0
    return
}

New-Item -ItemType Directory -Path $output, $stage -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$temp = Join-Path $output "$name-source.zip"
$agentPaths = @(
    'LICENSE',
    'LICENSE-MIT',
    'harness/distribution/profile',
    'harness/distribution/build-tools',
    'harness/distribution/update-tools',
    'harness/capabilities',
    'harness/distribution/runtime-versions.json',
    'harness/distribution/agent-build.json',
    'harness/launcher',
    'harness/scripts/AgentUpdateVersion.ps1',
    'harness/scripts/Build-Agent.ps1',
    'harness/scripts/Build-DevHarness.ps1',
    'harness/scripts/Verify-AgentProfile.cjs',
    'harness/scripts/Check-AgentUpdate.ps1',
    'harness/scripts/Extract-AgentUpdate.cjs',
    'harness/scripts/Initialize-Agent.ps1',
    'harness/scripts/Prepare-BundledRuntime.ps1',
    'harness/scripts/Prepare-BundledRuntime.cjs',
    'harness/scripts/vendor',
    'harness/scripts/Run-Agent.ps1',
    'harness/scripts/Restart-Agent.ps1',
    'harness/scripts/Prepare-CapabilityTools.ps1'
)
& git -c core.autocrlf=false -C $root archive '--format=zip' ("--output=$temp") HEAD @agentPaths
if ($LASTEXITCODE -ne 0) { throw 'Archiving Agent scripts failed.' }
[IO.Compression.ZipFile]::ExtractToDirectory($temp, $stage)

foreach ($relative in $submodules) {
    $source = Join-Path $root $relative
    $subArchive = Join-Path $output ("$name-" + ($relative -replace '[\\/]', '-') + '.zip')
    & git -c core.autocrlf=false -C $source archive '--format=zip' ("--output=$subArchive") HEAD
    if ($LASTEXITCODE -ne 0) { throw "Archiving $relative failed." }
    [IO.Compression.ZipFile]::ExtractToDirectory($subArchive, (Join-Path $stage $relative))
    Remove-Item -LiteralPath $subArchive
}
Remove-Item -LiteralPath $temp

$stageHarness = Join-Path $stage 'harness'
New-Item -ItemType Directory -Path (Join-Path $stage 'licenses') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $stageHarness 'dsh\LICENSE') -Destination (Join-Path $stage 'licenses\DSH-MIT.txt')
$dshCommit = (& git -C (Join-Path $root 'harness\dsh') rev-parse HEAD).Trim()
$manifest = [ordered]@{
    schema = 2
    product = 'moe4all-agent'
    packageKind = 'integrated'
    version = $Version
    sourceCommit = $rootCommit
    dshCommit = $dshCommit
    dshVersion = (Get-Content (Join-Path $root 'harness\dsh\apps\cli\package.json') -Raw | ConvertFrom-Json).version
    engineVersion = $engineTag
    runtimeVersions = (Get-Content -LiteralPath (Join-Path $root 'harness\distribution\runtime-versions.json') -Raw | ConvertFrom-Json)
    profileLockSha256 = (Get-FileHash -LiteralPath (Join-Path $stageHarness 'distribution\profile\pnpm-lock.yaml') -Algorithm SHA256).Hash.ToLowerInvariant()
    submodules = @($submodules | ForEach-Object { [ordered]@{ path = $_; commit = (& git -C (Join-Path $root $_) rev-parse HEAD).Trim() } })
}
$manifestPath = Join-Path $stageHarness 'distribution\agent-release.json'
[IO.File]::WriteAllText($manifestPath, (($manifest | ConvertTo-Json -Depth 8) + "`n"), (New-Object Text.UTF8Encoding($false)))
& (Join-Path $root 'harness\scripts\Build-AgentLauncher.ps1') -OutputPath (Join-Path $stage 'MoE4All.exe')
if ($LASTEXITCODE -ne 0) { throw 'Building the Agent EXE failed.' }

$buildHarness = $stageHarness
if ($PrebuiltHarnessRoot) {
    $buildHarness = [IO.Path]::GetFullPath($PrebuiltHarnessRoot)
    if (-not (Test-Path -LiteralPath (Join-Path $buildHarness 'dsh\apps\cli\lib\bin.js') -PathType Leaf)) {
        throw "Prebuilt DSH runtime is incomplete: $buildHarness"
    }
    $prebuiltManifestPath = Join-Path $buildHarness 'distribution\agent-release.json'
    if (-not (Test-Path -LiteralPath $prebuiltManifestPath -PathType Leaf)) {
        throw "Prebuilt release manifest is missing: $prebuiltManifestPath"
    }
    $prebuiltManifest = Get-Content -LiteralPath $prebuiltManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $builtCommit = [string]$prebuiltManifest.dshCommit
    if ($builtCommit -ne $dshCommit) {
        throw "Prebuilt DSH commit does not match the release pin: $builtCommit"
    }
    if ($prebuiltManifest.profileLockSha256 -ne $manifest.profileLockSha256 -or
        $prebuiltManifest.runtimeVersions.node -ne $manifest.runtimeVersions.node -or
        $prebuiltManifest.runtimeVersions.pnpm -ne $manifest.runtimeVersions.pnpm) {
        throw 'Prebuilt runtime versions or profile dependency lock differ from the release.'
    }
    foreach ($pin in $manifest.submodules) {
        $builtPin = @($prebuiltManifest.submodules | Where-Object { $_.path -eq $pin.path })
        if ($builtPin.Count -ne 1 -or $builtPin[0].commit -ne $pin.commit) { throw "Prebuilt submodule differs from release: $($pin.path)" }
    }
    $verifyNode = Get-ChildItem -LiteralPath (Join-Path $buildHarness 'runtime') -Directory -Filter 'node-v*-win-x64' |
        ForEach-Object { Join-Path $_.FullName 'node.exe' } | Select-Object -First 1
    & $verifyNode (Join-Path $PSScriptRoot 'Verify-AgentProfile.cjs') $buildHarness (Join-Path $buildHarness 'agent-state\home\profiles\web')
    if ($LASTEXITCODE -ne 0) { throw 'Prebuilt plugins do not match the pinned archives; rebuild the profile.' }
    & (Join-Path $stageHarness 'scripts\Initialize-Agent.ps1') -StateRoot (Join-Path $stageHarness 'agent-state')
    $builtProfile = Join-Path $buildHarness 'agent-state\home\profiles\web'
    $stageProfile = Join-Path $stageHarness 'agent-state\home\profiles\web'
    if (-not (Test-Path -LiteralPath (Join-Path $builtProfile 'node_modules') -PathType Container)) {
        throw "Prebuilt Agent profile is incomplete: $builtProfile"
    }
    & robocopy (Join-Path $builtProfile 'node_modules') (Join-Path $stageProfile 'node_modules') /E /XJ /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copying the prebuilt profile failed (robocopy $LASTEXITCODE)." }
    Copy-ProfileDependencyPins $builtProfile $stageProfile
    Copy-Item -LiteralPath (Join-Path $builtProfile 'pnpm-lock.yaml') -Destination (Join-Path $stageProfile 'pnpm-lock.yaml') -Force

    $builtNode = Get-ChildItem -LiteralPath (Join-Path $buildHarness 'runtime') -Filter node.exe -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match 'node-v[^\\]+-win-x64\\node\.exe$' } |
        Select-Object -First 1
    if ($null -eq $builtNode) { throw 'The prebuilt Node runtime is missing.' }
    $stageNodeRoot = Join-Path (Join-Path $stageHarness 'runtime') $builtNode.Directory.Name
    & robocopy $builtNode.Directory.FullName $stageNodeRoot /E /XJ /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copying the prebuilt Node runtime failed (robocopy $LASTEXITCODE)." }
} else {
    & (Join-Path $stageHarness 'scripts\Build-Agent.ps1') -StateRoot (Join-Path $stageHarness 'agent-state') -Release
}

$node = Get-ChildItem -LiteralPath (Join-Path $buildHarness 'runtime') -Filter node.exe -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match 'node-v[^\\]+-win-x64\\node\.exe$' } |
    Select-Object -First 1 -ExpandProperty FullName
$pnpmCli = Join-Path $buildHarness 'runtime\pnpm\node_modules\pnpm\bin\pnpm.cjs'
$pnpmStore = Join-Path $buildHarness 'runtime\pnpm-store'
if ($env:MOE4ALL_PNPM_STORE) {
    if (-not [IO.Path]::IsPathRooted($env:MOE4ALL_PNPM_STORE)) { throw 'MOE4ALL_PNPM_STORE must be absolute.' }
    $pnpmStore = [IO.Path]::GetFullPath($env:MOE4ALL_PNPM_STORE)
}
if (-not $node -or -not (Test-Path -LiteralPath $pnpmCli -PathType Leaf)) { throw 'The release build runtime is incomplete.' }
& (Join-Path $PSScriptRoot 'Prepare-CapabilityTools.ps1') -HarnessRoot $stageHarness -NodeExecutable $node
& $node (Join-Path $PSScriptRoot 'Prepare-AgentDeploy.cjs') (Join-Path $buildHarness 'dsh')
if ($LASTEXITCODE -ne 0) { throw 'Preparing the approved workspace helper build failed.' }
Push-Location (Join-Path $buildHarness 'dsh')
try {
    & $node $pnpmCli ("--config.store-dir=$pnpmStore") --config.inject-workspace-packages=true --config.node-linker=isolated --filter '@deepseek-ai/dsh' deploy --prod $deploy
    if ($LASTEXITCODE -ne 0) { throw 'Creating the production DSH deployment failed.' }
} finally {
    Pop-Location
}

& $node (Join-Path $PSScriptRoot 'Test-AgentDeployLock.cjs') (Join-Path $buildHarness 'dsh') $deploy
if ($LASTEXITCODE -ne 0) { throw 'DSH deployment escaped the fixed dependency lock.' }

$deployRoot = [IO.Path]::GetFullPath($deploy).TrimEnd([IO.Path]::DirectorySeparatorChar)
$buildDshRoot = [IO.Path]::GetFullPath((Join-Path $buildHarness 'dsh')).TrimEnd([IO.Path]::DirectorySeparatorChar)
$deployLinks = @(Get-ChildItem -LiteralPath $deployRoot -Recurse -Force -Attributes ReparsePoint)
$externalTargets = @{}
foreach ($entry in $deployLinks) {
    if (-not $entry.PSIsContainer) { throw "The DSH deployment contains a file symlink: $($entry.FullName)" }
    $targetValue = [string]@($entry.Target)[0]
    $target = [IO.Path]::GetFullPath((Join-Path $entry.Parent.FullName $targetValue))
    if (-not $target.StartsWith($deployRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        $externalTargets[$target] = $true
    }
}
$materializedTargets = @{}
$materializedRoot = Join-Path $deployRoot '.moe4all-workspace'
foreach ($target in @($externalTargets.Keys | Sort-Object)) {
    if (-not $target.StartsWith($buildDshRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "The DSH deployment link escapes the source workspace: $target"
    }
    $sourceRelative = Get-RelativePath $buildDshRoot $target
    $safeName = $sourceRelative -replace '[^0-9A-Za-z._-]', '-'
    $materialized = Join-Path $materializedRoot $safeName
    New-Item -ItemType Directory -Path $materialized -Force | Out-Null
    & robocopy $target $materialized /E /XJ /XD node_modules .git /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Materializing DSH workspace package $sourceRelative failed (robocopy $LASTEXITCODE)." }
    $materializedTargets[$target] = $materialized
}

# pnpm deploy does not materialize workspace packages that are only peer
# dependencies. Build the production dependency closure explicitly so the
# offline runtime never falls back to the source checkout's hoisted modules.
$workspacePackages = @{}
$dshSourceRoot = Join-Path $root 'harness\dsh'
$workspaceManifestPaths = @(& git -C $dshSourceRoot ls-files '--' ':(glob)**/package.json')
if ($LASTEXITCODE -ne 0 -or $workspaceManifestPaths.Count -eq 0) {
    throw 'Cannot enumerate DSH workspace package manifests.'
}
foreach ($relativeManifestPath in $workspaceManifestPaths) {
    $manifestPath = Join-Path $buildDshRoot ($relativeManifestPath.Replace('/', '\'))
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { continue }
    try {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    } catch {
        continue
    }
    $packageName = [string]$manifest.name
    if ($packageName -match '^@deepseek-ai/[0-9A-Za-z._-]+$' -and -not $workspacePackages.ContainsKey($packageName)) {
        $workspacePackages[$packageName] = Split-Path -Parent $manifestPath
    }
}

$runtimePackages = @{}
$runtimeQueue = New-Object 'Collections.Generic.Queue[string]'
function Register-RuntimePackage([string]$packagePath) {
    $manifestPath = Join-Path $packagePath 'package.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { return }
    try {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    } catch {
        return
    }
    $packageName = [string]$manifest.name
    if ($packageName -notmatch '^@deepseek-ai/[0-9A-Za-z._-]+$' -or $runtimePackages.ContainsKey($packageName)) { return }
    $runtimePackages[$packageName] = $packagePath
    foreach ($section in @('dependencies', 'optionalDependencies', 'peerDependencies')) {
        $dependencies = $manifest.$section
        if ($null -eq $dependencies) { continue }
        foreach ($dependency in @($dependencies.PSObject.Properties.Name)) {
            if ($dependency -match '^@deepseek-ai/[0-9A-Za-z._-]+$') { $runtimeQueue.Enqueue($dependency) }
        }
    }
}

$deployPnpm = Join-Path $deployRoot 'node_modules\.pnpm'
foreach ($entry in Get-ChildItem -LiteralPath $deployPnpm -Directory) {
    $scope = Join-Path $entry.FullName 'node_modules\@deepseek-ai'
    if (-not (Test-Path -LiteralPath $scope -PathType Container)) { continue }
    foreach ($package in Get-ChildItem -LiteralPath $scope -Directory -Force) {
        if (($package.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) {
            Register-RuntimePackage $package.FullName
        }
    }
}
foreach ($materialized in @($materializedTargets.Values)) {
    Register-RuntimePackage ([string]$materialized)
}
# Browser platform peers may be statically bundled and absent from pnpm's
# production graph. Retain their real packages for plugin OTA version checks.
$pluginCatalog = Get-Content -LiteralPath (Join-Path $root 'harness\capabilities\catalog.json') -Raw -Encoding UTF8 | ConvertFrom-Json
foreach ($plugin in $pluginCatalog.plugins) {
    $pluginManifest = Get-Content -LiteralPath (Join-Path $buildHarness "agent-state\home\profiles\web\node_modules\$($plugin.id)\package.json") -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($null -eq $pluginManifest.peerDependencies) { continue }
    foreach ($peer in $pluginManifest.peerDependencies.PSObject.Properties.Name) {
        if ($peer -match '^@deepseek-ai/[0-9A-Za-z._-]+$' -and $pluginManifest.peerDependenciesMeta.$peer.optional -ne $true) {
            $runtimeQueue.Enqueue($peer)
        }
    }
}
while ($runtimeQueue.Count -gt 0) {
    $packageName = $runtimeQueue.Dequeue()
    if ($runtimePackages.ContainsKey($packageName)) { continue }
    if (-not $workspacePackages.ContainsKey($packageName)) {
        throw "The production DSH dependency closure is missing workspace package $packageName."
    }
    $source = [string]$workspacePackages[$packageName]
    $sourceRelative = Get-RelativePath $buildDshRoot $source
    $safeName = $sourceRelative -replace '[^0-9A-Za-z._-]', '-'
    $materialized = Join-Path $materializedRoot $safeName
    New-Item -ItemType Directory -Path $materialized -Force | Out-Null
    & robocopy $source $materialized /E /XJ /XD node_modules .git /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Materializing DSH workspace dependency $sourceRelative failed (robocopy $LASTEXITCODE)." }
    Register-RuntimePackage $materialized
}

$links = @()
foreach ($entry in $deployLinks) {
    $targetValue = [string]@($entry.Target)[0]
    $target = [IO.Path]::GetFullPath((Join-Path $entry.Parent.FullName $targetValue))
    if (-not $target.StartsWith($deployRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        $target = [string]$materializedTargets[$target]
        if (-not $target) { throw "The DSH deployment link was not materialized: $($entry.FullName)" }
    }
    $links += [ordered]@{
        path = Get-RelativePath $deployRoot $entry.FullName
        target = Get-RelativePath $deployRoot $target
    }
}
$links = @($links | Sort-Object { $_.path.Length }, path)
$stageRuntime = Join-Path $stageHarness 'runtime'
$stageDsh = Join-Path $stageRuntime 'dsh'
New-Item -ItemType Directory -Path $stageDsh -Force | Out-Null
& robocopy $deployRoot $stageDsh /E /XJ /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Copying the production DSH deployment failed (robocopy $LASTEXITCODE)." }
$linksManifest = [ordered]@{ schema = 1; links = $links }
[IO.File]::WriteAllText((Join-Path $stageRuntime 'dsh-links.json'), (($linksManifest | ConvertTo-Json -Depth 6) + "`n"), (New-Object Text.UTF8Encoding($false)))

if ($engineExe) {
$engineRoot = Join-Path $stageRuntime "engine\$engineTag"
New-Item -ItemType Directory -Path $engineRoot -Force | Out-Null
$bundledEngine = Join-Path $engineRoot 'infr.exe'
Copy-Item -LiteralPath $engineExe -Destination $bundledEngine
$engineHash = (Get-FileHash -LiteralPath $bundledEngine -Algorithm SHA256).Hash.ToLowerInvariant()
$engineManifest = [ordered]@{
    schema = 1
    tag = $engineTag
    name = "MoE4All Engine $engineTag"
    executable = "$engineTag\infr.exe"
    sha256 = $engineHash
}
[IO.File]::WriteAllText((Join-Path $stageRuntime 'engine\bundled-engine.json'), (($engineManifest | ConvertTo-Json) + "`n"), (New-Object Text.UTF8Encoding($false)))

}

$seed = Join-Path $stageHarness 'distribution\prebuilt-state'
Move-Item -LiteralPath (Join-Path $stageHarness 'agent-state') -Destination $seed

$removePaths = @(
    (Join-Path $stageHarness 'dsh'),
    (Join-Path $stageHarness 'plugins'),
    (Join-Path $stageHarness ([IO.Path]::GetFileName($deploy))),
    (Join-Path $stageRuntime 'pnpm'),
    (Join-Path $stageRuntime 'pnpm-store'),
    (Join-Path $stageRuntime 'plugin-packs')
)
foreach ($path in $removePaths) {
    $resolved = [IO.Path]::GetFullPath($path)
    if (-not $resolved.StartsWith($stageHarness + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Cleanup target escapes the release: $resolved" }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
Get-ChildItem -LiteralPath $stageRuntime -File -Filter 'node-v*-win-x64.zip' -ErrorAction SilentlyContinue |
    Remove-Item -Force
foreach ($scriptName in @('Build-Agent.ps1', 'Build-DevHarness.ps1', 'Initialize-Agent.ps1', 'Verify-AgentProfile.cjs')) {
    $path = Join-Path $stageHarness "scripts\$scriptName"
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
}
if (-not ([IO.Path]::GetFullPath($deploy)).StartsWith($output + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Deployment cleanup escapes output.' }
Remove-Item -LiteralPath $deploy -Recurse -Force

& (Join-Path $PSScriptRoot 'Pack-AgentRuntime.ps1') -HarnessRoot $stageHarness
Compress-Archive -LiteralPath $stage -DestinationPath $archive -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($checksum, "$hash  $([IO.Path]::GetFileName($archive))`n", (New-Object Text.UTF8Encoding($false)))
Write-Host "Agent release: $archive"
Write-Host "SHA256: $hash"
$global:LASTEXITCODE = 0
