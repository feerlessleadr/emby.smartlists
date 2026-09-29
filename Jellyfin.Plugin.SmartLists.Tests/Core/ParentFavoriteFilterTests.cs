using Jellyfin.Plugin.SmartLists.Core;
using Jellyfin.Plugin.SmartLists.Core.Models;
using Jellyfin.Plugin.SmartLists.Core.QueryEngine;
using Jellyfin.Plugin.SmartLists.Services.Shared;
using Jellyfin.Plugin.SmartLists.Tests.Support;
using MediaBrowser.Controller.Entities;
using Expression = Jellyfin.Plugin.SmartLists.Core.QueryEngine.Expression;
using MediaTypeConstants = Jellyfin.Plugin.SmartLists.Core.Constants.MediaTypes;

namespace Jellyfin.Plugin.SmartLists.Tests.Core;

/// <summary>
/// Covers the "Include parent favorites" option of the IsFavorite rule (issue #535: a playlist of
/// tracks from favorited albums) from extraction through the full filter pipeline.
///
/// The end-to-end tests are the important ones. IsFavorite is a CHEAP (UserData) field, so a
/// parent-favorite rule that slipped into Phase 1 would be evaluated against an operand whose
/// ParentIsFavoriteByUser was never filled and would silently match nothing - only running the
/// real two-phase pipeline catches that.
///
/// HARNESS NOTE: user data is seeded into RefreshCache.UserDataCache and the user-data manager
/// throws on every call, which proves every ancestor lookup is answered from the per-refresh
/// cache (one lookup per (user, ancestor), not one per item).
/// </summary>
public class ParentFavoriteFilterTests
{
    private static void SeedNoUserData(RefreshQueueService.RefreshCache cache, params BaseItem[] items)
    {
        foreach (var item in items)
        {
            TestItems.SeedNoUserData(cache, item, TestItems.User);
            TestItems.SeedNoUserData(cache, item, TestItems.OtherUser);
        }
    }

    private static Operand Extract(BaseItem item, RefreshQueueService.RefreshCache cache, ExtractionGroup groups)
    {
        var options = new MediaTypeExtractionOptions
        {
            RequiredGroups = groups,
            AdditionalUserIds = [TestItems.OtherUser.Id.ToString("N")],
        };

        return OperandFactory.GetMediaType(
            BaseItem.LibraryManager,
            item,
            TestItems.User,
            TestItems.ThrowingUserData(),
            TestItems.UserManagerWithUsers(TestItems.User, TestItems.OtherUser),
            null,
            options,
            cache);
    }

    // ---------------------------------------------------------------------------------------
    // Factory - ParentIsFavoriteByUser population
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The album is a favorite for one user only. The flag is resolved per evaluated user - the
    /// list user AND every user a rule names - and keyed in "N" format like IsFavoriteByUser.
    /// </summary>
    [Fact]
    public void GetMediaType_FillsParentFavoritePerUser_FromAncestorUserData()
    {
        var top = TestItems.PhysicalFolder("music");
        var album = TestItems.Album("Kind of Blue");
        var track = TestItems.Track("Kind of Blue", 1, 1);
        TestItems.Under(album, top);
        TestItems.Under(track, album);

        var cache = new RefreshQueueService.RefreshCache();
        TestItems.SeedUserData(cache, album, TestItems.User, isFavorite: true);
        TestItems.SeedUserData(cache, album, TestItems.OtherUser, isFavorite: false);
        SeedNoUserData(cache, top, track);

        var operand = Extract(track, cache, ExtractionGroup.UserData | ExtractionGroup.ParentFavorite);

        Assert.True(operand.ParentIsFavoriteByUser[TestItems.User.Id.ToString("N")]);
        Assert.False(operand.ParentIsFavoriteByUser[TestItems.OtherUser.Id.ToString("N")]);

        // The track's own flag is untouched by the ancestor walk.
        Assert.False(operand.GetIsFavoriteByUser(TestItems.User.Id.ToString("N")));
    }

    /// <summary>
    /// A favorite anywhere up the chain counts - here the top folder two levels above the track.
    /// </summary>
    [Fact]
    public void GetMediaType_ParentFavorite_FindsAFavoriteAboveTheImmediateParent()
    {
        var top = TestItems.PhysicalFolder("music");
        var album = TestItems.Album("A Love Supreme");
        var track = TestItems.Track("A Love Supreme", 1, 1);
        TestItems.Under(album, top);
        TestItems.Under(track, album);

        var cache = new RefreshQueueService.RefreshCache();
        TestItems.SeedUserData(cache, top, TestItems.User, isFavorite: true);
        SeedNoUserData(cache, album, track);
        TestItems.SeedNoUserData(cache, top, TestItems.OtherUser);

        var operand = Extract(track, cache, ExtractionGroup.UserData | ExtractionGroup.ParentFavorite);

        Assert.True(operand.GetParentIsFavoriteByUser(TestItems.User.Id.ToString("N")));
        Assert.False(operand.GetParentIsFavoriteByUser(TestItems.OtherUser.Id.ToString("N")));
    }

