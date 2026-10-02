using System;
using MediaBrowser.Controller.Entities;
using Emby.Plugin.SmartLists.Services.Shared;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Emby.Plugin.SmartLists.Core.Orders
{
    public class LastEpisodeAirDateOrder : PropertyOrder<double>
    {
        public override string Name => "LastEpisodeAirDate Ascending";
        protected override bool IsDescending => false;
        protected override double GetSortValue(BaseItem item, User? user = null, IUserDataManager? userDataManager = null, ILogger? logger = null, RefreshQueueService.RefreshCache? refreshCache = null)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (refreshCache != null && refreshCache.LastEpisodeAirDateById.TryGetValue(item.InternalId, out var cachedDate))
            {
                return cachedDate;
            }

            return 0;
        }
    }

    public class LastEpisodeAirDateOrderDesc : PropertyOrder<double>
    {
        public override string Name => "LastEpisodeAirDate Descending";
        protected override bool IsDescending => true;
        protected override double GetSortValue(BaseItem item, User? user = null, IUserDataManager? userDataManager = null, ILogger? logger = null, RefreshQueueService.RefreshCache? refreshCache = null)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (refreshCache != null && refreshCache.LastEpisodeAirDateById.TryGetValue(item.InternalId, out var cachedDate))
            {
                return cachedDate;
            }

            return 0;
        }
    }
}
