using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PcStatsMonitor.Services;
using Serilog;
using Microsoft.Extensions.Logging;
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
                services.AddSingleton<LicenseService>();
                services.AddSingleton<IThemeService, ThemeService>();
                services.AddSingleton<IHardwareMonitorService, HardwareMonitorService>();
                services.AddHostedService(provider => (HardwareMonitorService)provider.GetRequiredService<IHardwareMonitorService>());
                services.AddSingleton<PcStatsMonitor.ViewModels.MainViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        // Register Global Exception Handlers
        this.DispatcherUnhandledException += (s, args) => 
        { 
            CrashLogger.LogCrash(args.Exception, "DispatcherUnhandledException"); 
            args.Handled = true; // Prevent app from exiting immediately so logs can flush
        };
        AppDomain.CurrentDomain.UnhandledException += (s, args) => 
        { 
            if (args.ExceptionObject is Exception ex)
                CrashLogger.LogCrash(ex, "AppDomain UnhandledException"); 
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, args) => 
        { 
            CrashLogger.LogCrash(args.Exception, "UnobservedTaskException"); 
            args.SetObserved();
        };
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        // ── Mandatory Administrator Check ──
        if (!IsRunAsAdmin())
        {
            MessageBox.Show(
                "BYLD Core requires Administrator privileges to access hardware sensors (CPU Temp, Clock, etc.).\n\nPlease close the app and 'Run as Administrator'.", 
                "Elevation Required", 
                MessageBoxButton.OK, 
                MessageBoxImage.Warning);
            
            // We don't shutdown here to allow the user to at least see the UI, 
            // but the warning explains why it's empty.
        }

        // Install the WinRing0x64 kernel driver BEFORE the host starts so that
        // LibreHardwareMonitor can read CPU temperature and clock via MSR on first run.
        var startupLogger = _host.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<App>>();
        try 
        {
            KernelDriverService.EnsureInstalled(startupLogger);
        }
        catch (Exception kernelEx)
        {
            startupLogger.LogWarning(kernelEx, "Skipping KernelDriverService installation because the IDE terminal lacks elevation.");
        }

        // ── License Verification is now securely handled within MainWindow ──
        startupLogger.LogInformation("[Startup] Passing execution to MainWindow for initialization...");

        await _host.StartAsync();

        DisplayDiagnosticLogger.LogDisplays("STARTUP");

        _notifyIcon = new System.Windows.Forms.NotifyIcon();
        try
        {
            var iconUri = new Uri("pack://application:,,,/Assets/byld-icon.ico");
            var iconInfo = Application.GetResourceStream(iconUri);
            if (iconInfo != null)
            {
                using var stream = iconInfo.Stream;
                using var bitmap = new System.Drawing.Bitmap(stream);
                _notifyIcon.Icon = System.Drawing.Icon.FromHandle(bitmap.GetHicon());
            }
        }
        catch 
        {
            // Fallback placeholder if icon conversion fails
            _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
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
        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        
        foreach (Window window in Current.Windows)
        {
            if (window is SettingsWindow)
            {
                window.Activate();
                return;
            }
        }
        var settingsWindow = new SettingsWindow(themeService, mainWindow.PluginManager);
        settingsWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        DisplayDiagnosticLogger.LogDisplays("EXIT");

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

    private static bool IsRunAsAdmin()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }
}
