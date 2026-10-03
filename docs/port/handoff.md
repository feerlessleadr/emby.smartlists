# Handoff: Add Emby support to jellyfin-smartlists-plugin

Repo: https://github.com/jyourstone/jellyfin-smartlists-plugin (AGPL-3.0)
Goal: one codebase that ships both a Jellyfin plugin and an Emby plugin, with **zero behavior change for Jellyfin**.

> Status of this document: a plan written from the README and `CLAUDE.md` only. Items marked **VERIFY** are inferences that were not confirmed against source. Read the code before trusting them.

---

## 1. Ground rules

- **No Jellyfin regressions.** Keep the existing assembly name, root namespace, and plugin GUID of the Jellyfin project unchanged. Changing them orphans existing installs, saved list data, and manifest entries.
- **Work in phases (section 5), one commit (or PR-sized chunk) per phase.** After every phase: `dotnet build` and `dotnet test` must pass.
- **Do not weaken the build.** The repo uses warnings-as-errors with `AnalysisMode=Recommended` (CA rules fail the build). Fix warnings rather than suppressing them. The test project is deliberately relaxed.
- **Ask before deciding** anything listed in section 7 (open decisions).
- Follow the repo's own `CLAUDE.md` conventions. The ones that matter most here:
  - Use constants such as `MediaTypes.Episode` instead of string literals.
  - Thread safety: list processing is sequential via `SemaphoreSlim(1,1)` in `RefreshQueueService`. Shared caches use concurrent collections.
  - `RefreshQueueService` constructs `PlaylistService`/`CollectionService` with `new`, not DI. New constructor dependencies must be threaded through manually. This will matter when introducing ports.
  - Config UI JS: no ES6 template literals (use string concatenation), never `is="emby-input"` (use `class="emby-input"`), use `showNotification()` rather than `Dashboard.alert()`.
  - New JS files must be registered in **both** the `.csproj` (`<EmbeddedResource>`) and `Plugin.cs` (`GetPages()`).
  - UI changes must update both `config.html` and `user-playlists.html`. The JS modules are shared.
  - Update the mkdocs content under `/docs/content/` for user-facing changes.

## 2. What the repo looks like today

Single plugin project targeting `net10.0` (Jellyfin 12; `main` ships forward-only). Structure per `CLAUDE.md`:

```
Jellyfin.Plugin.SmartLists/
├── Core/            Constants, Enums, Models (DTOs), Orders (25+ sorts), QueryEngine, SmartList.cs
├── Api/Controllers/ SmartListController, UserSmartListController
├── Services/        Abstractions (ISmartListService, ISmartListStore), Playlists, Collections,
│                    ExternalList (MDBList/IMDb/Trakt/TMDB/...), Users, Shared (AutoRefreshService, RefreshQueueService)
├── Configuration/   config.html, user-playlists.html, shared config-*.js
└── Utilities/       DtoMapper, InputValidator, LibraryManagerHelper, ...
Jellyfin.Plugin.SmartLists.Tests/   xunit, ~1,640 tests
```

Key facts:

- The tests cover the **pure C# surface**: query engine (operators, prefilter resolvers, `FieldRegistry`), sort implementations in `Core/Orders/`, external-list providers, and `Utilities/`. `CLAUDE.md` states these files have no Jellyfin dependencies. **This test suite is the safety net for the extraction.**
- Not covered by tests: anything that touches Jellyfin at runtime (playlist/collection creation, refresh scheduling, config UI). Those need manual verification against a running server.
- Rule fields: `FieldRegistry.cs` is the source of truth; `Operand.cs` holds the property; `Factory.cs` holds extraction logic. **VERIFY:** `Factory.cs` probably reads Jellyfin item types (`BaseItem` and friends) to build `Operand`s. If so, that is the main seam to cut.
- Two-phase filtering in `SmartList.cs`: cheap rules first, expensive fields (People, AudioLanguages, Collections, ...) extracted only for survivors. Preserve this.
- Sorting early-return paths in `FilterPlaylistItems()` must still call `ApplyMultipleOrders()`. Preserve this.
- CI: `.github/workflows/ci.yml` builds and tests on PRs and pushes. Releases: `.github/workflows/release.yml`, with `TARGETS` hardcoded to a single Jellyfin ABI. Tags `v*`; four-part version where Revision > 0 means RC.

