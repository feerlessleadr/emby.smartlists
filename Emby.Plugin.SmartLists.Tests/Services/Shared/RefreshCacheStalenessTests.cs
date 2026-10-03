using System.Reflection;
using Emby.Plugin.SmartLists.Core.QueryEngine;
using Emby.Plugin.SmartLists.Services.Shared;
using Emby.Plugin.SmartLists.Tests.Support;
using Emby.Plugin.SmartLists.Utilities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;

namespace Emby.Plugin.SmartLists.Tests.Services.Shared;

/// <summary>
/// Pins the per-drain staleness fix on <see cref="RefreshQueueService.RefreshCache"/>.
///
/// The container snapshots (<c>AllCollections</c>/<c>AllPlaylists</c>) and the membership caches
/// behind them are built ONCE per refresh-queue drain, while the cache itself is per user and lives
/// until the drain finishes. So when list A rewrote its own Emby container mid-drain, every list
/// refreshed after it in that drain still evaluated its Collections/Playlists rules against A's
/// pre-drain contents - permanently one refresh behind while lists refresh together, which is the
/// normal case under scheduled auto-refresh.
///
/// Reproduced live before the fix: playlist "ZZTest Alpha" changed from 33 to 49 members, and
/// "ZZTest Beta" (rule: Playlists contains "ZZTest Alpha") reported 33 in the same drain and only
/// caught up on a later one.
///
/// The precondition is easy to miss when reproducing by hand: the earlier list must itself use the
/// Collections/Playlists extractor, because that is what seeds the snapshot BEFORE it writes. A
/// first attempt with a Name-only rule on A came back green for exactly that reason.
/// </summary>
public class RefreshCacheStalenessTests
{
    private static readonly MethodInfo ExtractPlaylistsMethod =
        typeof(OperandFactory).GetMethod("ExtractPlaylists", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo ExtractCollectionsMethod =
        typeof(OperandFactory).GetMethod("ExtractCollections", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static List<string> ExtractPlaylists(BaseItem item, RefreshQueueService.RefreshCache cache)
        => (List<string>)ExtractPlaylistsMethod.Invoke(
            null, [item, TestItems.User, BaseItem.LibraryManager, cache, null, null])!;

    private static List<string> ExtractCollections(BaseItem item, RefreshQueueService.RefreshCache cache, int depth)
        => (List<string>)ExtractCollectionsMethod.Invoke(
            null, [item, TestItems.User, BaseItem.LibraryManager, cache, null, depth, null])!;

    private static Playlist PlaylistNamed(string name)
    {
        var playlist = new Playlist { Id = Guid.NewGuid(), InternalId = TestItems.NextId(), Name = name };
        playlist.SortName = name;
        return playlist;
    }

    private static BoxSet CollectionNamed(string name)
    {
        var boxSet = new BoxSet { Id = Guid.NewGuid(), InternalId = TestItems.NextId(), Name = name };
        boxSet.SortName = name;
        return boxSet;
    }

    /// <summary>
    /// A movie added to a playlist mid-drain must be visible to the next list in that same drain.
    /// </summary>
    [Fact]
    public void OnPlaylistWritten_MakesNewMembershipVisibleWithinTheSameDrain()
    {
        var inPlaylist = TestItems.Mov("Blade Runner");
        var addedLater = TestItems.Mov("Alien");
        var playlist = PlaylistNamed("ZZTest Alpha [Smart]");

        var cache = new RefreshQueueService.RefreshCache();
        cache.AllPlaylists = [playlist];
        cache.PlaylistMembershipCache[playlist.InternalId] = [inPlaylist.InternalId];

        // Seeds the per-drain snapshot, exactly as the earlier list's own refresh does.
        Assert.Equal(["ZZTest Alpha [Smart]"], ExtractPlaylists(inPlaylist, cache));
        Assert.Empty(ExtractPlaylists(addedLater, cache));

        cache.OnPlaylistWritten(playlist, [inPlaylist.InternalId, addedLater.InternalId]);

        Assert.Equal(["ZZTest Alpha [Smart]"], ExtractPlaylists(addedLater, cache));
    }

    /// <summary>
    /// ...and a movie removed from it must stop being reported, which is the half that made a
    /// "not in any smart playlist" rule keep excluding items it should have released.
    /// </summary>
    [Fact]
    public void OnPlaylistWritten_RetractsRemovedMembershipWithinTheSameDrain()
    {
        var dropped = TestItems.Mov("Blade Runner");
        var kept = TestItems.Mov("Alien");
        var playlist = PlaylistNamed("ZZTest Alpha [Smart]");

        var cache = new RefreshQueueService.RefreshCache();
        cache.AllPlaylists = [playlist];
        cache.PlaylistMembershipCache[playlist.InternalId] = [dropped.InternalId, kept.InternalId];

        Assert.Equal(["ZZTest Alpha [Smart]"], ExtractPlaylists(dropped, cache));

        cache.OnPlaylistWritten(playlist, [kept.InternalId]);

        Assert.Empty(ExtractPlaylists(dropped, cache));
        Assert.Equal(["ZZTest Alpha [Smart]"], ExtractPlaylists(kept, cache));
    }

    /// <summary>
    /// The collection side of the same fix.
    /// </summary>
    [Fact]
    public void OnCollectionWritten_MakesNewMembershipVisibleWithinTheSameDrain()
    {
        var inCollection = TestItems.Mov("Blade Runner");
        var addedLater = TestItems.Mov("Alien");
        var boxSet = CollectionNamed("ZZTest Alpha [Smart]");

        var cache = new RefreshQueueService.RefreshCache();
        cache.AllCollections = [boxSet];
        cache.CollectionDirectChildren[boxSet.InternalId] = [inCollection];

        Assert.Equal(["ZZTest Alpha [Smart]"], ExtractCollections(inCollection, cache, depth: 1));
        Assert.Empty(ExtractCollections(addedLater, cache, depth: 1));

        cache.OnCollectionWritten(boxSet, [inCollection, addedLater]);

        Assert.Equal(["ZZTest Alpha [Smart]"], ExtractCollections(addedLater, cache, depth: 1));
    }

    /// <summary>
    /// A container created mid-drain is absent from the snapshot altogether, so it has to be added
    /// rather than merely patched.
    /// </summary>
    [Fact]
    public void OnCollectionWritten_AddsACollectionCreatedDuringTheDrain()
    {
        var movie = TestItems.Mov("Blade Runner");
        var existing = CollectionNamed("Existing [Smart]");
        var createdMidDrain = CollectionNamed("ZZTest New [Smart]");

        var cache = new RefreshQueueService.RefreshCache();
        cache.AllCollections = [existing];
        cache.CollectionDirectChildren[existing.InternalId] = [];

        Assert.Empty(ExtractCollections(movie, cache, depth: 1));

        cache.OnCollectionWritten(createdMidDrain, [movie]);

        Assert.Equal(["ZZTest New [Smart]"], ExtractCollections(movie, cache, depth: 1));
    }

    /// <summary>
    /// Patching must never seed an UNBUILT membership cache: the builders are guarded on the
    /// dictionary being empty, so a single seeded entry would make an empty cache look built and
    /// strand every other container with no children.
    /// </summary>
    [Fact]
    public void OnContainerWritten_DoesNotSeedAnUnbuiltMembershipCache()
    {
        var movie = TestItems.Mov("Blade Runner");
        var boxSet = CollectionNamed("ZZTest Alpha [Smart]");
        var playlist = PlaylistNamed("ZZTest Alpha [Smart]");

        var cache = new RefreshQueueService.RefreshCache();

        cache.OnCollectionWritten(boxSet, [movie]);
        cache.OnPlaylistWritten(playlist, [movie.InternalId]);

        Assert.True(cache.CollectionDirectChildren.IsEmpty);
        Assert.True(cache.PlaylistMembershipCache.IsEmpty);
    }

    /// <summary>
    /// "Hide when empty" deletes the Emby container mid-drain; lists refreshed after it must
    /// stop seeing it.
    /// </summary>
    [Fact]
    public void OnContainerRemoved_HidesADeletedCollectionFromTheRestOfTheDrain()
    {
        var movie = TestItems.Mov("Blade Runner");
        var boxSet = CollectionNamed("ZZTest Alpha [Smart]");

        var cache = new RefreshQueueService.RefreshCache();
        cache.AllCollections = [boxSet];
        cache.CollectionDirectChildren[boxSet.InternalId] = [movie];

        Assert.Equal(["ZZTest Alpha [Smart]"], ExtractCollections(movie, cache, depth: 1));

        cache.OnContainerRemoved(boxSet.InternalId);

        Assert.Empty(ExtractCollections(movie, cache, depth: 1));
    }

    /// <summary>
    /// A favorite set mid-drain (UserDataSaved) must not be shadowed by the value an earlier refresh
    /// in that drain cached - parent-favorite refreshes read every album's user data on their way.
    /// Only the saved (item, user) pair goes; other users and other items keep their entries.
    /// </summary>
    [Fact]
    public void InvalidateUserData_DropsOnlyThatItemAndUsersEntry()
    {
        var album = TestItems.Album("Kind of Blue");
        var otherAlbum = TestItems.Album("Blue Train");

        var cache = new RefreshQueueService.RefreshCache();
        TestItems.SeedUserData(cache, album, TestItems.User);
        TestItems.SeedUserData(cache, album, TestItems.OtherUser);
        TestItems.SeedUserData(cache, otherAlbum, TestItems.User);
        TestItems.SeedNoUserData(cache, album, TestItems.User);

        cache.InvalidateUserData(album.InternalId, TestItems.User.Id);

        Assert.False(cache.UserDataCache.ContainsKey((album.InternalId, TestItems.User.Id)));
        Assert.False(cache.UserDataNegativeCache.ContainsKey((album.InternalId, TestItems.User.Id)));
        Assert.True(cache.UserDataCache.ContainsKey((album.InternalId, TestItems.OtherUser.Id)));
        Assert.True(cache.UserDataCache.ContainsKey((otherAlbum.InternalId, TestItems.User.Id)));
    }

    /// <summary>
    /// A save that lands while a cache-miss read is in flight must not leave that read's (possibly
    /// pre-save) value cached: the invalidation ran before the read stored it, so nothing else would
    /// ever remove it, and every later refresh in the drain would read the stale favorite state.
    /// The value is still returned to the caller that raced the save.
    /// </summary>
    [Fact]
    public void GetCachedUserData_SaveDuringRead_ReturnsButDoesNotCacheTheValue()
    {
        var album = TestItems.Album("Kind of Blue");
        var cache = new RefreshQueueService.RefreshCache();
        var stale = new UserItemData { Key = album.Id.ToString("N"), IsFavorite = false };
        var manager = ReadingUserData(stale, () => cache.InvalidateUserData(album.InternalId, TestItems.User.Id));

        var result = UserDataCacheHelper.GetCachedUserData(TestItems.User, album, cache, manager);

        Assert.Same(stale, result);
        Assert.False(cache.UserDataCache.ContainsKey((album.InternalId, TestItems.User.Id)));
        Assert.False(cache.UserDataNegativeCache.ContainsKey((album.InternalId, TestItems.User.Id)));
    }

    [Fact]
    public void GetCachedUserData_NoSaveDuringRead_CachesTheValue()
    {
        var album = TestItems.Album("Kind of Blue");
        var cache = new RefreshQueueService.RefreshCache();
        var data = new UserItemData { Key = album.Id.ToString("N"), IsFavorite = true };

        UserDataCacheHelper.GetCachedUserData(TestItems.User, album, cache, ReadingUserData(data, () => { }));

        Assert.Same(data, cache.UserDataCache[(album.InternalId, TestItems.User.Id)]);
    }

    /// <summary>
    /// An <see cref="IUserDataManager"/> whose GetUserData runs <c>duringRead</c> (e.g. a concurrent
    /// save's invalidation) before returning <c>value</c>.
    /// </summary>
    public class ReadingUserDataManager : DispatchProxy
    {
        public UserItemData? Value { get; set; }

        public Action DuringRead { get; set; } = () => { };

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "GetUserData")
            {
                DuringRead();
                return Value;
            }

            throw new NotSupportedException($"ReadingUserDataManager: {targetMethod?.Name} is not stubbed.");
        }
    }

    private static IUserDataManager ReadingUserData(UserItemData? value, Action duringRead)
    {
        var proxy = DispatchProxy.Create<IUserDataManager, ReadingUserDataManager>();
        ((ReadingUserDataManager)proxy).Value = value;
        ((ReadingUserDataManager)proxy).DuringRead = duringRead;
        return proxy;
    }
}
