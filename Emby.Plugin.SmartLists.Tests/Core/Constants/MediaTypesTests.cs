using Emby.Plugin.SmartLists.Core.Constants;
using System.Linq;
using MediaTypeConstants = Emby.Plugin.SmartLists.Core.Constants.MediaTypes;

namespace Emby.Plugin.SmartLists.Tests.Core.Constants;

public class MediaTypesTests
{
    [Fact]
    public void LiveTvChannel_MapsToLiveTvChannelKind_InBothDirections()
    {
        // The query side uses ItemKinds.LiveTvChannel; the runtime GetClientTypeName() of a
        // LiveTvChannel item is TvChannel, which is why OperandFactory/AutoRefreshService match
        // the class directly instead.
        Assert.Equal(ItemKinds.LiveTvChannel, MediaTypeConstants.MediaTypeToItemKind[MediaTypeConstants.LiveTvChannel]);
        Assert.Equal(MediaTypeConstants.LiveTvChannel, MediaTypeConstants.ItemKindToMediaType[ItemKinds.LiveTvChannel]);
    }

    [Fact]
    public void Mappings_RoundTrip_AndAllHasNoDuplicates()
    {
        foreach (var (kind, mediaType) in MediaTypeConstants.ItemKindToMediaType)
        {
            Assert.Equal(kind, MediaTypeConstants.MediaTypeToItemKind[mediaType]);
        }

        foreach (var (mediaType, kind) in MediaTypeConstants.MediaTypeToItemKind)
        {
            Assert.Equal(mediaType, MediaTypeConstants.ItemKindToMediaType[kind]);
        }

        Assert.Equal(MediaTypeConstants.All.Length, MediaTypeConstants.All.Distinct().Count());
    }
}
