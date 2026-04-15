using System.Windows;
using System.Windows.Input;

namespace PcStatsMonitor.Controls
{
    public partial class GlassMessageBox : Window
    {
        public GlassMessageBox(string message, string title = "Notification")
        {
            InitializeComponent();
            TxtMessage.Text = message;
            TxtTitle.Text = title;
        }

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
