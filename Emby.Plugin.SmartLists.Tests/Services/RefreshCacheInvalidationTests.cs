using System;
using Emby.Plugin.SmartLists.Services.Shared;
using Emby.Plugin.SmartLists.Tests.Support;
using MediaBrowser.Controller.Entities;
using Xunit;

namespace Emby.Plugin.SmartLists.Tests.Services;

/// <summary>
/// A <see cref="RefreshQueueService.RefreshCache"/> lives for a whole queue drain - it is cleared
/// only once the queue empties - so a container that gains or loses a child mid-drain would keep
/// being scored from its old child list, including by the refresh the change itself queued. The
/// library Added/Removed handlers call into this to drop the affected entries.
/// </summary>
public class RefreshCacheInvalidationTests
{
    private static readonly Guid SomeUser = Guid.NewGuid();

    [Fact]
    public void AddingOrRemovingAnEpisode_DropsItsSeriesAndSeasonChildLists()
    {
        var series = TestItems.Show("Show");
        var season = TestItems.SeasonOf("Season 1");
        var episode = TestItems.Ep("Show", 1, 1, show: series);
        episode.ParentId = season.InternalId; // Emby: the season folder is the episode's parent

        var cache = new RefreshQueueService.RefreshCache();
        cache.SeriesEpisodesForAggregation[series.InternalId] = [episode];
        cache.SeasonEpisodesForAggregation[season.InternalId] = [episode];
        cache.SeriesEpisodes[(series.InternalId, SomeUser, false)] = [episode];
        cache.SeriesEpisodes[(series.InternalId, SomeUser, null)] = [episode];
        cache.SeasonEpisodes[(season.InternalId, SomeUser)] = [episode];
        cache.NextUnwatched[(series.InternalId, SomeUser, true)] = (episode.InternalId, 1, 1);

        var removed = cache.InvalidateContainerChildren(episode);

        Assert.Equal(6, removed);
        Assert.Empty(cache.SeriesEpisodesForAggregation);
        Assert.Empty(cache.SeasonEpisodesForAggregation);
        Assert.Empty(cache.SeriesEpisodes);
        Assert.Empty(cache.SeasonEpisodes);
        Assert.Empty(cache.NextUnwatched);
    }

    [Fact]
    public void InvalidationIsScopedToTheAffectedContainers()
    {
        var series = TestItems.Show("Show");
        var otherSeries = TestItems.Show("Other Show");
        var episode = TestItems.Ep("Show", 1, 1, show: series);

        var cache = new RefreshQueueService.RefreshCache();
        cache.SeriesEpisodesForAggregation[series.InternalId] = [episode];
        cache.SeriesEpisodesForAggregation[otherSeries.InternalId] = [];
        cache.SeriesEpisodes[(otherSeries.InternalId, SomeUser, false)] = [];

        cache.InvalidateContainerChildren(episode);

        Assert.False(cache.SeriesEpisodesForAggregation.ContainsKey(series.InternalId));
        Assert.True(cache.SeriesEpisodesForAggregation.ContainsKey(otherSeries.InternalId));
        Assert.Single(cache.SeriesEpisodes);
    }

    [Fact]
    public void AddingOrRemovingATrack_DropsItsAlbumChildList()
    {
        var album = TestItems.Album("Album");
        var track = TestItems.Under(TestItems.Track("Album", 1, 1), album);

        var cache = new RefreshQueueService.RefreshCache();
        cache.AlbumTracksForAggregation[album.InternalId] = [track];
        cache.AlbumTracks[(album.InternalId, SomeUser)] = [track];

        Assert.Equal(2, cache.InvalidateContainerChildren(track));
        Assert.Empty(cache.AlbumTracksForAggregation);
        Assert.Empty(cache.AlbumTracks);
    }

    /// <summary>A container removed outright must drop its own entry, not just its children's.</summary>
    [Fact]
    public void RemovingTheContainerItself_DropsItsChildList()
    {
        var series = TestItems.Show("Show");

        var cache = new RefreshQueueService.RefreshCache();
        cache.SeriesEpisodesForAggregation[series.InternalId] = [];

        Assert.Equal(1, cache.InvalidateContainerChildren(series));
        Assert.Empty(cache.SeriesEpisodesForAggregation);
    }

    /// <summary>
    /// Movies and the like have no child list, so a change to one must not scan or evict anything -
    /// these events fire constantly and the cache is shared by every list in the drain.
    /// </summary>
    [Fact]
    public void ChangingANonContainerChild_DropsNothing()
    {
        var series = TestItems.Show("Show");
        var movie = TestItems.Mov("Movie");

        var cache = new RefreshQueueService.RefreshCache();
        cache.SeriesEpisodesForAggregation[series.InternalId] = [];

        Assert.Equal(0, cache.InvalidateContainerChildren(movie));
        Assert.Single(cache.SeriesEpisodesForAggregation);
    }
}
