namespace PcStatsMonitor.Models;

/// <summary>
/// Represents the full application configuration loaded from theme.json.
/// Edit theme.json at runtime to change colors, timing, and hardware toggles.
/// </summary>
public class ThemeConfig
{
    // ── Colors ──────────────────────────────────────────────────────────────
    public string BackgroundColor { get; set; } = Constants.DefaultThemeBackground;
    public string ForegroundColor { get; set; } = Constants.DefaultThemeForeground;
    public string AccentColor { get; set; } = Constants.DefaultThemeAccent;
    public string TrackColor { get; set; } = Constants.DefaultThemeTrack;
    public string AlertColor { get; set; } = Constants.DefaultThemeAlert;

    // ── Branding ─────────────────────────────────────────────────────────────
    public string LogoPath { get; set; } = Constants.DefaultLogoPath;
    public string BackgroundImagePath { get; set; } = "";
    public double BackgroundOpacity { get; set; } = 1.0;

    // ── Layout ───────────────────────────────────────────────────────────────
    public int WindowWidth { get; set; } = Constants.DefaultWindowWidth;
    public int WindowHeight { get; set; } = Constants.DefaultWindowHeight; // 9:16 aspect ratio (e.g. 1080x1920 scaled down)
    public int TargetMonitorIndex { get; set; } = -1; // -1 for auto-detection, 0+ for specific monitor index
    
    // ── Typography ───────────────────────────────────────────────────────────
    public string FontFamily { get; set; } = Constants.DefaultFontFamily; // Or exact font if provided, configurable by user
    public string FontWeight { get; set; } = Constants.DefaultFontWeight;

    // ── Transition ───────────────────────────────────────────────────────────
    public int TransitionDelaySeconds { get; set; } = Constants.DefaultTransitionDelaySeconds;
    public DisplayMode DisplayMode { get; set; } = DisplayMode.Auto;

    // ── Screen Visibility (Rotation Toggles) ─────────────────────────────────
    public bool ShowGaugesScreen { get; set; } = true;
    public bool ShowStorageScreen { get; set; } = true;
    public bool ShowClockScreen { get; set; } = true;
    public bool ShowWeatherScreen { get; set; } = false;
    public List<string> EnabledPlugins { get; set; } = new() { "System Clock", "Fan & RGB Controller" };

    // ── Rotation Order (Identifiers: "Gauges", "Storage", "Clock", or Plugin Name) ────
    public List<string> ScreenRotationOrder { get; set; } = new() { "Gauges", "Storage", "Clock" };

    // ── Built-in Clock Configuration ──────────────────────────────────────────
    public ClockConfig Clock { get; set; } = new();

    // ── Built-in Weather Configuration ────────────────────────────────────────
    public WeatherConfig Weather { get; set; } = new();

    // ── Thermal Alerts ───────────────────────────────────────────────────────
    public double CriticalCpuTemp { get; set; } = 85.0;
    public double CriticalGpuTemp { get; set; } = 85.0;

    // ── Hardware Toggles (configurable without recompiling) ──────────────────
    public List<string> ActiveMonitorsOrder { get; set; } = new() { Constants.GaugeGpu, Constants.GaugeCpu, Constants.GaugeRam };

    public bool IsCpuEnabled { get; set; } = true;
    public bool IsGpuEnabled { get; set; } = true;
    public bool IsMemoryEnabled { get; set; } = true;
    public bool IsMotherboardEnabled { get; set; } = false;
    public bool IsNetworkEnabled { get; set; } = false;
    public bool IsBatteryEnabled { get; set; } = false;
    
    // ── Gauge Sizes & Alignment ──────────────────────────────────────────────
    public Dictionary<string, double> GaugeScales { get; set; } = new(StringComparer.OrdinalIgnoreCase) {
        { Constants.GaugeGpu, 1.0 }, { Constants.GaugeCpu, 1.0 }, { Constants.GaugeRam, 1.0 }, { Constants.GaugeMotherboard, 1.0 }, { Constants.GaugeNetwork, 1.0 }
    };
    public Dictionary<string, double> GaugeOffsetsY { get; set; } = new(StringComparer.OrdinalIgnoreCase) {
        { Constants.GaugeGpu, 0.0 }, { Constants.GaugeCpu, 0.0 }, { Constants.GaugeRam, 0.0 }, { Constants.GaugeMotherboard, 0.0 }, { Constants.GaugeNetwork, 0.0 }
    };
    public Dictionary<string, double> GaugeOffsetsX { get; set; } = new(StringComparer.OrdinalIgnoreCase) {
        { Constants.GaugeGpu, 0.0 }, { Constants.GaugeCpu, 0.0 }, { Constants.GaugeRam, 0.0 }, { Constants.GaugeMotherboard, 0.0 }, { Constants.GaugeNetwork, 0.0 }
    };

    
    // ── Simulated SSD Customization ──────────────────────────────────────────
    public string CustomSsdImagePath { get; set; } = "";

