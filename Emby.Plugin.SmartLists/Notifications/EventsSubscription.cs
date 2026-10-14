using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Emby.Plugin.SmartLists.Notifications
{
    /// <summary>
    /// What one websocket connection that sent <c>SmartListsEventsStart</c> has been told so far, and what to send it next. Emby-free so the
    /// rules are unit tested: the first push is the events after the number the app gave (or only the latest number when it gave none), later
    /// pushes carry only events above the highest number already sent on this connection, an empty list is the heartbeat, and anyone who is not
    /// an administrator gets one error message and nothing more.
    /// </summary>
    public sealed class EventsSubscription
    {
        /// <summary>The text sent to a caller that is not an administrator.</summary>
        public const string AdministratorRequired = "Administrator login required.";

        private const int MaxEventsPerPush = 500;

        private bool _started;
        private bool _refused;
        private long _lastSent;

        /// <summary>Gets or sets the highest <c>Seq</c> the app has already seen (the optional third value of the start message).</summary>
        public long? AfterSeq { get; set; }

        /// <summary>Gets the highest <c>Seq</c> already sent on this connection.</summary>
        public long LastSentSeq => _lastSent;

        /// <summary>Reads the optional <c>afterSeq</c> value of the start message; anything that is not a whole number is ignored.</summary>
        /// <param name="values">The values after the delay and the interval.</param>
        public void ParseStartValues(string[] values)
        {
            AfterSeq = null;
            if (values.Length > 0 && long.TryParse(values[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var after))
            {
                AfterSeq = after;
            }
        }

        /// <summary>The message data to send now, or null when nothing is to be sent.</summary>
        /// <param name="log">The event log (null before the plugin has started).</param>
        /// <param name="isAdministrator">Whether the connection's user is an administrator right now.</param>
        /// <returns>The data object (events, heartbeat or error), or null.</returns>
        public Dictionary<string, object?>? Next(EventLog? log, bool isAdministrator)
        {
            if (!isAdministrator)
            {
                if (_refused)
                {
                    return null;
                }

                _refused = true;
                return new Dictionary<string, object?> { ["Error"] = AdministratorRequired };
            }

            _refused = false;
            if (log == null)
            {
                return null;
            }

            var latest = log.LatestSeq;
            if (!_started)
            {
                _started = true;
                _lastSent = AfterSeq ?? latest;
            }

            if (_lastSent > latest)
            {
                _lastSent = latest;
            }

            var data = EventsPayload.Build(log, _lastSent, MaxEventsPerPush);
            var events = (List<Dictionary<string, object?>>)data["Events"]!;
            if (events.Count > 0)
            {
                _lastSent = (long)events[^1]["Seq"]!;
            }

            data["ApiLevel"] = EventNames.ApiLevel;
            return data;
        }
    }

    /// <summary>Builds the <c>{ LatestSeq, Truncated, Events }</c> object shared by the catch-up route and the websocket push.</summary>
    public static class EventsPayload
    {
        public static Dictionary<string, object?> Build(EventLog log, long after, int limit)
        {
            var latest = log.LatestSeq;
            var events = log.After(after, limit, out var truncated);
            return new Dictionary<string, object?>
            {
                ["LatestSeq"] = latest,
                ["Truncated"] = truncated,
                ["Events"] = events.Select(e => new Dictionary<string, object?>
                {
                    ["Seq"] = e.Seq,
                    ["Name"] = e.Name,
                    ["AtUtc"] = e.AtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                    ["Data"] = PlainJson.ToPlain(e.Data),
                }).ToList(),
            };
        }
    }
}
