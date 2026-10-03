using MediaBrowser.Model.Globalization;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using MediaBrowser.Controller.Entities;
using Emby.Plugin.SmartLists.Services.Shared;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Movie = MediaBrowser.Controller.Entities.Movies.Movie;

namespace Emby.Plugin.SmartLists.Tests.Support;

/// <summary>
/// Stand-in for the server's ILibraryManager, needed because <c>Episode.Series</c> is not a stored
/// property - its getter calls <c>BaseItem.LibraryManager.GetItemById(SeriesId)</c>, which throws
/// <see cref="NullReferenceException"/> when no library manager has been installed.
///
/// That matters for real coverage, not convenience: RoundRobinBase.CompareWithinGroupByAirDate
/// reads <c>episode.Series?.SortName</c> to break same-day ties between episodes of DIFFERENT
/// series - the crossover-night ordering users get by editing a series' Sort Title. Without a
/// library manager that comparison throws, so every air-block test would have to avoid the exact
/// case air blocks exist for.
///
/// Only <c>GetItemById(Guid)</c> and the one-argument <c>GetCollectionFolders(BaseItem)</c> are
/// implemented. Every other member throws <see cref="NotSupportedException"/> on purpose: if
/// production code under test ever starts depending on more of the library manager, these tests
/// must fail loudly rather than quietly sort against a default-valued stub.
/// </summary>
public class TestLibraryManager : DispatchProxy
{
    /// <summary>
    /// Id → item, for <c>GetItemById</c>. Process-wide and never cleared - ids are GUIDs, so
    /// entries from different test classes cannot collide, and leaving them in place keeps the
    /// stub safe under xUnit's parallel test collections.
    /// </summary>
    internal static readonly ConcurrentDictionary<long, BaseItem> Items = new();

    /// <summary>
    /// Answers <c>GetCollectionFolders(BaseItem)</c>, keyed by the id of the item the resolver
    /// passes as the anchor - i.e. the CHAIN-TOP folder, deliberately NOT the leaf item id.
    ///
    /// This arm exists because a Emby library (a <c>CollectionFolder</c>) is never in an
    /// item's <c>ParentId</c> chain: it hangs off the UserRootFolder as a sibling structure.
    /// A parents-only walk therefore finds season tags but never library tags, so without this
    /// stub the library half of the ancestor walk could not be tested at all.
    /// </summary>
    internal static readonly ConcurrentDictionary<long, List<Folder>> CollectionFolders = new();

    /// <summary>
    /// Answers <c>GetVirtualFolders()</c>. Empty by default, so the path-matching supplement in
    /// <c>LibraryManagerHelper.GetLibraryFoldersForItemPath</c> contributes nothing unless a test
    /// deliberately configures a virtual folder (the symlinked-library case).
    /// </summary>
    internal static readonly List<VirtualFolderInfo> VirtualFolders = [];

    /// <summary>
    /// Per-id <c>GetItemById</c> call counter. Keyed by id rather than being a single total so
    /// it stays meaningful under xUnit's parallel test collections: ids are freshly generated
    /// per test, so no other class can perturb the count for the ids one test cares about.
    /// </summary>
    internal static readonly ConcurrentDictionary<long, int> GetItemByIdCalls = new();

    /// <summary>
    /// Answers <c>GetItemList(InternalItemsQuery)</c>, keyed by the query's <c>ParentId</c>.
    /// Used to test proactive container-cache warming (SmartList's aggregate-user cache warm-up)
    /// without needing a live Emby: tests register the children a container "has" here, then
    /// assert the refresh cache picks them up for every configured aggregate user.
    /// </summary>
    internal static readonly ConcurrentDictionary<long, List<BaseItem>> ItemListByParentId = new();

    /// <summary>Total <c>GetItemById</c> calls recorded for the given ids.</summary>
    internal static int CallsFor(params long[] ids)
        => ids.Sum(id => GetItemByIdCalls.TryGetValue(id, out var count) ? count : 0);

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        // Emby's GetItemById has a long (internal id) overload, which the plugin uses, and a Guid overload.
        if (targetMethod?.Name == "GetItemById" && args is { Length: 1 or 2 } && args[0] is long id)
        {
            GetItemByIdCalls.AddOrUpdate(id, 1, (_, count) => count + 1);
            return Items.TryGetValue(id, out var item) ? item : null;
        }

