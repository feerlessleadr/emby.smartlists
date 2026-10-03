# Emby port: Phase 1 design

> **Superseded** by [plan.md](plan.md) (Jellyfin support dropped; straight port). Kept for the analysis it contains.

Status: proposal, not implemented. Builds on [api-notes.md](api-notes.md) (Emby 4.10.1.0, net8.0). Supersedes the handoff doc's "Core = netstandard2.0" idea and the earlier "ID compatibility type" idea; both were revised after the evidence below.

## 1. Constraints

- Zero behavior change for Jellyfin. Keep its assembly name, root namespace, plugin GUID and file layout. The 1,764 tests and the CI build must stay green after every step.
- Emby 4.10.1.0 runs plugins on **net8.0**; the Jellyfin plugin is net10.0. Shared source must compile under net8.0 / C# `latest`. (The compile spike already did: the only errors were the platform gaps listed in the notes.)
- Warnings-as-errors and `AnalysisMode=Recommended` stay on for the Jellyfin project. The Emby project should use the same settings once it compiles.

## 2. What the evidence says

1. The seam is **not** a pure-data layer. About half of the source files (72 of 137 in the Jellyfin project) import platform namespaces, including nearly all of `Orders`, the prefilters, `Factory` and `SmartList`, which work directly on `BaseItem` / `User`. Emby has `BaseItem`, `Folder`, `Movie`, `Episode`, `Series`, `Season`, `Audio`, `User`, `UserItemData`, `InternalItemsQuery`, `ILibraryManager`, `IUserManager`, `IUserDataManager`, `IPlaylistManager`, `ICollectionManager`, all with the same names. So **compiling the same source against each platform's own types is feasible** (35 of ~127 files had errors; 278 errors in total, concentrated in 6 files).
2. **Item identity is not a problem for shared code.** Emby's `BaseItem` has a `Guid Id` (like Jellyfin) *and* a `long InternalId`. Only boundary calls (queries by ID, playlist/collection add/remove, user data by ID, shares) need the `long`. So shared code keeps using `Guid`; a small Guid→InternalId resolver at those boundaries handles the rest. This leaves roughly 500 `Guid` usages untouched.
3. `ISmartListService<TDto>` (`Services/Abstractions`) already separates "refresh/delete/disable a list" from the filtering logic. Emby can implement its own playlist and collection services against it, so the two Jellyfin services (the two biggest error sources: 107 of the 278 errors) never have to compile for Emby.
4. Emby has no DI registrator and no hosted services. A plugin starts from `IServerEntryPoint` and builds its own object graph. The repo already constructs `RefreshQueueService` by hand, so that pattern is familiar.
5. Emby's `ILibraryManager` has the same `ItemAdded / ItemUpdated / ItemRemoved` events; `IUserDataManager.UserDataSaved` exists with a different args shape; `IScheduledTask.Execute(CancellationToken, IProgress<double>)` differs from Jellyfin's.
6. Runtime-verified: constructor injection works for entry points and services; `GetClientTypeName()` returns the `IncludeItemTypes` strings; playlist remove-all-then-add is fast and order-preserving; forced sort titles need `MetadataFields.SortName` locked to survive a `FullRefresh`; the creating user holds a `ManageDelete` share.

## 3. Structure

```
Jellyfin.Plugin.SmartLists/          unchanged location, assembly, namespaces
  Core/ Utilities/ Services/...      shared source (compiled by both projects)
  Platform/                          NEW: Jellyfin implementation of the compat surface (section 4)
Emby.Plugin.SmartLists/              NEW, net8.0, references Emby's MediaBrowser.* DLLs (compile-only)
  Emby.Plugin.SmartLists.csproj      <Compile Include="..\Jellyfin.Plugin.SmartLists\Core\**" /> etc., with explicit Excludes
  Platform/                          Emby implementation of the same compat surface + shim types
  Services/                          EmbyPlaylistService, EmbyCollectionService, auto-refresh entry point, tasks
  Plugin.cs, Api/                    Emby plugin class, IService endpoints (later phases)
Jellyfin.Plugin.SmartLists.Tests/    unchanged for now (see section 7)
```

Why linked source and not a new shared assembly: nothing has to move in the Jellyfin project, so there is no Jellyfin regression surface from file moves, and the shared files can keep compiling against each platform's real `BaseItem`. The cost is that the Emby project must list its excludes explicitly (the Jellyfin-only files: `Api/`, `Plugin.cs`, `ServiceRegistrator.cs`, the two services, hosted services, tasks, `PluginPagesRegistrationService`).

