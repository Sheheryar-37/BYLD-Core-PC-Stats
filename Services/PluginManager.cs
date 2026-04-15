using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using PcStatsMonitor.PluginApi;

namespace PcStatsMonitor.Services
{
    /// <summary>
    /// Responsible for scanning the /Plugins directory and dynamically loading
    /// third-party widget DLLs using Reflection.
    /// </summary>
    public class PluginManager
    {
        /// <summary>
        /// List of successfully instantiated IWidgetPlugin objects.
        /// </summary>
        public List<IWidgetPlugin> LoadedPlugins { get; } = new();

        /// <summary>
        /// Scans the specified directory (defaults to AppDomain BaseDirectory\Plugins) 
        /// for .dll files containing IWidgetPlugin implementations.
        /// </summary>
        /// <param name="directoryPath">Optional custom path to scan.</param>
        public void LoadPlugins(string? directoryPath = null)
        {
            directoryPath ??= Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");

            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
                return;
            }

            var dllFiles = Directory.GetFiles(directoryPath, "*.dll");

            foreach (var file in dllFiles)
            {
                try
                {
                    var assembly = Assembly.LoadFrom(file);
                    var pluginTypes = assembly.GetTypes()
                        .Where(t => typeof(IWidgetPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                    foreach (var type in pluginTypes)
                    {
                        if (Activator.CreateInstance(type) is IWidgetPlugin plugin)
                        {
                            LoadedPlugins.Add(plugin);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Optionally log plugin loading failures
                    System.Diagnostics.Debug.WriteLine($"Failed to load plugin from {file}: {ex.Message}");
                }
            }
        }
    }
}
