using LibreHardwareMonitor.Hardware;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using PcStatsMonitor.Services;
using System.Collections.Generic;
using System;
using System.IO;
using System.Text.Json;
using System.Windows.Media;

namespace PcStatsMonitor.ViewModels;

/// <summary>
/// Defines the type of fan curve interpolation.
/// </summary>
public enum CurveType
{
    /// <summary>Linear ramp from MinSpeed% at MinTemp to MaxSpeed% at MaxTemp.</summary>
    Linear,
    /// <summary>Custom graph-based curve (future: user-editable control points).</summary>
    Custom
}

public class CurveItemViewModel : ViewModelBase
{
    private string _name = "";
    public string Name { get => _name; set => SetProperty(ref _name, value); }

    private string _temperatureSource = "";
    public string TemperatureSource { get => _temperatureSource; set => SetProperty(ref _temperatureSource, value); }

    private int _currentRpm;
    public int CurrentRpm { get => _currentRpm; set => SetProperty(ref _currentRpm, value); }

    private int _minTemp;
    public int MinTemp
    {
        get => _minTemp;
        set { if (SetProperty(ref _minTemp, value)) RecalculateGraphPoints(); }
    }

    private int _maxTemp;
    public int MaxTemp
    {
        get => _maxTemp;
        set { if (SetProperty(ref _maxTemp, value)) RecalculateGraphPoints(); }
    }

    private int _minSpeed;
    /// <summary>Minimum fan speed as a percentage (0–100).</summary>
    public int MinSpeed
    {
        get => _minSpeed;
        set { if (SetProperty(ref _minSpeed, value)) RecalculateGraphPoints(); }
    }

    private int _maxSpeed;
    /// <summary>Maximum fan speed as a percentage (0–100).</summary>
    public int MaxSpeed
    {
        get => _maxSpeed;
        set { if (SetProperty(ref _maxSpeed, value)) RecalculateGraphPoints(); }
    }

    private int _hysteresisUp;
    public int HysteresisUp { get => _hysteresisUp; set => SetProperty(ref _hysteresisUp, value); }

    private int _hysteresisDown;
    public int HysteresisDown { get => _hysteresisDown; set => SetProperty(ref _hysteresisDown, value); }

    private CurveType _curveType = CurveType.Linear;
    /// <summary>
    /// Determines the interpolation mode for this curve.
    /// Linear = straight ramp, Custom = user-defined control points.
    /// </summary>
    public CurveType CurveType
    {
        get => _curveType;
        set { if (SetProperty(ref _curveType, value)) RecalculateGraphPoints(); }
    }

    private PointCollection _graphPoints = new();
    /// <summary>
    /// Pre-computed polyline points for the curve graph visualization.
    /// Automatically recalculated when MinTemp, MaxTemp, MinSpeed%, or MaxSpeed% change.
    /// </summary>
    public PointCollection GraphPoints
    {
        get => _graphPoints;
        private set => SetProperty(ref _graphPoints, value);
    }

    public ObservableCollection<string> AvailableSources { get; } = new ObservableCollection<string>();

