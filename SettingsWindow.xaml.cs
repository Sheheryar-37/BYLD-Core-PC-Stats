using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Microsoft.Win32.TaskScheduler;
using PcStatsMonitor.Services;
using System.Windows.Input;
using System.Windows.Media;
using System.Collections.ObjectModel;
using System.Linq;
using PcStatsMonitor.Models;

namespace PcStatsMonitor;

public partial class SettingsWindow : Window
{
    private readonly IThemeService _themeService;
    private readonly PluginManager? _pluginManager;
    private bool _isInitializing = true;
    public ObservableCollection<string> ActiveMonitors { get; set; } = new();
    public ObservableCollection<string> ScreenRotationList { get; set; } = new();
    public ObservableCollection<PluginToggle> PluginSettings { get; set; } = new();
    
    private readonly PcStatsMonitor.Services.HardwareControlService _hwControl;
    
    public PcStatsMonitor.ViewModels.FanControlViewModel FanViewModel { get; }
    public PcStatsMonitor.ViewModels.RgbControlViewModel RgbViewModel { get; }

    public class PluginToggle : System.ComponentModel.INotifyPropertyChanged
    {
        private string _name = "";
        private bool _isEnabled;

        public string Name 
        { 
            get => _name; 
            set { _name = value; OnPropertyChanged(); } 
        }
        public bool IsEnabled 
        { 
            get => _isEnabled; 
            set { _isEnabled = value; OnPropertyChanged(); } 
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
        }
    }

    public SettingsWindow(IThemeService themeService, PcStatsMonitor.Services.HardwareControlService hwControl,
        PluginManager? pluginManager = null)
    {
        InitializeComponent();
        _themeService = themeService;
        _pluginManager = pluginManager;

        // Shared process-wide instance — never create a second HardwareControlService:
        // LibreHardwareMonitor's Ring0 state is process-global and a second Computer
        // (or disposing one) corrupts the others.
        _hwControl = hwControl;
        FanViewModel = new PcStatsMonitor.ViewModels.FanControlViewModel(_hwControl);
        RgbViewModel = new PcStatsMonitor.ViewModels.RgbControlViewModel(_hwControl);
        

        LstOrder.ItemsSource = ActiveMonitors;
        LstScreenOrder.ItemsSource = ScreenRotationList;
        ItemsPlugins.ItemsSource = PluginSettings;

        var cities = new string[] { 
            "London, UK", "New York, US", "Tokyo, JP", "Paris, FR", "Berlin, DE", 
            "Sydney, AU", "Dubai, AE", "Los Angeles, US", "Toronto, CA", "Singapore, SG", 
            "Karachi, PK", "Mumbai, IN", "Beijing, CN", "Moscow, RU", "Seoul, KR"
        };
        TxtWeatherCity.ItemsSource = cities;
        // if (TxtWeatherCityLayout != null) TxtWeatherCityLayout.ItemsSource = cities;

        LoadCurrentSettings();
        CenterOnPrimaryDisplay();
    }

