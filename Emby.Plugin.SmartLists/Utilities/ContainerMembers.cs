using System;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using Microsoft.Extensions.Logging;

namespace Emby.Plugin.SmartLists.Utilities
{
    /// <summary>
    /// Reads the members of a collection or playlist through Emby's typed APIs.
    /// </summary>
    /// <remarks>
    /// Emby has no <c>LinkedChildren</c>/<c>GetLinkedChildren</c> as Jellyfin did. A playlist's items come from
    /// <c>Playlist.GetChildren(query)</c> (verified on Emby 4.10.1.0: returns the items in order, each with its
    /// <c>ListItemEntryId</c>). A collection's members are read with <c>InternalItemsQuery.CollectionIds</c>, which is
    /// NOT yet verified against a live server (port phase 4).
    /// </remarks>
    public static class ContainerMembers
    {
        /// <summary>
        /// Gets the members of <paramref name="container"/> visible to <paramref name="user"/>.
        /// </summary>
        /// <param name="libraryManager">The library manager.</param>
        /// <param name="container">A <see cref="Playlist"/> or <see cref="BoxSet"/>.</param>
        /// <param name="user">The user whose view is read; null for no user filtering.</param>
        /// <param name="logger">Optional logger.</param>
        /// <returns>The members, or an empty array when the item is not a container or the lookup fails.</returns>
        public static BaseItem[] Get(ILibraryManager libraryManager, BaseItem container, User? user, ILogger? logger)
        {
            ArgumentNullException.ThrowIfNull(libraryManager);
            ArgumentNullException.ThrowIfNull(container);

            try
            {
                switch (container)
                {
                    case Playlist playlist:
                        return playlist.GetChildren(new InternalItemsQuery { User = user });
                    case BoxSet:
                        return libraryManager.GetItemList(new InternalItemsQuery
                        {
                            User = user,
                            CollectionIds = [container.InternalId],
                            Recursive = true,
                            GroupByPresentationUniqueKey = false,
                        });
                    default:
                        logger?.LogDebug("'{ContainerName}' is not a playlist or collection; no members read", container.Name);
                        return [];
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Reading the members of '{ContainerName}' failed", container.Name);
                return [];
            }
        }
    }
}
