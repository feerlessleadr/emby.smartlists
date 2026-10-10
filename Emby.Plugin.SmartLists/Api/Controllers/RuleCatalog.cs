using System;
using System.Collections.Generic;
using System.Linq;
using Emby.Plugin.SmartLists.Core.Constants;
using Emby.Plugin.SmartLists.Core.QueryEngine;

namespace Emby.Plugin.SmartLists.Api.Controllers
{
    /// <summary>
    /// Everything an API client (such as a mobile app) needs to build rules and sorts without hard-coding
    /// them: every rule field with its input type, operators and allowed values, the sort options, the
    /// operators with labels, and the value formats. Served at <c>GET /Plugins/SmartLists/catalog</c>;
    /// <c>GET /Plugins/SmartLists/info</c> reports the API version a client can check at connect time.
    /// The web page keeps its own copies of the sort list and value lists (config-core.js, config-rules.js);
    /// a unit test compares the sort list here with config-core.js so the two cannot drift.
    /// </summary>
    public static class RuleCatalog
    {
        /// <summary>
        /// Version of the HTTP API. Increase it only for a change that breaks existing clients
        /// (removed or renamed endpoints, properties or values); additions do not change it.
        /// </summary>
        public const int ApiVersion = 1;

        /// <summary>
        /// One selectable sort. <see cref="Directionless"/> sorts have no ascending/descending choice;
        /// <see cref="RoundRobin"/> sorts group items by a field (SortOption.GroupByField).
        /// </summary>
        public sealed record SortInfo(string Value, string Label, string Group, bool Directionless, bool RoundRobin);

        /// <summary>
        /// Gets the sorts a client can offer. Mirrors SmartLists.SORT_OPTIONS in config-core.js (without the hidden
        /// "External List Order").
        /// </summary>
        public static IReadOnlyList<SortInfo> Sorts { get; } =
        [
            new("Name", "Name", "General", false, false),
            new("Random", "Random", "General", true, false),
            new("NoOrder", "Default", "General", true, false),
            new("DateCreated", "Date Created", "Dates", false, false),
            new("ReleaseDate", "Release Date", "Dates", false, false),
            new("ProductionYear", "Production Year", "Dates", false, false),
            new("CommunityRating", "Community Rating", "Ratings & Playback", false, false),
            new("PlayCount (owner)", "Play Count (owner)", "Ratings & Playback", false, false),
            new("PlayCount (all users)", "Play Count (all users)", "Ratings & Playback", false, false),
            new("LastPlayed (owner)", "Last Played (owner)", "Ratings & Playback", false, false),
            new("LastPlayed (all users)", "Last Played (all users)", "Ratings & Playback", false, false),
            new("SeriesName", "Series Name", "TV", false, false),
            new("SeasonNumber", "Season Number", "TV", false, false),
            new("EpisodeNumber", "Episode Number", "TV", false, false),
            new("LastEpisodeAirDate", "Last Episode Air Date", "TV", false, false),
            new("AlbumName", "Album Name", "Music", false, false),
            new("Artist", "Artist", "Music", false, false),
            new("TrackNumber", "Track Number", "Music", false, false),
            new("Runtime", "Runtime", "Media Info", false, false),
            new("Resolution", "Resolution", "Media Info", false, false),
            new("Similarity", "Similarity (requires Similar To rule)", "Rule-Based", false, false),
            new("Rule Block Order", "Rule Group Order", "Rule-Based", false, false),
            new("Round Robin", "Round Robin (Interleave)", "Round Robin", false, true),
            new("Random Round Robin", "Random Round Robin (Interleave)", "Round Robin", true, true),
            new("Shuffled Round Robin", "Shuffled Round Robin (Interleave)", "Round Robin", true, true),
            new("Least Recently Watched Round Robin", "Least Recently Watched Round Robin (Interleave)", "Round Robin", true, true),
            new("Most Recently Watched Round Robin", "Most Recently Watched Round Robin (Interleave)", "Round Robin", true, true),
        ];

        private static readonly (string Value, string Label)[] PlaybackStatuses =
        [
            ("Played", "Played (Fully Played)"),
            ("InProgress", "In Progress (Partially Played)"),
            ("Unplayed", "Unplayed (Not Started)"),
        ];

        private static readonly (string Value, string Label)[] SeriesStatuses =
        [
            ("Continuing", "Continuing"),
            ("Ended", "Ended"),
            ("Unreleased", "Unreleased"),
        ];

