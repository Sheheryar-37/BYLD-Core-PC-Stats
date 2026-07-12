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

    [System.Text.Json.Serialization.JsonIgnore] public System.Windows.Media.SolidColorBrush AccentColorBrush { get { try { return new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(AccentColor)); } catch { return System.Windows.Media.Brushes.DodgerBlue; } } }
    [System.Text.Json.Serialization.JsonIgnore] public System.Windows.Media.SolidColorBrush ForegroundColorBrush { get { try { return new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(ForegroundColor)); } catch { return System.Windows.Media.Brushes.White; } } }
    [System.Text.Json.Serialization.JsonIgnore] public System.Windows.Media.SolidColorBrush BackgroundColorBrush { get { try { return new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(BackgroundColor)); } catch { return System.Windows.Media.Brushes.Black; } } }

    /// <summary>Accent colour for the storage screen's SSD card square. Empty = follow <see cref="AccentColor"/>.</summary>
    public string StorageAccentColor { get; set; } = "";

    /// <summary>Resolved brush for the SSD card accent square.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public System.Windows.Media.SolidColorBrush StorageAccentBrush
    {
        get
        {
            if (string.IsNullOrWhiteSpace(StorageAccentColor)) return AccentColorBrush;
            try
            {
                return new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(StorageAccentColor));
            }
            catch
            {
                return AccentColorBrush;
            }
        }
    }

    /// <summary>Card surface for widget screens, following the resolved widget theme.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public System.Windows.Media.SolidColorBrush WidgetCardBrush => IsWidgetThemeLight
        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF))
        : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x11, 0x11, 0x11));

    /// <summary>Card border for widget screens, following the resolved widget theme.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public System.Windows.Media.SolidColorBrush WidgetCardBorderBrush => IsWidgetThemeLight
        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x26, 0x00, 0x00, 0x00))
        : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x22, 0x22, 0x22));
    public bool LaunchOnStartup { get; set; } = false;

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
    public bool ShowFansScreen { get; set; } = true;
    public bool ShowRgbScreen { get; set; } = true;
    public List<string> EnabledPlugins { get; set; } = new() { "System Clock", "Fan & RGB Controller" };

    // ── Utility / Restrictions ───────────────────────────────────────────────
    public bool DisableSecondaryScreenDragging { get; set; } = false;

    // ── Settings UI Appearance ───────────────────────────────────────────────
    /// <summary>Settings backend UI theme: "Dark" (default) or "Light".</summary>
    public string UiTheme { get; set; } = "Dark";

    /// <summary>When false, the glassy translucent surfaces are replaced with solid panels.</summary>
    public bool LiquidGlassEnabled { get; set; } = true;

    /// <summary>
    /// Theme for the 7" display widgets (clock, gauges): "Dark" (default),
    /// "Light", "System" (Windows app theme), or "Auto" (follows <see cref="UiTheme"/>).
    /// </summary>
    public string WidgetTheme { get; set; } = "Dark";

    /// <summary>Resolved settings-window theme: true when the Settings UI should render light.
    /// "System" resolves against the Windows app theme.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsUiThemeLight =>
        string.Equals(UiTheme, "Light", StringComparison.OrdinalIgnoreCase) ||
        (string.Equals(UiTheme, "System", StringComparison.OrdinalIgnoreCase) && IsWindowsAppThemeLight());

    /// <summary>Resolved widget theme: true when the 7" display should render light.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsWidgetThemeLight =>
        string.Equals(WidgetTheme, "Light", StringComparison.OrdinalIgnoreCase) ||
        (string.Equals(WidgetTheme, "System", StringComparison.OrdinalIgnoreCase) && IsWindowsAppThemeLight()) ||
        (string.Equals(WidgetTheme, "Auto", StringComparison.OrdinalIgnoreCase) && IsUiThemeLight);

    /// <summary>
    /// Optional per-screen theme overrides for the 7" display's rotating screens.
    /// Key: screen id ("Gauges", "Storage", "Clock", "Weather", "Fans", "RGB");
    /// value: "Dark" or "Light". Screens without an entry (or set to "Auto")
    /// follow <see cref="WidgetTheme"/>.
    /// </summary>
    public Dictionary<string, string> ScreenThemes { get; set; } = new();

    /// <summary>True when the given rotating screen has an explicit Dark/Light override.</summary>
    public bool HasScreenThemeOverride(string screenKey)
    {
        return ScreenThemes != null &&
               ScreenThemes.TryGetValue(screenKey, out var value) &&
               (value.Equals("Dark", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Light", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Resolved theme for one rotating screen: its own override when set,
    /// otherwise the widget theme.</summary>
    public bool IsScreenThemeLight(string screenKey)
    {
        if (ScreenThemes != null && ScreenThemes.TryGetValue(screenKey, out var value))
        {
            if (value.Equals("Light", StringComparison.OrdinalIgnoreCase)) return true;
            if (value.Equals("Dark", StringComparison.OrdinalIgnoreCase)) return false;
        }

        return IsWidgetThemeLight;
    }

    /// <summary>
    /// Writes the widget colour preset (7" background/foreground/track, clock
    /// colours, weather theme) for light or dark onto this config. Shared by the
    /// widget-theme setting and the per-screen overrides.
    /// </summary>
    public void ApplyWidgetColorPreset(bool light)
    {
        BackgroundColor = light ? "#F4F6FA" : Constants.DefaultThemeBackground;
        ForegroundColor = light ? "#1E293B" : Constants.DefaultThemeForeground;
        TrackColor      = light ? "#D9DEE7" : Constants.DefaultThemeTrack;
        Clock.ClockFaceColor  = light ? "#FFFFFF" : "#1A1A1A";
        Clock.HourHandColor   = light ? "#1E293B" : "#FFFFFF";
        Clock.MinuteHandColor = light ? "#1E293B" : "#FFFFFF";
        Clock.MarkerColor     = light ? "#475569" : "#FFFFFF";
        Clock.DigitalColor    = light ? "#1E293B" : "#FFFFFF";
        Clock.DateColor       = light ? "#475569" : "#AAAAAA";
        Weather.WeatherTheme  = light ? "Light" : "Dark";
    }

    /// <summary>Reads the Windows "app mode" (light/dark) the user set in Windows Settings.</summary>
    private static bool IsWindowsAppThemeLight()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 1;
        }
        catch
        {
            return false;
        }
    }

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
    public string TimeFormat { get; set; } = "12h"; // 12h or 24h
    public bool ShowWeatherGallery { get; set; } = false;
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