    /// <summary>
    /// Stops the fan/RGB timers and releases the hardware handle so closed
    /// Settings windows don't keep polling sensors in the background.
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        FanViewModel.StopPolling();
        RgbViewModel.StopAutoRefresh();
        // Do NOT dispose _hwControl: it is the process-wide shared instance and the
        // main window / 7" display keep polling it after Settings closes.
        base.OnClosed(e);
    }

    /// <summary>
    /// Positions the window in the center of the primary display's working area.
    /// CenterScreen would otherwise open Settings on the small secondary display
    /// when the main window is active there.
    /// </summary>
    private void CenterOnPrimaryDisplay()
    {
        var work = SystemParameters.WorkArea; // primary display, in DIPs
        Left = work.Left + (work.Width - Width) / 2;
        Top  = work.Top + (work.Height - Height) / 2;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    /// <summary>
    /// Persists the Dark/Light and Liquid Glass toggles and re-skins the window.
    /// </summary>
    private void AppearanceToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;

        var theme = _themeService.CurrentTheme;
        theme.LiquidGlassEnabled = TglLiquidGlass.IsChecked == true;
        _themeService.SaveTheme();
        ApplyUiTheme();
    }

    /// <summary>Persists the settings-window theme (Dark / Light / System) and re-skins the window.</summary>
    private void CmbUiTheme_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;

        var theme = _themeService.CurrentTheme;
        theme.UiTheme = CmbUiTheme.SelectedIndex switch
        {
            1 => "Light",
            2 => "System",
            _ => "Dark"
        };
        // Only "Follow settings theme" tracks this choice — explicit widget
        // choices (and custom Colors-tab values) must not be overwritten.
        if (string.Equals(theme.WidgetTheme, "Auto", StringComparison.OrdinalIgnoreCase))
            ApplyWidgetThemePreset(theme);
        _themeService.SaveTheme();
        ApplyUiTheme();
        LoadClockSettings(); // clock face tiles are code-built with the theme baked in
    }

    /// <summary>
    /// Persists the widget-theme choice for the 7" display and applies its
    /// colour preset (background/foreground/track + clock face colours).
    /// </summary>
    private void CmbWidgetTheme_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;

        var theme = _themeService.CurrentTheme;
        theme.WidgetTheme = CmbWidgetTheme.SelectedIndex switch
        {
            1 => "Light",
            2 => "Auto",
            3 => "System",
            _ => "Dark"
        };
        ApplyWidgetThemePreset(theme);
        _themeService.SaveTheme();
        LoadCurrentSettings(); // refresh colour swatches + clock tab to the new preset
    }

    /// <summary>
    /// Writes the widget colour preset for the effective widget theme into the
    /// config. "Auto" resolves against the settings Light Theme toggle.
    /// </summary>
    private static void ApplyWidgetThemePreset(ThemeConfig theme)
    {
        bool light = theme.IsWidgetThemeLight;
        theme.BackgroundColor = light ? "#F4F6FA" : Models.Constants.DefaultThemeBackground;
        theme.ForegroundColor = light ? "#1E293B" : Models.Constants.DefaultThemeForeground;
        theme.TrackColor      = light ? "#D9DEE7" : Models.Constants.DefaultThemeTrack;
        theme.Clock.ClockFaceColor  = light ? "#FFFFFF" : "#1A1A1A";
        theme.Clock.HourHandColor   = light ? "#1E293B" : "#FFFFFF";
        theme.Clock.MinuteHandColor = light ? "#1E293B" : "#FFFFFF";
        theme.Clock.MarkerColor     = light ? "#475569" : "#FFFFFF";
        theme.Clock.DigitalColor    = light ? "#1E293B" : "#FFFFFF";
        theme.Clock.DateColor       = light ? "#475569" : "#AAAAAA";
        // The weather screen has its own theme switch — keep it in sync.
        theme.Weather.WeatherTheme  = light ? "Light" : "Dark";
    }

    /// <summary>
    /// Applies the current UI theme (Dark/Light × Glass/Solid) to the window.
    /// Works by mutating the shared theme brushes in place, which propagates
    /// through every StaticResource reference in the XAML.
    /// </summary>
    private void ApplyUiTheme()
    {
        var theme = _themeService.CurrentTheme;
        bool light = theme.IsUiThemeLight; // resolves "System" against the Windows app theme
        bool glass = theme.LiquidGlassEnabled;

        Background = NewBrush(WindowBackgroundHex(light, glass));
        SetThemeBrush("TextBrush",          light ? "#1E293B" : "#E0E0E0");
        SetThemeBrush("HeaderTextBrush",    light ? "#88334155" : "#88FFFFFF");
        SetThemeBrush("CardBgBrush",        CardBackgroundHex(light, glass));
        SetThemeBrush("CardBorderBrush",    light ? "#26000000" : (glass ? "#1AFFFFFF" : "#FF33334A"));
        SetThemeBrush("TabSelectedBgBrush", light ? "#1A000000" : "#22FFFFFF");
        SetThemeBrush("TabTextBrush",       light ? "#FF64748B" : "#FFAAAAAA");
        SetThemeBrush("InputBgBrush",       light ? "#FFFFFFFF" : "#FF222222");
        SetThemeBrush("InputFgBrush",       light ? "#FF1E293B" : "#FFFFFFFF");
        SetThemeBrush("InputBorderBrush",   light ? "#FFC7CDD6" : "#FF444444");
        SetThemeBrush("ButtonBgBrush",      light ? "#FFE9EDF2" : "#FF2A2A2A");
        SetThemeBrush("ButtonFgBrush",      light ? "#FF1E293B" : "#FFEEEEEE");
        SetThemeBrush("ButtonBorderBrush",  light ? "#26000000" : "#33FFFFFF");
        SetThemeBrush("CheckBorderBrush",   light ? "#66000000" : "#55FFFFFF");
        SetThemeBrush("SubtleTextBrush",    light ? "#FF52616F" : "#FFCCCCCC");
        SetThemeBrush("PanelFillBrush",     light ? "#10000000" : "#11FFFFFF");
        SetThemeBrush("WindowBorderBrush",  light ? "#29000000" : "#33FFFFFF");
        SetThemeBrush("SliderTrackBrush",   light ? "#26000000" : "#33FFFFFF");
        ApplyEmbeddedViewTheme(light, glass);
    }

    /// <summary>
    /// Re-tints the Fan/RGB dashboard views. Their cards deliberately stay dark
    /// navy in light mode (matching the Fan Control reference design), but they
    /// switch from translucent glass to opaque so white card text stays readable,
    /// and page-level headings outside the cards follow the window theme.
    /// </summary>
    private void ApplyEmbeddedViewTheme(bool light, bool glass)
    {
        SetBrushIn(FanView.Resources, "CardBg",      light ? "#FFFFFFFF" : (glass ? "#0AFFFFFF" : "#FF1D1D28"));
        SetBrushIn(FanView.Resources, "CardBorder",  light ? "#26000000" : (glass ? "#1AFFFFFF" : "#FF33334A"));
        SetBrushIn(FanView.Resources, "CardText",    light ? "#FF1E293B" : "#FFFFFFFF");
        SetBrushIn(FanView.Resources, "DimText",     light ? "#FF64748B" : "#88FFFFFF");
        SetBrushIn(FanView.Resources, "CardFill",    light ? "#14000000" : "#11FFFFFF");
        SetBrushIn(FanView.Resources, "CardStroke",  light ? "#26000000" : "#22FFFFFF");
        SetBrushIn(FanView.Resources, "FanPopupBg",  light ? "#FFFFFFFF" : "#FF1E1E2E");
        SetBrushIn(FanView.Resources, "PageText",    light ? "#FF1E293B" : "#FFFFFFFF");
        SetBrushIn(FanView.Resources, "PageDimText", light ? "#FF64748B" : "#88FFFFFF");
        SetBrushIn(FanView.Resources, "WarnText",    light ? "#FFB03A44" : "#FFFFB0B0");
        SetBrushIn(RgbView.Resources, "WarnText",        light ? "#FFB03A44" : "#FFFFB0B0");
        SetBrushIn(RgbView.Resources, "RgbCardBg",       light ? "#FFFFFFFF" : "#FF0C0C1A");
        SetBrushIn(RgbView.Resources, "RgbCardBorder",   light ? "#26000000" : "#FF1A1A35");
        SetBrushIn(RgbView.Resources, "RgbCardText",     light ? "#FF1E293B" : "#FFF1F5F9");
        SetBrushIn(RgbView.Resources, "RgbDimText",      light ? "#FF64748B" : "#FF94A3B8");
        SetBrushIn(RgbView.Resources, "RgbInputBg",      light ? "#FFEFF2F7" : "#FF1A1A2E");
        SetBrushIn(RgbView.Resources, "RgbInputBorder",  light ? "#26000000" : "#FF2A2A4A");
    }

    private static string WindowBackgroundHex(bool light, bool glass)
    {
        if (light) return glass ? "#F2EEF1F6" : "#FFEEF1F6";
        return glass ? "#D9161616" : "#FF14141C";
    }

    private static string CardBackgroundHex(bool light, bool glass)
    {
        if (light) return glass ? "#B3FFFFFF" : "#FFFFFFFF";
        return glass ? "#0AFFFFFF" : "#FF1E1E28";
    }

    /// <summary>Replaces a themed brush resource so all DynamicResource references update live.</summary>
    private void SetThemeBrush(string key, string hex)
    {
        SetBrushIn(Resources, key, hex);
    }

    /// <summary>
    /// Re-tints a themed brush. Mutates the existing (unfrozen) brush object in
    /// place so EVERY live reference updates — including ComboBox popups, whose
    /// separate visual trees do not reliably pick up a replaced resource entry.
    /// Falls back to inserting a fresh brush when the key is missing or frozen.
    /// All theme brushes are referenced via DynamicResource, so they stay
    /// unfrozen and mutable.
    /// </summary>
    private static void SetBrushIn(ResourceDictionary resources, string key, string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        if (resources[key] is SolidColorBrush brush && !brush.IsFrozen)
            brush.Color = color;
        else
            resources[key] = new SolidColorBrush(color);
    }

    private static SolidColorBrush NewBrush(string hex)
    {
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }

    public void JumpToSettingsTab(string tabHeaderName)
    {
        foreach (TabItem item in MainTabControl.Items)
        {
            if (item.Header?.ToString() == tabHeaderName)
            {
                MainTabControl.SelectedItem = item;
                break;
            }
        }
    }

    private void LoadCurrentSettings()
    {
        _isInitializing = true;
        var theme = _themeService.CurrentTheme;

        // General
        ChkLaunchOnStartup.IsChecked = theme.LaunchOnStartup;
        ChkDisableSecondaryScreenDragging.IsChecked = theme.DisableSecondaryScreenDragging;

        // Appearance
        CmbUiTheme.SelectedIndex = theme.UiTheme?.ToLowerInvariant() switch
        {
            "light"  => 1,
            "system" => 2,
            _        => 0
        };
        TglLiquidGlass.IsChecked = theme.LiquidGlassEnabled;
        CmbWidgetTheme.SelectedIndex = theme.WidgetTheme?.ToLowerInvariant() switch
        {
            "light"  => 1,
            "auto"   => 2,
            "system" => 3,
            _        => 0
        };
        ApplyUiTheme();

        // Theme Colors
        BtnBgColor.Background = new BrushConverter().ConvertFromString(theme.BackgroundColor) as SolidColorBrush;
        BtnBgColor.Tag = theme.BackgroundColor;
        BtnFgColor.Background = new BrushConverter().ConvertFromString(theme.ForegroundColor) as SolidColorBrush;
        BtnFgColor.Tag = theme.ForegroundColor;
        BtnAccentColor.Background = new BrushConverter().ConvertFromString(theme.AccentColor) as SolidColorBrush;
        BtnAccentColor.Tag = theme.AccentColor;
        BtnTrackColor.Background = new BrushConverter().ConvertFromString(theme.TrackColor) as SolidColorBrush;
        BtnTrackColor.Tag = theme.TrackColor;
        BtnAlertColor.Background = new BrushConverter().ConvertFromString(theme.AlertColor) as SolidColorBrush;
        BtnAlertColor.Tag = theme.AlertColor;

        // Toggles
        ChkCpu.IsChecked = theme.IsCpuEnabled;
        ChkGpu.IsChecked = theme.IsGpuEnabled;
        ChkMemory.IsChecked = theme.IsMemoryEnabled;
        ChkMotherboard.IsChecked = theme.IsMotherboardEnabled;
        ChkNetwork.IsChecked = theme.IsNetworkEnabled;

        // Gauges screen checkboxes
        ChkGaugesScreen.IsChecked = theme.ShowGaugesScreen;
        ChkStorageScreen.IsChecked = theme.ShowStorageScreen;
        ChkClockScreen.IsChecked = theme.ShowClockScreen;
        ChkFansScreen.IsChecked = theme.ShowFansScreen;
        ChkRgbScreen.IsChecked = theme.ShowRgbScreen;
        
        // SYNC BOTH WEATHER CHECKBOXES (Layout tab and Weather tab)
        ChkWeatherScreenLayout.IsChecked = theme.ShowWeatherScreen;
        ChkShowWeather.IsChecked = theme.ShowWeatherScreen;

        // Plugins
        PluginSettings.Clear();
        if (_pluginManager != null)
        {
            foreach (var plugin in _pluginManager.LoadedPlugins)
            {
                bool isEnabled = theme.EnabledPlugins != null && theme.EnabledPlugins.Contains(plugin.Name);
                PluginSettings.Add(new PluginToggle { Name = plugin.Name, IsEnabled = isEnabled });
            }
        }
        else
        {
            // Fallback for design-time or if manager is missing
            foreach (var pName in theme.EnabledPlugins ?? new List<string>())
            {
                PluginSettings.Add(new PluginToggle { Name = pName, IsEnabled = true });
            }
        }

        // Gauges Ordering List
        ActiveMonitors.Clear();
        foreach (var m in theme.ActiveMonitorsOrder) ActiveMonitors.Add(m);
        if (ActiveMonitors.Count > 0) LstOrder.SelectedIndex = 0;

        // Global Screen Rotation Sequence
        ScreenRotationList.Clear();
        if (theme.ScreenRotationOrder == null || theme.ScreenRotationOrder.Count == 0)
        {
            // Fallback default
            theme.ScreenRotationOrder = new List<string> { "Gauges", "Storage" };
            foreach(var p in theme.EnabledPlugins) theme.ScreenRotationOrder.Add(p);
        }

        foreach (var s in theme.ScreenRotationOrder) ScreenRotationList.Add(s);

        // Images
        TxtLogo.Text = theme.LogoPath;
        TxtBgImage.Text = theme.BackgroundImagePath;
        SldOpacity.Value = theme.BackgroundOpacity;
        
        // Transition Delay
        SldInterval.Value = theme.TransitionDelaySeconds;
        ChkAutoRotate.IsChecked = (theme.DisplayMode == DisplayMode.Auto);

        // Clock Settings
        LoadClockSettings();

        // Weather Settings
        LoadWeatherSettings();
        
        // Demo Hardware Settings
        ChkDemoHardware.IsChecked = HardwareControlService.IsDemoMode;
        if (HardwareControlService.IsDemoMode)
        {
            FanViewModel.LoadFans();
        }

        LoadProfilesList();

        // License Settings
        try
        {
            var licenseSvc = new LicenseService();
            TxtMachineId.Text = licenseSvc.GetMachineId();
            bool isLicenseValid = false;

            if (System.IO.File.Exists("license.key"))
            {
                TxtLicenseKey.Text = System.IO.File.ReadAllText("license.key");
                if (licenseSvc.CheckLicense(out string errorMessage))
                {
                    TxtLicenseStatus.Text = "Status: License Activated and Valid";
                    TxtLicenseStatus.Foreground = new SolidColorBrush(Colors.MediumSeaGreen);
                    isLicenseValid = true;
                }
                else
                {
                    TxtLicenseStatus.Text = $"Status: {errorMessage}";
                    TxtLicenseStatus.Foreground = new SolidColorBrush(Color.FromRgb(230, 57, 70)); // #E63946
                }
            }

            if (!isLicenseValid)
            {
                foreach (TabItem item in MainTabControl.Items)
                {
                    if (item.Header?.ToString() != "Registration")
                    {
                        item.Visibility = Visibility.Collapsed;
                    }
                }
            }
        }
        catch { }

        _isInitializing = false;
    }

    private void LoadWeatherSettings()
    {
        var theme = _themeService.CurrentTheme;
        if (theme.Weather == null) return;
        var w = theme.Weather;

        ChkShowWeather.IsChecked = theme.ShowWeatherScreen;
        if (ChkWeatherScreenLayout != null) ChkWeatherScreenLayout.IsChecked = theme.ShowWeatherScreen;
        if (ChkWeatherGallery != null) ChkWeatherGallery.IsChecked = w.ShowWeatherGallery;

        TxtWeatherApiKey.Password = w.ApiKey;
        TxtWeatherCity.Text = w.City;

        SetComboItem(CmbWeatherUnits, w.Units == "imperial" ? "Imperial (°F)" : "Metric (°C)");
        SetComboItem(CmbWeatherTheme, w.WeatherTheme);
        
        string intervalText = w.UpdateIntervalMinutes switch
        {
            1 => "1 Minute",
            5 => "5 Minutes",
            15 => "15 Minutes",
            30 => "30 Minutes",
            60 => "1 Hour",
            _ => "15 Minutes"
        };
        SetComboItem(CmbWeatherInterval, intervalText);

        BtnWeatherGlowColor.Tag = w.GlowColor;
        try { BtnWeatherGlowColor.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(w.GlowColor ?? "#3b82f6")); } catch {}

        SetComboItem(CmbWeatherTimeFormat, w.TimeFormat);
    }

    private void ChkDemoHardware_Click(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;

        bool isDemo = ChkDemoHardware.IsChecked == true;
        PcStatsMonitor.Services.HardwareControlService.IsDemoMode = isDemo;
        
        // Offload the heavy synchronous hardware polling to a background priority dispatcher 
        // to ensure the CheckBox visually updates instantly before freezing the thread.
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            // Reload fans/RGB for the new mode (demo or live)
            FanViewModel.LoadFans();
            
            if (isDemo)
            {
                RgbViewModel.Connect();
            }
            else
            {
                RgbViewModel.Devices.Clear();
                RgbViewModel.IsConnected = false;
            }
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void UpdateThemeObject()
    {
        if (_isInitializing) return;

        var theme = _themeService.CurrentTheme;

        theme.BackgroundColor = BtnBgColor.Tag?.ToString() ?? theme.BackgroundColor;
        theme.ForegroundColor = BtnFgColor.Tag?.ToString() ?? theme.ForegroundColor;
        theme.AccentColor = BtnAccentColor.Tag?.ToString() ?? theme.AccentColor;
        theme.TrackColor = BtnTrackColor.Tag?.ToString() ?? theme.TrackColor;
        theme.AlertColor = BtnAlertColor.Tag?.ToString() ?? theme.AlertColor;

        theme.IsCpuEnabled = ChkCpu.IsChecked ?? true;
        theme.IsGpuEnabled = ChkGpu.IsChecked ?? true;
        theme.IsMemoryEnabled = ChkMemory.IsChecked ?? true;
        theme.IsMotherboardEnabled = ChkMotherboard.IsChecked ?? false;
        theme.IsNetworkEnabled = ChkNetwork.IsChecked ?? false;

        theme.ShowGaugesScreen = ChkGaugesScreen.IsChecked ?? true;
        theme.ShowStorageScreen = ChkStorageScreen.IsChecked ?? true;
        theme.ShowClockScreen = ChkClockScreen.IsChecked ?? true;
        theme.ShowWeatherScreen = ChkWeatherScreenLayout.IsChecked ?? false;
        theme.ShowFansScreen = ChkFansScreen.IsChecked ?? true;
        theme.ShowRgbScreen = ChkRgbScreen.IsChecked ?? true;
        if (theme.Weather == null) theme.Weather = new WeatherConfig();
        theme.Weather.ShowWeatherGallery = ChkWeatherGallery.IsChecked ?? false;

        theme.EnabledPlugins = PluginSettings.Where(ps => ps.IsEnabled).Select(ps => ps.Name).ToList();
        theme.ScreenRotationOrder = ScreenRotationList.ToList();
        
        theme.ActiveMonitorsOrder = ActiveMonitors.ToList();

        theme.LogoPath = TxtLogo.Text;
        theme.BackgroundImagePath = TxtBgImage.Text;
        theme.BackgroundOpacity = SldOpacity.Value;
        theme.TransitionDelaySeconds = (int)SldInterval.Value;
        theme.DisplayMode = ChkAutoRotate.IsChecked == true ? DisplayMode.Auto : DisplayMode.Manual;
        
        theme.DisableSecondaryScreenDragging = ChkDisableSecondaryScreenDragging.IsChecked == true;
        
        if (theme.LaunchOnStartup != (ChkLaunchOnStartup.IsChecked == true))
        {
            theme.LaunchOnStartup = ChkLaunchOnStartup.IsChecked == true;
            try { UpdateStartupTask(theme.LaunchOnStartup); } catch { }
        }
 
        SaveClockSettings();
        SaveWeatherSettings();
 
        // Persist to disk and update UI
        _themeService.SaveTheme(true);
        _themeService.NotifyThemeUpdated();
        UserActionLogger.LogAction("Updated global theme configuration.");
    }

    private void SaveWeatherSettings()
    {
        var theme = _themeService.CurrentTheme;
        theme.Weather ??= new WeatherConfig();
        var w = theme.Weather;

        theme.ShowWeatherScreen = ChkShowWeather.IsChecked ?? false;
        if (ChkWeatherGallery != null) w.ShowWeatherGallery = ChkWeatherGallery.IsChecked ?? false;
        w.ApiKey = TxtWeatherApiKey.Password;
        w.City = TxtWeatherCity.Text;
        // if (TxtWeatherCityLayout != null) TxtWeatherCityLayout.Text = w.City;
        
        w.Units = (CmbWeatherUnits.SelectedItem as ComboBoxItem)?.Content?.ToString()?.Contains("°F") == true ? "imperial" : "metric";
        w.WeatherTheme = (CmbWeatherTheme.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Dark";
        
        var intervalItem = CmbWeatherInterval.SelectedItem as ComboBoxItem;
        if (intervalItem != null && int.TryParse(intervalItem.Tag?.ToString(), out int mins))
        {
            w.UpdateIntervalMinutes = mins;
        }

        w.GlowColor = BtnWeatherGlowColor.Tag?.ToString() ?? "#3b82f6";
        w.TimeFormat = (CmbWeatherTimeFormat.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "12h";
    }

    private void BtnColorPick_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            var initialHex = btn.Tag?.ToString() ?? "#FFFFFF";
            var picker = new ColorPickerWindow(initialHex) { Owner = this };
            
            if (picker.ShowDialog() == true)
            {
                var hex = picker.SelectedHex;
                btn.Tag = hex;
                btn.Background = new BrushConverter().ConvertFromString(hex) as SolidColorBrush;
                UpdateThemeObject();
            }
        }
    }

    private void BtnMoveUp_Click(object sender, RoutedEventArgs e)
    {
        int idx = LstOrder.SelectedIndex;
        if (idx > 0)
        {
            var item = ActiveMonitors[idx];
            ActiveMonitors.RemoveAt(idx);
            ActiveMonitors.Insert(idx - 1, item);
            LstOrder.SelectedIndex = idx - 1;
            UpdateThemeObject();
        }
    }

    private void BtnMoveDown_Click(object sender, RoutedEventArgs e)
    {
        int idx = LstOrder.SelectedIndex;
        if (idx >= 0 && idx < ActiveMonitors.Count - 1)
        {
            var item = ActiveMonitors[idx];
            ActiveMonitors.RemoveAt(idx);
            ActiveMonitors.Insert(idx + 1, item);
            LstOrder.SelectedIndex = idx + 1;
            UpdateThemeObject();
        }
    }

    private void LstOrder_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (LstOrder.SelectedItem is string selected)
        {
            LblConfigTitle.Text = $"{selected.ToUpper()} CONFIG";
            var theme = _themeService.CurrentTheme;
            double scale = theme.GaugeScales != null && theme.GaugeScales.TryGetValue(selected, out double s) ? s : 1.0;
            if (LblGaugeScale != null) LblGaugeScale.Text = $"{scale:F2}x";
        }
        else
        {
            LblConfigTitle.Text = "SELECT A GAUGE ABOVE";
            if (LblGaugeScale != null) LblGaugeScale.Text = "1.00x";
        }
    }

    private void BtnGaugeConfig_Click(object sender, RoutedEventArgs e)
    {
        if (LstOrder.SelectedItem is not string selectedMonitor || sender is not Button btn) return;

        var theme = _themeService.CurrentTheme;
        if (theme.GaugeScales == null) theme.GaugeScales = new(StringComparer.OrdinalIgnoreCase);
        if (theme.GaugeOffsetsX == null) theme.GaugeOffsetsX = new(StringComparer.OrdinalIgnoreCase);
        if (theme.GaugeOffsetsY == null) theme.GaugeOffsetsY = new(StringComparer.OrdinalIgnoreCase);

        // Ensure defaults exist
        if (!theme.GaugeScales.ContainsKey(selectedMonitor)) theme.GaugeScales[selectedMonitor] = 1.0;
        if (!theme.GaugeOffsetsX.ContainsKey(selectedMonitor)) theme.GaugeOffsetsX[selectedMonitor] = 0.0;
        if (!theme.GaugeOffsetsY.ContainsKey(selectedMonitor)) theme.GaugeOffsetsY[selectedMonitor] = 0.0;

        double stepScale = 0.05;
        double stepPos = 2.0;

        switch (btn.Name)
        {
            case "BtnGaugeScaleUp": theme.GaugeScales[selectedMonitor] += stepScale; break;
            case "BtnGaugeScaleDown": theme.GaugeScales[selectedMonitor] = Math.Max(0.1, theme.GaugeScales[selectedMonitor] - stepScale); break;
            case "BtnGaugeUp": theme.GaugeOffsetsY[selectedMonitor] -= stepPos; break;
            case "BtnGaugeDown": theme.GaugeOffsetsY[selectedMonitor] += stepPos; break;
            case "BtnGaugeLeft": theme.GaugeOffsetsX[selectedMonitor] -= stepPos; break;
            case "BtnGaugeRight": theme.GaugeOffsetsX[selectedMonitor] += stepPos; break;
            case "BtnGaugeReset":
                theme.GaugeScales[selectedMonitor] = 1.0;
                theme.GaugeOffsetsX[selectedMonitor] = 0.0;
                theme.GaugeOffsetsY[selectedMonitor] = 0.0;
                break;
        }

        if (LblGaugeScale != null) LblGaugeScale.Text = $"{theme.GaugeScales[selectedMonitor]:F2}x";

        UpdateThemeObject();
    }
    private void BtnResetAllGauges_Click(object sender, RoutedEventArgs e)
    {
        var theme = _themeService.CurrentTheme;
        if (theme.GaugeScales != null) theme.GaugeScales.Clear();
        if (theme.GaugeOffsetsX != null) theme.GaugeOffsetsX.Clear();
        if (theme.GaugeOffsetsY != null) theme.GaugeOffsetsY.Clear();
        
        if (LstOrder.SelectedItem is string selectedMonitor)
        {
            if (LblGaugeScale != null) LblGaugeScale.Text = "1.00x";
        }
        
        UpdateThemeObject();
    }

    private void OnSettingChanged(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;

        if (sender is CheckBox chk && chk.Name.StartsWith("Chk") && chk.Name != "ChkStorage")
        {
            var map = new System.Collections.Generic.Dictionary<string, string>
            {
                { "ChkCpu", Constants.GaugeCpu }, { "ChkGpu", Constants.GaugeGpu }, { "ChkMemory", Constants.GaugeRam },
                { "ChkMotherboard", Constants.GaugeMotherboard }, { "ChkNetwork", Constants.GaugeNetwork }
            };

            if (map.TryGetValue(chk.Name, out var monitorName))
            {
                if (chk.IsChecked == true)
                {
                    if (ActiveMonitors.Count >= 3)
                    {
                        PcStatsMonitor.Controls.GlassMessageBox.ShowDialog(this, "Maximum of 3 monitors can be displayed at a time.", "Limit Reached");
                        _isInitializing = true;
                        chk.IsChecked = false;
                        _isInitializing = false;
                        return;
                    }
                    if (!ActiveMonitors.Contains(monitorName)) 
                    {
                        ActiveMonitors.Add(monitorName);
                        
                        // Sort active monitors by master default order to preserve placement
                        var masterOrder = new System.Collections.Generic.List<string> { 
                            Constants.GaugeGpu, Constants.GaugeCpu, Constants.GaugeRam, Constants.GaugeMotherboard, Constants.GaugeNetwork 
                        };
                        var sorted = ActiveMonitors.OrderBy(m => masterOrder.IndexOf(m)).ToList();
                        ActiveMonitors.Clear();
                        foreach (var m in sorted) ActiveMonitors.Add(m);
                    }
                }
                else
                {
                    ActiveMonitors.Remove(monitorName);
                }
            }
        }
        UpdateThemeObject();
    }

    private void OnScreenSettingChanged(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        if (!ValidateActiveScreenCount(sender)) return;

        UpdateScreenRotationList();
        UpdateThemeObject();
    }

    private bool ValidateActiveScreenCount(object sender)
    {
        int activeCount = (ChkGaugesScreen.IsChecked == true ? 1 : 0) + 
                          (ChkStorageScreen.IsChecked == true ? 1 : 0) + 
                          (ChkClockScreen.IsChecked == true ? 1 : 0) + 
                          (ChkWeatherScreenLayout?.IsChecked == true || ChkShowWeather?.IsChecked == true ? 1 : 0) +
                          (ChkWeatherGallery?.IsChecked == true ? 1 : 0) +
                          (ChkFansScreen.IsChecked == true ? 1 : 0) +
                          (ChkRgbScreen.IsChecked == true ? 1 : 0) +
                          PluginSettings.Count(ps => ps.IsEnabled);

        if (activeCount == 0)
        {
            PcStatsMonitor.Controls.GlassMessageBox.ShowDialog(this, "At least one screen must be active.", "Requirement");
            _isInitializing = true;
            if (sender is CheckBox cb) cb.IsChecked = true;
            _isInitializing = false;
            return false;
        }
        return true;
    }

    private void OnPluginSettingChanged(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        
        // Ensure the model is updated before we rebuild the list
        if (sender is CheckBox cb && cb.DataContext is PluginToggle toggle)
        {
            // Temporarily store old value for rollback
            bool oldVal = toggle.IsEnabled;
            toggle.IsEnabled = cb.IsChecked ?? false;

            if (!ValidateActiveScreenCount(sender))
            {
                toggle.IsEnabled = oldVal; // Rollback model too
                return;
            }
        }

        UpdateScreenRotationList();
        UpdateThemeObject();
    }

    private void UpdateScreenRotationList()
    {
        // Add/Remove items from ScreenRotationList based on current checkboxes
        // but preserve order if they already exist
        var activeItems = new List<string>();
        if (ChkGaugesScreen.IsChecked == true) activeItems.Add("Gauges");
        if (ChkStorageScreen.IsChecked == true) activeItems.Add("Storage");
        if (ChkClockScreen.IsChecked == true) activeItems.Add("Clock");
        
        // Check either checkbox (they should be synced anyway)
        if ((ChkWeatherScreenLayout?.IsChecked == true) || (ChkShowWeather?.IsChecked == true)) 
            activeItems.Add("Weather");
            
        if (ChkWeatherGallery != null && ChkWeatherGallery.IsChecked == true) activeItems.Add("Gallery");
        if (ChkFansScreen.IsChecked == true) activeItems.Add("Fans");
        if (ChkRgbScreen.IsChecked == true) activeItems.Add("RGB");
        foreach(var ps in PluginSettings) if (ps.IsEnabled) activeItems.Add(ps.Name);

        // 1. Remove items no longer active
        for (int i = ScreenRotationList.Count - 1; i >= 0; i--)
        {
            if (!activeItems.Contains(ScreenRotationList[i])) ScreenRotationList.RemoveAt(i);
        }

        // 2. Add new active items 
        foreach(var item in activeItems)
        {
            if (!ScreenRotationList.Contains(item)) ScreenRotationList.Add(item);
        }
    }

    private void BtnMoveScreenUp_Click(object sender, RoutedEventArgs e)
    {
        int idx = LstScreenOrder.SelectedIndex;
        if (idx > 0) 
        {
            var item = ScreenRotationList[idx];
            ScreenRotationList.RemoveAt(idx);
            ScreenRotationList.Insert(idx - 1, item);
            LstScreenOrder.SelectedIndex = idx - 1;
            UpdateThemeObject();
        }
    }

    private void BtnMoveScreenDown_Click(object sender, RoutedEventArgs e)
    {
        int idx = LstScreenOrder.SelectedIndex;
        if (idx >= 0 && idx < ScreenRotationList.Count - 1)
        {
            var item = ScreenRotationList[idx];
            ScreenRotationList.RemoveAt(idx);
            ScreenRotationList.Insert(idx + 1, item);
            LstScreenOrder.SelectedIndex = idx + 1;
            UpdateThemeObject();
        }
    }

    private void SldOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateThemeObject();
    private void SldInterval_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateThemeObject();

    private void BtnBrowseLogo_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Image Files|*.png;*.jpg;*.jpeg" };
        if (dlg.ShowDialog() == true) { TxtLogo.Text = dlg.FileName; UpdateThemeObject(); }
    }

    private void BtnBrowseBg_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Image Files|*.png;*.jpg;*.jpeg" };
        if (dlg.ShowDialog() == true) { TxtBgImage.Text = dlg.FileName; UpdateThemeObject(); }
    }

    private void BtnClearBg_Click(object sender, RoutedEventArgs e) { TxtBgImage.Text = ""; UpdateThemeObject(); }
    private void BtnClearLogo_Click(object sender, RoutedEventArgs e) { TxtLogo.Text = ""; UpdateThemeObject(); }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        UpdateThemeObject();
        _themeService.SaveTheme(true);
        PcStatsMonitor.Controls.GlassMessageBox.ShowDialog(this, "Settings saved successfully!", "Saved");
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Minimizes the settings window.
    /// </summary>
    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    /// <summary>
    /// Toggles between maximized and normal window state.
    /// Updates the button content to reflect the current state.
    /// </summary>
    private void BtnMaxRestore_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            BtnMaxRestore.Content = "\u25A1"; // □ Maximize icon
            BtnMaxRestore.ToolTip = "Maximize";
        }
        else
        {
            WindowState = WindowState.Maximized;
            BtnMaxRestore.Content = "\u2750"; // ❐ Restore icon
            BtnMaxRestore.ToolTip = "Restore";
        }
    }

    // ── Clock Settings ─────────────────────────────────────────────────────────

    private static readonly string[] FaceNames = { 
        "Classic", "Neon", "Minimal", "Glow", "Bold", 
        "Luxury", "Square", "Techno", "Roman", "Gold", 
        "Dot", "Orbit", "Industrial", "Retro", "Futura",
        "Square Minimal", "Square Luxury"
    };
    private string _selectedFace = "Classic";

    private void LoadClockSettings()
    {
        var theme = _themeService.CurrentTheme;
        theme.Clock ??= new ClockConfig();
        var clk = theme.Clock;
        _selectedFace = clk.FaceName ?? "Classic";

        // Populate face selector cards
        PnlFaces.Children.Clear();
        foreach (var face in FaceNames)
        {
            bool isSelected = face == _selectedFace;
            // Tiles follow the Settings theme (client preference); the mini clock
            // preview itself keeps its real dark face so it still reflects how the
            // clock renders on the always-dark 7" display.
            bool lightTiles = _themeService.CurrentTheme.IsUiThemeLight;
            var card = new Border
            {
                Width = 100, Height = 100, CornerRadius = new CornerRadius(10),
                Margin = new Thickness(0, 0, 10, 0),
                Background = TileBackground(lightTiles, isSelected),
                BorderBrush = isSelected
                    ? new SolidColorBrush(Color.FromArgb(200, 59, 130, 246))
                    : new SolidColorBrush(Color.FromArgb(70, 140, 150, 170)),
                BorderThickness = new Thickness(isSelected ? 2 : 1),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = face
            };

            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            
            // Visual Preview (Mini Clock)
            var previewBox = new Viewbox { Width = 60, Height = 60 };
            var previewClock = new PcStatsMonitor.Controls.BuiltInClockScreen();
            var prevCfg = new ClockConfig {
                FaceName = face, ClockScale = 0.9,
                HourHandColor = "#FFFFFF", MinuteHandColor = "#FFFFFF", SecondHandColor = "#3b82f6",
                ClockFaceColor = "#1A1A1A",
                ShowDigitalClock = false, ShowDate = false
            };
            previewClock.ApplyConfig(prevCfg, new ThemeConfig { BackgroundColor = "Transparent" });
            previewBox.Child = previewClock;

            // The clock renders with its real dark face (it previews the always-dark
            // 7" display). On light tiles it needs a dark "mini screen" backdrop, or
            // the white hands/markers wash out and the face reads as a black dot.
            var previewHost = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(4),
                Margin = new Thickness(0, 0, 0, 5),
                HorizontalAlignment = HorizontalAlignment.Center,
                Background = lightTiles
                    ? new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x1C))
                    : Brushes.Transparent,
                Child = previewBox
            };
            stack.Children.Add(previewHost);

            var label = new TextBlock
            {
                Text = face, HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = lightTiles
                    ? new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B))
                    : Brushes.White,
                Opacity = isSelected ? 1.0 : 0.7,
                FontWeight = isSelected ? FontWeights.Bold : FontWeights.Normal,
                FontSize = 11
            };
            stack.Children.Add(label);
            card.Child = stack;

            card.MouseLeftButtonDown += (s, _) =>
            {
                if (s is Border b && b.Tag is string faceName)
                {
                    _selectedFace = faceName;
                    SaveClockSettings(); // Save the new selection
                    LoadClockSettings(); // Re-render cards to show selection
                    _themeService.NotifyThemeUpdated();
                }
            };
            PnlFaces.Children.Add(card);
        }

        ApplyClockControlValues(clk);
    }

    /// <summary>Face-tile backdrop for the current theme and selection state.</summary>
    private static SolidColorBrush TileBackground(bool light, bool isSelected)
    {
        if (light)
        {
            return isSelected
                ? new SolidColorBrush(Color.FromRgb(0xDB, 0xE7, 0xFB))
                : new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
        }

        return isSelected
            ? new SolidColorBrush(Color.FromRgb(0x1B, 0x24, 0x40))
            : new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x1C));
    }

    /// <summary>Populates the Clock tab's controls (colors, sliders, combos, toggles) from the config.</summary>
    private void ApplyClockControlValues(ClockConfig clk)
    {
        // Setup color buttons
        SetColorButton(BtnClockHourColor,  clk.HourHandColor);
        SetColorButton(BtnClockMinColor,   clk.MinuteHandColor);
        SetColorButton(BtnClockSecColor,   clk.SecondHandColor);
        SetColorButton(BtnClockFaceColor,  clk.ClockFaceColor);
        SetColorButton(BtnDigitalColor,    clk.DigitalColor);
        SetColorButton(BtnDateColor,       clk.DateColor);
        SetColorButton(BtnClockBgColor,    clk.CustomBackgroundColor);

        // Sliders
        SldDigitalSize.Value = clk.DigitalFontSize;
        SldDateSize.Value =    clk.DateFontSize;
        SldClockBgOpacity.Value = clk.CustomBackgroundOpacity;

        // Position labels
        TxtClockScale.Text  = $"{clk.ClockScale:F1}x";
        TxtClockX.Text      = ((int)clk.ClockOffsetX).ToString();
        TxtClockY.Text      = ((int)clk.ClockOffsetY).ToString();
        TxtDigitalX.Text    = ((int)clk.DigitalOffsetX).ToString();
        TxtDigitalY.Text    = ((int)clk.DigitalOffsetY).ToString();
        TxtDateX.Text       = ((int)clk.DateOffsetX).ToString();
        TxtDateY.Text       = ((int)clk.DateOffsetY).ToString();

        // ComboBoxes
        SetComboItem(CmbDigitalFont,   clk.DigitalFontFamily);
        SetComboItem(CmbDateFont,      clk.DateFontFamily);
        SetComboItem(CmbDigitalFormat, clk.DigitalFormat);
        SetComboItem(CmbDateFormat,    clk.DateFormat);

        TxtClockBgImage.Text     = clk.CustomBackgroundImagePath ?? "";

        // Marker Color
        SetColorButton(BtnClockMarkerColor, clk.MarkerColor ?? "#888888");

        // Glow
        ChkClockShowGlow.IsChecked = clk.ShowGlow;
        ChkClockShowOuterRing.IsChecked = clk.ShowOuterRing;
        SetColorButton(BtnClockGlowColor, clk.GlowColor ?? "#3b82f6");
        SldClockGlowWidth.Value = clk.GlowWidth;

        // Motion
        ChkClockMotion.IsChecked = clk.ContinuousMotion;

        // Date & Digital Toggles
        ChkShowDigital.IsChecked = clk.ShowDigitalClock;
        ChkShowDate.IsChecked = clk.ShowDate;
        ChkClockCustomBg.IsChecked = clk.UseCustomBackground;
    }

    private void SaveClockSettings()
    {
        var theme = _themeService.CurrentTheme;
        theme.Clock ??= new ClockConfig();
        var clk = theme.Clock;

        clk.FaceName           = _selectedFace;
        clk.HourHandColor      = BtnClockHourColor.Tag?.ToString()  ?? clk.HourHandColor;
        clk.MinuteHandColor    = BtnClockMinColor.Tag?.ToString()    ?? clk.MinuteHandColor;
        clk.SecondHandColor    = BtnClockSecColor.Tag?.ToString()    ?? clk.SecondHandColor;
        clk.ClockFaceColor     = BtnClockFaceColor.Tag?.ToString()   ?? clk.ClockFaceColor;
        clk.DigitalColor       = BtnDigitalColor.Tag?.ToString()     ?? clk.DigitalColor;
        clk.DateColor          = BtnDateColor.Tag?.ToString()        ?? clk.DateColor;
        clk.CustomBackgroundColor = BtnClockBgColor.Tag?.ToString()  ?? clk.CustomBackgroundColor;

        clk.DigitalFontSize    = SldDigitalSize.Value;
        clk.DateFontSize       = SldDateSize.Value;
        clk.CustomBackgroundOpacity = SldClockBgOpacity.Value;

        clk.DigitalFontFamily  = (CmbDigitalFont.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? clk.DigitalFontFamily;
        clk.DateFontFamily     = (CmbDateFont.SelectedItem    as ComboBoxItem)?.Content?.ToString() ?? clk.DateFontFamily;
        clk.DigitalFormat      = (CmbDigitalFormat.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? clk.DigitalFormat;
        clk.DateFormat         = (CmbDateFormat.SelectedItem   as ComboBoxItem)?.Content?.ToString() ?? clk.DateFormat;

        clk.UseCustomBackground        = ChkClockCustomBg.IsChecked ?? false;
        clk.ShowDigitalClock           = ChkShowDigital.IsChecked ?? true;
        clk.ShowDate                  = ChkShowDate.IsChecked ?? true;
        clk.CustomBackgroundImagePath  = TxtClockBgImage.Text;

        clk.MarkerColor                = BtnClockMarkerColor.Tag?.ToString() ?? clk.MarkerColor;
        clk.ShowGlow                   = ChkClockShowGlow.IsChecked ?? false;
        clk.ShowOuterRing              = ChkClockShowOuterRing.IsChecked ?? true;
        clk.GlowColor                  = BtnClockGlowColor.Tag?.ToString() ?? clk.GlowColor;
        clk.GlowWidth                  = SldClockGlowWidth.Value;
        clk.ContinuousMotion           = ChkClockMotion.IsChecked ?? false;
    }

    private void BtnClockColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var initialHex = btn.Tag?.ToString() ?? "#FFFFFF";
        var picker = new ColorPickerWindow(initialHex) { Owner = this };
        if (picker.ShowDialog() == true)
        {
            SetColorButton(btn, picker.SelectedHex);
            SaveClockSettings();
            _themeService.NotifyThemeUpdated();
        }
    }

    private void BtnClockPos_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var clk = _themeService.CurrentTheme.Clock!;
        const double step = 5.0;
        switch (btn.Name)
        {
            case "BtnClockScaleUp":   clk.ClockScale    = Math.Min(3.0, clk.ClockScale + 0.1); break;
            case "BtnClockScaleDn":   clk.ClockScale    = Math.Max(0.3, clk.ClockScale - 0.1); break;
            case "BtnClockLeft":      clk.ClockOffsetX -= step; break;
            case "BtnClockRight":     clk.ClockOffsetX += step; break;
            case "BtnClockUp":        clk.ClockOffsetY -= step; break;
            case "BtnClockDown":      clk.ClockOffsetY += step; break;
            case "BtnDigitalLeft":    clk.DigitalOffsetX -= step; break;
            case "BtnDigitalRight":   clk.DigitalOffsetX += step; break;
            case "BtnDigitalUp":      clk.DigitalOffsetY -= step; break;
            case "BtnDigitalDown":    clk.DigitalOffsetY += step; break;
            case "BtnDateLeft":       clk.DateOffsetX -= step; break;
            case "BtnDateRight":      clk.DateOffsetX += step; break;
            case "BtnDateUp":         clk.DateOffsetY -= step; break;
            case "BtnDateDown":       clk.DateOffsetY += step; break;
        }
        TxtClockScale.Text = $"{clk.ClockScale:F1}x";
        TxtClockX.Text     = ((int)clk.ClockOffsetX).ToString();
        TxtClockY.Text     = ((int)clk.ClockOffsetY).ToString();
        TxtDigitalX.Text   = ((int)clk.DigitalOffsetX).ToString();
        TxtDigitalY.Text   = ((int)clk.DigitalOffsetY).ToString();
        TxtDateX.Text      = ((int)clk.DateOffsetX).ToString();
        TxtDateY.Text      = ((int)clk.DateOffsetY).ToString();
        _themeService.NotifyThemeUpdated();
    }

    private void OnClockSettingChanged(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        bool customBg = ChkClockCustomBg.IsChecked ?? false;
        PnlClockCustomBg.Visibility = customBg ? Visibility.Visible : Visibility.Collapsed;
        TxtClockBgInfo.Visibility   = customBg ? Visibility.Collapsed : Visibility.Visible;
        SaveClockSettings();
        _themeService.NotifyThemeUpdated();
    }

    private void OnClockSettingChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        SaveClockSettings();
        _themeService.NotifyThemeUpdated();
    }

    private void OnClockSettingChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing) return;
        SaveClockSettings();
        _themeService.NotifyThemeUpdated();
    }

    private void BtnBrowseClockBg_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Image Files|*.png;*.jpg;*.jpeg" };
        if (dlg.ShowDialog() == true) { TxtClockBgImage.Text = dlg.FileName; SaveClockSettings(); _themeService.NotifyThemeUpdated(); }
    }

    private void BtnClearClockBg_Click(object sender, RoutedEventArgs e)
    {
        TxtClockBgImage.Text = "";
        SaveClockSettings();
        _themeService.NotifyThemeUpdated();
    }

    private void ScrFaces_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer)
        {
            if (e.Delta > 0) scrollViewer.LineLeft();
            else scrollViewer.LineRight();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Scrolls the page-level ScrollViewer directly on mouse wheel so inner
    /// controls (ListBoxes, TextBoxes) can't swallow the event and make the
    /// page feel "stuck". The horizontal clock-face strip keeps its own wheel.
    /// </summary>
    private void PageScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer) return;
        if (IsInsideFaceStrip(e.OriginalSource as DependencyObject)) return;

        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta * 0.75);
        e.Handled = true;
    }

    /// <summary>Returns true when the wheel event originated inside the ScrFaces strip.</summary>
    private bool IsInsideFaceStrip(DependencyObject? source)
    {
        while (source != null)
        {
            if (ReferenceEquals(source, ScrFaces)) return true;
            source = source is System.Windows.Media.Visual
                ? System.Windows.Media.VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }
        return false;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────
    private static void SetColorButton(Button btn, string hex)
    {
        try
        {
            btn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            btn.Tag = hex;
        }
        catch { btn.Tag = hex; }
    }

    private static void SetComboItem(ComboBox cmb, string value)
    {
        foreach (ComboBoxItem item in cmb.Items)
        {
            if (item.Content?.ToString()?.Equals(value, StringComparison.OrdinalIgnoreCase) == true)
            {
                cmb.SelectedItem = item;
                return;
            }
        }
        if (cmb.Items.Count > 0) cmb.SelectedIndex = 0;
    }

    private void OnWeatherSettingChanged(object sender, RoutedEventArgs e) 
    {
        if (_isInitializing) return;
        
        // Sync the two checkboxes if they exist
        if (sender == ChkShowWeather && ChkWeatherScreenLayout != null) ChkWeatherScreenLayout.IsChecked = ChkShowWeather.IsChecked;
        else if (sender == ChkWeatherScreenLayout && ChkShowWeather != null) ChkShowWeather.IsChecked = ChkWeatherScreenLayout.IsChecked;

        // Validation for unchecking
        if (!ValidateActiveScreenCount(sender)) return;

        // Sync City textboxes
        // if (sender == TxtWeatherCity && TxtWeatherCityLayout != null) TxtWeatherCityLayout.Text = TxtWeatherCity.Text;
        // else if (sender == TxtWeatherCityLayout && TxtWeatherCity != null) TxtWeatherCity.Text = TxtWeatherCityLayout.Text;

        UpdateScreenRotationList();
        UpdateThemeObject();
    }
    private void OnWeatherSettingChanged(object sender, SelectionChangedEventArgs e) => UpdateThemeObject();
    private void WeatherApiKey_PasswordChanged(object sender, RoutedEventArgs e) { /* Don't update theme on every keystroke for security/perf */ }
    
    private void BtnSaveWeatherKey_Click(object sender, RoutedEventArgs e)
    {
        UpdateThemeObject();
        _themeService.SaveTheme(true);
        PcStatsMonitor.Controls.GlassMessageBox.ShowDialog(this, "API Key saved.", "Weather");
    }

    private void BtnUpdateWeather_Click(object sender, RoutedEventArgs e)
    {
        UpdateThemeObject();
        _themeService.NotifyThemeUpdated();
        PcStatsMonitor.Controls.GlassMessageBox.ShowDialog(this, "Weather location updated.", "Weather");
    }

    // Weather City API Search Logic
    private System.Windows.Threading.DispatcherTimer _citySearchTimer;
    private bool _isUpdatingCity;

    private void TxtWeatherCity_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_isUpdatingCity) return;
        if (e.Key == System.Windows.Input.Key.Enter || e.Key == System.Windows.Input.Key.Up || e.Key == System.Windows.Input.Key.Down || e.Key == System.Windows.Input.Key.Escape) return;
        
        if (_citySearchTimer == null)
        {
            _citySearchTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _citySearchTimer.Tick += CitySearchTimer_Tick;
        }
        _citySearchTimer.Stop();
        _citySearchTimer.Start();
    }

    private async void CitySearchTimer_Tick(object sender, EventArgs e)
    {
        _citySearchTimer.Stop();
        
        var tb = TxtWeatherCity.Template.FindName("PART_EditableTextBox", TxtWeatherCity) as System.Windows.Controls.TextBox;
        string query = tb?.Text ?? TxtWeatherCity.Text;
        if (string.IsNullOrWhiteSpace(query) || query.Length < 3) return;
        
        string apiKey = TxtWeatherApiKey.Password;
        if (string.IsNullOrWhiteSpace(apiKey)) return;

        try
        {
            int limit = 5;
            string url = $"https://api.openweathermap.org/geo/1.0/direct?q={Uri.EscapeDataString(query)}&limit={limit}&appid={apiKey}";
            using var client = new System.Net.Http.HttpClient();
            var response = await client.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var items = System.Text.Json.JsonSerializer.Deserialize<List<GeoCity>>(json);
                if (items != null && items.Count > 0)
                {
                    _isUpdatingCity = true;
                    
                    string savedText = tb?.Text ?? TxtWeatherCity.Text;
                    int savedCaret = tb?.CaretIndex ?? savedText.Length;
                    
                    TxtWeatherCity.ItemsSource = items;
                    TxtWeatherCity.SelectedIndex = -1; // Don't auto-select anything
                    TxtWeatherCity.IsDropDownOpen = true;
                    
                    // Restore text on next dispatcher frame, after WPF finishes its auto-select
                    Dispatcher.BeginInvoke(new System.Action(() =>
                    {
                        if (tb != null)
                        {
                            tb.Text = savedText;
                            tb.CaretIndex = savedCaret;
                        }
                        else
                        {
                            TxtWeatherCity.Text = savedText;
                        }
                        _isUpdatingCity = false;
                    }), System.Windows.Threading.DispatcherPriority.Input);
                }
            }
        }
        catch { _isUpdatingCity = false; }
    }

    private void BtnApplyLicense_Click(object sender, RoutedEventArgs e)
    {
        string rawKey = TxtLicenseKey.Text.Trim();
        if (string.IsNullOrWhiteSpace(rawKey))
        {
            PcStatsMonitor.Controls.CustomMessageBox.ShowDialog(this, "Please paste a valid license key string first.", "Missing Key", MessageBoxButton.OK);
            return;
        }

        System.IO.File.WriteAllText("license.key", rawKey);
        
        var licenseSvc = new LicenseService();
        if (licenseSvc.CheckLicense(out string errorMessage))
        {
            TxtLicenseStatus.Text = "Status: License Activated and Valid";
            TxtLicenseStatus.Foreground = new SolidColorBrush(Colors.MediumSeaGreen);
            
            foreach (TabItem item in MainTabControl.Items)
            {
                item.Visibility = Visibility.Visible;
            }
            
            PcStatsMonitor.Controls.CustomMessageBox.ShowDialog(this, "Thank you! License strictly verified and activated.", "Success", MessageBoxButton.OK);
        }
        else
        {
            TxtLicenseStatus.Text = $"Status: {errorMessage}";
            TxtLicenseStatus.Foreground = new SolidColorBrush(Color.FromRgb(230, 57, 70)); // #E63946
            PcStatsMonitor.Controls.CustomMessageBox.ShowDialog(this, $"Verification Failed:\n{errorMessage}", "Invalid License", MessageBoxButton.OK);
        }
    }

    public class GeoCity
    {
        public string name { get; set; }
        public string state { get; set; }
        public string country { get; set; }
        public override string ToString() => string.IsNullOrWhiteSpace(state) ? $"{name}, {country}" : $"{name}, {state}, {country}";
    }

    private void UpdateStartupTask(bool enable)
    {
        string taskName = "BYLDCore_PCStatsMonitor_Startup";
        string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;

        if (string.IsNullOrEmpty(exePath)) return;

        using (var ts = new TaskService())
        {
            if (enable)
            {
                TaskDefinition td = ts.NewTask();
                td.RegistrationInfo.Description = "Starts BYLD Core (PC Stats Monitor) on User Logon with Highest Privileges";
                td.Principal.RunLevel = TaskRunLevel.Highest;
                
                // Trigger at logon of the current user
                td.Triggers.Add(new LogonTrigger { UserId = System.Security.Principal.WindowsIdentity.GetCurrent().Name });

                // Action: Start the program
                string workingDir = System.IO.Path.GetDirectoryName(exePath);
                td.Actions.Add(new ExecAction(exePath, null, workingDir));

                // Robust settings for background apps
                td.Settings.DisallowStartIfOnBatteries = false;
                td.Settings.StopIfGoingOnBatteries = false;
                td.Settings.ExecutionTimeLimit = TimeSpan.Zero;
                td.Settings.Priority = System.Diagnostics.ProcessPriorityClass.Normal;

                ts.RootFolder.RegisterTaskDefinition(taskName, td);
            }
            else
            {
                ts.RootFolder.DeleteTask(taskName, false);
            }
        }
    }

    private void BtnRefreshHardware_Click(object? sender, RoutedEventArgs? e)
    {
        FanViewModel.LoadFans();
        RgbViewModel.Connect();
    }

    private void LoadProfilesList()
    {
        if (CmbProfiles == null) return;
        var profiles = _themeService.GetProfiles();
        CmbProfiles.ItemsSource = profiles;
        if (profiles.Length > 0) CmbProfiles.SelectedIndex = 0;
    }

    private void BtnLoadProfile_Click(object sender, RoutedEventArgs e)
    {
        if (CmbProfiles.SelectedItem is string profileName)
        {
            if (_themeService.LoadProfile(profileName))
            {
                // Refresh UI
                LoadCurrentSettings();
                PcStatsMonitor.Controls.GlassMessageBox.ShowDialog(this, $"Layout profile '{profileName}' loaded.", "Success");
            }
            else
            {
                PcStatsMonitor.Controls.GlassMessageBox.ShowDialog(this, $"Failed to load profile.", "Error");
            }
        }
    }

    private void BtnSaveProfile_Click(object sender, RoutedEventArgs e)
    {
        string newName = TxtNewProfileName.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(newName) || newName == "Profile Name...")
        {
            PcStatsMonitor.Controls.GlassMessageBox.ShowDialog(this, "Please enter a valid name for the layout profile.", "Invalid Name");
            return;
        }

        _themeService.SaveProfile(newName);
        LoadProfilesList();
        
        // Select the newly saved one
        for (int i = 0; i < CmbProfiles.Items.Count; i++)
        {
            if (CmbProfiles.Items[i].ToString() == newName)
            {
                CmbProfiles.SelectedIndex = i;
                break;
            }
        }
        TxtNewProfileName.Text = "";
        PcStatsMonitor.Controls.GlassMessageBox.ShowDialog(this, $"Layout profile '{newName}' saved.", "Success");
    }

    private void BtnDeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (CmbProfiles.SelectedItem is string profileName)
        {
            _themeService.DeleteProfile(profileName);
            LoadProfilesList();
        }
    }
}
