using LibreHardwareMonitor.Hardware;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using PcStatsMonitor.Services;
using System.Collections.Generic;
using System;

namespace PcStatsMonitor.ViewModels;

public class CurveItemViewModel : ViewModelBase
{
    private string _name = "";
    public string Name { get => _name; set => SetProperty(ref _name, value); }

    private string _temperatureSource = "";
    public string TemperatureSource { get => _temperatureSource; set => SetProperty(ref _temperatureSource, value); }

    private int _currentRpm;
    public int CurrentRpm { get => _currentRpm; set => SetProperty(ref _currentRpm, value); }

    private int _minTemp;
    public int MinTemp { get => _minTemp; set => SetProperty(ref _minTemp, value); }

    private int _maxTemp;
    public int MaxTemp { get => _maxTemp; set => SetProperty(ref _maxTemp, value); }

    private int _minSpeed;
    public int MinSpeed { get => _minSpeed; set => SetProperty(ref _minSpeed, value); }

    private int _maxSpeed;
    public int MaxSpeed { get => _maxSpeed; set => SetProperty(ref _maxSpeed, value); }

    private int _hysteresisUp;
    public int HysteresisUp { get => _hysteresisUp; set => SetProperty(ref _hysteresisUp, value); }

    private int _hysteresisDown;
    public int HysteresisDown { get => _hysteresisDown; set => SetProperty(ref _hysteresisDown, value); }

    public ObservableCollection<string> AvailableSources { get; } = new ObservableCollection<string>();
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

    public ICommand RefreshFansCommand { get; }

    public FanControlViewModel(HardwareControlService hardwareService)
    {
        _hardwareService = hardwareService;
        RefreshFansCommand = new RelayCommand(_ => LoadFans());
    }

    public void LoadFans()
    {
        IsLoading = true;
        ShowHvciWarning = false;
        Fans.Clear();
        Curves.Clear();

        if (HardwareControlService.IsDemoMode)
        {
            var sourceOptions = new[] { "33 °C - CPU Package - Intel Core i", "40 °C - Memory - AMD Radeon RX", "Mix - Max" };
            var curve1 = new CurveItemViewModel { Name = "CPU Cooler", CurrentRpm = 850, MinTemp = 40, MaxTemp = 75, MinSpeed = 800, MaxSpeed = 1500, HysteresisUp = 3, HysteresisDown = 7 };
            foreach(var s in sourceOptions) curve1.AvailableSources.Add(s);
            curve1.TemperatureSource = sourceOptions[0];
            
            var curve2 = new CurveItemViewModel { Name = "Case Fans", CurrentRpm = 650, MinTemp = 55, MaxTemp = 70, MinSpeed = 650, MaxSpeed = 1200, HysteresisUp = 3, HysteresisDown = 7 };
            foreach(var s in sourceOptions) curve2.AvailableSources.Add(s);
            curve2.TemperatureSource = sourceOptions[2];

            var curve3 = new CurveItemViewModel { Name = "CPU -> Case", CurrentRpm = 650, MinTemp = 55, MaxTemp = 70, MinSpeed = 650, MaxSpeed = 1200, HysteresisUp = 3, HysteresisDown = 7 };
            foreach(var s in sourceOptions) curve3.AvailableSources.Add(s);
            curve3.TemperatureSource = sourceOptions[0];

            var curve4 = new CurveItemViewModel { Name = "GPU", CurrentRpm = 0, MinTemp = 50, MaxTemp = 85, MinSpeed = 0, MaxSpeed = 2000, HysteresisUp = 5, HysteresisDown = 5 };
            foreach(var s in sourceOptions) curve4.AvailableSources.Add(s);
            curve4.TemperatureSource = sourceOptions[1];

            Curves.Add(curve1);
            Curves.Add(curve2);
            Curves.Add(curve3);
            Curves.Add(curve4);
            
            Fans.Add(new FanItemViewModel(null, _hardwareService, "GPU Fan", 0f) { SelectedCurve = "GPU" });
            Fans.Add(new FanItemViewModel(null, _hardwareService, "CPU Push", 17f) { SelectedCurve = "CPU Cooler", CurrentRpm = 855 });
            Fans.Add(new FanItemViewModel(null, _hardwareService, "CPU Pull", 100f) { SelectedCurve = "CPU Cooler", CurrentRpm = 902 });
            Fans.Add(new FanItemViewModel(null, _hardwareService, "System Exhaust", 43.9f) { SelectedCurve = "Case Fans", CurrentRpm = 587 });
        }
        else
        {
            // Seed a default LIVE curve for users to assign physical fans to. 
            // In a future phase, we will load these from a saved JSON configuration.
            var liveSourceOptions = new[] { "CPU Package", "Motherboard VRM", "GPU Core", "Liquid Temp", "Mix - Max" };
            
            var liveCurve1 = new CurveItemViewModel { Name = "Default System Curve", CurrentRpm = 0, MinTemp = 40, MaxTemp = 80, MinSpeed = 30, MaxSpeed = 100, HysteresisUp = 3, HysteresisDown = 5 };
            foreach(var s in liveSourceOptions) liveCurve1.AvailableSources.Add(s);
            liveCurve1.TemperatureSource = liveSourceOptions[0];

            Curves.Add(liveCurve1);

            var sensors = _hardwareService.GetFanSensors();
            ShowHvciWarning = _hardwareService.IsHvciBlockingFans;
            
            // Add ALL fans (both read-only SensorType.Fan and adjustable SensorType.Control) 
            // so the Settings screen perfectly matches the Rotating Screen.
            foreach (var sensor in sensors)
            {
                Fans.Add(new FanItemViewModel(sensor, _hardwareService));
            }
        }

        IsLoading = false;
    }
}
