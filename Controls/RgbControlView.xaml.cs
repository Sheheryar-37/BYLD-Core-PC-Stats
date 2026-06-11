using System.Windows.Controls;
using System.Windows.Input;
using PcStatsMonitor.ViewModels;

namespace PcStatsMonitor.Controls;

public partial class RgbControlView : UserControl
{
    public ICommand ChangeColorCommand { get; }

    public RgbControlView()
    {
        InitializeComponent();
        ChangeColorCommand = new RelayCommand(ExecuteChangeColor);
    }

    private void ExecuteChangeColor(object? parameter)
    {
        if (parameter is RgbZoneViewModel zone)
        {
            // Re-using the main window's ColorPicker logic but calling it here
            string hex = $"#{zone.SelectedColor.A:X2}{zone.SelectedColor.R:X2}{zone.SelectedColor.G:X2}{zone.SelectedColor.B:X2}";
            var picker = new ColorPickerWindow(hex);
            if (picker.ShowDialog() == true)
            {
                var newColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(picker.SelectedHex);
                zone.SelectedColor = newColor;
            }
        }
    }
}
