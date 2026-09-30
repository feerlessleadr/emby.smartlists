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
/// Episode list matching on an ancestor's favorite would never be queued. A favorite change on a
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
    public void IsParentFavoriteTrigger_OnlyForFavoriteChangesOnFolders()
    {
        var album = TestItems.Album("Kind of Blue");
        var season = TestItems.SeasonOf("Season 1");
        var track = TestItems.Track("Kind of Blue", 1, 1);

        Assert.True(AutoRefreshService.IsParentFavoriteTrigger(album, favoriteMayHaveChanged: true));
        Assert.True(AutoRefreshService.IsParentFavoriteTrigger(season, favoriteMayHaveChanged: true));

        // A leaf item is routed by its own media type already.
        Assert.False(AutoRefreshService.IsParentFavoriteTrigger(track, favoriteMayHaveChanged: true));

        // Marking a season played, a play count change, etc. cannot change any parent-favorite state.
        Assert.False(AutoRefreshService.IsParentFavoriteTrigger(season, favoriteMayHaveChanged: false));
    }

    [Fact]
    public void FavoriteMayHaveChanged_WithPreviousState_IsExact()
    {
        var favorite = new UserDataState { IsFavorite = true };
        var notFavorite = new UserDataState { IsFavorite = false };

        Assert.True(AutoRefreshService.FavoriteMayHaveChanged(favorite, notFavorite, UserDataSaveReason.UpdateUserRating));
        Assert.True(AutoRefreshService.FavoriteMayHaveChanged(notFavorite, favorite, UserDataSaveReason.UpdateUserRating));

        // Played flipped, favorite didn't - whatever the save reason.
        Assert.False(AutoRefreshService.FavoriteMayHaveChanged(new UserDataState { Played = true }, notFavorite, UserDataSaveReason.TogglePlayed));
        Assert.False(AutoRefreshService.FavoriteMayHaveChanged(notFavorite, notFavorite, UserDataSaveReason.UpdateUserRating));
    }

    /// <summary>
    /// First-seen events have no previous state: the saves that carry favorite toggles count
    /// (Jellyfin's MarkFavorite saves with UpdateUserRating), mark-played and playback saves don't.
    /// </summary>
    [Fact]
    public void FavoriteMayHaveChanged_FirstSeen_CountsFavoriteCarryingSaves()
    {
        var empty = new UserDataState();

        Assert.True(AutoRefreshService.FavoriteMayHaveChanged(empty, null, UserDataSaveReason.UpdateUserRating));
        Assert.True(AutoRefreshService.FavoriteMayHaveChanged(empty, null, UserDataSaveReason.UpdateUserData));
        Assert.True(AutoRefreshService.FavoriteMayHaveChanged(new UserDataState { IsFavorite = true }, null, UserDataSaveReason.Import));

        Assert.False(AutoRefreshService.FavoriteMayHaveChanged(empty, null, UserDataSaveReason.TogglePlayed));
        Assert.False(AutoRefreshService.FavoriteMayHaveChanged(new UserDataState { Played = true }, null, UserDataSaveReason.PlaybackFinished));
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