        if (targetMethod?.Name == "GetItemById" && args is { Length: 1 or 2 } && args[0] is Guid guid)
        {
            var byGuid = Items.Values.FirstOrDefault(i => i.Id == guid);
            if (byGuid != null)
            {
                GetItemByIdCalls.AddOrUpdate(byGuid.InternalId, 1, (_, count) => count + 1);
            }

            return byGuid;
        }

        if (targetMethod?.Name == "GetCollectionFolders" && args is { Length: 1 } && args[0] is BaseItem anchor)
        {
            // Emby's GetCollectionFolders returns Folder[].
            return CollectionFolders.TryGetValue(anchor.InternalId, out var folders) ? folders.ToArray() : Array.Empty<Folder>();
        }

        // Emby loads BaseItem.Genres / Studios / Tags lazily through GetItemLinks the first time they are read.
        // Fixture items carry no stored links, so answer with an empty list of the requested tuple type.
        if (targetMethod?.Name == "GetItemLinks")
        {
            return Activator.CreateInstance(targetMethod.ReturnType);
        }

        if (targetMethod?.Name == "GetVirtualFolders" && args is null or { Length: 0 })
        {
            return VirtualFolders.ToList();
        }

        if (targetMethod?.Name == "GetItemList" && args is { Length: 1 } && args[0] is InternalItemsQuery query && query.ParentIds is { Length: 1 })
        {
            // Fail loudly rather than answering []: an unregistered parent means the test reached a
            // query it never set up, and a silent empty result would be asserted against as if real.
            // Emby's GetItemList returns BaseItem[].
            return ItemListByParentId.TryGetValue(query.ParentIds[0], out var children)
                ? children.ToArray()
                : throw new NotSupportedException(
                    $"TestLibraryManager: GetItemList called for unregistered parent {query.ParentIds[0]}. Seed ItemListByParentId first.");
        }

        throw new NotSupportedException(
            $"TestLibraryManager: {targetMethod?.Name} is not stubbed. Add it deliberately - see Support/TestItems.cs.");
    }
}

/// <summary>
/// An <see cref="IUserDataManager"/> that throws on every call.
///
/// Production reads user data through <c>UserDataCacheHelper.GetCachedUserData</c>, which checks
/// <c>RefreshCache.UserDataCache</c> first and only falls through to the manager on a miss. Tests
/// seed the cache instead (see <see cref="TestItems.SeedUserData"/>), so a throwing manager both
/// avoids stubbing a 13-member interface AND proves the cache-first path is the one being taken -
/// a silent regression to per-item DB round-trips would fail the test rather than slow production
/// down unnoticed.
///
/// The manager reference itself still has to be non-null: RoundRobinLeastRecentlyWatchedOrder
/// .BuildGroupRecencyAndHoldState bails out early (groups fall back to alphabetical) when it is
/// null, which would make the recency tests vacuous.
/// </summary>
public class ThrowingUserDataManager : DispatchProxy
{
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        throw new NotSupportedException(
            $"ThrowingUserDataManager: {targetMethod?.Name} was called - the RefreshCache user-data cache should have answered instead.");
    }
}

/// <summary>
/// A minimal <see cref="IUserManager"/> backed by a fixed user list, for testing that "all users"
/// aggregate sorts resolve EVERY server user (via <c>PlaylistUserResolver.GetAllUsers</c>) rather
/// than only the users a list happens to be shared with. Answers <c>GetUsers()</c> and
/// <c>GetUserById(Guid)</c>; everything else throws so an unexpected dependency fails loudly.
/// </summary>
public class TestUserManager : DispatchProxy
{
    public List<User> Users { get; set; } = [];

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        // Emby lists users through GetUserList(UserQuery) -> User[] (the Users property is obsolete).
        if (targetMethod?.Name == "GetUserList")
        {
            return Users.ToArray();
        }

        if (targetMethod?.Name == "GetUserById" && args is { Length: 1 } && args[0] is Guid id)
        {
            return Users.FirstOrDefault(u => u.Id == id);
        }

        throw new NotSupportedException(
            $"TestUserManager: {targetMethod?.Name} is not stubbed. Add it deliberately - see Support/TestItems.cs.");
    }
}

