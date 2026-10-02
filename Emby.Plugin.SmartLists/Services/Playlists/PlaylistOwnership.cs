using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Dto;

namespace Emby.Plugin.SmartLists.Services.Playlists
{
    /// <summary>
    /// Playlist ownership on Emby. Emby has no <c>OwnerUserId</c> on a playlist: access is stored as
    /// <see cref="UserItemShare"/> rows, and the creating user holds a <see cref="UserItemShareLevel.ManageDelete"/>
    /// share (verified on Emby 4.10.1.0: creating a playlist as a user produced exactly one such row for that user).
    /// So "the owner" is the user holding the <c>ManageDelete</c> share.
    /// </summary>
    public static class PlaylistOwnership
    {
        /// <summary>
        /// Gets the internal ID of the playlist's owner, or null when no owner share exists.
        /// </summary>
        /// <param name="itemRepository">The item repository.</param>
        /// <param name="playlist">The playlist.</param>
        /// <returns>The owner's user internal ID, or null.</returns>
        public static long? GetOwnerInternalId(IItemRepository itemRepository, Playlist playlist)
        {
            ArgumentNullException.ThrowIfNull(itemRepository);
            ArgumentNullException.ThrowIfNull(playlist);

            var shares = itemRepository.GetUserItemShares(
                new UserItemShareQuery { ItemIds = [playlist.InternalId] },
                CancellationToken.None);

            foreach (var share in shares)
            {
                if (share.ShareLevel == UserItemShareLevel.ManageDelete)
                {
                    return share.UserId;
                }
            }

            return null;
        }

        /// <summary>
        /// Whether <paramref name="user"/> owns <paramref name="playlist"/>.
        /// </summary>
        /// <param name="itemRepository">The item repository.</param>
        /// <param name="playlist">The playlist.</param>
        /// <param name="user">The user.</param>
        /// <returns>True when the user holds the owner share.</returns>
        public static bool IsOwnedBy(IItemRepository itemRepository, Playlist playlist, User user)
        {
            ArgumentNullException.ThrowIfNull(user);
            return GetOwnerInternalId(itemRepository, playlist) == user.InternalId;
        }

        /// <summary>
        /// Makes <paramref name="user"/> the owner: grants the owner share and demotes any other owner to
        /// <see cref="UserItemShareLevel.Manage"/>. UNVERIFIED on a live server: ownership changes only happen
        /// when a playlist found by its SmartLists tether is owned by someone else, which should be rare.
        /// </summary>
        /// <param name="itemRepository">The item repository.</param>
        /// <param name="playlist">The playlist.</param>
        /// <param name="user">The new owner.</param>
        public static void SetOwner(IItemRepository itemRepository, Playlist playlist, User user)
        {
            ArgumentNullException.ThrowIfNull(itemRepository);
            ArgumentNullException.ThrowIfNull(playlist);
            ArgumentNullException.ThrowIfNull(user);

            var updates = new List<UserItemShare>
            {
                new() { UserId = user.InternalId, ItemId = playlist.InternalId, ShareLevel = UserItemShareLevel.ManageDelete },
            };

            var existing = itemRepository.GetUserItemShares(
                new UserItemShareQuery { ItemIds = [playlist.InternalId] },
                CancellationToken.None);

            foreach (var share in existing)
            {
                if (share.ShareLevel == UserItemShareLevel.ManageDelete && share.UserId != user.InternalId)
                {
                    updates.Add(new UserItemShare { UserId = share.UserId, ItemId = playlist.InternalId, ShareLevel = UserItemShareLevel.Manage });
                }
            }

            itemRepository.SaveUserItemShares([.. updates]);
        }

        /// <summary>
        /// Gets the items currently in the playlist, in order, with their per-entry IDs.
        /// </summary>
        /// <param name="playlist">The playlist.</param>
        /// <param name="user">The user whose view of the playlist is read.</param>
        /// <returns>The playlist's items; each carries its <see cref="BaseItem.ListItemEntryId"/>.</returns>
        public static BaseItem[] GetItems(Playlist playlist, User user)
        {
            ArgumentNullException.ThrowIfNull(playlist);
            return playlist.GetChildren(new InternalItemsQuery { User = user });
        }
    }
}
