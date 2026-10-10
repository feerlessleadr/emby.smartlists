# SmartLists HTTP API

The plugin's admin page and the Emby SmartLists Android app both talk to this API. It is the same API in both cases: there is no separate "app API". This document is the contract. `ApiVersion` (see `GET /info`) changes only when an existing client would break; new endpoints and new properties do not change it.

## Basics

- **Base URL:** `<server>/emby/Plugins/SmartLists/` (Emby also serves it without the `/emby` prefix: `<server>/Plugins/SmartLists/`). Examples below write `…/` for the base.
- **Authentication:** every call needs an Emby access token of an **administrator**, sent as the header `X-Emby-Token: <token>`. (Do not put the token in the URL: it ends up in proxy logs.) No token or a bad token gives `401`; a valid token of a non-administrator gives `403`.
- **Getting a token:** either an Emby API key (Dashboard, Advanced, Api Keys), or sign in as the administrator:
  `POST /emby/Users/AuthenticateByName` with the header
  `X-Emby-Authorization: MediaBrowser Client="Emby SmartLists", Device="<device name>", DeviceId="<stable id>", Version="<app version>"`
  and the JSON body `{"Username": "...", "Pw": "..."}`. The response has `AccessToken` and `User`.
- **Format:** JSON, property names exactly as shown (PascalCase). Request bodies need `Content-Type: application/json`. A smart list `Id` is a GUID with dashes (`fc34c420-ddaa-48c4-ba30-f3b6f82dc67f`); Emby user ids are written without dashes (`187e8098a0404bf1a42200bcf6f9d29f`). Emby's own ids (`PlaylistId`, library `Id`) are numbers written as strings. Dates are ISO 8601 (UTC).
- **Errors:** a failed call from the plugin carries `{"title": "Validation Error", "status": 400, "detail": "List name cannot be empty"}`; `detail` is the message to show a person. `400` is a validation problem (also a malformed id or body), `404` an unknown list (the body may be empty), `500` an unexpected server error. A `401` comes from Emby itself and is plain text (`Access token is invalid or expired.`).
- **Transport:** use HTTPS. A token can do everything an administrator can.

## Compatibility check

`GET …/info`

```json
{ "Name": "SmartLists", "PluginVersion": "0.1.9.0", "ApiVersion": 1 }
```

Call it first. A client built for API version N works with any plugin whose `ApiVersion` is N. If it is different, tell the user to update the app or the plugin.

## Rule-builder catalog

`GET …/catalog` returns what a client needs to build a rule editor without hard-coding anything:

| Property | Meaning |
|---|---|
| `Fields[]` | Every rule field: `Name`, `Label`, `Category` (Content, Video, Audio, RatingsPlayback, File, Library, People, PeopleSubFields, Collection), `Type`, `Operators[]`, `UserSpecific`, `Values` |
| `Operators[]` | `Value` and `Label` for every operator |
| `Sorts[]` | `Value`, `Label`, `Group`, `Directionless` (no ascending/descending choice), `RoundRobin` (needs a group-by field) |
| `RoundRobinGroupByFields[]`, `WithinGroupOrders[]` | Choices for round-robin sorts |
| `MediaTypes[]` | Values for a list's `MediaTypes` |
| `AutoRefreshModes[]` | Values for `AutoRefresh` |
| `RelativeDateUnits[]`, `Weekdays[]` | Choices for date rules |

