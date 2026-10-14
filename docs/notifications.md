# Phone alerts (push notifications)

SmartLists can tell the Embidextrous Android app when something needs attention: a list failed to refresh, a refresh run finished, or the schedule timer stopped. This page is the contract as built (plugin 0.1.12). It follows the shared push contract that Label Maker and Hall Pass use, so the app has one parser, one catch-up routine and one settings screen for all of them.

## How it works

0. **Websocket subscription (what the app uses).** Emby links a websocket to a user session only when the token is in the websocket address (`?api_key=`). The app keeps its token in the `X-Emby-Token` header, so it is not an "administrator session" and `SendMessageToAdminSessions` never reaches it. The plugin therefore also offers a subscription on Emby's own websocket, like Hall Monitor's status push; see "Websocket subscription" below. The session push in step 1 stays (it still reaches Emby's own web admin sessions).
1. **Push.** The plugin sends a message to every connected **administrator** session with Emby's `ISessionManager.SendMessageToAdminSessions`. It is fire and forget: a failure is logged and never affects a refresh. A session that is not connected at that moment never sees the message, and a socket that signed in with only an API key receives nothing.
2. **Catch-up.** Every event gets a number (`Seq`) and is kept in a log of the last 500 events. The app asks `GET …/Events?After=<seq>` for what it missed and ignores numbers it already has.
3. **Catalog.** `GET …/Notifications/Catalog` describes each event so the app can build its rule screen from the server.

All routes need an administrator token in `X-Emby-Token`, like every other SmartLists route. The base is `<server>/emby/Plugins/SmartLists/` (see [api.md](api.md)); the paths below are relative to it.

## The messages

A message is `{"MessageType":"SmartLists.RefreshFailed","Data":{...}}`. Every `Data` starts with these common fields:

| Field | Meaning |
|---|---|
| `ApiLevel` | Level of this notification contract (1). Grows only when a field is removed or changes meaning. It is not the HTTP `ApiVersion`. |
| `Seq` | A whole number that grows by one per event. It is saved, so it survives restarts, and it never goes back (even after a damaged log file). |
| `EventId` | A GUID, unique per event. |
| `ServerTimeUtc` | ISO 8601, for example `2026-10-14T12:00:00Z`. |
| `Event` | The short name (`RefreshFailed`). |

| Message | Sent when | Event fields |
|---|---|---|
| `SmartLists.RefreshFailed` | A list refresh ended with an error. At most one alert per list per hour; lists that fail within 10 seconds of each other make one event. | `Count`, `Lists` (the first 5: `ListId`, `ListName`, `ListType` `Playlist`/`Collection`, `Error`, `Trigger` `Manual`/`Auto`/`Scheduled`) |
| `SmartLists.RefreshRunFinished` | A "Refresh all" run or a scheduled batch ended (two or more lists refreshed by hand or by schedule, then nothing running or queued for about 8 seconds). Refreshes caused by library changes do not count. | `Total`, `Succeeded`, `Failed`, `DurationSeconds` |
| `SmartLists.TimerStopped` | The schedule timer is not running (or is more than two hours late) while enabled lists have schedules. At most once a day. | `Message` |

A refresh that was cancelled, or that a client asked for wrongly (bad id, disabled list, unknown list), is not an alert. A failed refresh that was not alerted because the list already had an alert in the last hour is still in the Status page.

### Sample payloads

```json
{
  "MessageType": "SmartLists.RefreshFailed",
  "Data": {
    "ApiLevel": 1, "Seq": 12, "EventId": "5b0c2a3e-1d8f-4a66-9a52-0c6f0d5d7a11",
    "ServerTimeUtc": "2026-10-14T12:00:10Z", "Event": "RefreshFailed",
    "Count": 2,
    "Lists": [
      { "ListId": "fc34c420-ddaa-48c4-ba30-f3b6f82dc67f", "ListName": "Next Up", "ListType": "Playlist",
        "Error": "Error refreshing smart list: Object reference not set to an instance of an object.", "Trigger": "Scheduled" },
      { "ListId": "0a1b2c3d-1111-2222-3333-444455556666", "ListName": "Recent Movies", "ListType": "Collection",
        "Error": "The operation timed out.", "Trigger": "Scheduled" }
    ]
  }
}
```

```json
{
  "MessageType": "SmartLists.RefreshRunFinished",
  "Data": {
    "ApiLevel": 1, "Seq": 13, "EventId": "9d3f7e60-6a14-4c0e-8f0b-2a7c1e9b5d42",
    "ServerTimeUtc": "2026-10-14T12:01:00Z", "Event": "RefreshRunFinished",
    "Total": 14, "Succeeded": 13, "Failed": 1, "DurationSeconds": 42.5
  }
}
```

```json
{
  "MessageType": "SmartLists.TimerStopped",
  "Data": {
    "ApiLevel": 1, "Seq": 14, "EventId": "3c8e1b9a-7f20-4d35-b6a1-5e4d9c0f8a77",
    "ServerTimeUtc": "2026-10-14T14:05:00Z", "Event": "TimerStopped",
    "Message": "Scheduled list refreshes have stopped running. Restart the refresh timer from the SmartLists page or restart Emby."
  }
}
```

**No file paths.** Drive letters, share names and folder paths never appear in a payload. Lists are described by name and type only, and any sentence copied from an error has paths replaced by `[path]`, is cut to its first line and is at most 200 characters.

## Websocket subscription

Connect to `/embywebsocket` with the administrator's token in the `X-Emby-Token` header and `deviceId` in the address (no `api_key`), then send:

```json
{"MessageType":"SmartListsEventsStart","Data":"0,30000,12"}
```

`0,30000` are the usual delay and interval in milliseconds; the third value (`12` here) is the highest `Seq` the app has already seen and is optional. `{"MessageType":"SmartListsEventsStop"}` ends the subscription. The plugin answers with `SmartListsEvents` messages:

```json
{"MessageType":"SmartListsEvents","Data":{"ApiLevel":1,"LatestSeq":14,"Truncated":false,
  "Events":[{"Seq":13,"Name":"SmartLists.RefreshRunFinished","AtUtc":"2026-10-14T12:01:00Z","Data":{"...":"as pushed"}}]}}
```

- Once right after the start: the events after `afterSeq`, or an empty `Events` with only the `LatestSeq` baseline when no `afterSeq` was sent.
- Promptly (about a second) whenever a new event is logged, only to subscribed connections, and each connection only gets events above the highest `Seq` already sent on it (so nothing arrives twice).
- A heartbeat every 30 seconds: the same message with an empty `Events`, so the app can tell a dead connection from a quiet one.
- `Events` is oldest first, the same objects as the catch-up route; `Truncated` is true when the log no longer reaches back to `afterSeq`. An `afterSeq` higher than `LatestSeq` is treated as `LatestSeq`.
- Only an administrator may subscribe (checked on every push). Anyone else gets `{"MessageType":"SmartListsEvents","Data":{"Error":"Administrator login required."}}` once and nothing more.

The log of what the connection already has is per socket. The catch-up route stays for reconnects and as a backup poll.

## Flood control

- A failing list is announced once per hour, however often it fails. Lists that fail together (within 10 seconds) are one event with `Count` and the first five lists.
- A refresh run is one summary, not one message per list. A single list refreshed by hand is not a run.
- The stopped-timer alert repeats at most once a day.
- An event whose switch is off is neither sent nor logged, so the catch-up route never shows it. It does not use up the one-hour allowance of a list either.

## Settings that change what the server sends

On the plugin's **Settings** tab, section **Phone alerts**: a master switch **Send phone alerts** and one switch per event. Defaults: master on, `RefreshFailed` on, `TimerStopped` on, `RefreshRunFinished` off. They are saved in the plugin configuration as `SendPhoneAlerts`, `PhoneAlertRefreshFailed`, `PhoneAlertRefreshRunFinished` and `PhoneAlertTimerStopped`. Rules, thresholds, quiet hours and sound live in the app, per phone.

## Routes

### `GET …/Events?After=<seq>&Limit=100`

Events after a number, oldest first, from the saved log of the last 500 (`Limit` 1 to 500, default 100; `After` default 0).

```json
{
  "LatestSeq": 14,
  "Truncated": false,
  "Events": [
    { "Seq": 12, "Name": "SmartLists.RefreshFailed", "AtUtc": "2026-10-14T12:00:10Z", "Data": { "...": "the same object that was pushed" } }
  ]
}
```

`Truncated: true` means the log no longer reaches back to `After` (more than 500 events ago); the app should then read the Status route (`GET …/Status`) instead. If the plugin has not finished starting the route answers `503`.

### `GET …/Notifications/Catalog`

```json
{
  "ApiLevel": 1,
  "Service": { "Id": "SmartLists", "Title": "SmartLists", "ChannelId": "smart-lists", "ChannelTitle": "Smart lists" },
  "Events": [
    {
      "Id": "SmartLists.RefreshFailed",
      "Title": "A list refresh failed",
      "Description": "Sent when a smart list could not be refreshed. At most one alert per list per hour; lists that fail together make one alert.",
      "Params": [],
      "DefaultRule": { "Enabled": true, "Params": {} },
      "LockScreenText": "A smart list failed to refresh. Unlock to see which.",
      "OpensTab": "SmartLists/Status"
    }
  ]
}
```

The catalog lists the three events in the order `RefreshFailed`, `RefreshRunFinished`, `TimerStopped`. None has rule parameters. `DefaultRule.Enabled` matches the default switches above (`RefreshRunFinished` is false).

### `GET …/info`

Gains two properties and keeps everything else, including `ApiVersion: 1` (the app requires it to be equal):

```json
{ "Name": "SmartLists", "PluginVersion": "0.1.12.0", "ApiVersion": 1, "ApiLevel": 1, "Features": ["events", "catalog"] }
```

## Where it is wired

- `Notifications/` (new files; nothing else in the plugin depends on them): `NotificationAggregator` (the flood-control and batching rules, no Emby types), `EventLog` (sequence numbers, 500-event ring, a separate `.seq` file), `NotificationCatalog`, `NotificationModels`, `PlainJson`, and `NotificationService` (the Emby glue: timers, the push, the watchdog).
- `RefreshStatusService.CompleteOperation` is where every refresh outcome is recorded (queue, "Refresh all" and scheduled batches); it now raises an `OperationCompleted` event that the notification service listens to.
- `AutoRefreshService` records when its one-shot schedule timer is next due (`IsScheduleTimerStalled`). A watchdog checks every 5 minutes.
- `EventsPushListener` (an Emby `BasePeriodicWebSocketListener`, the pattern of Hall Monitor's `StatusPushListener`) and `EventsSubscription` (the Emby-free per-connection rules, unit tested). `NotificationService` calls `EventsPushListener.NotifyChanged()` after every logged event.
- `StartupEntryPoint` receives `ISessionManager` from Emby and starts the service after the auto-refresh service.
- The log lives in `<programdata>/config/SmartLists.Notifications/events.json` (with `events.json.seq`), outside the lists folder so it is never read as a list.

## Testing

Unit tests (`Emby.Plugin.SmartLists.Tests/Notifications/NotificationTests.cs`) cover the batching, the one-per-hour and once-a-day rules, the switches, path stripping, the log (restart, 500-event ring, truncation, damaged file), the catalog and the `info` additions. They do not talk to a running Emby.

Live check: `dev/Test-PhoneAlerts.ps1` connects the way the app does (token only in the `X-Emby-Token` header, `deviceId` in the address, no `api_key`), checks the baseline, the 30-second heartbeat, the catch-up route, the catalog, `info`, optionally a pushed event (`-ExpectEvent`) and the non-administrator refusal (`-NonAdminToken`). It has not been run yet. A test that connects with `api_key` proves nothing about the app.

Manual steps for the pieces the script cannot cause by itself:

1. Install the build, start Emby, sign in as an administrator, and open Emby's websocket the way the Embidextrous app does (token in the `X-Emby-Token` header) and send `SmartListsEventsStart`.
2. Make a list fail, for example by deleting the Emby playlist's owner user while keeping a scheduled list, or by stopping the library path a rule reads. Wait about 15 seconds: a `SmartLists.RefreshFailed` message arrives. Fail the same list again within the hour: nothing arrives.
3. Turn on **A refresh run finished**, save, and use **Refresh all** with at least two lists: one `SmartLists.RefreshRunFinished` arrives about 10 seconds after the last list.
4. `GET /emby/Plugins/SmartLists/Events?After=0` lists what was sent, with `Seq` growing by one. Restart Emby and check that the next `Seq` continues.
5. Turn off the master switch and repeat step 2: nothing arrives and `Events` does not grow.
6. `TimerStopped` is hard to provoke; the watchdog only acts when the timer object is missing or more than two hours late, so verify it by reading the code and the unit tests.
