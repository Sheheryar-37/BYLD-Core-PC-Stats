using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PcStatsMonitor.Services;
using Serilog;
using Application = System.Windows.Application;

namespace PcStatsMonitor;

public partial class App : Application
{
    private IHost _host;

    private System.Windows.Forms.NotifyIcon? _notifyIcon;

    public App()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(Models.Constants.LogFilePath, rollingInterval: RollingInterval.Day)
            .CreateLogger();

        _host = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices((context, services) =>
            {
                services.AddSingleton<IThemeService, ThemeService>();
                services.AddSingleton<IHardwareMonitorService, HardwareMonitorService>();
                services.AddHostedService(provider => (HardwareMonitorService)provider.GetRequiredService<IHardwareMonitorService>());
                services.AddSingleton<PcStatsMonitor.ViewModels.MainViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        // Install the WinRing0x64 kernel driver BEFORE the host starts so that
        // LibreHardwareMonitor can read CPU temperature and clock via MSR on first run.
        var startupLogger = _host.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<App>>();
        KernelDriverService.EnsureInstalled(startupLogger);

        await _host.StartAsync();

        _notifyIcon = new System.Windows.Forms.NotifyIcon();
        var iconUri = new Uri("pack://application:,,,/Assets/icon-black.ico");
        var iconStream = Application.GetResourceStream(iconUri)?.Stream;
        if (iconStream != null)
        {
            _notifyIcon.Icon = new System.Drawing.Icon(iconStream);
        }
        _notifyIcon.Visible = true;
        _notifyIcon.Text = "BYLD Core";
        _notifyIcon.DoubleClick += (s, args) => ShowSettings();

        var contextMenu = new System.Windows.Forms.ContextMenuStrip();
        contextMenu.Items.Add("Settings", null, (s, args) => ShowSettings());
        contextMenu.Items.Add("Exit", null, (s, args) => Shutdown());
        _notifyIcon.ContextMenuStrip = contextMenu;

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
        
        base.OnStartup(e);
    }

    private void ShowSettings()
    {
        var themeService = _host.Services.GetRequiredService<IThemeService>();
        foreach (Window window in Current.Windows)
        {
            if (window is SettingsWindow)
            {
                window.Activate();
                return;
            }
        }
        var settingsWindow = new SettingsWindow(themeService);
        settingsWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }

        using (_host)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5));
        }
        
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