        private static readonly (string Value, string Label)[] ExtraTypes =
        [
            ("BehindTheScenes", "Behind the Scenes"),
            ("Clip", "Clip"),
            ("DeletedScene", "Deleted Scene"),
            ("Featurette", "Featurette"),
            ("Interview", "Interview"),
            ("Sample", "Sample"),
            ("Scene", "Scene"),
            ("Short", "Short"),
            ("ThemeSong", "Theme Song"),
            ("ThemeVideo", "Theme Video"),
            ("Trailer", "Trailer"),
            ("Unknown", "Unknown"),
        ];

        private static readonly (string Value, string Label)[] Resolutions =
        [
            ("480p", "480p (854x480)"),
            ("720p", "720p (1280x720)"),
            ("1080p", "1080p (1920x1080)"),
            ("1440p", "1440p (2560x1440)"),
            ("4K", "4K (3840x2160)"),
            ("8K", "8K (7680x4320)"),
        ];

        private static readonly (string Value, string Label)[] RelativeDateUnits =
        [
            ("hours", "Hour(s)"),
            ("days", "Day(s)"),
            ("weeks", "Week(s)"),
            ("months", "Month(s)"),
            ("years", "Year(s)"),
        ];

        private static readonly (string Value, string Label)[] Weekdays =
        [
            ("0", "Sunday"),
            ("1", "Monday"),
            ("2", "Tuesday"),
            ("3", "Wednesday"),
            ("4", "Thursday"),
            ("5", "Friday"),
            ("6", "Saturday"),
        ];

        private static readonly (string Value, string Label)[] GroupByFields =
        [
            ("SeriesName", "Series Name"),
            ("AlbumName", "Album Name"),
            ("Artist", "Artist"),
            ("Genres", "Genres"),
            ("Studios", "Studios"),
            ("Collections", "Collections"),
        ];

        private static readonly (string Value, string Label)[] AutoRefreshModes =
        [
            ("Never", "Manual only"),
            ("OnLibraryChanges", "When items are added to the library"),
            ("OnAllChanges", "On any change (including playback)"),
        ];

        /// <summary>
        /// Gets the finite value choices for a field, or null when the field takes free input.
        /// </summary>
        private static IEnumerable<object>? ValuesFor(string fieldName) => fieldName switch
        {
            "PlaybackStatus" => ToOptions(PlaybackStatuses),
            "SeriesStatus" => ToOptions(SeriesStatuses),
            "ExtraType" => ToOptions(ExtraTypes),
            "Resolution" => ToOptions(Resolutions),
            _ => null,
        };

        private static IEnumerable<object> ToOptions(IEnumerable<(string Value, string Label)> options)
            => options.Select(o => (object)new { o.Value, o.Label }).ToArray();

        /// <summary>
        /// Builds the info document for <c>GET /info</c>.
        /// </summary>
        /// <param name="pluginVersion">The plugin's assembly version.</param>
        /// <returns>The info object.</returns>
        public static object BuildInfo(string pluginVersion) => new
        {
            Name = "SmartLists",
            PluginVersion = pluginVersion,
            ApiVersion,
        };

        /// <summary>
        /// Builds the catalog for <c>GET /catalog</c>.
        /// </summary>
        /// <returns>The catalog object.</returns>
        public static object Build()
        {
            var fields = Enum.GetValues<FieldCategory>()
                .Where(c => c != FieldCategory.SimilarityComparison)
                .SelectMany(c => FieldRegistry.GetFieldsByCategory(c))
                .Where(f => f.Name != "ItemType")
                .GroupBy(f => f.Name, StringComparer.Ordinal)
                .Select(g => g.First())
                .Select(f => new
                {
                    f.Name,
                    Label = f.DisplayLabel,
                    Category = f.Category.ToString(),
                    Type = f.Type.ToString(),
                    Operators = f.AllowedOperators,
                    UserSpecific = f.IsUserSpecific,
                    Values = ValuesFor(f.Name),
                })
                .ToArray();

            return new
            {
                ApiVersion,
                Fields = fields,
                Operators = Core.Constants.Operators.AllOperators,
                Sorts = Sorts,
                RoundRobinGroupByFields = ToOptions(GroupByFields),
                WithinGroupOrders = ToOptions([("Natural", "Natural order (season/episode, disc/track, name)"), ("AirDate", "Air date")]),
                MediaTypes = MediaTypes.All,
                AutoRefreshModes = ToOptions(AutoRefreshModes),
                RelativeDateUnits = ToOptions(RelativeDateUnits),
                Weekdays = ToOptions(Weekdays),
            };
        }
    }
}