/// <summary>
/// Builders for the item shapes the round-robin orders group and interleave.
///
/// Two Emby traps these builders exist to close, both of which throw
/// <see cref="NullReferenceException"/> rather than failing an assertion, and both of which have
/// already cost a debugging session:
///
/// 1. Reading <c>item.SortName</c> that was never assigned throws. Every builder assigns it.
/// 2. Assigning <c>Name</c> RESETS the cached sort name, so <c>Name</c> must be set BEFORE
///    <c>SortName</c>. The object-initializer-then-assign shape below is deliberate.
/// </summary>
public static class TestItems
{
    private static long _nextInternalId;

    /// <summary>
    /// A unique item/user internal id. Emby assigns <c>InternalId</c> in its database; plugin code keys every
    /// cache and lookup by it, so each built item needs its own.
    /// </summary>
    internal static long NextId() => Interlocked.Increment(ref _nextInternalId);

    /// <summary>
    /// A fixture date as a UTC instant. Assigning a DateTime of unspecified kind to a DateTimeOffset interprets it in the
    /// machine's local timezone, which made expected values drift by hours depending on where the tests ran.
    /// </summary>
    internal static DateTimeOffset Utc(DateTime date) => new(DateTime.SpecifyKind(date, DateTimeKind.Utc));

    internal static DateTimeOffset? Utc(DateTime? date) => date.HasValue ? Utc(date.Value) : null;

    /// <summary>Installs the library manager stub once per test process, before any test runs.</summary>
    [ModuleInitializer]
    internal static void InstallLibraryManager()
    {
        BaseItem.LibraryManager = DispatchProxy.Create<ILibraryManager, TestLibraryManager>();
        BaseItem.LocalizationManager = DispatchProxy.Create<ILocalizationManager, NeutralLocalizationManager>();
        BaseItem.ItemRepository = DispatchProxy.Create<MediaBrowser.Controller.Persistence.IItemRepository, LoudItemRepository>();
    }

    public static readonly User User = new() { Name = "tester", Id = Guid.NewGuid(), InternalId = NextId() };

    /// <summary>A second user, for asserting that per-user state is actually keyed by user.</summary>
    public static readonly User OtherUser = new() { Name = "other", Id = Guid.NewGuid(), InternalId = NextId() };

    /// <summary>A server user with its own Guid id and internal id (Emby users are items).</summary>
    public static User NewUser(string name) => new() { Name = name, Id = Guid.NewGuid(), InternalId = NextId() };

    public static IUserDataManager ThrowingUserData() => DispatchProxy.Create<IUserDataManager, ThrowingUserDataManager>();

    /// <summary>An <see cref="IUserManager"/> that knows only the given users - see <see cref="TestUserManager"/>.</summary>
    public static IUserManager UserManagerWithUsers(params User[] users)
    {
        var proxy = DispatchProxy.Create<IUserManager, TestUserManager>();
        ((TestUserManager)proxy).Users = [.. users];
        return proxy;
    }

    /// <summary>
    /// A Series registered with the library manager stub, so episodes pointing at it can resolve
    /// <c>episode.Series</c>. <paramref name="sortName"/> is the custom Sort Title users edit to
    /// order a crossover night; it defaults to the name.
    /// </summary>
    public static Series Show(string name, string? sortName = null)
    {
        var series = new Series { Id = Guid.NewGuid(), InternalId = TestItems.NextId(), Name = name };
        series.SortName = sortName ?? name;
        TestLibraryManager.Items[series.InternalId] = series;
        series.MarkTaggedItemsLoaded();
        return series;
    }

    /// <summary>
    /// An Episode. Pass <paramref name="show"/> to attach it to a registered Series (needed for
    /// the same-day cross-series tie-break); otherwise only the denormalized SeriesName is set,
    /// which is what <c>ExtractGroupKey</c> groups on.
    /// </summary>
    public static Episode Ep(
        string seriesName,
        int season,
        int episode,
        DateTime? aired = null,
        string? name = null,
        Series? show = null)
    {
        var item = new Episode
        {
            Id = Guid.NewGuid(), InternalId = TestItems.NextId(),
            Name = name ?? $"{seriesName} S{season:00}E{episode:00}",
            SeriesName = seriesName,
            ParentIndexNumber = season,
            IndexNumber = episode,
            PremiereDate = Utc(aired),
        };

        item.SortName = item.Name;

        if (show != null)
        {
            item.SeriesId = show.InternalId;
        }

        item.MarkTaggedItemsLoaded();
        return item;
    }