    /// <summary>
    /// Recalculates the polyline points for the fan curve mini-graph.
    /// Graph canvas is ~190px wide × 55px tall.
    /// </summary>
    private void RecalculateGraphPoints()
    {
        const double graphWidth = 180.0;
        const double graphHeight = 48.0;
        const double padLeft = 5.0;
        const double padTop = 3.0;

        double clampedMinSpeed = Math.Clamp(_minSpeed, 0, 100);
        double clampedMaxSpeed = Math.Clamp(_maxSpeed, 0, 100);

        // Y axis: 0% speed = bottom (graphHeight), 100% speed = top (0)
        double yMin = padTop + graphHeight - (clampedMinSpeed / 100.0 * graphHeight);
        double yMax = padTop + graphHeight - (clampedMaxSpeed / 100.0 * graphHeight);

        var points = new PointCollection();

        if (CurveType == CurveType.Linear)
        {
            // Flat at MinSpeed from left edge to MinTemp zone, then linear ramp, then flat at MaxSpeed
            double xStart = padLeft;
            double xRampStart = padLeft + graphWidth * 0.25;
            double xRampEnd = padLeft + graphWidth * 0.70;
            double xEnd = padLeft + graphWidth;

            points.Add(new System.Windows.Point(xStart, yMin));
            points.Add(new System.Windows.Point(xRampStart, yMin));
            points.Add(new System.Windows.Point(xRampEnd, yMax));
            points.Add(new System.Windows.Point(xEnd, yMax));
        }
        else
        {
            // Custom: S-curve approximation using 5 control points
            double xStart = padLeft;
            double x1 = padLeft + graphWidth * 0.20;
            double x2 = padLeft + graphWidth * 0.40;
            double x3 = padLeft + graphWidth * 0.65;
            double xEnd = padLeft + graphWidth;

            double yMid = (yMin + yMax) / 2.0;

            points.Add(new System.Windows.Point(xStart, yMin));
            points.Add(new System.Windows.Point(x1, yMin));
            points.Add(new System.Windows.Point(x2, yMid));
            points.Add(new System.Windows.Point(x3, yMax));
            points.Add(new System.Windows.Point(xEnd, yMax));
        }

        GraphPoints = points;
    }

    /// <summary>
    /// Initializes default graph points after construction.
    /// Must be called after all properties are set.
    /// </summary>
    public void InitializeGraph()
    {
        RecalculateGraphPoints();
    }
}

public class FanItemViewModel : ViewModelBase
{
    private readonly HardwareControlService _hardwareService;
    public ISensor? Sensor { get; }
    
    private string _demoName = "Fan";
    public string Name => Sensor?.Name ?? _demoName;
    public string Identifier => Sensor?.Identifier.ToString() ?? "demo_fan";

    private bool _isManual;
    public bool IsManual
    {
        get => _isManual;
        set
        {
            if (SetProperty(ref _isManual, value))
            {
                if (Sensor != null)
                {
                    if (!value)
                    {
                        _hardwareService.SetFanAuto(Sensor);
                    }
                    else
                    {
                        _hardwareService.SetFanSpeed(Sensor, SpeedPercentage);
                    }
                }
                OnPropertyChanged(nameof(IsAuto));
            }
        }
    }

    public bool IsAuto
    {
        get => !IsManual;
        set => IsManual = !value;
    }

    private float _speedPercentage;
    public float SpeedPercentage
    {
        get => _speedPercentage;
        set
        {
            if (SetProperty(ref _speedPercentage, value))
            {
                if (IsManual && Sensor != null)
                {
                    _hardwareService.SetFanSpeed(Sensor, value);
                }
            }
        }
    }

    private int _currentRpm;
    public int CurrentRpm
    {
        get => _currentRpm;
        set
        {
            if (SetProperty(ref _currentRpm, value))
                OnPropertyChanged(nameof(IsZeroRpm));
        }
    }

    /// <summary>
    /// Returns true when the fan is reporting 0 RPM (e.g. GPU zero-fan mode at idle).
    /// </summary>
    public bool IsZeroRpm => CurrentRpm == 0;

    private string _selectedCurve = "";
    public string SelectedCurve { get => _selectedCurve; set => SetProperty(ref _selectedCurve, value); }

    public ObservableCollection<string> AvailableCurves { get; } = new ObservableCollection<string>();

    private int _stepUp;
    public int StepUp { get => _stepUp; set => SetProperty(ref _stepUp, value); }

    private int _stepDown;
    public int StepDown { get => _stepDown; set => SetProperty(ref _stepDown, value); }

    private int _startPercentage;
    public int StartPercentage { get => _startPercentage; set => SetProperty(ref _startPercentage, value); }

    private int _stopPercentage;
    public int StopPercentage { get => _stopPercentage; set => SetProperty(ref _stopPercentage, value); }

