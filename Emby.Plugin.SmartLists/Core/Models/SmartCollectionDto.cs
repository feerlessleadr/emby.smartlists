using System;

namespace Emby.Plugin.SmartLists.Core.Models
{
    /// <summary>
    /// DTO for server-wide smart collections
    /// Collections are server-wide (visible to all users) but have an owner for rule context
    /// </summary>
    [Serializable]
    public class SmartCollectionDto : SmartListDto
    {
        public SmartCollectionDto()
        {
            Type = Core.Enums.SmartListType.Collection;
        }

        // Collection-specific properties
        public string? CollectionId { get; set; }  // Emby collection (BoxSet) ID for reliable lookup
    }
}

