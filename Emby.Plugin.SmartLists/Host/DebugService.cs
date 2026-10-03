using System;
using System.Linq;
using System.Threading;
using Emby.Plugin.SmartLists.Core.Models;
using Emby.Plugin.SmartLists.Core.QueryEngine;
using MediaBrowser.Model.Services;

namespace Emby.Plugin.SmartLists.Host
{
    /// <summary>
    /// TEMPORARY (phase 4): runs the real playlist/collection services against the live server so their Emby behavior
    /// can be verified before the real API exists. Removed in phase 6.
    /// </summary>
    [Route("/SmartListsDebug/Refresh", "GET", Summary = "Phase 4 debug: build a one-rule list and refresh it")]
    public class DebugRefreshRequest : IReturn<string>
    {
        public string Kind { get; set; } = "playlist";

        public string Name { get; set; } = "SL Debug";

        public string Id { get; set; } = "00000000-0000-0000-0000-00000000d001";

        public string UserName { get; set; } = "kevin";

        public string MediaType { get; set; } = "Movie";

        public string Field { get; set; } = "Name";

        public string Operator { get; set; } = "Contains";

        public string Value { get; set; } = string.Empty;

        public bool Public { get; set; }

        public string? ExistingId { get; set; }

        public string? SortTitle { get; set; }

        public bool AllUsers { get; set; }

        public string? ImageFile { get; set; }

        public bool Queue { get; set; }

        public string? AutoRefresh { get; set; }
    }

    // Emby discovers service methods by reflection on instances, so Get cannot be static.
#pragma warning disable CA1822
    public class DebugService : IService
    {
        public object Get(DebugRefreshRequest r)
        {
            var host = SmartListsHost.Instance ?? throw new InvalidOperationException("SmartLists host is not running");
            var user = host.UserManager.GetUserList(new MediaBrowser.Model.Querying.UserQuery())
                .FirstOrDefault(u => string.Equals(u.Name, r.UserName, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException("No such user: " + r.UserName);

            var sets = new System.Collections.Generic.List<ExpressionSet>
            {
                new() { Expressions = [new Expression(r.Field, r.Operator, r.Value)] },
            };

            System.Collections.Generic.Dictionary<string, string>? images = null;
            if (!string.IsNullOrEmpty(r.ImageFile))
            {
                using var stream = System.IO.File.OpenRead(r.ImageFile);
                var stored = host.ImageService.SaveImageAsync(r.Id, "Primary", stream, System.IO.Path.GetFileName(r.ImageFile)).GetAwaiter().GetResult();
                images = new() { ["Primary"] = stored };
            }

            (bool Success, string Message, string Id) result;
            if (string.Equals(r.Kind, "collection", StringComparison.OrdinalIgnoreCase))
            {
                var dto = new SmartCollectionDto { CollectionId = r.ExistingId, Id = r.Id, Name = r.Name, UserId = user.Id.ToString("D"), ExpressionSets = sets, MediaTypes = [r.MediaType], SortTitle = r.SortTitle, CustomImages = images };
                result = host.CollectionService.RefreshAsync(dto, null, CancellationToken.None).GetAwaiter().GetResult();
            }
            else
            {
                var dto = new SmartPlaylistDto { PlaylistId = r.ExistingId, Id = r.Id, Name = r.Name, UserId = user.Id.ToString("D"), ExpressionSets = sets, MediaTypes = [r.MediaType], Public = r.Public, AllUsers = r.AllUsers, SortTitle = r.SortTitle, CustomImages = images, AutoRefresh = Enum.TryParse<Core.Enums.AutoRefreshMode>(r.AutoRefresh, true, out var arm) ? arm : Core.Enums.AutoRefreshMode.Never };
                if (r.AllUsers)
                {
                    Utilities.PlaylistUserResolver.ExpandAllUsers(dto, host.UserManager);
                }

                if (r.Queue)
                {
                    var saved = host.PlaylistStore.SaveAsync(dto).GetAwaiter().GetResult();
                    host.RefreshQueue.EnqueueOperation(new Services.Shared.RefreshQueueItem { ListId = saved.Id!, ListName = saved.Name, ListType = Core.Enums.SmartListType.Playlist, OperationType = Services.Shared.RefreshOperationType.Create, ListData = saved, TriggerType = Core.Enums.RefreshTriggerType.Manual });
                    host.AutoRefresh?.UpdatePlaylistInCache(saved);
                    return "queued " + saved.Id;
                }

                result = host.PlaylistService.RefreshAsync(dto, null, CancellationToken.None).GetAwaiter().GetResult();
            }

            return result.Success + " | " + result.Message + " | id=" + result.Id;
        }
    }
}
