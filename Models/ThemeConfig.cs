namespace PcStatsMonitor.Models;

/// <summary>
/// A snapshot of the user's chosen 7"-display colours for one theme mode
/// (dark OR light), remembered so switching modes never destroys the other
/// mode's customizations.
/// </summary>
public class PaletteSnapshot
{
    public string Background { get; set; } = Constants.DefaultThemeBackground;
    public string Foreground { get; set; } = Constants.DefaultThemeForeground;
    public string Track { get; set; } = Constants.DefaultThemeTrack;
    public string ClockFace { get; set; } = "#1A1A1A";
    public string ClockHour { get; set; } = "#FFFFFF";
    public string ClockMinute { get; set; } = "#FFFFFF";
    public string ClockMarker { get; set; } = "#FFFFFF";
    public string ClockDigital { get; set; } = "#FFFFFF";
    public string ClockDate { get; set; } = "#AAAAAA";
    public string WeatherTheme { get; set; } = "Dark";

    /// <summary>The default LIGHT-mode palette — used to seed a fresh light palette.</summary>
    public static PaletteSnapshot LightDefault() => new()
    {
        Background = "#F4F6FA", Foreground = "#1E293B", Track = "#D9DEE7",
        ClockFace = "#FFFFFF", ClockHour = "#1E293B", ClockMinute = "#1E293B",
        ClockMarker = "#475569", ClockDigital = "#1E293B", ClockDate = "#475569",
        WeatherTheme = "Light"
    };
}

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

    /// <summary>Overall SSD card colour. Empty = the default metallic gradient.</summary>
    public string StorageCardColor { get; set; } = "";

    /// <summary>Colour of the spinning fan icons on the 7" System Cooling screen.
    /// Empty = follow <see cref="AccentColor"/>.</summary>
    public string FanAnimationColor { get; set; } = "";

    /// <summary>Per-fan icon colour on the 7" System Cooling screen, chosen by the user and
    /// keyed by fan name. A fan with no entry uses a distinct default from the built-in
    /// palette, so fans are still easy to tell apart out of the box.</summary>
    public Dictionary<string, string> FanColors { get; set; } = new();

    /// <summary>Custom display name per fan, chosen by the user and keyed by the hardware
    /// sensor name (the stable key). A fan with no entry shows its hardware name. Applied on
    /// both the Fan Control cards and the 7" System Cooling widget (client round 17, item 3).</summary>
    public Dictionary<string, string> FanNames { get; set; } = new();

    /// <summary>Resolved brush for the fan icons — the user's colour when set, else the accent.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public System.Windows.Media.SolidColorBrush FanAnimationBrush
    {
        get
        {
            if (string.IsNullOrWhiteSpace(FanAnimationColor)) return AccentColorBrush;
            try
            {
                return new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(FanAnimationColor));
            }
            catch
            {
                return AccentColorBrush;
            }
        }
    }

    /// <summary>Advanced: keep sending fan-control commands even to hardware that
    /// appears to ignore them (for users with a driver-side workaround enabled).</summary>
    public bool ForceFanControlOverride { get; set; } = false;

    /// <summary>
    /// Diagnostic logging on/off (hardware, sensor, display, action and app logs).
    /// Persisted so it survives restarts; OFF on first run because logging writes to
    /// disk continuously. Crash logs are always written regardless.
    /// </summary>
    public bool LoggingEnabled { get; set; } = false;

    /// <summary>User-chosen background colour for the desktop app/settings window. Empty = the
    /// built-in Dark/Light/System window background (client round 17, item 6).</summary>
    public string UiBackgroundColor { get; set; } = "";

    /// <summary>User-chosen accent colour for the desktop app/settings window (buttons, tabs,
    /// highlights). Empty = the built-in blue accent (client round 17, item 6).</summary>
    public string UiAccentColor { get; set; } = "";

    /// <summary>The SSD card fill: a solid user colour when set, else the default metallic gradient.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public System.Windows.Media.Brush StorageCardBrush
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(StorageCardColor))
                return TrySolid(StorageCardColor, System.Windows.Media.Color.FromRgb(0x12, 0x12, 0x12));
            return DefaultSsdGradient();
        }
    }

    private static System.Windows.Media.Brush TrySolid(string hex, System.Windows.Media.Color fallback)
    {
        try { return new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)); }
        catch { return new System.Windows.Media.SolidColorBrush(fallback); }
    }

    private static System.Windows.Media.Brush DefaultSsdGradient()
    {
        var b = new System.Windows.Media.LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0),
            EndPoint = new System.Windows.Point(1, 1)
        };
        b.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(0x1A, 0x1A, 0x1A), 0));
        b.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(0x12, 0x12, 0x12), 0.2));
        b.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(0x08, 0x08, 0x08), 0.8));
        b.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(0x05, 0x05, 0x05), 1));
        b.Freeze();
        return b;
    }

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
    /// <summary>
    /// The user's own colour palettes, remembered PER MODE so switching between
    /// light and dark never destroys the other mode's customizations (client
    /// round 9: dark forgot its colours on light→dark, and light forgot its own).
    /// </summary>
    public PaletteSnapshot SavedDarkPalette { get; set; } = new();
    public PaletteSnapshot SavedLightPalette { get; set; } = PaletteSnapshot.LightDefault();

    /// <summary>Copies the currently-applied colours into the palette for the given
    /// mode. Called whenever the user edits colours (mode = the current display mode).</summary>
    public void CaptureUserPalette(bool light)
    {
        var snapshot = new PaletteSnapshot
        {
            Background = BackgroundColor, Foreground = ForegroundColor, Track = TrackColor,
            ClockFace = Clock.ClockFaceColor, ClockHour = Clock.HourHandColor,
            ClockMinute = Clock.MinuteHandColor, ClockMarker = Clock.MarkerColor,
            ClockDigital = Clock.DigitalColor, ClockDate = Clock.DateColor,
            WeatherTheme = Weather.WeatherTheme
        };
        if (light) SavedLightPalette = snapshot;
        else SavedDarkPalette = snapshot;
    }

    /// <summary>
    /// Applies the widget colour preset by RESTORING the saved palette for that
    /// mode. Both modes keep their own palette, so light↔dark round-trips are
    /// fully lossless in both directions.
    /// </summary>
    public void ApplyWidgetColorPreset(bool light)
    {
        var p = (light ? SavedLightPalette : SavedDarkPalette) ?? new PaletteSnapshot();
        BackgroundColor = p.Background; ForegroundColor = p.Foreground; TrackColor = p.Track;
        Clock.ClockFaceColor = p.ClockFace; Clock.HourHandColor = p.ClockHour;
        Clock.MinuteHandColor = p.ClockMinute; Clock.MarkerColor = p.ClockMarker;
        Clock.DigitalColor = p.ClockDigital; Clock.DateColor = p.ClockDate;
        Weather.WeatherTheme = p.WeatherTheme;
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
