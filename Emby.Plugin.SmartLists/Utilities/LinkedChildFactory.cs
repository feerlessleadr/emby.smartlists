using System;
using MediaBrowser.Controller.Entities;

namespace Emby.Plugin.SmartLists.Utilities
{
    /// <summary>
    /// Creates LinkedChild instances.
    /// </summary>
    public static class LinkedChildFactory
    {
        /// <summary>
        /// Creates a linked child for <paramref name="item"/>.
        /// </summary>
        /// <remarks>
        /// TODO(port phase 4): Emby's <see cref="LinkedChild"/> carries only a <c>Path</c>; playlists use
        /// ListItem entries and collections use ICollectionManager, so the services must stop building
        /// LinkedChild arrays by ID. This shim keeps the callers compiling until they are rewritten.
        /// </remarks>
        /// <param name="itemId">The item's internal ID (unused by Emby's LinkedChild).</param>
        /// <param name="item">The item.</param>
        /// <returns>A linked child pointing at the item's path.</returns>
        public static LinkedChild Create(long itemId, BaseItem item)
        {
            ArgumentNullException.ThrowIfNull(item);
            return new LinkedChild { Path = item.Path };
        }
    }
}
