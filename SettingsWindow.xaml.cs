using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
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

    public SettingsWindow(IThemeService themeService, PluginManager? pluginManager = null)
    {
        InitializeComponent();
        _themeService = themeService;
        _pluginManager = pluginManager;
        
        LstOrder.ItemsSource = ActiveMonitors;
        LstScreenOrder.ItemsSource = ScreenRotationList;
        ItemsPlugins.ItemsSource = PluginSettings;

        LoadCurrentSettings();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void LoadCurrentSettings()
    {
        _isInitializing = true;
        var theme = _themeService.CurrentTheme;

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

        ChkGaugesScreen.IsChecked = theme.ShowGaugesScreen;
        ChkStorageScreen.IsChecked = theme.ShowStorageScreen;

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

        // Clock Settings
        LoadClockSettings();

        _isInitializing = false;
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

        theme.EnabledPlugins = PluginSettings.Where(ps => ps.IsEnabled).Select(ps => ps.Name).ToList();
        theme.ScreenRotationOrder = ScreenRotationList.ToList();
        
        theme.ActiveMonitorsOrder = ActiveMonitors.ToList();

        theme.LogoPath = TxtLogo.Text;
        theme.BackgroundImagePath = TxtBgImage.Text;
        theme.BackgroundOpacity = SldOpacity.Value;
        theme.TransitionDelaySeconds = (int)SldInterval.Value;

        SaveClockSettings();

        _themeService.NotifyThemeUpdated();
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
        PnlGaugeConfig.Visibility = LstOrder.SelectedItem is string ? Visibility.Visible : Visibility.Collapsed;
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
                    if (!ActiveMonitors.Contains(monitorName)) ActiveMonitors.Add(monitorName);
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

        // Prevent unchecking everything
        int activeCount = (ChkGaugesScreen.IsChecked == true ? 1 : 0) + 
                          (ChkStorageScreen.IsChecked == true ? 1 : 0) + 
                          PluginSettings.Count(ps => ps.IsEnabled);

        if (activeCount == 0)
        {
            PcStatsMonitor.Controls.GlassMessageBox.ShowDialog(this, "At least one screen must be active.", "Requirement");
            _isInitializing = true;
            if (sender is CheckBox cb) cb.IsChecked = true;
            _isInitializing = false;
            return;
        }

        UpdateScreenRotationList();
        UpdateThemeObject();
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

            // Prevent unchecking everything
            int activeCount = (ChkGaugesScreen.IsChecked == true ? 1 : 0) + 
                              (ChkStorageScreen.IsChecked == true ? 1 : 0) + 
                              PluginSettings.Count(ps => ps.IsEnabled);

            if (activeCount == 0)
            {
                PcStatsMonitor.Controls.GlassMessageBox.ShowDialog(this, "At least one screen must be active.", "Requirement");
                _isInitializing = true;
                toggle.IsEnabled = true;
                cb.IsChecked = true;
                _isInitializing = false;
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

        // 3. User requested "Main Dashboard will always be first"
        if (ScreenRotationList.Contains("Gauges") && ScreenRotationList[0] != "Gauges")
        {
            ScreenRotationList.Remove("Gauges");
            ScreenRotationList.Insert(0, "Gauges");
        }
    }

    private void BtnMoveScreenUp_Click(object sender, RoutedEventArgs e)
    {
        int idx = LstScreenOrder.SelectedIndex;
        if (idx > 1) // 0 is Gauges, locked
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
        if (idx >= 1 && idx < ScreenRotationList.Count - 1) // 0 is Gauges, locked
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

    // ── Clock Settings ─────────────────────────────────────────────────────────

    private static readonly string[] FaceNames = { "Classic", "Neon", "Minimal", "Glow", "Bold" };
    private string _selectedFace = "Classic";

    private void LoadClockSettings()
    {
        var clk = _themeService.CurrentTheme.Clock ?? new ClockConfig();
        _selectedFace = clk.FaceName ?? "Classic";

        // Populate face selector cards
        PnlFaces.Children.Clear();
        foreach (var face in FaceNames)
        {
            bool isSelected = face == _selectedFace;
            var card = new Border
            {
                Width = 90, Height = 90, CornerRadius = new CornerRadius(10),
                Margin = new Thickness(0, 0, 8, 0),
                Background = isSelected 
                    ? new SolidColorBrush(Color.FromArgb(80, 59, 130, 246))
                    : new SolidColorBrush(Color.FromArgb(25, 255, 255, 255)),
                BorderBrush = isSelected
                    ? new SolidColorBrush(Color.FromArgb(200, 59, 130, 246))
                    : new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                BorderThickness = new Thickness(isSelected ? 2 : 1),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = face
            };
            var label = new TextBlock
            {
                Text = face, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.White, FontWeight = isSelected ? FontWeights.Bold : FontWeights.Normal,
                FontSize = 13
            };
            card.Child = label;
            card.MouseLeftButtonDown += (s, _) =>
            {
                if (s is Border b && b.Tag is string faceName)
                {
                    _selectedFace = faceName;
                    LoadClockSettings(); // Re-render cards to show selection
                    SaveClockSettings();
                    _themeService.NotifyThemeUpdated();
                }
            };
            PnlFaces.Children.Add(card);
        }

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

        // Background toggle
        ChkClockCustomBg.IsChecked  = clk.UseCustomBackground;
        PnlClockCustomBg.Visibility = clk.UseCustomBackground ? Visibility.Visible : Visibility.Collapsed;
        TxtClockBgInfo.Visibility   = clk.UseCustomBackground ? Visibility.Collapsed : Visibility.Visible;
        TxtClockBgImage.Text        = clk.CustomBackgroundImagePath ?? "";
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
        clk.CustomBackgroundImagePath  = TxtClockBgImage.Text;
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
}
