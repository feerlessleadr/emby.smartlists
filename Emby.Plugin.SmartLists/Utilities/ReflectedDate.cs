using System;

namespace Emby.Plugin.SmartLists.Utilities
{
    /// <summary>
    /// Reads a date from a value obtained by reflection. Emby's date properties are <see cref="DateTimeOffset"/>
    /// (<c>UserItemData.LastPlayedDate</c>, <c>BaseItem.PremiereDate</c>), where Emby used <see cref="DateTime"/>:
    /// code that tested <c>value is DateTime</c> silently saw "no date" for every Emby item.
    /// </summary>
    public static class ReflectedDate
    {
        /// <summary>
        /// Tries to read a usable date (not the minimum value) from <paramref name="value"/> as UTC.
        /// </summary>
        /// <param name="value">A boxed <see cref="DateTime"/> or <see cref="DateTimeOffset"/> (nullable wrappers are unwrapped by boxing).</param>
        /// <param name="utc">The date as UTC when available.</param>
        /// <returns>True when a date other than the minimum value was found.</returns>
        public static bool TryGetUtc(object? value, out DateTime utc)
        {
            switch (value)
            {
                case DateTimeOffset offset when offset != DateTimeOffset.MinValue:
                    utc = offset.UtcDateTime;
                    return true;
                case DateTime dateTime when dateTime != DateTime.MinValue:
                    utc = dateTime;
                    return true;
                default:
                    utc = DateTime.MinValue;
                    return false;
            }
        }
    }
}
