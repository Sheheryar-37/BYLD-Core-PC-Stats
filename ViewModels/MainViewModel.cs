using System.Windows;
using PcStatsMonitor.Models;
using PcStatsMonitor.Services;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;

namespace PcStatsMonitor.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly IHardwareMonitorService _monitorService;
    private readonly IThemeService _themeService;

    private HardwareMetrics _metrics = new();
    public HardwareMetrics Metrics
    {
        get => _metrics;
        set => SetProperty(ref _metrics, value);
    }

    public ObservableCollection<FanMetric> ObservableFans { get; } = new();

    private ThemeConfig _themeConfig = new();
    public ThemeConfig Theme
    {
        get => _themeConfig;
        set {
            if (SetProperty(ref _themeConfig, value))
            {
                UpdateLogoBrush();
            }
        }
    }

    private System.Windows.Media.Brush _logoBrush = System.Windows.Media.Brushes.White;
    public System.Windows.Media.Brush LogoBrush
    {
        get => _logoBrush;
        set => SetProperty(ref _logoBrush, value);
    }

    /// <summary>
    /// RGB ViewModel exposed for the RGB Display Screen to bind against.
    /// Populated with demo data automatically when demo mode is active.
    /// </summary>
    public RgbControlViewModel Rgb { get; }

    /// <summary>The process-wide fan control view-model. Owned here so the curve engine keeps
    /// running whether or not the Settings window is open (client round 18, items 10 and 11).</summary>
    public FanControlViewModel FanControl { get; }
    private bool _rgbDemoLoaded = false;

    private void UpdateLogoBrush()
    {
        // The logo tint follows the widget theme for the 7" display — a light
        // widget theme needs a dark logo, and vice versa.
        SetLogoBrush(Theme?.IsWidgetThemeLight ?? false);
    }

    /// <summary>
    /// Tints the 7" logo for the given mode: near-black on a light background, white on a
    /// dark one. Public because the logo must follow the resolved screen theme even when
    /// the rest of the palette is unchanged and the full binding refresh is skipped — the
    /// logo stayed white on the light theme otherwise (client round 14, item 11).
    /// Setting only this brush is safe: unlike a full Theme refresh it cannot feed back
    /// into the screen-evaluation loop.
    /// </summary>
    public void SetLogoTheme(bool isLight) => SetLogoBrush(isLight);

    private void SetLogoBrush(bool isLight)
    {
        LogoBrush = isLight
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x11, 0x18, 0x27))
            : System.Windows.Media.Brushes.White;
    }

    /// <summary>The 7" fan-icon colour for a fan: the user's chosen colour (keyed by name)
    /// when set, otherwise a distinct default from the palette by position.</summary>
    private System.Windows.Media.Brush ResolveFanBrush(string name, int index)
    {
        var colors = _themeService?.CurrentTheme?.FanColors;
        if (colors != null && !string.IsNullOrEmpty(name) &&
            colors.TryGetValue(name, out var hex) && !string.IsNullOrWhiteSpace(hex))
            return FanMetric.BrushFromHex(hex);
        return FanMetric.PaletteBrush(index);
    }

    private bool _rgbLiveConnectStarted;

    /// <summary>
    /// Starts the OpenRGB connection once, in the background, on real hardware. Without this the
    /// 7" RGB screen and the device list stayed empty until the Settings window was opened (the
    /// only other place that connects), which is why the client had to "manually sync" every run
    /// (client round 18, item 6). Runs off the first metrics tick rather than at construction so
    /// it never delays startup; ConnectAsync does all socket work on the thread pool.
    /// </summary>
    private void ConnectLiveRgbOnce()
    {
        if (_rgbLiveConnectStarted || HardwareControlService.IsDemoMode) return;
        _rgbLiveConnectStarted = true;
        Rgb.Connect();
    }

    /// <summary>
    /// Rebuilds the 7" fan list in place, skipping fans the user hid (client round 18, item 9b).
    /// The palette colour uses each fan's ORIGINAL index so hiding one never re-colours the rest.
    /// </summary>
    /// <summary>The most recent metrics, kept so the 7" fan list can be rebuilt on demand
    /// (e.g. right after the user hides or reorders a fan) without waiting for the next tick.</summary>
    private HardwareMetrics? _lastMetrics;

    /// <summary>Rebuilds the 7" fan list immediately from the last known readings.</summary>
    private void RefreshFanWidget()
    {
        if (_lastMetrics is { } metrics)
            Application.Current?.Dispatcher.Invoke(() => SyncObservableFans(metrics));
    }

    private void SyncObservableFans(HardwareMetrics metrics)
    {
        _lastMetrics = metrics;
        var theme = _themeService?.CurrentTheme;
        var hidden = theme?.HiddenFanNames;
        var visible = new List<(string real, double speed, int index)>();
        for (int i = 0; i < metrics.Fans.Count; i++)
        {
            string real = metrics.Fans[i].Name;
            if (hidden == null || !hidden.Contains(real))
                visible.Add((real, metrics.Fans[i].Speed, i));
        }

        // Honour the user's chosen order for the 7" display (client round 18, item 13).
        // OrderBy is stable, so fans the user never moved keep their detected order.
        if (theme != null)
            visible = visible
                .OrderBy(f => ThemeConfig.DisplayOrderIndex(theme.FanDisplayOrder, f.real))
                .ToList();

        for (int j = 0; j < visible.Count; j++)
            ApplyFanRow(j, visible[j].real, visible[j].speed, visible[j].index);

        while (ObservableFans.Count > visible.Count)
            ObservableFans.RemoveAt(ObservableFans.Count - 1);
    }

    /// <summary>Updates (or appends) one 7" fan row. Colour/name are keyed by the real sensor
    /// name; only the shown label switches to the user's custom name.</summary>
    private void ApplyFanRow(int slot, string real, double speed, int paletteIndex)
    {
        if (slot < ObservableFans.Count)
        {
            ObservableFans[slot].Name = ResolveFanDisplayName(real);
            ObservableFans[slot].Speed = speed;
            ObservableFans[slot].AnimationBrush = ResolveFanBrush(real, paletteIndex);
            return;
        }

        ObservableFans.Add(new FanMetric
        {
            Name = ResolveFanDisplayName(real),
            Speed = speed,
            AnimationBrush = ResolveFanBrush(real, paletteIndex)
        });
    }

    /// <summary>The label to show for a fan on the 7" widget: the user's custom name
    /// (keyed by the hardware sensor name) when set, otherwise the hardware name itself.</summary>
    private string ResolveFanDisplayName(string sensorName)
    {
        var names = _themeService?.CurrentTheme?.FanNames;
        if (names != null && !string.IsNullOrEmpty(sensorName) &&
            names.TryGetValue(sensorName, out var custom) && !string.IsNullOrWhiteSpace(custom))
            return custom;
        return sensorName;
    }

    /// <summary>
    /// Re-raises the Theme bindings after in-memory colour swaps (per-screen
    /// theme overrides). When <paramref name="logoLightOverride"/> is given, the
    /// logo tint follows the active screen's resolved theme instead of the
    /// global widget theme.
    /// </summary>
    public void NotifyThemeRefreshed(bool? logoLightOverride = null)
    {
        OnPropertyChanged(nameof(Theme));
        if (logoLightOverride is { } isLight)
            SetLogoBrush(isLight);
        else
            UpdateLogoBrush();
    }

    public MainViewModel(IHardwareMonitorService monitorService, IThemeService themeService,
        HardwareControlService hardwareControl)
    {
        _monitorService = monitorService;
        _themeService = themeService;
        Rgb = new RgbControlViewModel(hardwareControl, themeService);

        // Owned by the main window, NOT the Settings window: the curve engine lives in this
        // view-model, so a Settings-scoped instance meant fan control stopped the moment Settings
        // was closed — leaving fans pinned at the last written percentage with nothing updating
        // them, and making the remembered "Let BYLD Core control my fans" choice pointless on
        // launch (client round 18, items 10 and 11). Settings now drives this same instance.
        FanControl = new FanControlViewModel(hardwareControl, themeService);

        // Rebuild the 7" fan list the instant the user hides/reorders a fan, rather than waiting
        // for the next sensor tick — that wait read as a lag (client round 19, item 6).
        FanControl.WidgetLayoutChanged += (_, _) => RefreshFanWidget();

        _themeService.ThemeChanged += (s, theme) =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                // ThemeService mutates and re-sends the SAME ThemeConfig instance,
                // so the Theme setter's change detection never fires — refresh the
                // derived state explicitly here.
                Theme = theme;
                OnPropertyChanged(nameof(Theme));
                UpdateLogoBrush();
            });
        };
        Theme = _themeService.CurrentTheme;
        UpdateLogoBrush();

        _monitorService.MetricsUpdated += (s, metrics) => 
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                Metrics = metrics;
                SyncObservableFans(metrics);

                // Sync demo RGB devices for the RGB display screen
                if (HardwareControlService.IsDemoMode && !_rgbDemoLoaded)
                {
                    _rgbDemoLoaded = true;
                    Rgb.Connect();
                }
                else if (!HardwareControlService.IsDemoMode && _rgbDemoLoaded)
                {
                    _rgbDemoLoaded = false;
                    Rgb.Devices.Clear();
                    Rgb.IsConnected = false;
                }

                ConnectLiveRgbOnce();
            });
        };
    }
}
