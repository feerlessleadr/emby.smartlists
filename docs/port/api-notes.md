# Emby API notes (Jellyfin → Emby mapping)

Working notes for the Emby port. Not published: this file is outside `docs/content/`.

## How this was produced

- Reference: Emby Server **4.10.1.0** (`system/MediaBrowser.{Controller,Model,Common}.dll`; runtime **net8.0**).
- Jellyfin reference: `Jellyfin.Controller`/`Jellyfin.Model` **12.0.0** (net10.0).
- Method: compiled the plugin's `Core/`, `Utilities/` and `Services/` sources against the Emby DLLs in a scratch project, with the missing Jellyfin namespaces stubbed (`Jellyfin.Data.Enums.BaseItemKind`, `Jellyfin.Database.Implementations.*`, `Jellyfin.Extensions`) and `User` aliased to Emby's `MediaBrowser.Controller.Entities.User`. `BasicDirectoryService`, `BackupTask` and `CleanupTask` were excluded (interface-shape mismatches). Result: about 127 files compiled, 35 with errors, 278 errors total. The count is a lower bound, because fixing errors can expose more.
- Status: compile-level only. Nothing here has been verified at runtime against a running Emby server. Signatures (what exists) come from the compiler; semantics (what it does) are unverified.

## Summary

| Area | Finding |
|---|---|
| Type overlap | 889 public types in Emby vs 882 in Jellyfin; `BaseItem`, `Folder`, `Movie`, `Episode`, `Series`, `Season`, `Audio`, `MusicAlbum`, `MusicArtist`, `ILibraryManager`, `IUserManager`, `IUserDataManager`, `IPlaylistManager`, `ICollectionManager`, `IScheduledTask`, `IApplicationPaths`, `BasePlugin`, `IHasWebPages`, `MediaStream`, `InternalItemsQuery` all exist under the same names. |
| Item ID | **Emby `BaseItem` has both `Guid Id` and `long InternalId`.** Query filters, playlist/collection managers, user data and shares all take the `long` internal ID. `ILibraryManager.GetItemById` accepts either. So this is an ID translation problem, not a type swap. |
| Item kind | **Emby has no `BaseItemKind` enum; item types are strings.** |
| Clean in compile test | `Engine`, `Operand`, `Expression`, `FieldRegistry`, `Core/Models`, `Core/Enums`, and nearly all of `Core/Orders/` (only `SeriesNameOrder` errors, 6). |
| Error hot spots | `PlaylistService` 54, `CollectionService` 53, `Factory.cs` 49, `SmartList.cs` 16, `RefreshQueueService` 13, `AutoRefreshService` 11, `ParentValuesPrefilterResolver` 10, `LibraryManagerHelper` 8, `StreamLanguagePrefilterResolver` 7. |

## Type-level mapping

