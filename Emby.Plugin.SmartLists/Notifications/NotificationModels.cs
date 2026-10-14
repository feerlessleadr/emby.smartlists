using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Emby.Plugin.SmartLists.Notifications
{
    /// <summary>
    /// The message names the phone app listens for (docs/notifications.md). The full name is <c>SmartLists.</c> plus the short name.
    /// </summary>
    public static class EventNames
    {
        public const string Prefix = "SmartLists.";
        public const string RefreshFailed = "RefreshFailed";
        public const string RefreshRunFinished = "RefreshRunFinished";
        public const string TimerStopped = "TimerStopped";

        /// <summary>
        /// The level of the notification contract (not the HTTP API version). Grows only when a field is removed or changes meaning.
        /// </summary>
        public const int ApiLevel = 1;

        public static string Full(string shortName) => Prefix + shortName;
    }

    /// <summary>
    /// The "Send phone alerts" switches on the plugin page: a master switch and one per event. An event that is off is neither sent nor logged.
    /// </summary>
    public sealed class NotificationSwitches
    {
        public bool SendPhoneAlerts { get; set; } = true;

        public bool RefreshFailed { get; set; } = true;

        public bool RefreshRunFinished { get; set; }

        public bool TimerStopped { get; set; } = true;

        /// <summary>
        /// Whether an event may be sent and logged right now.
        /// </summary>
        /// <param name="shortName">The short event name.</param>
        /// <returns>True when the master switch and the event's own switch are on.</returns>
        public bool IsOn(string shortName)
        {
            if (!SendPhoneAlerts)
            {
                return false;
            }

            return shortName switch
            {
                EventNames.RefreshFailed => RefreshFailed,
                EventNames.RefreshRunFinished => RefreshRunFinished,
                EventNames.TimerStopped => TimerStopped,
                _ => false,
            };
        }
    }

    /// <summary>An event the aggregator decided to send, before it has a sequence number.</summary>
    public sealed class PendingEvent
    {
        public PendingEvent(string shortName, Dictionary<string, object?> fields)
        {
            Event = shortName;
            Fields = fields;
        }

        public string Event { get; }

        public Dictionary<string, object?> Fields { get; }
    }

    /// <summary>An event as sent and logged: the full message name, the time and the whole payload (common fields included).</summary>
    public sealed class NotificationEvent
    {
        public long Seq { get; set; }

        /// <summary>Gets or sets the message name, for example SmartLists.RefreshFailed.</summary>
        public string Name { get; set; } = string.Empty;

        public DateTime AtUtc { get; set; }

        public JsonElement Data { get; set; }
    }

    /// <summary>How one list refresh ended, as the status service records it. The notification code reads this and nothing else.</summary>
    public sealed class RefreshOutcome
    {
        public string ListId { get; set; } = string.Empty;

        public string ListName { get; set; } = string.Empty;

        /// <summary>Gets or sets "Playlist" or "Collection".</summary>
        public string ListType { get; set; } = string.Empty;

        /// <summary>Gets or sets "Manual", "Auto" or "Scheduled".</summary>
        public string Trigger { get; set; } = string.Empty;

        public bool Success { get; set; }

        public string? Error { get; set; }

        public DateTime StartUtc { get; set; }

        public DateTime EndUtc { get; set; }
    }
}
