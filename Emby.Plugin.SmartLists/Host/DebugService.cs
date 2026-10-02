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

        public string UserName { get; set; } = "kevin";

        public string MediaType { get; set; } = "Movie";

        public string Field { get; set; } = "Name";

        public string Operator { get; set; } = "Contains";

        public string Value { get; set; } = string.Empty;

        public bool Public { get; set; }
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

            (bool Success, string Message, string Id) result;
            if (string.Equals(r.Kind, "collection", StringComparison.OrdinalIgnoreCase))
            {
                var dto = new SmartCollectionDto { Name = r.Name, UserId = user.Id.ToString("D"), ExpressionSets = sets, MediaTypes = [r.MediaType] };
                result = host.CollectionService.RefreshAsync(dto, null, CancellationToken.None).GetAwaiter().GetResult();
            }
            else
            {
                var dto = new SmartPlaylistDto { Name = r.Name, UserId = user.Id.ToString("D"), ExpressionSets = sets, MediaTypes = [r.MediaType], Public = r.Public };
                result = host.PlaylistService.RefreshAsync(dto, null, CancellationToken.None).GetAwaiter().GetResult();
            }

            return result.Success + " | " + result.Message + " | id=" + result.Id;
        }
    }
}