If the exclude list becomes painful later, the linked tree can be moved to a physical shared folder in one mechanical commit. That is a refactor, not a prerequisite.

## 4. The compat surface

Shared code calls a small, named API. Each platform provides the implementation in its own `Platform/` folder, in the same namespace (`Jellyfin.Plugin.SmartLists.Platform`). Where possible it is an **extension method**, so call sites change from `x.Foo` to `x.GetFoo()` and nothing else.

| Need | Shared call (new) | Jellyfin impl | Emby impl |
|---|---|---|---|
| user name | `user.GetUserName()` | `user.Username` | `user.Name` |
| item kind | `item.GetBaseItemKind()` (existing call, keeps working) | Jellyfin's own method | shim: map `GetClientTypeName()` to the shim enum |
| kind enum | `BaseItemKind` | `Jellyfin.Data.Enums.BaseItemKind` | **shim enum of the same name/values in the Emby assembly** |
| `User` type | `User` | `Jellyfin.Database.Implementations.Entities.User` | global alias to `MediaBrowser.Controller.Entities.User` |
| save item | `await item.SaveAsync(reason, ct)` (replaces `UpdateToRepositoryAsync`, ~21 sites) | `UpdateToRepositoryAsync` | `UpdateToRepository(ItemUpdateType.MetadataEdit, parent)` |
| extras owner | `item.GetOwnerItem(libraryManager)` (2 sites) | `GetOwner()` | `GetItemById(ParentId)` when `ExtraType != null` |
| provider ids / locked fields | `MetadataProvider`, `MetadataField` | same names | shim aliases to `MetadataProviders`, `MetadataFields` |
| build item queries | `ItemQueries.*` helpers (parent, type filter, person, ids; ~25 sites) | object initializer on Jellyfin's query | `ParentIds`, `string[] IncludeItemTypes`, `PersonIds`, `long[]` conversion |
| ID boundary | `IdResolver.ToInternal(Guid[])` / `FromInternal(long[])` | identity (no-op) | Guid ⇄ `InternalId` map, filled from items seen, falls back to `GetItemById` |
| cleaning strings | `string.RemoveDiacritics()` | `Jellyfin.Extensions` | `MediaBrowser.Controller.Extensions` |
| language codes | injectable delegate (already exists) | `TryGetISO6392TFromB` | `FindLanguageInfo(...).ThreeLetterISOLanguageNames` |
| name dumps / counts / stream languages (prefilters) | `PrefilterContext` members, nullable (already supported) | current calls | **initially return null, so pushdowns are skipped (correct, slower)**; add mappings later if measured |

Two principles:

- **Shim types live in the Emby assembly only**, never in Jellyfin's. Putting `Jellyfin.Data.Enums.BaseItemKind` into the Emby assembly is a naming wart but means ~70 shared files compile unchanged. If the wart is unacceptable we can rename to a neutral namespace and change those usings in the Jellyfin project; that is purely mechanical.
- **Compat helpers go through compiler errors, not guesses.** The spike's error list is the work list: a call site is changed only if the Emby build fails on it.

## 5. Ports that are real behavior differences

These are not renames; each needs a genuine Emby implementation. All of them implement something that already exists as an interface or as a constructor parameter in the shared code.

| Port | Existing seam | Jellyfin | Emby |
|---|---|---|---|
| Playlist refresh | `ISmartListService<SmartPlaylistDto>` | existing `PlaylistService` | new `EmbyPlaylistService`: `CreatePlaylist` (User, IsPublic, `long[]` ids), then refresh = **remove-all then add** via `IPlaylistManager` (decided and measured); owner = user with a `ManageDelete` share; find a user's playlist through shares; delete via `ILibraryManager.DeleteItem` |
| Collection refresh | `ISmartListService<SmartCollectionDto>` | existing `CollectionService` | new `EmbyCollectionService` using `ICollectionManager` (create/add/remove take `long` ids) |
| Auto refresh triggers | `AutoRefreshService` (shared) + `AutoRefreshHostedService` (Jellyfin shell) | `IHostedService` | `IServerEntryPoint` shell wiring `ItemAdded/Updated/Removed` and `UserDataSaved`; map `UpdateUserRating` (favorite) and `TogglePlayed` to the shared "user data changed" notion |
| Scheduled tasks | `CleanupTask`, `BackupTask` | Jellyfin `IScheduledTask` | Emby `IScheduledTask.Execute(ct, progress)` thin shells over shared logic |
| Logging | `ILogger<T>` everywhere | built in | small adapter implementing `Microsoft.Extensions.Logging.ILogger` over Emby's `ILogger` (Emby already loads `Microsoft.Extensions.Logging.Abstractions` 8.0.0.0) |
| Composition | `ServiceRegistrator` | MS DI | hand-built graph in the Emby entry point (same style as the manual `new RefreshQueueService(...)`) |
| Sort title | `MetadataHelper` | `ForcedSortName` | `SetSortNameDirect` + lock `MetadataFields.SortName` |
| Config + API | MVC controllers, `Configuration/*.js` | as is | Phases 3 and 6; not part of Phase 1 |

