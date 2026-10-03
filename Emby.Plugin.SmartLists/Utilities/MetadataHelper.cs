using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using Emby.Plugin.SmartLists.Core.Models;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Emby.Plugin.SmartLists.Utilities;

/// <summary>
/// Shared metadata operations for playlists and collections.
/// </summary>
public static class MetadataHelper
{
    /// <summary>
    /// Applies custom metadata (Sort Title, Overview, Tags, Favorite) from the smart list configuration to a Emby item.
    /// Called after metadata refresh to prevent providers from overwriting custom values.
    /// </summary>
    public static async Task ApplyCustomMetadataAsync(
        BaseItem item,
        SmartListDto dto,
        ILogger logger,
        CancellationToken cancellationToken,
        User? favoriteUser = null,
        IUserDataManager? userDataManager = null)
    {
        bool changed = false;

        // Apply Sort Title. Emby has no ForcedSortName on BaseItem: a custom title is set directly and the
        // SortName field is locked, otherwise a FullRefresh overwrites it (verified on Emby 4.10.1.0).
        // The lock doubles as the "has a custom sort title" marker.
        var newSortTitle = string.IsNullOrWhiteSpace(dto.SortTitle) ? null : dto.SortTitle;
        var sortNameLocked = item.LockedFields?.Contains(MetadataFields.SortName) == true;
        if (newSortTitle != null)
        {
            if (!sortNameLocked || item.SortName != newSortTitle)
            {
                item.SetSortNameDirect(newSortTitle);
                item.LockedFields = (item.LockedFields ?? []).Union([MetadataFields.SortName]).ToArray();
                changed = true;
                logger.LogDebug("Set custom sort title to '{SortTitle}' for {ItemName}", newSortTitle, item.Name);
            }
        }
        else if (sortNameLocked)
        {
            // Cleared: unlock and fall back to the plain name. Emby's auto-generated sort name (article
            // stripping etc.) is recomputed by the next metadata refresh; ResetSortName is not public.
            item.LockedFields = item.LockedFields!.Where(f => f != MetadataFields.SortName).ToArray();
            item.SetSortNameDirect(item.Name);
            changed = true;
            logger.LogDebug("Cleared custom sort title for {ItemName}", item.Name);
        }

        // Apply Overview
        var newOverview = string.IsNullOrWhiteSpace(dto.Overview) ? null : dto.Overview;
        if (item.Overview != newOverview)
        {
            item.Overview = newOverview;
            changed = true;
            logger.LogDebug("Set Overview for {ItemName}", item.Name);
        }

        // Apply Tags. Null means SmartLists should leave existing Emby tags alone;
        // an empty list is intentional and clears managed tags.
        if (dto.Tags != null)
        {
            var newTags = dto.Tags
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => tag.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var currentTags = item.Tags ?? [];
            if (!currentTags.SequenceEqual(newTags, StringComparer.OrdinalIgnoreCase))
            {
                item.Tags = newTags;
                changed = true;
                logger.LogDebug("Set {TagCount} tag(s) for {ItemName}", newTags.Length, item.Name);
            }
        }

        if (changed)
        {
            await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
        }

        // Favorite is stored as user data in Emby, so it is applied separately from item metadata.
        if (dto.Favorite.HasValue)
        {
            if (favoriteUser == null || userDataManager == null)
            {
                logger.LogDebug("Favorite state requested for {ItemName}, but no user context was available", item.Name);
                return;
            }

            var userData = userDataManager.GetUserData(favoriteUser, item);
            if (userData == null)
            {
                logger.LogWarning("Could not load user data for {ItemName} and user {UserId}; favorite state was not applied", item.Name, favoriteUser.Id);
                return;
            }

            if (userData.IsFavorite != dto.Favorite.Value)
            {
                userData.IsFavorite = dto.Favorite.Value;
                userDataManager.SaveUserData(favoriteUser, item, userData, UserDataSaveReason.UpdateUserRating, cancellationToken);
                logger.LogDebug("Set favorite state to {Favorite} for {ItemName} and user {UserId}", dto.Favorite.Value, item.Name, favoriteUser.Id);
            }
        }
    }
}
