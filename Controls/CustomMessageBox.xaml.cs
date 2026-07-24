using System.Windows;
using System.Windows.Input;

namespace PcStatsMonitor.Controls
{
    public partial class CustomMessageBox : Window
    {
        public MessageBoxResult Result { get; private set; }

        /// <summary>
        /// Whether these dialogs render light. Set from SettingsWindow.ApplyUiTheme so the
        /// popups follow the UI theme — they were hard-coded dark and looked wrong on the
        /// light UI (client round 13, item 4).
        /// </summary>
        public static bool UseLightTheme { get; set; }

        public CustomMessageBox(string message, string title, MessageBoxButton buttons)
        {
            InitializeComponent();
            TxtMessage.Text = message;
            TxtTitle.Text = title;
            if (UseLightTheme) ApplyLightTheme();

            if (buttons == MessageBoxButton.YesNo)
            {
                BtnYes.Visibility = Visibility.Visible;
                BtnNo.Visibility = Visibility.Visible;
            }
            else
            {
                BtnOk.Visibility = Visibility.Visible;
            }
        }

        /// <summary>Re-skins the dialog for the light UI: pale card, dark text, and a
        /// legible secondary button. The BrandBlue title and primary button carry over.</summary>
        private void ApplyLightTheme()
        {
            RootBorder.Background = Brush("#F7F8FA");
            RootBorder.BorderBrush = Brush("#3b82f6");
            TxtMessage.Foreground = Brush("#1F2937");
            BtnNo.Foreground = Brush("#374151");
            BtnNo.BorderBrush = Brush("#CBD5E1");
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
            Result = MessageBoxResult.OK;
            DialogResult = true;
            Close();
        }

        private void BtnYes_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.Yes;
            DialogResult = true;
            Close();
        }

        private void BtnNo_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.No;
            DialogResult = false;
            Close();
        }

        public static MessageBoxResult ShowDialog(Window owner, string message, string title = "Notification", MessageBoxButton buttons = MessageBoxButton.OK)
        {
            var msgBox = new CustomMessageBox(message, title, buttons);
            if (owner != null && owner.IsLoaded)
            {
                msgBox.Owner = owner;
            }
            else
            {
                msgBox.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            msgBox.ShowDialog();
            return msgBox.Result;
        }
    }
}
