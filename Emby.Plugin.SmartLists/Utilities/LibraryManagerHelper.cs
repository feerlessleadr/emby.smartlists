using Emby.Plugin.SmartLists.Core.Constants;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Emby.Plugin.SmartLists.Utilities
{
    /// <summary>
    /// Utility class for common ILibraryManager operations using reflection.
    /// </summary>
    public static class LibraryManagerHelper
    {
        /// <summary>
        /// Triggers a library scan using reflection to call QueueLibraryScan if available.
        /// </summary>
        /// <param name="libraryManager">The library manager instance</param>
        /// <param name="logger">Optional logger for diagnostics</param>
        /// <returns>True if the scan was successfully queued, false otherwise</returns>
        public static bool QueueLibraryScan(ILibraryManager libraryManager, ILogger? logger = null)
        {
            ArgumentNullException.ThrowIfNull(libraryManager);
            
            try
            {
                logger?.LogDebug("Triggering library scan");
                var queueScanMethod = libraryManager.GetType().GetMethod("QueueLibraryScan");
                if (queueScanMethod != null)
                {
                    queueScanMethod.Invoke(libraryManager, null);
                    logger?.LogDebug("Queued library scan");
                    return true;
                }
                else
                {
                    logger?.LogWarning("QueueLibraryScan method not found on ILibraryManager");
                    return false;
                }
            }
            catch (TargetInvocationException ex)
            {
                // Unwrap TargetInvocationException to get the actual inner exception
                logger?.LogWarning(ex.InnerException ?? ex, "Failed to trigger library scan");
                return false;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to trigger library scan");
                return false;
            }
        }
        /// <summary>
        /// Gets TopParentIds for actual user libraries by resolving VirtualFolder physical paths
        /// to their folder BaseItem IDs. Excludes internal folders like live TV recordings.
        /// </summary>
        /// <param name="libraryManager">The library manager instance</param>
        /// <returns>Array of internal item IDs for library top parent folders</returns>
        public static long[] GetLibraryTopParentIds(ILibraryManager libraryManager)
        {
            var ids = new List<long>();
            foreach (var vf in libraryManager.GetVirtualFolders())
            {
                if (vf.Locations == null)
                {
                    continue;
                }

                foreach (var location in vf.Locations)
                {
                    if (string.IsNullOrEmpty(location))
                    {
                        continue;
                    }

                    // Skip live TV recording locations
                    if (location.Contains("/livetv/", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var folder = libraryManager.FindByPath(location, true);
                    if (folder != null)
                    {
                        ids.Add(folder.InternalId);
                    }
                }
            }

            return ids.ToArray();
        }

        /// <summary>
        /// Resolves library names by comparing an item's file path with configured virtual folder locations.
        /// This supplements GetCollectionFolders for symlinked libraries whose collection-folder lookup may
        /// resolve to the source item instead of the virtual recommendation library.
        /// </summary>
        public static IReadOnlyList<string> GetLibraryNamesForItemPath(ILibraryManager libraryManager, BaseItem item)
        {
            var names = new List<string>();
            var itemPaths = new[]
            {
                item.Path,
                item.ContainingFolderPath,
            };

            foreach (var vf in libraryManager.GetVirtualFolders())
            {
                if (string.IsNullOrWhiteSpace(vf.Name) || vf.Locations == null)
                {
                    continue;
                }

                if (vf.Locations.Any(location =>
                    !string.IsNullOrWhiteSpace(location) &&
                    itemPaths.Any(itemPath => IsPathInLocation(itemPath, location))))
                {
                    names.Add(vf.Name);
                }
            }

            return names
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
                .AsReadOnly();
        }

        /// <summary>
        /// Resolves the library folder ITEMS containing an item by comparing its file path with configured
        /// virtual folder locations - the same match <see cref="GetLibraryNamesForItemPath"/> performs, but
        /// returning the <c>CollectionFolder</c> items so callers can read metadata off them rather than
        /// just the name.
        ///
        /// Supplements (never replaces) <c>GetCollectionFolders</c>: for a symlinked library that call can
        /// return the SOURCE library rather than the virtual one, so it yields a wrong-but-non-empty result
        /// that a fallback-only check would never notice.
        /// </summary>
        public static IReadOnlyList<BaseItem> GetLibraryFoldersForItemPath(ILibraryManager libraryManager, BaseItem item)
        {
            var folders = new List<BaseItem>();
            var itemPaths = new[]
            {
                item.Path,
                item.ContainingFolderPath,
            };

            foreach (var vf in libraryManager.GetVirtualFolders())
            {
                if (vf.Locations == null || string.IsNullOrWhiteSpace(vf.ItemId))
                {
                    continue;
                }

                if (!vf.Locations.Any(location =>
                    !string.IsNullOrWhiteSpace(location) &&
                    itemPaths.Any(itemPath => IsPathInLocation(itemPath, location))))
                {
                    continue;
                }

                // Emby's virtual folder ItemId is the folder's internal id; accept a Guid string as a fallback.
                BaseItem? folder = null;
                if (long.TryParse(vf.ItemId, out var folderInternalId))
                {
                    folder = libraryManager.GetItemById(folderInternalId);
                }
                else if (Guid.TryParse(vf.ItemId, out var folderGuid))
                {
                    folder = libraryManager.GetItemById(folderGuid);
                }

                if (folder != null)
                {
                    folders.Add(folder);
                }
            }

            return folders.AsReadOnly();
        }

        public static void ApplyVirtualItemQueryScope(InternalItemsQuery query, bool includeVirtualItems, long[] validTopParentIds)
        {
            query.IsVirtualItem = includeVirtualItems ? null : false;

            if (!includeVirtualItems)
            {
                query.TopParentIds = validTopParentIds;
            }
        }

        private static bool IsPathInLocation(string? itemPath, string location)
        {
            if (string.IsNullOrWhiteSpace(itemPath))
            {
                return false;
            }

            var normalizedPath = NormalizePathForPrefixMatch(itemPath);
            var normalizedLocation = NormalizePathForPrefixMatch(location);

            return normalizedPath.Equals(normalizedLocation, StringComparison.OrdinalIgnoreCase) ||
                normalizedPath.StartsWith(normalizedLocation + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePathForPrefixMatch(string path)
        {
            var normalizedPath = path.Replace('\\', '/').TrimEnd('/');
            return normalizedPath.Length == 0 ? "/" : normalizedPath;
        }

        /// <summary>
        /// Parent item kinds that can own extras (behind the scenes, deleted scenes, featurettes, etc.).
        /// </summary>
        /// <summary>
        /// Extra types fetched for lists that include extras (everything except theme songs/videos and additional parts).
        /// </summary>
        private static readonly ExtraType[] ExtrasTypes =
        [
            ExtraType.Clip,
            ExtraType.Trailer,
            ExtraType.BehindTheScenes,
            ExtraType.DeletedScene,
            ExtraType.Interview,
            ExtraType.Scene,
            ExtraType.Sample,
        ];

        private static readonly string[] ExtrasParentKinds =
        [
            ItemKinds.Movie,
            ItemKinds.Series,
            ItemKinds.Season,
            ItemKinds.MusicVideo,
        ];

        /// <summary>
        /// Fetches extras from parent items and builds an optional reverse mapping from extra ID
        /// to owning series ID. Extras are linked to parent items via ExtraIds and cannot be found
        /// through standard library queries.
        /// </summary>
        /// <param name="libraryManager">The library manager instance</param>
        /// <param name="user">User context for the query</param>
        /// <param name="topParentIds">TopParentIds to scope the parent query</param>
        /// <param name="existingItems">Already-fetched items used to deduplicate extras</param>
        /// <param name="extraOwnerMap">Optional map to populate with extra ID → owning series ID</param>
        /// <param name="logger">Optional logger for diagnostics</param>
        /// <param name="listName">Optional list name for log messages</param>
        /// <returns>List of extras not already present in existingItems</returns>
        public static List<BaseItem> FetchExtras(
            ILibraryManager libraryManager,
            User user,
            long[] topParentIds,
            IReadOnlyList<BaseItem> existingItems,
            ConcurrentDictionary<long, long>? extraOwnerMap = null,
            ILogger? logger = null,
            string? listName = null)
        {
            logger?.LogDebug("IncludeExtras enabled for '{Name}', fetching extras from parent items", listName);

            var parentQuery = new InternalItemsQuery(user)
            {
                IncludeItemTypes = ExtrasParentKinds,
                Recursive = true,
                IsVirtualItem = false,
                TopParentIds = topParentIds,
            };
            var parents = libraryManager.GetItemsResult(parentQuery).Items;

            var seenIds = new HashSet<long>(existingItems.Select(i => i.InternalId));
            var extrasList = new List<BaseItem>();

            foreach (var parent in parents)
            {
                var extras = parent.GetExtras(ExtrasTypes);
                if (extras.Length == 0)
                {
                    continue;
                }

                // Resolve series ID for this parent (if applicable) for reverse mapping
                long? parentSeriesId = null;
                if (extraOwnerMap != null)
                {
                    if (parent is Series)
                    {
                        parentSeriesId = parent.InternalId;
                    }
                    else if (parent is Season season)
                    {
                        parentSeriesId = season.SeriesId != 0 ? season.SeriesId : null;
                    }
                }

                foreach (var extra in extras)
                {
                    if (seenIds.Add(extra.InternalId))
                    {
                        extrasList.Add(extra);

                        // Build reverse mapping: extra ID → owning Series ID
                        if (parentSeriesId.HasValue)
                        {
                            extraOwnerMap!.TryAdd(extra.InternalId, parentSeriesId.Value);
                        }
                    }
                }
            }

            logger?.LogDebug("Found {ExtrasCount} extras from {ParentCount} parent items for '{Name}'",
                extrasList.Count, parents.Length, listName);

            return extrasList;
        }
    }
}
