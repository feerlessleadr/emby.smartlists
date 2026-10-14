using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Logging;

namespace Emby.Plugin.SmartLists.Notifications
{
    /// <summary>The state Emby keeps for one subscribed websocket connection.</summary>
    public class EventsPushState : WebSocketListenerState
    {
        public EventsSubscription Subscription { get; } = new EventsSubscription();
    }

    /// <summary>
    /// Alerts over Emby's own websocket, for an app that cannot rely on session pushes (Emby links a socket to a session only when the token is in
    /// the address, and the app keeps its token in a header). The app connects to /embywebsocket as an administrator and sends
    /// <c>{"MessageType":"SmartListsEventsStart","Data":"0,30000,&lt;afterSeq&gt;"}</c> (the third value is optional); the plugin then sends
    /// <c>{"MessageType":"SmartListsEvents","Data":{ApiLevel, LatestSeq, Truncated, Events[]}}</c> once at once, whenever new events are logged, and as
    /// a heartbeat (empty Events) every 30 seconds. <c>SmartListsEventsStop</c> ends it. Anyone but an administrator gets <c>Data.Error</c> only.
    /// The pattern is Hall Monitor's StatusPushListener.
    /// </summary>
    public class EventsPushListener : BasePeriodicWebSocketListener<Dictionary<string, object?>, EventsPushState>
    {
        private static EventsPushListener? _instance;

        public EventsPushListener(ILogManager logManager)
            : base(logManager.GetLogger("SmartListsEvents"))
        {
            _instance = this;
        }

        protected override string Name => "SmartListsEvents";

        protected override bool SendOnInitialConnection => true;

        /// <summary>Pushes to every subscribed connection now. Safe to call from anywhere; a failure is logged and swallowed.</summary>
        public static void NotifyChanged()
        {
            try
            {
                _instance?.SendData(force: true);
            }
            catch (Exception ex)
            {
                _instance?.Logger.ErrorException("[SmartLists] Could not push events.", ex, Array.Empty<object>());
            }
        }

        protected override void ParseMessageParams(string[] values, EventsPushState state)
        {
            state.Subscription.ParseStartValues(values);
        }

        protected override Task<Dictionary<string, object?>> GetDataToSend(IWebSocketConnection connection, EventsPushState state, CancellationToken cancellationToken)
        {
            // One subscription per connection: a second "Start" on the same socket must not multiply the traffic.
#pragma warning disable MT1000 // Emby's own listener list; locking it is how Hall Monitor and Emby guard it
            lock (ActiveConnections)
            {
                var first = ActiveConnections.FirstOrDefault(c => c.Item1 == connection);
                if (first != null && first.Item3 != state)
                {
                    return Task.FromResult<Dictionary<string, object?>>(null!);
                }
            }

#pragma warning restore MT1000
            var service = NotificationService.Instance;
            var isAdministrator = connection.User?.Policy?.IsAdministrator == true;

            var data = state.Subscription.Next(service?.Log, isAdministrator);
            return Task.FromResult(data!);
        }

    }
}
