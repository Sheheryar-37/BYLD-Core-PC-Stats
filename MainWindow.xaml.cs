using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PcStatsMonitor.ViewModels;
using PcStatsMonitor.Services;
using PcStatsMonitor.Models;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace PcStatsMonitor;

public partial class MainWindow : Window
{
    private readonly ILogger<MainWindow> _logger;
    private readonly IThemeService _themeService;
    private readonly DispatcherTimer _transitionTimer;
    private int _currentScreenIndex = 0;
    private int _previousScreenIndex = -1;
    public PluginManager PluginManager => _pluginManager;
    private PluginManager _pluginManager;
    private MouseHookService _mouseHook;
    private readonly Services.HardwareControlService _hwControl;

    public MainWindow(MainViewModel viewModel, IThemeService themeService,
        Services.HardwareControlService hwControl, Microsoft.Extensions.Logging.ILogger<MainWindow> logger)
    {
        InitializeComponent();
        DataContext = viewModel;
        _themeService = themeService;
        _hwControl = hwControl;
        _logger = logger;

        // Clock and weather are configured by code when the rotation lands on
        // them — without this, a theme change while one is on screen would not
        // show until the next rotation cycle (client round 7, item 2).
        _themeService.ThemeChanged += (s, cfg) =>
            Dispatcher.Invoke(() => OnThemeServiceChanged(cfg));
        // Never persist a transient per-screen colour swap: put the user's real
        // colours back right before the theme file is written. The subsequent
        // ThemeChanged re-applies the visible screen's override.
        _themeService.ThemeSaving += (s, e) =>
            Dispatcher.Invoke(() => RestoreBaseColorsIfOverridden(_themeService.CurrentTheme));
        
        _mouseHook = new MouseHookService();

        LicenseService licenseSvc = new LicenseService();
        if (!licenseSvc.CheckLicense(out string errorMessage))
        {
            UnlicensedGrid.Visibility = Visibility.Visible;
            TxtLicenseError.Text = $"{errorMessage}\n\nYour Machine ID (Hardware Signature):\n{licenseSvc.GetMachineId()}";
            
            // If license is invalid, halt rendering and transitions
            _transitionTimer?.Stop();
            GaugesContainer.Visibility = Visibility.Collapsed;
            SsdScreen.Visibility = Visibility.Collapsed;
            ClockScreen.Visibility = Visibility.Collapsed;
            WeatherScreenArea.Visibility = Visibility.Collapsed;
            WeatherGalleryArea.Visibility = Visibility.Collapsed;
            FansScreenArea.Visibility = Visibility.Collapsed;
            RgbScreenArea.Visibility = Visibility.Collapsed;
            PluginScreen.Visibility = Visibility.Collapsed;
        }

        // Allow moving the borderless window by clicking anywhere
        MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        
        // Keyboard Shortcuts: Ctrl+M (Cycle Monitor), F11 (Toggle Full-Screen)
        KeyDown += (s, e) =>
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.M)
            {
                CycleMonitor();
            }
            else if (e.Key == Key.F11)
            {
                ToggleFullScreen();
            }
        };

        // Handle the infinite loop transitions
        _transitionTimer = new DispatcherTimer();
        _transitionTimer.Tick += TransitionTimer_Tick;

        // Listen for theme config changes to adjust the timer speed or start/stop
        viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(viewModel.Theme))
            {
                _transitionTimer.Interval = TimeSpan.FromSeconds(viewModel.Theme.TransitionDelaySeconds);
                _mouseHook.IsEnabled = viewModel.Theme.DisableSecondaryScreenDragging;
                
                if (viewModel.Theme.DisplayMode == DisplayMode.Auto)
                {
                    if (!_transitionTimer.IsEnabled) _transitionTimer.Start();
                }
                else
                {
                    _transitionTimer.Stop();
                }
                
                EvaluateScreenVisibility(animate: false);
            }
        };

        // Start initial timer if Auto mode is enabled
        _transitionTimer.Interval = TimeSpan.FromSeconds(viewModel.Theme.TransitionDelaySeconds);
        _mouseHook.IsEnabled = viewModel.Theme.DisableSecondaryScreenDragging;
        if (viewModel.Theme.DisplayMode == DisplayMode.Auto) _transitionTimer.Start();
        
        _currentScreenIndex = 0;
        EvaluateScreenVisibility(animate: false);
        
        // Initialize Plugins
        _pluginManager = new PluginManager();
        _pluginManager.LoadPlugins();
        foreach(var plugin in _pluginManager.LoadedPlugins)
        {
            try
            {
                plugin.Initialize(_transitionTimer);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error initializing plugin {n}", plugin.Name);
            }
        }
        
        viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(viewModel.Metrics))
            {
                if (_pluginManager?.LoadedPlugins == null) return;
                
                foreach(var plugin in _pluginManager.LoadedPlugins)
                {
                    try 
                    { 
                        plugin.Update(viewModel.Metrics); 
                    } 
                    catch (Exception) { /* Ignored inline update errors */ }
                }
            }
        };

        Loaded += (s, e) => SnapToInternalMonitor();
    }

    protected override void OnClosed(EventArgs e)
    {
        _mouseHook?.Dispose();
        base.OnClosed(e);
    }

    private void CycleMonitor()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        if (screens.Length <= 1) return;

        // Find current screen index
        var currentScreenBounds = new System.Drawing.Rectangle((int)Left, (int)Top, (int)Width, (int)Height);
        int currentIndex = 0;
        for (int i = 0; i < screens.Length; i++)
        {
            if (screens[i].Bounds.IntersectsWith(currentScreenBounds))
            {
                currentIndex = i;
                break;
            }
        }

        // Cycle to next
        int nextIndex = (currentIndex + 1) % screens.Length;
        _logger.LogInformation("[Display] CycleMonitor requested. Moving from screen {i} to screen {n}.", currentIndex, nextIndex);
        _themeService.CurrentTheme.TargetMonitorIndex = nextIndex;
        _themeService.SaveTheme(true);
        SnapToInternalMonitor();
    }

    private void ToggleFullScreen()
    {
        if (this.WindowState == WindowState.Maximized)
        {
            this.WindowState = WindowState.Normal;
            this.WindowStyle = WindowStyle.None;
            this.Topmost = true;
        }
        else
        {
            this.WindowState = WindowState.Maximized;
            this.WindowStyle = WindowStyle.None;
            this.Topmost = true;
        }
    }

    private void SnapToInternalMonitor()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        _logger.LogInformation("[Display] SnapToInternalMonitor triggered. Detected {c} total screens.", screens.Length);
        
        if (screens.Length == 0) return;

        for (int i = 0; i < screens.Length; i++)
        {
            var s = screens[i];
            _logger.LogInformation("[Display] Screen #{i}: {d} | Bounds: {w}x{h} at ({l},{t}) | Primary: {p}", 
                i, s.DeviceName, s.Bounds.Width, s.Bounds.Height, s.Bounds.Left, s.Bounds.Top, s.Primary);
        }

        System.Windows.Forms.Screen targetScreen = screens[0];
        var vm = DataContext as MainViewModel;
        bool isAutoMode = vm?.Theme?.DisplayMode == DisplayMode.Auto;
        int savedPref = _themeService.CurrentTheme.TargetMonitorIndex;

        _logger.LogInformation("[Display] Config => DisplayMode: {m}, SavedTargetIndex: {i}", isAutoMode ? "Auto" : "Manual", savedPref);

        bool hasValidUserPref = savedPref >= 0 && savedPref < screens.Length;

        if (screens.Length == 1)
        {
            targetScreen = screens[0];
            _logger.LogInformation("[Display] Only 1 screen detected. Selecting Primary Screen.");
        }
        else
        {
            _logger.LogInformation("[Display] Multiple screens detected. ALWAYS Commencing Auto-Detection for the Case Screen on startup.");
            
            var tinyScreen = screens.FirstOrDefault(s => !s.Primary && s.Bounds.Width <= 1280 && s.Bounds.Height <= 800);
            var verticalScreen = screens.FirstOrDefault(s => s.Bounds.Height > s.Bounds.Width);
            
            if (tinyScreen != null) 
            {
                targetScreen = tinyScreen;
                _logger.LogInformation("[Display] Selected Tiny Screen ({w}x{h}) as target.", targetScreen.Bounds.Width, targetScreen.Bounds.Height);
            }
            else if (verticalScreen != null) 
            {
                targetScreen = verticalScreen;
                _logger.LogInformation("[Display] Selected Vertical Screen ({w}x{h}) as target.", targetScreen.Bounds.Width, targetScreen.Bounds.Height);
            }
            else if (hasValidUserPref) 
            {
                targetScreen = screens[savedPref];
                _logger.LogInformation("[Display] Selected saved preference Screen #{s} ({n}) as target.", savedPref, targetScreen.DeviceName);
            }
            else 
            {
                targetScreen = screens.FirstOrDefault(s => !s.Primary) ?? screens[0];
                _logger.LogInformation("[Display] Selected generic Non-Primary Screen ({n}) as target.", targetScreen.DeviceName);
            }
        }

        if (targetScreen != null)
        {
            // Configure MouseHookService blocked screen bounds
            if (_mouseHook != null)
            {
                _mouseHook.BlockedScreenBounds = targetScreen.Bounds;
            }

            // ── DPI-aware coordinate conversion ──────────────────────────────────
            // System.Windows.Forms.Screen always returns physical pixel coordinates.
            // WPF uses logical (device-independent) pixels. On a 4K screen at 200%
            // DPI scaling, every physical pixel coordinate must be divided by 2.
            // We read the current DPI from the WPF presentation source.
            var source = PresentationSource.FromVisual(this);
            double dpiScaleX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double dpiScaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
            _logger.LogInformation("[Display] DPI scale detected: {sx}x, {sy}x", dpiScaleX, dpiScaleY);

            this.WindowState = WindowState.Normal;
            
            if (!targetScreen.Primary)
            {
                // Convert physical screen coordinates to WPF logical coordinates
                double logicalLeft   = targetScreen.WorkingArea.Left   / dpiScaleX;
                double logicalTop    = targetScreen.WorkingArea.Top    / dpiScaleY;
                double logicalWidth  = targetScreen.WorkingArea.Width  / dpiScaleX;
                double logicalHeight = targetScreen.WorkingArea.Height / dpiScaleY;

                this.Left   = logicalLeft;
                this.Top    = logicalTop;
                this.Width  = logicalWidth;
                this.Height = logicalHeight;
                this.WindowState = WindowState.Maximized;
                _logger.LogInformation(
                    "[Display] Moving app to Secondary Screen '{n}' | Physical=({pl},{pt}) Logical=({ll},{lt}) | DPI={sx}x", 
                    targetScreen.DeviceName, targetScreen.WorkingArea.Left, targetScreen.WorkingArea.Top,
                    logicalLeft, logicalTop, dpiScaleX);
            }
            else
            {
                vm = DataContext as MainViewModel;
                this.Width  = (vm != null) ? vm.Theme.WindowWidth  : 480;
                this.Height = (vm != null) ? vm.Theme.WindowHeight : 854;
                
                double areaW = targetScreen.WorkingArea.Width  / dpiScaleX;
                double areaH = targetScreen.WorkingArea.Height / dpiScaleY;
                double areaL = targetScreen.WorkingArea.Left   / dpiScaleX;
                double areaT = targetScreen.WorkingArea.Top    / dpiScaleY;

                if (screens.Length == 1)
                {
                    this.Left = areaL + (areaW - this.Width)  / 2;
                    this.Top  = areaT + (areaH - this.Height) / 2;
                    _logger.LogInformation("[Display] Moving app to center of single Primary '{n}' | Left={l}, Top={t}, W/H={w}x{h}", 
                        targetScreen.DeviceName, this.Left, this.Top, this.Width, this.Height);
                }
                else
                {
                    this.Left = areaL + 50;
                    this.Top  = areaT + 50;
                    _logger.LogInformation("[Display] Moving app to offset of Primary '{n}' | Left={l}, Top={t}, W/H={w}x{h}", 
                        targetScreen.DeviceName, this.Left, this.Top, this.Width, this.Height);
                }
            }
        }
    }

    private void EvaluateScreenVisibility(bool animate = true)
    {
        if (UnlicensedGrid.Visibility == Visibility.Visible) return;
        
        var vm = DataContext as MainViewModel;
        if (vm == null) return;

        var config = vm.Theme;
        if (config == null) return;

        // Ensure ScreenRotationOrder is initialized properly
        if (config.ScreenRotationOrder == null || config.ScreenRotationOrder.Count == 0)
        {
            config.ScreenRotationOrder = new List<string> { "Gauges", "Storage", "Clock", "Weather" };
        }

        // Ensure built-in screens (Clock, Weather) are in the rotation order if enabled
        if (config.ShowClockScreen && !config.ScreenRotationOrder.Contains("Clock"))
            config.ScreenRotationOrder.Add("Clock");
        
        if (config.ShowWeatherScreen && !config.ScreenRotationOrder.Contains("Weather"))
            config.ScreenRotationOrder.Add("Weather");

        if (config.Weather.ShowWeatherGallery && !config.ScreenRotationOrder.Contains("Gallery"))
            config.ScreenRotationOrder.Add("Gallery");

        if (config.ShowFansScreen && !config.ScreenRotationOrder.Contains("Fans"))
            config.ScreenRotationOrder.Add("Fans");

        if (config.ShowRgbScreen && !config.ScreenRotationOrder.Contains("RGB"))
            config.ScreenRotationOrder.Add("RGB");

        // Synchronize missing enabled plugins into the rotation order
        if (config.EnabledPlugins != null)
        {
            foreach (var p in config.EnabledPlugins)
                if (!config.ScreenRotationOrder.Contains(p))
                    config.ScreenRotationOrder.Add(p);
        }

        DisplayMode mode = config.DisplayMode; // We can still use this for manual overrides if needed

        // If in Auto, resolve _currentScreenIndex to a screen name
        string currentScreenName = "Gauges";
        if (_currentScreenIndex >= 0 && _currentScreenIndex < config.ScreenRotationOrder.Count)
        {
            currentScreenName = config.ScreenRotationOrder[_currentScreenIndex];
        }

        // Map name to UI Elements
        UIElement targetScreen = GaugesContainer;
        bool currentScreenValid = false;

        if (currentScreenName == "Gauges" && config.ShowGaugesScreen)
        {
            targetScreen = GaugesContainer;
            currentScreenValid = true;
        }
        else if (currentScreenName == "Storage" && config.ShowStorageScreen)
        {
            targetScreen = SsdScreen;
            currentScreenValid = true;
        }
        else if (currentScreenName == "Clock" && config.ShowClockScreen)
        {
            BuiltInClock.ApplyConfig(config.Clock, config);
            targetScreen = ClockScreen;
            currentScreenValid = true;
        }
        else if (currentScreenName == "Weather" && config.ShowWeatherScreen)
        {
            WeatherCtrl.ApplyConfig(config.Weather ?? new WeatherConfig(), config);
            targetScreen = WeatherScreenArea;
            currentScreenValid = true;
        }
        else if (currentScreenName == "Gallery" && config.Weather.ShowWeatherGallery)
        {
            targetScreen = WeatherGalleryArea;
            currentScreenValid = true;
        }
        else if (currentScreenName == "Fans" && config.ShowFansScreen)
        {
            targetScreen = FansScreenArea;
            currentScreenValid = true;
        }
        else if (currentScreenName == "RGB" && config.ShowRgbScreen)
        {
            targetScreen = RgbScreenArea;
            currentScreenValid = true;
        }
        else
        {
            // Check plugins
            var plugin = _pluginManager?.LoadedPlugins?.FirstOrDefault(p => p.Name == currentScreenName);
            if (plugin != null && config.EnabledPlugins != null && config.EnabledPlugins.Contains(plugin.Name))
            {
                try
                {
                    PluginHost.Content = plugin.GetUI();
                    targetScreen = PluginScreen;
                    currentScreenValid = true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error displaying plugin: {ex.Message}");
                }
            }
        }

        // If current screen is hidden or invalid, try to find the FIRST valid one in the order
        if (!currentScreenValid)
        {
            for (int i = 0; i < config.ScreenRotationOrder.Count; i++)
            {
                string name = config.ScreenRotationOrder[i];
                if (name == "Gauges" && config.ShowGaugesScreen) { targetScreen = GaugesContainer; _currentScreenIndex = i; currentScreenValid = true; break; }
                if (name == "Storage" && config.ShowStorageScreen) { targetScreen = SsdScreen; _currentScreenIndex = i; currentScreenValid = true; break; }
                if (name == "Clock" && config.ShowClockScreen) 
                { 
                    BuiltInClock.ApplyConfig(config.Clock, config);
                    targetScreen = ClockScreen; _currentScreenIndex = i; currentScreenValid = true; break; 
                }
                if (name == "Weather" && config.ShowWeatherScreen)
                {
                    WeatherCtrl.ApplyConfig(config.Weather ?? new WeatherConfig(), config);
                    targetScreen = WeatherScreenArea; _currentScreenIndex = i; currentScreenValid = true; break;
                }
                if (name == "Gallery" && config.Weather.ShowWeatherGallery)
                {
                    targetScreen = WeatherGalleryArea; _currentScreenIndex = i; currentScreenValid = true; break;
                }
                if (name == "Fans" && config.ShowFansScreen)
                {
                    targetScreen = FansScreenArea; _currentScreenIndex = i; currentScreenValid = true; break;
                }
                if (name == "RGB" && config.ShowRgbScreen)
                {
                    targetScreen = RgbScreenArea; _currentScreenIndex = i; currentScreenValid = true; break;
                }

                var p = _pluginManager?.LoadedPlugins?.FirstOrDefault(pl => pl.Name == name);
                if (p != null && config.EnabledPlugins.Contains(p.Name))
                {
                    try { PluginHost.Content = p.GetUI(); targetScreen = PluginScreen; _currentScreenIndex = i; currentScreenValid = true; break; } catch { }
                }
            }

            // Absolute fallback if everything is somehow broken
            if (!currentScreenValid) targetScreen = GaugesContainer;
        }

        // Per-screen theme override (client request): swap the display colours
        // for the screen we are about to show, before the transition animates.
        ApplyScreenTheme(targetScreen, config);

        UIElement[] allScreens = { GaugesContainer, SsdScreen, PluginScreen, ClockScreen, WeatherScreenArea, WeatherGalleryArea, FansScreenArea, RgbScreenArea };

        if (animate)
        {
            var duration = new Duration(TimeSpan.FromMilliseconds(700));
            var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };

            foreach (var screen in allScreens)
            {
                var transform = (screen.RenderTransform as TransformGroup)?.Children.OfType<TranslateTransform>().FirstOrDefault() 
                                ?? (screen.RenderTransform as TranslateTransform);

                if (screen == targetScreen)
                {
                    screen.Visibility = Visibility.Visible;
                    screen.IsHitTestVisible = true;

                    // Animate Opacity
                    var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(1.0, duration) { EasingFunction = ease };
                    screen.BeginAnimation(UIElement.OpacityProperty, fadeIn);

                    // Animate Slide In from Right
                    if (transform != null)
                    {
                        var slideIn = new System.Windows.Media.Animation.DoubleAnimation(480, 0, duration) { EasingFunction = ease };
                        transform.BeginAnimation(TranslateTransform.XProperty, slideIn);
                    }
                }
                else if (screen.Visibility == Visibility.Visible && screen.Opacity > 0)
                {
                    screen.IsHitTestVisible = false;

                    // Animate Opacity Out — then COLLAPSE (not Hidden): Hidden keeps
                    // the screen's bindings, ticking clock, gauge animations and glow
                    // blur effects running every frame off-screen, which was burning
                    // CPU continuously (client round 9). Collapsed stops all of it.
                    var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(0.0, duration) { EasingFunction = ease };
                    fadeOut.Completed += (s, e) => { if (screen != targetScreen) screen.Visibility = Visibility.Collapsed; };
                    screen.BeginAnimation(UIElement.OpacityProperty, fadeOut);

                    // Animate Slide Out to Left
                    if (transform != null)
                    {
                        var slideOut = new System.Windows.Media.Animation.DoubleAnimation(0, -480, duration) { EasingFunction = ease };
                        transform.BeginAnimation(TranslateTransform.XProperty, slideOut);
                    }
                }
            }
        }
        else
        {
            foreach (var screen in allScreens)
            {
                screen.BeginAnimation(UIElement.OpacityProperty, null);
                var transform = (screen.RenderTransform as TransformGroup)?.Children.OfType<TranslateTransform>().FirstOrDefault() 
                                ?? (screen.RenderTransform as TranslateTransform);
                if (transform != null) transform.BeginAnimation(TranslateTransform.XProperty, null);

                bool isActive = (screen == targetScreen);
                screen.Opacity = isActive ? 1.0 : 0.0;
                if (transform != null) transform.X = 0;
                screen.IsHitTestVisible = isActive;
                // Collapsed (not Hidden) so inactive screens stop rendering and
                // stop running their animations/effects entirely.
                screen.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    private void TransitionTimer_Tick(object? sender, EventArgs e)
    {
        if (UnlicensedGrid.Visibility == Visibility.Visible) return;
        
        var vm = DataContext as MainViewModel;
        if (vm == null || vm.Theme == null) return;

        var config = vm.Theme;
        if (config.DisplayMode != DisplayMode.Auto) return;

        int totalSlots = config.ScreenRotationOrder?.Count ?? 0;
        if (totalSlots == 0) return;

        // Move to next screen in order
        _previousScreenIndex = _currentScreenIndex;
        _currentScreenIndex = (_currentScreenIndex + 1) % totalSlots;

        // Safety: verify the new screen is actually enabled
        for (int i = 0; i < totalSlots; i++)
        {
            string name = config.ScreenRotationOrder[_currentScreenIndex];
            bool isValid = false;
            if (name == "Gauges" && config.ShowGaugesScreen) isValid = true;
            else if (name == "Storage" && config.ShowStorageScreen) isValid = true;
            else if (name == "Clock" && config.ShowClockScreen) isValid = true;
            else if (name == "Weather" && config.ShowWeatherScreen) isValid = true;
            else if (name == "Gallery" && config.Weather?.ShowWeatherGallery == true) isValid = true;
            else if (name == "Fans" && config.ShowFansScreen) isValid = true;
            else if (name == "RGB" && config.ShowRgbScreen) isValid = true;
            else if (config.EnabledPlugins != null && config.EnabledPlugins.Contains(name)) isValid = true;

            if (isValid) break;

            _currentScreenIndex = (_currentScreenIndex + 1) % totalSlots;
        }

        // ONLY animate if the target screen is actually different from the current one
        if (_currentScreenIndex != _previousScreenIndex)
        {
            EvaluateScreenVisibility(animate: true);
        }
    }

    private void DualCircularGauge_Loaded(object sender, RoutedEventArgs e)
    {
        // Added to resolve the XamlParseException for the 'Loaded' event in XAML.
        // You can add your custom loaded animations or logic here!
    }

    // ── Per-screen theme overrides ──────────────────────────────────────────
    // Only one rotating screen is visible at a time, so an override swaps the
    // display colours IN MEMORY as the rotation transitions. The user's saved
    // colours are snapshotted first and restored on screens without overrides,
    // and nothing here is ever written to disk.

    /// <summary>The user's saved colours, captured before the first in-memory override.</summary>
    private sealed record ScreenColorBase(
        string Background, string Foreground, string Track,
        string ClockFace, string ClockHour, string ClockMinute, string ClockMarker,
        string ClockDigital, string ClockDate, string WeatherTheme);

    private ScreenColorBase? _baseColors;
    private ScreenColorBase? _transientColors;
    private bool _screenColorsOverridden;
    private UIElement? _activeThemedScreen;

    /// <summary>Handles theme changes coming from Settings: forget the old colour
    /// snapshot (the saved colours may have changed) and re-theme the visible screen.</summary>
    private void OnThemeServiceChanged(ThemeConfig config)
    {
        try
        {
            ApplyThemeChange(config);
        }
        catch (Exception ex)
        {
            // A throw mid-switch leaves the screen half-themed (client round 12: light
            // stuck / widgets "merged"). Log it rather than letting it bubble or freeze.
            Serilog.Log.Error(ex, "[Theme] Failed to apply theme change — screen may be partially themed.");
        }
    }

    private void ApplyThemeChange(ThemeConfig config)
    {
        _baseColors = null;
        _transientColors = null;
        _screenColorsOverridden = false;

        if (_activeThemedScreen != null)
            ApplyScreenTheme(_activeThemedScreen, config);
        else
            RefreshCodeConfiguredScreens(config);
    }

    /// <summary>Applies the per-screen theme override for the screen about to show.
    /// Screens without an explicit override always render the user's saved colours.</summary>
    private void ApplyScreenTheme(UIElement targetScreen, ThemeConfig config)
    {
        _activeThemedScreen = targetScreen;
        string? key = ScreenKeyFor(targetScreen);

        if (key == null || !config.HasScreenThemeOverride(key))
        {
            RestoreBaseColorsIfOverridden(config);
            return;
        }

        bool light = config.IsScreenThemeLight(key);
        _baseColors ??= CaptureBaseColors(config);
        config.ApplyWidgetColorPreset(light);
        _transientColors = CaptureBaseColors(config);
        _screenColorsOverridden = true;
        RefreshThemeVisuals(config, light);
    }

    private void RestoreBaseColorsIfOverridden(ThemeConfig config)
    {
        if (!_screenColorsOverridden || _baseColors == null)
        {
            RefreshCodeConfiguredScreens(config);
            return;
        }

        // Settings may have just written NEW colours (e.g. the widget-theme
        // preset when switching back to dark). If the config no longer matches
        // the transient values this override wrote, those new colours are the
        // user's intent — accept them as the new base instead of clobbering
        // them with the stale snapshot (client round 8, item 6).
        if (_transientColors != null && CaptureBaseColors(config) != _transientColors)
        {
            _baseColors = null;
            _transientColors = null;
            _screenColorsOverridden = false;
            RefreshCodeConfiguredScreens(config);
            return;
        }

        var b = _baseColors;
        config.BackgroundColor      = b.Background;
        config.ForegroundColor      = b.Foreground;
        config.TrackColor           = b.Track;
        config.Clock.ClockFaceColor  = b.ClockFace;
        config.Clock.HourHandColor   = b.ClockHour;
        config.Clock.MinuteHandColor = b.ClockMinute;
        config.Clock.MarkerColor     = b.ClockMarker;
        config.Clock.DigitalColor    = b.ClockDigital;
        config.Clock.DateColor       = b.ClockDate;
        config.Weather.WeatherTheme  = b.WeatherTheme;
        _screenColorsOverridden = false;
        _transientColors = null;
        RefreshThemeVisuals(config, logoLightOverride: null);
    }

    private static ScreenColorBase CaptureBaseColors(ThemeConfig c) => new(
        c.BackgroundColor, c.ForegroundColor, c.TrackColor,
        c.Clock.ClockFaceColor, c.Clock.HourHandColor, c.Clock.MinuteHandColor,
        c.Clock.MarkerColor, c.Clock.DigitalColor, c.Clock.DateColor,
        c.Weather?.WeatherTheme ?? "Dark");

    /// <summary>Maps a rotating-screen element to its ScreenThemes key. Null for
    /// screens without per-screen theming (plugins, internal gallery).</summary>
    private string? ScreenKeyFor(UIElement screen)
    {
        if (screen == GaugesContainer) return "Gauges";
        if (screen == SsdScreen) return "Storage";
        if (screen == ClockScreen) return "Clock";
        if (screen == WeatherScreenArea) return "Weather";
        if (screen == FansScreenArea) return "Fans";
        if (screen == RgbScreenArea) return "RGB";
        return null;
    }

    private void RefreshThemeVisuals(ThemeConfig config, bool? logoLightOverride)
    {
        (DataContext as MainViewModel)?.NotifyThemeRefreshed(logoLightOverride);
        RefreshCodeConfiguredScreens(config);
    }

    /// <summary>Re-applies the current config to the code-configured screens so
    /// theme changes are visible immediately, not on the next rotation.</summary>
    private void RefreshCodeConfiguredScreens(ThemeConfig config)
    {
        try
        {
            BuiltInClock.ApplyConfig(config.Clock, config);
            WeatherCtrl.ApplyConfig(config.Weather ?? new WeatherConfig(), config);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[Theme] Live screen refresh failed.");
        }
    }

    private void BtnOpenSettings_Click(object sender, RoutedEventArgs e)
    {
        // Must disable Topmost so SettingsWindow can appear above it
        this.Topmost = false;
        
        var settingsWindow = new SettingsWindow(_themeService, _hwControl, _pluginManager);
        settingsWindow.Owner = this;
        settingsWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        
        // Instruct settings window to open to Registration tab directly
        settingsWindow.JumpToSettingsTab("Registration");
        settingsWindow.ShowDialog();
        
        // After Settings closes, check if license became valid
        LicenseService licenseSvc = new LicenseService();
        if (licenseSvc.CheckLicense(out string errorMessage))
        {
            UnlicensedGrid.Visibility = Visibility.Collapsed;
            EvaluateScreenVisibility(animate: false);
            var vm = DataContext as MainViewModel;
            if (vm?.Theme?.DisplayMode == DisplayMode.Auto) _transitionTimer?.Start();
        }
        else
        {
            TxtLicenseError.Text = $"{errorMessage}\n\nYour Machine ID (Hardware Signature):\n{licenseSvc.GetMachineId()}";
            
            GaugesContainer.Visibility = Visibility.Collapsed;
            SsdScreen.Visibility = Visibility.Collapsed;
            ClockScreen.Visibility = Visibility.Collapsed;
            WeatherScreenArea.Visibility = Visibility.Collapsed;
            WeatherGalleryArea.Visibility = Visibility.Collapsed;
            FansScreenArea.Visibility = Visibility.Collapsed;
            RgbScreenArea.Visibility = Visibility.Collapsed;
            PluginScreen.Visibility = Visibility.Collapsed;
        }
        
        this.Topmost = true;
    }
}

public class DictionaryValueConverter : System.Windows.Data.IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        double defaultVal = parameter != null && double.TryParse(parameter.ToString(), out double p) ? p : 0.0;
        
        if (values.Length >= 2 && values[0] is System.Collections.Generic.Dictionary<string, double> dict && values[1] is string key)
        {
            if (dict.TryGetValue(key, out double val)) return val;
        }
        return defaultVal;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