    /// <summary>
    /// Without the ParentFavorite group (Phase 1, or any list without the option) the walk does
    /// not run at all and the dictionary stays empty.
    /// </summary>
    [Fact]
    public void GetMediaType_WithoutParentFavoriteGroup_LeavesTheDictionaryEmpty()
    {
        var top = TestItems.PhysicalFolder("music");
        var album = TestItems.Album("Blue Train");
        var track = TestItems.Track("Blue Train", 1, 1);
        TestItems.Under(album, top);
        TestItems.Under(track, album);

        var cache = new RefreshQueueService.RefreshCache();
        TestItems.SeedUserData(cache, album, TestItems.User, isFavorite: true);
        SeedNoUserData(cache, top, track);

        var operand = Extract(track, cache, ExtractionGroup.UserData);

        Assert.Empty(operand.ParentIsFavoriteByUser);
        Assert.Empty(cache.AncestorItemsById);
    }

    // ---------------------------------------------------------------------------------------
    // End to end - SmartList.FilterPlaylistItems
    // ---------------------------------------------------------------------------------------

    private sealed record Library(BaseItem A1, BaseItem A2, BaseItem B1, BaseItem B2, RefreshQueueService.RefreshCache Cache)
    {
        public BaseItem[] Pool => [A1, A2, B1, B2];
    }

    /// <summary>
    /// A1 and A2 sit on an album the user favorited; B1 and B2 on one they did not, and B2 is an
    /// individually favorited track.
    /// </summary>
    private static Library BuildLibrary()
    {
        var top = TestItems.PhysicalFolder("music");
        var favoriteAlbum = TestItems.Album("Favorite Album");
        var otherAlbum = TestItems.Album("Other Album");
        TestItems.Under(favoriteAlbum, top);
        TestItems.Under(otherAlbum, top);

        var a1 = TestItems.Under(TestItems.Track("Favorite Album", 1, 1, name: "A1"), favoriteAlbum);
        var a2 = TestItems.Under(TestItems.Track("Favorite Album", 1, 2, name: "A2"), favoriteAlbum);
        var b1 = TestItems.Under(TestItems.Track("Other Album", 1, 1, name: "B1"), otherAlbum);
        var b2 = TestItems.Under(TestItems.Track("Other Album", 1, 2, name: "B2"), otherAlbum);

        var cache = new RefreshQueueService.RefreshCache();
        TestItems.SeedUserData(cache, favoriteAlbum, TestItems.User, isFavorite: true);
        TestItems.SeedUserData(cache, otherAlbum, TestItems.User, isFavorite: false);
        TestItems.SeedUserData(cache, b2, TestItems.User, isFavorite: true);
        SeedNoUserData(cache, top, a1, a2, b1);

        return new Library(a1, a2, b1, b2, cache);
    }

    private static string[] FilterNames(Library library, params Expression[] rules)
    {
        var dto = new SmartPlaylistDto
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Parent favorite test",
            MediaTypes = [MediaTypeConstants.Audio],
            ExpressionSets = [new ExpressionSet { Expressions = [.. rules] }],
        };

        var byId = library.Pool.ToDictionary(i => i.Id);
        var ids = new SmartList(dto).FilterPlaylistItems(
            library.Pool, BaseItem.LibraryManager, TestItems.User, library.Cache, TestItems.ThrowingUserData());

        return [.. ids.Select(id => byId[id].Name).Order(StringComparer.Ordinal)];
    }

    [Fact]
    public void FilterPlaylistItems_IncludeParentFavorite_MatchesTracksOnFavoriteAlbumsAndOwnFavorites()
    {
        var names = FilterNames(BuildLibrary(), new Expression("IsFavorite", "Equal", "true") { IncludeParentFavorite = true });

        Assert.Equal(["A1", "A2", "B2"], names);
    }

    [Fact]
    public void FilterPlaylistItems_OnlyParentFavorite_MatchesTracksOnFavoriteAlbumsOnly()
    {
        var names = FilterNames(
            BuildLibrary(),
            new Expression("IsFavorite", "Equal", "true") { IncludeParentFavorite = true, OnlyParentFavorite = true });

        Assert.Equal(["A1", "A2"], names);
    }

    /// <summary>
    /// "Is Favorite = No" with parents ANDs: a track on a favorited album is excluded even though
    /// the track itself is not a favorite.
    /// </summary>
    [Fact]
    public void FilterPlaylistItems_IncludeParentFavoriteEqualFalse_ExcludesTracksOnFavoriteAlbums()
    {
        var names = FilterNames(BuildLibrary(), new Expression("IsFavorite", "Equal", "false") { IncludeParentFavorite = true });

        Assert.Equal(["B1"], names);
    }

    /// <summary>
    /// With a cheap rule in the same set the list takes the real Phase 1 -> Phase 2 path: the
    /// cheap Name rule runs in Phase 1, the parent-favorite rule must wait for Phase 2.
    /// </summary>
    [Fact]
    public void FilterPlaylistItems_ParentFavoriteWithACheapRule_RunsThroughBothPhases()
    {
        var names = FilterNames(
            BuildLibrary(),
            new Expression("Name", "Contains", "2"),
            new Expression("IsFavorite", "Equal", "true") { IncludeParentFavorite = true });

        Assert.Equal(["A2", "B2"], names);
    }

    /// <summary>
    /// Default off: the plain rule matches only the individually favorited track.
    /// </summary>
    [Fact]
    public void FilterPlaylistItems_PlainIsFavorite_IgnoresAlbumFavorites()
    {
        var names = FilterNames(BuildLibrary(), new Expression("IsFavorite", "Equal", "true"));

        Assert.Equal(["B2"], names);
    }
}
