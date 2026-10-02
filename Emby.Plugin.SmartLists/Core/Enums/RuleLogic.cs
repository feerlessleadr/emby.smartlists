using System.Text.Json.Serialization;

namespace Emby.Plugin.SmartLists.Core.Enums
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RuleLogic
    {
        And,
        Or,
    }
}

