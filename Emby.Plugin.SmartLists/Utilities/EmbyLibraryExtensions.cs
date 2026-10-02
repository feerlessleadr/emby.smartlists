using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.Querying;

namespace Emby.Plugin.SmartLists.Utilities
{
    /// <summary>
    /// Thin helpers over Emby's library APIs for lookups the query engine and prefilters need.
    /// Behavior is verified at the signature level only; see docs/emby-api-notes.md.
    /// </summary>
    public static class EmbyLibraryExtensions
    {
        /// <summary>
        /// Gets the folder that holds an item's own image files. Items that live on disk (playlists) have a
        /// containing folder; BoxSets are virtual and keep theirs under the server metadata path.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <returns>The folder path, or null when none can be resolved.</returns>
        public static string? GetItemImageFolder(this BaseItem item)
        {
            ArgumentNullException.ThrowIfNull(item);
            var path = item.ContainingFolderPath;
            if (!string.IsNullOrEmpty(path) && System.IO.Directory.Exists(path))
            {
                return path;
            }

            path = item.GetInternalMetadataPath();
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            System.IO.Directory.CreateDirectory(path);
            return path;
        }

        /// <summary>
        /// Counts the items matching <paramref name="query"/>.
        /// </summary>
        /// <param name="libraryManager">The library manager.</param>
        /// <param name="query">The query.</param>
        /// <returns>The total number of matching items.</returns>
        public static int GetCount(this ILibraryManager libraryManager, InternalItemsQuery query)
        {
            ArgumentNullException.ThrowIfNull(libraryManager);
            return libraryManager.GetItemsResult(query).TotalRecordCount;
        }

        /// <summary>
        /// Gets the distinct person names matching <paramref name="query"/>.
        /// </summary>
        /// <param name="libraryManager">The library manager.</param>
        /// <param name="query">The query.</param>
        /// <returns>The person names.</returns>
        public static IReadOnlyList<string> GetPeopleNames(this ILibraryManager libraryManager, InternalItemsQuery query)
        {
            ArgumentNullException.ThrowIfNull(libraryManager);
            return libraryManager.GetPeople(query).Items.Select(t => t.Item1.Name).ToList();
        }

        /// <summary>
        /// Gets the distinct stream languages of the given type across the library.
        /// </summary>
        /// <param name="libraryManager">The library manager.</param>
        /// <param name="streamType">Audio or subtitle.</param>
        /// <returns>The language codes as stored.</returns>
        public static IReadOnlyList<string> GetMediaStreamLanguages(this ILibraryManager libraryManager, MediaStreamType streamType)
        {
            ArgumentNullException.ThrowIfNull(libraryManager);
            return libraryManager.GetStreamLanguages(new InternalItemsQuery { Recursive = true }, streamType, CancellationToken.None).Items.ToList();
        }

        /// <summary>
        /// Gets all genre names (video genres).
        /// </summary>
        /// <param name="repository">The item repository.</param>
        /// <returns>The genre names.</returns>
        public static IReadOnlyList<string> GetGenreNames(this IItemRepository repository)
        {
            ArgumentNullException.ThrowIfNull(repository);
            return Names(repository.GetGenres(new InternalItemsQuery { Recursive = true }, CancellationToken.None));
        }

        /// <summary>
        /// Gets all music genre names.
        /// </summary>
        /// <param name="repository">The item repository.</param>
        /// <returns>The music genre names.</returns>
        public static IReadOnlyList<string> GetMusicGenreNames(this IItemRepository repository)
        {
            ArgumentNullException.ThrowIfNull(repository);
            return Names(repository.GetMusicGenres(new InternalItemsQuery { Recursive = true }, CancellationToken.None));
        }

        /// <summary>
        /// Gets all studio names.
        /// </summary>
        /// <param name="repository">The item repository.</param>
        /// <returns>The studio names.</returns>
        public static IReadOnlyList<string> GetStudioNames(this IItemRepository repository)
        {
            ArgumentNullException.ThrowIfNull(repository);
            return Names(repository.GetStudios(new InternalItemsQuery { Recursive = true }, CancellationToken.None));
        }

        /// <summary>
        /// Maps an ISO 639-2/B language code to its 639-2/T equivalent using Emby's language table.
        /// </summary>
        /// <param name="localization">The localization manager.</param>
        /// <param name="isoB">The 639-2/B code.</param>
        /// <param name="isoT">The 639-2/T code, when found.</param>
        /// <returns>True when a mapping was found.</returns>
        public static bool TryGetISO6392TFromB(this ILocalizationManager localization, string isoB, out string? isoT)
        {
            ArgumentNullException.ThrowIfNull(localization);
            isoT = localization.FindLanguageInfo(isoB)?.ThreeLetterISOLanguageName;
            return !string.IsNullOrEmpty(isoT);
        }

        private static List<string> Names(QueryResult<Tuple<BaseItem, ItemCounts>> result) =>
            result.Items.Select(t => t.Item1.Name).ToList();
    }
}
