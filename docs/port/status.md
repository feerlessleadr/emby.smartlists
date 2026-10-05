# Emby port: running status and decision log

Living handoff document. Read this first, then [CLAUDE.md](../../CLAUDE.md) (architecture, commands, gotchas), [plan.md](plan.md) (original phases and known gaps) and [api-notes.md](api-notes.md) (verified Emby API facts). The progress log and checkpoints at the bottom (A to V) are an append-only history; "Where we are", "Decisions", "How to build", "Test environment" and "What is left" are kept current.

## Where we are (2026-10-05)

- **All seven port phases are done and the plugin is released.** Releases published by the owner on GitHub (`feerlessleadr/emby.smartlists`): v0.1.0 (first), v0.1.1 (layout polish), v0.1.2 (single self-contained DLL), v0.1.3 (authenticated image previews and the Field-dropdown fix). Latest source is `main` at or after `2b79608`; `artifacts/` (git-ignored) holds the zips, bare DLLs and `RELEASE_NOTES-*.md`.
- 1,790 unit tests pass; the plugin builds with 0 warnings (warnings-as-errors, full analyzers). Verified on a test Emby 4.10.1.0, on a clean second profile, and by the owner on their production Emby server (it works; the owner reported the issues fixed in 0.1.2 and 0.1.3).
- Known limits: the People prefilter is off (per-item evaluation, correct but slower); external lists, the end-user page, unsupported person roles and `VideoRangeType` are hidden; collection display order has no Emby equivalent; the Help/Documentation links in the page point at `docs/reference` on GitHub (written for Jellyfin, mechanically adapted).
- **No CI** (workflows deleted at the owner's choice). The owner builds the DLL locally with `dev/build-release.ps1`, publishes a GitHub release by hand (tag `vX.Y.Z`, ONE asset: the bare `Emby.Plugin.SmartLists.dll`), and their GitHub-repo-plugin-installer updates production.

## Decisions (do not relitigate without new evidence)

| Decision | Detail |
|---|---|
| Jellyfin dropped | This repo is the Emby plugin. No upstream tracking, no compat layer, no Jellyfin-named shims. The original Jellyfin code is kept on the `jellyfin-original` branch of the repo (old `main` at `e25f62f`). |
| Target | Emby Server 4.10.1.0, net8.0. Local dev install: `C:\Users\kgiglio\Downloads\embyserver-win-x64-4.10.1.0` (plugins in `programdata\plugins`, launch `system\EmbyServer.exe`). A clean second profile lives in `Downloads\emby-clean-test` (own copy of `system`, port 8097; stopped). |
| Repo | `https://github.com/feerlessleadr/emby.smartlists` (renamed from `jellyfin-smartlists-plugin-emby`). The port was fast-forwarded onto `main` (no history rewrite) at the owner's choice ("option 2"); the local branch is `main`. Pushing and release publishing happen only when the owner says so. |
| Scope | In: rules, smart playlists and collections, manual/scheduled/auto refresh, admin page, images, backups, templates. Out (hidden): external lists, end-user page, person roles Emby lacks, `VideoRangeType`. |
| Distribution | A single self-contained `Emby.Plugin.SmartLists.dll` (ImageSharp embedded; a loose DLL is file-locked on Windows and broke in-place upgrades in 0.1.1). Installed by the owner's `Emby.GitHubRepoPluginInstall` plugin: it uses GitHub Releases (newest non-prerelease by published date), detects a new version by a differing **tag name**, prefers a loose `.dll` asset over a zip, extracts zips recursively over `plugins` with overwrite, and does not retry locked files. So: a new tag and a new assembly version for every release, one asset only. |
| Versioning | Assembly version = tag (`-p:Version=x.y.z`). Emby caches plugin files by version (ETag), so dev builds use a timestamp version. |
| Item identity | `long BaseItem.InternalId` for items; users stay `Guid`. `ItemKinds` strings replace `BaseItemKind`. DTO fields renamed `PlaylistId` / `CollectionId`. |
| Playlist refresh | Remove all entries, then add, via `IPlaylistManager`. Owner = the user with the `ManageDelete` share row. |
| Forced sort title | `SetSortNameDirect` + lock `MetadataFields.SortName`, re-applied after creation (Emby's post-create refresh can revert it). |
| Collections | `ICollectionManager.CreateCollection` (locked), membership by add/remove diff, images under `GetInternalMetadataPath()`. |
| Docs | Live on GitHub in the repo (`README.md`, `docs/install.md`, `using.md`, `development.md`, `security.md`, `reference/` carried over from upstream, `port/` history). No mkdocs site. |
| License and credit | AGPL-3.0 (inherited, `LICENSE` kept). The README credits **jyourstone** (original Jellyfin SmartLists plugin: idea and much of the design) and **ankenyr** (original SmartPlaylist plugin), states the AGPL source-availability obligations, is explicitly unaffiliated with Emby, and carries an AI disclosure and no-warranty section ("ported exclusively with Claude ... if you are not comfortable with that, do not use this plugin"). |
| Security posture | Admin-only API (`[Authenticated(Roles="Admin")]` on one wildcard route); `docs/security.md` lists the tests run (authn/authz incl. non-admin 403, traversal, malicious backup zip, upload validation, fuzzing, XSS in the UI, dependencies). Advice given to the owner for the internet-facing server: block `/emby/Plugins/*`, `/Plugins/*`, `/web/configurationpage*` and `/emby/web/configurationpages*` at Caddy for non-private source IPs (`respond 403`, `not remote_ip private_ranges`), keep Emby patched, use strong admin passwords. Not yet verified against the owner's Caddy. |
| People prefilter | Disabled (returns null) until Emby `GetPeople` semantics are verified. |
| Warnings | `TreatWarningsAsErrors` ON for the plugin; the tests project is relaxed on purpose. |

## How to build, test, deploy (Windows)

See `docs/development.md`. Short version (from the repo root):

```powershell
$env:EMBY_SYSTEM_DIR = 'C:\Users\kgiglio\Downloads\embyserver-win-x64-4.10.1.0\system'
dotnet build Emby.Plugin.SmartLists/Emby.Plugin.SmartLists.csproj
dotnet test  Emby.Plugin.SmartLists.Tests/Emby.Plugin.SmartLists.Tests.csproj
./dev/deploy-local.ps1 -EmbyRoot 'C:\Users\kgiglio\Downloads\embyserver-win-x64-4.10.1.0'   # run from the repo root
./dev/build-release.ps1 -Version 0.1.4                                                     # -> artifacts\*.dll and *.zip
```

- Tests run on net10.0 (the ASP.NET 8 runtime is not installed). Stale Jellyfin DLLs in the test `bin`/`obj` must be deleted.
- Each server start leaves an `embytray.exe` (`Stop-Process -Name embytray`). Emby refuses a second instance from the same exe path.
- Browser-pane testing: the pane must be visible; the owner has to type the sign-in password; click the SmartLists sidebar entry after loading `.../index.html?fresh=N#!/dashboard`. Click coordinates are in the screenshot frame, not CSS pixels.
- A chained shell command that `cd`s elsewhere and then calls `dev/deploy-local.ps1` by relative path silently skipped deploys twice: always run it from the repo root.

## Test environment (Emby)

- Libraries at `C:\claude\{Movies,Music,Shows}` hold generated placeholder media (5 movies, 150 bulk movies, 2 series, 12 tracks; "Test Movie One" has an NFO with actors and a director). Users: `kevin` (Guid 187e8098a0404bf1a42200bcf6f9d29f, admin) and `test1` (non-admin).
- Test API key file `%USERPROFILE%\.emby-spike-key`: deleted once, then recreated by the owner for more testing (it may exist). Never print it; send it only as `X-Emby-Token` to `localhost:8096`; delete it again (and revoke the key in Emby's API Keys page) when testing is finished.
- The throwaway spike plugin was removed from the plugins folder in phase 4.

## What is left

- Nothing required. Possible follow-ups: verify the Caddy block on the real proxy; watch the installer update path (0.1.2 to 0.1.3 should work in place); optionally re-enable the People prefilter after verifying `GetPeople`; a performance audit of lazily loaded Genres/Tags on very large libraries; audit the remaining name-based reflection in `Factory.cs` (about 57 sites), `Engine.cs`, `MediaStreamHelper`, `ArtistOrder`; widen the narrow rule-field dropdown list (long names wrap); decide whether to release a build after 0.1.3.

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
- **Still unverified against a running Emby (phase 4):** everything listed in "Known gaps" in `plan.md`. The unit tests prove the plugin logic against Emby's real types, not Emby's runtime behavior.
- **Committed** phases 1-3 as `a13ab8d` on `emby/step3-shared-compile` (223 files). Working tree clean after that commit. (`/verify` skill not applicable: it drives a Jellyfin Docker container.)

### Phase 4 plan (started)

Goal: make the playlist/collection services actually work on a live Emby. Order of work:
1. **Offline, unit-testable first:** replace the remaining Jellyfin-only reflection in `Factory.cs` (playlist membership/ownership: `OwnerUserId`, `OpenAccess`, `Shares`, `LinkedChildren`, `ItemId`, around lines 3923-3950 and 4175-4190) and any similar in `PlaylistUserResolver`/others with typed Emby calls (`PlaylistOwnership`, `playlist.GetChildren`); add unit tests. Raise swallowed per-item exceptions in `SmartList` from Debug to Warning.
2. **Minimum host to run the code in Emby** (pulls forward the core of phase 5): `IServerEntryPoint` composition root that builds the service graph by hand (incl. `IFileSystem`, stores, `RefreshQueueService`), and a logging adapter from Emby's `ILogger` to `Microsoft.Extensions.Logging.ILogger<T>`; plus a temporary authenticated debug endpoint to trigger a refresh from a DTO.
3. **Runtime verification on the test Emby** (libraries at `C:\claude`, users `kevin`/`test1`): create a smart playlist and a smart collection from rules, refresh, change membership, check ownership/IsPublic/sort title/collection membership (`CollectionIds`), per-user playlists, and record results in `api-notes.md`. Remove the throwaway spike plugin from the Emby plugins folder when the real plugin is deployed.
- **Phase 4, step 1 done** (offline): all reflective container-member/playlist-ownership lookups replaced with typed Emby calls. New `Utilities/ContainerMembers.cs` (`Playlist.GetChildren(query)` verified; BoxSet members via `CollectionIds` still unverified). `Factory` no longer reflects on `OwnerUserId`/`OpenAccess`/`Shares`/`LinkedChildren`/`ItemId` (Emby's user-scoped playlist query already returns only visible playlists). `SmartList` reports skipped-item exceptions through `ReportSkippedItem` (first 5 per refresh at Warning, then Debug plus one notice). 1,732 tests pass, 0 warnings.
- **Next: phase 4 step 2** (minimum host to run in Emby): `IServerEntryPoint` composition root + Emby-to-`Microsoft.Extensions.Logging` adapter + a temporary authenticated debug endpoint, then step 3 (runtime verification on the test Emby).

### Checkpoint F — phase 4 step 2 (host composed, not yet run in Emby)
- Added `Host/EmbyLoggerProvider.cs` (MEL → Emby `ILogger`, text passed as `"{0}"` arg; ReadOnlyMemory overloads are obsolete errors), `Host/SmartListsHost.cs` (hand-built composition root, `Instance`), `Host/StartupEntryPoint.cs` (`IServerEntryPoint`), `Host/DebugService.cs` (TEMPORARY `/SmartListsDebug/Refresh?Kind=&Name=&UserName=&MediaType=&Field=&Operator=&Value=&Public=`; remove in phase 6). External list service is null.
- Plugin builds with 0 warnings. Next: step 3 — deploy to test Emby (needs user OK to restart, remove spike DLL), hit the debug endpoint, record results in `api-notes.md`.

### Checkpoint G — phase 4 step 3 (runtime verification) DONE for core paths
- Deployed `Emby.Plugin.SmartLists.dll` + `SixLabors.ImageSharp.dll` to the test Emby plugins folder; spike DLL removed. Results in `api-notes.md` ("Phase 4 runtime results"). Create/update/public flip for playlists and collections all pass. Test lists deleted afterwards.
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

### Checkpoint M — docs and repo tidy-up (phase 7)
- Docs now live on GitHub in the repo: `README.md` (rewritten for Emby), `docs/install.md`, `docs/using.md`, `docs/development.md`, `docs/reference/` (the upstream user guide and examples, mechanically adapted Jellyfin->Emby with a README listing what does not apply; `external-lists.md` removed), `docs/port/` (this log, plan, API notes, handoff, superseded phase-1 design — moved from `docs/` and `EMBY_PORT_HANDOFF.md`). `CLAUDE.md` and `AGENTS.md` rewritten (identical) for the Emby project.
- Removed upstream/Jellyfin-only infrastructure: mkdocs site (`mkdocs.yml`, overrides, content, screenshots), `dev/` Docker scripts, `.claude/skills/release`, issue templates, funding, stale bot, release-notes config, `CONTRIBUTING.md`.
- Added `dev/deploy-local.ps1` and `dev/build-release.ps1` (both exercised: release zip holds the two DLLs; deploy restarts the dev server), rewrote the `verify` skill, rewrote `ci.yml` and `release.yml` (need the repo variable `EMBY_SERVER_URL` for the Emby assemblies; NOT yet run on GitHub).
- The in-page Help/Documentation links now point at `blob/main/docs/reference/user-guide/*.md` in `feerlessleadr/emby.smartlists`.
- Left: remove the API key file `%USERPROFILE%\.emby-spike-key` when done testing; optional fix for the startup ImageSharp loader error; reference docs still contain Jellyfin-era detail on features that do not apply.

### Checkpoint N — startup loader error fixed
- Cause: Emby calls `GetTypes()` on each plugin assembly before `Plugin`'s AssemblyResolve exists. `CollageBuilder`'s async methods compiled to state-machine structs with `TaskAwaiter<Image>` fields, which need ImageSharp to load, so two types failed (`Error loading types from assembly`). Found with a small probe that loads the DLL in an `AssemblyLoadContext` that cannot resolve ImageSharp and calls `GetTypes()`.
- Fix: `CollageBuilder` does its ImageSharp work in synchronous private methods; the public `...Async` methods are `Task.Run` wrappers. After the change `GetTypes()` succeeds without ImageSharp, the server log has no loader error, and the auto-generated cover (collage + badge) is still produced.

### Checkpoint O — security review (see docs/security.md)
- Probed the admin API (authn, traversal, upload, malicious backup zip, JSON/regex/size fuzzing), UI script injection, dependencies and static patterns. Findings fixed: uploaded/restored images were accepted by extension only and SVG was allowed -> `Utilities/ImageContentValidator` (magic bytes), SVG removed, rejected uploads now return 400. No traversal, injection or auth bypass found.
- Non-administrator session (test1) verified: 403 on every endpoint tried, including the plugin configuration GET.

### Checkpoint P — CI removed
- Deleted `.github/` (the CI and release workflows) at the owner's request: the plugin is built and tested locally, and the workflows could not run without a hosted copy of the Emby server DLLs. `docs/development.md` no longer describes CI. Earlier checkpoints that mention the workflows are historical.

### Checkpoint Q — page layout polish (from production feedback)
- Dropdowns (native selects, searchable and multi selects) were much shorter than the text inputs: `config-controller.js` now measures `#playlistName` and sets `--sl-control-height` on the view so every control is the same height (35px at the default zoom).
- Tab bar (`.localnav`) now has a tinted background with a theme-primary active tab; rule groups, rule rows, the group max-items strip and sort boxes are separate tinted panels (`--sl-surface-1/2`, derived from the theme text colour so they work in light and dark). All of it lives in the stylesheet injected by `config-controller.js`.
- Verified in the light theme in the browser pane; the dark theme was only previewed by overriding Emby's theme variables, not with Emby's real dark theme.

### Checkpoint R — page layout polish, round 2
- Tab bar is content-width (`inline-flex`) with rounded ends; every control (native selects, searchable and multi selects, inputs, textareas, the tags box) shares one fill/border/radius from the theme input colour (`--sl-input-bg/-border`) and one height; text areas get vertical padding; dropdown text sizes match; more space under labels that sit above a multi-select (`:has()`); `.selectContainer` is full width (bumper select). All in the stylesheet injected by `config-controller.js`.
- Gotcha: `dev/deploy-local.ps1` must be run from the repo root (a failed relative path in a chained command silently skipped a deploy twice); dev builds in the same minute share a version, so the browser may keep a cached controller.

### Checkpoint S — v0.1.2: single self-contained DLL
- Production report: upgrading 0.1.1 with the GitHub plugin installer failed with "cannot access ...SixLabors.ImageSharp.dll because it is being used by another process" (Windows file lock on the loose ImageSharp DLL, loaded via `Assembly.LoadFrom`).
- Fix: ImageSharp is embedded in the plugin DLL (`EmbeddedResource` from the NuGet package, runtime asset excluded) and `Plugin.cs` loads it with `Assembly.Load(bytes)` (cached) in the assembly-resolve handler. Verified locally: no loose DLL, no startup loader error, covers still generated, plugin DLL can be overwritten while Emby runs. `dev/build-release.ps1` now writes the zip (one file) and a bare DLL; the installer prefers a loose `.dll` asset, so a release should carry just the bare DLL (or just the zip). Docs updated (install, development, README, CLAUDE.md/AGENTS.md, security notes).
- Caveat: the 0.1.1 -> 0.1.2 upgrade still needs one manual stop/replace/start because 0.1.1 holds the file.

### Checkpoint T — v0.1.3: authenticated image previews
- `Plugins/SmartLists/{id}/images/{type}/file` requires the access token; `<img src>`/`<a href>` cannot send Emby's header, so the edit-form thumbnail was broken and the link showed "Access token is invalid or expired". `config-images.js` now fetches the image with the `Authorization: MediaBrowser Token=...` header and uses a blob: URL (`SmartLists.fetchImageBlobUrl`, `setAuthedImageSrc`, delegated click handler for `a.sl-authed-image-link`); `config-lists.js` builds those links in the Manage details. No token in URLs.
- Reported but NOT reproduced: rule *Field* dropdown not opening with a second Emby tab (playlist screen) open. In the browser pane (two tabs, real clicks, create and edit mode, after server-pushed refresh events, after a synthetic `pagehide`) every custom dropdown (rule field, sort by, media types, users, settings) opened. Waiting on the reporter's browser/console details.

### Checkpoint U — v0.1.3: Field dropdown dying after the page is re-shown
- Reproduced by the owner (create a list, open a playlist in another tab, return): the rule Field dropdown no longer opened until a reload. Root cause: `initSearchableSelect` bound its element listeners with `{signal: ruleRow signal}`; `reinitializeExistingRules` (run from `initPage` after `pagehide` cleanup resets `_pageInitialized`, then `pageshow`) aborts every rule's AbortController and creates a new one, but `initSearchableSelect` returns early on already-initialised selects, so the display click handler stayed removed. Other dropdowns (multi-selects, native selects) were unaffected because they do not use the rule signal.
- Fix: the widget no longer ties its listeners to the passed signal (they live and die with its own DOM elements); the document-level outside-click listener removes itself when the wrapper leaves the DOM. A/B verified in the browser pane by dispatching `pagehide` + `pageshow` on the page: old code -> dropdown dead; new code -> opens after any number of cycles; all other custom dropdowns also open.

### Checkpoint V: publishing, releases and owner decisions after the port (summary)
- **Repo and publishing:** the owner renamed the GitHub repo to `feerlessleadr/emby.smartlists`; `origin` was updated and the in-repo links retargeted. The old Jellyfin `main` is preserved as the branch `jellyfin-original`; the port was pushed to `main` as a fast-forward; later fixes were pushed in batches, each on request.
- **README additions (all pushed):** an accurate intro (rules are defined by the user in a rule builder; nothing is generated from free text); an AI disclosure and no-warranty section; credits to jyourstone and ankenyr; an AGPL-3.0 license summary and a non-affiliation line. `docs/install.md` got a "Before you install" backup note.
- **Licensing guidance given (not legal advice):** an AGPL-3.0 derivative must stay AGPL; linking to the public repo from the Emby forums is fine; keep `LICENSE`; the repo is the source offer; no Emby code is redistributed; the embedded ImageSharp has its own split license, fine for an open-source repo.
- **GitHub installer plugin analysed** (see Decisions): one loose DLL asset per release and a new tag every time. The first update through it failed with "file in use" on `SixLabors.ImageSharp.dll`, which led to v0.1.2 (checkpoint S); the 0.1.1 to 0.1.2 step needed one manual stop, replace, start.
- **CI removed** at the owner's request (checkpoint P); `.github/` is gone; a stale Jellyfin `release` skill copy under `.agents` was deleted and the `verify` skills were rewritten for Emby.
- **Production feedback loop:** layout polish (checkpoints Q and R, shipped in v0.1.1); image preview and Field dropdown bugs (checkpoints T and U, shipped in v0.1.3). The dropdown bug was reproduced from the owner's live tab state and confirmed fixed with an A/B check (old code dead after a simulated `pagehide` plus `pageshow`, new code alive).
- **Housekeeping:** stray `embytray.exe` icons were closed on request; the clean-profile server on port 8097 is stopped; test lists and temporary files were deleted after each test; this status head was rewritten on 2026-10-05 so it no longer says "phase 3 in progress" or "nothing committed".
