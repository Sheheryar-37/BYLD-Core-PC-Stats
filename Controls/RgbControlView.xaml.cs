using System.Windows.Controls;
using System.Windows.Input;
using PcStatsMonitor.ViewModels;

namespace PcStatsMonitor.Controls;

public partial class RgbControlView : UserControl
{
    public ICommand ChangeColorCommand { get; }
    public ICommand ApplyAllColorCommand { get; }
    public ICommand RestoreAllCommand { get; }

    public RgbControlView()
    {
        InitializeComponent();
        ChangeColorCommand = new RelayCommand(ExecuteChangeColor);
        ApplyAllColorCommand = new RelayCommand(_ => ExecuteApplyAllColor());
        RestoreAllCommand = new RelayCommand(_ => (DataContext as RgbControlViewModel)?.RestoreDeviceStates());
    }

    /// <summary>
    /// Opens the colour picker and applies the chosen colour (or gradient)
    /// to every zone of every detected RGB device.
    /// </summary>
    private void ExecuteApplyAllColor()
    {
        if (DataContext is not RgbControlViewModel vm) return;

        string hex = $"#{vm.MasterColor.R:X2}{vm.MasterColor.G:X2}{vm.MasterColor.B:X2}";
        string endHex = $"#{vm.MasterEndColor.R:X2}{vm.MasterEndColor.G:X2}{vm.MasterEndColor.B:X2}";

        var picker = new ColorPickerWindow(hex, vm.MasterIsGradient, endHex);
        if (picker.ShowDialog() != true) return;

        var newColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(picker.SelectedHex);
        var endColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(picker.GradientEndHex);
        vm.ApplyColorToAllZones(newColor, picker.IsGradient, endColor);
    }

    private void ExecuteChangeColor(object? parameter)
    {
        if (parameter is RgbZoneViewModel zone)
        {
            // Re-using the main window's ColorPicker logic but calling it here
            string hex = $"#{zone.SelectedColor.R:X2}{zone.SelectedColor.G:X2}{zone.SelectedColor.B:X2}";
            string endHex = $"#{zone.GradientEndColor.R:X2}{zone.GradientEndColor.G:X2}{zone.GradientEndColor.B:X2}";
            
            var picker = new ColorPickerWindow(hex, zone.IsGradient, endHex);
            if (picker.ShowDialog() == true)
            {
                var newColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(picker.SelectedHex);
                var endColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(picker.GradientEndHex);

                // Switch the device into a colour-capable mode first — the same step
                // apply-to-all uses — so a second pick shows even when the device is
                // sitting in an effect mode (ENE DRAM in Rainbow ignores direct LED
                // writes, which is why the client's second colour never took).
                zone.Owner?.EnsureColorCapableMode(newColor);

                // Set silently, then force ONE write — a deliberate user pick must
                // reach the hardware even if it matches the current colour.
                zone.SetColorsSilently(newColor, picker.IsGradient, endColor);
                zone.ReapplyColor();
            }
        }
    }

    // ── 7" display: which RGB devices show, and in what order (round 18, items 9 & 13) ──

    /// <summary>Shows/hides this device on the 7" RGB screen.</summary>
    private void ChkRgbShowOnWidget_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is not System.Windows.FrameworkElement fe ||
            fe.DataContext is not RgbDeviceViewModel device) return;
        ViewModel?.SetDeviceWidgetVisibility(device, !device.ShowOnWidget);
    }

    /// <summary>Moves this device one place earlier on the 7" display.</summary>
    private void BtnRgbMoveUp_Click(object sender, System.Windows.RoutedEventArgs e) => MoveDevice(sender, -1);

    /// <summary>Moves this device one place later on the 7" display.</summary>
    private void BtnRgbMoveDown_Click(object sender, System.Windows.RoutedEventArgs e) => MoveDevice(sender, 1);

    private void MoveDevice(object sender, int delta)
    {
        if (sender is not System.Windows.FrameworkElement fe ||
            fe.DataContext is not RgbDeviceViewModel device) return;
        ViewModel?.MoveDeviceOnWidget(device, delta);
    }

    private RgbControlViewModel? ViewModel => DataContext as RgbControlViewModel;

    // ── Device rename (round 22, item 5) ────────────────────────────────────

    /// <summary>Double-click a device name to rename it inline.</summary>
    private void RgbName_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && sender is System.Windows.FrameworkElement fe &&
            fe.DataContext is RgbDeviceViewModel device)
            device.IsEditingName = true;
    }

    /// <summary>Selects the whole name as soon as the rename box appears.</summary>
    private void RgbNameEdit_IsVisibleChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBox box || !box.IsVisible) return;
        box.Dispatcher.BeginInvoke(new Action(() => { box.Focus(); box.SelectAll(); }),
            System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>Enter commits the new name; Escape cancels without saving.</summary>
    private void RgbNameEdit_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) CommitRgbName(sender);
        else if (e.Key == System.Windows.Input.Key.Escape && sender is TextBox b &&
                 b.DataContext is RgbDeviceViewModel d)
            d.IsEditingName = false;
    }

    private void RgbNameEdit_LostFocus(object sender, System.Windows.RoutedEventArgs e) => CommitRgbName(sender);

    private void CommitRgbName(object sender)
    {
        // LostFocus fires again after Enter/Escape closed the editor — the guard stops a
        // second, redundant save.
        if (sender is not TextBox box || box.DataContext is not RgbDeviceViewModel device) return;
        if (!device.IsEditingName) return;
        ViewModel?.SetDeviceName(device, box.Text);
    }
}
