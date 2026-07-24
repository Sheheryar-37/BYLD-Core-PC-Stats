using System.Windows;
using System.Windows.Input;

namespace PcStatsMonitor.Controls
{
    public partial class GlassMessageBox : Window
    {
        /// <summary>
        /// Whether these dialogs render light. Set from SettingsWindow.ApplyUiTheme so the
        /// popups follow the UI theme — they were hard-coded dark and looked wrong on the
        /// light UI (client round 13, item 4). This is the box used for "Settings saved
        /// successfully!" and the other Settings confirmations.
        /// </summary>
        public static bool UseLightTheme { get; set; }

        public GlassMessageBox(string message, string title = "Notification")
        {
            InitializeComponent();
            TxtMessage.Text = message;
            TxtTitle.Text = title;
            if (UseLightTheme) ApplyLightTheme();
        }

        /// <summary>Re-skins the dialog for the light UI: pale card and dark body text.
        /// The BrandBlue title and OK button carry over unchanged.</summary>
        private void ApplyLightTheme()
        {
            RootBorder.Background = Brush("#F7F8FA");
            TxtMessage.Foreground = Brush("#1F2937");
        }

        private static System.Windows.Media.SolidColorBrush Brush(string hex) =>
            new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        public static bool? ShowDialog(Window owner, string message, string title = "Notification")
        {
            var msgBox = new GlassMessageBox(message, title) { Owner = owner };
            return msgBox.ShowDialog();
        }
    }
}
