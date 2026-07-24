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
        // ControlledBy: logging is gated by the user's Logging toggle (off on first run)
        // and can be switched at runtime without rebuilding the logger.
        // flushToDiskInterval: Serilog buffers writes, so a hard crash discarded the very
        // entries that explain it — the client's freeze left no trace at all. Flush every
        // second so diagnostics survive the crash they describe.
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(AppLogging.LevelSwitch)
            .WriteTo.File(Models.Constants.LogFilePath, rollingInterval: RollingInterval.Day,
                flushToDiskInterval: TimeSpan.FromSeconds(1))
            .CreateLogger();

        // Honour the pre-start logging override (BYLD_LOG=1 or a "logging.on" file) from
        // the very first line, so a launch-time crash is captured before Settings loads.
        if (AppLogging.OverrideActive()) AppLogging.SetEnabled(true);

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
            // The process is going down: give the fans back to the motherboard first,
            // or they stay pinned at the last value we wrote.
            TryRestoreFansToAuto();
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

        // Diagnostic: log each DISTINCT exception the moment it is thrown, even if it
        // is caught downstream. This surfaces the swallowed exceptions behind Joe's
        // "froze and closed itself" (no crash log = a caught exception or a hang).
        AppDomain.CurrentDomain.FirstChanceException += (s, args) => LogFirstChance(args.Exception);
    }

    /// <summary>
    /// Returns every fan the app took over to the motherboard's own control. Safe to call
    /// from a crash handler — it must never throw while the process is already failing.
    /// </summary>
    private void TryRestoreFansToAuto()
    {
        try { _host.Services.GetRequiredService<HardwareControlService>().RestoreAllFansToAuto(); }
        catch (Exception ex) { Log.Warning(ex, "[FAN] Could not restore fans to automatic control."); }
    }

    private readonly HashSet<string> _seenFirstChance = new();

    /// <summary>Logs each distinct first-chance exception once (deduped by type+message) so
    /// a pre-crash exception is captured without flooding the log on repeat throws.</summary>
    private void LogFirstChance(Exception ex)
    {
        if (ex is System.Net.Sockets.SocketException || ex is OperationCanceledException)
            return;

        string signature = ex.GetType().FullName + "|" + ex.Message;
        lock (_seenFirstChance)
        {
            if (!_seenFirstChance.Add(signature)) return;
        }

        try { Log.Warning(ex, "[FirstChance] {Type}: {Message}", ex.GetType().Name, ex.Message); }
        catch { /* diagnostics must never throw */ }
    }

    private long _lastUiBeatTicks;

    /// <summary>
    /// Detects a frozen UI thread. A background thread watches a heartbeat the UI thread
    /// updates every second; if the gap exceeds 5s the app is hung, which is logged so a
    /// freeze (which leaves no crash log) is at least recorded with its duration.
    /// </summary>
    private void StartUiWatchdog()
    {
        System.Threading.Interlocked.Exchange(ref _lastUiBeatTicks, DateTime.UtcNow.Ticks);
        var beat = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        beat.Tick += (s, e) => System.Threading.Interlocked.Exchange(ref _lastUiBeatTicks, DateTime.UtcNow.Ticks);
        beat.Start();

        var watchdog = new System.Threading.Thread(WatchdogLoop) { IsBackground = true, Name = "UiWatchdog" };
        watchdog.Start();
    }

    private void WatchdogLoop()
    {
        bool warned = false;
        while (true)
        {
            System.Threading.Thread.Sleep(2000);
            long ticks = System.Threading.Interlocked.Read(ref _lastUiBeatTicks);
            var gap = DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc);
            warned = ReportWatchdogGap(gap, warned);
        }
    }

    private static bool ReportWatchdogGap(TimeSpan gap, bool warned)
    {
        if (gap <= TimeSpan.FromSeconds(5))
            return false;

        if (!warned)
            try { Log.Warning("[Watchdog] UI thread unresponsive for {Seconds:F1}s — the app may be frozen.", gap.TotalSeconds); }
            catch { }
        return true;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        StartupTrace.BeginSession();
        StartupTrace.Write("OnStartup - begin");

        // ── Show Splash Screen immediately (plus one on the secondary display) ──
        _splash = new SplashWindow();
        SizePrimarySplashToApp(_splash);
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

        // Apply the user's saved Logging preference (the pre-start override still wins).
        try { AppLogging.SetEnabled(_host.Services.GetRequiredService<IThemeService>().CurrentTheme.LoggingEnabled || AppLogging.OverrideActive()); }
        catch { /* first run / theme unavailable: logging stays off unless overridden */ }

        // Ensure the PawnIO driver is present BEFORE the host starts so that
        // LibreHardwareMonitor can read CPU temperature/clock (MSR) and the
        // motherboard Super I/O (case fans, VRM/chipset temps) on first run.
        // Since LHM 0.9.5-pre454 those reads go through PawnIO, not WinRing0.
        var startupLogger = _host.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<App>>();
        SetSplashStatus("Checking hardware driver...");
        try
        {
            PawnIoDriverService.EnsureAvailable(startupLogger);
        }
        catch (Exception kernelEx)
        {
            startupLogger.LogWarning(kernelEx, "Skipping PawnIO driver check because the process lacks elevation.");
        }

        // ── License Verification is now securely handled within MainWindow ──
        startupLogger.LogInformation("[Startup] Passing execution to MainWindow for initialization...");

        SetSplashStatus("Starting hardware monitoring...");
        StartupTrace.Write("host StartAsync - begin");
        await _host.StartAsync();
        StartupTrace.Write("host StartAsync - done");

        DisplayDiagnosticLogger.LogDisplays("STARTUP");
        StartupTrace.Write("display snapshot - done");

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
        StartupTrace.Write("notify icon - done; constructing MainWindow");
        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        StartupTrace.Write("MainWindow constructed");

        // ── Keep MainWindow invisible until hardware data is ready ──
        // This prevents the user from seeing empty gauges after the splash closes.
        mainWindow.Opacity = 0;
        mainWindow.Show();
        StartupTrace.Write("MainWindow shown");

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

        StartUiWatchdog(); // monitor for UI-thread freezes now that startup is done

        StartupTrace.Write("OnStartup - complete");
        base.OnStartup(e);
    }

    /// <summary>
    /// Sizes the primary-monitor splash to the same footprint the main window uses on a
    /// single/primary display (Theme.WindowWidth x WindowHeight), so the loading screen
    /// matches the app that replaces it. CenterScreen keeps it centred. The 7" secondary
    /// splash is handled separately (it fills that display).
    /// </summary>
    private void SizePrimarySplashToApp(SplashWindow splash)
    {
        try
        {
            var theme = _host.Services.GetRequiredService<IThemeService>().CurrentTheme;
            if (theme.WindowWidth > 0) splash.Width = theme.WindowWidth;
            if (theme.WindowHeight > 0) splash.Height = theme.WindowHeight;
            splash.ScaleBrandingToWindow(0.55, 0.35); // balanced against the footprint, still crisp
        }
        catch
        {
            // Keep the default splash size if the theme is unavailable this early in startup.
        }
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

        // Hand every fan we drove back to the motherboard BEFORE anything else. A control
        // left in software mode stays pinned at the last written value, so exiting used to
        // leave the client's fans stopped at 30%.
        TryRestoreFansToAuto();

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
