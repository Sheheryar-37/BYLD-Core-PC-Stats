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

    /// <summary>
    /// Maps a temperature to a fan speed percentage along this curve —
    /// linear interpolation between (MinTemp, MinSpeed) and (MaxTemp, MaxSpeed).
    /// </summary>
    public float EvaluateSpeed(float temperature)
    {
        if (MaxTemp <= MinTemp) return MaxSpeed;
        if (temperature <= MinTemp) return MinSpeed;
        if (temperature >= MaxTemp) return MaxSpeed;

        float t = (temperature - MinTemp) / (float)(MaxTemp - MinTemp);
        return MinSpeed + t * (MaxSpeed - MinSpeed);
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

    /// <summary>Optional paired RPM (tach) sensor from the same hardware as the control.</summary>
    public ISensor? RpmSensor { get; set; }

    /// <summary>The last speed % the curve engine actually wrote to this fan, and
    /// when — used to rate-limit and deadband writes so the GPU driver isn't
    /// hammered every tick (client round 9: "Fan Control severely throttles my PC").</summary>
    public float LastCurveTarget { get; set; } = float.NaN;
    public DateTime LastCurveWriteUtc { get; set; } = DateTime.MinValue;

    /// <summary>Consecutive writes where the control read back ~0 despite a &gt;0
    /// command — the hardware is ignoring us (client's RX 9070). After enough of
    /// these the engine stops writing to spare the driver (which throttles the PC).</summary>
    public int UnresponsiveWrites { get; set; }

    /// <summary>True once the fan is confirmed to ignore control writes; the engine
    /// stops driving it and the card is released to its own automatic control.</summary>
    public bool ControlDisabled { get; set; }

    private string _iconColorHex = "#3B82F6";
    /// <summary>Hex colour of this fan's icon on the 7" screen. Shown on the picker swatch
    /// in Fan Control and persisted per fan (client round 15: pick a colour per fan).</summary>
    public string IconColorHex
    {
        get => _iconColorHex;
        set
        {
            if (SetProperty(ref _iconColorHex, value))
                OnPropertyChanged(nameof(IconColorBrush));
        }
    }

    /// <summary>The swatch brush for <see cref="IconColorHex"/>.</summary>
    public System.Windows.Media.Brush IconColorBrush => Models.FanMetric.BrushFromHex(IconColorHex);

    private string _demoName = "Fan";

    /// <summary>The hardware sensor name — the STABLE key used to persist this fan's
    /// colour and custom name. Never changes when the user renames the fan.</summary>
    public string Name => Sensor?.Name ?? _demoName;
    public string Identifier => Sensor?.Identifier.ToString() ?? "demo_fan";

    private string? _customName;

    /// <summary>What the user sees for this fan: their custom label when set, otherwise the
    /// hardware sensor name. Bound by the Fan Control card (client round 17, item 3).</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(_customName) ? Name : _customName!;

    /// <summary>Applies a saved custom name (from the theme) without re-persisting it.</summary>
    public void SetCustomNameQuiet(string? customName)
    {
        _customName = customName;
        OnPropertyChanged(nameof(DisplayName));
    }

    private bool _showOnWidget = true;
    /// <summary>Whether this fan appears on the 7" System Cooling widget. Unused/empty headers
    /// can be hidden there while still showing in Fan Control (client round 18, item 9b).</summary>
    public bool ShowOnWidget
    {
        get => _showOnWidget;
        set => SetProperty(ref _showOnWidget, value);
    }

    private bool _isEditingName;
    /// <summary>True while the name field is in inline-edit mode on the card.</summary>
    public bool IsEditingName
    {
        get => _isEditingName;
        set
        {
            if (SetProperty(ref _isEditingName, value) && value)
                EditableName = DisplayName;
        }
    }

    private string _editableName = string.Empty;
    /// <summary>The working text while renaming; committed by the view through SetFanName.</summary>
    public string EditableName
    {
        get => _editableName;
        set => SetProperty(ref _editableName, value);
    }

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

    /// <summary>
    /// Explains a 0 RPM reading in terms of THIS fan's hardware. A graphics card idling
    /// with its fans stopped is normal; a motherboard header reading zero means no
    /// tachometer signal is reaching the board. The GPU wording used to be shown for every
    /// fan, including the client's case-fan headers (round 14, item 5).
    /// </summary>
    public string ZeroRpmHint => IsGpuFan
        ? "GPU is in zero-fan mode (normal at low temps)"
        : "No RPM signal on this header — the fan may be unplugged, or wired to a fan hub/PSU rather than the motherboard.";

    private bool IsGpuFan =>
        Sensor?.Hardware.HardwareType is HardwareType.GpuAmd
            or HardwareType.GpuNvidia
            or HardwareType.GpuIntel;

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
        
        // Demo fans get placeholder curve names; live fans are filled by
        // FanControlViewModel.SyncFanCurveLists() with the real curve names.
        if (sensor == null)
        {
            AvailableCurves.Add("GPU");
            AvailableCurves.Add("CPU Cooler");
            AvailableCurves.Add("Case Fans");
            SelectedCurve = AvailableCurves[0];
        }
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

    private readonly IThemeService? _themeService;

    private bool _appControlsFans;

    /// <summary>
    /// Master switch. ON: the app drives the fans from their curves. OFF: every fan this
    /// app took over is handed straight back to the motherboard's own control.
    ///
    /// Deliberately NOT persisted — it resets to false on every launch and on exit, so a
    /// crash, restart or stopped app can never leave the machine's cooling stranded under
    /// software control (the client's fans were left stopped at 30%). The user opts in
    /// each session.
    /// </summary>
    public bool AppControlsFans
    {
        get => _appControlsFans;
        set
        {
            if (_appControlsFans == value) return;
            _appControlsFans = value;
            OnPropertyChanged();
            OnAppControlsFansChanged(value);
        }
    }

    private void OnAppControlsFansChanged(bool enabled)
    {
        PersistFanControlEnabled(enabled);

        if (!enabled)
        {
            _hardwareService.RestoreAllFansToAuto();
            return;
        }

        // Handing control to the app has to actually do something. Fans deliberately start
        // with NO curve assigned so nothing is driven until the user opts in — but that
        // left the engine with no curve to evaluate, so flipping this switch appeared to do
        // nothing at all (client round 14, item 9). Turning it on IS the opt-in, so give
        // any unassigned fan a curve now.
        AssignDefaultCurveWhereMissing();

        // Force a fresh write on the next tick. Resetting LastCurveWriteUtc alone is not
        // enough: the fans were just released to BIOS, but LastCurveTarget still holds the
        // last value we wrote, so the deadband check saw "no change" and skipped the re-apply
        // entirely — toggling off then on again did nothing (client round 18, item 10).
        foreach (var fan in Fans)
        {
            fan.LastCurveWriteUtc = DateTime.MinValue;
            fan.LastCurveTarget = float.NaN;
        }
    }

    private void AssignDefaultCurveWhereMissing()
    {
        var defaultCurve = Curves.FirstOrDefault()?.Name;
        if (string.IsNullOrEmpty(defaultCurve)) return;

        foreach (var fan in Fans)
            AssignCurveIfMissing(fan, defaultCurve);
    }

    /// <summary>Remembers the user's choice across restarts, unless we are mid-restore.</summary>
    private void PersistFanControlEnabled(bool enabled)
    {
        if (_restoringFanControl || _themeService == null) return;
        if (_themeService.CurrentTheme.AppFanControlEnabled == enabled) return;
        _themeService.CurrentTheme.AppFanControlEnabled = enabled;
        _themeService.SaveTheme();
    }

    private bool _restoringFanControl;
    private bool _fanControlRestored;

    /// <summary>
    /// Re-applies the persisted "Let BYLD Core control my fans" choice once, after fans are
    /// loaded (client round 18, item 11). Runs under a guard so it does not re-persist the value
    /// it just read, and only once per session. The safety net is intact: fans are still released
    /// to the BIOS on exit/crash, so this simply re-takes control on the next clean launch.
    /// </summary>
    private void RestorePersistedFanControl()
    {
        if (_fanControlRestored || _themeService == null) return;
        if (Fans.Count == 0) return;

        _fanControlRestored = true;
        if (!_themeService.CurrentTheme.AppFanControlEnabled) return;

        _restoringFanControl = true;
        try { AppControlsFans = true; }
        finally { _restoringFanControl = false; }
    }

    private static void AssignCurveIfMissing(FanItemViewModel fan, string curveName)
    {
        if (string.IsNullOrEmpty(fan.SelectedCurve))
            fan.SelectedCurve = curveName;
    }

    /// <summary>
    /// Advanced override: when ON, the engine keeps sending fan-control commands
    /// even to hardware that appears to ignore them, instead of auto-releasing to
    /// the card's own control. For users who have enabled a driver-side workaround
    /// (e.g. the RX 9070 manual-tuning steps) so the writes actually take effect.
    /// </summary>
    public bool ForceControlOverride
    {
        get => _themeService?.CurrentTheme.ForceFanControlOverride ?? false;
        set
        {
            if (_themeService == null || value == ForceControlOverride) return;
            _themeService.CurrentTheme.ForceFanControlOverride = value;
            _themeService.SaveTheme();
            OnPropertyChanged();
            if (value) ReenableDisabledFans();
        }
    }

    /// <summary>Re-arms fans that were auto-disabled, so the override takes effect immediately.</summary>
    private void ReenableDisabledFans()
    {
        foreach (var fan in Fans)
        {
            fan.ControlDisabled = false;
            fan.UnresponsiveWrites = 0;
        }
        DetectionStatusMessage = "";
    }

    public FanControlViewModel(HardwareControlService hardwareService, IThemeService? themeService = null)
    {
        _hardwareService = hardwareService;
        _themeService = themeService;
        // A manual refresh also re-arms the automatic retries (see MaxFanScanAttempts).
        RefreshFansCommand = new RelayCommand(_ => { ResetFanDetectionRetries(); LoadFans(); });
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

    private int _emptyFanPolls;

    private void PollSensors()
    {
        if (IsLoading) return;

        if (Fans.Count == 0)
        {
            RetryFanDetection();
            return;
        }

        try
        {
            foreach (var fan in Fans)
                UpdateFanReadings(fan);

            EvaluateCurves();
        }
        catch { /* Silent fail for polling errors */ }
    }

    /// <summary>
    /// GPU fan/control sensors appear a few seconds after startup (AMD's ADL
    /// initializes lazily), so a fan list loaded too early stays empty forever.
    /// Retry detection every ~12 s while the list is empty (client round 6:
    /// "no fans show up again" while the deep scan found 2 sensors later).
    /// </summary>
    private void RetryFanDetection()
    {
        if (HardwareControlService.IsDemoMode) return;
        if (_fanScanAttempts >= MaxFanScanAttempts) return;
        if (++_emptyFanPolls % 8 != 0) return;

        _fanScanAttempts++;
        LoadFans();
    }

    // The deep scan walks every hardware item and is expensive. Now that this view-model lives
    // for the whole session (not just while Settings is open), a machine with no controllable
    // fans would rescan every 12s forever. Cap the attempts — ~2 minutes is far longer than the
    // few seconds a GPU needs to expose its fan lazily — and let Refresh retry on demand.
    private const int MaxFanScanAttempts = 10;
    private int _fanScanAttempts;

    /// <summary>Re-arms automatic fan detection, so the Refresh button works after the
    /// automatic retries have been exhausted.</summary>
    public void ResetFanDetectionRetries()
    {
        _fanScanAttempts = 0;
        _emptyFanPolls = 0;
    }

    private static void UpdateFanReadings(FanItemViewModel fan)
    {
        if (fan.Sensor == null) return;

        // No Hardware.Update() here: HardwareMonitorService refreshes every
        // hardware item on the shared Computer once per second already —
        // updating again per fan per tick doubled the sensor I/O for nothing
        // (client round 8: "PC runs hard with the app open").
        if (fan.Sensor.Value.HasValue)
            ApplySensorReading(fan);
        if (fan.RpmSensor?.Value is { } rpm)
            fan.CurrentRpm = (int)rpm;
    }

    private static void ApplySensorReading(FanItemViewModel fan)
    {
        if (fan.Sensor!.SensorType == SensorType.Fan)
            fan.CurrentRpm = (int)fan.Sensor.Value!.Value;
        else if (fan.Sensor.SensorType == SensorType.Control && !fan.IsManual)
            fan.SpeedPercentage = fan.Sensor.Value!.Value;
    }

    /// <summary>
    /// The curve engine: every poll tick, each fan that is in curve mode with a
    /// selected curve gets the speed interpolated from its curve's live
    /// temperature source. This is what actually drives the fans.
    /// </summary>
    private void EvaluateCurves()
    {
        // Fans are only driven while the user has handed control to the app; otherwise
        // they stay on the motherboard's own curve. Readouts keep updating either way.
        if (AppControlsFans)
            foreach (var fan in Fans)
                ApplyCurveToFan(fan);

        foreach (var curve in Curves)
            UpdateCurveReadout(curve);
    }

    /// <summary>Shows the live RPM of the (first) fan driven by this curve on the curve card.</summary>
    private void UpdateCurveReadout(CurveItemViewModel curve)
    {
        var fan = Fans.FirstOrDefault(f => f.SelectedCurve == curve.Name && f.CurrentRpm > 0);
        curve.CurrentRpm = fan?.CurrentRpm ?? 0;
    }

    // Writing a fan control is a driver call; on some GPUs (client's RX 9070)
    // it forces a manual power state that throttles the whole PC. So the curve
    // engine only writes when the target has moved meaningfully (deadband), and
    // never more often than this interval — instead of every 1.3 s.
    private const float CurveWriteDeadbandPercent = 4f;
    private static readonly TimeSpan MinCurveWriteInterval = TimeSpan.FromSeconds(5);

    // After this many writes that the hardware clearly ignored, stop driving the
    // fan — repeatedly poking a control the GPU rejects is what throttled the PC.
    private const int UnresponsiveWriteLimit = 4;

    private void ApplyCurveToFan(FanItemViewModel fan)
    {
        if (fan.IsManual || fan.ControlDisabled || fan.Sensor is not { SensorType: SensorType.Control }) return;

        var curve = Curves.FirstOrDefault(c => c.Name == fan.SelectedCurve);
        if (curve == null) return;

        float? temp = ResolveSourceTemperature(curve.TemperatureSource);
        if (temp == null) return;

        float target = curve.EvaluateSpeed(temp.Value);
        if (!ShouldWriteCurveTarget(fan, target)) return;

        TrackResponsiveness(fan, target);
        if (fan.ControlDisabled) return;

        fan.LastCurveTarget = target;
        fan.LastCurveWriteUtc = DateTime.UtcNow;
        _hardwareService.SetFanSpeed(fan.Sensor, target,
            $"curve '{curve.Name}', {curve.TemperatureSource} = {temp.Value:F0}°C");
    }

    /// <summary>
    /// Detects a fan whose control writes are ignored — the control sensor reads
    /// back ~0 despite a &gt;0 command we already issued. Some GPUs (client's RX
    /// 9070) reject software fan control, and every rejected write is an
    /// expensive driver call that throttles the PC. After the limit the fan is
    /// released to automatic control and the engine stops writing to it.
    /// </summary>
    private void TrackResponsiveness(FanItemViewModel fan, float target)
    {
        // Override on: keep driving the fan regardless (the user has a driver-side
        // workaround that makes writes take effect). Never auto-disable.
        if (ForceControlOverride) return;

        // Only judge after a previous >0 command has had time to take effect.
        bool commandedButIgnored = fan.LastCurveTarget > 5f &&
                                   (fan.Sensor!.Value ?? 0) < 2f &&
                                   (fan.RpmSensor?.Value ?? 0) < 1f;
        if (!commandedButIgnored)
        {
            fan.UnresponsiveWrites = 0;
            return;
        }

        if (++fan.UnresponsiveWrites < UnresponsiveWriteLimit) return;

        fan.ControlDisabled = true;
        _hardwareService.SetFanAuto(fan.Sensor!);
        DetectionStatusMessage = $"'{fan.Name}' does not accept software fan control on this hardware — " +
            "released to the card's automatic control to keep the system responsive.";
        ShowHvciWarning = true;
    }

    /// <summary>
    /// True only when the curve target has moved past the deadband since the last
    /// write, or the minimum interval has elapsed. Compares against the last
    /// WRITTEN target (not the live reading, which never catches up on GPUs that
    /// ignore the command) — that comparison was what caused per-tick hammering.
    /// </summary>
    private static bool ShouldWriteCurveTarget(FanItemViewModel fan, float target)
    {
        if (float.IsNaN(fan.LastCurveTarget)) return true;
        if (Math.Abs(target - fan.LastCurveTarget) >= CurveWriteDeadbandPercent) return true;
        return DateTime.UtcNow - fan.LastCurveWriteUtc >= MinCurveWriteInterval
               && Math.Abs(target - fan.LastCurveTarget) >= 1f;
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
        SyncFanCurveLists();
    }

    /// <summary>
    /// Removes a curve from the collection and persists the change.
    /// </summary>
    private void DeleteCurve(CurveItemViewModel? curve)
    {
        if (curve == null) return;
        Curves.Remove(curve);
        FanCurvePersistence.SaveCurves(Curves);
        SyncFanCurveLists();
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
    /// <summary>Maps temperature-source labels to their live sensors.</summary>
    private readonly Dictionary<string, ISensor> _tempSources = new();

    /// <summary>Synthetic CPU source backed by the AMD iGPU SoC sensor — the same
    /// proxy the 7" gauges use when the MSR CPU temperature is unavailable
    /// (PawnIO not installed) and Tctl reads zero.</summary>
    private const string CpuProxySource = "CPU Temperature (via iGPU proxy)";

    private string[] GetAvailableSourceOptions()
    {
        if (HardwareControlService.IsDemoMode)
        {
            return new[] { "33 °C - CPU Package - Intel Core i", "40 °C - Memory - AMD Radeon RX", "Mix - Max" };
        }

        _tempSources.Clear();
        foreach (var sensor in _hardwareService.GetTemperatureSensors())
            _tempSources[$"{sensor.Name} — {sensor.Hardware.Name}"] = sensor;

        var options = _tempSources.Keys.ToList();
        AddCpuProxyIfNeeded(options);
        options.Add("Mix - Max");
        return options.ToArray();
    }

    /// <summary>
    /// When no CPU temperature sensor delivers a real value (e.g. PawnIO not
    /// installed, so the MSR Tctl/Tdie reads zero), offer the iGPU SoC proxy as
    /// the FIRST source so new curves default to something that actually works.
    /// </summary>
    private void AddCpuProxyIfNeeded(List<string> options)
    {
        bool cpuAlive = _tempSources.Values.Any(
            s => s.Hardware.HardwareType == HardwareType.Cpu && s.Value > 0);
        if (cpuAlive) return;

        if (_tempSources.Values.Any(IsIgpuSocSensor))
            options.Insert(0, CpuProxySource);
    }

    /// <summary>The AMD iGPU's SoC temperature sensor — it sits on the CPU die and
    /// tracks CPU temperature closely. Only the integrated GPU exposes it.</summary>
    private static bool IsIgpuSocSensor(ISensor sensor)
    {
        return sensor.Hardware.HardwareType == HardwareType.GpuAmd &&
               sensor.Name.Contains("SoC", StringComparison.OrdinalIgnoreCase);
    }

    private float? ReadIgpuSocProxy()
    {
        var soc = _tempSources.Values.FirstOrDefault(IsIgpuSocSensor);
        return soc != null ? ReadTemperature(soc) : null;
    }

    /// <summary>
    /// Resolves a temperature-source label from the curve editor to a live
    /// temperature reading. Falls back to keyword matching for labels saved by
    /// older builds ("CPU Package", "GPU Core").
    /// </summary>
    private float? ResolveSourceTemperature(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        if (source == "Mix - Max") return MaxOfAllSources();
        if (source == CpuProxySource) return ReadIgpuSocProxy();
        if (_tempSources.TryGetValue(source, out var sensor)) return ReadSensorWithCpuFallback(sensor);
        return ResolveLegacySource(source);
    }

    /// <summary>
    /// Reads a sensor; when a CPU temperature sensor is dead (permanent zero when
    /// the MSR path is unavailable, e.g. PawnIO not installed), transparently falls
    /// back to the iGPU SoC proxy so curves bound to "Core (Tctl/Tdie)" keep driving fans.
    /// </summary>
    private float? ReadSensorWithCpuFallback(ISensor sensor)
    {
        var value = ReadTemperature(sensor);
        if (value != null) return value;

        return sensor.Hardware.HardwareType == HardwareType.Cpu ? ReadIgpuSocProxy() : null;
    }

    private float? MaxOfAllSources()
    {
        float? max = null;
        foreach (var sensor in _tempSources.Values)
            max = MaxOf(max, ReadTemperature(sensor));
        return max;
    }

    private static float? MaxOf(float? a, float? b)
    {
        if (a == null) return b;
        if (b == null) return a;
        return Math.Max(a.Value, b.Value);
    }

    /// <summary>Reads a temperature, treating 0 as unavailable (dead MSR reads).
    /// The sensor values are kept fresh by HardwareMonitorService's 1 s loop on
    /// the shared Computer — no extra Hardware.Update() per read.</summary>
    private static float? ReadTemperature(ISensor sensor)
    {
        return sensor.Value is > 0 ? sensor.Value : null;
    }

    private float? ResolveLegacySource(string source)
    {
        var match = _tempSources.FirstOrDefault(kv => LegacyMatches(source, kv.Key));
        if (match.Value != null) return ReadSensorWithCpuFallback(match.Value);

        // Saved CPU-labelled sources still work via the proxy even when no
        // matching sensor exists at all.
        return source.Contains("CPU", StringComparison.OrdinalIgnoreCase) ? ReadIgpuSocProxy() : null;
    }

    private static bool LegacyMatches(string source, string label)
    {
        if (source.Contains("CPU", StringComparison.OrdinalIgnoreCase))
            return label.Contains("CPU", StringComparison.OrdinalIgnoreCase) ||
                   label.Contains("Tctl", StringComparison.OrdinalIgnoreCase);
        if (source.Contains("GPU", StringComparison.OrdinalIgnoreCase))
            return label.Contains("GPU Core", StringComparison.OrdinalIgnoreCase);
        return false;
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

        // Applied to BOTH paths: these used to live inside the live loader only, so in demo mode
        // per-fan colours, custom names, 7"-visibility and ordering silently did nothing.
        ApplyPerFanPreferences();
        ApplySavedFanOrder();

        IsLoading = false;
    }

    /// <summary>
    /// Applies each fan's saved colour, custom name and 7"-display visibility. Runs while the
    /// list is still in hardware-detection order, so the palette colour assigned by position
    /// stays stable no matter how the user later reorders the cards.
    /// </summary>
    private void ApplyPerFanPreferences()
    {
        for (int i = 0; i < Fans.Count; i++)
        {
            var fan = Fans[i];
            fan.IconColorHex = ResolveFanColorHex(fan.Name, i);
            fan.SetCustomNameQuiet(ResolveFanCustomName(fan.Name));
            fan.ShowOnWidget = !IsFanHiddenFromWidget(fan.Name);
        }
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

        var sensors = FilterOutGpuFanIfBoardFansPresent(_hardwareService.GetFanSensors());
        ShowHvciWarning = _hardwareService.FanStatus != FanDetectionStatus.Success;

        switch (_hardwareService.FanStatus)
        {
            case FanDetectionStatus.DriverBlocked:
                DetectionStatusMessage = "The PawnIO hardware driver needed to read your motherboard's fan headers is not installed.\n\n" +
                                         "1. Install PawnIO (Microsoft-signed) from https://pawnio.eu/ and restart BYLD Core.\n" +
                                         "2. PawnIO installs in a few seconds and does not require disabling any Windows security features.";
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

        // Pair each Control (%) sensor with its RPM tach so one physical fan
        // shows as ONE card with both readings, instead of two half-cards.
        // Per-fan colours/names/visibility and ordering are applied by LoadFans for both the
        // live and demo paths — see ApplyPerFanPreferences.
        foreach (var item in PairFanSensors(sensors))
            Fans.Add(item);

        SyncFanCurveLists();
        ReleaseAllFansToBios();
        RestorePersistedFanControl();
    }

    /// <summary>Reads a fan's chosen icon colour (keyed by name) or a distinct palette default.</summary>
    private string ResolveFanColorHex(string name, int index)
    {
        var colors = _themeService?.CurrentTheme.FanColors;
        if (colors != null && colors.TryGetValue(name, out var hex) && !string.IsNullOrWhiteSpace(hex))
            return hex;
        return Models.FanMetric.PaletteHex(index);
    }

    /// <summary>Sets and persists a fan's 7"-screen icon colour, keyed by fan name.</summary>
    public void SetFanColor(FanItemViewModel fan, string hex)
    {
        fan.IconColorHex = hex;
        if (_themeService == null) return;
        _themeService.CurrentTheme.FanColors[fan.Name] = hex;
        _themeService.SaveTheme();
    }

    /// <summary>Reads a fan's custom display name from the theme, or null when the user hasn't set one.</summary>
    private string? ResolveFanCustomName(string sensorName)
    {
        var names = _themeService?.CurrentTheme.FanNames;
        if (names != null && names.TryGetValue(sensorName, out var custom) && !string.IsNullOrWhiteSpace(custom))
            return custom;
        return null;
    }

    /// <summary>
    /// Sets and persists a fan's custom display name, keyed by its hardware sensor name so it
    /// survives redetection. A blank name clears the override and restores the hardware name.
    /// </summary>
    public void SetFanName(FanItemViewModel fan, string? newName)
    {
        string trimmed = newName?.Trim() ?? string.Empty;
        fan.SetCustomNameQuiet(string.IsNullOrWhiteSpace(trimmed) ? null : trimmed);
        fan.IsEditingName = false;
        if (_themeService == null) return;
        UpdateFanNameInTheme(fan.Name, trimmed);
        _themeService.SaveTheme();
        // Push the new label to the 7" display now rather than on the next sensor tick
        // (client round 20, item 5).
        RaiseWidgetLayoutChanged();
    }

    private void UpdateFanNameInTheme(string sensorName, string trimmed)
    {
        var names = _themeService!.CurrentTheme.FanNames;
        if (string.IsNullOrWhiteSpace(trimmed)) names.Remove(sensorName);
        else names[sensorName] = trimmed;
    }

    /// <summary>
    /// Raised when a fan's 7"-display visibility or order changes, so the widget can rebuild
    /// straight away instead of waiting for the next sensor tick (client round 19, item 6).
    /// </summary>
    public event EventHandler? WidgetLayoutChanged;

    private void RaiseWidgetLayoutChanged() => WidgetLayoutChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>True when this fan is hidden from the 7" widget (persisted by sensor name).</summary>
    private bool IsFanHiddenFromWidget(string sensorName) =>
        _themeService?.CurrentTheme.HiddenFanNames.Contains(sensorName) == true;

    /// <summary>
    /// Shows or hides a fan on the 7" System Cooling widget and persists the choice, keyed by
    /// the hardware sensor name (client round 18, item 9b). The fan always stays in Fan Control.
    /// </summary>
    public void SetFanWidgetVisibility(FanItemViewModel fan, bool visible)
    {
        fan.ShowOnWidget = visible;
        if (_themeService == null) return;

        var hidden = _themeService.CurrentTheme.HiddenFanNames;
        bool changed = visible ? hidden.Remove(fan.Name) : AddIfMissing(hidden, fan.Name);
        if (changed) _themeService.SaveTheme();
        RaiseWidgetLayoutChanged();
    }

    private static bool AddIfMissing(List<string> list, string value)
    {
        if (list.Contains(value)) return false;
        list.Add(value);
        return true;
    }

    /// <summary>
    /// Moves a fan one place earlier/later on the 7" display and persists the order (client
    /// round 18, item 13). The order is rewritten from the currently visible fans, so fans the
    /// user never moved keep a stable position.
    /// </summary>
    public void MoveFanOnWidget(FanItemViewModel fan, int delta)
    {
        if (_themeService == null) return;

        // Move within the live Fans collection so the Fan Control cards visibly reorder too —
        // ordering only the saved list left the on-screen cards unchanged, which read as the
        // menu items doing nothing (client round 18 follow-up). Hidden fans are included so
        // they keep a stable position and reappear where the user left them.
        int from = Fans.IndexOf(fan);
        int to = from + delta;
        if (from < 0 || to < 0 || to >= Fans.Count) return;

        Fans.Move(from, to);
        PersistFanOrder();
        RaiseWidgetLayoutChanged();
    }

    /// <summary>Saves the current on-screen fan order, which the 7" display then mirrors.</summary>
    private void PersistFanOrder()
    {
        if (_themeService == null) return;
        _themeService.CurrentTheme.FanDisplayOrder = Fans.Select(f => f.Name).ToList();
        _themeService.SaveTheme();
    }

    /// <summary>
    /// Re-sorts the freshly detected fan list into the user's saved display order, so the Fan
    /// Control cards come back in the same order after a reload or restart.
    /// </summary>
    private void ApplySavedFanOrder()
    {
        var order = _themeService?.CurrentTheme.FanDisplayOrder;
        if (order == null || order.Count == 0) return;

        var sorted = Fans.OrderBy(f => Models.ThemeConfig.DisplayOrderIndex(order, f.Name)).ToList();
        for (int target = 0; target < sorted.Count; target++)
        {
            int current = Fans.IndexOf(sorted[target]);
            if (current != target) Fans.Move(current, target);
        }
    }

    /// <summary>
    /// Hands every detected fan back to the motherboard at startup. AppControlsFans always
    /// begins false, so the hardware must match that. This also cleans up after a hard
    /// crash: a control left in software mode stays pinned at the last written value even
    /// though the app that set it is gone, which is how the client's fans ended up stopped.
    /// </summary>
    private void ReleaseAllFansToBios()
    {
        foreach (var fan in Fans)
            ReleaseFanToBios(fan);
    }

    private void ReleaseFanToBios(FanItemViewModel fan)
    {
        if (fan.Sensor is { SensorType: SensorType.Control })
            _hardwareService.SetFanAuto(fan.Sensor);
    }

    /// <summary>
    /// When the motherboard Super I/O exposes real fan controls, drops the discrete
    /// GPU fan from the list. AMD cards like the RX 9070 reject every software fan
    /// write, and each rejected driver call thrashes the whole system — the card
    /// manages its own fan, so once real board fans are available we stop touching it.
    /// </summary>
    private static List<ISensor> FilterOutGpuFanIfBoardFansPresent(List<ISensor> sensors)
    {
        bool hasBoardFan = sensors.Any(s => IsMotherboardFan(s));
        if (!hasBoardFan) return sensors;
        return sensors.Where(s => !IsDiscreteGpuFan(s)).ToList();
    }

    private static bool IsMotherboardFan(ISensor s) =>
        s.Hardware.HardwareType == HardwareType.Motherboard ||
        s.Hardware.HardwareType == HardwareType.SuperIO;

    private static bool IsDiscreteGpuFan(ISensor s) =>
        s.Hardware.HardwareType == HardwareType.GpuAmd ||
        s.Hardware.HardwareType == HardwareType.GpuNvidia ||
        s.Hardware.HardwareType == HardwareType.GpuIntel;

    private List<FanItemViewModel> PairFanSensors(List<ISensor> sensors)
    {
        var controls = sensors.Where(s => s.SensorType == SensorType.Control).ToList();
        var tachs    = sensors.Where(s => s.SensorType == SensorType.Fan).ToList();

        var result = controls.Select(c => CreatePairedItem(c, tachs)).ToList();
        result.AddRange(tachs.Where(t => !HasMatchingControl(t, controls))
                             .Select(t => new FanItemViewModel(t, _hardwareService)));
        return result;
    }

    private FanItemViewModel CreatePairedItem(ISensor control, List<ISensor> tachs)
    {
        var rpm = tachs.FirstOrDefault(t => t.Hardware == control.Hardware && t.Name == control.Name)
               ?? tachs.FirstOrDefault(t => t.Hardware == control.Hardware && t.Index == control.Index);
        return new FanItemViewModel(control, _hardwareService) { RpmSensor = rpm };
    }

    private static bool HasMatchingControl(ISensor tach, List<ISensor> controls)
    {
        return controls.Any(c => c.Hardware == tach.Hardware &&
                                 (c.Name == tach.Name || c.Index == tach.Index));
    }

    /// <summary>
    /// Refreshes every fan card's curve dropdown to the current curve names,
    /// preserving the selection when it still exists.
    /// </summary>
    private void SyncFanCurveLists()
    {
        var names = Curves.Select(c => c.Name).ToList();
        foreach (var fan in Fans)
            SyncCurveList(fan, names);
    }

    private static void SyncCurveList(FanItemViewModel fan, List<string> names)
    {
        var selected = fan.SelectedCurve;
        fan.AvailableCurves.Clear();
        foreach (var name in names) fan.AvailableCurves.Add(name);

        // NEVER auto-assign a curve. Assigning one hands that fan from the motherboard
        // to this app, and auto-assigning seized all eight headers — including the
        // water pump — and drove them to the default curve's 30% floor at idle, which
        // is below their spin-up threshold: the client's PC went silent with the fans
        // stopped (round 13, item 3). A fan stays on BIOS control until the user picks
        // a curve for it.
        fan.SelectedCurve = names.Contains(selected) ? selected : "";
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

    /// <summary>Absolute path of the saved fan-curve file, so named profiles can bundle it.</summary>
    public static string CurveFilePath => CurveFile;

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
