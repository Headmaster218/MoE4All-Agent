param(
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [ValidateSet('picker','picker-light','chrome','chrome-live','chrome-regions','startup-flow','desktop-failure')][string]$Area = 'picker',
    [switch]$Dialogs
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$vendor = Join-Path $root 'harness\launcher\vendor\webview2'
$exe = Join-Path $output 'NativeDesktopTests.exe'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$arguments = @('/nologo','/target:exe','/platform:x64','/codepage:65001','/main:NativeDesktopTests',
    '/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:System.Web.Extensions.dll',
    ('/reference:' + (Join-Path $vendor 'Microsoft.Web.WebView2.Core.dll')),
    ('/reference:' + (Join-Path $vendor 'Microsoft.Web.WebView2.WinForms.dll')),
    ('/resource:' + (Join-Path $root 'harness\launcher\Startup.html') + ',MoE4All.Startup.html'),
    ('/resource:' + (Join-Path $root 'harness\launcher\DesktopChrome.js') + ',MoE4All.DesktopChrome.js'),
    ('/win32manifest:' + (Join-Path $root 'harness\launcher\MoE4All.exe.manifest')),
    ('/out:' + $exe))
$arguments += @(Get-ChildItem -LiteralPath (Join-Path $root 'harness\launcher') -Filter '*.cs' -File | ForEach-Object { $_.FullName })
$arguments += Join-Path $root 'harness\tests\NativeDesktopTests.cs'
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Native desktop unit test compilation failed.' }
Copy-Item -LiteralPath (Join-Path $root 'harness\launcher\MoE4All.exe.config') -Destination ($exe + '.config') -Force
foreach ($name in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll')) {
    Copy-Item -LiteralPath (Join-Path $vendor $name) -Destination $output -Force
}
$testArguments = @($Area)
if ($Dialogs) { $testArguments += '--dialogs' }
$stdout = Join-Path $output 'tests.stdout.log'
$stderr = Join-Path $output 'tests.stderr.log'
$info = New-Object System.Diagnostics.ProcessStartInfo
$info.FileName = $exe
$info.Arguments = $testArguments -join ' '
$info.WorkingDirectory = $output
$info.UseShellExecute = $false
$info.CreateNoWindow = $true
$info.RedirectStandardOutput = $true
$info.RedirectStandardError = $true
$process = [Diagnostics.Process]::Start($info)
$readOut = $process.StandardOutput.ReadToEndAsync()
$readError = $process.StandardError.ReadToEndAsync()
$timeout = if ($Area -eq 'startup-flow') { 90000 } else { 30000 }
$timedOut = -not $process.WaitForExit($timeout)
if ($timedOut) { $process.Kill(); $process.WaitForExit() }
[IO.File]::WriteAllText($stdout, $readOut.GetAwaiter().GetResult())
[IO.File]::WriteAllText($stderr, $readError.GetAwaiter().GetResult())
Get-Content -LiteralPath $stdout
Get-Content -LiteralPath $stderr
if ($timedOut) { throw "Native desktop tests exceeded $timeout ms." }
if ($process.ExitCode -ne 0) { throw 'Native desktop unit tests failed.' }
