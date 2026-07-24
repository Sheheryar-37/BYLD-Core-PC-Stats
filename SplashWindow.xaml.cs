using System;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace PcStatsMonitor;

/// <summary>
/// Premium loading/splash screen displayed during application startup.
/// Shows the BYLD logo with a pulsing glow animation and a thin progress bar.
/// Minimum display time is 2 seconds to avoid a jarring flash.
/// </summary>
public partial class SplashWindow : Window
{
    private readonly DispatcherTimer _progressTimer;
    private double _currentProgress = 0;
    private readonly DateTime _showTime;

    /// <summary>
    /// Minimum time (in seconds) the splash screen remains visible.
    /// Prevents the screen from flashing away too quickly on fast machines.
    /// </summary>
    private const double MinDisplaySeconds = 2.0;

    /// <summary>
    /// Scales the branding to a fixed share of the window instead of filling it.
    /// Filling a tall window upscaled the 56px logo until it looked pixelated; leaving
    /// it at natural size left a large window with tiny text. Capping it keeps the
    /// proportions balanced and the artwork crisp. The 7" secondary splash keeps the
    /// default Uniform stretch so its branding fills that screen.
    /// </summary>
    public void ScaleBrandingToWindow(double widthFraction, double heightFraction)
    {
        BrandingViewbox.MaxWidth  = Width * widthFraction;
        BrandingViewbox.MaxHeight = Height * heightFraction;
    }

    public SplashWindow()
    {
        InitializeComponent();
        _showTime = DateTime.UtcNow;

        // Fade in the status text once loaded
        Loaded += (s, e) =>
        {
            var fadeIn = (Storyboard)FindResource("StatusFadeIn");
            fadeIn.Begin();
        };

        // Animate the progress bar to simulate loading progress
        _progressTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _progressTimer.Tick += ProgressTimer_Tick;
        _progressTimer.Start();
    }

    /// <summary>
    /// Animates the progress bar forward in small increments.
    /// Slows down as it approaches 80% (waiting for actual loading to complete).
    /// </summary>
    private void ProgressTimer_Tick(object? sender, EventArgs e)
    {
        // Asymptotic approach: fast at first, slows near 80%
        double remaining = 130.0 - _currentProgress;
        double step = Math.Max(0.3, remaining * 0.04);
        _currentProgress = Math.Min(130.0, _currentProgress + step);

        ProgressBar.Width = _currentProgress;
    }

    /// <summary>
    /// Updates the status text shown below the logo.
    /// </summary>
    public void SetStatus(string message)
    {
        StatusText.Text = message;
    }

    /// <summary>
    /// Creates a full-screen splash on the first non-primary display (e.g. the
    /// 7" secondary screen) so it shows branded loading instead of the desktop.
    /// Returns null when no secondary display is connected.
    /// </summary>
    public static SplashWindow? TryCreateForSecondaryDisplay()
    {
        var screen = System.Windows.Forms.Screen.AllScreens.FirstOrDefault(s => !s.Primary);
        if (screen == null) return null;

        // Position AND maximise inside the Loaded handler — not before Show — so it
        // matches the main window's proven placement (MainWindow.SnapToInternalMonitor).
        // Doing it earlier read the primary monitor's DPI and maximised from an
        // unplaced position, which left the 7" splash short of the edges on load.
        var splash = new SplashWindow { WindowStartupLocation = WindowStartupLocation.Manual };
        splash.Loaded += (s, e) => splash.FillScreen(screen);
        return splash;
    }

    /// <summary>
    /// Fills the given screen exactly, using the per-monitor DPI read from THIS
    /// window's own presentation source (as the main window does). Placing the
    /// window on the target monitor first, then maximising, makes WPF choose the
    /// correct monitor and cover it edge to edge.
    /// </summary>
    private void FillScreen(System.Windows.Forms.Screen screen)
    {
        var source = PresentationSource.FromVisual(this);
        double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

        WindowState = WindowState.Normal;
        Left   = screen.WorkingArea.Left   / dpiX;
        Top    = screen.WorkingArea.Top    / dpiY;
        Width  = screen.WorkingArea.Width  / dpiX;
        Height = screen.WorkingArea.Height / dpiY;
        WindowState = WindowState.Maximized;
    }

    /// <summary>
    /// Completes the progress bar animation and fades out the splash screen.
    /// Ensures the minimum display time is respected.
    /// </summary>
    public void FinishAndClose()
    {
        _progressTimer.Stop();

        // Ensure minimum display time
        double elapsed = (DateTime.UtcNow - _showTime).TotalSeconds;
        double waitMs = Math.Max(0, (MinDisplaySeconds - elapsed) * 1000);

        var closeTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(waitMs)
        };
        closeTimer.Tick += (s, e) =>
        {
            closeTimer.Stop();

            // Snap progress to full
            ProgressBar.Width = 160;

            // Fade out the entire window
            var fadeOut = new DoubleAnimation(1.0, 0.0, new Duration(TimeSpan.FromMilliseconds(400)));
            fadeOut.Completed += (s2, e2) => Close();
            BeginAnimation(OpacityProperty, fadeOut);
        };
        closeTimer.Start();
    }
}
