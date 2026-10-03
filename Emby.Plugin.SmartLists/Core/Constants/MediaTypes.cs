using System;
using System.Collections.Generic;
using System.Linq;


namespace Emby.Plugin.SmartLists.Core.Constants
{
    /// <summary>
    /// Centralized media type constants to prevent typos and improve maintainability
    /// </summary>
    public static class MediaTypes
    {
        // TV Types
        public const string Episode = nameof(Episode);

        // Series media type: Not supported in Playlists (Emby expands to episodes causing playback issues)
        // but supported in Collections (Collections don't play sequentially, so no expansion issues)
        public const string Series = nameof(Series);

        // Season media type: Not supported in Playlists (Emby expands to episodes causing playback issues)
        // but supported in Collections (same pattern as Series)
        public const string Season = nameof(Season);

        // Movie Types
        public const string Movie = nameof(Movie);

        // Audio Types
        public const string Audio = nameof(Audio);

        // MusicAlbum media type: Not supported in Playlists (Emby expands to individual tracks)
        // but supported in Collections (same pattern as Series)
        public const string MusicAlbum = nameof(MusicAlbum);

        // Music Video Types
        public const string MusicVideo = nameof(MusicVideo);

        // Home Video and Photo Types (matching Emby's backend types)
        public const string Video = nameof(Video);
        public const string Photo = nameof(Photo);

        // Book Types
        public const string Book = nameof(Book);
        public const string AudioBook = nameof(AudioBook);

        // Live TV Types
        // LiveTvChannel media type: Not supported in Playlists (Emby's PlaylistManager drops any
        // item whose SupportsAddingToPlaylist is false, and LiveTvChannel never overrides it) but
        // supported in Collections. Note: LiveTvChannel.GetClientTypeName() returns ItemKinds.TvChannel
        // at runtime, so the runtime type-name switches in OperandFactory/AutoRefreshService match the
        // LiveTvChannel class directly instead of going through ItemKindToMediaType.
        public const string LiveTvChannel = nameof(LiveTvChannel);

        // Container Types
        // Collection media type (BoxSet): Not supported in Playlists (Emby playlists can only
        // contain media items; containers are silently dropped) but supported in Collections
        public const string Collection = nameof(Collection);

        // Playlist media type: Not supported in Playlists (same pattern as Collection)
        // but supported in Collections
        public const string Playlist = nameof(Playlist);

        // Fallback Type
        public const string Unknown = nameof(Unknown);

        /// <summary>
        /// Centralized mapping between ItemKind and MediaTypes.
        /// This is the single source of truth for all media type mappings.
        /// </summary>
        public static readonly Dictionary<string, string> ItemKindToMediaType = new()
        {
            { ItemKinds.Episode, Episode },
            { ItemKinds.Movie, Movie },
            { ItemKinds.Audio, Audio },
            { ItemKinds.MusicVideo, MusicVideo },
            { ItemKinds.Video, Video },
            { ItemKinds.Photo, Photo },
            { ItemKinds.Book, Book },
            { ItemKinds.AudioBook, AudioBook },
            // Series: Supported in Collections, not in Playlists
            { ItemKinds.Series, Series },
            // Season: Supported in Collections, not in Playlists
            { ItemKinds.Season, Season },
            // MusicAlbum: Supported in Collections, not in Playlists
            { ItemKinds.MusicAlbum, MusicAlbum },
            // LiveTvChannel: Supported in Collections, not in Playlists
            { ItemKinds.LiveTvChannel, LiveTvChannel },
            // Collection: Supported in Collections, not in Playlists
            { ItemKinds.BoxSet, Collection },
            // Playlist: Supported in Collections, not in Playlists
            { ItemKinds.Playlist, Playlist }
        };

        /// <summary>
        /// Reverse mapping from MediaTypes to ItemKind.
        /// </summary>
        public static readonly Dictionary<string, string> MediaTypeToItemKind = new()
        {
            { Episode, ItemKinds.Episode },
            { Movie, ItemKinds.Movie },
            { Audio, ItemKinds.Audio },
            { MusicVideo, ItemKinds.MusicVideo },
            { Video, ItemKinds.Video },
            { Photo, ItemKinds.Photo },
            { Book, ItemKinds.Book },
            { AudioBook, ItemKinds.AudioBook },
            // Series: Supported in Collections, not in Playlists
            { Series, ItemKinds.Series },
            // Season: Supported in Collections, not in Playlists
            { Season, ItemKinds.Season },
            // MusicAlbum: Supported in Collections, not in Playlists
            { MusicAlbum, ItemKinds.MusicAlbum },
            // LiveTvChannel: Supported in Collections, not in Playlists
            { LiveTvChannel, ItemKinds.LiveTvChannel },
            // Collection: Supported in Collections, not in Playlists
            { Collection, ItemKinds.BoxSet },
            // Playlist: Supported in Collections, not in Playlists
            { Playlist, ItemKinds.Playlist }
        };

