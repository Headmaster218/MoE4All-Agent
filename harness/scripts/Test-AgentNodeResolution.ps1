param(
    [Parameter(Mandatory = $true)][string]$Launcher,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a fresh isolated test directory.' }
New-Item -ItemType Directory -Path $output | Out-Null
$assembly = [Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($Launcher))
$method = $assembly.GetType('MoE4All').GetMethod('FindBundledNode', [Reflection.BindingFlags]'Static,NonPublic')
if (-not $method) { throw 'The compiled launcher does not contain the bundled Node resolver.' }
foreach ($relative in @('node.exe', 'harness\bootstrap\node.exe', 'harness\runtime\node-v24.9.0-win-x64\node.exe')) {
    $root = Join-Path $output ([guid]::NewGuid().ToString('N'))
    $file = Join-Path $root $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $file) -Force | Out-Null
    [IO.File]::WriteAllText($file, 'test-only node identity')
    if ($method.Invoke($null, [string[]]@($root)) -ne $file) { throw ('The runtime layout was not resolved: ' + $relative) }
}
$root = Join-Path $output 'precedence'
foreach ($relative in @('node.exe', 'harness\bootstrap\node.exe', 'harness\runtime\node-v24.9.0-win-x64\node.exe')) {
    $file = Join-Path $root $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $file) -Force | Out-Null
    [IO.File]::WriteAllText($file, 'test-only node identity')
}
if ($method.Invoke($null, [string[]]@($root)) -ne (Join-Path $root 'node.exe')) { throw 'The installed root runtime must take precedence.' }
try { $method.Invoke($null, [string[]]@((Join-Path $output 'missing'))) | Out-Null; throw 'A missing runtime was accepted.' }
catch {
    if ($_.Exception.GetBaseException() -isnot [IO.FileNotFoundException]) { throw }
}
Write-Output 'Bundled Node resolution passed: root, bootstrap, development, precedence and missing runtime.'
