#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Builds a Release plugin (ImageSharp is embedded in the single DLL) and writes artifacts/Emby.Plugin.SmartLists-<Version>.zip plus the bare DLL.
.PARAMETER Version
  Plugin version, three or four numeric parts (e.g. 0.1.0). Emby uses it as the resource cache key: change it for every distributed build.
.PARAMETER EmbySystemDir
  Path to the "system" folder of an Emby Server 4.10.1.0 install (defaults to $env:EMBY_SYSTEM_DIR).
#>
param(
    [Parameter(Mandatory)] [string] $Version,
    [string] $EmbySystemDir = $env:EMBY_SYSTEM_DIR
)

$ErrorActionPreference = 'Stop'
if (-not $EmbySystemDir) { throw 'Set -EmbySystemDir or $env:EMBY_SYSTEM_DIR' }
$repo = Split-Path -Parent $PSScriptRoot

dotnet build (Join-Path $repo 'Emby.Plugin.SmartLists/Emby.Plugin.SmartLists.csproj') -c Release "-p:EmbySystemDir=$EmbySystemDir" "-p:Version=$Version" --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

$out = Join-Path $repo 'Emby.Plugin.SmartLists/bin/Release/net8.0'
$dest = Join-Path $repo 'artifacts'
New-Item -ItemType Directory -Force $dest | Out-Null
$zip = Join-Path $dest "Emby.Plugin.SmartLists-$Version.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path (Join-Path $out 'Emby.Plugin.SmartLists.dll') -DestinationPath $zip
Copy-Item (Join-Path $out 'Emby.Plugin.SmartLists.dll') (Join-Path $dest 'Emby.Plugin.SmartLists.dll') -Force
Write-Host "Wrote $zip (and a bare Emby.Plugin.SmartLists.dll; upload only ONE of the two to a release)"
