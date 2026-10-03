namespace Emby.Plugin.SmartLists.Core.Constants
{
    /// <summary>
    /// Provider ID keys stamped on Emby items by this plugin.
    /// </summary>
    public static class ProviderKeys
    {
        /// <summary>
        /// Tethers a Emby playlist/collection to its smart list DTO ID, so the item can be
        /// recovered when the stored Emby item ID goes stale, and so duplicates provably
        /// created by this plugin can be cleaned up safely.
        /// </summary>
        public const string SmartLists = "SmartLists";
    }
}
