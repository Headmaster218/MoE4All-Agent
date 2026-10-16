[CmdletBinding()]
param(
    [string]$OutputPath,
    [switch]$Console
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (-not $OutputPath) { $OutputPath = Join-Path $root 'MoE4All.exe' }
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Path (Split-Path -Parent $OutputPath) -Force | Out-Null
$source = Join-Path $root 'harness\launcher\MoE4All.cs'
$capabilitiesSource = Join-Path $root 'harness\launcher\CapabilityCenter.cs'
$pickerSource = Join-Path $root 'harness\launcher\DesktopPicker.cs'
$dialogThemeSource = Join-Path $root 'harness\launcher\NativeDialogTheme.cs'
$windowSource = Join-Path $root 'harness\launcher\DesktopWindow.cs'
$chromeSource = Join-Path $root 'harness\launcher\DesktopChrome.cs'
$startupPage = Join-Path $root 'harness\launcher\Startup.html'
$manifest = Join-Path $root 'harness\launcher\MoE4All.exe.manifest'
$config = Join-Path $root 'harness\launcher\MoE4All.exe.config'
$vendor = Join-Path $root 'harness\launcher\vendor\webview2'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) { throw 'The .NET Framework C# compiler is unavailable.' }
if (-not (Test-Path -LiteralPath $manifest -PathType Leaf)) { throw "The launcher manifest is missing: $manifest" }
if (-not (Test-Path -LiteralPath $config -PathType Leaf)) { throw "The launcher configuration is missing: $config" }
$core = Join-Path $vendor 'Microsoft.Web.WebView2.Core.dll'
$forms = Join-Path $vendor 'Microsoft.Web.WebView2.WinForms.dll'
foreach ($dependency in @($core, $forms, (Join-Path $vendor 'WebView2Loader.dll'))) {
    if (-not (Test-Path -LiteralPath $dependency -PathType Leaf)) { throw "WebView2 launcher dependency is missing: $dependency" }
}
$target = if ($Console) { 'exe' } else { 'winexe' }
$nativeArgs = @(
    '/nologo', ("/target:$target"), '/platform:x64', '/optimize+', '/codepage:65001',
    '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll', '/reference:System.Web.Extensions.dll',
    ("/reference:$core"), ("/reference:$forms"),
    ('/resource:' + $startupPage + ',MoE4All.Startup.html'),
    ('/resource:' + (Join-Path $root 'harness\launcher\DesktopChrome.js') + ',MoE4All.DesktopChrome.js'),
    ('/win32manifest:' + $manifest), ('/out:' + $OutputPath), $source, $capabilitiesSource, $pickerSource, $windowSource, $dialogThemeSource, $chromeSource
)
& $compiler @nativeArgs
if ($LASTEXITCODE -ne 0) { throw "Launcher compilation failed with exit code $LASTEXITCODE" }
Copy-Item -LiteralPath $config -Destination ($OutputPath + '.config') -Force
$destination = Split-Path -Parent ([IO.Path]::GetFullPath($OutputPath))
foreach ($name in @(
    'Microsoft.Web.WebView2.Core.dll',
    'Microsoft.Web.WebView2.WinForms.dll',
    'WebView2Loader.dll',
    'Microsoft.Web.WebView2.LICENSE.txt'
)) {
    Copy-Item -LiteralPath (Join-Path $vendor $name) -Destination (Join-Path $destination $name) -Force
}
Write-Host "MoE4All launcher: $OutputPath"
