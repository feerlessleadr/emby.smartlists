using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugin.SmartLists.Configuration;
using Emby.Plugin.SmartLists.Host;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Logging;

namespace Emby.Plugin.SmartLists.Notifications
{
    /// <summary>
    /// Connects the Emby-free notification logic to Emby: listens for finished refreshes, watches the schedule timer, keeps the numbered event log and
    /// pushes each event to the connected administrator sessions (<see cref="ISessionManager.SendMessageToAdminSessions{T}"/>). Sending is fire and
    /// forget: a failure is logged and never reaches a refresh.
    /// </summary>
    public sealed class NotificationService : IDisposable
    {
        private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan WatchdogInterval = TimeSpan.FromMinutes(5);

        // A one-shot schedule timer that is later than this has not re-armed itself. A long refresh batch delays the re-arm, so be generous.
        private static readonly TimeSpan TimerGrace = TimeSpan.FromHours(2);

        private readonly SmartListsHost _host;
        private readonly ISessionManager _sessions;
        private readonly ILogger<NotificationService> _logger;
        private readonly NotificationAggregator _aggregator;
        private readonly Timer _tick;
        private readonly Timer _watchdog;
        private bool _disposed;

        public NotificationService(SmartListsHost host, ISessionManager sessions)
        {
            _host = host;
            _sessions = sessions;
            _logger = host.CreateLogger<NotificationService>();

            var folder = Path.Combine(host.ApplicationPaths.PluginConfigurationsPath, "SmartLists.Notifications");
            Log = new EventLog(Path.Combine(folder, "events.json"));
            _aggregator = new NotificationAggregator(CurrentSwitches);

            host.RefreshStatus.OperationCompleted += OnRefreshCompleted;
            _tick = new Timer(OnTick, null, TickInterval, TickInterval);
            _watchdog = new Timer(OnWatchdog, null, WatchdogInterval, WatchdogInterval);
        }

        /// <summary>Gets the numbered log of sent events (read by the Events route).</summary>
        public EventLog Log { get; }

        /// <summary>
        /// Gets the plugin's running notification service, or null before the server has started the plugin.
        /// </summary>
        public static NotificationService? Instance { get; private set; }

        /// <summary>
        /// Starts the notification service and publishes it as <see cref="Instance"/>.
        /// </summary>
        /// <param name="host">The plugin host.</param>
        /// <param name="sessions">Emby's session manager.</param>
        /// <returns>The service.</returns>
        public static NotificationService Start(SmartListsHost host, ISessionManager sessions)
        {
            var service = new NotificationService(host, sessions);
            Instance = service;
            return service;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _disposed = true;
            _host.RefreshStatus.OperationCompleted -= OnRefreshCompleted;
            _tick.Dispose();
            _watchdog.Dispose();
            if (ReferenceEquals(Instance, this))
            {
                Instance = null;
            }
        }

        private static NotificationSwitches CurrentSwitches()
        {
            var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
            return new NotificationSwitches
            {
                SendPhoneAlerts = config.SendPhoneAlerts,
                RefreshFailed = config.PhoneAlertRefreshFailed,
                RefreshRunFinished = config.PhoneAlertRefreshRunFinished,
                TimerStopped = config.PhoneAlertTimerStopped,
            };
        }

        private void OnRefreshCompleted(RefreshOutcome outcome)
        {
            if (!_disposed)
            {
                _aggregator.OnRefreshCompleted(outcome);
            }
        }

        private void OnTick(object? state)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                var idle = !_host.RefreshStatus.GetOngoingOperations().Any() && _host.RefreshStatus.GetQueuedCount() == 0;
                foreach (var pending in _aggregator.Tick(idle))
                {
                    Send(pending);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Phone alert batching failed");
            }
        }

        private async void OnWatchdog(object? state)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                var autoRefresh = _host.AutoRefresh;
                if (autoRefresh == null || !autoRefresh.IsScheduleTimerStalled(TimerGrace))
                {
                    return;
                }

                var playlists = await _host.PlaylistStore.GetAllAsync().ConfigureAwait(false);
                var collections = await _host.CollectionStore.GetAllAsync().ConfigureAwait(false);
                var anySchedule = playlists.Any(p => p.Enabled && p.Schedules != null && p.Schedules.Any(s => s?.Trigger != null))
                    || collections.Any(c => c.Enabled && c.Schedules != null && c.Schedules.Any(s => s?.Trigger != null));
                if (!anySchedule)
                {
                    return;
                }

                var pending = _aggregator.OnTimerStopped("Scheduled list refreshes have stopped running. Restart the refresh timer from the SmartLists page or restart Emby.");
                if (pending != null)
                {
                    Send(pending);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Schedule timer check for phone alerts failed");
            }
        }

        /// <summary>Numbers and stores the event, then pushes it without waiting.</summary>
        private void Send(PendingEvent pending)
        {
            var logged = Log.Append(pending);
            EventsPushListener.NotifyChanged();
            var data = PlainJson.ToPlain(logged.Data);
            _ = Task.Run(async () =>
            {
                try
                {
                    await _sessions.SendMessageToAdminSessions(logged.Name, data, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("A phone alert could not be sent: {Reason}", ex.GetType().Name + " " + ex.Message);
                }
            });
        }

        /// <summary>
        /// Whether an access token belongs to a session of an administrator. The token is only passed to Emby's session manager: it is never logged,
        /// echoed or stored, and a failed lookup is just "not an administrator".
        /// </summary>
        /// <param name="token">The access token from the start message.</param>
        /// <param name="deviceId">The device id from the socket address.</param>
        /// <param name="remoteAddress">The caller's address.</param>
        /// <returns>True for a live session of an administrator.</returns>
        public bool IsAdministratorToken(string? token, string? deviceId, System.Net.IPAddress? remoteAddress)
        {
            if (string.IsNullOrEmpty(token))
            {
                return false;
            }

            try
            {
                var session = _sessions.GetSessionByAuthenticationToken(token, deviceId, remoteAddress, "http");
                return session != null && _host.UserManager.GetUserById(session.UserId)?.Policy?.IsAdministrator == true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>The events for the Events route, as plain dictionaries.</summary>
        /// <param name="after">The last number the caller has.</param>
        /// <param name="limit">The most events to return.</param>
        /// <returns>The answer.</returns>
        public Dictionary<string, object?> GetEvents(long after, int limit) => EventsPayload.Build(Log, after, limit);
    }
}