using System.Windows;
using PcStatsMonitor.Models;
using PcStatsMonitor.Services;
using System.Collections.ObjectModel;

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
        Rgb = new RgbControlViewModel(hardwareControl);

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

                // Update ObservableFans in-place to prevent UI element recreation and flickering
                for (int i = 0; i < metrics.Fans.Count; i++)
                {
                    if (i < ObservableFans.Count)
                    {
                        ObservableFans[i].Name = metrics.Fans[i].Name;
                        ObservableFans[i].Speed = metrics.Fans[i].Speed;
                    }
                    else
                    {
                        ObservableFans.Add(new FanMetric { Name = metrics.Fans[i].Name, Speed = metrics.Fans[i].Speed });
                    }
                }
                while (ObservableFans.Count > metrics.Fans.Count)
                {
                    ObservableFans.RemoveAt(ObservableFans.Count - 1);
                }

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
            });
        };
    }
}
