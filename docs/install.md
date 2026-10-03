# Installing SmartLists on Emby

Requires **Emby Server 4.10.1.0** (the plugin is compiled against that version; other versions are untested).

## Install

1. Get the plugin zip: either a release build, or build it yourself (`docs/development.md`, "Building a release zip"). It contains exactly two files:
   - `Emby.Plugin.SmartLists.dll`
   - `SixLabors.ImageSharp.dll` (used for the cover collage and badge)
2. Stop Emby Server.
3. Extract both files into Emby's plugin folder, `<programdata>\plugins` (on Windows by default `C:\Users\<you>\AppData\Roaming\Emby-Server\programdata\plugins`; on Linux `/var/lib/emby/plugins`). Do not put them in a sub-folder.
4. Start Emby Server.
5. Sign in as an administrator and open *Dashboard*. **SmartLists** appears in the left menu (under *Advanced*). Two tasks, *SmartLists backup task* and *SmartLists cleanup task*, appear under *Scheduled Tasks*.

Check the server log (`<programdata>\logs\embyserver.txt`) for lines starting `SmartLists:`. Expect `SmartLists started (auto-refresh listening).`


## Upgrade

Stop Emby, replace both DLLs, start Emby. Settings (`<programdata>\plugins\configurations\Emby.Plugin.SmartLists.xml`) and lists (`<programdata>\data\smartlists`) are kept. After an upgrade, reload the SmartLists page with a hard refresh if it looks stale; Emby caches plugin files by plugin version.

## Uninstall

Stop Emby and delete the two DLLs. To remove everything, also delete `<programdata>\data\smartlists` and the settings file above. Playlists and collections the plugin created stay in Emby; delete them there, or delete the smart lists in the SmartLists page first (with "also delete the Emby list" ticked).

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| No **SmartLists** menu entry | DLLs not directly in `plugins`, wrong Emby version, or server not restarted. Check the log for `Loading Emby.Plugin.SmartLists`. |
| Page loads but is blank or old after an upgrade | Browser cached the previous version's files: hard refresh, or restart the browser tab. |
| Covers are plain / no collage | `SixLabors.ImageSharp.dll` missing from `plugins`. |
| Lists never update on library changes | The list's *Auto refresh* is set to *Never*; set it to *On library changes* or *On all changes*. |
