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
        bool isLight = false;
        var mode = Theme?.Weather?.WeatherTheme ?? "Dark";
        if (mode == "System" || mode == "Auto")
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var val = key?.GetValue("AppsUseLightTheme");
                isLight = (val is int i && i == 1);
            }
            catch { }
        }
        else
        {
            isLight = mode == "Light";
        }
        LogoBrush = isLight ? System.Windows.Media.Brushes.Black : System.Windows.Media.Brushes.White;
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
                Theme = theme;
                OnPropertyChanged(nameof(Theme));
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