## 3. Target architecture

```
SmartLists.sln
├── SmartLists.Core/                 netstandard2.0 (+ Polyfill package for modern C# features)
│   ├── Constants/ Enums/ Models/ Orders/ QueryEngine/   (moved from Core/)
│   ├── ExternalList/  Utilities/ (the pure parts)
│   ├── Refresh/       queue + auto-refresh orchestration (behind ports)
│   ├── Handlers/      API logic as plain DTO-in/DTO-out handlers (no MVC types)
│   └── Abstractions/  ports (section 4)
├── Jellyfin.Plugin.SmartLists/      net10.0. Adapter: Plugin.cs, MVC controllers (thin), port impls, config pages
├── Emby.Plugin.SmartLists/          Emby adapter (TFM: see section 7, decision 1)
└── SmartLists.Core.Tests/           the existing test project, retargeted at Core
```

Principle: **Core knows nothing about Jellyfin or Emby.** Adapters translate platform types to Core types and back.

## 4. Proposed ports (illustrative; refine against the real code)

```csharp
// What the engine needs from the library, as plain data
public interface IMediaItemSource {
    IReadOnlyList<ItemRef> GetItems(UserRef user, MediaTypeFilter filter);
    // Replaces the BaseItem-dependent part of Factory.cs. Only extract fields the rules/sorts need
    // (supports the two-phase filtering: cheap fields first, expensive fields on demand).
    Operand ToOperand(ItemRef item, IReadOnlySet<string> neededFields);
}

public interface IListWriter {                        // playlists and collections
    Task<ListRef?> FindAsync(string name, UserRef? owner, ListKind kind);
    Task UpsertAsync(ListRef list, IReadOnlyList<ItemRef> ordered, CancellationToken ct);
    Task DeleteAsync(ListRef list, CancellationToken ct);
}

public interface IUserDirectory   { UserRef? Resolve(string idOrName); IReadOnlyList<UserRef> All(); }
public interface IUserDataReader  { ItemUserData Get(UserRef user, ItemRef item); } // played, favorite, play count, last played
public interface IRefreshTriggers { event Func<RefreshReason, Task> Raised; }        // library updated, user data saved
public interface IScheduledRefresh{ void Register(Func<CancellationToken, Task> run); }
```

- `ISmartListStore` already exists and should move to Core mostly as-is. Both platforms provide an application-paths abstraction for the data folder.
- Logging: use `Microsoft.Extensions.Logging.Abstractions` in Core; bridge to Emby's logger in the Emby adapter.
- JSON/HTTP: external-list providers use `HttpClient` and `System.Text.Json` today. Emby loads its own assemblies into the plugin environment, so check for version conflicts early (section 6).

## 5. Phases

Each phase ends with: build green, tests green, short summary of what moved and what was left behind.

