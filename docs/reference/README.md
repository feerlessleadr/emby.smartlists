# Reference (carried over from the Jellyfin plugin)

The pages in `user-guide/` and `examples/` document fields, operators, sorting, scheduling, bumpers and example lists. They were written for the Jellyfin version and only mechanically adapted ("Jellyfin" → "Emby"), so read them with these differences in mind:

- **External lists** (the *External List* field, *External List Order* sort, MDBList/Trakt/IMDb/Letterboxd/ListenBrainz/Scrob examples) are not available on Emby and are hidden in the page.
- The **end-user page**, "Enable User Page" and user access control do not exist on Emby.
- **Person fields** are limited to actor, director, writer, producer, guest star, composer, conductor and lyricist; the *Video Range Type* field is not available.
- Statements about plugin installation, Jellyfin versions, "Plugin Pages" and "File Transformation" do not apply; use `../install.md`.
- Behaviour that depends on Jellyfin internals (collection display order, `Live TV Channel` handling, performance notes) has not been re-verified on Emby.

For what the Emby build actually supports, see `../using.md`.

- [user-guide/configuration.md](user-guide/configuration.md)
- [user-guide/fields-and-operators.md](user-guide/fields-and-operators.md)
- [user-guide/media-types.md](user-guide/media-types.md)
- [user-guide/sorting-and-limits.md](user-guide/sorting-and-limits.md)
- [user-guide/auto-refresh.md](user-guide/auto-refresh.md)
- [user-guide/bumpers.md](user-guide/bumpers.md)
- [user-guide/advanced-configuration.md](user-guide/advanced-configuration.md)
- [user-guide/user-selection.md](user-guide/user-selection.md)
- [examples/common-use-cases.md](examples/common-use-cases.md)
- [examples/advanced-examples.md](examples/advanced-examples.md)
