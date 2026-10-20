using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Extensions;
using Emby.Plugin.SmartLists.Utilities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Emby.Plugin.SmartLists.Core.QueryEngine
{
    /// <summary>
    /// Ancestor-inherited Tags/Studios/Genres for one node of the item tree.
    /// IMMUTABLE: instances are shared across operands via the per-refresh memo and are
    /// assigned to Operand properties BY REFERENCE. Members are IReadOnlyList so that
    /// mutation is a compile error rather than a comment-enforced convention — Empty is a
    /// process-lifetime singleton and a stray mutation would corrupt every list, for every
    /// user, for the rest of the process.
    /// </summary>
    public sealed class AncestorValues
    {
        /// <summary>
        /// Shared, process-lifetime "nothing inherited" instance. The three members are empty
        /// but NEVER null: AnyRegexMatch's empty-list branch tests the pattern against
        /// string.Empty, whereas a null list returns false, so null and empty are observably
        /// different for MatchRegex.
        /// </summary>
        public static readonly AncestorValues Empty = new([], [], []);

        public IReadOnlyList<string> Tags { get; }
        public IReadOnlyList<string> Studios { get; }
        public IReadOnlyList<string> Genres { get; }

        private AncestorValues(IReadOnlyList<string> tags, IReadOnlyList<string> studios, IReadOnlyList<string> genres)
        {
            Tags = tags;
            Studios = studios;
            Genres = genres;
        }

        /// <summary>
        /// Returns a NEW instance holding this instance's values plus the node's own
        /// Tags/Studios/Genres. Returns <c>this</c> unchanged when the node contributes
        /// nothing, so a chain of value-less folders allocates nothing.
        /// </summary>
        /// <param name="node">The ancestor node whose own values are folded in.</param>
        /// <returns>The combined values.</returns>
        public AncestorValues Union(BaseItem node)
        {
            if (node is null)
            {
                return this;
            }

            // BaseItem is nullable-OBLIVIOUS in both ABIs (no NullableContextAttribute), so these
            // three are declared string[] yet can be null at runtime and nothing warns under
            // Nullable=enable. Guard each one individually.
            var nodeTags = node.Tags;
            var nodeStudios = node.Studios;
            var nodeGenres = node.Genres;

            var hasTags = nodeTags is { Length: > 0 };
            var hasStudios = nodeStudios is { Length: > 0 };
            var hasGenres = nodeGenres is { Length: > 0 };

            if (!hasTags && !hasStudios && !hasGenres)
            {
                return this;
            }

            return new AncestorValues(
                hasTags ? Combine(Tags, nodeTags) : Tags,
                hasStudios ? Combine(Studios, nodeStudios) : Studios,
                hasGenres ? Combine(Genres, nodeGenres) : Genres);
        }

        private static IReadOnlyList<string> Combine(IReadOnlyList<string> existing, string[] additions)
        {
            var combined = new List<string>(existing.Count + additions.Length);

            // Ordinal, deliberately NOT OrdinalIgnoreCase (which is what core's GetInheritedTags uses).
            // Six of the seven operators are OrdinalIgnoreCase so dedup strength is invisible to them,
            // but MatchRegex is case-SENSITIVE (GetOrCreateRegex passes RegexOptions.None) — case-insensitive
            // dedup could discard the only casing a pattern would have matched.
            var seen = new HashSet<string>(existing.Count + additions.Length, StringComparer.Ordinal);

            foreach (var value in existing)
            {
                if (value is not null && seen.Add(value))
                {
                    combined.Add(value);
                }
            }

            foreach (var value in additions)
            {
                if (value is not null && seen.Add(value))
                {
                    combined.Add(value);
                }
            }

            return combined;
        }
    }

    /// <summary>
    /// The ONE ancestor walk. It serves both the ancestor-inherited Tags/Studios/Genres
    /// (<see cref="Resolve"/>) and parent favorites (<see cref="ResolveAncestors"/>), so both
    /// share a single set of boundary, cycle, depth-cap, truncation, owner-fallback and
    /// library-union rules. Do NOT add a second walker next to this one.
    /// </summary>
    internal static class AncestorValueResolver
    {
        // Runaway/cycle insurance ONLY; it must never bind. Measured real depth in the dev
        // library is 4 (episode->season->series->folder), 2 (movie), 3 (audio);
        // /music/Artist/Album/Disc/track reaches 5 and deep genre trees maybe 7.
        // Do NOT "tighten" this to 10 — a binding cap produces truncated results.
        private const int MaxAncestorDepth = 20;

        /// <summary>
        /// Ancestor-inherited values for <paramref name="item"/>, EXCLUDING the item's own
        /// values (Engine unions the item's own field separately).
        ///
        /// The walk is: parent chain (stopping BEFORE AggregateFolder/UserRootFolder/UserView)
        /// UNION libraryManager.GetCollectionFolders(chainTop). The second half is NOT optional:
        /// a CollectionFolder (a Emby library) is never in the ParentId chain — it hangs off
        /// the UserRootFolder as a sibling structure — so a parents-only walk finds season tags
        /// but never library tags. Core's BaseItem.GetInheritedTags()/GetAncestorIds() have the
        /// same two-part shape.
        ///
        /// COST: the memo is keyed on the IMMEDIATE PARENT id and is consulted BEFORE any parent
        /// is materialized, so a hit costs one dictionary lookup and ZERO ILibraryManager calls —
        /// parity with the code this replaces. A miss costs O(distinct containers) GetItemById
        /// plus one GetCollectionFolders path-scan per top physical folder.
        /// NOTE: when a list's ONLY rule is parent-aware, SmartList takes the expensive-only path
        /// (SmartList.cs:3026) and this runs once per CANDIDATE item, not per Phase-2 survivor.
        /// </summary>
        /// <param name="item">The item whose ancestors are walked.</param>
        /// <param name="libraryManager">Library manager used for the library (CollectionFolder) union.</param>
        /// <param name="memo">Per-refresh memo keyed by ANCESTOR NODE id.</param>
        /// <param name="logger">Optional logger.</param>
        /// <returns>The inherited values; never null.</returns>
        internal static AncestorValues Resolve(
            BaseItem item,
            ILibraryManager libraryManager,
            ConcurrentDictionary<long, AncestorValues> memo,
            ILogger? logger,
            LinkedAlbumCache? albums = null)
        {
            var inherited = Walk(item, libraryManager, memo, AncestorValues.Empty, static (acc, node) => acc.Union(node), logger);
            return IncludeLinkedAlbum(item, libraryManager, inherited, albums, static (acc, node) => acc.Union(node), logger);
        }

        /// <summary>
        /// Every ancestor NODE of <paramref name="item"/> — the same walk as <see cref="Resolve"/>
        /// with the same memo keying, EXCLUDING the item itself. Parent favorites need the nodes
        /// rather than folded values because favorite state is per user and is read through the
        /// per-refresh user-data cache. Order: library/top first, immediate parent last.
        /// The returned lists are shared through the memo — never mutate them.
        /// </summary>
        /// <param name="item">The item whose ancestors are walked.</param>
        /// <param name="libraryManager">Library manager used for the library (CollectionFolder) union.</param>
        /// <param name="memo">Per-refresh memo keyed by ANCESTOR NODE id.</param>
        /// <param name="logger">Optional logger.</param>
        /// <returns>The ancestor nodes; never null.</returns>
        internal static IReadOnlyList<BaseItem> ResolveAncestors(
            BaseItem item,
            ILibraryManager libraryManager,
            ConcurrentDictionary<long, IReadOnlyList<BaseItem>> memo,
            ILogger? logger,
            LinkedAlbumCache? albums = null)
        {
            var inherited = Walk<IReadOnlyList<BaseItem>>(item, libraryManager, memo, Array.Empty<BaseItem>(), AppendAncestor, logger);
            return IncludeLinkedAlbum(item, libraryManager, inherited, albums, AppendAncestor, logger);
        }

        /// <summary>
        /// Returns a NEW list holding <paramref name="acc"/> plus <paramref name="node"/>, or
        /// <paramref name="acc"/> unchanged when the node is null or already present (a library
        /// found by both GetCollectionFolders and path matching contributes once).
        /// </summary>
        private static IReadOnlyList<BaseItem> AppendAncestor(IReadOnlyList<BaseItem> acc, BaseItem node)
            => node is null || acc.Any(a => a.Id == node.Id) ? acc : [.. acc, node];

        private static TAcc Walk<TAcc>(
            BaseItem item,
            ILibraryManager libraryManager,
            ConcurrentDictionary<long, TAcc> memo,
            TAcc empty,
            Func<TAcc, BaseItem, TAcc> union,
            ILogger? logger)
            where TAcc : class
        {
            // MEMO-FIRST on the raw ParentId long — do NOT call GetParent() before this line.
            // GetParent() is `ParentId.IsEmpty() ? null : LibraryManager.GetItemById(ParentId)`,
            // so materializing the parent first would cost a library round-trip per ITEM.
            var parentId = item.ParentId;
            if (parentId != 0 && memo.TryGetValue(parentId, out var cached))
            {
                return cached;
            }

            var node = item.GetParent();   // Emby: an extra's ParentId is its owner, so GetParent() covers extras (verified on 4.10.1.0)
            if (node is null || IsWalkBoundary(node))
            {
                // No walkable ancestors — either the parent could not be resolved at all, or the
                // item sits directly under the AggregateFolder/UserRootFolder boundary. The library
                // still counts in both cases: GetCollectionFolders resolves independently of the
                // parent chain, so returning Empty here would drop library values for these items
                // exactly the way the old one-level extractors dropped season values in #495.
                return GetLibraryValues(item, libraryManager, empty, union, logger);
            }

            var chain = new List<BaseItem>();
            var visited = new HashSet<long>();
            TAcc? seed = null;
            var truncated = false;

            while (node is not null && !IsWalkBoundary(node))
            {
                if (memo.TryGetValue(node.InternalId, out var hit)) { seed = hit; break; }
                if (!visited.Add(node.InternalId) || chain.Count >= MaxAncestorDepth)
                {
                    logger?.LogWarning(
                        "SmartLists ancestor walk stopped early at '{Name}' ({Id}) - cycle or depth cap",
                        node.Name, node.Id);
                    truncated = true;
                    break;
                }
                chain.Add(node);
                node = node.GetParent();
            }

            if (seed is null)
            {
                // Library values are resolved from the deepest node reached. GetCollectionFolders
                // walks independently of our chain, so it is valid (and required) even when the
                // chain was truncated — dropping it on truncation would silently reproduce #495.
                seed = GetLibraryValues(chain[^1], libraryManager, empty, union, logger);
            }

            var acc = seed;
            for (var i = chain.Count - 1; i >= 0; i--)
            {
                acc = union(acc, chain[i]);

                // NEVER memoize a truncated (partial) result. A truncated walk is missing the TOP
                // of the chain, so caching it would make later walks return a value that depends on
                // WHICH ITEM WARMED THE CACHE FIRST (an episode 4 levels down vs a series 1 level down).
                if (!truncated) { memo[chain[i].InternalId] = acc; }
            }

            return acc;
        }

        /// <summary>
        /// Folds in the virtual MusicAlbum linked by <see cref="BaseItem.InternalAlbumId"/>
        /// when that album is not already in the ParentId chain.
        ///
        /// On Emby 4.10.1.0 a track's ParentId is the on-disk folder. The album people tag
        /// in the metadata editor is a separate MusicAlbum with no path and IsFolder false,
        /// stored on the track as InternalAlbumId and indexed in AncestorIds. Reading
        /// <see cref="BaseItem.AlbumId"/> is deliberately avoided: when InternalAlbumId is 0
        /// that getter calls GetAlbumItem, which loads tags. The album is applied per item,
        /// after the memoized parent walk, so two tracks in one folder keep their own albums.
        /// The album is NOT written into <paramref name="albums"/> under the parent-memo key.
        /// </summary>
        private static TAcc IncludeLinkedAlbum<TAcc>(
            BaseItem item,
            ILibraryManager libraryManager,
            TAcc inherited,
            LinkedAlbumCache? albums,
            Func<TAcc, BaseItem, TAcc> union,
            ILogger? logger)
            where TAcc : class
        {
            var albumId = item.InternalAlbumId;
            if (albumId == 0 || albumId == item.InternalId)
            {
                return inherited;
            }

            // Already the immediate parent, or already present in the node list. Folding it
            // again would allocate a new ancestor list and break the memo's reference sharing.
            if (albumId == item.ParentId)
            {
                return inherited;
            }

            if (inherited is IReadOnlyList<BaseItem> nodes && nodes.Any(node => node.InternalId == albumId))
            {
                return inherited;
            }

            var album = (albums ?? new LinkedAlbumCache()).Resolve(albumId, item, libraryManager, logger);
            if (album == null || album.InternalId == item.InternalId || IsWalkBoundary(album))
            {
                return inherited;
            }

            return union(inherited, album);
        }

        private static bool IsWalkBoundary(BaseItem node)
            => node is AggregateFolder || node is UserRootFolder || node is UserView;

        private static TAcc GetLibraryValues<TAcc>(
            BaseItem anchor,
            ILibraryManager libraryManager,
            TAcc empty,
            Func<TAcc, BaseItem, TAcc> union,
            ILogger? logger)
            where TAcc : class
        {
            try
            {
                var acc = empty;
                foreach (var folder in libraryManager.GetCollectionFolders(anchor)) { acc = union(acc, folder); }

                // Symlinked and plugin-created virtual libraries: GetCollectionFolders can resolve to the
                // SOURCE library instead of the virtual one, so it returns a wrong-but-non-empty result and
                // the library's own tags/studios/genres never appear. Path-matching against the configured
                // virtual folders catches those. Union rather than fall back, for exactly that reason.
                // Union de-duplicates, so a library found by both routes contributes once.
                //
                // Matched on the ANCHOR, not the leaf item, and that is deliberate: this result is memoized
                // against ANCESTOR NODE ids and shared by every item under that container, so anything
                // derived from a single item's path would poison the memo for its siblings. The anchor is
                // the chain-top folder, which lives under the same library location as its contents.
                foreach (var folder in LibraryManagerHelper.GetLibraryFoldersForItemPath(libraryManager, anchor))
                {
                    acc = union(acc, folder);
                }

                return acc;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "SmartLists failed to resolve library values for '{Name}'", anchor.Name);
                return empty;
            }
        }
    }

    /// <summary>
    /// Per-refresh cache of virtual albums resolved by <see cref="BaseItem.InternalAlbumId"/>.
    /// Kept off the parent-walk memo: that memo is shared by every item under a folder, and
    /// two tracks in one folder can belong to different albums.
    /// </summary>
    internal sealed class LinkedAlbumCache
    {
        private readonly ConcurrentDictionary<long, BaseItem> _found = new();
        private readonly ConcurrentDictionary<long, byte> _missing = new();

        /// <summary>Drops every cached album. Call this whenever the parent-walk memo is cleared.</summary>
        public void Clear()
        {
            _found.Clear();
            _missing.Clear();
        }

        /// <summary>
        /// The album item, or null when it does not resolve. A failure is remembered for this
        /// cache's lifetime so one missing album is not looked up again for every track.
        /// </summary>
        public BaseItem? Resolve(long albumId, BaseItem item, ILibraryManager libraryManager, ILogger? logger)
        {
            if (_missing.ContainsKey(albumId))
            {
                return null;
            }

            if (_found.TryGetValue(albumId, out var cached))
            {
                return cached;
            }

            BaseItem? album;
            try
            {
                album = libraryManager.GetItemById(albumId);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "SmartLists failed to resolve linked album {AlbumId} for '{Name}'", albumId, item.Name);
                _missing[albumId] = 0;
                return null;
            }

            if (album == null)
            {
                _missing[albumId] = 0;
                return null;
            }

            _found[albumId] = album;
            return album;
        }
    }
}
