using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using Emby.Plugin.SmartLists.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

// Emby reads the plugin ID from the assembly GUID attribute.
[assembly: Guid("7f0b8a52-6c1d-4b57-9a2e-5d3b0c4e91a8")]

namespace Emby.Plugin.SmartLists
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1724:Type names should not match namespaces", Justification = "Plugin class name is required by Emby's plugin loader convention.")]
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            _pluginsPath = applicationPaths.PluginsPath;

            // Help .NET find the bundled ImageSharp DLL next to the plugin.
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
        }

        private readonly string _pluginsPath;

        private Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs args)
        {
            var assemblyName = new AssemblyName(args.Name);
            if (!"SixLabors.ImageSharp".Equals(assemblyName.Name, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // Emby may load the plugin from memory, leaving Assembly.Location empty; fall back to its plugins folder.
            var pluginDirectory = System.IO.Path.GetDirectoryName(GetType().Assembly.Location);
            if (string.IsNullOrEmpty(pluginDirectory))
            {
                pluginDirectory = _pluginsPath;
            }

            var imageSharpPath = System.IO.Path.Combine(pluginDirectory, "SixLabors.ImageSharp.dll");
            return System.IO.File.Exists(imageSharpPath) ? Assembly.LoadFrom(imageSharpPath) : null;
        }

        public override string Name => "SmartLists";

        public override string Description => "Create smart, rule-based playlists and collections in Emby, for movies, shows, music, or anything your library holds.";

        /// <summary>
        /// Gets the current plugin instance.
        /// </summary>
        public static Plugin? Instance { get; private set; }

        /// <summary>
        /// Gets the plugin's web pages.
        /// </summary>
        /// <returns>The web pages.</returns>
        public IEnumerable<PluginPageInfo> GetPages()
        {
            return [
                new PluginPageInfo
                {
                    Name = Name,
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.config.html",
                    EnableInMainMenu = true
                },
                // Page controller: loads the modules below (Emby runs page code through data-controller)
                new PluginPageInfo
                {
                    Name = "smartlistsjs",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.config-controller.js",
                },
                // Core utilities and constants (must load first)
                new PluginPageInfo
                {
                    Name = "config-core.js",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.config-core.js",
                },
                // Formatters and option generators
                new PluginPageInfo
                {
                    Name = "config-formatters.js",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.config-formatters.js",
                },
                // Schedule management
                new PluginPageInfo
                {
                    Name = "config-schedules.js",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.config-schedules.js",
                },
                // Image management
                new PluginPageInfo
                {
                    Name = "config-images.js",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.config-images.js",
                },
                // Sort management
                new PluginPageInfo
                {
                    Name = "config-sorts.js",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.config-sorts.js",
                },
                // Generic multi-select component
                new PluginPageInfo
                {
                    Name = "config-multi-select.js",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.config-multi-select.js",
                },
                // Multi-select component CSS
                new PluginPageInfo
                {
                    Name = "config-multi-select.css",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.config-multi-select.css",
                },
                // Searchable single-select component
                new PluginPageInfo
                {
                    Name = "config-searchable-select.js",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.config-searchable-select.js",
                },
                // User selection component
                new PluginPageInfo
                {
                    Name = "config-user-select.js",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.config-user-select.js",
                },
                // Rule management
                new PluginPageInfo
                {
                    Name = "config-rules.js",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.config-rules.js",
                },
                // Playlist CRUD operations
                new PluginPageInfo
                {
                    Name = "config-lists.js",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.config-lists.js",
                },
                // Template catalog and picker
                new PluginPageInfo
                {
                    Name = "config-templates.js",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.config-templates.js",
                },
                // Filtering and search
                new PluginPageInfo
                {
                    Name = "config-filters.js",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.config-filters.js",
                },
                // Bulk actions
                new PluginPageInfo
                {
                    Name = "config-bulk-actions.js",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.config-bulk-actions.js",
                },
                // Status page
                new PluginPageInfo
                {
                    Name = "config-status.js",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.config-status.js",
                },
                // API calls
                new PluginPageInfo
                {
                    Name = "config-api.js",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.config-api.js",
                },
                // Initialization (must load last)
                new PluginPageInfo
                {
                    Name = "config-init.js",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.config-init.js",
                }
            ];
        }
    }
}
