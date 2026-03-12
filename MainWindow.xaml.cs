using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using PcStatsMonitor.ViewModels;

namespace PcStatsMonitor;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _transitionTimer;
    private bool _showingGauges = true;

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
        
        _showingGauges = true;
        EvaluateScreenVisibility(animate: false);
    }

    private void EvaluateScreenVisibility(bool animate = true)
    {
        var vm = DataContext as MainViewModel;
        string mode = vm?.Theme?.DisplayMode ?? "Auto";

        bool showGauges = _showingGauges;
        if (mode.Equals("Gauges", StringComparison.OrdinalIgnoreCase))
        {
            showGauges = true;
            _showingGauges = true;
            _transitionTimer.Stop();
        }
        else if (mode.Equals("Storage", StringComparison.OrdinalIgnoreCase))
        {
            showGauges = false;
            _showingGauges = false;
            _transitionTimer.Stop();
        }
        else
        {
            // Auto mode
            if (!_transitionTimer.IsEnabled) _transitionTimer.Start();
        }

        if (animate)
        {
            CrossFadeScreens(showGauges);
        }
        else
        {
            GaugesContainer.Opacity = showGauges ? 1.0 : 0.0;
            GaugesContainer.IsHitTestVisible = showGauges;
            GaugesContainer.Visibility = showGauges ? Visibility.Visible : Visibility.Hidden;

            SsdScreen.Opacity = showGauges ? 0.0 : 1.0;
            SsdScreen.IsHitTestVisible = !showGauges;
            SsdScreen.Visibility = showGauges ? Visibility.Hidden : Visibility.Visible;
        }
    }

    private void CrossFadeScreens(bool showGauges)
    {
        var duration = new Duration(TimeSpan.FromMilliseconds(800));

        if (showGauges) GaugesContainer.Visibility = Visibility.Visible;
        else SsdScreen.Visibility = Visibility.Visible;

        var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(0.0, duration);
        var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(1.0, duration);

        fadeOut.Completed += (s, e) => 
        {
            if (showGauges) SsdScreen.Visibility = Visibility.Hidden;
            else GaugesContainer.Visibility = Visibility.Hidden;
        };

        if (showGauges)
        {
            GaugesContainer.IsHitTestVisible = true;
            SsdScreen.IsHitTestVisible = false;
            
            GaugesContainer.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            SsdScreen.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }
        else
        {
            GaugesContainer.IsHitTestVisible = false;
            SsdScreen.IsHitTestVisible = true;

            GaugesContainer.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            SsdScreen.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }
    }

    private void TransitionTimer_Tick(object? sender, EventArgs e)
    {
        var vm = DataContext as MainViewModel;
        string mode = vm?.Theme?.DisplayMode ?? "Auto";

        // Flip bool only if we are in alternating rotation mode
        if (mode.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            _showingGauges = !_showingGauges;
            EvaluateScreenVisibility(animate: true);
        }
        else
        {
            // If locked in static mode, ensure visibility is forcibly applied correctly (no animation)
            EvaluateScreenVisibility(animate: false);
        }
    }

    private void DualCircularGauge_Loaded(object sender, RoutedEventArgs e)
    {
        // Added to resolve the XamlParseException for the 'Loaded' event in XAML.
        // You can add your custom loaded animations or logic here!
    }
}