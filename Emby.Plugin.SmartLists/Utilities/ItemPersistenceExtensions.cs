using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Emby.Plugin.SmartLists.Utilities
{
    /// <summary>
    /// Emby saves item changes synchronously (<c>BaseItem.UpdateToRepository</c>,
    /// <c>ILibraryManager.UpdateItem</c>). These wrappers keep the async call shape of the surrounding
    /// refresh pipeline, honoring cancellation before the save.
    /// </summary>
    public static class ItemPersistenceExtensions
    {
        /// <summary>
        /// Saves <paramref name="item"/> to the repository using its current parent.
        /// </summary>
        /// <param name="item">The item to save.</param>
        /// <param name="reason">What changed (drives which downstream refreshes run).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A completed task.</returns>
        public static Task UpdateToRepositoryAsync(this BaseItem item, ItemUpdateType reason, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(item);
            cancellationToken.ThrowIfCancellationRequested();
            item.UpdateToRepository(reason, item.GetParent());
            return Task.CompletedTask;
        }

        /// <summary>
        /// Saves <paramref name="item"/> through the library manager.
        /// </summary>
        /// <param name="libraryManager">The library manager.</param>
        /// <param name="item">The item to save.</param>
        /// <param name="parent">The item's parent.</param>
        /// <param name="reason">What changed.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A completed task.</returns>
        public static Task UpdateItemAsync(this ILibraryManager libraryManager, BaseItem item, BaseItem? parent, ItemUpdateType reason, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(libraryManager);
            ArgumentNullException.ThrowIfNull(item);
            cancellationToken.ThrowIfCancellationRequested();
            libraryManager.UpdateItem(item, parent, reason);
            return Task.CompletedTask;
        }
    }
}
