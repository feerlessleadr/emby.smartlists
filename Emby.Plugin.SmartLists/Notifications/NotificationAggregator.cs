using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Emby.Plugin.SmartLists.Notifications
{
    /// <summary>
    /// Decides which events are worth a phone alert and batches them (docs/notifications.md): one failure alert per list per hour, several lists
    /// that fail together make one event, one summary per refresh run, and the stopped-timer alert at most once a day. It knows nothing about Emby:
    /// it is told how refreshes ended and what time it is, and it hands back the events to send. Nothing here sends or stores anything.
    /// </summary>
    public sealed class NotificationAggregator
    {
        /// <summary>The most lists described inside one RefreshFailed event.</summary>
        public const int MaxListsInEvent = 5;

        /// <summary>The longest error text sent for one list.</summary>
        public const int MaxErrorLength = 200;

        /// <summary>A list that failed gets no second failure alert for this long.</summary>
        public static readonly TimeSpan PerListInterval = TimeSpan.FromHours(1);

        /// <summary>Failures that arrive within this time of the first one go into the same event.</summary>
        public static readonly TimeSpan FailureBatchWindow = TimeSpan.FromSeconds(10);

        /// <summary>A run is finished when nothing has completed for this long and nothing is queued or running.</summary>
        public static readonly TimeSpan RunQuietWindow = TimeSpan.FromSeconds(8);

        /// <summary>The stopped-timer alert is repeated no sooner than this.</summary>
        public static readonly TimeSpan TimerInterval = TimeSpan.FromDays(1);

        // Refreshes that end in "failed" only because the request itself was wrong or the owner cancelled: not an alert.
        private static readonly string[] IgnoredErrors =
        {
            "Operation was cancelled",
            "Invalid list ID format",
            "Cannot refresh disabled list",
            "Smart list not found",
        };

        // A Windows drive path or UNC path up to the next quote or end of line, or an absolute Unix path of two or more parts.
        private static readonly Regex WindowsPath = new(@"(?:[A-Za-z]:[\\/]|\\\\)[^""'<>|*?\r\n]*", RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromSeconds(1));
        private static readonly Regex UnixPath = new(@"(?<![\w:/])/(?:[^\s/""'<>|*?:]+/)+[^\s/""'<>|*?:]*", RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

        private readonly Func<NotificationSwitches> _switches;
        private readonly Func<DateTime> _utcNow;
        private readonly object _lock = new();
        private readonly Dictionary<string, DateTime> _lastFailureAlert = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<RefreshOutcome> _pendingFailures = new();
        private DateTime _pendingSince;
        private RunState? _run;
        private DateTime? _lastTimerAlert;

        public NotificationAggregator(Func<NotificationSwitches> switches, Func<DateTime>? utcNow = null)
        {
            _switches = switches;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        /// <summary>
        /// Removes drive letters, share names and folder paths from a sentence (an exception message, a list name) and shortens it. Everything
        /// that looks like a path becomes "[path]".
        /// </summary>
        /// <param name="text">The sentence.</param>
        /// <returns>The sentence without paths, on one line, at most <see cref="MaxErrorLength"/> characters.</returns>
        public static string StripPaths(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var line = text.Trim().Split('\n', 2)[0].Trim();
            try
            {
                line = WindowsPath.Replace(line, "[path]");
                line = UnixPath.Replace(line, "[path]");
            }
            catch (RegexMatchTimeoutException)
            {
                return "An error occurred (details are in the server log).";
            }

            return line.Length > MaxErrorLength ? line[..(MaxErrorLength - 3)] + "..." : line;
        }

        /// <summary>Tells whether a failed refresh is worth an alert (a cancelled refresh or a bad request is not).</summary>
        /// <param name="outcome">How the refresh ended.</param>
        /// <returns>True for a real failure.</returns>
        public static bool IsRealFailure(RefreshOutcome outcome)
        {
            if (outcome.Success)
            {
                return false;
            }

            var error = outcome.Error ?? string.Empty;
            return !IgnoredErrors.Any(i => string.Equals(error, i, StringComparison.Ordinal));
        }

        /// <summary>Records how one list refresh ended. Nothing is sent yet: <see cref="Tick"/> hands the batched events back.</summary>
        /// <param name="outcome">How the refresh ended.</param>
        public void OnRefreshCompleted(RefreshOutcome outcome)
        {
            var failed = !outcome.Success;
            if (failed && !IsRealFailure(outcome))
            {
                return;
            }

            lock (_lock)
            {
                var now = _utcNow();
                var switches = _switches();

                if (failed && switches.IsOn(EventNames.RefreshFailed))
                {
                    var key = outcome.ListId;
                    if (!_lastFailureAlert.TryGetValue(key, out var last) || now - last >= PerListInterval)
                    {
                        _lastFailureAlert[key] = now;
                        if (_pendingFailures.Count == 0)
                        {
                            _pendingSince = now;
                        }

                        _pendingFailures.Add(outcome);
                    }
                }

                // A run is the manual "refresh all" or a scheduled batch; refreshes caused by library changes do not count.
                if (switches.IsOn(EventNames.RefreshRunFinished) && !string.Equals(outcome.Trigger, "Auto", StringComparison.OrdinalIgnoreCase))
                {
                    _run ??= new RunState { FirstStart = outcome.StartUtc };
                    _run.Total++;
                    if (outcome.Success)
                    {
                        _run.Succeeded++;
                    }
                    else
                    {
                        _run.Failed++;
                    }

                    if (outcome.StartUtc < _run.FirstStart)
                    {
                        _run.FirstStart = outcome.StartUtc;
                    }

                    if (outcome.EndUtc > _run.LastEnd)
                    {
                        _run.LastEnd = outcome.EndUtc;
                    }

                    _run.LastCompletion = now;
                }
            }
        }

        /// <summary>Called every few seconds: returns the events that are ready to send now.</summary>
        /// <param name="refreshesIdle">True when no refresh is running or queued.</param>
        /// <returns>The events, possibly none.</returns>
        public IReadOnlyList<PendingEvent> Tick(bool refreshesIdle)
        {
            var ready = new List<PendingEvent>();
            lock (_lock)
            {
                var now = _utcNow();
                var switches = _switches();

                if (_pendingFailures.Count > 0 && now - _pendingSince >= FailureBatchWindow)
                {
                    var batch = _pendingFailures.ToList();
                    _pendingFailures.Clear();
                    if (switches.IsOn(EventNames.RefreshFailed))
                    {
                        ready.Add(BuildFailure(batch));
                    }
                }

                if (_run != null && refreshesIdle && now - _run.LastCompletion >= RunQuietWindow)
                {
                    var run = _run;
                    _run = null;

                    // One refresh is not a run: the owner sees it in the list. Two or more is a "refresh all" or a scheduled batch.
                    if (run.Total >= 2 && switches.IsOn(EventNames.RefreshRunFinished))
                    {
                        ready.Add(new PendingEvent(EventNames.RefreshRunFinished, new Dictionary<string, object?>
                        {
                            ["Total"] = run.Total,
                            ["Succeeded"] = run.Succeeded,
                            ["Failed"] = run.Failed,
                            ["DurationSeconds"] = Math.Round(Math.Max(0, (run.LastEnd - run.FirstStart).TotalSeconds), 1),
                        }));
                    }
                }
            }

            return ready;
        }

        /// <summary>The refresh timer is not running although lists have schedules. Returns the event unless one went out in the last day.</summary>
        /// <param name="message">A plain sentence for the phone.</param>
        /// <returns>The event, or null.</returns>
        public PendingEvent? OnTimerStopped(string message)
        {
            lock (_lock)
            {
                var now = _utcNow();
                if (!_switches().IsOn(EventNames.TimerStopped) || (_lastTimerAlert != null && now - _lastTimerAlert.Value < TimerInterval))
                {
                    return null;
                }

                _lastTimerAlert = now;
                return new PendingEvent(EventNames.TimerStopped, new Dictionary<string, object?> { ["Message"] = StripPaths(message) });
            }
        }

        private static PendingEvent BuildFailure(List<RefreshOutcome> batch)
        {
            var lists = batch.Take(MaxListsInEvent).Select(o => new Dictionary<string, object?>
            {
                ["ListId"] = o.ListId,
                ["ListName"] = StripPaths(o.ListName),
                ["ListType"] = o.ListType,
                ["Error"] = ErrorText(o.Error),
                ["Trigger"] = o.Trigger,
            }).ToList();

            return new PendingEvent(EventNames.RefreshFailed, new Dictionary<string, object?>
            {
                ["Count"] = batch.Count,
                ["Lists"] = lists,
            });
        }

        private static string ErrorText(string? error)
        {
            var text = StripPaths(error);
            return text.Length == 0 ? "The refresh did not finish." : text;
        }

        private sealed class RunState
        {
            public int Total { get; set; }

            public int Succeeded { get; set; }

            public int Failed { get; set; }

            public DateTime FirstStart { get; set; }

            public DateTime LastEnd { get; set; }

            public DateTime LastCompletion { get; set; }
        }
    }
}
