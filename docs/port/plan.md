# Emby port: straight-port plan

Status: active. **Supersedes** [phase1-design-superseded.md](phase1-design-superseded.md), whose dual-platform compat layer is no longer needed. Facts about the Emby API are in [api-notes.md](api-notes.md).

## Decisions

| Question | Decision |
|---|---|
| Keep Jellyfin support? | **No.** This repo becomes the Emby plugin. No upstream tracking. |
| Layout | Convert in place: rename `Jellyfin.Plugin.SmartLists` to `Emby.Plugin.SmartLists` and edit files where they are (keeps git history). Delete Jellyfin-only code. |
| Target | Emby Server 4.10.1.0, net8.0. |
| Playlist refresh | Remove all entries, then add (measured: 150 items in about 15 ms). |
| MVP | Rules, smart playlists, smart collections, manual and scheduled refresh, auto-refresh on library and user-data changes, admin config page. **Deferred:** external lists, custom list images (ImageSharp), backups, end-user page. |
| Distribution | Manual DLL drop (zip per release). Catalog or manifest later. |
| Namespace / assembly | `Emby.Plugin.SmartLists`. New plugin GUID (already chosen in the step 3 work). |

## What the earlier work gives us

- Runtime-verified Emby behavior (types, extras, shares, playlist timing, sort-title locking, user-data events) is in the notes. Nothing needs re-learning.
- The step 3 branch showed the shared code compiles against Emby's types with about 100 targeted edits. In a straight port those become ordinary edits, with no shim layer.
- Throwaway artifacts to drop: `Platform/Shims.cs`, `Platform/EmbyCompat.cs` (useful bits get folded into real helpers), `Platform/MeasurementStubs.cs`.

## Phases

Each phase ends with the project building; from phase 3 on, tests must pass.

1. **Rename and retarget.** `git mv` the projects and solution, replace the namespace and assembly name, point the csproj at Emby's assemblies, remove the Jellyfin package references. Expect a large error count; that is the baseline.
2. **Convert the code to Emby types.** In place, by error group:
   - `Username` to `Name`; `BaseItemKind` becomes Emby's type-name strings (a small `ItemKinds` helper replaces the shim enum); item IDs use `InternalId` (`long`) for queries and `Id` (`Guid`) where Guid is already the identity.
   - Query initializers: `ParentIds`, `PersonIds`, `string[] IncludeItemTypes`.
   - Dates (`DateTimeOffset`), arrays vs lists, `UserDataSaveEventArgs` / `UserDataSaveReason`, the one-off members.
   - Delete Jellyfin-only code: `ServiceRegistrator`, hosted services, `PluginPagesRegistrationService`, MVC attributes.
