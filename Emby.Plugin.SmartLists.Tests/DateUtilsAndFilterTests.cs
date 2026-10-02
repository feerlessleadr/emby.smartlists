using Emby.Plugin.SmartLists.Core.QueryEngine;
using MediaBrowser.Controller.Entities;

// The DateUtils tests. The file keeps its historical name (it once also covered an API filter, which now has its
// own file under Api/ until the controllers are rewritten for Emby).
namespace Emby.Plugin.SmartLists.Tests;

/// <summary>
/// Covers both public methods of <see cref="DateUtils"/>.
/// <para>
/// Emby stores <c>BaseItem.PremiereDate</c> as a <see cref="DateTimeOffset"/>?: an instant plus an offset, so the
/// Jellyfin-era questions about <see cref="DateTimeKind"/> (is an unspecified date UTC or local?) no longer exist.
/// DateUtils returns the instant as UTC. It needs no Emby host, DB or DI, so a bare <see cref="BaseItem"/> subclass
/// is enough.
/// </para>
/// </summary>
public class DateUtilsTests
{
    /// <summary>1970-01-01T00:00:00Z -> 2000-01-01T00:00:00Z is 10957 days (30 years + 7 leap days).</summary>
    private const long Epoch2000 = 946_684_800L;

    /// <summary>Unix seconds for 9999-12-31T23:59:59Z, the ceiling of the representable range.</summary>
    private const long EpochMaxValue = 253_402_300_799L;

    private sealed class TestItem : BaseItem
    {
    }

    private static BaseItem ItemWithPremiereDate(DateTimeOffset? premiereDate)
        => new TestItem { PremiereDate = premiereDate };

    [Fact]
    public void TryGetPremiereDate_NullItem_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => DateUtils.TryGetPremiereDate(null!, out _));
    }

    [Fact]
    public void GetReleaseDateUnixTimestamp_NullItem_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => DateUtils.GetReleaseDateUnixTimestamp(null!));
    }

    /// <summary>The instant is returned as UTC.</summary>
    [Fact]
    public void TryGetPremiereDate_PremiereDateSet_ReturnsTrueAndTheUtcInstant()
    {
        var expected = new DateTime(1999, 3, 31, 12, 34, 56, DateTimeKind.Utc);

        var found = DateUtils.TryGetPremiereDate(ItemWithPremiereDate(new DateTimeOffset(expected)), out var actual);

        Assert.True(found);
        Assert.Equal(expected, actual);
        Assert.Equal(DateTimeKind.Utc, actual.Kind);
    }

    /// <summary>A date carrying a non-zero offset is normalised to the same instant in UTC.</summary>
    [Fact]
    public void TryGetPremiereDate_DateWithOffset_IsConvertedToTheUtcInstant()
    {
        var withOffset = new DateTimeOffset(2000, 1, 1, 2, 0, 0, TimeSpan.FromHours(2));

        var found = DateUtils.TryGetPremiereDate(ItemWithPremiereDate(withOffset), out var actual);

        Assert.True(found);
        Assert.Equal(new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc), actual);
    }

    [Fact]
    public void TryGetPremiereDate_NoPremiereDate_ReturnsFalseAndMinValue()
    {
        var found = DateUtils.TryGetPremiereDate(ItemWithPremiereDate(null), out var actual);

        Assert.False(found);
        Assert.Equal(DateTime.MinValue, actual);
    }

    /// <summary>MinValue is the "no date" sentinel and is rejected like a missing date.</summary>
    [Fact]
    public void TryGetPremiereDate_MinValuePremiereDate_ReturnsFalse()
    {
        var found = DateUtils.TryGetPremiereDate(ItemWithPremiereDate(DateTimeOffset.MinValue), out var actual);

        Assert.False(found);
        Assert.Equal(DateTime.MinValue, actual);
    }

    [Fact]
    public void GetReleaseDateUnixTimestamp_UtcDate_ReturnsExactEpochSeconds()
    {
        var item = ItemWithPremiereDate(new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(Epoch2000, DateUtils.GetReleaseDateUnixTimestamp(item));
    }

    /// <summary>The offset is honoured: the same instant expressed at +05:30 gives the same timestamp.</summary>
    [Fact]
    public void GetReleaseDateUnixTimestamp_DateWithOffset_UsesTheInstant()
    {
        var item = ItemWithPremiereDate(new DateTimeOffset(2000, 1, 1, 5, 30, 0, TimeSpan.FromMinutes(330)));

        Assert.Equal(Epoch2000, DateUtils.GetReleaseDateUnixTimestamp(item));
    }

    [Fact]
    public void GetReleaseDateUnixTimestamp_NoPremiereDate_ReturnsZero()
    {
        Assert.Equal(0d, DateUtils.GetReleaseDateUnixTimestamp(ItemWithPremiereDate(null)));
    }

    /// <summary>
    /// Known wart, pinned deliberately: 0 is both "released exactly at the Unix epoch" and
    /// "no release date". Callers cannot distinguish the two from the return value alone -
    /// they must use TryGetPremiereDate if that matters.
    /// </summary>
    [Fact]
    public void GetReleaseDateUnixTimestamp_ExactUnixEpoch_ReturnsZeroSameAsMissingDate()
    {
        var item = ItemWithPremiereDate(new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(0d, DateUtils.GetReleaseDateUnixTimestamp(item));
        Assert.Equal(DateUtils.GetReleaseDateUnixTimestamp(ItemWithPremiereDate(null)), DateUtils.GetReleaseDateUnixTimestamp(item));
    }

    /// <summary>Pre-1970 releases (most of cinema) must go negative, not clamp to zero.</summary>
    [Fact]
    public void GetReleaseDateUnixTimestamp_PreEpochDate_ReturnsNegativeSeconds()
    {
        var item = ItemWithPremiereDate(new DateTimeOffset(1969, 12, 31, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(-86_400d, DateUtils.GetReleaseDateUnixTimestamp(item));
    }

    /// <summary>Sub-second precision is truncated, not rounded up into the next second.</summary>
    [Fact]
    public void GetReleaseDateUnixTimestamp_SubSecondPrecision_IsTruncated()
    {
        var item = ItemWithPremiereDate(new DateTimeOffset(2000, 1, 1, 0, 0, 0, 999, TimeSpan.Zero));

        Assert.Equal(Epoch2000, DateUtils.GetReleaseDateUnixTimestamp(item));
    }

    /// <summary>Upper boundary: the maximum date is legal and must not throw or return 0.</summary>
    [Fact]
    public void GetReleaseDateUnixTimestamp_MaxValue_ReturnsMaxEpochSeconds()
    {
        Assert.Equal(EpochMaxValue, DateUtils.GetReleaseDateUnixTimestamp(ItemWithPremiereDate(DateTimeOffset.MaxValue)));
    }
}