        /// <summary>
        /// Gets all supported media types as an array (includes Series and container types for Collections support)
        /// </summary>
        public static readonly string[] All = [.. ItemKindToMediaType
            .Select(static kvp => kvp.Value)];

        /// <summary>
        /// Gets non-audio media types (everything except Audio and AudioBook)
        /// </summary>
        public static readonly string[] NonAudioTypes = [Movie, Episode, Season, MusicVideo, Video, Photo, Book];

        /// <summary>
        /// Gets audio-only media types (Audio, AudioBook)
        /// </summary>
        public static readonly string[] AudioOnly = [Audio, AudioBook];

        /// <summary>
        /// Gets book media types (Book, AudioBook)
        /// </summary>
        public static readonly string[] BookTypes = [Book, AudioBook];

        /// <summary>
        /// Gets TV media types (Episode and Series)
        /// </summary>
        public static readonly string[] TV = [Episode, Series, Season];

        /// <summary>
        /// Gets music-related media types (Audio, AudioBook, MusicVideo)
        /// </summary>
        public static readonly string[] MusicRelated = [Audio, AudioBook, MusicVideo, MusicAlbum];

        /// <summary>
        /// Gets media types that can have video streams (excludes Photo, Audio, Book, AudioBook)
        /// </summary>
        public static readonly string[] VideoStreamCapable = [Movie, Episode, MusicVideo, Video];

        /// <summary>
        /// Gets container media types (Collection, Playlist) - only valid for smart collections,
        /// never for smart playlists (Emby playlists can only contain media items)
        /// </summary>
        public static readonly string[] ContainerTypes = [Collection, Playlist];

        // HashSet variants for O(1) membership checks (performance optimization)

        /// <summary>
        /// HashSet variant of AudioOnly for O(1) membership checks
        /// </summary>
        public static readonly HashSet<string> AudioOnlySet = new(AudioOnly, StringComparer.Ordinal);

        /// <summary>
        /// HashSet variant of NonAudioTypes for O(1) membership checks
        /// </summary>
        public static readonly HashSet<string> NonAudioSet = new(NonAudioTypes, StringComparer.Ordinal);

        /// <summary>
        /// HashSet variant of BookTypes for O(1) membership checks
        /// </summary>
        public static readonly HashSet<string> BookTypesSet = new(BookTypes, StringComparer.Ordinal);

        /// <summary>
        /// HashSet variant of MusicRelated for O(1) membership checks
        /// </summary>
        public static readonly HashSet<string> MusicRelatedSet = new(MusicRelated, StringComparer.Ordinal);

        /// <summary>
        /// HashSet variant of VideoStreamCapable for O(1) membership checks
        /// </summary>
        public static readonly HashSet<string> VideoStreamCapableSet = new(VideoStreamCapable, StringComparer.Ordinal);

        /// <summary>
        /// HashSet variant of ContainerTypes for O(1) membership checks
        /// </summary>
        public static readonly HashSet<string> ContainerTypesSet = new(ContainerTypes, StringComparer.Ordinal);

        /// <summary>
        /// Checks whether a media type is a container type (Collection or Playlist)
        /// </summary>
        public static bool IsContainerType(string mediaType) => ContainerTypesSet.Contains(mediaType);

        /// <summary>
        /// Gets ItemKind array for audio-only content (derived from centralized mapping)
        /// </summary>
        public static string[] GetAudioOnlyItemKinds() =>
            AudioOnly.Select(mediaType => MediaTypeToItemKind[mediaType]).ToArray();

        /// <summary>
        /// Gets ItemKind array for non-audio content (derived from centralized mapping)
        /// </summary>
        public static string[] GetNonAudioItemKinds() =>
            NonAudioTypes.Select(mediaType => MediaTypeToItemKind[mediaType]).ToArray();

    }
}
