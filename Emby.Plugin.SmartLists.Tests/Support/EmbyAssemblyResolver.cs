using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace Emby.Plugin.SmartLists.Tests.Support;

/// <summary>
/// Lets the test host load Emby's own assemblies (MediaBrowser.*, and whatever they pull in) from an Emby
/// Server install instead of copying hundreds of DLLs next to the tests. The install location comes from the
/// <c>EmbySystemDir</c> assembly metadata, which the csproj fills from the EmbySystemDir MSBuild property
/// (or the EMBY_SYSTEM_DIR environment variable), so no path is hard-coded in source.
/// </summary>
internal static class EmbyAssemblyResolver
{
    [ModuleInitializer]
    internal static void Install()
    {
        var dir = typeof(EmbyAssemblyResolver).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "EmbySystemDir")?.Value;

        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            throw new InvalidOperationException(
                "EmbySystemDir is not set or does not exist. Build the tests with -p:EmbySystemDir=\"<Emby install>\\system\" "
                + "or set the EMBY_SYSTEM_DIR environment variable.");
        }

        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            var path = Path.Combine(dir, name.Name + ".dll");
            return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
        };
    }
}
