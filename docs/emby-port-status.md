# Emby port: running status and decision log

Living handoff document. Read this first, then [emby-port-plan.md](emby-port-plan.md) (phases and known gaps) and [emby-api-notes.md](emby-api-notes.md) (verified Emby API facts). Last updated at the point described under "Where we are".

## Decisions (do not relitigate without new evidence)

| Decision | Detail |
|---|---|
| Jellyfin dropped | This repo becomes the Emby plugin. No upstream tracking, no compat layer, no Jellyfin-named shims. |
| Target | Emby Server 4.10.1.0, net8.0. Local install at `C:\Users\kgiglio\Downloads\embyserver-win-x64-4.10.1.0` (plugins in `programdata\plugins`, launch `system\EmbyServer.exe`). |
| Layout | Converted in place: `Jellyfin.Plugin.SmartLists` renamed to `Emby.Plugin.SmartLists` (namespace, assembly, tests project, sln). Branch `emby/step3-shared-compile`. **Nothing is committed.** |
| MVP | Rules, smart playlists and collections, manual and scheduled refresh, auto-refresh, admin config page. Deferred: external lists, end-user page, backups (images compile with ImageSharp bundled, unverified at runtime). |
| Distribution | Manual DLL zip for now. |
| Item identity | `long BaseItem.InternalId` for items (Emby's REST/query/playlist/collection APIs use it). User IDs stay `Guid`. `ItemKinds` string constants replace `BaseItemKind`. |
| Playlist refresh | Remove all entries, then add, via `IPlaylistManager` (measured about 15 ms for 150 items, order preserved). |
| Playlist owner | The user holding the `ManageDelete` share row (`PlaylistOwnership`). |
| Forced sort title | `SetSortNameDirect` + lock `MetadataFields.SortName` (survives FullRefresh and ReplaceAllMetadata; verified). |
| Collections | Create via `ICollectionManager.CreateCollection` (locked); membership changes by add/remove diff; members read with `InternalItemsQuery.CollectionIds` (unverified). |
| Dates | Emby uses `DateTimeOffset`; shared reader `Utilities/ReflectedDate`, typed `DateUtils`. |
| People prefilter | Disabled (returns null, per-item evaluation) until Emby `GetPeople` semantics are verified. |
| Warnings | `TreatWarningsAsErrors` is ON for the plugin (0 warnings, 0 errors). Tests project is relaxed on purpose. |

## How to build and test (Windows)

```powershell
$e = 'C:\Users\kgiglio\Downloads\embyserver-win-x64-4.10.1.0\system'
dotnet build Emby.Plugin.SmartLists/Emby.Plugin.SmartLists.csproj -p:EmbySystemDir=$e
dotnet test  Emby.Plugin.SmartLists.Tests/Emby.Plugin.SmartLists.Tests.csproj -p:EmbySystemDir=$e
```

- Tests run on net10.0 (ASP.NET 8 runtime is not installed here). The three `MediaBrowser.*` DLLs are copied next to the tests (`Private=true`); `Support/EmbyAssemblyResolver.cs` resolves anything else from `EmbySystemDir`.
- If tests load the wrong `MediaBrowser.Controller` (version 12.0.0 = Jellyfin), the test `bin/obj` still holds stale Jellyfin DLLs: delete `Emby.Plugin.SmartLists.Tests/bin` and `obj` and rebuild.

## Test environment (Emby)

- Libraries at `C:\claude\{Movies,Music,Shows}` hold generated placeholder media (5 movies + trailer, 150 bulk movies, 2 series, 12 tracks). Users: `kevin` (Guid 187e8098a0404bf1a42200bcf6f9d29f, internal id 1), `test1` (id 2).
- API key is in `%USERPROFILE%\.emby-spike-key`; never print it; send as `X-Emby-Token` to `localhost:8096` only; delete when done.
- A throwaway probe plugin `SmartListsEmbySpike.dll` is deployed in the plugins folder (source only in the Claude session scratchpad). Remove it when finished.

## Where we are

- **Phase 1 (rename/retarget): done.**
- **Phase 2 (convert to Emby types): done.** Plugin compiles clean.
- **Phase 3 (port the tests): in progress.** The test project compiles. Last run: **1,732 tests, 1,690 pass, 42 fail** (started at 608 failing). The 30 tests of `Api/Filters` are excluded until phase 6 (`Api/SmartListsProblemDetailsAttributeTests.cs`, `Compile Remove="Api\**"`).
- Fixture decisions made in `Emby.Plugin.SmartLists.Tests/Support/TestItems.cs`: every built item/user gets a unique `InternalId` (`TestItems.NextId()`); `TestLibraryManager` is keyed by `InternalId`, answers `GetItemById(long|Guid [,ctx])`, `GetItemLinks` (empty), array return types; `NeutralLocalizationManager` (`GetRatingScore`/`GetRatingLevel` null, `RemoveDiacritics` identity) is installed globally because Emby's `SortName` setter needs it; `LoudItemRepository` (answers `OnItemLinksFilled`, throws on anything else); `TestItems.NewUser(name)`; `.Loaded()` / `MarkTaggedItemsLoaded()` on items whose genres/tags/studios are set in memory, otherwise Emby's lazy loader overwrites them with the stored (empty) values.

### The 42 remaining failures (by test class)

UserDataOrderTests 9, Entities (ParentValues/People-style fixtures) 6, ParentValuesPrefilterResolverTests 5, RoundRobinLeastRecentlyWatchedTests 4, ContainerMatchingTests 4, AggregateUserScopeTests 3, NumericAndDateOrderTests 2, GroupIntoCollectionsTests 2, NameOrderTests 2, and one each in RoundRobinAirBlock, AncestorWalk, RoundRobinGrouping, CollectionAggregateMetadata, SimilarToGroupMapping. All remaining ones are `Assert` value/collection differences. Treat each as either a fixture gap or a **real production bug** (the date reflection bug in `DateUtils`/last-played was found this way and fixed). Do not just edit expectations: decide per failure.

Helper scripts used this session live in the Claude scratchpad (not in the repo): a compiler-driven fixer for Guid-to-long `.Id` edits and a TRX failure grouper. Recreate them if needed; `dotnet test --logger "trx"` plus grouping by message is the useful part.

## What is left

1. **Finish phase 3**: resolve the 42 failures; re-run the whole suite; keep 0 plugin warnings.
2. Port or re-home the 30 excluded API-filter tests when phase 6 lands.
3. **Phase 4**: runtime-verify and finish services against the test Emby (see known gaps in the plan: playlist membership/ownership reflection in `Factory.cs` and `PlaylistUserResolver`, `CollectionIds`, `SetOwner`, `IsPublic` round trip, unverified type-name strings).
4. **Phase 5**: `IServerEntryPoint` composition root (build the service graph by hand, pass `IFileSystem` etc.), logging adapter over Emby's `ILogger`, `IScheduledTask` shells (cleanup/backup), event wiring (`ItemAdded/Updated/Removed`, `UserDataSaved`: `UpdateUserRating` = favorite toggle, `TogglePlayed`). Rewrite from the excluded files `AutoRefreshHostedService`, `StorageMigrationHostedService`, `CleanupTask`, `BackupTask`.
5. **Phase 6**: replace the MVC controllers (`Api/**`, about 6,000 lines) with Emby `IService` endpoints; adapt `config-*.js` and both HTML pages; hide fields that can never match on Emby (people roles beyond Actor/Director/Writer/Producer/GuestStar/Composer/Conductor/Lyricist, `VideoRangeType`); rename `Jellyfin*` DTO/UI vocabulary together.
6. **Phase 7**: verify against the test Emby with real playlists/collections, update docs and `CLAUDE.md`/`AGENTS.md`, replace CI and release workflows (Emby build, zip artifact), remove the spike plugin and the API key file.
7. **Performance audit** (new): in Emby, `BaseItem.Genres/Studios/Tags` load lazily from the database on first read, so rule evaluation over large libraries can issue a query per item. Measure on the test server before trusting refresh times.
8. Audit the remaining name-based reflection in `Factory.cs` (57 sites), `Engine.cs` (29), `MediaStreamHelper`, `ArtistOrder`, etc. against Emby's real types (only `MediaStream` and the user-data/date properties have been checked).

## Gotchas already hit

- Emby `SortName` setter needs `BaseItem.LocalizationManager` (static) or it throws NullReferenceException.
- `BaseItem.PremiereDate` and `UserItemData.LastPlayedDate` are `DateTimeOffset?`; `is DateTime` tests silently fail.
- Extras: an extra's `ParentId` is its owner's `InternalId`; there is no `GetOwner()`.
- Emby returns arrays (`BaseItem[]`, `long[]`) where Jellyfin returned lists; use `.Length`.
- `ImageRefreshMode=None` is rejected by the refresh endpoint (HTTP 400).
- Do not run the app or tests from a shell where `Remove-Item` paths contain quoted XPath strings (the safety wrapper misparses them); use fresh result folders instead.

## Progress log (append-only; newest last)

- Phase 3 checkpoint A: tests compile; 608 failing -> 42 after fixture work (InternalId everywhere, library/localization/repository stubs, `MarkTaggedItemsLoaded`, `NewUser`).
- Phase 3 checkpoint B: **21 failing / 1,711 passing (1,732 run).** Fixed: a machine-timezone harness bug (DateTime of unspecified kind assigned to DateTimeOffset used the local offset; fixtures now use `TestItems.Utc(...)`). Production bugs found and fixed so far by this phase: `DateUtils`/last-played reflection expected `DateTime` but Emby stores `DateTimeOffset` (now typed / `ReflectedDate`). Remaining failing classes: ParentValuesPrefilterResolverTests 5, ContainerMatchingTests 4, NameOrderTests 2, GroupIntoCollectionsTests 2, AggregateUserScopeTests 2, NumericAndDateOrderTests 2, and one each in CollectionAggregateMetadata, RoundRobinGrouping, SimilarToGroupMapping, AncestorWalk.
- Phase 3 checkpoint C: **8 failing / 1,724 passing.** Production changes made from test findings: `PlaylistUserResolver` now lists users with `GetUserList(new UserQuery())` (the reflective `GetUsers`/`Users` hack is gone; `IUserManager.Users` is obsolete on Emby). Fixture changes: localization stub strips diacritics and returns a constant `GetRatingLevel`; audio builders mark tags loaded; the two "extra via owner" tests were rewritten for Emby semantics (an extra's `ParentId` is its owner). Remaining: ContainerMatchingTests 4 (`MatchByMembersOff_*`), GroupIntoCollectionsTests 2, CollectionAggregateMetadata 1 (`RunsDespiteMetadataLock`, rating), SimilarToGroupMapping 1 (best block score).
- Phase 3 checkpoint D: **2 failing / 1,730 passing.** Real production bug found and fixed: `Factory.cs` built `Operand.MediaType` with `baseItem.MediaType.ToString()`; on Emby `MediaType` is a nullable string (null for BoxSet/Series/Season/MusicAlbum), so operand creation threw for every container item and the swallowed exception silently dropped them (rules on collections/series would never match). Now `baseItem.MediaType ?? string.Empty`. Verified client type names (by constructing the classes): `BoxSet`, `Playlist`, `Movie`, `Series`, `Season`, `Episode`, `Audio`, `MusicAlbum`, `MusicArtist`, `Folder`. Remaining: `SimilarToGroupMappingTests.ItemMatchingTwoSimilarToBlocks_SortsOnItsBestBlockScore`, `CollectionAggregateMetadataTests.RunsDespiteMetadataLock` (rating "R" came back null).
- Debug technique that works: a throwaway xunit test that runs `SmartList.FilterPlaylistItems` with a capturing `ILogger` and fails with the log text; read the message from the TRX. Swallowed exceptions in the "simple path" hide real errors (logged only at Debug).
- **Phase 3 DONE (checkpoint E): 1,732 tests, 0 failing; plugin 0 warnings / 0 errors.** Last fixes: `Array.IndexOf(result, item.Id)` in `SimilarToGroupMappingTests` compared `long[]` to a `Guid` through the `object` overload (silent -1); the rest of the tests' `.Id` usages were audited and are consistent (Guid vs Guid, or just user-data key strings). Second production bug fixed from a test: Emby's `UpdateRatingToItems` does nothing on a locked item and smart collections are always locked, so `CollectionService.UpdateAggregateMetadata` now lifts `IsLocked` around that one in-memory call and restores it.
- Production bugs found by porting the tests (all fixed): (1) `DateUtils`/last-played reflection expected `DateTime`, Emby uses `DateTimeOffset`; (2) `Operand.MediaType` threw for container items (`MediaType` is a nullable string on Emby), silently dropping every collection/series/season/album from rule evaluation; (3) aggregate rating never applied to locked smart collections; (4) `PlaylistUserResolver` reflective user lookup (now `GetUserList`).
- **Current state:** Phases 1, 2, 3 done. Phase 4 next. Test count excludes the 30 API-filter tests (`Api/SmartListsProblemDetailsAttributeTests.cs`, excluded until phase 6).

### Carry-forward risks surfaced by phase 3 (add to phase 4/5 work)

- **Swallowed exceptions hide real bugs.** `SmartList.ProcessItemsSimple` (and the chunked path) catch per-item exceptions and log only at Debug ("Error processing item ... Skipping item"). Both MediaType and similar bugs were invisible in logs. In phase 4/5 raise this to Warning (with the exception) so a server admin can see skipped items.
- **Fixture facts worth keeping:** Emby items need `Loaded()`/`MarkTaggedItemsLoaded()` before in-memory genres/tags/studios/artists/album are readable; `TestItems.Utc(...)` for any date (unspecified `DateTime` to `DateTimeOffset` uses the machine's timezone); a probe test with a capturing `ILogger` or a recording `DispatchProxy` is the fastest way to learn what Emby code calls.
- **Still unverified against a running Emby (phase 4):** everything listed in "Known gaps" in `emby-port-plan.md`. The unit tests prove the plugin logic against Emby's real types, not Emby's runtime behavior.
- **Committed** phases 1-3 as `a13ab8d` on `emby/step3-shared-compile` (223 files). Working tree clean after that commit. (`/verify` skill not applicable: it drives a Jellyfin Docker container.)

### Phase 4 plan (started)

Goal: make the playlist/collection services actually work on a live Emby. Order of work:
1. **Offline, unit-testable first:** replace the remaining Jellyfin-only reflection in `Factory.cs` (playlist membership/ownership: `OwnerUserId`, `OpenAccess`, `Shares`, `LinkedChildren`, `ItemId`, around lines 3923-3950 and 4175-4190) and any similar in `PlaylistUserResolver`/others with typed Emby calls (`PlaylistOwnership`, `playlist.GetChildren`); add unit tests. Raise swallowed per-item exceptions in `SmartList` from Debug to Warning.
2. **Minimum host to run the code in Emby** (pulls forward the core of phase 5): `IServerEntryPoint` composition root that builds the service graph by hand (incl. `IFileSystem`, stores, `RefreshQueueService`), and a logging adapter from Emby's `ILogger` to `Microsoft.Extensions.Logging.ILogger<T>`; plus a temporary authenticated debug endpoint to trigger a refresh from a DTO.
3. **Runtime verification on the test Emby** (libraries at `C:\claude`, users `kevin`/`test1`): create a smart playlist and a smart collection from rules, refresh, change membership, check ownership/IsPublic/sort title/collection membership (`CollectionIds`), per-user playlists, and record results in `emby-api-notes.md`. Remove the throwaway spike plugin from the Emby plugins folder when the real plugin is deployed.
- **Phase 4, step 1 done** (offline): all reflective container-member/playlist-ownership lookups replaced with typed Emby calls. New `Utilities/ContainerMembers.cs` (`Playlist.GetChildren(query)` verified; BoxSet members via `CollectionIds` still unverified). `Factory` no longer reflects on `OwnerUserId`/`OpenAccess`/`Shares`/`LinkedChildren`/`ItemId` (Emby's user-scoped playlist query already returns only visible playlists). `SmartList` reports skipped-item exceptions through `ReportSkippedItem` (first 5 per refresh at Warning, then Debug plus one notice). 1,732 tests pass, 0 warnings.
- **Next: phase 4 step 2** (minimum host to run in Emby): `IServerEntryPoint` composition root + Emby-to-`Microsoft.Extensions.Logging` adapter + a temporary authenticated debug endpoint, then step 3 (runtime verification on the test Emby).

### Checkpoint F — phase 4 step 2 (host composed, not yet run in Emby)
- Added `Host/EmbyLoggerProvider.cs` (MEL → Emby `ILogger`, text passed as `"{0}"` arg; ReadOnlyMemory overloads are obsolete errors), `Host/SmartListsHost.cs` (hand-built composition root, `Instance`), `Host/StartupEntryPoint.cs` (`IServerEntryPoint`), `Host/DebugService.cs` (TEMPORARY `/SmartListsDebug/Refresh?Kind=&Name=&UserName=&MediaType=&Field=&Operator=&Value=&Public=`; remove in phase 6). External list service is null.
- Plugin builds with 0 warnings. Next: step 3 — deploy to test Emby (needs user OK to restart, remove spike DLL), hit the debug endpoint, record results in `emby-api-notes.md`.

### Checkpoint G — phase 4 step 3 (runtime verification) DONE for core paths
- Deployed `Emby.Plugin.SmartLists.dll` + `SixLabors.ImageSharp.dll` to the test Emby plugins folder; spike DLL removed. Results in `emby-api-notes.md` ("Phase 4 runtime results"). Create/update/public flip for playlists and collections all pass. Test lists deleted afterwards.
- Remaining phase 4 gaps: sort title, AllUsers playlists, images, People prefilter, collection "[Smart]" suffix on create. Next: phase 5 (auto-refresh/events, startup migration, scheduled tasks, backups).

### Checkpoint H — phase 4 closed out
- Verified on the test Emby: sort title (with create-time re-assert), AllUsers via the queue, custom images (playlist + collection), People rules. Fixed along the way: ImageSharp resolve, BoxSet image folder, removed unsupported collection DisplayOrder. 1,732 tests pass.
- Test data left in place: `C:\claude\Movies\Test Movie One (2001)\movie.nfo` (actors Jane Tester, Bob Sample; director Dan Directo). Debug endpoint still present (remove in phase 6).
- Still open: People prefilter disabled; NFO saver IOException after collection create (Emby-side). Next: phase 5.

### Checkpoint I — phase 5 DONE (entry point, events, tasks)
- `SmartListsHost` now also builds `BackupService` and (via `StartAutoRefresh()`, called from `StartupEntryPoint.Run`) `AutoRefreshService`; `CreateLogger<T>()` is public. `AutoRefreshService` itself needed no change (its `ItemAdded/Removed/Updated`, `UserDataSaved` wiring compiled against Emby already).
- `CleanupTask`/`BackupTask` are real Emby `IScheduledTask`s (`Execute(CancellationToken, IProgress<double>)`, trigger types are strings `"DailyTrigger"`/`"WeeklyTrigger"`). Emby constructs tasks itself, so they take no constructor args and resolve services from `SmartListsHost.Instance` on use. Cleanup's owner check uses `PlaylistOwnership` (owner is a `long` InternalId) and an unfiltered `GetItemList` (no DB-side provider-id narrowing).
- Deleted dead Jellyfin shells: `AutoRefreshHostedService`, `StorageMigrationHostedService` (only migrated old Jellyfin storage layouts; nothing to migrate on a fresh Emby install), `PluginPagesRegistrationService`; csproj exclusions trimmed to `Api\**`.
- Runtime-verified on the test Emby: both tasks list under category SmartLists and run to Completed; backup produced a zip when `BackupEnabled` was set in `plugins\configurations\Emby.Plugin.SmartLists.xml` (config XML loads); auto-refresh fires on library add (new movie -> playlist grew), on removal (list shrank), and on favorite/unfavorite with `OnAllChanges`; the cache is rebuilt from stored lists at startup. 1,732 tests pass, 0 warnings.
- Not verified: scheduled (time-of-day) refresh firing (timer initialised, next check logged), played-status triggers, parent-favorite triggers, cleanup actually deleting an orphan.
- Next: phase 6 (replace `Api/**` MVC controllers with `IService` endpoints, adapt UI, hide unsupported fields, rename Jellyfin vocabulary, remove debug endpoint).

### Checkpoint J — phase 6 DONE (admin API + UI on Emby)
- **API**: `Api/ControllerRouter.cs` dispatches Emby's single wildcard endpoint (`Api/EmbyApiService.cs`, `[Route("/Plugins/SmartLists/{Path*}")]`, `[Authenticated(Roles="Admin")]`) to the existing `SmartListController` by reading its `[HttpGet/Post/Put/Delete]` templates and `[FromRoute/Query/Body]`/`IFormFile` parameters. The controller now takes `SmartListsHost` instead of DI and gets the caller id from `CallerUserId`. Error bodies become `{title,status,detail}`; GUIDs serialize without dashes like Emby's own. Multipart uploads come from Emby's `Request.Files`/`GetFormData()` (reading the raw stream fails: Emby consumes it). End-user controllers (`UserPagesController`, `UserSmartListController`) stay excluded (end-user page is outside the MVP).
- **UI**: page root is an Emby view (`<div is="emby-scroller" class="view ..." data-controller="__plugin/smartlistsjs">` + `.scrollSlider`), no `<html>/<body>`, no inline script. `config-controller.js` is an AMD controller (`define(['baseView', ...])`) that loads the `config-*.js` modules once (with a per-session `&t=` stamp), injects a stylesheet mapping `--jf-palette-*` to Emby theme variables, records the shown view in `SmartLists.activePage`, then fires `pageshow` so `config-init.js` initialises it. `PLUGIN_ID` fixed to the Emby GUID. Icons `material-icons` -> `md-icon`.
- **Gotchas found**: (1) Emby serves plugin resources with an ETag derived from the plugin *version* only, so a rebuild at the same version leaves browsers on stale JS/HTML; dev builds use `-p:Version=0.MMdd.HHmm` (releases change version anyway). (2) Emby's router treats any hash change as navigation and rebuilds the view, so the tab can no longer be kept in the URL (`updateUrl` stores `SmartLists.currentTab`; hashchange handler removed). (3) Emby keeps old views in the DOM, so page lookups use `SmartLists.getActivePage()` instead of `document.querySelector`. (4) The Browser pane must be visible or Emby's view transitions never finish. (5) Drive the page via the sidebar link / a fresh URL (`?fresh=N`); same-URL hash navigation and `location.reload()` leave stale views.
- **Vocabulary**: `JellyfinPlaylistId`/`JellyfinCollectionId` -> `PlaylistId`/`CollectionId` (DTOs, JS, tests), other `Jellyfin*` identifiers/text -> `Emby*`/`Emby`. Left alone: `PrefilterValueCleaner`/`StreamLanguagePrefilterResolver` doc comments that describe Jellyfin ABIs, and the Help/Documentation links (still the upstream docs).
- **Fields hidden**: people roles Emby can't represent (arrangers, mixers, authors, ... 16 fields) and `VideoRangeType` removed from `FieldRegistry` and the UI arrays.
- **Cleanup**: temporary debug endpoint deleted. API-filter tests restored (+29) and `ControllerRouterTests` added (+10): 1,771 tests pass.
- **Verified in the browser** (kevin, Emby 4.10.1.0): page loads and themed; Create List form -> playlist created (10 items); Manage Lists shows lists; Settings tab loads plugin config. Verified by API: list/get/create/update/delete, refresh, enable/disable, images upload/list/file/delete, backups create/list/download/preview/restore-upload, status/timer endpoints.
- **Not verified in the UI**: editing/cloning/deleting from the Manage tab, image upload widget, Status tab polling, bulk actions, backup UI, schedule editor, template picker, saving Settings.
- Next: phase 7 (verify remaining UI, docs, CLAUDE.md/AGENTS.md, CI/release workflows, remove spike plugin leftovers and the API key file).

### Checkpoint K — UI click-through (phase 6 close-out)
- Verified in the browser pane (kevin): Create (blank and from template), Clone, Edit + Update, Manage list/expand/kebab, bulk Disable/Enable (removes/recreates the Emby lists), single delete via confirm modal, Status tab (shows last refresh), Settings tab save (round-trips through `plugins\configurations\Emby.Plugin.SmartLists.xml`), schedule row editor, More-options fold, image upload through the dynamic file input (served as the list's Primary image), manual backup creation.
- Fixed: custom-select text was white-on-light (Emby's `.emby-select-withcolor` forces white) and dropdowns were translucent -> overrides in the controller's injected stylesheet; "View in Emby" link now `/web/index.html#!/item?id=..&serverId=..`; leftover `jellyfin*` JS variable names.
- Hidden because they cannot work yet: Settings "User Access" and "External Lists" sections (wrapped `display:none`, `data-unsupported`), the `ExternalList` field and External List Order sorts (registry), the Trakt and ListenBrainz templates.
- Still open: help/documentation links point at the upstream Jellyfin docs site; cleanup-task orphan sweep unverified (40 h recency window); delete from the end-user page n/a; Created By shows "Unknown".
- Lists deleted through the API before the `JellyfinPlaylistId` -> `PlaylistId` rename left orphan Emby playlists (stored JSON still used the old key): only affects my test data, but note any data written by a pre-rename build is not read back.

### Checkpoint L — fresh-install test (clean Emby profile, port 8097)
- Setup: copy of the Emby 4.10.1.0 `system` dir to `Downloads\emby-clean-test\system` (Emby refuses a second instance from the same exe path) with its own `programdata` (port 8097 via `config/system.xml`), plugin installed by extracting a release-style zip (`Emby.Plugin.SmartLists.dll` + `SixLabors.ImageSharp.dll` only; built `-c Release -p:Version=0.1.0`).
- Verified: plugin loads and starts on a clean profile; SmartLists appears in the admin sidebar; page loads; create a playlist and a collection from the UI (155 movies scanned); restart keeps both (lists persisted under `data\smartlists`, auto-refresh cache reloads 1 playlist + 1 collection); `currentuser` works with a browser session.
- **Cleanup sweep verified**: with the 40 h recency guard temporarily disabled in a throwaway build (reverted, not committed), deleting a list's stored config and running the cleanup task removed the orphaned BoxSet and kept the live playlist.
- Known cosmetic: on every startup Emby logs `Error loading types from assembly ... Could not load SixLabors.ImageSharp, Version=3.0.0.0` (a type scan of the plugin folder, before the plugin's own AssemblyResolve exists). Harmless: collage/badge/cover generation works. Possible fix later: avoid ImageSharp types in anything Emby reflects over, or resolve it from the default context.
- First submit after creating a list sometimes does nothing when driven by script (button disabled while the post-create refresh runs); a second click worked. Likely a harness timing artefact; recheck by hand.
- "Created By" row is now hidden when unknown.
