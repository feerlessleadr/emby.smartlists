# SmartLists for Emby

Rule-based **playlists and collections** for Emby Server. You pick the media types and define the rules in a point-and-click rule builder (for example: genre contains *Action*, release year between 1990 and 1999, playback status is *Unplayed*, sorted by release date, newest first). The plugin then builds the list and keeps it up to date as your library and watch history change. Rules are written by you; nothing is generated from free-text descriptions. A few built-in templates give you a starting point.

This is a port of the [Jellyfin SmartLists plugin](https://github.com/jyourstone/jellyfin-smartlists-plugin) to **Emby Server 4.10.1.0**. Jellyfin is no longer supported by this repository, and upstream changes are not tracked.

## AI disclosure and disclaimer

This plugin was ported from the Jellyfin SmartLists plugin to Emby **exclusively with Claude**, Anthropic's AI model, used through Claude Code. The code, tests and documentation in this repository were produced by that AI; the repository owner directed the work and ran it against test Emby servers.

The software is provided **as is**, with no warranty of any kind. The repository owner takes **no responsibility for mistakes, errors, data loss or other problems** in the code, whether introduced by the AI or otherwise, or for any harm to your server, library or data from installing or using it.

**If you are not comfortable with that, do not use this plugin.** If you do use it, back up your Emby data first (see [docs/install.md](docs/install.md)), try it on a test server before a production one, and read [docs/security.md](docs/security.md) for what was and was not tested.

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
2. Stop Emby, place `Emby.Plugin.SmartLists.dll` in the server's `plugins` folder (`<programdata>/plugins`), start Emby.
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

## 🙏 Credits

The idea for this plugin, and much of its design, belong to **[jyourstone](https://github.com/jyourstone)**, the author of the [Jellyfin SmartLists plugin](https://github.com/jyourstone/jellyfin-smartlists-plugin). The rule builder, the query engine, the sorting, the admin page and its layout, the templates, the refresh model and much of the documentation in this repository come from that project; this repository adapts it to Emby. Please go and look at, use and support the original.

That plugin is in turn based on the original SmartPlaylist plugin by **[ankenyr](https://github.com/ankenyr)** ([original repository](https://github.com/ankenyr/jellyfin-smartplaylist-plugin)), to whom jyourstone credits the foundational work and the core idea.

## License

Licensed under the **GNU Affero General Public License v3.0** (AGPL-3.0), the same license as the project this is derived from. See [LICENSE](LICENSE). In practice: you may use, modify and share it, but a modified version you distribute, or let others use over a network, must also be AGPL-3.0 and its source must be made available to those users. This repository is the source for this version, and the page's Help link points here.

This is an independent, unofficial plugin. It is not affiliated with, endorsed by or supported by Emby, or by the authors credited above. "Emby" is a trademark of its owner.
