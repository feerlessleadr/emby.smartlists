---
name: verify
description: Build and deploy the SmartLists plugin to the local Emby test server and exercise a change end-to-end. Use after any change with runtime behaviour (services, API, page).
---

# Verifying SmartLists changes against a local Emby

Unit tests do not touch Emby. Anything that creates playlists/collections, refreshes, reacts to library/user-data events, or changes the page must be checked on a live server.

## Deploy

```powershell
./dev/deploy-local.ps1 -EmbyRoot '<folder containing Emby system + programdata>'
```

It builds with a timestamped version (Emby caches plugin files by version), stops the server, copies `Emby.Plugin.SmartLists.dll` + `SixLabors.ImageSharp.dll` into `programdata\plugins` and restarts. Then check the log for `SmartLists started` (`programdata\logs\embyserver.txt`, lines `SmartLists: ...`).

## Exercise

- **API**: with an Emby API key (never print it; send as `X-Emby-Token` to localhost only) call `/emby/Plugins/SmartLists` (GET list, POST create, PUT/DELETE by id, `/{id}/refresh`, `/Status`, `/backups`). Then read the result back through Emby (`/emby/Items?IncludeItemTypes=Playlist,BoxSet&UserId=...`).
- **Page**: sign in to the dev server in the in-app browser (the pane must be visible), open *Dashboard → SmartLists* by clicking the sidebar entry, and drive forms through the DOM (set value, dispatch `input`/`change`, click the real button). See `CLAUDE.md` "UI gotchas".
- **Events**: add/remove a media file and trigger a library scan, or favourite an item, to check auto-refresh.

## Clean-profile check (before a release)

Install the release zip into a copy of the Emby `system` folder with a fresh `programdata` and a different port (see `docs/development.md`), then create a playlist and a collection, restart, and confirm they persist.

## Clean up

Delete test lists (`DELETE /emby/Plugins/SmartLists/{id}` removes the Emby item too), stop extra servers and `Stop-Process -Name embytray`.
