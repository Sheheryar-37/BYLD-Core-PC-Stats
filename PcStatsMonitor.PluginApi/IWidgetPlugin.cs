using System.Windows.Controls;

namespace PcStatsMonitor.PluginApi
{
    /// <summary>
    /// The core interface that any third-party widget must implement to be loaded
    /// by the BYLD Core dashboard.
    /// </summary>
    public interface IWidgetPlugin
    {
        /// <summary>Display name of the widget.</summary>
        string Name { get; }
        
        /// <summary>Semantic version of the plugin.</summary>
        string Version { get; }
        
        /// <summary>Creator of the plugin.</summary>
        string Author { get; }

        /// <summary>
        /// Called once when the plugin is loaded. Use this to setup your UI and resources.
        /// </summary>
        /// <param name="timerService">Reference to the host's DispatcherTimer for sync.</param>
        void Initialize(dynamic timerService);

        /// <summary>
        /// Called every 1000ms by the host application with the latest hardware telemetry.
        /// </summary>
        /// <param name="hardwareData">Dynamic object containing CPU, GPU, RAM and Storage metrics.</param>
        void Update(dynamic hardwareData);

        /// <summary>
        /// Returns the visual element to be displayed on the dashboard.
        /// </summary>
        UserControl GetUI();
    }
}
