using Jellyfin.Plugin.SmartLists.Core.Models;
using Jellyfin.Plugin.SmartLists.Core.QueryEngine;
using Jellyfin.Plugin.SmartLists.Services.Shared;
using Jellyfin.Plugin.SmartLists.Tests.Support;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.SmartLists.Tests.Services.Shared;

/// <summary>
/// Pins the two predicates behind auto-refresh for the IsFavorite "Include parent favorites"
/// option. Favoriting an album, series, season or folder fires UserDataSaved for that CONTAINER
/// only, and the media-type caches route it to MusicAlbum/Series/Season lists - so an Audio or
/// Episode list matching on an ancestor's favorite would never be queued. A user-data change on a
/// folder item therefore also queues every list that <see cref="AutoRefreshService.UsesParentFavorite(IEnumerable{ExpressionSet})"/>.
/// </summary>
public class AutoRefreshParentFavoriteTests
{
    private static List<ExpressionSet> Sets(params Expression[] expressions)
        => [new ExpressionSet { Expressions = [.. expressions] }];

    [Fact]
    public void UsesParentFavorite_TrueOnlyForIsFavoriteRulesWithAParentFlag()
    {
        Assert.True(AutoRefreshService.UsesParentFavorite(Sets(new Expression("IsFavorite", "Equal", "true") { IncludeParentFavorite = true })));
        Assert.True(AutoRefreshService.UsesParentFavorite(Sets(new Expression("IsFavorite", "Equal", "true") { OnlyParentFavorite = true })));

        Assert.False(AutoRefreshService.UsesParentFavorite(Sets(new Expression("IsFavorite", "Equal", "true"))));
        Assert.False(AutoRefreshService.UsesParentFavorite(Sets(new Expression("Tags", "Equal", "Anime") { IncludeParentTags = true })));
        Assert.False(AutoRefreshService.UsesParentFavorite(null));
        Assert.False(AutoRefreshService.UsesParentFavorite([]));
        Assert.False(AutoRefreshService.UsesParentFavorite([new ExpressionSet()]));
    }

    /// <summary>
    /// Any rule set counts - the option in the second OR group still makes the list parent-aware.
    /// </summary>
    [Fact]
    public void UsesParentFavorite_FindsTheOptionInAnyRuleSet()
    {
        List<ExpressionSet> sets =
        [
            new ExpressionSet { Expressions = [new Expression("Name", "Contains", "live")] },
            new ExpressionSet { Expressions = [new Expression("IsFavorite", "Equal", "true") { IncludeParentFavorite = true }] },
        ];

        Assert.True(AutoRefreshService.UsesParentFavorite(sets));
    }

    [Fact]
    public void IsParentFavoriteTrigger_OnlyForUserDataChangesOnFolders()
    {
        var album = TestItems.Album("Kind of Blue");
        var season = TestItems.SeasonOf("Season 1");
        var track = TestItems.Track("Kind of Blue", 1, 1);
        var userId = Guid.NewGuid();

        Assert.True(AutoRefreshService.IsParentFavoriteTrigger(album, userId));
        Assert.True(AutoRefreshService.IsParentFavoriteTrigger(season, userId));

        // A leaf item is routed by its own media type already.
        Assert.False(AutoRefreshService.IsParentFavoriteTrigger(track, userId));

        // A library change (no triggering user) is not a favorite change.
        Assert.False(AutoRefreshService.IsParentFavoriteTrigger(album, null));
    }

    /// <summary>
    /// Un-favoriting an album whose earlier state was never seen (restart, state-cache eviction)
    /// arrives as an all-empty state - containers carry no play state of their own. Jellyfin saves a
    /// favorite toggle with UpdateUserRating, and explicit edits always count, so it is still routed.
    /// The same holds for a leaf item marked unplayed or un-favorited right after a restart.
    /// </summary>
    [Theory]
    [InlineData(UserDataSaveReason.UpdateUserRating)]
    [InlineData(UserDataSaveReason.TogglePlayed)]
    [InlineData(UserDataSaveReason.UpdateUserData)]
    public void IsRelevantFirstUserDataEvent_ExplicitEditWithEmptyState_IsRelevant(UserDataSaveReason reason)
    {
        Assert.True(AutoRefreshService.IsRelevantFirstUserDataEvent(new UserDataState(), reason));
    }

    /// <summary>
    /// Playback and import saves keep the old rule: an empty first state is an initial load, not a change.
    /// </summary>
    [Theory]
    [InlineData(UserDataSaveReason.PlaybackStart)]
    [InlineData(UserDataSaveReason.PlaybackProgress)]
    [InlineData(UserDataSaveReason.PlaybackFinished)]
    [InlineData(UserDataSaveReason.Import)]
    public void IsRelevantFirstUserDataEvent_PlaybackOrImportWithEmptyState_IsIgnored(UserDataSaveReason reason)
    {
        Assert.False(AutoRefreshService.IsRelevantFirstUserDataEvent(new UserDataState(), reason));
    }

    [Fact]
    public void IsRelevantFirstUserDataEvent_MeaningfulStateIsRelevantRegardless()
    {
        Assert.True(AutoRefreshService.IsRelevantFirstUserDataEvent(new UserDataState { IsFavorite = true }, UserDataSaveReason.Import));
        Assert.True(AutoRefreshService.IsRelevantFirstUserDataEvent(new UserDataState { Played = true }, UserDataSaveReason.PlaybackFinished));
        Assert.True(AutoRefreshService.IsRelevantFirstUserDataEvent(new UserDataState { PlayCount = 1 }, UserDataSaveReason.PlaybackFinished));
        Assert.True(AutoRefreshService.IsRelevantFirstUserDataEvent(new UserDataState { LastPlayedDate = DateTime.UtcNow }, UserDataSaveReason.PlaybackProgress));
    }
}