    private int _offset;
    public int Offset { get => _offset; set => SetProperty(ref _offset, value); }

    private int _minimumPercentage;
    public int MinimumPercentage { get => _minimumPercentage; set => SetProperty(ref _minimumPercentage, value); }

    public FanItemViewModel(ISensor? sensor, HardwareControlService hardwareService, string demoName = "Fan", float demoSpeed = 50f)
    {
        Sensor = sensor;
        _hardwareService = hardwareService;
        _demoName = demoName;
        
        // Try to read current value if available
        if (sensor != null && sensor.Value.HasValue)
        {
            _speedPercentage = sensor.Value.Value;
        }
        else
        {
            _speedPercentage = demoSpeed;
        }
        _isManual = false; // Default to auto

        // Mock defaults
        CurrentRpm = (int)(_speedPercentage * 25); 
        StepUp = 8;
        StepDown = 8;
        StartPercentage = 9;
        StopPercentage = 6;
        MinimumPercentage = 0;
        Offset = 0;
        
        AvailableCurves.Add("GPU");
        AvailableCurves.Add("CPU Cooler");
        AvailableCurves.Add("Case Fans");
        SelectedCurve = AvailableCurves[0];
    }
}

public class FanControlViewModel : ViewModelBase
{
    private readonly HardwareControlService _hardwareService;

    public ObservableCollection<FanItemViewModel> Fans { get; } = new();
    public ObservableCollection<CurveItemViewModel> Curves { get; } = new();

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    private bool _showHvciWarning;
    public bool ShowHvciWarning
    {
        get => _showHvciWarning;
        set => SetProperty(ref _showHvciWarning, value);
    }

    /// <summary>
    /// Provides context-specific detection status for the fan warning banner.
    /// </summary>
    private string _detectionStatusMessage = "";
    public string DetectionStatusMessage
    {
        get => _detectionStatusMessage;
        set => SetProperty(ref _detectionStatusMessage, value);
    }

    public ICommand RefreshFansCommand { get; }

    /// <summary>
    /// Adds a new user-defined fan curve with default values.
    /// </summary>
    public ICommand AddCurveCommand { get; }

    /// <summary>
    /// Deletes a fan curve by removing it from the collection and persisting.
    /// The command parameter is the <see cref="CurveItemViewModel"/> to remove.
    /// </summary>
    public ICommand DeleteCurveCommand { get; }

