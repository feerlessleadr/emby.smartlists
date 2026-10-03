# SmartLists for Emby

An Emby Server plugin that creates dynamic playlists and collections from user-defined rules (genres, ratings, years, play state, and so on) and keeps them up to date automatically.

This is a straight port of the Jellyfin SmartLists plugin to **Emby Server 4.10.1.0 (net8.0)**. Jellyfin support was dropped on purpose and upstream is not tracked. Read `docs/port/status.md` for the decision log, progress history and open items, and `docs/port/api-notes.md` for verified Emby API behaviour before touching anything that calls Emby.

## Scope

In: rules, smart playlists and collections, manual / scheduled / automatic refresh, the admin page, images, backups, templates.

Out (hidden in the UI, not built): external lists (MDBList, Trakt, ...), the end-user page and its endpoints (`UserPagesController`, `UserSmartListController` are excluded from the build), people roles Emby cannot represent, `VideoRangeType`.

## Development commands (Windows, PowerShell)

```powershell
# The plugin and tests compile against the Emby server assemblies, which are not on NuGet.
# Point EmbySystemDir at the "system" folder of an Emby Server 4.10.1.0 install.
$sys = 'C:\path\to\embyserver-win-x64-4.10.1.0\system'

dotnet build Emby.Plugin.SmartLists/Emby.Plugin.SmartLists.csproj "-p:EmbySystemDir=$sys"
dotnet test  Emby.Plugin.SmartLists.Tests/Emby.Plugin.SmartLists.Tests.csproj "-p:EmbySystemDir=$sys"
```

The build treats all warnings as errors with `AnalysisMode=Recommended`: CA/SA analyzer findings (CA1822 make-static, CA1305 locale, SA1300 naming, ...) fail it.

### Deploying to a local Emby

1. Stop the server (`Stop-Process -Name EmbyServer`).
2. Copy `Emby.Plugin.SmartLists.dll` and `SixLabors.ImageSharp.dll` from `bin\<config>\net8.0` into `<programdata>\plugins`.
3. Start `system\EmbyServer.exe -programdata <programdata>`. Logs: `<programdata>\logs\embyserver.txt` (plugin lines are `SmartLists: <Class>: ...`).

Things that will bite you:

- **Plugin resources are cached by version.** Emby serves the page, controller and JS with an ETag derived from the plugin *version* only, so a rebuild at the same version leaves the browser on stale code. For dev builds pass a changing version, e.g. `-p:Version=0.$(Get-Date -Format MMdd).$(Get-Date -Format HHmm)` (three parts; .NET appends the fourth). Releases change version anyway.
- **One server per exe path.** Emby refuses to start a second instance from the same `EmbyServer.exe`. For a second (clean) profile copy the `system` folder, give it its own `-programdata`, and set `HttpServerPortNumber`/`HttpsPortNumber` in `config/system.xml`.
- Each server start spawns an `embytray.exe`; they accumulate. Kill them with `Stop-Process -Name embytray`.

### Testing

`Emby.Plugin.SmartLists.Tests` (xunit, ~1,770 tests, net10.0). It covers the query engine (operators, prefilter resolvers, `FieldRegistry` invariants), `Core/Orders`, `Utilities`, the `ControllerRouter`, and the error-filter. Fixtures build real Emby `BaseItem`s in `Support/TestItems.cs`.

- The test project targets net10.0 but the ASP.NET 8 runtime is not required: three `MediaBrowser.*` DLLs are copied next to the tests and `Support/EmbyAssemblyResolver.cs` resolves the rest from `EmbySystemDir`. If you see Jellyfin or wrong-version assemblies, delete the test `bin`/`obj` folders.
- Tests do **not** cover anything that talks to a running Emby (playlist/collection creation, refresh queue, auto-refresh events, the page). Verify those on a live server; `docs/port/api-notes.md` records what has been verified and how.

## Project structure

```text
Emby.Plugin.SmartLists/
├── Plugin.cs                 BasePlugin<PluginConfiguration> + IHasWebPages; plugin Id = assembly [Guid]
├── Host/                     Composition root: SmartListsHost (hand-built service graph), StartupEntryPoint
│                             (IServerEntryPoint), EmbyLoggerProvider (MEL -> Emby ILogger)
├── Api/                      EmbyApiService (one wildcard /Plugins/SmartLists/{Path*} endpoint),
│                             ControllerRouter (maps [Http*] attributes of SmartListController), Controllers/
├── Core/                     Business logic: Constants, Enums, Models, Orders (sorts), QueryEngine, SmartList.cs
├── Services/                 Playlists, Collections, Shared (AutoRefresh, RefreshQueue, Backup/Cleanup tasks,
│                             images, status), ExternalList (compiled, not wired), Abstractions
├── Utilities/                DtoMapper, InputValidator, Emby* extension helpers, ContainerMembers, ...
└── Configuration/            config.html + config-*.js (admin page), config-controller.js (Emby page controller)
```

