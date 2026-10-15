[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$InstallRoot,
    [Parameter(Mandatory = $true)][int]$ParentPid,
    [Parameter(Mandatory = $true)][string]$ReadyFile
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($InstallRoot)
$executable = Join-Path $root 'MoE4All.exe'
$parent = Get-Process -Id $ParentPid -ErrorAction Stop
if (-not $parent.Path.Equals($executable, [StringComparison]::OrdinalIgnoreCase)) { throw 'The launcher identity changed; restart cancelled.' }
$ack = [IO.Path]::GetFullPath($ReadyFile)
$cache = [IO.Path]::GetFullPath((Join-Path $root 'cache')).TrimEnd('\') + '\'
if (-not $ack.StartsWith($cache, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($ack) -notmatch '^restart-[0-9a-f]{32}\.ready$') {
    throw 'The restart acknowledgement must stay inside the Agent cache.'
}
[IO.File]::WriteAllText($ack, [string]$ParentPid, (New-Object Text.UTF8Encoding($false)))
if (-not $parent.WaitForExit(60000)) { throw 'The Agent did not close; no processes were terminated.' }
Start-Process -FilePath $executable -WorkingDirectory $root -WindowStyle Normal | Out-Null