    public FanControlViewModel(HardwareControlService hardwareService)
    {
        _hardwareService = hardwareService;
        RefreshFansCommand = new RelayCommand(_ => LoadFans());
        AddCurveCommand = new RelayCommand(_ => AddNewCurve());
        DeleteCurveCommand = new RelayCommand(param => DeleteCurve(param as CurveItemViewModel));

        if (!HardwareControlService.IsDemoMode)
        {
            _pollTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1.5)
            };
            _pollTimer.Tick += (s, e) => PollSensors();
            _pollTimer.Start();
        }
    }

    private System.Windows.Threading.DispatcherTimer? _pollTimer;

    /// <summary>Stops the sensor polling timer. Call when the hosting view closes.</summary>
    public void StopPolling()
    {
        _pollTimer?.Stop();
    }

    private void PollSensors()
    {
        if (IsLoading || Fans.Count == 0) return;
        
        try
        {
            foreach (var fan in Fans)
            {
                if (fan.Sensor != null)
                {
                    // Update the parent hardware to refresh the sensor value
                    fan.Sensor.Hardware.Update();
                    
                    if (fan.Sensor.Value.HasValue)
                    {
                        if (fan.Sensor.SensorType == LibreHardwareMonitor.Hardware.SensorType.Fan)
                        {
                            fan.CurrentRpm = (int)fan.Sensor.Value.Value;
                        }
                        else if (fan.Sensor.SensorType == LibreHardwareMonitor.Hardware.SensorType.Control && !fan.IsManual)
                        {
                            fan.SpeedPercentage = fan.Sensor.Value.Value;
                        }
                    }
                }
            }
        }
        catch { /* Silent fail for polling errors */ }
    }

    /// <summary>
    /// Creates a new curve with sensible defaults and adds it to the collection.
    /// </summary>
    private void AddNewCurve()
    {
        int nextIndex = Curves.Count + 1;
        var sourceOptions = GetAvailableSourceOptions();

        var newCurve = new CurveItemViewModel
        {
            Name = $"Custom Curve {nextIndex}",
            CurrentRpm = 0,
            MinTemp = 40,
            MaxTemp = 80,
            MinSpeed = 20,
            MaxSpeed = 100,
            HysteresisUp = 3,
            HysteresisDown = 5,
            CurveType = CurveType.Linear
        };

        foreach (var s in sourceOptions) newCurve.AvailableSources.Add(s);
        newCurve.TemperatureSource = sourceOptions.FirstOrDefault() ?? "CPU Package";
        newCurve.InitializeGraph();

        Curves.Add(newCurve);
        FanCurvePersistence.SaveCurves(Curves);
    }

    /// <summary>
    /// Removes a curve from the collection and persists the change.
    /// </summary>
    private void DeleteCurve(CurveItemViewModel? curve)
    {
        if (curve == null) return;
        Curves.Remove(curve);
        FanCurvePersistence.SaveCurves(Curves);
    }

    /// <summary>
    /// Creates a deep copy of an existing curve and adds it to the collection.
    /// </summary>
    public void DuplicateCurve(CurveItemViewModel source)
    {
        var sourceOptions = GetAvailableSourceOptions();

        var clone = new CurveItemViewModel
        {
            Name = $"{source.Name} (Copy)",
            CurrentRpm = 0,
            MinTemp = source.MinTemp,
            MaxTemp = source.MaxTemp,
            MinSpeed = source.MinSpeed,
            MaxSpeed = source.MaxSpeed,
            HysteresisUp = source.HysteresisUp,
            HysteresisDown = source.HysteresisDown,
            CurveType = source.CurveType
        };

        foreach (var s in sourceOptions) clone.AvailableSources.Add(s);
        clone.TemperatureSource = source.TemperatureSource;
        clone.InitializeGraph();

        Curves.Add(clone);
        FanCurvePersistence.SaveCurves(Curves);
    }

    /// <summary>
    /// Returns the list of available temperature source labels.
    /// </summary>
    private string[] GetAvailableSourceOptions()
    {
        if (HardwareControlService.IsDemoMode)
        {
            return new[] { "33 °C - CPU Package - Intel Core i", "40 °C - Memory - AMD Radeon RX", "Mix - Max" };
        }

        return new[] { "CPU Package", "Motherboard VRM", "GPU Core", "Liquid Temp", "Mix - Max" };
    }

    public void LoadFans()
    {
        IsLoading = true;
        ShowHvciWarning = false;
        DetectionStatusMessage = "";
        Fans.Clear();
        Curves.Clear();

        if (HardwareControlService.IsDemoMode)
        {
            LoadDemoFans();
        }
        else
        {
            LoadLiveFans();
        }

        IsLoading = false;
    }

    /// <summary>
    /// Populates fans and curves with demo/mock data for UI development.
    /// </summary>
    private void LoadDemoFans()
    {
        var sourceOptions = GetAvailableSourceOptions();

        var curve1 = new CurveItemViewModel { Name = "CPU Cooler", CurrentRpm = 850, MinTemp = 40, MaxTemp = 75, MinSpeed = 30, MaxSpeed = 80, HysteresisUp = 3, HysteresisDown = 7 };
        foreach (var s in sourceOptions) curve1.AvailableSources.Add(s);
        curve1.TemperatureSource = sourceOptions[0];
        curve1.InitializeGraph();

        var curve2 = new CurveItemViewModel { Name = "Case Fans", CurrentRpm = 650, MinTemp = 55, MaxTemp = 70, MinSpeed = 40, MaxSpeed = 85, HysteresisUp = 3, HysteresisDown = 7, CurveType = CurveType.Linear };
        foreach (var s in sourceOptions) curve2.AvailableSources.Add(s);
        curve2.TemperatureSource = sourceOptions[2];
        curve2.InitializeGraph();

        var curve3 = new CurveItemViewModel { Name = "CPU -> Case", CurrentRpm = 650, MinTemp = 55, MaxTemp = 70, MinSpeed = 35, MaxSpeed = 75, HysteresisUp = 3, HysteresisDown = 7, CurveType = CurveType.Custom };
        foreach (var s in sourceOptions) curve3.AvailableSources.Add(s);
        curve3.TemperatureSource = sourceOptions[0];
        curve3.InitializeGraph();

        var curve4 = new CurveItemViewModel { Name = "GPU", CurrentRpm = 0, MinTemp = 50, MaxTemp = 85, MinSpeed = 0, MaxSpeed = 100, HysteresisUp = 5, HysteresisDown = 5 };
        foreach (var s in sourceOptions) curve4.AvailableSources.Add(s);
        curve4.TemperatureSource = sourceOptions[1];
        curve4.InitializeGraph();

        Curves.Add(curve1);
        Curves.Add(curve2);
        Curves.Add(curve3);
        Curves.Add(curve4);

        Fans.Add(new FanItemViewModel(null, _hardwareService, "GPU Fan", 0f) { SelectedCurve = "GPU" });
        Fans.Add(new FanItemViewModel(null, _hardwareService, "CPU Push", 17f) { SelectedCurve = "CPU Cooler", CurrentRpm = 855 });
        Fans.Add(new FanItemViewModel(null, _hardwareService, "CPU Pull", 100f) { SelectedCurve = "CPU Cooler", CurrentRpm = 902 });
        Fans.Add(new FanItemViewModel(null, _hardwareService, "System Exhaust", 43.9f) { SelectedCurve = "Case Fans", CurrentRpm = 587 });
    }

    /// <summary>
    /// Loads live fans from hardware and restores persisted curves.
    /// </summary>
    private void LoadLiveFans()
    {
        var liveSourceOptions = GetAvailableSourceOptions();

        // Try to restore saved curves first
        var savedCurves = FanCurvePersistence.LoadCurves();
        if (savedCurves != null && savedCurves.Count > 0)
        {
            foreach (var saved in savedCurves)
            {
                var curve = new CurveItemViewModel
                {
                    Name = saved.Name,
                    CurrentRpm = 0,
                    MinTemp = saved.MinTemp,
                    MaxTemp = saved.MaxTemp,
                    MinSpeed = saved.MinSpeed,
                    MaxSpeed = saved.MaxSpeed,
                    HysteresisUp = saved.HysteresisUp,
                    HysteresisDown = saved.HysteresisDown,
                    CurveType = saved.CurveType
                };
                foreach (var s in liveSourceOptions) curve.AvailableSources.Add(s);
                curve.TemperatureSource = saved.TemperatureSource ?? liveSourceOptions[0];
                curve.InitializeGraph();
                Curves.Add(curve);
            }
        }
        else
        {
            // First run — create a sensible default curve
            var liveCurve1 = new CurveItemViewModel
            {
                Name = "Default System Curve",
                CurrentRpm = 0,
                MinTemp = 40,
                MaxTemp = 80,
                MinSpeed = 30,
                MaxSpeed = 100,
                HysteresisUp = 3,
                HysteresisDown = 5,
                CurveType = CurveType.Linear
            };
            foreach (var s in liveSourceOptions) liveCurve1.AvailableSources.Add(s);
            liveCurve1.TemperatureSource = liveSourceOptions[0];
            liveCurve1.InitializeGraph();

            Curves.Add(liveCurve1);
        }

        var sensors = _hardwareService.GetFanSensors();
        ShowHvciWarning = _hardwareService.FanStatus != FanDetectionStatus.Success;

        switch (_hardwareService.FanStatus)
        {
            case FanDetectionStatus.DriverBlocked:
                DetectionStatusMessage = "The hardware driver (WinRing0x64.sys) needed to read your motherboard's fan headers could not be loaded this session.\n\n" +
                                         "1. Close other hardware tools (OpenRGB, RGB or monitoring utilities) and restart BYLD Core.\n" +
                                         "2. If it persists, restart your PC and launch BYLD Core before any other hardware tool.\n" +
                                         "3. Also check Windows Security → Protection History and allow any blocked driver entries.";
                break;
            case FanDetectionStatus.SuperIoUnsupported:
                DetectionStatusMessage = "Your motherboard's I/O chip is not yet supported for direct case fan monitoring. GPU fan controls (if available) will still work. This is a hardware compatibility limitation, not a configuration issue.";
                break;
            case FanDetectionStatus.NoFansDetected:
                DetectionStatusMessage = "The system successfully scanned for fans, but none were detected. Make sure your fans are connected directly to the motherboard headers, not to a proprietary USB hub (like Corsair Commander).";
                break;
            default:
                DetectionStatusMessage = "";
                ShowHvciWarning = false;
                break;
        }

        // Add ALL fans (both read-only SensorType.Fan and adjustable SensorType.Control) 
        // so the Settings screen perfectly matches the Rotating Screen.
        foreach (var sensor in sensors)
        {
            Fans.Add(new FanItemViewModel(sensor, _hardwareService));
        }
    }
}