    /// <summary>
    /// A Season - a container whose recency is aggregated over cached children rather than read
    /// from its own user-data row.
    /// </summary>
    public static Season SeasonOf(string name)
    {
        var season = new Season { Id = Guid.NewGuid(), InternalId = TestItems.NextId(), Name = name };
        season.SortName = name;
        season.MarkTaggedItemsLoaded();
        return season;
    }

    /// <summary>
    /// Links <paramref name="child"/> to <paramref name="parent"/> through <c>ParentId</c> - the
    /// only link the ancestor walk follows - and registers BOTH with the library-manager stub so
    /// <c>GetParent()</c> can resolve them.
    ///
    /// Deliberately does NOT touch <c>SeriesId</c>/<c>SeasonId</c>: those drift from the real
    /// tree, and reaching for them instead of the parent chain is exactly what issue #495 was.
    /// </summary>
    public static T Under<T>(T child, BaseItem parent)
        where T : BaseItem
    {
        child.ParentId = parent.InternalId;
        TestLibraryManager.Items[child.InternalId] = child;
        TestLibraryManager.Items[parent.InternalId] = parent;
        return child;
    }

    /// <summary>
    /// A plain physical folder - the <c>/shows</c> or <c>/movies</c> level of the tree, and the
    /// stand-in for a library's CollectionFolder. Returned UNREGISTERED: <see cref="Under{T}"/>
    /// registers it when it is linked into a chain, and library folders are reached through
    /// <see cref="TestLibraryManager.CollectionFolders"/> rather than through <c>GetItemById</c>.
    /// </summary>
    public static Folder PhysicalFolder(string name, params string[] tags)
    {
        var folder = new Folder { Id = Guid.NewGuid(), InternalId = TestItems.NextId(), Name = name };
        folder.SortName = name;

        if (tags.Length > 0)
        {
            folder.Tags = tags;
        }

        folder.MarkTaggedItemsLoaded();
        return folder;
    }

    /// <summary>A MusicAlbum - the audio equivalent of <see cref="SeasonOf"/>.</summary>
    public static MusicAlbum Album(string name)
    {
        var album = new MusicAlbum { Id = Guid.NewGuid(), InternalId = TestItems.NextId(), Name = name };
        album.SortName = name;
        album.MarkTaggedItemsLoaded();
        return album;
    }

    /// <summary>An Audio track. Disc is ParentIndexNumber, track is IndexNumber.</summary>
    public static Audio Track(string album, int disc, int track, string? name = null, string? artist = null)
    {
        var item = new Audio
        {
            Id = Guid.NewGuid(), InternalId = TestItems.NextId(),
            Name = name ?? $"{album} D{disc}T{track:00}",
            Album = album,
            ParentIndexNumber = disc,
            IndexNumber = track,
        };

        item.SortName = item.Name;

        if (artist != null)
        {
            item.Artists = [artist];
        }

        item.MarkTaggedItemsLoaded();
        return item;
    }

    /// <summary>A Movie - the generic non-episode, non-audio item.</summary>
    public static Movie Mov(string name, DateTime? aired = null, string? sortName = null, string[]? genres = null, string[]? studios = null)
    {
        var item = new Movie { Id = Guid.NewGuid(), InternalId = TestItems.NextId(), Name = name, PremiereDate = Utc(aired) };
        item.SortName = sortName ?? name;

        if (genres != null)
        {
            item.Genres = genres;
        }

        if (studios != null)
        {
            item.Studios = studios;
        }

        item.MarkTaggedItemsLoaded();
        return item;
    }

    public static string[] Names(IEnumerable<BaseItem> items) => items.Select(i => i.Name).ToArray();

    /// <summary>
    /// Seeds one item's per-user watch state into the refresh cache, the way a real refresh would
    /// after its first read. <paramref name="lastPlayed"/> null means "no timestamp".
    /// </summary>
    public static void SeedUserData(
        RefreshQueueService.RefreshCache cache,
        BaseItem item,
        User user,
        bool played = false,
        DateTime? lastPlayed = null,
        bool isFavorite = false)
    {
        cache.UserDataCache[(item.InternalId, user.Id)] = new UserItemData
        {
            Key = item.Id.ToString("N"),
            Played = played,
            LastPlayedDate = Utc(lastPlayed),
            IsFavorite = isFavorite,
        };
    }

