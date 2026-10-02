using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Entities;
using Emby.Plugin.SmartLists.Core.QueryEngine;
using Emby.Plugin.SmartLists.Services.Shared;
using Emby.Plugin.SmartLists.Utilities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Emby.Plugin.SmartLists.Core.Orders
{
    /// <summary>
    /// Base class for LastPlayed ordering to eliminate duplication
    /// </summary>
    public abstract class LastPlayedOrderBase : Order
    {
        protected abstract bool IsDescending { get; }

        public override IEnumerable<BaseItem> OrderBy(
            IEnumerable<BaseItem> items,
            User user,
            IUserDataManager? userDataManager,
            ILogger? logger,
            RefreshQueueService.RefreshCache? refreshCache = null)
        {
            if (items == null) return [];
            if (userDataManager == null || user == null)
            {
                logger?.LogWarning("UserDataManager or User is null for LastPlayed sorting, returning unsorted items");
                return items;
            }

            try
            {
                // Pre-fetch all user data to avoid repeated database calls during sorting
                var list = items as IList<BaseItem> ?? items.ToList();
                var sortValueCache = new Dictionary<BaseItem, DateTime>(list.Count);

                foreach (var item in list)
                {
                    try
                    {
                        sortValueCache[item] = GetLastPlayedValue(item, user, userDataManager, logger, refreshCache);
                    }
                    catch (Exception ex)
                    {
                        logger?.LogWarning(ex, "Error getting user data for item {ItemName} for user {UserId}", item.Name, user.Id);
                        sortValueCache[item] = DateTime.MinValue; // Default to never played
                    }
                }

                // Sort using cached DateTime values directly (no tie-breaker to avoid album grouping)
                return IsDescending
                    ? list.OrderByDescending(item => sortValueCache[item])
                    : list.OrderBy(item => sortValueCache[item]);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error in LastPlayed sorting for user {UserId}, returning unsorted items", user.Id);
                return items;
            }
        }

        public override IComparable GetSortKey(
            BaseItem item,
            User user,
            IUserDataManager? userDataManager,
            ILogger? logger,
            Dictionary<long, int>? itemRandomKeys = null,
            RefreshQueueService.RefreshCache? refreshCache = null)
        {
            try
            {
                return GetLastPlayedValue(item, user, userDataManager, logger, refreshCache);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error getting last played date for item {ItemId} user {UserId}", item.Id, user.Id);
                return DateTime.MinValue;
            }
        }

        /// <summary>
        /// The sort value for a single item. Owner-scoped orders use the refresh's own user; the
        /// all-users variant overrides this to take the most recent date across every configured
        /// user - see <see cref="LastPlayedTotalOrderBase"/>.
        /// </summary>
        protected virtual DateTime GetLastPlayedValue(
            BaseItem item,
            User user,
            IUserDataManager? userDataManager,
            ILogger? logger,
            RefreshQueueService.RefreshCache? refreshCache)
        {
            return GetLastPlayedForUser(item, user, userDataManager, refreshCache);
        }

        /// <summary>
        /// LastPlayedDate for one user: the aggregate across a container's children when the item is
        /// a container, otherwise that user's own row for the item itself.
        /// </summary>
        internal static DateTime GetLastPlayedForUser(
            BaseItem item,
            User user,
            IUserDataManager? userDataManager,
            RefreshQueueService.RefreshCache? refreshCache)
        {
            if (userDataManager != null)
            {
                var aggregateLastPlayedDate = GetAggregateLastPlayedDate(item, user, userDataManager, refreshCache);
                if (aggregateLastPlayedDate.HasValue)
                {
                    return aggregateLastPlayedDate.Value;
                }
            }

            object? userData = null;

            // Try to get user data from cache if available
            if (refreshCache != null && userDataManager != null)
            {
                userData = UserDataCacheHelper.GetCachedUserData(user, item, refreshCache, userDataManager);
            }
            else if (userDataManager != null)
            {
                userData = userDataManager.GetUserData(user, item);
            }

            return GetLastPlayedDateFromUserData(userData);
        }

        /// <summary>
        /// Gets aggregate LastPlayedDate for container items from their children, which
        /// <see cref="OperandFactory.GetAggregationChildren"/> fetches and caches on demand.
        /// </summary>
        internal static DateTime? GetAggregateLastPlayedDate(
            BaseItem item,
            User user,
            IUserDataManager userDataManager,
            RefreshQueueService.RefreshCache? refreshCache)
        {
            if (refreshCache == null)
            {
                return null;
            }

            var children = OperandFactory.GetAggregationChildren(item, refreshCache);

            if (children == null || children.Length == 0)
            {
                return null;
            }

            DateTime maxLastPlayedDate = DateTime.MinValue;
            foreach (var child in children)
            {
                var childUserData = UserDataCacheHelper.GetCachedUserData(user, child, refreshCache, userDataManager);
                var childLastPlayedDate = GetLastPlayedDateFromUserData(childUserData);
                if (childLastPlayedDate > maxLastPlayedDate)
                {
                    maxLastPlayedDate = childLastPlayedDate;
                }
            }

            return maxLastPlayedDate == DateTime.MinValue ? null : maxLastPlayedDate;
        }

        /// <summary>
        /// Extracts LastPlayedDate from user data as UTC, or <see cref="DateTime.MinValue"/> when there is none.
        /// Emby's <c>UserItemData.LastPlayedDate</c> is a <see cref="DateTimeOffset"/>?, which is why a plain
        /// <c>is DateTime</c> test would see no date for every item.
        /// </summary>
        internal static DateTime GetLastPlayedDateFromUserData(object? userData)
        {
            if (userData == null) return DateTime.MinValue;

            var lastPlayedValue = userData.GetType().GetProperty("LastPlayedDate")?.GetValue(userData);
            return ReflectedDate.TryGetUtc(lastPlayedValue, out var utc) ? utc : DateTime.MinValue;
        }
    }
}