| Jellyfin | Emby 4.10.1.0 | Notes |
|---|---|---|
| `Jellyfin.Database.Implementations.Entities.User` | `MediaBrowser.Controller.Entities.User` | Different type and namespace. No `Username` member (see below). |
| `Jellyfin.Data.Enums.BaseItemKind` | none | Emby uses string type names. |
| `Jellyfin.Database.Implementations.Enums.SortOrder` | `MediaBrowser.Model.Entities.SortOrder` | Ambiguous if both namespaces are imported. |
| `Jellyfin.Data.Events.Users.UserDataSaveEventArgs` | `MediaBrowser.Controller.Library.UserDataSaveEventArgs` | Different namespace; members differ. |
| `MediaBrowser.Controller.Library.UserItemData` | none | Emby: `MediaBrowser.Controller.Entities.UserItemData`. |
| `MediaBrowser.Controller.Entities.IHasLookupInfo` | none | |
| `MediaBrowser.Model.Playlists.*` | none | `PlaylistUserPermissions` is missing from `MediaBrowser.Model.Entities` too. |
| `Jellyfin.Extensions.*` | none | e.g. `string.RemoveDiacritics`. |
| `Jellyfin.Data.*` | none | |
| `IScheduledTask` | `IScheduledTask` | Same name; `Execute(CancellationToken, IProgress<double>)` is not implemented as written by `BackupTask`/`CleanupTask`. |
| `IDirectoryService` | `IDirectoryService` | Same name; different member set and return types (`List<string>` vs the plugin's `GetFilePaths` return type). `BasicDirectoryService` needs a full rewrite. |
| `IServerEntryPoint` | `IServerEntryPoint` | Present in Emby; Jellyfin uses `IHostedService`. Not yet exercised. |

## Member-level gaps (count = uses in the compile test)

| Jellyfin member | Emby status | Count |
|---|---|---|
| `BaseItem.GetBaseItemKind()` | missing | 23 |
| `User.Username` | missing | 19 |
| `Playlist.Shares` | missing | 12 |
| `BaseItem.UpdateToRepositoryAsync` | missing | 11 |
| `Playlist.UpdateToRepositoryAsync` | missing | 10 |
| `LinkedChild.ItemId` | missing | 7 |
| `Playlist.OwnerUserId` | missing | 7 |
| `InternalItemsQuery.ParentId` | missing | 7 |
| `ILibraryManager.UpdateItemAsync` | missing | 4 |
| `UserDataSaveEventArgs.UserId` | missing | 4 |
| `UserDataSaveReason.UpdateUserData` | missing | 3 |
| `Playlist.LinkedChildren` | missing | 3 |
| `InternalPeopleQuery.ItemId` | missing | 3 |
| `BaseItem.ForcedSortName` | missing | 2 |
| `PlaylistCreationRequest.Public` / `.UserId` | missing | 1 / 1 |
| `BaseItem.GetOwner` | missing | 1 |
| `ILibraryManager.GetCount` | missing | 1 |
| `ILibraryManager.GetPeopleNames` | missing | 1 |
| `ILibraryManager.GetMediaStreamLanguages` | missing | 1 |
| `ILocalizationManager.TryGetISO6392TFromB` | missing | 1 |
| `InternalItemsQuery.Person` | missing | 1 |
| `IItemRepository.GetGenreNames` / `GetStudioNames` / `GetMusicGenreNames` | missing | 1 each |
| `Audio.AlbumEntity` | missing | 1 |
| `Episode.SeasonId` | missing | 1 |
| `MetadataProvider`, `MetadataField` | not found in current namespaces (may live elsewhere or be named differently) | 1 each |

## Type-mismatch categories

| Mismatch | Count | Cause |
|---|---|---|
| `BaseItemKind` → `string` | 21 | Item kinds are strings in Emby. |
| `long` → `Guid` | 19 | Item IDs are `long`. |
| `long != Guid` | 12 | Same. |
| `Guid[]` → `long[]` | 5 | Same (ID arrays in queries). |
| `BaseItemKind[]` → `string[]` | 6 | `IncludeItemTypes` etc. |
| `DateTimeOffset` → `DateTime` | 7 | Date properties differ. |
| `string` → `EventId` | 6 | Logger call shape differs. |
| `IReadOnlyList<BaseItem>` → `BaseItem[]` | 2 | Collection return types differ. |
| `List<string>` → `string[]` | 2 | Same. |

## Where there is no equivalent (needs design, not a rename)

- **Playlist ownership and sharing.** `Playlist.Shares`, `OwnerUserId`, `LinkedChildren`, `PlaylistCreationRequest.UserId`/`Public` and `PlaylistUserPermissions` are all missing. Per-user playlists are a core feature, so this needs an adapter and probably different Emby mechanics.
- **Item persistence.** The `UpdateToRepositoryAsync` / `UpdateItemAsync` write path has no same-named Emby counterpart. The Emby way to save item changes still has to be found.
- **Item identity.** `Guid` vs `long InternalId` runs through the refresh caches, linked-child handling and all `InternalItemsQuery` ID filters (see "Resolved" below: the `long` ID is the one Emby's APIs take).
- **Item kind.** `MediaTypes.cs`, query `IncludeItemTypes`, and every `GetBaseItemKind()` call need a mapping to Emby's string types.
- **Aggregate name queries** (genres, studios, music genres, people names, stream languages) used by the prefilter resolvers and `LibraryManagerHelper`.
- **User-data change events** used by `AutoRefreshService` (`UserId`, save reason).

## Resolved: Emby equivalents (signatures from reflection; behavior unverified)

Signatures below were read by loading the Emby 4.10.1.0 assemblies. They show what exists and how it is shaped. How each call behaves at runtime (ownership rules, duplicate handling, what triggers events) has not been tested.

### Playlists

| Jellyfin | Emby | Notes |
|---|---|---|
| `IPlaylistManager.CreatePlaylist(PlaylistCreationRequest)` | `Task<PlaylistCreationResult> CreatePlaylist(PlaylistCreationRequest)` | Request: `Name`, `long[] ItemIdList`, `string MediaType`, `User User`, `bool IsPublic`. Jellyfin's `UserId` becomes a `User` object and `Public` becomes `IsPublic`. Result `Id` is a `string`. |
| add items | `AddToPlaylist(Playlist, long[] itemIds, bool skipDuplicates, User, CancellationToken)` (+ `AddToPlaylist(long playlistId, long[] itemIds, User)`) | `GetAddToPlaylistInfo(...)` previews. |
| remove items | `RemoveFromPlaylist(Playlist or long playlistId, long[] entryIds)` | Takes **entry IDs** (per-row IDs), not item IDs. |
| reorder | `MoveItem(Playlist or long playlistId, long entryId, int newIndex)` | |
| replace whole content | **no single call** | **Decision: remove all entries (`RemoveFromPlaylist`) then add (`AddToPlaylist`) through the public manager API.** Avoids the low-level `IItemRepository.UpdateListItems(BaseItem list, LinkedChild[] items)`. Open item: benchmark on large playlists on a real server; revisit only if too slow. Also check that an empty playlist between the two steps does not trigger unwanted `PlaylistItemsRemoved` side effects or client-visible flicker. |
| `Playlist.LinkedChildren` / `LinkedChild.ItemId` | `LinkedChild` has only `Path`; playlist rows are `ListItem { long ListItemEntryId, long ListItemId }` via `IItemRepository.AddListItems/GetNewListItems/RemoveListItemsBy*/MoveListItem` | Playlist contents are modelled as list items with entry IDs, not linked children. Any code reading `LinkedChildren` needs rework. |
| events | `PlaylistItemsAdded/Removed/Moved` on `IPlaylistManager` | |
| `Playlist.OwnerUserId` | none on `Playlist` | See ownership below. |
| `Playlist.Shares` | `IHasShares.Shares : Share[]` (`string UserId`, `bool CanEdit`), but the real access model is `UserItemShare` (below) | `Playlist` itself was not listed as implementing `IHasShares` in the interface dump; check `BaseItem`. |

### Ownership and sharing

- Emby models access as `UserItemShare { long UserId, long ItemId, UserItemShareLevel? ShareLevel }`, stored via `IItemRepository.SaveUserItemShares / GetUserItemShares(UserItemShareQuery { long[] ItemIds }) / DeleteUserItemShares(itemId, maxShareLevel)`.
- `UserItemShareLevel` (in `MediaBrowser.Model.Dto`): `None, Read, Write, Manage, ManageDelete`.
- `BaseItem` carries `bool IsPublic` and `UserItemShareLevel? ShareLevel`, plus `CanManageAccess`, `CanLeaveSharedContent`, `CanMakePublic`, `CanMakePrivate`. `Playlist` adds `SupportsManageAccess`, `SupportsMakePublicOrPrivate`, `IsAuthorizedToDelete(User, level, folders)`. `ILibraryManager.MakePublic(BaseItem, User)` exists.
- There is no `OwnerUserId`. Inference (unverified): the creating user is the owner and holds the top share level (`ManageDelete`). Confirm by creating a playlist on a real server and reading `GetUserItemShares`.
- Implication: per-user playlist ownership and "shared with" semantics map to share rows, not a property. The user-facing page and `PlaylistUserResolver` need an Emby-specific implementation.

### Saving items

| Jellyfin | Emby |
|---|---|
| `BaseItem.UpdateToRepositoryAsync(reason, ct)` | `ILibraryManager.UpdateItem(BaseItem item, BaseItem parent, ItemUpdateType reason[, MetadataRefreshOptions])` and `UpdateItems(List<BaseItem>, parent, reason, ...)` (sync, not `Task`). Lower level: `IItemRepository.SaveItem(s)`. |
| `ILibraryManager.UpdateItemAsync` | the sync `UpdateItem` above |
| `ItemUpdateType` | `None, MetadataImport, ImageUpdate, MetadataDownload, MetadataEdit` (`MediaBrowser.Controller.Library`). Jellyfin's values are not identical. |

### Collections

`ICollectionManager`: `Task<BoxSet> CreateCollection(CollectionCreationOptions)`, `Task AddToCollection(long collectionId, long[] itemIds)`, `void RemoveFromCollection(BoxSet, long[] itemIds)`; events `CollectionCreated`, `ItemsAddedToCollection`, `ItemsRemovedFromCollection`. `CollectionCreationOptions`: `Name`, `long ParentId`, `bool IsLocked`, `ProviderIds`, `long[] ItemIdList`, `long[] UserIds`. Not compared member by member with Jellyfin's `ICollectionManager` (those were not in the compile errors, so the existing calls may already fit; re-check after the ID work).

### User and user data

- `IUserManager`: `User[] Users`, `GetUserById(Guid | long | string | ReadOnlySpan<char>)`, `GetUsers(UserQuery)`. `User.Name` replaces Jellyfin's `User.Username` (19 uses).
- `IUserDataManager`: `GetUserData(User, BaseItem)` (and `long`/`string` user ID overloads), `SaveUserData(User or long userId, BaseItem, UserItemData, UserDataSaveReason, ct)`, `GetAllUserData(long userId)`, event `UserDataSaved : EventHandler<UserDataSaveEventArgs>`.
- `UserDataSaveEventArgs` (Emby): `User User`, `UserDataSaveReason SaveReason`, `UserItemData UserData`, `BaseItem Item`, `BaseItem[] CollectionFolders`. It has **`User`, not `UserId`**: use `args.User` (4 uses).
- `UserDataSaveReason` (`MediaBrowser.Model.Entities`): `PlaybackStart, PlaybackProgress, PlaybackFinished, TogglePlayed, UpdateUserRating, Import, UpdateHideFromResume, ReportSearched`. **There is no `UpdateUserData`**; refresh triggers that match on it need re-mapping (likely `TogglePlayed` / `UpdateUserRating`, but favorites toggling is not obviously covered: needs a runtime check).
- `UserItemData` (Emby): `Key`, `Rating`, `PlaybackPositionTicks`, `PlayCount`, `IsFavorite`, `HideFromResume`, `LastPlayedDate : DateTimeOffset?`, `Played`, stream indexes, `RatingLastModified`, `PlaystateLastModified`, `DateLastSearched`. Property set looks compatible with what the orders read.

### Remaining lookups (resolved at signature level; behavior unverified)

**Query objects.** Emby's `InternalItemsQuery` uses `long` IDs and arrays:

| Jellyfin | Emby |
|---|---|
| `ParentId` (Guid) | `long[] ParentIds`, or `BaseItem Parent`. Also `AncestorIds`, `ListIds`, `SeriesIds`, `AlbumIds`, `ArtistIds`, `AlbumArtistIds`, `ItemIds`, `ExcludeItemIds` (all `long[]`). |
| `IncludeItemTypes : BaseItemKind[]` | `string[] IncludeItemTypes` (also `ExcludeItemTypes`, `MediaTypes`) |
| `Person` (name) | `long[] PersonIds` plus `PersonType[] PersonTypes`. Person is by ID, so a name must be resolved to an ID first (see People). |
| `OrderBy` | `(string, SortOrder)[] OrderBy` |
| `InternalPeopleQuery.ItemId` | `long[] ItemIds` (also `PersonTypes`, `MaxListOrder`, `EnableGroupByName`). Result is `List<PersonInfo>` via `IItemRepository.GetItemPeople`. |
| `PersonKind` | `MediaBrowser.Model.Entities.PersonType`: `Actor, Director, Writer, Producer, GuestStar, Composer, Conductor, Lyricist`. |

**Aggregate name dumps (prefilter pushdowns).**

| Jellyfin | Emby |
|---|---|
| `IItemRepository.GetGenreNames / GetMusicGenreNames / GetStudioNames` | `GetGenres / GetMusicGenres / GetStudios(InternalItemsQuery, ct)` returning `QueryResult<Tuple<BaseItem, ItemCounts>>`; the name is `Item1.Name`. `GetAllGenres` also exists. |
| `ILibraryManager.GetPeopleNames` | `ILibraryManager.GetPeople(InternalItemsQuery)` (`Tuple<BaseItem, ItemCounts>`) or `GetPeopleItems(query)` (`BaseItem`); name is `.Name`. |
| `ILibraryManager.GetMediaStreamLanguages(type)` | `ILibraryManager.GetStreamLanguages(InternalItemsQuery, MediaStreamType, ct)` → `QueryResult<string>` (also on `IItemRepository`). |
| `ILibraryManager.GetCount(query)` | `IItemRepository.GetCount(InternalItemsQuery, ct)` → `int`. `ILibraryManager` has no `GetCount`. |

Important: `ParentValuesPrefilterResolver`, `PeoplePrefilterResolver` and `StreamLanguagePrefilterResolver` are pushdown *optimizations*. `PrefilterContext.ItemRepository` is already documented as nullable ("dump-dependent pushdowns" are skipped when it is null). So the Emby adapter can start by supplying no repository and no pushdowns (correct, slower), then add the mappings above where measurements justify it.

**Items and metadata.**

| Jellyfin | Emby |
|---|---|
| `BaseItem.GetBaseItemKind()` | none. Candidates: `BaseItem.GetClientTypeName()` (string), or the CLR type name. Needs a runtime check that the strings match the `IncludeItemTypes` values (`"BoxSet"`, `"Playlist"`, `"Episode"`, ...). `LiveTvChannel` vs `TvChannel` handling in `MediaTypes.cs` will need review. |
| `BaseItem.UpdateToRepositoryAsync(reason, ct)` | `BaseItem.UpdateToRepository(ItemUpdateType, BaseItem parent[, MetadataRefreshOptions])` (sync). This is the closer match than `ILibraryManager.UpdateItem`, which also exists. |
| `BaseItem.ForcedSortName` | none on `BaseItem` (only on `BaseItemDto`). `SortName {get;set;}` exists, and `MetadataFields.SortName` can be locked. Whether setting `SortName` persists as a forced value is unverified. |
| `BaseItem.GetOwner()` (extras) | none found. `BaseItem` has `GetExtras(...)`, `GetExtraIds(...)`, `ExtraType` but no owner getter. Used by `AncestorValueResolver`; extras handling needs a separate investigation or can be skipped initially. |
| `BaseItem.GetParent()` | `GetParent()` and `Parent : Folder` exist. |
| `Audio.AlbumEntity` | none. `Audio` has `Album` (string, via `IHasMusicAlbum`), `AlbumArtists`, `AlbumArtistItems`. Resolving the album entity needs a query (`AlbumIds`) or by name. |
| `Episode.SeasonId` | none. `Episode.Season` / `Episode.SeasonFolder : Season` and `Episode.Series` exist; use `.Season?.InternalId`. |
| `MetadataProvider.Imdb/Tmdb/Tvdb` | `MediaBrowser.Model.Entities.MetadataProviders` (plural), has `Imdb, Tmdb, Tvdb`, also `TmdbCollection`. `GetProviderId`/`SetProviderId` exist as extensions in `ProviderIdsExtensions`. |
| `MetadataField.Genres/Studios/OfficialRating` | `MediaBrowser.Model.Entities.MetadataFields` (plural); all three exist. `LockedFields : MetadataFields[]`. |
| `string.RemoveDiacritics()` | exists as `MediaBrowser.Controller.Extensions.StringExtensions.RemoveDiacritics` (different namespace). Compatible once the `using` is adapted. |
| `ILocalizationManager.TryGetISO6392TFromB` | none. Use `FindLanguageInfo(string)` → `CultureDto` (`ThreeLetterISOLanguageName(s)`); the plugin's injectable mirror delegate in `StreamLanguagePrefilterResolver` makes this a small adapter. |
| `Jellyfin.Plugin.Instance` (CS0234) | not an Emby gap: a plugin-singleton naming issue inside the plugin itself (`Plugin.cs` was not in the compiled set). |

**Users.** `MediaBrowser.Controller.Entities.User : BaseItem`, so Emby users have both `Guid Id` and `long InternalId`, and `IUserManager.GetUserById` accepts `Guid | long | string`. User ID translation is therefore straightforward. Only `User.Name` (vs `Username`) differs, per the earlier note.

### Runtime verification (spike plugin on a live Emby 4.10.1.0)

Run with a throwaway plugin (`SmartListsEmbySpike`, net8.0, referencing only `MediaBrowser.Common/Controller/Model`) against a test server with 5 movies + a trailer extra, 150 bulk movies, 2 series (12 episodes), 12 tagged tracks, and two users (`kevin` = internal ID 1, `test1` = 2). Findings:

| Question | Result |
|---|---|
| Plugin loads / entry point runs | Yes. `IServerEntryPoint` ran; runtime is .NET 8.0.31. Constructor injection works for both `IServerEntryPoint` and `IService` classes (`ILogManager`, `IUserDataManager`, `ILibraryManager`, `IUserManager`, `IPlaylistManager`, `IItemRepository` all resolved). |
| Endpoint registration | `IService` + `[Route]` request class works; unauthenticated calls return 401, unknown routes 404, calls with `X-Emby-Token` succeed. |
| Loaded assembly versions | `System.Text.Json` 8.0.0.0, `System.Net.Http` 8.0.0.0, `Microsoft.Extensions.Logging.Abstractions` 8.0.0.0 (Emby's own copies). `SixLabors.ImageSharp` not loaded: the real plugin must bundle it; conflict risk not yet tested. |
| `GetBaseItemKind()` replacement | **`GetClientTypeName()` returns exactly the `IncludeItemTypes` strings** for Movie, Series, Season, Episode, Audio, MusicAlbum, MusicArtist, Folder, CollectionFolder, Trailer. `BoxSet`/`Playlist` not observed (none existed yet; expected the same). `LiveTvChannel`/`TvChannel` untested. |
| Extras owner | `ExtraType` is set (e.g. `Trailer`, CLR type `Trailer`); the extra's **`ParentId` is the owner's `InternalId`**. `Parent` is null. The extra is **not** returned by a Recursive `Movie` query. `movie.GetExtras(ExtraType[])` returns it. Replacement for `GetOwner()`: `lib.GetItemById(extra.ParentId)`. |
| Playlist ownership | Creating a playlist as `kevin` produced exactly one share row: `UserId = kevin.InternalId`, level **`ManageDelete`**. `BaseItem.ShareLevel` is null (it is per user, not stored on the item). `CanManageAccess(user, ManageDelete)` is true for the creator and false for the other user. So "owner" = the user holding the `ManageDelete` share. |
| Playlist storage | Playlists are `.m3u` files under `programdata\data\userplaylists\<name> [playlist]\`. |
| Remove-all-then-add | **Fast**: 150 items: create 18 ms, remove all 8 ms, add all 7 ms; replace with 75 items 14 ms. Order of the added IDs is preserved exactly. Entry IDs come from `ListItemEntryId` on `playlist.GetChildren(new InternalItemsQuery { User = ... })`. Decision stands. Not measured: events fired during the sequence, or behavior with many thousands of items. |
| Forced sort title | `item.SetSortNameDirect(x)` + `item.UpdateToRepository(ItemUpdateType.MetadataEdit, parent)` persists, and the REST DTO then reports `ForcedSortName = x`. **A library scan, a `Default` refresh and a `ValidationOnly` refresh keep it; a `FullRefresh` (even with `ReplaceAllMetadata=false`) overwrites it with the plain name.** Also adding `MetadataFields.SortName` to `item.LockedFields` before saving **protects it** from `FullRefresh` and from `ReplaceAllMetadata=true`. So the Emby port must lock `SortName` when it sets a custom title (the Jellyfin code already checks `LockedFields` for genres/studios/rating, so the pattern is familiar). `ResetSortName` is not public: to undo, set the plain name and remove the lock. (Refresh endpoint note: `ImageRefreshMode=None` returns HTTP 400; use `Default`/`ValidationOnly`/`FullRefresh`.) |
| Favorite / played events | `UserDataSaved` fires with **`UpdateUserRating`** for favorite toggles (`IsFavorite` in `UserData`) and **`TogglePlayed`** for played toggles. Jellyfin's `UpdateUserData` reason does not exist; map both of these. `args.User.Name` is populated. |
| User IDs | `User` has `InternalId` (long) and `Id` (Guid); `kevin`=1, `test1`=2. |

### Still open after this pass

- Events fired by remove-all-then-add (`PlaylistItemsRemoved`/`Added`), and playlist behavior at very large sizes.
- `BoxSet` / `Playlist` / `LiveTvChannel` type-name strings (no such items existed in the test run).
- `SixLabors.ImageSharp` bundling and any assembly conflicts for the full plugin.
- Whether `GetPeople`/`GetStudios` return the same set the Jellyfin name dumps did (matters only if pushdowns are added).

## Emby-specific risks (unverified)

- Emby is net8.0, the Jellyfin plugin is net10.0: shared source must avoid net10-only APIs.
- Emby loads its own assemblies into the plugin environment: check `System.Text.Json`, `HttpClient`, `Microsoft.Extensions.Logging` and SixLabors.ImageSharp for version conflicts once a skeleton loads.
- Emby's HTTP API layer is not ASP.NET MVC. How plugin endpoints are declared has not been checked.
- Plugin page registration (`IHasWebPages`/`PluginPageInfo`) exists by name; the JS `ApiClient` differences have not been checked.

## Next

1. Verify the open runtime questions above against a running Emby server (type-name strings, owner/extras, forced sort title, playlist ownership via shares).
2. Design the ID and item-kind compatibility types for the shared layer.
3. Define the port list (user directory, user data, list writer, refresh triggers, plugin/API shell).

### Added during phase 3 (verified by constructing the classes offline and by test)

- `GetClientTypeName()` returns `BoxSet`, `Playlist`, `Movie`, `Series`, `Season`, `Episode`, `Audio`, `MusicAlbum`, `MusicArtist`, `Folder` for the corresponding classes.
- `BaseItem.MediaType` is a nullable **string** (null for containers such as BoxSet, Series, Season, MusicAlbum).
- `BaseItem.PremiereDate`, `DateCreated`, `UserItemData.LastPlayedDate` are `DateTimeOffset`; `BaseItem.SortName`'s setter calls `StringExtensions.RemoveDiacritics`, which goes through the static `BaseItem.LocalizationManager` (it throws a NullReferenceException if that is unset).
- `BaseItem.Genres`/`Studios`/`Tags`/`Artists`/`Album` load lazily from the library (`EnsureTaggedItemsLoaded` -> `GetItemLinks`, then `IItemRepository.OnItemLinksFilled`). `MarkTaggedItemsLoaded()` marks them loaded. Performance implication on large libraries: unmeasured.
- `BaseItem.UpdateRatingToItems(BaseItem[])` assigns nothing on a locked item (`IsLocked = true`); it ranks ratings with `ILocalizationManager.GetRatingLevel(string)`.
- `IUserManager.Users` is obsolete; use `GetUserList(new UserQuery())` (all users when unfiltered). `ILibraryManager.GetCollectionFolders(BaseItem)` returns `Folder[]`; `GetItemList` returns `BaseItem[]`.

## Phase 4 runtime results (real plugin services on Emby 4.10.1.0, via temporary debug endpoint)
- Plugin loads; `IServerEntryPoint` constructor injection of ILogManager/ILibraryManager/IUserManager/IPlaylistManager/ICollectionManager/IUserDataManager/IProviderManager/IFileSystem/IServerApplicationPaths/IItemRepository all resolve. Plugin log lines appear in `embyserver.txt` as `SmartLists: <Class>: ...`.
- Playlist create (155 Movies) and collection create (155 items) work; membership read back through REST matches (collection membership via `CollectionIds` verified indirectly: the service diff reports match REST children counts 10 -> 1 -> 0).
- Playlist update in place (remove-all-then-add): counts 0 -> 9 -> 1 -> 10 correct, same playlist id.
- Collection update via add/remove diff: 10 -> 1 -> 0 correct, same id.
- `Public` flip true/false persists: visible to the second user when public, hidden again when private. Private playlist is not visible to non-owner.
- Without a stored id the service creates a duplicate playlist each call (expected: lookup is by `JellyfinPlaylistId`/store; the store is not used by the debug endpoint).
- Observed: newly created collection is named without the "[Smart]" suffix, then renamed with it on the first update (cosmetic; fix when real create path is wired).
- Not yet verified: sort title persistence, per-user (AllUsers) playlists, ImageSharp image path, People prefilter.

## Phase 4 close-out results (Emby 4.10.1.0)
- **Sort title**: persists on playlists and collections (`SetSortNameDirect` + `SortName` lock). On *create*, Emby's own queued post-create refresh can revert it despite the lock, so both services re-assert it up to 3 times at 2 s intervals after creation (`ReassertSortTitleAsync`). Update paths are reliable.
- **AllUsers playlists**: going through `RefreshQueueService` (store -> queue -> per-user fan-out) creates one private playlist per user, each owned by that user, with the sort title applied.
- **Custom images**: playlist and collection Primary images are applied and served (verified by fetching `/Items/{id}/Images/Primary` and reading the pixels). Needed two fixes: the ImageSharp resolver now falls back to `IApplicationPaths.PluginsPath` (Emby loads plugins with an empty `Assembly.Location`), and BoxSets have no `ContainingFolderPath`, so images go to `GetInternalMetadataPath()` (`EmbyLibraryExtensions.GetItemImageFolder`).
- **People rules** (NFO-fed test data on "Test Movie One"): Actors Contains / Equal, Directors Equal, People Contains and Actors NotContains all return the right counts (1, 1, 1, 1, 154 of 155). The People *prefilter* stays disabled; evaluation is per item, correct but not accelerated.
- **Collection DisplayOrder**: `BoxSet.DisplayOrder` is a `CollectionDisplayOrder` enum with only PremiereDate/SortName; Jellyfin's "Default = order added" does not exist in Emby, so the reflective setter was removed.
- **Create-name note corrected**: the "[Smart]" suffix is applied at create; only the success message printed the unformatted name.
- Observed, not fixed: Emby's NFO saver sometimes throws an IOException on `collection.nfo` right after a collection is created (its own queued refresh racing the plugin's save). Non-fatal, logged by Emby.
