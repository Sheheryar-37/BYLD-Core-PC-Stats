namespace PcStatsMonitor.Models;

/// <summary>
/// Centralized constants used across the application.
/// </summary>
public static class Constants
{
    // File Paths
    public const string ThemeConfigFilePath = "theme.json";
    public const string LogFilePath = "logs/pcstatsmonitor.log";
    public const string DefaultLogoPath = "Assets/logo.png";

    // Application Defaults
    public const string DefaultThemeBackground = "#060606";
    public const string DefaultThemeForeground = "#FFFFFF";
    public const string DefaultThemeAccent = "#3b82f6";
    public const string DefaultThemeTrack = "#0B0B0B";
    public const string DefaultThemeAlert = "#FF4022";
    public const string DefaultFontFamily = "Mucho Sans";
    
    // UI Layout Defaults
    public const int DefaultWindowWidth = 480;
    public const int DefaultWindowHeight = 854;
    public const int DefaultTransitionDelaySeconds = 5;

    // Default Fallback Strings (For Hardware Monitoring)
    public const string DefaultMemorySensorName = "Memory";
    public const string FallbackDriveVendor = "NOT FOUND";
    public const string FallbackPrimaryDriveModel = "No primary drive detected";
    public const string FallbackSecondaryDriveModel = "No secondary drive detected";
    public const string DriveTypeSolidState = "Solid State Drive";

    // Sensor Keys
    public const string SensorDataMemoryUsed = "Memory Used";
    public const string SensorDataMemoryAvailable = "Memory Available";
    public const string SensorLoadTotalActivity = "Total Activity";
    public const string SensorDataUsedSpace = "Used Space";
}
