using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Entities;
using Emby.Plugin.SmartLists.Core.Models;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Querying;

namespace Emby.Plugin.SmartLists.Utilities
{
    /// <summary>
    /// Resolves the effective user mappings for smart playlists.
    /// </summary>
    internal static class PlaylistUserResolver
    {
        public static string NormalizeUserId(Guid userId) => userId.ToString("N");

        public static string NormalizeUserId(string userId)
        {
            return Guid.TryParse(userId, out var guid) ? guid.ToString("N") : userId;
        }

        /// <summary>
        /// Whether any rule of the playlist, bumper rules included, is pinned to one of the given
        /// users through its UserId (e.g. Is Favorite for user B). Such a rule reads that user's data
        /// for EVERY user's copy of the playlist, so a change by that user affects all of them.
        /// </summary>
        /// <param name="playlist">The playlist whose rules are checked.</param>
        /// <param name="normalizedUserIds">User ids in "N" format.</param>
        /// <returns>True when a rule references one of the users.</returns>
        public static bool HasRulePinnedToAnyUser(SmartPlaylistDto playlist, ICollection<string> normalizedUserIds)
        {
            ArgumentNullException.ThrowIfNull(playlist);

            return IsPinnedIn(playlist.ExpressionSets) || IsPinnedIn(playlist.Bumpers?.ExpressionSets);

            bool IsPinnedIn(List<ExpressionSet>? expressionSets) =>
                expressionSets?.Any(set => set.Expressions?.Any(expression =>
                    !string.IsNullOrEmpty(expression.UserId) && normalizedUserIds.Contains(NormalizeUserId(expression.UserId))) == true) == true;
        }

        public static List<User> GetAllUsers(IUserManager userManager)
        {
            ArgumentNullException.ThrowIfNull(userManager);

            return GetUsers(userManager)
                .Where(u => u != null && u.Id != Guid.Empty)
                .OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // IUserManager.Users is obsolete on Emby; an unfiltered GetUserList returns every server user.
        private static IEnumerable<User> GetUsers(IUserManager userManager) => userManager.GetUserList(new UserQuery());

        public static void ExpandAllUsers(SmartPlaylistDto playlist, IUserManager userManager)
        {
            ArgumentNullException.ThrowIfNull(playlist);
            ArgumentNullException.ThrowIfNull(userManager);

            if (!playlist.AllUsers)
            {
                return;
            }

            var existingByUserId = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            if (playlist.UserPlaylists != null)
            {
                foreach (var mapping in playlist.UserPlaylists)
                {
                    if (!string.IsNullOrEmpty(mapping.UserId) &&
                        Guid.TryParse(mapping.UserId, out var userId) &&
                        userId != Guid.Empty &&
                        !existingByUserId.ContainsKey(userId.ToString("N")))
                    {
                        existingByUserId[userId.ToString("N")] = mapping.PlaylistId;
                    }
                }
            }

            playlist.UserPlaylists = GetAllUsers(userManager)
                .Select(user =>
                {
                    var normalizedUserId = user.Id.ToString("N");
                    existingByUserId.TryGetValue(normalizedUserId, out var jellyfinPlaylistId);
                    return new SmartPlaylistDto.UserPlaylistMapping
                    {
                        UserId = normalizedUserId,
                        PlaylistId = jellyfinPlaylistId
                    };
                })
                .ToList();

            playlist.Public = false;
        }

        public static HashSet<string> GetEffectiveUserIds(SmartPlaylistDto playlist, IUserManager? userManager = null)
        {
            ArgumentNullException.ThrowIfNull(playlist);

            var userIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (playlist.AllUsers && userManager != null)
            {
                foreach (var user in GetAllUsers(userManager))
                {
                    userIds.Add(user.Id.ToString("N"));
                }

                return userIds;
            }

            if (playlist.UserPlaylists != null && playlist.UserPlaylists.Count > 0)
            {
                foreach (var mapping in playlist.UserPlaylists)
                {
                    if (!string.IsNullOrEmpty(mapping.UserId) &&
                        Guid.TryParse(mapping.UserId, out var userId) &&
                        userId != Guid.Empty)
                    {
                        userIds.Add(userId.ToString("N"));
                    }
                }
            }
            else if (!string.IsNullOrEmpty(playlist.UserId) &&
                     Guid.TryParse(playlist.UserId, out var parsedUserId) &&
                     parsedUserId != Guid.Empty)
            {
                userIds.Add(parsedUserId.ToString("N"));
            }

            return userIds;
        }

        public static SmartPlaylistDto.UserPlaylistMapping? FindMapping(SmartPlaylistDto playlist, string userId)
        {
            if (playlist.UserPlaylists == null)
            {
                return null;
            }

            var normalizedUserId = NormalizeUserId(userId);
            return playlist.UserPlaylists.FirstOrDefault(mapping =>
                !string.IsNullOrEmpty(mapping.UserId) &&
                string.Equals(NormalizeUserId(mapping.UserId), normalizedUserId, StringComparison.OrdinalIgnoreCase));
        }
    }
}