3. **Port the tests.** Retarget the test project; rewrite `Support/TestItems.cs` to build Emby `BaseItem`s; fix the fallout; get the suite green again. This is the safety net for everything after it, so it comes before the services are rewritten.
4. **Emby services.** Rewrite `PlaylistService` (create as owner, `IsPublic`, shares, remove-all-then-add, find a user's playlists, delete) and `CollectionService` (`ICollectionManager`) against Emby. Thread them through `RefreshQueueService` by injection, not `new`.
5. **Plugin shell.** `IServerEntryPoint` composition root (hand-built service graph), logging adapter over Emby's `ILogger`, `IScheduledTask` shells (cleanup), event wiring (`ItemAdded/Updated/Removed`, `UserDataSaved` with `UpdateUserRating` and `TogglePlayed`).
6. **API and UI.** Replace the MVC controllers with Emby `IService` endpoints (plain handlers underneath); adapt `config-*.js` and the two pages to Emby's plugin page model.
7. **Verify, docs, release.** Run against the test Emby with real playlists and collections; update docs and `CLAUDE.md`; replace the CI and release workflows (Emby build, zip artifact).

## Rules while porting

- Build and tests stay green at each phase boundary; commits are per phase (only when asked to commit).
- Warnings-as-errors and the analyzers come back on once the project compiles; do not leave them off.
- Keep the repo's existing conventions (constants over literals, thread-safe caches, no ES6 template literals in the UI JS).
- Emby-specific facts get checked against the real server before code relies on them.

## Phase 2 outcome (done)

`Emby.Plugin.SmartLists` now compiles for Emby 4.10.1.0 (net8.0) with **0 warnings and 0 errors, with `TreatWarningsAsErrors` back on**. Build with `dotnet build Emby.Plugin.SmartLists/Emby.Plugin.SmartLists.csproj -p:EmbySystemDir="<emby>\system"`.

What changed in the shared code:

- Item identity is `long` (`BaseItem.InternalId`) everywhere items are keyed, cached or queried; user IDs stay `Guid`. `ItemKinds` (string constants equal to `GetClientTypeName()` / `IncludeItemTypes`) replaces `BaseItemKind`.
- New helpers: `Utilities/EmbyLibraryExtensions.cs` (counts, name lookups, language mapping), `Utilities/ItemPersistenceExtensions.cs` (async-shaped wrappers over Emby's synchronous saves), `Services/Playlists/PlaylistOwnership.cs` (owner = `ManageDelete` share).
- `PlaylistService`: ownership via shares; create with `User` / `IsPublic` / `ItemIdList`; replace items by remove-all-then-add; public flag via `BaseItem.IsPublic`.
- `CollectionService`: creation via `ICollectionManager.CreateCollection`; membership changes by add/remove difference; members read through `InternalItemsQuery.CollectionIds`.
- Forced sort title: `SetSortNameDirect` + lock `MetadataFields.SortName` (verified against refreshes).
- `DateTimeOffset` handling, array-vs-list differences, `UserDataSaved` args (`e.User`, `UpdateUserRating`/`TogglePlayed`), `IsPlayed` for folders.
- Jellyfin-only code (`Api/**`, hosted services, `PluginPagesRegistrationService`, `CleanupTask`, `BackupTask`) is excluded from the build, to be rewritten in phases 5 and 6. `ServiceRegistrator` is deleted.
- `RefreshQueueService` and `ManualRefreshService` now take an `IFileSystem` (needed for Emby's `DirectoryService`). They still construct the two services with `new`; the composition root in phase 5 supplies the dependencies.

### Known gaps carried into later phases (compile-clean, runtime-unverified or silently inert)

These compile but will not work as intended on Emby until addressed. Fix and verify each against the test server:

1. **Playlist membership and ownership by reflection in `Factory.cs`** (about lines 3923-3950 and 4175-4190: `OwnerUserId`, `OpenAccess`, `Shares`, `LinkedChildren`, `ItemId`). These read Jellyfin-only properties, find nothing on Emby, and degrade silently: the `Playlists` rule field would see empty membership. Rewrite with `PlaylistOwnership` and `playlist.GetChildren(...)` (phase 4).
2. **`PlaylistUserResolver`** reflects on `Shares` / `OwnerUserId`: same story (phase 4).
3. **Unverified Emby behavior used by the ports:** `InternalItemsQuery.CollectionIds` returning a BoxSet's direct members; `SetOwner` demoting the previous owner; setting `BaseItem.IsPublic` back to private persisting; extra-type string values for `Photo`, `Book`, `AudioBook`, `LiveTvChannel`, `BoxSet`, `Playlist`.
4. **`MediaStream.VideoRangeType` does not exist on Emby** (`VideoRange` does). The Factory reflection returns nothing for it, so rules on that field never match; hide or remove the field in phase 6.
5. **People roles:** Emby's `PersonType` has 8 values (Actor, Director, Writer, Producer, GuestStar, Composer, Conductor, Lyricist); the other fields (Arrangers, Mixers, Authors, ...) can never match. The People prefilter is disabled (returns null, so per-item evaluation stays correct) until Emby's `GetPeople` semantics are verified. Hide the unsupported fields in phase 6.
6. **Remaining reflection** (`Factory.cs` 57 sites, `Engine.cs` 29, mostly JSON/date handling that is platform-neutral; `MediaStreamHelper`, `ArtistOrder`, `LastPlayedOrderBase`): audit each lookup against Emby's real types the way `MediaStream` was checked here.
7. **Mixed Jellyfin vocabulary** in DTO fields and the UI (`JellyfinPlaylistId`, `JellyfinCollectionId`, log messages): rename together with the UI work in phase 6 so the JSON and JS stay in sync.
8. **ImageSharp bundling** (collage/badge covers): compiles; whether Emby's plugin loader resolves a dependency DLL next to the plugin is unverified.
9. (Resolved in phase 3: the test project builds against Emby types and all 1,732 tests pass. See `status.md`.)
