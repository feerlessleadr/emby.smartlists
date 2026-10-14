using System.Collections.Generic;

namespace Emby.Plugin.SmartLists.Notifications
{
    /// <summary>
    /// The list of events with their titles, parameters, suggested rules and lock-screen texts, so the phone app can build its rule screen from the
    /// server (docs/notifications.md). Plain dictionaries and lists, so any JSON writer produces the same shape.
    /// </summary>
    public static class NotificationCatalog
    {
        public static Dictionary<string, object?> Build()
        {
            return new Dictionary<string, object?>
            {
                ["ApiLevel"] = EventNames.ApiLevel,
                ["Service"] = new Dictionary<string, object?>
                {
                    ["Id"] = "SmartLists",
                    ["Title"] = "SmartLists",
                    ["ChannelId"] = "smart-lists",
                    ["ChannelTitle"] = "Smart lists",
                },
                ["Events"] = new List<object>
                {
                    Entry(
                        EventNames.RefreshFailed,
                        "A list refresh failed",
                        "Sent when a smart list could not be refreshed. At most one alert per list per hour; lists that fail together make one alert.",
                        true,
                        "A smart list failed to refresh. Unlock to see which.",
                        "SmartLists/Status"),
                    Entry(
                        EventNames.RefreshRunFinished,
                        "A refresh run finished",
                        "Sent when a Refresh all or a scheduled batch of lists has finished, with how many worked and how many failed.",
                        false,
                        "Smart lists finished refreshing. Unlock to see the result.",
                        "SmartLists/Status"),
                    Entry(
                        EventNames.TimerStopped,
                        "The refresh timer stopped",
                        "Sent when scheduled refreshes have stopped running although lists have schedules. At most once a day.",
                        true,
                        "Scheduled list refreshes have stopped. Unlock for details.",
                        "SmartLists/Status"),
                },
            };
        }

        private static Dictionary<string, object?> Entry(string shortName, string title, string description, bool enabled, string lockScreenText, string opensTab) => new()
        {
            ["Id"] = EventNames.Full(shortName),
            ["Title"] = title,
            ["Description"] = description,
            ["Params"] = new List<object>(),
            ["DefaultRule"] = new Dictionary<string, object?> { ["Enabled"] = enabled, ["Params"] = new Dictionary<string, object?>() },
            ["LockScreenText"] = lockScreenText,
            ["OpensTab"] = opensTab,
        };
    }
}
