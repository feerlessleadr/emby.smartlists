# Security notes

Written for running the plugin on an internet-facing Emby server. This is a review and test record by the author, not an independent audit.

## What the plugin exposes

- **API**: one catch-all route, `/emby/Plugins/SmartLists/{path}`, for every verb. It carries `[Authenticated(Roles = "Admin")]`, so Emby rejects anything without a valid **administrator** session or API key before plugin code runs. Authentication is Emby's own token header; there are no cookies, so no CSRF surface.
- **Page files**: the admin page, its controller and the `config-*.js` scripts are served by Emby at `/web/configurationpage?name=...` **without authentication** (this is how Emby serves every plugin's pages). They are static code with no data or secrets.
- **Background work**: event handlers (library and user-data changes), a refresh queue, and two scheduled tasks (backup, cleanup). None accept network input.
- **Outbound network**: none. The external-list providers (HTTP clients) are compiled in but not wired up, and the settings and fields that would use them are hidden.
- **Files written**: `<programdata>/data/smartlists` (list definitions, images, backups), images under Emby's playlist/collection folders, and the plugin settings XML.

## Tests run (Emby 4.10.1.0, local test server)

| Area | Test | Result |
|---|---|---|
| Authentication | All endpoints and verbs with no token and with a bad token | 401 everywhere |
| Authorization | Non-administrator session (user `test1`, `IsAdministrator=false`) calling list, users, fields, libraries, status, backups (list and create), timer, refresh-all, create, delete, and the plugin configuration endpoint | 403 on every call |
| Path traversal | `..`, encoded `%2e%2e%2f`, backslashes, NUL, in list ids, image types, backup file names; GET, DELETE | Rejected (400/404), no file outside the plugin folders touched |
| Backup restore | Crafted zip with `../` and `..\` entry names, non-GUID folders, hostile `config.json` (path-like `CustomImages`, HTML in fields) | Entries confined to the list's own GUID folder by file name only; traversal entries skipped or flattened; nothing written outside the plugin folder |
| Image upload | Name traversal, double extensions, NUL, 23 MB file, random bytes, executable bytes named `.jpg`, SVG with a script | Stored under fixed names (`primary.<ext>`); size cap enforced. **Found:** bytes were not checked and SVG was accepted. **Fixed:** magic-byte validation, SVG removed (`ImageContentValidator`), also applied to restored zips |
| Input handling | Malformed JSON, wrong types, depth-3000 JSON, 100 kB and 30 MB bodies, 5,000-character regex, invalid regex, injection-style field names | 400 with a message each time; server stayed up (name cap 500 chars, regex cap 1,000, JSON depth 64) |
| Regex | Catastrophic pattern `(a+)+$` | Accepted but every evaluation runs under a 1 s match timeout |
| Script injection (UI) | HTML/script payloads in list name, overview, tags, sort title and rule value; viewed in Manage, expanded, and opened in Edit | Rendered as text, no element injected, no script ran |
| Dependencies | `dotnet list package --vulnerable --include-transitive`, `--deprecated` | Reported on a fresh restore (2026-10-07): eleven new ImageSharp advisories, five of which affect 3.1.12 (the fix exists only in 4.x, which needs a paid Six Labors license). **Handled:** none is reachable in this plugin (see the ImageSharp note below), so they are suppressed in the csproj with a reason each. Only direct reference: SixLabors.ImageSharp 3.1.12 |
| Static scan | Process start, dynamic assembly load, binary/XML deserialization, SQL, script compilation | None used (ImageSharp is loaded from a resource embedded in the plugin DLL via `Assembly.Load`) |

## Known residual risks

- **Admin-only abuse**: an administrator can already do worse in Emby, but note: the *custom backup path* setting lets an admin choose where backup zips are written; a backup zip upload is capped at 1 GB and its contents are not size-limited when extracted (zip bomb), so a stolen admin token could fill a disk.
- **Lists for unknown users**: creating a list for a non-existent user id is accepted (it fails at refresh). Harmless.
- **Error detail**: some 500 responses include the exception message, which can contain server paths. Only administrators see these.
- **ImageSharp** decodes uploaded images and library artwork. It is pinned at 3.1.12 because the fixes for the advisories published 2026-10-07 (GHSA-wmxv-xphr-5c9g, GHSA-j9gm-c75j-xc9q, GHSA-jjfr-hcj7-qf5w, GHSA-j3p4-wp97-rph4, GHSA-gwg2-r3hj-4w44) exist only in 4.x, which needs a paid Six Labors license. Decoding is also bounded (see the 2026-10-10 review below: size, pixel and frame limits). Mitigation: TIFF is not decodable (`CollageBuilder` registers only JPEG, PNG, GIF, WebP and BMP decoders) and TIFF uploads are rejected (`ImageContentValidator`, extension and content-type lists), which removes the BigTIFF decoder loop and the TIFF encoder bugs; the plugin does not use the histogram processor, and the ICC parser bug in 3.x needs `IccProfile.Entries`, which the plugin never reads. Verified with a 24-byte BigTIFF: the default decoder is still running after 6 s, the plugin's options reject it in 4 ms. Re-check with `dotnet list package --vulnerable` and move to a patched 3.x if one appears.
- **Plugin UI files are public** (Emby behaviour). No secrets are in them.
- **Settings XML** has fields for third-party API keys (unused here, hidden in the UI). They would be stored in plain text if ever set.

## Security review of 2026-10-10 (plugin 0.1.11)

A read-only review of the plugin and the Android app (two independent reviews, then each finding checked against the code). No critical or high issue; nothing reachable without an administrator token except the library-artwork decode. Findings and what was done:

| Finding | Status |
|---|---|
| Status page history put a refresh error message into a quoted HTML attribute with an escape function that did not escape quotes (list-controlled text such as a regex could add attributes, so script in the admin page) | Fixed (0.1.11): `config-status.js` escapes quotes. Checked in a browser: a hostile message stays inside one attribute |
| Restoring a backup did not apply the checks a created list gets, and kept the Emby playlist/collection ids from the zip, so a crafted or foreign backup could steer a later refresh onto an unrelated item | Fixed: restore validates each list (`InputValidator.ValidateSmartList`), drops the Emby item ids (an overwrite keeps the ids this server already has for that list) and uses the canonical Id form |
| Restore with `overwrite=false` could overwrite a list when the zip wrote its Id without dashes; creating a list with an existing `Id` replaced it | Fixed: Ids are normalised before the existence check; `POST /` with an Id that exists answers 409, with a non-GUID 400 |
| The `SimilarTo` rule compiled a regex per item with no match timeout (a catastrophic pattern could pin a CPU core) | Fixed: it uses the shared cache and timeout like every other regex rule; the cache is also capped (500 patterns). The empty-string prefilter probe has the timeout too |
| Library artwork and uploaded covers were decoded with no limits on size, dimensions or frames | Fixed: a source file over 50 MB or a picture over 40 megapixels is refused after reading only its header (`Image.Identify`), one frame is read, and the decoder's allocation is capped at 512 MB |

Not changed, accepted for now (all need an administrator token): the admin gate is Emby's `[Authenticated(Roles = "Admin")]` on the request class and the plugin does not re-check it (the `[Authorize]` and `[AllowAnonymous]` attributes on the controller are ignored by the router, so they decorate rather than protect); backup upload and restore have no entry-count or total-size cap beyond the 1 GB upload limit (a stolen admin token could fill a disk, as noted above); some 500 responses and `GET /backups` include exception text or the backup folder path; request bodies are read without a size limit of our own (Emby's applies); route values are unescaped once more in `ControllerRouter` (every consumer tolerates it; a trap for future parameters). The app-side findings are in the app repository's `docs/decisions.md`.
## Recommendations for an internet-facing server

1. Keep Emby itself patched: it is the real attack surface; the plugin adds only administrator-only endpoints.
2. Do not share administrator accounts; use a strong password and, ideally, access the dashboard through a VPN or restrict `/web` and `/emby/Plugins` at the reverse proxy.
3. Put Emby behind a TLS reverse proxy with rate limiting; Emby's login throttling is the main defence against token guessing.
4. Keep the plugin folder write-protected for non-admin OS users, and back up `<programdata>/data/smartlists`.
5. After upgrading the plugin, hard-refresh the dashboard page (Emby caches plugin files by version).

## Re-running the checks

The probes above are plain HTTP calls with an admin API key against a test server (see `docs/development.md`); the unit tests `ControllerRouterTests` and `ImageContentValidatorTests` pin the router and image rules. The non-admin check needs a non-administrator Emby user signed in to the browser: call `ApiClient.ajax({type:'GET', url: ApiClient.getUrl('Plugins/SmartLists')})` from the console and expect 403.
