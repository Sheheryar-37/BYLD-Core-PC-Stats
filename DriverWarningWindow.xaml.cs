using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace PcStatsMonitor;

/// <summary>
/// Themed replacement for the stock <see cref="MessageBox"/> used when a required
/// hardware driver (PawnIO) is missing. Matches the app's dark, glassmorphic
/// design language and offers a one-click link to download the driver.
/// </summary>
public partial class DriverWarningWindow : Window
{
    private readonly string _url;

    /// <summary>Creates the dialog with a heading, body message and a download URL
    /// opened by the primary button.</summary>
    public DriverWarningWindow(string title, string message, string url)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        _url = url;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void GetPawnIo_Click(object sender, RoutedEventArgs e)
    {
        TryOpenUrl();
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void TryOpenUrl()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_url) { UseShellExecute = true });
        }
        catch
        {
            // Non-fatal: the URL is also shown in the message body as a fallback.
        }
    }
}