### How it runs on Emby

- Emby has no plugin service registrator and no hosted services. `StartupEntryPoint` (constructor-injected with Emby managers) builds `SmartListsHost`, which news up the stores, services, `RefreshQueueService`, `AutoRefreshService`, `BackupService` by hand. Scheduled tasks (`CleanupTask`, `BackupTask`) are instantiated by Emby with no arguments, so they read services from `SmartListsHost.Instance`.
- Emby does not host ASP.NET MVC. `SmartListController` is still written with `[HttpGet]`/`[FromBody]`/`ActionResult`; `ControllerRouter` reads those attributes and dispatches Emby's wildcard request to it, turning results into Emby responses (errors become `{title,status,detail}`, GUIDs serialize without dashes, multipart comes from Emby's `Request.Files`). Adding a controller action needs no extra wiring. The caller id comes from `CallerUserId`, set from the Emby session.
- The admin page is an Emby view (root `<div is="emby-scroller" ... data-controller="__plugin/smartlistsjs">` with a `.scrollSlider` child), not a Jellyfin page. `config-controller.js` (AMD `define`) loads the `config-*.js` modules, injects a stylesheet mapping `--jf-palette-*` to Emby theme variables, and records the shown view in `SmartLists.activePage`.

## Key principles

### DRY
Extract duplicated code into helpers. Check `Utilities/` before adding new ones.

### Thread safety
List processing is sequential (`SemaphoreSlim(1,1)` in `RefreshQueueService`), but background scheduling and caches use concurrent collections (`ConcurrentDictionary`, `ConcurrentQueue`). Use thread-safe collections for anything shared between the background refresh task and the API.

### Two-phase filtering
Expensive fields (People, AudioLanguages, Collections, ...) are evaluated in two phases in `SmartList.cs`: cheap rules first, expensive extraction only for items that pass. Expensive fields are flagged in `FieldRegistry.cs` via `ExtractionGroup`; use `FieldRegistry.IsExpensiveField(name)`.

### Adding rule fields
`FieldRegistry.cs` is the source of truth. A new field needs `FieldRegistry.cs` (definition), `Operand.cs` (property) and `Factory.cs` (extraction). The UI dropdown is populated from the API, but `config-core.js` has hard-coded `FIELD_TYPES` arrays (`STRING_FIELDS`, `LIST_FIELDS`, `NUMERIC_FIELDS`, ...) that decide which inputs and operators show: add the field there too.

### Sorting
Each `Order` in `Core/Orders/` implements `GetSortKey()` (multi-sort) and `OrderBy()` (single-sort). Multi-sort flow: `ApplyMultipleOrders()` -> `WrapOrdersWithChildAggregation()` -> `ApplySortingCore()`. Early-return paths in `FilterPlaylistItems()` must still apply sorting. A new sort needs: `Core/Orders/`, `OrderFactory.cs`, `IsDescendingOrder()` in `SmartList.cs`, and `config-sorts.js`.

## Emby facts worth remembering

Full detail in `docs/port/api-notes.md`. The ones that cause bugs:

- **Item ids are `long InternalId`** (REST, queries, playlist/collection APIs); users are `Guid`. `BaseItem.Id` is a Guid but the plugin keys everything by `InternalId`. Item type names are strings (`ItemKinds`, e.g. `BoxSet`, `Playlist`, `MusicAlbum`) equal to `GetClientTypeName()`.
- `PremiereDate`, `DateCreated`, `LastPlayedDate` are `DateTimeOffset`. `BaseItem.MediaType` is null for containers. `Genres/Studios/Tags/Artists/Album` load lazily from the DB on first read (watch per-item cost on big libraries).
- `BaseItem.SortName`'s setter needs the static `BaseItem.LocalizationManager`. A forced sort title = `SetSortNameDirect` + lock `MetadataFields.SortName`; Emby's post-create refresh can revert it, so both services re-apply it after creation.
- Playlists: owner = the user share with `ManageDelete`; refresh = remove all entries, then add (`IPlaylistManager`). Collections: `ICollectionManager.CreateCollection` (created locked), membership by add/remove diff; BoxSets are virtual (no containing folder: images live under `GetInternalMetadataPath()`, see `GetItemImageFolder`). `UpdateRatingToItems` does nothing on locked items.
- Emby's logger is a different interface from Microsoft's; the plugin code uses `Microsoft.Extensions.Logging` and `EmbyLoggerProvider` bridges it (text is passed as a `"{0}"` argument; the `ReadOnlyMemory` overloads are obsolete errors).
- Emby loads `SixLabors.ImageSharp.dll` from the plugins folder at start-up and logs a harmless `Error loading types from assembly` for it; `Plugin.cs` resolves it from `IApplicationPaths.PluginsPath` because `Assembly.Location` is empty.
- `ILibraryManager`/`IUserManager` return arrays; `IUserManager.Users` is obsolete (use `GetUserList(new UserQuery())`); `BaseItem.IsFolder` is obsolete (`is Folder`).

## UI gotchas (config-*.js and config.html)

- **No ES6 template literals** in the JS: use string concatenation.
- Use `class="emby-input"`, never `is="emby-input"`. Use `showNotification()` for messages, not `Dashboard.alert()`.
- Icons are `<span class="md-icon">name</span>` (Emby), not `material-icons`. Do not hand-draw checkbox icons: Emby's `emby-checkbox` renders its own.
- **Never use `document.querySelector('.SmartListsConfigurationPage')`**: Emby keeps visited views in the DOM, so several can exist. Use `SmartLists.getActivePage()`.
- **Do not write the tab to the URL / do not listen to `hashchange`**: Emby's router treats any hash change as a navigation and rebuilds the view. The tab lives in `SmartLists.currentTab`.
- Emby's `.emby-select-withcolor` forces white text: colours for the custom selects are overridden in the stylesheet injected by `config-controller.js`.
- **New JS files must be registered in two places**: `Emby.Plugin.SmartLists.csproj` (`<EmbeddedResource>`) and `Plugin.cs` `GetPages()` (`PluginPageInfo`), and loaded from the module list in `config-controller.js`.
- The plugin id used by `getPluginConfiguration` (`SmartLists.PLUGIN_ID`) must equal the assembly `[Guid]` in `Plugin.cs`.
- Verifying in the in-app browser: the browser pane must be **visible** or Emby's view transitions never finish; navigate to `.../index.html?fresh=N#!/dashboard` and click the SmartLists sidebar entry (same-URL hash navigation and `location.reload()` leave stale views). Drive forms through the DOM (set value, dispatch `input`/`change`, click the real button).

## Conventions

- `RefreshQueueService` constructs `PlaylistService`/`CollectionService` with `new`: a new constructor dependency must be threaded through `RefreshQueueService` (and `ManualRefreshService`, and `SmartListsHost`).
- Use `MediaTypes.Episode`-style constants (`Core/Constants/`) and `ItemKinds` instead of string literals.
- Per-item exceptions in `SmartList` go through `ReportSkippedItem` (first few at Warning, rest at Debug).
- DTO JSON uses `PlaylistId` / `CollectionId` for the Emby item id (renamed from the Jellyfin names; data written by a pre-rename build is not read back).

## Versioning and releases

Personal project: no release automation beyond a CI build. A release is a zip of `Emby.Plugin.SmartLists.dll` + `SixLabors.ImageSharp.dll` built with `-c Release -p:Version=x.y.z`, extracted into Emby's `plugins` folder (see `docs/install.md`). The Emby plugin version is the assembly version; change it for every distributed build because it is also the resource cache key.

## When making changes

- Update `docs/` when behaviour changes, and append to the progress log in `docs/port/status.md` for anything a later session needs to know.
- UI changes: `config.html` plus the shared `config-*.js`. `user-playlists.html` and the end-user controllers are unused on Emby.
- Create-form fields: required inputs must never sit inside `#advanced-options-body` (collapsed `display:none` hides native validation). New advanced fields go under the matching sub-heading in the fold (Limits / Bumpers / Automation / Sharing / Presentation); new core fields go above it. If a new advanced field has an unambiguous non-default state, add a signal in `syncAdvancedSection` (`config-lists.js`) so edit mode surfaces it as a chip and expands the fold.
- Form fields need updates in HTML, JS (create/edit/display) and the backend DTOs.
