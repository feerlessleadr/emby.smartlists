using System.Text.Json.Serialization;

namespace Emby.Plugin.SmartLists.Core.Enums
{
    /// <summary>
    /// Type discriminator for smart lists (Playlist vs Collection)
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SmartListType
    {
        Playlist,
        Collection,
    }
}

