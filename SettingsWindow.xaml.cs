using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PcStatsMonitor.Services;
using System.Windows.Input;
using System.Windows.Media;
using System.Collections.ObjectModel;
using System.Linq;

namespace PcStatsMonitor;

public partial class SettingsWindow : Window
{
    private readonly IThemeService _themeService;
    private bool _isInitializing = true;
    public ObservableCollection<string> ActiveMonitors { get; set; } = new();

    public SettingsWindow(IThemeService themeService)
    {
        InitializeComponent();
        _themeService = themeService;
        
        LstOrder.ItemsSource = ActiveMonitors;
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
        BtnAccentColor.Background = new BrushConverter().ConvertFromString(theme.AccentColor) as SolidColorBrush; // Actually AccentColor is used for glows, but let's assume it maps
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

        ChkGaugesScreen.IsChecked = theme.DisplayMode == "Auto" || theme.DisplayMode == "Gauges";
        ChkStorageScreen.IsChecked = theme.DisplayMode == "Auto" || theme.DisplayMode == "Storage";

        // Ordering List
        ActiveMonitors.Clear();
        foreach (var m in theme.ActiveMonitorsOrder) ActiveMonitors.Add(m);

        // Images
        TxtLogo.Text = theme.LogoPath;
        TxtBgImage.Text = theme.BackgroundImagePath;
        SldOpacity.Value = theme.BackgroundOpacity;

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

        if (ChkGaugesScreen.IsChecked == true && ChkStorageScreen.IsChecked == true)
            theme.DisplayMode = "Auto";
        else if (ChkGaugesScreen.IsChecked == true)
            theme.DisplayMode = "Gauges";
        else
            theme.DisplayMode = "Storage";
        
        theme.ActiveMonitorsOrder = ActiveMonitors.ToList();

        theme.LogoPath = TxtLogo.Text;
        theme.BackgroundImagePath = TxtBgImage.Text;
        theme.BackgroundOpacity = SldOpacity.Value;

        _themeService.NotifyThemeUpdated();
    }

    private void BtnColorPick_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            using var dlg = new System.Windows.Forms.ColorDialog();
            if (btn.Tag is string currentHex && currentHex.Length >= 7)
            {
                try { dlg.Color = System.Drawing.ColorTranslator.FromHtml(currentHex.Substring(0, 7)); } catch { }
            }

            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var hex = "#" + dlg.Color.R.ToString("X2") + dlg.Color.G.ToString("X2") + dlg.Color.B.ToString("X2");
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

    private void OnSettingChanged(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;

        if (sender is CheckBox chk && chk.Name.StartsWith("Chk") && chk.Name != "ChkStorage")
        {
            var map = new System.Collections.Generic.Dictionary<string, string>
            {
                { "ChkCpu", "CPU" }, { "ChkGpu", "GPU" }, { "ChkMemory", "RAM" },
                { "ChkMotherboard", "MOTHERBOARD" }, { "ChkNetwork", "NETWORK" }
            };

            if (map.TryGetValue(chk.Name, out var monitorName))
            {
                if (chk.IsChecked == true)
                {
                    if (ActiveMonitors.Count >= 3)
                    {
                        MessageBox.Show(this, "Maximum of 3 monitors can be displayed at a time.", "Limit Reached", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        if (ChkGaugesScreen.IsChecked == false && ChkStorageScreen.IsChecked == false)
        {
            MessageBox.Show(this, "At least one screen must be active.", "Requirement", MessageBoxButton.OK, MessageBoxImage.Information);
            _isInitializing = true;
            if (sender == ChkGaugesScreen) ChkGaugesScreen.IsChecked = true;
            else if (sender == ChkStorageScreen) ChkStorageScreen.IsChecked = true;
            _isInitializing = false;
            return;
        }

        UpdateThemeObject();
    }

    private void SldOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateThemeObject();

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
        MessageBox.Show(this, "Settings saved successfully!", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
}
