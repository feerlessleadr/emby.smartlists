#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Builds the plugin and deploys it to a local portable Emby Server (stop, copy, start).
.PARAMETER EmbyRoot
  Folder that contains Emby's "system" and "programdata" folders (the portable Windows download).
.PARAMETER Configuration
  Build configuration (default Debug).
.PARAMETER NoStart
  Deploy but do not start the server again.
#>
param(
    [Parameter(Mandatory)] [string] $EmbyRoot,
    [string] $Configuration = 'Debug',
    [switch] $NoStart
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$system = Join-Path $EmbyRoot 'system'
$data = Join-Path $EmbyRoot 'programdata'
$plugins = Join-Path $data 'plugins'
if (-not (Test-Path (Join-Path $system 'EmbyServer.exe'))) { throw "No EmbyServer.exe under $system" }

# Emby caches plugin pages and scripts by plugin *version*; a changing version makes browsers fetch the new files.
$now = Get-Date
$version = "0.$($now.ToString('MMdd')).$($now.ToString('HHmm'))"

Write-Host "Building $Configuration ($version)..."
dotnet build (Join-Path $repo 'Emby.Plugin.SmartLists/Emby.Plugin.SmartLists.csproj') -c $Configuration "-p:EmbySystemDir=$system" "-p:Version=$version" --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
$out = Join-Path $repo "Emby.Plugin.SmartLists/bin/$Configuration/net8.0"

Write-Host 'Stopping Emby...'
Get-CimInstance Win32_Process -Filter "Name='EmbyServer.exe'" |
    Where-Object { $_.ExecutablePath -and ($_.ExecutablePath -like "$system*") } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
Start-Sleep -Seconds 3

New-Item -ItemType Directory -Force $plugins | Out-Null
Copy-Item (Join-Path $out 'Emby.Plugin.SmartLists.dll'), (Join-Path $out 'SixLabors.ImageSharp.dll') $plugins -Force
Write-Host "Copied plugin to $plugins"

if (-not $NoStart) {
    Start-Process (Join-Path $system 'EmbyServer.exe') -WorkingDirectory $system -ArgumentList '-programdata', "`"$data`"" -WindowStyle Minimized
    Write-Host 'Emby started. Log: ' (Join-Path $data 'logs/embyserver.txt')
}