    /// <summary>
    /// Records that an item has no user-data row at all, which is distinct from having a row with
    /// Played=false: production memoizes the miss in a separate negative cache.
    /// </summary>
    public static void SeedNoUserData(RefreshQueueService.RefreshCache cache, BaseItem item, User user)
    {
        cache.UserDataNegativeCache[(item.InternalId, user.Id)] = 0;
    }

    /// <summary>
    /// Builds the item id → collection name map SmartList hands the order for "Collections"
    /// grouping. Items not passed here are absent from the map on purpose - production falls back
    /// to series-name grouping for them.
    /// </summary>
    public static Dictionary<long, string> CollectionMap(params (string Collection, BaseItem[] Items)[] groups)
    {
        var map = new Dictionary<long, string>();
        foreach (var (collection, items) in groups)
        {
            foreach (var item in items)
            {
                map[item.InternalId] = collection;
            }
        }

        return map;
    }
}

/// <summary>
/// Stand-in for <c>BaseItem.LocalizationManager</c>, a static that is null offline. On Emby it matters far
/// beyond rating scores: <c>BaseItem.SortName</c>'s setter calls <c>StringExtensions.RemoveDiacritics</c>, which
/// reaches this manager, so without it EVERY test item builder throws <see cref="NullReferenceException"/>.
///
/// Answers <c>GetRatingLevel</c> with a constant level and <c>GetRatingScore</c> with null (no rating system loaded - what the real manager returns for a
/// rating it does not know) and <c>RemoveDiacritics</c> by stripping combining marks.
/// Everything else throws so a new dependency on localization fails loudly instead of returning a default.
/// </summary>
public class NeutralLocalizationManager : DispatchProxy
{
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        // Emby ranks ratings through GetRatingLevel(string) -> int?; a constant level keeps single-rating fixtures
        // deterministic (a null level means "unknown rating" and nothing gets assigned).
        if (targetMethod?.Name == "GetRatingLevel")
        {
            return (int?)1;
        }

        if (targetMethod?.Name == "GetRatingScore")
        {
            return null;
        }

        if (targetMethod?.Name == "RemoveDiacritics" && args is { Length: 1 })
        {
            return RemoveDiacritics((string?)args[0]);
        }

        throw new NotSupportedException(
            $"NeutralLocalizationManager: {targetMethod?.Name} is not stubbed. Add it deliberately - see Support/TestItems.cs.");
    }

    /// <summary>Strips accents the way the real manager does (decompose, drop combining marks, recompose).</summary>
    private static string? RemoveDiacritics(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var decomposed = text.Normalize(System.Text.NormalizationForm.FormD);
        var kept = decomposed.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark);
        return new string(kept.ToArray()).Normalize(System.Text.NormalizationForm.FormC);
    }
}

/// <summary>
/// A stand-in for <c>BaseItem.ItemRepository</c> that fails loudly: any call names the member, so a new dependency
/// on the repository shows up as a clear test failure instead of a NullReferenceException inside Emby's code.
/// </summary>
public class LoudItemRepository : DispatchProxy
{
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        // Called by BaseItem.EnsureTaggedItemsLoaded after the (stubbed, empty) link lookup; nothing to notify offline.
        if (targetMethod?.Name == "OnItemLinksFilled")
        {
            return null;
        }

        throw new NotSupportedException(
            $"LoudItemRepository: {targetMethod?.Name} is not stubbed. Add it deliberately - see Support/TestItems.cs.");
    }
}

/// <summary>Fixture helpers for items built inline in tests.</summary>
public static class TestItemExtensions
{
    /// <summary>
    /// Marks the item's genres, studios and tags as loaded. Emby reads them lazily from the library on first access
    /// and would otherwise replace the values a test assigned in memory with the (empty) stored ones.
    /// </summary>
    public static T Loaded<T>(this T item)
        where T : BaseItem
    {
        item.MarkTaggedItemsLoaded();
        return item;
    }
}
