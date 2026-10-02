using System;
using MediaBrowser.Controller.Entities;

namespace Emby.Plugin.SmartLists.Core.QueryEngine
{
    public static class DateUtils
    {
        /// <summary>
        /// Reads the PremiereDate of a BaseItem. Emby stores it as a <see cref="DateTimeOffset"/>?; it is returned as UTC.
        /// </summary>
        /// <param name="item">The BaseItem to extract the release date from. Must not be null.</param>
        /// <param name="premiereDate">The premiere date as UTC when available.</param>
        /// <returns>True when the item has a usable premiere date; otherwise false.</returns>
        /// <exception cref="ArgumentNullException">Thrown when item is null.</exception>
        public static bool TryGetPremiereDate(BaseItem item, out DateTime premiereDate)
        {
            ArgumentNullException.ThrowIfNull(item);

            var value = item.PremiereDate;
            if (value.HasValue && value.Value != DateTimeOffset.MinValue)
            {
                premiereDate = value.Value.UtcDateTime;
                return true;
            }

            premiereDate = DateTime.MinValue;
            return false;
        }

        /// <summary>
        /// Returns the Unix timestamp (seconds) of an item's PremiereDate, or 0 when it has none.
        /// </summary>
        /// <param name="item">The BaseItem to extract the release date from. Must not be null.</param>
        /// <returns>Unix timestamp of the release date, or 0 if the date is not available.</returns>
        /// <exception cref="ArgumentNullException">Thrown when item is null.</exception>
        public static double GetReleaseDateUnixTimestamp(BaseItem item)
        {
            ArgumentNullException.ThrowIfNull(item);

            return TryGetPremiereDate(item, out var premiereDate)
                ? new DateTimeOffset(premiereDate, TimeSpan.Zero).ToUnixTimeSeconds()
                : 0;
        }
    }
}
