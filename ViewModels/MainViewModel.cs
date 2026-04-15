using System.Windows;
using PcStatsMonitor.Models;
using PcStatsMonitor.Services;

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

    public MainViewModel(IHardwareMonitorService monitorService, IThemeService themeService)
    {
        _monitorService = monitorService;
        _themeService = themeService;

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
            Application.Current.Dispatcher.Invoke(() => Metrics = metrics);
        };
    }
}
