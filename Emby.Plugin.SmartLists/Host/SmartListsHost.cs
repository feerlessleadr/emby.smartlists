using System;
using Emby.Plugin.SmartLists.Services.Collections;
using Emby.Plugin.SmartLists.Services.Playlists;
using Emby.Plugin.SmartLists.Services.Shared;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using Microsoft.Extensions.Logging;

namespace Emby.Plugin.SmartLists.Host
{
    /// <summary>
    /// The plugin's composition root. Emby has no plugin service registrator and no hosted services, so the object
    /// graph is built by hand here, from the managers Emby injects into the entry point.
    /// </summary>
    public sealed class SmartListsHost : IDisposable
    {
        private readonly LoggerFactory _loggerFactory;

        private SmartListsHost(
            ILibraryManager libraryManager,
            IUserManager userManager,
            IPlaylistManager playlistManager,
            ICollectionManager collectionManager,
            IUserDataManager userDataManager,
            IProviderManager providerManager,
            IFileSystem fileSystem,
            IServerApplicationPaths applicationPaths,
            IItemRepository itemRepository,
            ILogManager logManager)
        {
            LibraryManager = libraryManager;
            UserManager = userManager;
            ItemRepository = itemRepository;

            _loggerFactory = new LoggerFactory([new EmbyLoggerProvider(logManager)]);

            RefreshStatus = new RefreshStatusService(Log<RefreshStatusService>());
            ImageService = new SmartListImageService(applicationPaths, Log<SmartListImageService>());
            FileSystem = new SmartListFileSystem(applicationPaths, Log<SmartListFileSystem>());
            PlaylistStore = new PlaylistStore(FileSystem, Log<PlaylistStore>());
            CollectionStore = new CollectionStore(FileSystem, Log<CollectionStore>());

            // External lists (MDBList, Trakt, ...) are outside the Emby MVP: passing null makes rules that need them
            // report that no external list data is available instead of failing.
            PlaylistService = new PlaylistService(
                userManager, libraryManager, playlistManager, userDataManager, Log<PlaylistService>(), ImageService, null, itemRepository);
            CollectionService = new CollectionService(
                libraryManager, collectionManager, userManager, userDataManager, Log<CollectionService>(), providerManager, fileSystem, ImageService, null, itemRepository);

            RefreshQueue = new RefreshQueueService(
                Log<RefreshQueueService>(),
                userManager,
                libraryManager,
                playlistManager,
                collectionManager,
                userDataManager,
                providerManager,
                fileSystem,
                applicationPaths,
                RefreshStatus,
                _loggerFactory,
                ImageService,
                null,
                itemRepository);
            RefreshStatus.SetRefreshQueueService(RefreshQueue);
        }

        /// <summary>
        /// Gets the running host, or null before the server has started the plugin.
        /// </summary>
        public static SmartListsHost? Instance { get; private set; }

        public ILibraryManager LibraryManager { get; }

        public IUserManager UserManager { get; }

        public IItemRepository ItemRepository { get; }

        public RefreshStatusService RefreshStatus { get; }

        public SmartListImageService ImageService { get; }

        public ISmartListFileSystem FileSystem { get; }

        public PlaylistStore PlaylistStore { get; }

        public CollectionStore CollectionStore { get; }

        public PlaylistService PlaylistService { get; }

        public CollectionService CollectionService { get; }

        public RefreshQueueService RefreshQueue { get; }

        /// <summary>
        /// Builds the graph and publishes it as <see cref="Instance"/>.
        /// </summary>
        /// <returns>The host.</returns>
        public static SmartListsHost Create(
            ILibraryManager libraryManager,
            IUserManager userManager,
            IPlaylistManager playlistManager,
            ICollectionManager collectionManager,
            IUserDataManager userDataManager,
            IProviderManager providerManager,
            IFileSystem fileSystem,
            IServerApplicationPaths applicationPaths,
            IItemRepository itemRepository,
            ILogManager logManager)
        {
            var host = new SmartListsHost(
                libraryManager, userManager, playlistManager, collectionManager, userDataManager, providerManager, fileSystem, applicationPaths, itemRepository, logManager);
            Instance = host;
            return host;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            RefreshQueue.Dispose();
            _loggerFactory.Dispose();
            if (ReferenceEquals(Instance, this))
            {
                Instance = null;
            }
        }

        private ILogger<T> Log<T>() => new Logger<T>(_loggerFactory);
    }
}
