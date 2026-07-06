using System.Windows.Controls;
using System.Windows.Input;
using PcStatsMonitor.ViewModels;

namespace PcStatsMonitor.Controls;

public partial class RgbControlView : UserControl
{
    public ICommand ChangeColorCommand { get; }
    public ICommand ApplyAllColorCommand { get; }

    public RgbControlView()
    {
        InitializeComponent();
        ChangeColorCommand = new RelayCommand(ExecuteChangeColor);
        ApplyAllColorCommand = new RelayCommand(_ => ExecuteApplyAllColor());
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
                
                // Set these before SelectedColor so ApplyColor uses the correct gradient values
                zone.GradientEndColor = endColor;
                zone.IsGradient = picker.IsGradient;
                zone.SelectedColor = newColor; // This triggers ApplyColor
            }
        }
    }
}
