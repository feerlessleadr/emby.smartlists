# Using SmartLists on Emby

Open *Dashboard → SmartLists*. The page has four tabs: **Create List**, **Manage Lists**, **Status** and **Settings**.

## Creating a list

1. Optionally start from a **template** (TV channel, continue watching, fresh and unseen, balanced genre mix, album roulette, ...) and press *Use*.
2. Choose **Playlist** (belongs to the chosen user or users) or **Collection** (server-wide).
3. Name it, pick the **media types**, and build **rules**: each row is *field / operator / value*. Rows in a group are combined with AND, groups with OR.
4. Pick sorting, then *Create*. The list is built immediately and refreshed in the background.
5. *More options* holds limits, bumpers, automation (refresh triggers and schedules), sharing, and presentation (cover image, sort title, overview, tags).

The generated playlist or collection is named with a prefix/suffix from *Settings* (default suffix `[Smart]`) so you can tell it from hand-made ones.

## Managing lists

*Manage Lists* shows every list with item count and state. Per list: edit, clone, refresh, enable/disable, convert between playlist and collection, delete. Bulk actions apply to the ticked lists. **Disabling** a list removes its Emby playlist or collection but keeps the definition; **enabling** recreates it.

## Refresh

- *On library changes*: items added, removed or updated trigger a refresh of lists whose rules can be affected.
- *On all changes*: also watch-state and favourite changes.
- *Schedules*: daily, weekly, monthly or interval refreshes per list.
- *Manual*: the *Refresh* menu item or *Refresh All Lists*; progress is on the **Status** tab.

Playlist refresh removes all entries and adds the new matches, so manual edits to a smart playlist are overwritten.

## Backups

*Settings → Backup & Restore*: create a backup now, or enable scheduled backups (daily at 03:00 by default; change the trigger under *Scheduled Tasks*). A backup is a zip of every list definition and its images. Restore from a listed backup or by uploading a zip.

## Differences from the Jellyfin plugin

| Area | On Emby |
|---|---|
| External lists (MDBList, Trakt, IMDb, ...) | Not available; hidden. |
| End-user page ("let users manage their own lists") | Not available; hidden. |
| Person fields | Actor, director, writer, producer, guest star, composer, conductor, lyricist only. |
| Video range type field | Not available (Emby exposes video range only). |
| Collection ordering | Emby has no "order added" display order; collections sort by Emby's own options. |
| Tab in the page URL | Not kept (Emby's router rebuilds the page on hash changes). |

## Known issues

- The *People* prefilter is off, so people rules scan item by item. Correct but slower on big libraries.
- Emby's own refresh after a list is created can briefly revert its sort title; the plugin re-applies it a few seconds later.
- Emby sometimes logs an `IOException` for `collection.nfo` right after a collection is created (Emby's own refresh racing the plugin's save). Non-fatal.
- Cleanup of leftover playlists and collections only touches items older than 40 hours.

## Field, operator and sorting reference

See [reference/README.md](reference/README.md). Those pages were written for the Jellyfin version; sections on external lists, the user page and some fields do not apply.
