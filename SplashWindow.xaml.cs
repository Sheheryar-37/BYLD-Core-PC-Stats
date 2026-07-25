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
    /// Caps how large the branding may render. The window still covers the whole 7"
    /// display (the client asked for edge-to-edge black), but letting the Viewbox scale a
    /// 56px logo across a 1200x2048 panel made the loading screen look enormous compared
    /// with previous builds (round 14, item 1). Capping keeps the artwork at a sensible
    /// size and crisp, without shrinking the window itself.
    /// </summary>
    public void CapBrandingSize(double maxWidth, double maxHeight)
    {
        BrandingViewbox.MaxWidth  = maxWidth;
        BrandingViewbox.MaxHeight = maxHeight;
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
        splash.Loaded += (s, e) =>
        {
            splash.FillScreen(screen);
            // Cover the panel, but keep the branding near its designed size rather than
            // scaling it across the whole 7" display.
            splash.CapBrandingSize(300, 260);
        };
        return splash;
    }

    /// <summary>
    /// Covers the given screen exactly, matching the size the MAIN window ends up at so the
    /// splash and the app that replaces it are identical in height and width.
    ///
    /// Sizes to the full monitor BOUNDS, not the working area. The main window is a
    /// transparent borderless window whose Maximize covers the whole monitor (over the
    /// taskbar) — 1200x2048 on the client's 7". This splash is opaque, so Maximize stopped
    /// at the working area (1200x1952) and left it ~96px shorter. Setting the full bounds
    /// explicitly (Topmost keeps it above the taskbar) makes the two match exactly.
    /// </summary>
    private void FillScreen(System.Windows.Forms.Screen screen)
    {
        var source = PresentationSource.FromVisual(this);
        double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

        WindowState = WindowState.Normal;
        Left   = screen.Bounds.Left   / dpiX;
        Top    = screen.Bounds.Top    / dpiY;
        Width  = screen.Bounds.Width  / dpiX;
        Height = screen.Bounds.Height / dpiY;
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
