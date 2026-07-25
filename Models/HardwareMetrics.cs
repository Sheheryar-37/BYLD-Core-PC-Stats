namespace PcStatsMonitor.Models;

public class HardwareMetrics
{
    public double CpuTemp { get; set; }
    public double CpuLoad { get; set; }
    public double CpuClock { get; set; }

    public double GpuTemp { get; set; }
    public double GpuLoad { get; set; }
    public double GpuClock { get; set; }

    public double RamLoad { get; set; }
    public double RamUsedGb { get; set; }
    public double RamTotalGb { get; set; }

    public double MotherboardTemp { get; set; }
    public double FanSpeed { get; set; }

    public double NetworkUp { get; set; }
    public double NetworkDown { get; set; }

    public List<DriveMetrics> Drives { get; set; } = new();
    public List<FanMetric> Fans { get; set; } = new();
}

public class FanMetric : System.ComponentModel.INotifyPropertyChanged
{
    private string _name = string.Empty;
    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    private double _speed;
    public double Speed
    {
        get => _speed;
        set
        {
            _speed = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSpinning));
            OnPropertyChanged(nameof(StatusText));
        }
    }

    /// <summary>True while the fan is turning — drives the icon animation on the 7" screen.</summary>
    public bool IsSpinning => _speed > 0;

    /// <summary>Short status shown under the fan name on the 7" System Cooling screen.</summary>
    public string StatusText => _speed > 0 ? "Spinning" : "Idle";

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
    }
}

public class DriveMetrics
{
    public string Name { get; set; } = string.Empty;
    public string Vendor { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string SizeString { get; set; } = string.Empty;
    public double Load { get; set; }
    public double UsedSpaceGb { get; set; }
    public double TotalSpaceGb { get; set; }
}
