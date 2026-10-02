using System;
using MediaBrowser.Controller.Entities;
using Emby.Plugin.SmartLists.Services.Shared;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Emby.Plugin.SmartLists.Core.Orders
{
    public class RuntimeOrder : PropertyOrder<long>
    {
        public override string Name => "Runtime Ascending";
        protected override bool IsDescending => false;

        protected override long GetSortValue(BaseItem item, User? user = null, IUserDataManager? userDataManager = null, ILogger? logger = null, RefreshQueueService.RefreshCache? refreshCache = null)
        {
            ArgumentNullException.ThrowIfNull(item);
            // Runtime is in ticks
            return item.RunTimeTicks ?? 0L;
        }
    }

    public class RuntimeOrderDesc : PropertyOrder<long>
    {
        public override string Name => "Runtime Descending";
        protected override bool IsDescending => true;

        protected override long GetSortValue(BaseItem item, User? user = null, IUserDataManager? userDataManager = null, ILogger? logger = null, RefreshQueueService.RefreshCache? refreshCache = null)
        {
            ArgumentNullException.ThrowIfNull(item);
            // Runtime is in ticks
            return item.RunTimeTicks ?? 0L;
        }
    }
}

