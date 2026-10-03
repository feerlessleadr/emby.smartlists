using System.Text.Json.Serialization;

namespace Emby.Plugin.SmartLists.Core.Enums
{
    /// <summary>
    /// Action to perform when a visibility schedule triggers
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ScheduleAction
    {
        /// <summary>
        /// Enable the list (make it visible in Emby)
        /// </summary>
        Enable,
        
        /// <summary>
        /// Disable the list (hide it from Emby)
        /// </summary>
        Disable
    }
}
