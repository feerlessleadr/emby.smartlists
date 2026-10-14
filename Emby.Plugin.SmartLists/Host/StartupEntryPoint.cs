using System;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Session;
using Emby.Plugin.SmartLists.Notifications;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;

namespace Emby.Plugin.SmartLists.Host
{
    /// <summary>
    /// Starts the plugin inside Emby: Emby injects the managers into this constructor and calls <see cref="Run"/> once
    /// the server is up (the equivalent of Emby's hosted services).
    /// </summary>
    public sealed class StartupEntryPoint : IServerEntryPoint
    {
        private readonly ILogger _log;
        private readonly SmartListsHost _host;
        private readonly ISessionManager _sessionManager;
        private NotificationService? _notifications;

        public StartupEntryPoint(
            ILogManager logManager,
            ILibraryManager libraryManager,
            IUserManager userManager,
            IPlaylistManager playlistManager,
            ICollectionManager collectionManager,
            IUserDataManager userDataManager,
            IProviderManager providerManager,
            IFileSystem fileSystem,
            IServerApplicationPaths applicationPaths,
            IItemRepository itemRepository,
            ISessionManager sessionManager)
        {
            _sessionManager = sessionManager;
            _log = logManager.GetLogger("SmartLists");
            _host = SmartListsHost.Create(
                libraryManager, userManager, playlistManager, collectionManager, userDataManager, providerManager, fileSystem, applicationPaths, itemRepository, logManager);
        }

        /// <inheritdoc />
        public void Run()
        {
            _host.StartAutoRefresh();
            _notifications = NotificationService.Start(_host, _sessionManager);
            _log.Info("SmartLists started (auto-refresh listening, phone alerts ready).");
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _notifications?.Dispose();
            _host.Dispose();
        }
    }
}