One required change in shared code: `RefreshQueueService` constructs `PlaylistService` / `CollectionService` with `new`. It must receive them (or a factory) from the composition root instead, since the Emby build excludes those classes. This is the single structural change to a Jellyfin file; everything else is call-site renames.

## 6. Phase 1 steps, in order

Each step ends with build + tests green; steps 1 to 3 touch Jellyfin and must be reviewed as such.

1. **Add the compat surface on the Jellyfin side** (`Platform/`), with the extension methods above as trivial pass-throughs, and change the matching call sites. No behavior change; `dotnet build` (warnings as errors) and the 1,764 tests must pass. Manual Jellyfin verification (create/refresh a playlist and a collection) is still required, as the tests do not cover runtime behavior.
2. **Thread the services through `RefreshQueueService`** (constructor/factory parameter instead of `new`). Same verification.
3. **Create `Emby.Plugin.SmartLists`** with the linked-source globs and explicit excludes, plus shim types and the compat implementations. Goal: **zero compile errors** for the shared subset (use the spike's error list as the checklist).
4. **Emby skeleton on top of that**: plugin class, entry point composing the shared services, logging adapter, and a no-op `ISmartListService` pair. It should load in the test Emby and log the shared engine evaluating a hard-coded rule against the generated test library (proves the engine, `Factory`, orders and prefilters run on Emby item types).
5. Only then Phase 2 of the handoff doc: `EmbyPlaylistService` / `EmbyCollectionService`, triggers, tasks.

## 7. Tests

The test project is net10.0 and builds real Jellyfin `BaseItem`s (`Support/TestItems.cs`), so it cannot run against Emby items. Plan:

- Keep it as-is for the Jellyfin side (it protects steps 1 and 2).
- For Emby, rely on runtime verification against the test server (documented in the notes) plus a small number of contract tests that hit the test server's endpoints. Revisit a multi-target or second test project after step 4, when we know how much of `TestItems` can be abstracted.

## 8. Risks and open decisions

- **Decision A (shim namespace):** accept Jellyfin-named shim types (`Jellyfin.Data.Enums.BaseItemKind`, etc.) in the Emby assembly, or rename to a neutral namespace and touch ~70 `using` lines in the Jellyfin project. Recommendation: accept the shim for now.
- **Decision B (location):** same repo and solution vs a fork. This is the handoff doc's decision 3 (upstream vs fork). The design works either way, but upstream acceptance should be discussed before step 1, since it edits the Jellyfin project.
- **Risk: 35 files, 278 errors is a lower bound.** The compiler stops at declaration errors before reporting method-body errors in some files, so the real count after the first fix pass will be higher. The estimate is that it converges on the same six files; verify early by doing step 3 on a branch before step 1 lands.
- **Risk: net8.0 vs net10.0.** The shared files compile on net8.0 today, but any future use of net10-only APIs in shared code will break Emby. Add the Emby build to CI as soon as step 3 compiles.
- **Risk: SixLabors.ImageSharp** (custom list images) and `IHttpClientFactory` (external lists) have not been tried on Emby. Both are outside the MVP; exclude `SmartListImageService` and the external-list providers from the first Emby build.
- **Unverified at runtime:** playlist event behavior and very large playlists, the `BoxSet`/`Playlist`/`LiveTvChannel` type strings, and whether prefilter name dumps are worth mapping.

## 9. Step 3 result (measured, on branch `emby/step3-shared-compile`)

`Emby.Plugin.SmartLists` now exists (net8.0, linked shared source, `Platform/Shims.cs`, `Platform/EmbyCompat.cs`, a minimal `Plugin.cs`, and a temporary `Platform/MeasurementStubs.cs` that stands in for the excluded services). Build it with:

```powershell
dotnet build Emby.Plugin.SmartLists/Emby.Plugin.SmartLists.csproj -p:EmbySystemDir="C:\path\to\embyserver\system"
```

Status: **does not compile yet: 127 errors in 21 shared files.** The Emby project's own files compile cleanly. The Jellyfin project and its tests were not touched.

How the error count moved: 278 (spike, including the two big services) → 159 (services excluded, shims added) → 127 (compat extensions and the `Plugin` class). Nothing in the shared source changed to get there.

What the compat layer already absorbed without editing shared files: `GetBaseItemKind()`, `UpdateToRepositoryAsync()`, `ILibraryManager.GetCount/GetPeopleNames/GetMediaStreamLanguages`, `IItemRepository.Get*Names`, `TryGetISO6392TFromB`, `Plugin.Instance`, the `User` / `SortOrder` / `MetadataProvider` / `MetadataField` aliases, and the `BaseItemKind` enum.

What is left needs call-site edits in the shared (Jellyfin-project) files, because extension methods cannot replace properties, object-initializer members or types. These are the step 1 work list:

| Group | Sites | Fix |
|---|---|---|
| A. `User.Username` | 12 | `user.GetUserName()` (Jellyfin: `Username`, Emby: `Name`) |
| B. Query initializers: `ParentId`, `Person`, `InternalPeopleQuery.ItemId` and its constructor | 13 | `ItemQueries.*` helper |
| C. `BaseItemKind[]` where Emby wants `string[]` (`IncludeItemTypes`, etc.) | 17 | same helper (kind to type name) |
| D. `long` vs `Guid` item IDs (`ParentId`, `SeriesId`, ID arrays) | 28 | `IdResolver` at the boundary, per the design |
| E. `DateTimeOffset` vs `DateTime` (e.g. `DateCreated`) | 9 | small conversion helper |
| F. `UserDataSaveEventArgs.UserId` / `UserDataSaveReason.UpdateUserData` | 7 | normalize event args in the Emby trigger shell (maps `UpdateUserRating`, `TogglePlayed`) |
| H. One-offs: `ForcedSortName` (2), `GetOwner` (1), `Audio.AlbumEntity` (1), `Episode.SeasonId` (1), `LinkedChild.ItemId` (1), `IsPlayed(user, data)` overload (3), `GetExtras()` overload (1) | 10 | per-site helpers; `ForcedSortName` also needs the `SortName` lock |
| I. Cascades and small mismatches | 28 | mostly six `BaseItem[]` values used with `.Count` (arrays have `Length`); the logger-overload and definite-assignment errors disappear once those are fixed. Also `List<string>` to `string[]` and a `PersonType` string. |
| G. Excluded-service members | 3 | go away with step 2 (inject the services) |

Revised estimate: roughly 100 call-site edits across 21 files, concentrated in `Factory.cs` (36), `RefreshQueueService.cs` (15), `AutoRefreshService.cs` (10), `LibraryManagerHelper.cs` (8), `SmartList.cs` (7) and `ParentValuesPrefilterResolver.cs` (7). The Orders, Engine, Operand, FieldRegistry and DTO files, and most of the Utilities and external-list code, already compile for Emby with no edits.

Notes from the build itself:

- Emby ships its own ASP.NET Core 8.0, `Microsoft.Extensions.*` (Logging, DI, Http, Hosting) assemblies, so the shared code's `using` lines bind at runtime without bundling. `SixLabors.ImageSharp` is not shipped, which is why `SmartListImageService`, `CollageBuilder` and `CoverBadgeHelper` stay excluded.
- Arrays vs lists matters: Emby's `GetItemList` returns `BaseItem[]` where Jellyfin returns `IReadOnlyList<BaseItem>`. Fixing the `.Count` sites in the shared source (for example with `Count()` or by materializing to a list) is a Jellyfin-side edit that the Jellyfin analyzers may flag (CA1829), so check it with the warnings-as-errors build.
- `EMBY_SYSTEM_DIR` / `-p:EmbySystemDir` points the build at an Emby install. CI will need either an Emby install or the `MediaBrowser.Server.Core` NuGet package (latest stable on nuget.org is 4.9.1.90; the 4.10.x packages are betas only), so decide that before step 7.
