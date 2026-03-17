using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using PcStatsMonitor.ViewModels;
using PcStatsMonitor.Services;
using PcStatsMonitor.Models;

namespace PcStatsMonitor;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _transitionTimer;
    private int _currentScreenIndex = 0; // 0 = Gauges, 1 = Storage, 2+ = Plugins
    public PluginManager PluginManager => _pluginManager;
    private PluginManager _pluginManager;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Allow moving the borderless window by clicking anywhere
        MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

        // Handle the infinite loop transitions
        _transitionTimer = new DispatcherTimer();
        _transitionTimer.Tick += TransitionTimer_Tick;

        // Listen for theme config changes to adjust the timer speed
        viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(viewModel.Theme))
            {
                _transitionTimer.Interval = TimeSpan.FromSeconds(viewModel.Theme.TransitionDelaySeconds);
                if (!_transitionTimer.IsEnabled) _transitionTimer.Start();
                EvaluateScreenVisibility(animate: false);
            }
        };

        // Start initial timer
        _transitionTimer.Interval = TimeSpan.FromSeconds(viewModel.Theme.TransitionDelaySeconds);
        _transitionTimer.Start();
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
                System.Diagnostics.Debug.WriteLine($"Error initializing plugin: {ex.Message}");
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
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error updating plugin '{plugin.Name}': {ex.Message}");
                    }
                }
            }
        };

        Loaded += (s, e) => SnapToInternalMonitor();
    }

    private void SnapToInternalMonitor()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        if (screens.Length > 1)
        {
            // Look for a vertical screen first, otherwise pick the first non-primary screen
            var targetScreen = screens.FirstOrDefault(s => s.Bounds.Height > s.Bounds.Width) 
                               ?? screens.FirstOrDefault(s => !s.Primary);

            if (targetScreen != null)
            {
                this.WindowState = WindowState.Normal;
                this.Left = targetScreen.WorkingArea.Left;
                this.Top = targetScreen.WorkingArea.Top;
                this.Width = targetScreen.WorkingArea.Width;
                this.Height = targetScreen.WorkingArea.Height;
                this.WindowState = WindowState.Maximized;
            }
        }
    }

    private void EvaluateScreenVisibility(bool animate = true)
    {
        var vm = DataContext as MainViewModel;
        if (vm == null) return;

        var config = vm.Theme;
        if (config == null) return;

        // Ensure ScreenRotationOrder is initialized properly
        if (config.ScreenRotationOrder == null || config.ScreenRotationOrder.Count == 0)
        {
            config.ScreenRotationOrder = new List<string> { "Gauges", "Storage", "Clock" };
        }

        // Ensure built-in screens (Clock) are in the rotation order if enabled
        if (config.ShowClockScreen && !config.ScreenRotationOrder.Contains("Clock"))
            config.ScreenRotationOrder.Add("Clock");

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

                var p = _pluginManager?.LoadedPlugins?.FirstOrDefault(pl => pl.Name == name);
                if (p != null && config.EnabledPlugins.Contains(p.Name))
                {
                    try { PluginHost.Content = p.GetUI(); targetScreen = PluginScreen; _currentScreenIndex = i; currentScreenValid = true; break; } catch { }
                }
            }

            // Absolute fallback if everything is somehow broken
            if (!currentScreenValid) targetScreen = GaugesContainer;
        }

        UIElement[] allScreens = { GaugesContainer, SsdScreen, PluginScreen, ClockScreen };

        if (animate)
        {
            var duration = new Duration(TimeSpan.FromMilliseconds(800));
            var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(0.0, duration);
            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(1.0, duration);

            foreach(var screen in allScreens)
            {
                if (screen == targetScreen)
                {
                    screen.Visibility = Visibility.Visible;
                    screen.IsHitTestVisible = true;
                    screen.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                }
                else
                {
                    if (screen.Visibility == Visibility.Visible && screen.Opacity > 0)
                    {
                        screen.IsHitTestVisible = false;
                        var fadeOutAnim = fadeOut.Clone();
                        fadeOutAnim.Completed += (s, e) => { if (screen != targetScreen) screen.Visibility = Visibility.Hidden; };
                        screen.BeginAnimation(UIElement.OpacityProperty, fadeOutAnim);
                    }
                }
            }
        }
        else
        {
            foreach(var screen in allScreens)
            {
                screen.BeginAnimation(UIElement.OpacityProperty, null);
                bool isActive = (screen == targetScreen);
                screen.Opacity = isActive ? 1.0 : 0.0;
                screen.IsHitTestVisible = isActive;
                screen.Visibility = isActive ? Visibility.Visible : Visibility.Hidden;
            }
        }
    }

    private void TransitionTimer_Tick(object? sender, EventArgs e)
    {
        var vm = DataContext as MainViewModel;
        if (vm == null || vm.Theme == null) return;

        var config = vm.Theme;
        if (config.DisplayMode != DisplayMode.Auto) return;

        int totalSlots = config.ScreenRotationOrder?.Count ?? 0;
        if (totalSlots == 0) return;

        // Move to next screen in order
        _currentScreenIndex = (_currentScreenIndex + 1) % totalSlots;

        // Safety: verify the new screen is actually enabled
        // If not, it will be skipped by EvaluateScreenVisibility falling back to Gauges,
        // but for smooth rotation let's try to find the next valid one here.
        for (int i = 0; i < totalSlots; i++)
        {
            string name = config.ScreenRotationOrder[_currentScreenIndex];
            bool isValid = false;
            if (name == "Gauges" && config.ShowGaugesScreen) isValid = true;
            else if (name == "Storage" && config.ShowStorageScreen) isValid = true;
            else if (name == "Clock" && config.ShowClockScreen) isValid = true;
            else if (config.EnabledPlugins != null && config.EnabledPlugins.Contains(name)) isValid = true;

            if (isValid) break;

            _currentScreenIndex = (_currentScreenIndex + 1) % totalSlots;
        }
        
        EvaluateScreenVisibility(animate: true);
    }

    private void DualCircularGauge_Loaded(object sender, RoutedEventArgs e)
    {
        // Added to resolve the XamlParseException for the 'Loaded' event in XAML.
        // You can add your custom loaded animations or logic here!
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
