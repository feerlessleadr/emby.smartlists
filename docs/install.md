# Installing SmartLists on Emby

Requires **Emby Server 4.10.1.0** (the plugin is compiled against that version; other versions are untested).

## Before you install

Back up your Emby data folder (`<programdata>`, at least `config`, `data` and `plugins`) first, and try the plugin on a test server before a production one. To roll back, stop Emby, delete `Emby.Plugin.SmartLists.dll`, and restore the backup if needed.

## Install

1. Get the plugin: either a release build (the single file `Emby.Plugin.SmartLists.dll`, possibly inside a zip), or build it yourself (`docs/development.md`, "Building a release zip"). It is self-contained: the image library used for covers is embedded in it.
2. Stop Emby Server.
3. Put `Emby.Plugin.SmartLists.dll` (extract it from the zip if needed) into Emby's plugin folder, `<programdata>\plugins` (on Windows by default `C:\Users\<you>\AppData\Roaming\Emby-Server\programdata\plugins`; on Linux `/var/lib/emby/plugins`). Do not put it in a sub-folder.
4. Start Emby Server.
5. Sign in as an administrator and open *Dashboard*. **SmartLists** appears in the left menu (under *Advanced*). Two tasks, *SmartLists backup task* and *SmartLists cleanup task*, appear under *Scheduled Tasks*.

Check the server log (`<programdata>\logs\embyserver.txt`) for lines starting `SmartLists:`. Expect `SmartLists started (auto-refresh listening).`


## Upgrade

Stop Emby, replace `Emby.Plugin.SmartLists.dll`, start Emby. (Versions before 0.1.2 also left a loose `SixLabors.ImageSharp.dll` in `plugins`; it is no longer used by this plugin and can be deleted unless another plugin of yours needs it.) Settings (`<programdata>\plugins\configurations\Emby.Plugin.SmartLists.xml`) and lists (`<programdata>\data\smartlists`) are kept. After an upgrade, reload the SmartLists page with a hard refresh if it looks stale; Emby caches plugin files by plugin version.

## Uninstall

Stop Emby and delete `Emby.Plugin.SmartLists.dll`. To remove everything, also delete `<programdata>\data\smartlists` and the settings file above. Playlists and collections the plugin created stay in Emby; delete them there, or delete the smart lists in the SmartLists page first (with "also delete the Emby list" ticked).

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| No **SmartLists** menu entry | The DLL is not directly in `plugins`, wrong Emby version, or server not restarted. Check the log for `Loading Emby.Plugin.SmartLists`. |
| Page loads but is blank or old after an upgrade | Browser cached the previous version's files: hard refresh, or restart the browser tab. |
| Covers are plain / no collage | Check the log for `SmartLists:` warnings about image processing; the library is embedded in the plugin DLL, so a replaced or truncated DLL is the usual cause. |
| Lists never update on library changes | The list's *Auto refresh* is set to *Never*; set it to *On library changes* or *On all changes*. |