/// <summary>
/// Persists fan curve configurations to a local JSON file so they survive
/// application restarts. Curves are saved on every add/delete and restored
/// when the fan control panel is loaded.
/// </summary>
public static class FanCurvePersistence
{
    private static readonly string SettingsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings");
    private static readonly string CurveFile = Path.Combine(SettingsDir, "fan_curves.json");

    /// <summary>
    /// Saves all curves to disk as JSON.
    /// </summary>
    public static void SaveCurves(ObservableCollection<CurveItemViewModel> curves)
    {
        try
        {
            var state = curves.Select(c => new FanCurveState
            {
                Name = c.Name,
                TemperatureSource = c.TemperatureSource,
                MinTemp = c.MinTemp,
                MaxTemp = c.MaxTemp,
                MinSpeed = c.MinSpeed,
                MaxSpeed = c.MaxSpeed,
                HysteresisUp = c.HysteresisUp,
                HysteresisDown = c.HysteresisDown,
                CurveType = c.CurveType
            }).ToList();

            if (!Directory.Exists(SettingsDir))
                Directory.CreateDirectory(SettingsDir);

            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(CurveFile, json);
        }
        catch
        {
            // Persistence is best-effort; hardware control must never crash
        }
    }

    /// <summary>
    /// Loads previously saved curves from disk.
    /// Returns null if no saved state exists.
    /// </summary>
    public static List<FanCurveState>? LoadCurves()
    {
        if (!File.Exists(CurveFile)) return null;

        try
        {
            var json = File.ReadAllText(CurveFile);
            return JsonSerializer.Deserialize<List<FanCurveState>>(json);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// JSON-serializable state for a single fan curve configuration.
/// </summary>
public class FanCurveState
{
    public string Name { get; set; } = "";
    public string? TemperatureSource { get; set; }
    public int MinTemp { get; set; }
    public int MaxTemp { get; set; }
    /// <summary>Minimum fan speed as percentage (0–100).</summary>
    public int MinSpeed { get; set; }
    /// <summary>Maximum fan speed as percentage (0–100).</summary>
    public int MaxSpeed { get; set; }
    public int HysteresisUp { get; set; }
    public int HysteresisDown { get; set; }
    public CurveType CurveType { get; set; }
}
