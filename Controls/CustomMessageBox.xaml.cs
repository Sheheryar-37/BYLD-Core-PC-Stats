using System.Windows;
using System.Windows.Input;

namespace PcStatsMonitor.Controls
{
    public partial class CustomMessageBox : Window
    {
        public MessageBoxResult Result { get; private set; }

        public CustomMessageBox(string message, string title, MessageBoxButton buttons)
        {
            InitializeComponent();
            TxtMessage.Text = message;
            TxtTitle.Text = title;

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
