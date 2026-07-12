using System;
using System.Windows;
using System.Windows.Threading;
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
    private SplashWindow? _splash;
    private SplashWindow? _splashSecondary;

    private System.Windows.Forms.NotifyIcon? _notifyIcon;

    public App()
    {
        // Information level: per-tick Debug chatter wrote ~15 log lines/second
        // (disk I/O + string formatting every poll) and contributed to the
        // client's "PC runs hard" report. Warnings, errors and the [RGB→]/[FAN]
        // forensic entries (hardware.log) are unaffected.
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(Models.Constants.LogFilePath, rollingInterval: RollingInterval.Day)
            .CreateLogger();

        _host = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices((context, services) =>
            {
                services.AddSingleton<LicenseService>();
                services.AddSingleton<IThemeService, ThemeService>();
                // ONE HardwareControlService for the whole process: LibreHardwareMonitor's
                // Ring0 driver state is process-global, so multiple Computer instances
                // corrupt each other when any of them closes (NRE storm on every poll).
                services.AddSingleton<HardwareControlService>();
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
            // OpenRGB.NET's socket read-loop throws aborted-I/O SocketExceptions when
            // the connection closes (app exit, server restart). Benign — don't record
            // them as crashes, or the crash log fills with noise on every shutdown.
            bool benignSocketAbort = System.Linq.Enumerable.All(
                args.Exception.Flatten().InnerExceptions,
                ex => ex is System.Net.Sockets.SocketException);
            if (!benignSocketAbort)
                CrashLogger.LogCrash(args.Exception, "UnobservedTaskException");
            args.SetObserved();
        };
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        // ── Show Splash Screen immediately (plus one on the secondary display) ──
        _splash = new SplashWindow();
        _splash.Show();
        _splashSecondary = SplashWindow.TryCreateForSecondaryDisplay();
        _splashSecondary?.Show();
        SetSplashStatus("Checking privileges...");

        // ── Mandatory Administrator Check ──
        if (!IsRunAsAdmin())
        {
            if (_splash != null) _splash.Topmost = false;
            
            MessageBox.Show(
                _splash,
                "BYLD Core requires Administrator privileges to access hardware sensors (CPU Temp, Clock, etc.).\n\nPlease close the app and 'Run as Administrator'.", 
                "Elevation Required", 
                MessageBoxButton.OK, 
                MessageBoxImage.Warning);
            
            if (_splash != null) _splash.Topmost = true;
            
            // We don't shutdown here to allow the user to at least see the UI, 
            // but the warning explains why it's empty.
        }

        // Install the WinRing0x64 kernel driver BEFORE the host starts so that
        // LibreHardwareMonitor can read CPU temperature and clock via MSR on first run.
        var startupLogger = _host.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<App>>();
        SetSplashStatus("Installing hardware drivers...");
        try
        {
            KernelDriverService.EnsureInstalled(startupLogger);
            // Functional Ring0 check with a throwaway probe, BEFORE any long-lived
            // Computer instance opens — reclaiming after they exist corrupts
            // LibreHardwareMonitor's shared static driver state.
            KernelDriverService.VerifyRing0WithProbe(startupLogger);
        }
        catch (Exception kernelEx)
        {
            startupLogger.LogWarning(kernelEx, "Skipping KernelDriverService installation because the IDE terminal lacks elevation.");
        }

        // ── License Verification is now securely handled within MainWindow ──
        startupLogger.LogInformation("[Startup] Passing execution to MainWindow for initialization...");

        SetSplashStatus("Starting hardware monitoring...");
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

        SetSplashStatus("Loading interface...");
        var mainWindow = _host.Services.GetRequiredService<MainWindow>();

        // ── Keep MainWindow invisible until hardware data is ready ──
        // This prevents the user from seeing empty gauges after the splash closes.
        mainWindow.Opacity = 0;
        mainWindow.Show();

        SetSplashStatus("Waiting for sensor data...");

        // Subscribe to the first MetricsUpdated event to know when gauges have real data
        var hwService = _host.Services.GetRequiredService<IHardwareMonitorService>();
        var splashRef = _splash;
        var splashSecondaryRef = _splashSecondary;
        bool splashClosed = false;

        void CloseSplashAndReveal()
        {
            if (splashClosed) return;
            splashClosed = true;

            Dispatcher.Invoke(() =>
            {
                // Fade in the main window
                var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0.0, 1.0,
                    new Duration(TimeSpan.FromMilliseconds(350)));
                mainWindow.BeginAnimation(Window.OpacityProperty, fadeIn);

                // Close both splash screens with smooth fade-out
                splashRef?.FinishAndClose();
                splashSecondaryRef?.FinishAndClose();
            });
        }

        // Close splash when first hardware metrics arrive (gauges will be populated)
        void OnFirstMetrics(object? sender, Models.HardwareMetrics metrics)
        {
            hwService.MetricsUpdated -= OnFirstMetrics;
            CloseSplashAndReveal();
        }
        hwService.MetricsUpdated += OnFirstMetrics;

        // Safety timeout: if sensors take too long (e.g., driver issues), close splash anyway after 10s
        var safetyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        safetyTimer.Tick += (s, args) =>
        {
            safetyTimer.Stop();
            hwService.MetricsUpdated -= OnFirstMetrics;
            CloseSplashAndReveal();
        };
        safetyTimer.Start();
        
        base.OnStartup(e);
    }

    /// <summary>Mirrors a loading status message to both splash screens.</summary>
    private void SetSplashStatus(string message)
    {
        _splash?.SetStatus(message);
        _splashSecondary?.SetStatus(message);
    }

    private void ShowSettings()
    {
        var themeService = _host.Services.GetRequiredService<IThemeService>();
        var hwControl = _host.Services.GetRequiredService<HardwareControlService>();
        var mainWindow = _host.Services.GetRequiredService<MainWindow>();

        foreach (Window window in Current.Windows)
        {
            if (window is SettingsWindow)
            {
                window.Activate();
                return;
            }
        }
        var settingsWindow = new SettingsWindow(themeService, hwControl, mainWindow.PluginManager);
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