    // ── Sensor Overrides ─────────────────────────────────────────────────────
    public SensorNamesConfig SensorNames { get; set; } = new();
}

public class ClockConfig
{
    // Analog Face
    public string FaceName { get; set; } = "Classic"; // Classic, Neon, Minimal, Glow, Bold
    public string HourHandColor { get; set; } = "#FFFFFF";
    public string MinuteHandColor { get; set; } = "#FFFFFF";
    public string SecondHandColor { get; set; } = "#3b82f6";
    public string ClockFaceColor { get; set; } = "#1A1A1A";
    public string MarkerColor { get; set; } = "#FFFFFF";
    public bool ContinuousMotion { get; set; } = false;
    public bool ShowGlow { get; set; } = false;
    public bool ShowOuterRing { get; set; } = true;
    public string GlowColor { get; set; } = Constants.DefaultClockGlowColor;
    public double GlowWidth { get; set; } = Constants.DefaultClockGlowWidth;
    public double ClockScale { get; set; } = 1.0;
    public double ClockOffsetX { get; set; } = 0.0;
    public double ClockOffsetY { get; set; } = 0.0;

    // Digital Clock
    public string DigitalColor { get; set; } = "#FFFFFF";
    public string DigitalFontFamily { get; set; } = "Segoe UI";
    public double DigitalFontSize { get; set; } = 80.0;
    public string DigitalFormat { get; set; } = "12h"; // 12h or 24h
    public double DigitalOffsetX { get; set; } = 0.0;
    public double DigitalOffsetY { get; set; } = 0.0;
    public bool ShowDigitalClock { get; set; } = true;

    // Date
    public string DateColor { get; set; } = "#AAAAAA";
    public string DateFontFamily { get; set; } = "Segoe UI";
    public double DateFontSize { get; set; } = 18.0;
    public string DateFormat { get; set; } = "Long"; // Long, Short, Numeric
    public double DateOffsetX { get; set; } = 0.0;
    public double DateOffsetY { get; set; } = 0.0;
    public bool ShowDate { get; set; } = true;

    // Screen Background
    public bool UseCustomBackground { get; set; } = false;
    public string CustomBackgroundColor { get; set; } = "#060606";
    public string CustomBackgroundImagePath { get; set; } = "";
    public double CustomBackgroundOpacity { get; set; } = 1.0;
}

public class WeatherConfig
{
    public string ApiKey { get; set; } = Constants.DefaultWeatherApiKey;
    public string City { get; set; } = Constants.DefaultWeatherCity;
    public string Units { get; set; } = Constants.DefaultWeatherUnits; // metric, imperial, standard
    public int UpdateIntervalMinutes { get; set; } = Constants.DefaultWeatherUpdateIntervalMinutes;
    public bool ShowForecast { get; set; } = true;
    public string WeatherTheme { get; set; } = "Dark"; // Dark, Light, System
    public string TextColor { get; set; } = "#FFFFFF";
    public string AccentColor { get; set; } = "#3b82f6";
    public string GlowColor { get; set; } = "#3b82f6";
}

public class SensorNamesConfig
{
    public string[] CpuTemp { get; set; } = new[] { "Core (Tctl/Tdie)", "Tctl/Tdie", "Core Average", "CPU Package", "Package", "Core Max" };
    public string[] CpuLoad { get; set; } = new[] { "CPU Total", "Total", "CPU Core" };
    public string[] CpuClock { get; set; } = new[] { "CPU Core #1", "Core #1", "Core 1", "CPU Core" };
    public string[] GpuTemp { get; set; } = new[] { "GPU Core", "Core" };
    public string[] GpuLoad { get; set; } = new[] { "GPU Core", "Core", "D3D 3D" };
    public string[] GpuClock { get; set; } = new[] { "GPU Core", "Core" };
    public string[] RamLoad { get; set; } = new[] { Constants.DefaultMemorySensorName };
}
