# STATUS: SmartLists for Emby (jellyfin-smartlists-plugin-emby)

Last updated: 2026-11-03. Deeper history: `docs/port/status.md`.

## What this is
The Emby port of the Jellyfin SmartLists plugin: rules-based smart playlists and collections for Emby Server 4.10.1.0. GitHub repo `feerlessleadr/emby.smartlists` (renamed from this folder's name). AGPL-3.0.

## Current state
- Latest release `v0.1.12` (2026-10-14, tag on `8e5d6eb`): phone alerts for the Embidextrous app. The published DLL passed the 18-check phone-alert live test on the test server. Tags `v0.1.0` to `v0.1.12` all exist. Release notes are in `docs/releases/`.
- `main` equals `origin/main` at `ce9e702` ("Record the release"). Working tree clean.
- About 1,858 unit tests pass (xunit, net10.0). The plugin builds with zero warnings (warnings are errors).
- `.github/workflows/release.yml` exists: a `vX.Y.Z` tag builds the DLL on windows-latest and publishes one bare DLL. `docs/port/status.md` says "No CI" in its head section. That text is stale. A later entry restored the workflow. Trust the workflow file.
- Verified on the owner's production server up to the 0.1.3 era. Later versions reached production through the GitHub Repo Plugin Install plugin, but no entry records a production check of each one.

## In progress
Nothing.

## Next steps
Nothing required. Optional, from `docs/port/status.md`:
- Check the Caddy rule (block `/emby/Plugins/*`, `/Plugins/*`, `/web/configurationpage*` for non-private IPs) on the real proxy.
- Re-enable the People prefilter after verifying Emby `GetPeople` behavior.
- Performance audit of lazily loaded Genres and Tags on very large libraries.
- Audit about 57 name-based reflection sites in `Factory.cs`.
- Widen the narrow rule-field dropdown.
- Keep ImageSharp at 3.1.12. Version 4.x needs a paid license.

## Connections (details in WORKSPACE.md)
- Android app: Embidextrous (`C:\dev\embidextrous`) and the older `C:\dev\emby.smartlists.android` call this plugin's REST API at `/emby/Plugins/SmartLists/`. This repo owns that contract (`docs/api.md`, `docs/notifications.md`).
- Contract rule: never change `ApiVersion` in `GET /info` for an addition. The app requires equality. Keep alert message names and fields stable.
- Tags written by Hall Pass or Tag Team can drive smart playlists through the `Tags` rule field. No code dependency, only shared Emby data.

## Known issues
- Rule compile failures such as a bad regex are counted as successful refreshes by `RefreshQueueService`, so they send no phone alert.
- Emby logs a harmless loader message about ImageSharp on some starts. It was fixed in 0.1.2 for the state-machine case. Recheck if it comes back.

## Not verified
- `TimerStopped` alert, and Seq continuing across an Emby restart (unit tested only).
- Phone alerts with the real Android app (the app side of this is in the Embidextrous repo).
- Dark theme of the admin page in Emby's real dark theme.
- Linux or Docker Emby servers.
