# DECISIONS: SmartLists for Emby

Append-only. Newest at the bottom. Never rewrite an entry; add a new one that references it.
The full dated history (checkpoints A to W, security and alert work) is in `docs/port/status.md`. The entries below are the standing decisions from its "do not relitigate" table.

### 2026-10-04, Drop Jellyfin and do not track upstream
- **Context:** The port started from the upstream Jellyfin plugin. Keeping both targets would need a compatibility layer.
- **Options considered:** Dual Jellyfin and Emby support; Emby only with upstream tracking; Emby only without tracking.
- **Decision:** Emby only. No compatibility layer, no Jellyfin-named shims, no upstream tracking. The old Jellyfin `main` is kept on the branch `jellyfin-original` (commit `e25f62f`).
- **Rationale:** The Emby APIs differ too much (ids, items, hosting, UI). A layer would slow every change.
- **Impact:** `C:\dev\jellyfin-smartlists-plugin` is only a reference clone. Upstream features are ported by hand if wanted.

### 2026-10-04, Target Emby 4.10.1.0 on net8.0, compile against the server's own DLLs
- **Context:** Emby assemblies are not on NuGet.
- **Options considered:** Reference a NuGet package; reference the server's `system` folder.
- **Decision:** Build with `-p:EmbySystemDir=<system folder>` (or env `EMBY_SYSTEM_DIR`). Tests run on net10.0 because the ASP.NET 8 runtime is not installed on the dev PC.
- **Rationale:** It is the only way to compile against the real types.
- **Impact:** CI downloads the Emby server from MediaBrowser/Emby.Releases and runs on windows-latest (Linux failed to load the Windows assemblies).

### 2026-10-04, Item identity, refresh and sort rules
- **Context:** Emby differs from Jellyfin in ids and in how playlists and collections behave.
- **Decision:**
  - Items use `long InternalId`; users stay `Guid`. DTO fields are `PlaylistId` and `CollectionId`.
  - Playlist refresh removes all entries, then adds, through `IPlaylistManager`. The owner is the user with the `ManageDelete` share row.
  - Forced sort title uses `SetSortNameDirect` plus a lock on `SortName`, re-applied after creation.
  - Collections use `ICollectionManager.CreateCollection` (locked) with a membership diff.
  - The People prefilter is disabled until `GetPeople` semantics are verified.
- **Rationale:** Each rule came from a verified runtime finding (see `docs/port/api-notes.md`). Emby's post-create refresh can revert a sort title.
- **Impact:** `Services/Playlists`, `Services/Collections`, `Core/QueryEngine`. Data written before the `PlaylistId` rename is not read back.

### 2026-10-04, Scope: what is in and what is hidden
- **Decision:** In: rules, playlists, collections, manual, scheduled and automatic refresh, admin page, images, backups, templates. Out and hidden: external lists (MDBList, Trakt), the end-user page, person roles Emby lacks, `VideoRangeType`.
- **Rationale:** They cannot work on Emby yet or are outside the MVP.
- **Impact:** `UserPagesController` and `UserSmartListController` are excluded from the build.

### 2026-10-05, Ship one self-contained DLL
- **Context:** Upgrading 0.1.1 through the GitHub plugin installer failed because Windows locked the loose `SixLabors.ImageSharp.dll`.
- **Options considered:** Loose DLL plus zip; zip only; embed ImageSharp in the plugin DLL.
- **Decision:** Embed ImageSharp as a resource and load it from memory. A release has exactly one asset, the bare `Emby.Plugin.SmartLists.dll`, and a new tag name each time.
- **Rationale:** The installer prefers a loose `.dll` asset, detects updates by a changed tag name, and does not retry locked files.
- **Impact:** v0.1.2 and later. No type may need ImageSharp to load, because Emby calls `GetTypes()` before the resolver exists.

### 2026-10-05, Version equals tag, and every build gets a new version
- **Decision:** Assembly version equals the tag. Dev builds use a timestamp version (`0.MMdd.HHmm`).
- **Rationale:** Emby caches plugin pages by plugin version only, so a rebuild at the same version leaves browsers on stale code.
- **Impact:** `dev/deploy-local.ps1`, `dev/build-release.ps1`.

### 2026-10-05, Admin-only API and security posture
- **Decision:** One wildcard route with `[Authenticated(Roles="Admin")]`. Keep `docs/security.md` as the list of tests run. Advise blocking plugin and config-page paths for non-private IPs at the reverse proxy.
- **Rationale:** The plugin can create and delete lists and write files, so only admins should reach it.
- **Impact:** The Caddy rule is advice only and was never verified against the owner's proxy.

### 2026-10-05, License and credit
- **Decision:** AGPL-3.0 (inherited). The README credits jyourstone and ankenyr, states it is unaffiliated with Emby, and carries an AI disclosure and no-warranty section.
- **Rationale:** An AGPL derivative must stay AGPL. Not legal advice.
- **Impact:** `LICENSE`, `README.md`.

### 2026-10-05, Warnings are errors in the plugin, relaxed in tests
- **Decision:** `TreatWarningsAsErrors` with `AnalysisMode=Recommended` for the plugin only.
- **Impact:** New code must satisfy CA and SA analyzers.

### 2026-10-07, Keep ImageSharp 3.1.12, suppress advisories with reasons
- **Context:** Eleven ImageSharp advisories appeared; five affect 3.1.12. Version 4.x refuses to build without a paid Six Labors license.
- **Decision:** Stay on 3.1.12. Limit decoders to JPEG, PNG, GIF, WebP and BMP. Remove TIFF. Put per-advisory `NuGetAuditSuppress` entries with reasons in both csproj files.
- **Rationale:** No free fixed version exists. The decoder limits remove the affected paths.
- **Impact:** `CollageBuilder`, `ImageContentValidator`, `docs/security.md`. The Dependabot branch for 4.1.2 is deliberately not adopted.

### 2026-10-10, Android app is a separate repo, and the plugin API is a contract
- **Context:** The owner wanted an admin Android app for SmartLists.
- **Decision:** Separate public repo (`emby.smartlists.android`, later merged into Embidextrous). The plugin owns `docs/api.md`. `GET /info` gives `ApiVersion` (the app needs equality) and `GET /catalog` describes fields and sorts.
- **Rationale:** The plugin installer and Obtainium would each see the other's releases, and the plugin workflow triggers on every `v*` tag.
- **Impact:** Never change `ApiVersion` for an addition. Keep names stable.

### 2026-10-14, Phone alerts over a websocket subscription, not session pushes
- **Context:** `SendMessageToAdminSessions` does not reach a socket whose token is in a header, which is how the app connects.
- **Decision:** Add `EventsPushListener` with `SmartListsEventsStart` and `SmartListsEvents`. The start message carries the admin token as its fourth value. The plugin validates it through the session manager on every push and never logs it. The REST catch-up (`GET Events`) and session push stay.
- **Rationale:** Found by live test with Hall Pass. A 30 second own heartbeat timer replaced Emby's period timer, which did not fire.
- **Impact:** `docs/notifications.md`, `C:\dev\embidextrous\docs\push-contract.md` (shared shape, owned by the app side). Released in v0.1.12.
