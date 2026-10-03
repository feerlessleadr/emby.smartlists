# Developing the plugin

Windows with PowerShell is the primary environment; the commands translate directly to Linux/macOS.

## Prerequisites

- .NET SDK 10 (the plugin targets `net8.0`, the tests `net10.0`).
- An **Emby Server 4.10.1.0** install. The plugin and tests compile against its assemblies (`MediaBrowser.Common/Controller/Model.dll`, which are not on NuGet). Note the path of its `system` folder.
- Optional for UI work: a signed-in browser session on the dev server.

Set the path once per shell, or pass `-p:EmbySystemDir=...` each time (the env var `EMBY_SYSTEM_DIR` also works):

```powershell
$env:EMBY_SYSTEM_DIR = 'C:\path\to\embyserver-win-x64-4.10.1.0\system'
```

## Build and test

```powershell
dotnet build Emby.Plugin.SmartLists/Emby.Plugin.SmartLists.csproj
dotnet test  Emby.Plugin.SmartLists.Tests/Emby.Plugin.SmartLists.Tests.csproj
```

Warnings are errors with the full analyzer set, so style and CA findings fail the build.

## Run against a local Emby

`dev/deploy-local.ps1` stops the dev server, builds with a timestamped version (the version is Emby's cache key for plugin files), copies the DLLs into the server's `plugins` folder and starts it again:

```powershell
./dev/deploy-local.ps1 -EmbyRoot 'C:\path\to\embyserver-win-x64-4.10.1.0'
```

It assumes the server's data lives in `<EmbyRoot>\programdata` (the layout of the portable Windows download). Then open `http://localhost:8096`, sign in as admin and click **SmartLists** in the Dashboard menu. Hard-refresh if the page looks stale.

Tips:

- Server log: `<programdata>\logs\embyserver.txt`; plugin lines start with `SmartLists:`.
- Emby refuses a second instance from the same `EmbyServer.exe`. For a clean second profile (fresh-install testing) copy the `system` folder, use a new `-programdata`, and set `HttpServerPortNumber` / `HttpsPortNumber` in `config/system.xml`.
- Each server start leaves an `embytray.exe` running; `Stop-Process -Name embytray` clears them.
- When driving the page from an automated browser the window must be visible, and navigating by clicking the sidebar entry is more reliable than editing the hash; details in `CLAUDE.md` ("UI gotchas").

## Building a release zip

```powershell
./dev/build-release.ps1 -Version 0.1.0
```

Produces `artifacts/Emby.Plugin.SmartLists-0.1.0.zip` containing `Emby.Plugin.SmartLists.dll` and `SixLabors.ImageSharp.dll`. Use a new version for every distributed build.

## CI

There is no CI: the repository has no GitHub workflows. Build and test locally with the commands above (the plugin needs Emby's own assemblies, which cannot be committed, so a hosted runner would need them supplied separately).

## Project layout and architecture

See `CLAUDE.md` for the layout, how the plugin is composed on Emby (host, entry point, API router, page controller), the conventions, and the list of Emby behaviours that have caused bugs. `docs/port/api-notes.md` has the verified Emby API facts and `docs/port/status.md` the history.

## Adding things

- **A rule field**: `FieldRegistry.cs`, `Operand.cs`, `Factory.cs`, and the `FIELD_TYPES` arrays in `config-core.js`.
- **A sort**: a class in `Core/Orders/`, `OrderFactory.cs`, `IsDescendingOrder()` in `SmartList.cs`, `config-sorts.js`.
- **An API endpoint**: add an attributed action to `SmartListController`; `ControllerRouter` picks it up.
- **A page script**: embed it in the csproj, register it in `Plugin.cs` `GetPages()` and in the module list of `config-controller.js`.