0. **Baseline.** Clone, `dotnet build`, `dotnet test`. Record the test count (about 1,640 expected). Read `CLAUDE.md`, `Plugin.cs`, `Factory.cs`, `Operand.cs`, `SmartList.cs`, `RefreshQueueService`, `PlaylistService`, `CollectionService`. Produce a short inventory: which files in `Core/`, `Services/`, `Utilities/` reference `MediaBrowser.*` or `Jellyfin.*` types. **That inventory decides the real seam; adjust the plan to it.**
1. **Extract `SmartLists.Core`** with zero behavior change: move the Jellyfin-free code (start with what the tests cover). Retarget tests. Jellyfin project references Core. Expect `netstandard2.0` friction (missing APIs, `init`/records need polyfills, nullable attributes); use a polyfill package rather than rewriting logic.
2. **Introduce ports; cut the Jellyfin seam.** Define the interfaces in Core, implement them in the Jellyfin adapter, move orchestration (refresh queue, filter flow) into Core. Thread new dependencies through `RefreshQueueService` manually (it uses `new`). Verify manually against a local Jellyfin (`dev/build-local.sh`, `http://localhost:8096`; see the repo's `/verify` skill).
3. **Handlers.** Move controller logic into plain handlers in Core. Jellyfin controllers become thin shells. Emby's API layer is not MVC, so each platform gets its own thin endpoint shell.
4. **Emby skeleton.** New project referencing Core. The plugin loads in a real Emby server, shows an empty config page, and logs a startup line. No features yet. Establishes the build, deploy, and load loop.
5. **Emby ports, read-only first.** Implement `IMediaItemSource`, `IUserDirectory`, `IUserDataReader` so rules can be evaluated and previewed. Then the writers (`IListWriter`), triggers, and scheduler.
6. **UI shim.** Shared `config-*.js` modules need a platform shim (page registration, `ApiClient` differences). Respect the JS rules in section 1.
7. **CI/release.** Second build artifact for Emby; manifest/distribution handled separately for Emby (see decision 5). Update docs.

MVP for the Emby adapter: rules, playlists, collections, manual and scheduled refresh. Defer the end-user page (depends on Jellyfin-only plugins), external lists, and other extras until the core path works.

## 6. Getting the Emby API reference

There is no ready-made decompiled dump. Generate one:

1. **NuGet reference package (first stop).** `MediaBrowser.Server.Core` (and its dependency `MediaBrowser.Common`) is Emby's official plugin-reference package; latest seen is `4.9.1.90`. Add it to the Emby project, or download the `.nupkg` and inspect the DLLs. Emby's old plugin guide says plugins should reference only this package plus framework defaults, so loading issues may result from referencing anything else.
   - Guide (old, but still the canonical overview): https://github.com/MediaBrowser/Emby/wiki/How-to-build-a-Server-Plugin
   - Package: https://www.nuget.org/packages/mediabrowser.server.core
2. **The installed server's own DLLs.** The NuGet version can lag the server you actually target. Treat the installed server as the truth. Look in the server install's `system` folder for `MediaBrowser.Controller.dll`, `MediaBrowser.Model.dll`, `MediaBrowser.Common.dll`, and `Emby.Server.Implementations.dll`. **VERIFY** exact paths for the platform in use.
3. **Decompile with ILSpy** (CLI works well for an agent):
   ```bash
   dotnet tool install -g ilspycmd
   ilspycmd -p -o ./emby-decompiled /path/to/MediaBrowser.Controller.dll
   ```
   Then grep the output. Likely counterparts to look for (**VERIFY**: names and signatures may differ from Jellyfin's): `ILibraryManager`, `IPlaylistManager`, `ICollectionManager`, `IUserManager`, `IUserDataManager`, `IScheduledTask`, `IApplicationPaths`, and the plugin page interfaces (`IHasWebPages`, `PluginPageInfo`).
4. **Open-source Emby plugins as working examples.**
   - Emby's own plugin repo: https://github.com/MediaBrowser/Emby.Plugins
   - Several community plugins build for both Jellyfin and Emby from one repo (for example `metatube-community/jellyfin-plugin-metatube`). Look at how they handle dual targeting and platform differences.
5. Save findings to `docs/port/api-notes.md` (or similar) as you go: which Jellyfin call maps to which Emby call, and where there is **no** equivalent.

Early risks to check once the skeleton loads:
- **Dependency conflicts:** Emby loads its own copies of assemblies. Confirm `System.Text.Json`/`HttpClient`/logging versions that Core brings in don't clash.
- **API shape:** Emby's HTTP API layer is not ASP.NET MVC. Confirm how plugin endpoints are declared before writing the Emby endpoint shell.

## 7. Open decisions (ask the user)

1. **Which Emby version(s) to support** (stable vs beta). This fixes the Emby adapter TFM and therefore Core's TFM.
2. **MVP scope** (section 5 proposes one).
3. **Upstream vs fork.** This is AGPL-3.0, so a fork stays AGPL. The maintainer clearly uses Claude Code (`.claude/skills`). A restructure this size likely wants a discussion thread before a PR.
4. **Core TFM:** `netstandard2.0` only, or multi-target (`netstandard2.0;net10.0`)?
5. **Emby distribution:** how users install it (manual DLL drop, Emby's plugin catalog, a self-hosted manifest). Affects the release workflow.

## 8. Definition of done

- Jellyfin plugin builds from the new structure, all existing tests pass, and manual verification on a local Jellyfin shows playlists/collections create and refresh as before.
- Emby plugin loads in a real Emby server; at least one smart playlist and one smart collection can be created from rules and refreshed.
- CI builds and tests both adapters; release workflow produces an Emby artifact.
- Docs updated; platform differences and unsupported features documented.
