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
        set => SetProperty(ref _themeConfig, value);
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

        _monitorService.MetricsUpdated += (s, metrics) => 
        {
            Application.Current.Dispatcher.Invoke(() => Metrics = metrics);
        };
    }
}
