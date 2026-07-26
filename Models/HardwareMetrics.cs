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
            bool spinChanged = (_speed > 0) != (value > 0);
            _speed = value;
            OnPropertyChanged();
            // Only re-notify the derived flags when the spinning state actually flips,
            // not every second — this list refreshes once a second for every fan.
            if (spinChanged)
            {
                OnPropertyChanged(nameof(IsSpinning));
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    /// <summary>True while the fan is turning — drives the icon animation on the 7" screen.</summary>
    public bool IsSpinning => _speed > 0;

    /// <summary>Short status shown under the fan name on the 7" System Cooling screen.</summary>
    public string StatusText => _speed > 0 ? "Spinning" : "Idle";

    private System.Windows.Media.Brush _animationBrush = System.Windows.Media.Brushes.DodgerBlue;
    /// <summary>Per-fan colour for the spinning icon on the 7" screen, so each fan is easy to
    /// tell apart. Assigned from the palette (or the user's choice) when the list is built.</summary>
    public System.Windows.Media.Brush AnimationBrush
    {
        get => _animationBrush;
        set
        {
            // The brush is reassigned every poll tick; skip the notify (and re-render) when
            // it is the same cached brush. Colours only change when the user picks a new one.
            if (ReferenceEquals(_animationBrush, value)) return;
            _animationBrush = value;
            OnPropertyChanged();
        }
    }

    /// <summary>A distinct, premium colour per fan index — cycles if there are more fans than colours.</summary>
    private static readonly string[] FanPaletteHex =
    {
        "#3B82F6", // blue
        "#22D3EE", // cyan
        "#34D399", // green
        "#F5C518", // amber
        "#F97316", // orange
        "#F43F5E", // rose
        "#A78BFA", // violet
        "#14B8A6"  // teal
    };
    private static readonly System.Windows.Media.Brush[] FanPalette = BuildFanPalette();

    private static int Wrap(int index, int length) => ((index % length) + length) % length;

    /// <summary>The default palette hex for a fan at the given position.</summary>
    public static string PaletteHex(int index) => FanPaletteHex[Wrap(index, FanPaletteHex.Length)];

    /// <summary>The default palette brush for a fan at the given position.</summary>
    public static System.Windows.Media.Brush PaletteBrush(int index) => FanPalette[Wrap(index, FanPalette.Length)];

    // Cache frozen brushes per hex so the same colour always returns the SAME reference —
    // that lets AnimationBrush skip the notify/re-render when the colour is unchanged.
    private static readonly Dictionary<string, System.Windows.Media.Brush> _brushCache = new();

    /// <summary>Returns a cached frozen brush for a hex string, falling back to blue on a bad value.</summary>
    public static System.Windows.Media.Brush BrushFromHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return System.Windows.Media.Brushes.DodgerBlue;

        lock (_brushCache)
        {
            if (_brushCache.TryGetValue(hex, out var cached)) return cached;
            var brush = BuildFrozenBrush(hex);
            _brushCache[hex] = brush;
            return brush;
        }
    }

    private static System.Windows.Media.Brush BuildFrozenBrush(string hex)
    {
        try
        {
            var brush = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }
        catch
        {
            return System.Windows.Media.Brushes.DodgerBlue;
        }
    }

    private static System.Windows.Media.Brush[] BuildFanPalette()
    {
        // Use BuildFrozenBrush, NOT BrushFromHex: this runs during static initialization of
        // FanPalette, which is declared before the _brushCache field, so the cache is still
        // null at this point. Touching it here threw in the type initializer and broke every
        // fan load (client round 15). BuildFrozenBrush has no such dependency.
        var brushes = new System.Windows.Media.Brush[FanPaletteHex.Length];
        for (int i = 0; i < FanPaletteHex.Length; i++)
            brushes[i] = BuildFrozenBrush(FanPaletteHex[i]);
        return brushes;
    }

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
