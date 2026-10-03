# SmartLists for Emby

Rule-based **playlists and collections** for Emby Server. You pick the media types and define the rules in a point-and-click rule builder (for example: genre contains *Action*, release year between 1990 and 1999, playback status is *Unplayed*, sorted by release date, newest first). The plugin then builds the list and keeps it up to date as your library and watch history change. Rules are written by you; nothing is generated from free-text descriptions. A few built-in templates give you a starting point.

This is a port of the [Jellyfin SmartLists plugin](https://github.com/jyourstone/jellyfin-smartlists-plugin) to **Emby Server 4.10.1.0**. Jellyfin is no longer supported by this repository, and upstream changes are not tracked.

## What works

- **Rule builder** with grouped AND/OR rules over genres, ratings, dates, play state, people, tags, studios, resolution, runtime and many more fields. Sorting (up to three levels), item and runtime limits, random selection.
- **Smart playlists** (one per chosen user, or for all users) and **smart collections**.
- **Automatic refresh** when items are added, removed or updated, and when watch state changes. Schedules (daily, weekly, ...) and manual refresh from the page or in bulk.
- **Admin page** under *Dashboard → SmartLists*: create, edit, clone, enable/disable, delete, status and history, templates.
- **Custom cover images**, sort titles, descriptions and tags on the generated lists.
- **Backups**: manual or scheduled zip of all list definitions and images, with restore.
- Scheduled tasks for backups and clean-up of leftovers appear under *Dashboard → Scheduled Tasks*.

## What does not (yet)

- External lists (MDBList, Trakt, IMDb, ...), and the end-user page. Both are hidden in the UI.
- Fields Emby cannot supply: person roles other than actor, director, writer, producer, guest star, composer, conductor and lyricist, and video range type.
- The *People* prefilter is switched off, so people rules are correct but evaluated item by item (slower on very large libraries).

See [docs/using.md](docs/using.md) for details and known quirks.

## Install

1. Download `Emby.Plugin.SmartLists-<version>.zip` (or build it, see [docs/development.md](docs/development.md)).
2. Stop Emby, extract the two DLLs into the server's `plugins` folder (`<programdata>/plugins`), start Emby.
3. Open *Dashboard* and click **SmartLists** in the left menu.

Full steps and troubleshooting: [docs/install.md](docs/install.md).

## Documentation

| | |
|---|---|
| [docs/install.md](docs/install.md) | Install, upgrade, uninstall |
| [docs/using.md](docs/using.md) | Using the plugin, differences from the Jellyfin version, known issues |
| [docs/security.md](docs/security.md) | What the plugin exposes, security tests run, residual risks, hardening tips |
| [docs/development.md](docs/development.md) | Build, test, deploy and verify against a local Emby |
| [docs/reference/](docs/reference/README.md) | Field, operator, sorting and scheduling reference carried over from the Jellyfin version |
| [docs/port/](docs/port/status.md) | How the port was done: decisions, verified Emby API behaviour, history |

## License

See [LICENSE](LICENSE).