`Type` tells a client which input to show: `Text` (free text), `Numeric`, `Date`, `Boolean` (value `true` or `false`), `List` (a field that holds several values, such as genres or tags), `UserData` (the current user's play data, here `PlaybackStatus`), `Simple`, `Resolution`, `AspectRatio`, `Framerate`, `Similarity` (`SimilarTo`). `Values` is a list of `{Value, Label}` when the field has fixed choices (`PlaybackStatus`, `SeriesStatus`, `ExtraType`, `Resolution`) and `null` otherwise. The older `GET …/fields` returns a similar but flatter structure and is kept for the web page.

### How a rule's `TargetValue` is written

| Field type / operator | `TargetValue` |
|---|---|
| Text, `Equal`, `Contains`, `MatchRegex`... | the text (a .NET regex for `MatchRegex`) |
| `IsIn` / `IsNotIn` | values separated by `;`. For `LibraryName` each value is a **whole** library name (case-insensitive); for other fields a value matches if the field contains it |
| Numeric | the number, as text |
| Boolean | `true` or `false` |
| Date with `After`, `Before`, `Equal`, `NotEqual` | `yyyy-MM-dd` |
| Date with `NewerThan`, `OlderThan` | `number:unit`, for example `30:days` (units from `RelativeDateUnits`) |
| Date with `Weekday` | `0` (Sunday) to `6` (Saturday) |
| `PlaybackStatus` | `Played`, `InProgress` or `Unplayed` |
| `Resolution` | `480p`, `720p`, `1080p`, `1440p`, `4K`, `8K` |
| `AspectRatio` | `width:height`, for example `16:9` |

## Smart lists

A smart list is either a playlist or a collection (`Type`). The same JSON shape is used to read, create and update.

| Call | What it does |
|---|---|
| `GET …/` | All smart lists (array). `?type=Playlist` or `?type=Collection` filters |
| `GET …/{id}` | One smart list |
| `POST …/` | Create (`201` and the saved list). `?skipRefresh=true` skips the first refresh |
| `PUT …/{id}` | Replace a list with the body (`200` and the saved list). `?skipRefresh=true` supported |
| `DELETE …/{id}` | Delete (`204`). `?deleteEmbyList=true` (default) also deletes the Emby playlist/collection; `false` keeps it |
| `POST …/{id}/enable`, `POST …/{id}/disable` | Enable (creates and fills the Emby list) or disable (removes the Emby list, keeps the definition) |
| `POST …/{id}/refresh` | Queue a refresh of one list (`200`; it runs in the background) |
| `POST …/refresh` | Queue a refresh of every list |

### The smart list object

Main properties (read-only ones are ignored on write):

| Property | Type | Notes |
|---|---|---|
| `Type` | `"Playlist"` or `"Collection"` | Required on create |
| `Id` | string | Read-only; generated on create |
| `Name` | string | Required. Up to 500 characters |
| `Enabled` | bool | Default `true` |
| `UserId` | string | Playlists: the user the list is for (a playlist needs at least one user: `UserId`, `UserPlaylists` or `AllUsers`, otherwise `400`). Collections: the user whose play data rules use |
| `UserPlaylists` | `[{UserId, PlaylistId}]` | Playlists: which users get a playlist (`PlaylistId` is read-only) |
| `Public`, `AllUsers` | bool | Playlists: shared with everyone / one playlist per user |
| `MediaTypes` | string[] | Values from the catalog, for example `["Episode"]` |
| `ExpressionSets` | see below | The rules |
| `Order` | `{ "SortOptions": [...] }` | Up to 3 sort options, see below |
| `MaxItems`, `MaxPlayTimeMinutes` | int or null | Limits |
| `MinItems` | int or null | The Emby list exists only when at least this many items match |
| `AutoRefresh` | `"Never"`, `"OnLibraryChanges"`, `"OnAllChanges"` | |
| `Schedules`, `VisibilitySchedules` | array | Refresh/visibility schedules (see the web page; advanced) |
| `SortTitle`, `Overview`, `Tags`, `Favorite` | optional | Metadata applied to the Emby list |
| `IncludeExtras`, `MatchByMembers`, `GroupIntoCollections` | bool | Advanced; see the user guide |
| `LastRefreshed`, `DateCreated`, `ItemCount`, `TotalRuntimeMinutes`, `CreatedByUserId`, `PlaylistId`/`CollectionId`, `FileName` | | Read-only |

**Rules** (`ExpressionSets`) are groups. Rules inside a group are combined with AND, groups are combined with OR:

```json
"ExpressionSets": [
  { "Expressions": [
      { "MemberName": "NextUnwatched", "Operator": "Equal", "TargetValue": "true", "IncludeUnwatchedSeries": false },
      { "MemberName": "LibraryName", "Operator": "IsIn", "TargetValue": "TV; Anime" }
  ], "MaxItems": null }
]
```

A rule is `MemberName` (a field `Name` from the catalog), `Operator`, `TargetValue`, and optional extras that apply to certain fields: `UserId` (evaluate a user-specific field for another user), `IncludeUnwatchedSeries` (`NextUnwatched`; default `true` when omitted), `IncludeUnknownDates` (dates that can be missing), `RuntimeUnit` (`"seconds"` for `RuntimeMinutes`), `OnlyDefaultAudioLanguage`, `CollectionSearchDepth`, and the parent-tag/studio/genre switches (`OnlyParentTags`, `OnlyParentStudios`, `OnlyParentGenres`, `IncludeParentTags`, `IncludeParentStudios`, `IncludeParentGenres`, `IncludeEpisodesWithinSeries`).

**Sorts:** `{ "SortBy": "<Value from catalog Sorts>", "SortOrder": "Ascending" | "Descending" }`. Directionless sorts ignore `SortOrder`. Round-robin sorts also need `"GroupByField"` (from `RoundRobinGroupByFields`) and may set `"WithinGroupOrder"` (`Natural` or `AirDate`).

### Minimal create example

```http
POST /emby/Plugins/SmartLists/
X-Emby-Token: <token>
Content-Type: application/json

{
  "Type": "Playlist",
  "Name": "Next Up",
  "UserId": "187e8098a0404bf1a42200bcf6f9d29f",
  "MediaTypes": ["Episode"],
  "ExpressionSets": [{ "Expressions": [
    { "MemberName": "NextUnwatched", "Operator": "Equal", "TargetValue": "true", "IncludeUnwatchedSeries": false }
  ]}],
  "Order": { "SortOptions": [
    { "SortBy": "Most Recently Watched Round Robin", "SortOrder": "Ascending", "GroupByField": "SeriesName" }
  ]},
  "AutoRefresh": "OnAllChanges"
}
```

## Lookups

| Call | Returns |
|---|---|
| `GET …/users` | `[{Id, Name}]`, the Emby users a list can belong to |
| `GET …/libraries` | `[{Id, Name, CollectionType}]` |
| `GET …/currentuser` | The signed-in user (`400` when called with an API key, which has no user) |

## Status

| Call | Returns |
|---|---|
| `GET …/Status` | `{ongoingOperations[], history[], statistics{totalLists, ongoingOperationsCount, queuedOperationsCount, lastRefreshTime, averageRefreshDuration, successfulRefreshes, failedRefreshes}}` |
| `GET …/Status/Ongoing` | Refreshes running or queued now |
| `GET …/Status/History` | Recent refreshes |
| `GET …/Timer/Status` | The schedule timer (`isRunning`, `nextScheduledCheck`); `POST …/Timer/Restart` restarts it |

## Images and backups (not needed by a basic client)

- Images: `GET …/{id}/images`, `POST …/{id}/images` (multipart: `file`, `imageType`), `DELETE …/{id}/images/{imageType}`, `GET …/{id}/images/{imageType}/file` (needs the token header). Allowed: JPEG, PNG, GIF, WebP, BMP, AVIF, APNG, ICO.
- Backups: `GET …/backups`, `POST …/backups`, `GET/DELETE …/backups/{filename}`, `POST …/backups/{filename}/restore?overwrite=`, `POST …/backups/upload` and `…/backups/preview` (multipart).

## Changes

- **API version 1** (plugin 0.1.9): documented; added `GET /info` and `GET /catalog`. Everything else already existed (it is what the web page uses).
