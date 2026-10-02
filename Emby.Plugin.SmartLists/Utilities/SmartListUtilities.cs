using System;
using System.Linq;
using Emby.Plugin.SmartLists.Core.Models;

namespace Emby.Plugin.SmartLists.Utilities
{
    public static class SmartListUtilities
    {
        public static bool UsesLibraryNameRule(SmartListDto? dto)
        {
            return dto?.ExpressionSets?.Any(set =>
                set.Expressions?.Any(expr =>
                    string.Equals(expr.MemberName, "LibraryName", StringComparison.OrdinalIgnoreCase)) == true) == true;
        }
    }
}
