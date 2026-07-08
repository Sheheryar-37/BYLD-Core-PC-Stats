using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Media;
using PcStatsMonitor.Services;
using OpenRGB.NET;

namespace PcStatsMonitor.ViewModels;

/// <summary>
/// Maps raw OpenRGB zone names to user-friendly labels.
/// Zones wired to motherboard ARGB headers appear as "Addressable Header 1" etc.
/// in OpenRGB — this mapper presents them as "Case RGB Strip 1" etc. to the user.
/// </summary>
public static class ZoneNameMapper
{
    private static readonly Dictionary<string, string> FriendlyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Addressable Header 1", "Case RGB Strip 1" },
        { "Addressable Header 2", "Case RGB Strip 2" },
        { "Addressable Header 3", "Case RGB Strip 3" },
        { "AURA ARGB Header", "Motherboard ARGB" },
        { "AURA RGB Header", "Motherboard RGB" },
    };

    /// <summary>
    /// Returns a friendly display name if a mapping exists; otherwise returns the original name.
    /// </summary>
    public static string GetDisplayName(string rawName)
    {
        return FriendlyNames.TryGetValue(rawName, out var friendly) ? friendly : rawName;
    }
}

public class RgbZoneViewModel : ViewModelBase
{
    private readonly HardwareControlService _hardwareService;
    private readonly int _deviceId;
    private readonly int _zoneId;

    /// <summary>
    /// User-friendly zone name (may be remapped from the raw OpenRGB zone name).
    /// </summary>
    public string Name { get; }
    public uint LedCount { get; }

    private System.Windows.Media.Color _selectedColor;
    public System.Windows.Media.Color SelectedColor
    {
        get => _selectedColor;
        set
        {
            if (SetProperty(ref _selectedColor, value))
            {
                ApplyColor();
            }
        }
    }

    private bool _isGradient;
    public bool IsGradient
    {
        get => _isGradient;
        set
        {
            if (SetProperty(ref _isGradient, value))
            {
                ApplyColor();
            }
        }
    }

    private System.Windows.Media.Color _gradientEndColor;
    public System.Windows.Media.Color GradientEndColor
    {
        get => _gradientEndColor;
        set
        {
            if (SetProperty(ref _gradientEndColor, value))
            {
                ApplyColor();
            }
        }
    }

    public RgbZoneViewModel(Zone zone, int deviceId, int zoneId, HardwareControlService hardwareService)
    {
        Name = ZoneNameMapper.GetDisplayName(zone.Name);
        LedCount = zone.LedCount;
        _deviceId = deviceId;
        _zoneId = zoneId;
        _hardwareService = hardwareService;
        _selectedColor = System.Windows.Media.Colors.White; // Default
        _gradientEndColor = System.Windows.Media.Colors.White;
        _isGradient = false;
    }

    public RgbZoneViewModel(string demoName, uint demoLedCount)
    {
        Name = demoName;
        LedCount = demoLedCount;
        _deviceId = -1;
        _zoneId = -1;
        _hardwareService = null!;
        _selectedColor = System.Windows.Media.Colors.White;
        _gradientEndColor = System.Windows.Media.Colors.White;
        _isGradient = false;
    }

    private void ApplyColor()
    {
        if (HardwareControlService.IsDemoMode) return;
        
        if (IsGradient && LedCount > 1)
        {
            var colors = new OpenRGB.NET.Color[LedCount];
            for (int i = 0; i < LedCount; i++)
            {
                float ratio = (float)i / (LedCount - 1);
                byte r = (byte)(SelectedColor.R + ratio * (GradientEndColor.R - SelectedColor.R));
                byte g = (byte)(SelectedColor.G + ratio * (GradientEndColor.G - SelectedColor.G));
                byte b = (byte)(SelectedColor.B + ratio * (GradientEndColor.B - SelectedColor.B));
                colors[i] = new OpenRGB.NET.Color(r, g, b);
            }
            _hardwareService.UpdateRgbZoneColors(_deviceId, _zoneId, colors);
        }
        else
        {
            var orgbColor = new OpenRGB.NET.Color(SelectedColor.R, SelectedColor.G, SelectedColor.B);
            _hardwareService.UpdateRgbZoneColor(_deviceId, _zoneId, orgbColor);
        }

        // Persist the change
        RgbSettingsPersistence.SaveCurrentState();
    }

    /// <summary>Re-sends the currently selected colours to the hardware,
    /// even when the colour properties did not change.</summary>
    public void ReapplyColor() => ApplyColor();

    /// <summary>
    /// Updates the colour properties WITHOUT triggering a hardware write, so a
    /// caller can follow up with one single <see cref="ReapplyColor"/>. Setting
    /// the properties normally fires a write per property change, which hammered
    /// the RAM's SMBus with duplicate writes during apply-to-all.
    /// </summary>
    public void SetColorsSilently(System.Windows.Media.Color color, bool isGradient, System.Windows.Media.Color endColor)
    {
        _selectedColor = color;
        _isGradient = isGradient;
        _gradientEndColor = endColor;
        OnPropertyChanged(nameof(SelectedColor));
        OnPropertyChanged(nameof(IsGradient));
        OnPropertyChanged(nameof(GradientEndColor));
    }
}

public class RgbDeviceViewModel : ViewModelBase
{
    private readonly HardwareControlService _hardwareService;
    public int DeviceId { get; }
    public string Name { get; }
    public string Type { get; }

    public ObservableCollection<RgbZoneViewModel> Zones { get; } = new();
    public ObservableCollection<string> Modes { get; } = new();

    private string? _selectedMode;
    public string? SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (SetProperty(ref _selectedMode, value) && value != null)
            {
                if (!HardwareControlService.IsDemoMode)
                {
                    _hardwareService.RequestRgbEffect(DeviceId, value);
                }
                // Persist mode change
                RgbSettingsPersistence.SaveCurrentState();
            }
        }
    }

    /// <summary>
    /// Switches the lighting mode and sends the colour WITH the mode change, for
    /// modes that carry mode-specific colours (e.g. ENE DRAM "Static"). Used by
    /// apply-to-all so the device doesn't light up with its stale stored colour.
    /// </summary>
    public void ApplyModeWithColor(string modeName, System.Windows.Media.Color color)
    {
        if (!Modes.Contains(modeName)) return;

        if (!HardwareControlService.IsDemoMode)
        {
            var orgb = new OpenRGB.NET.Color(color.R, color.G, color.B);
            _hardwareService.RequestRgbEffect(DeviceId, modeName, orgb);
        }

        _selectedMode = modeName;
        OnPropertyChanged(nameof(SelectedMode));
        RgbSettingsPersistence.SaveCurrentState();
    }

    public RgbDeviceViewModel(Device device, int deviceId, HardwareControlService hardwareService)
    {
        DeviceId = deviceId;
        Name = device.Name;
        Type = device.Type.ToString();
        _hardwareService = hardwareService;

        for (int i = 0; i < device.Zones.Length; i++)
        {
            Zones.Add(new RgbZoneViewModel(device.Zones[i], deviceId, i, hardwareService));
        }

        foreach (var mode in device.Modes)
        {
            Modes.Add(mode.Name);
        }

        if (device.ActiveModeIndex >= 0 && device.ActiveModeIndex < device.Modes.Length)
        {
            _selectedMode = device.Modes[device.ActiveModeIndex].Name;
        }
    }

    public RgbDeviceViewModel(string demoName, string demoType)
    {
        DeviceId = -1;
        Name = demoName;
        Type = demoType;
        _hardwareService = null!;
    }
}

public class RgbControlViewModel : ViewModelBase
{
    private readonly HardwareControlService _hardwareService;

    public ObservableCollection<RgbDeviceViewModel> Devices { get; } = new();

    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        set => SetProperty(ref _isConnected, value);
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    private bool _showAdminWarning;
    public bool ShowAdminWarning
    {
        get => _showAdminWarning;
        set => SetProperty(ref _showAdminWarning, value);
    }

    /// <summary>Modes supported by every detected device, offered in the "Apply to all" bar.</summary>
    public ObservableCollection<string> CommonModes { get; } = new();

    private string? _selectedCommonMode;
    /// <summary>When set, applies the chosen lighting mode to every device that supports it.</summary>
    public string? SelectedCommonMode
    {
        get => _selectedCommonMode;
        set
        {
            if (!SetProperty(ref _selectedCommonMode, value) || value == null) return;
            foreach (var device in Devices)
            {
                if (device.Modes.Contains(value)) device.SelectedMode = value;
            }
        }
    }

    private System.Windows.Media.Color _masterColor = System.Windows.Media.Colors.White;
    /// <summary>The colour last applied to all devices; shown on the "Apply to all" swatch.</summary>
    public System.Windows.Media.Color MasterColor
    {
        get => _masterColor;
        set => SetProperty(ref _masterColor, value);
    }

    private bool _masterIsGradient;
    /// <summary>Whether the last "apply to all" used a gradient pattern.</summary>
    public bool MasterIsGradient
    {
        get => _masterIsGradient;
        set => SetProperty(ref _masterIsGradient, value);
    }

    private System.Windows.Media.Color _masterEndColor = System.Windows.Media.Colors.White;
    /// <summary>Gradient end colour for the "apply to all" pattern.</summary>
    public System.Windows.Media.Color MasterEndColor
    {
        get => _masterEndColor;
        set => SetProperty(ref _masterEndColor, value);
    }

    private sealed record ZoneSnapshot(
        System.Windows.Media.Color Color, bool IsGradient, System.Windows.Media.Color EndColor);
    private sealed record DeviceSnapshot(string? Mode, List<ZoneSnapshot> Zones);

    /// <summary>Per-device state captured just before the first "apply to all",
    /// so the user can undo it. Null when nothing has been applied yet.</summary>
    private Dictionary<string, DeviceSnapshot>? _preApplySnapshot;

    /// <summary>True when an "apply to all" can be undone.</summary>
    public bool CanRestoreDevices => _preApplySnapshot != null;

    /// <summary>
    /// Applies one colour (or gradient pattern) to every zone of every detected device.
    /// </summary>
    public void ApplyColorToAllZones(System.Windows.Media.Color color, bool isGradient, System.Windows.Media.Color endColor)
    {
        CaptureSnapshotIfNeeded();

        MasterColor = color;
        MasterIsGradient = isGradient;
        MasterEndColor = endColor;

        foreach (var device in Devices)
            ApplyColorToDevice(device, color, isGradient, endColor);
    }

    private static void ApplyColorToDevice(RgbDeviceViewModel device,
        System.Windows.Media.Color color, bool isGradient, System.Windows.Media.Color endColor)
    {
        // Devices sitting in a hardware effect (Rainbow, Breathing…) ignore or
        // black out on direct LED writes (ENE DRAM does) — switch the device to
        // a colour-capable mode first. Prefer "Static" over "Direct": Static is
        // stored by the hardware itself, while Direct needs continuous refresh
        // and drops back to black on devices like ENE DRAM once writes stop.
        // The colour is sent WITH the mode change — mode-specific-colour devices
        // otherwise light up with their stale stored colour (usually red).
        var colorMode = device.Modes.FirstOrDefault(m => m == "Static")
                     ?? device.Modes.FirstOrDefault(m => m == "Direct");
        if (colorMode != null)
            device.ApplyModeWithColor(colorMode, color);

        foreach (var zone in device.Zones)
        {
            // Exactly ONE hardware write per zone — property setters would each
            // fire their own write and hammer the DRAM controller's SMBus.
            zone.SetColorsSilently(color, isGradient, endColor);
            zone.ReapplyColor();
        }
    }

    private void CaptureSnapshotIfNeeded()
    {
        if (_preApplySnapshot != null) return;

        _preApplySnapshot = Devices.ToDictionary(SnapshotKey, SnapshotDevice);
        OnPropertyChanged(nameof(CanRestoreDevices));
    }

    private static string SnapshotKey(RgbDeviceViewModel device) => $"{device.DeviceId}:{device.Name}";

    private static DeviceSnapshot SnapshotDevice(RgbDeviceViewModel device)
    {
        var zones = device.Zones
            .Select(z => new ZoneSnapshot(z.SelectedColor, z.IsGradient, z.GradientEndColor))
            .ToList();
        return new DeviceSnapshot(device.SelectedMode, zones);
    }

    /// <summary>
    /// Undoes the last "apply to all": restores every device's mode and zone
    /// colours captured just before the first apply-all of this session.
    /// </summary>
    public void RestoreDeviceStates()
    {
        if (_preApplySnapshot == null) return;

        foreach (var device in Devices)
            RestoreDevice(device);

        _preApplySnapshot = null;
        OnPropertyChanged(nameof(CanRestoreDevices));
    }

    private void RestoreDevice(RgbDeviceViewModel device)
    {
        if (!_preApplySnapshot!.TryGetValue(SnapshotKey(device), out var snapshot)) return;

        if (snapshot.Mode != null) device.SelectedMode = snapshot.Mode;
        for (int i = 0; i < device.Zones.Count && i < snapshot.Zones.Count; i++)
            RestoreZone(device.Zones[i], snapshot.Zones[i]);
    }

    private static void RestoreZone(RgbZoneViewModel zone, ZoneSnapshot snapshot)
    {
        zone.GradientEndColor = snapshot.EndColor;
        zone.IsGradient = snapshot.IsGradient;
        zone.SelectedColor = snapshot.Color;
        zone.ReapplyColor();
    }

    /// <summary>Recomputes the intersection of modes across all devices.</summary>
    private void RebuildCommonModes()
    {
        CommonModes.Clear();
        if (Devices.Count == 0) return;

        var common = Devices[0].Modes.AsEnumerable();
        foreach (var device in Devices.Skip(1))
            common = common.Intersect(device.Modes);

        foreach (var mode in common)
            CommonModes.Add(mode);
    }

    public ICommand ConnectCommand { get; }
    public ICommand RefreshCommand { get; }

    private System.Windows.Threading.DispatcherTimer? _autoRefreshTimer;

    public RgbControlViewModel(HardwareControlService hardwareService)
    {
        _hardwareService = hardwareService;
        ConnectCommand = new RelayCommand(_ => Connect());
        RefreshCommand = new RelayCommand(_ => LoadDevices(), _ => IsConnected);
        StartAutoRefresh();
    }

    /// <summary>
    /// Periodically re-enumerates OpenRGB devices so newly connected hardware
    /// appears without the user pressing Refresh. The list is only rebuilt
    /// when the device set actually changes, to avoid UI flicker.
    /// </summary>
    private void StartAutoRefresh()
    {
        if (HardwareControlService.IsDemoMode) return;

        _autoRefreshTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(10)
        };
        _autoRefreshTimer.Tick += (s, e) => AutoRefreshDevices();
        _autoRefreshTimer.Start();
    }

    /// <summary>Stops the auto-refresh timer. Call when the hosting view closes.</summary>
    public void StopAutoRefresh()
    {
        _autoRefreshTimer?.Stop();
    }

    private int _reconnectTicks;

    private void AutoRefreshDevices()
    {
        // Demo mode fakes IsConnected — polling the real server here would
        // return an empty list and wipe the demo cards.
        if (HardwareControlService.IsDemoMode) return;
        if (IsLoading) return;

        if (!IsConnected)
        {
            TryReconnect();
            return;
        }

        var devices = _hardwareService.GetRgbDevices();
        if (!HasDeviceListChanged(devices)) return;

        Devices.Clear();
        for (int i = 0; i < devices.Count; i++)
            Devices.Add(new RgbDeviceViewModel(devices[i], i, _hardwareService));

        RebuildCommonModes();
        RgbSettingsPersistence.RestoreState(this);
    }

    /// <summary>
    /// Attempts to re-establish the OpenRGB connection roughly every 30 s
    /// (every 3rd auto-refresh tick) so the device list recovers without a
    /// manual refresh once the server comes up or restarts as admin.
    /// </summary>
    private void TryReconnect()
    {
        if (++_reconnectTicks % 3 != 0) return;
        Connect();
    }

    private bool HasDeviceListChanged(List<Device> devices)
    {
        if (devices.Count != Devices.Count) return true;
        return devices.Where((d, i) => d.Name != Devices[i].Name).Any();
    }

    public void Connect()
    {
        IsLoading = true;
        if (HardwareControlService.IsDemoMode)
        {
            IsConnected = true;
        }
        else
        {
            IsConnected = _hardwareService.ConnectRgbServer();
        }
        
        if (IsConnected)
        {
            LoadDevices();
        }
        IsLoading = false;
    }

    public void LoadDevices()
    {
        if (!IsConnected) return;

        IsLoading = true;
        Devices.Clear();

        if (HardwareControlService.IsDemoMode)
        {
            var demoMobo = new RgbDeviceViewModel("ASRock B650I Lightning WiFi", "Motherboard");
            demoMobo.Zones.Add(new RgbZoneViewModel("Case RGB Strip 1", 80));
            demoMobo.Zones.Add(new RgbZoneViewModel("Case RGB Strip 2", 80));
            demoMobo.Modes.Add("Static");
            demoMobo.Modes.Add("Breathing");
            demoMobo.Modes.Add("Rainbow");
            demoMobo.SelectedMode = "Rainbow";

            var demoRam = new RgbDeviceViewModel("G.Skill Trident Z5 RGB", "DRAM");
            demoRam.Zones.Add(new RgbZoneViewModel("DIMM 1", 8));
            demoRam.Zones.Add(new RgbZoneViewModel("DIMM 2", 8));
            demoRam.Modes.Add("Direct");
            demoRam.Modes.Add("Color Shift");
            demoRam.SelectedMode = "Color Shift";

            Devices.Add(demoMobo);
            Devices.Add(demoRam);
        }
        else
        {
            var devices = _hardwareService.GetRgbDevices();
            for (int i = 0; i < devices.Count; i++)
            {
                Devices.Add(new RgbDeviceViewModel(devices[i], i, _hardwareService));
            }

            // If we connected but found 0 devices, or couldn't get devices, 
            // it's highly likely OpenRGB needs to be run as Admin to see SMBus/USB RGB controllers.
            if (devices.Count == 0 && !HardwareControlService.IsDemoMode)
            {
                ShowAdminWarning = true;
                _hardwareService.RestartOpenRgbAsAdmin();
                
                // Sleep to allow it to restart, then try once more.
                System.Threading.Thread.Sleep(3000);
                if (_hardwareService.ConnectRgbServer())
                {
                    devices = _hardwareService.GetRgbDevices();
                    for (int i = 0; i < devices.Count; i++)
                    {
                        Devices.Add(new RgbDeviceViewModel(devices[i], i, _hardwareService));
                    }
                    ShowAdminWarning = devices.Count == 0;
                }
            }
            else
            {
                ShowAdminWarning = false;
            }
        }

        RebuildCommonModes();

        // Restore any previously-saved settings (colours + modes)
        RgbSettingsPersistence.RestoreState(this);

        IsLoading = false;
    }
}

/// <summary>
/// Persists RGB device settings (selected mode + per-zone colours) to a local JSON file
/// so they survive application restarts. Settings are saved on every user change and
/// restored automatically when devices are loaded.
/// </summary>
public static class RgbSettingsPersistence
{
    private static readonly string SettingsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings");
    private static readonly string SettingsFile = Path.Combine(SettingsDir, "rgb_settings.json");

    /// <summary>Reference to the active ViewModel for saving.</summary>
    private static RgbControlViewModel? _activeViewModel;

    /// <summary>
    /// Saves the current state of all RGB devices (mode + zone colours) to disk.
    /// Called automatically whenever a colour or mode is changed.
    /// </summary>
    public static void SaveCurrentState()
    {
        if (_activeViewModel == null) return;

        try
        {
            var state = new RgbPersistedState
            {
                Devices = _activeViewModel.Devices.Select(d => new RgbDeviceState
                {
                    DeviceName = d.Name,
                    DeviceId = d.DeviceId,
                    SelectedMode = d.SelectedMode,
                    Zones = d.Zones.Select(z => new RgbZoneState
                    {
                        ZoneName = z.Name,
                        ColorR = z.SelectedColor.R,
                        ColorG = z.SelectedColor.G,
                        ColorB = z.SelectedColor.B,
                        IsGradient = z.IsGradient,
                        GradientEndR = z.GradientEndColor.R,
                        GradientEndG = z.GradientEndColor.G,
                        GradientEndB = z.GradientEndColor.B
                    }).ToList()
                }).ToList()
            };

            if (!Directory.Exists(SettingsDir))
                Directory.CreateDirectory(SettingsDir);

            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFile, json);
        }
        catch
        {
            // Silently fail — persistence is best-effort; hardware control must never crash
        }
    }

    /// <summary>
    /// Restores previously saved colours and modes onto the loaded devices.
    /// Matching is done by device name + zone name (not by index) to be resilient
    /// against device enumeration order changes.
    /// </summary>
    public static void RestoreState(RgbControlViewModel viewModel)
    {
        _activeViewModel = viewModel;

        if (!File.Exists(SettingsFile)) return;

        try
        {
            var json = File.ReadAllText(SettingsFile);
            var state = JsonSerializer.Deserialize<RgbPersistedState>(json);
            if (state?.Devices == null) return;

            foreach (var deviceVm in viewModel.Devices)
            {
                var savedDevice = state.Devices.FirstOrDefault(
                    d => string.Equals(d.DeviceName, deviceVm.Name, StringComparison.OrdinalIgnoreCase));
                if (savedDevice == null) continue;

                // Restore mode (set backing field to avoid triggering a hardware call during load)
                if (!string.IsNullOrEmpty(savedDevice.SelectedMode) && deviceVm.Modes.Contains(savedDevice.SelectedMode))
                {
                    deviceVm.SelectedMode = savedDevice.SelectedMode;
                }

                // Restore zone colours
                foreach (var zoneVm in deviceVm.Zones)
                {
                    var savedZone = savedDevice.Zones?.FirstOrDefault(
                        z => string.Equals(z.ZoneName, zoneVm.Name, StringComparison.OrdinalIgnoreCase));
                    if (savedZone != null)
                    {
                        // Set colors before IsGradient, so the final setter applies it correctly.
                        zoneVm.GradientEndColor = System.Windows.Media.Color.FromRgb(
                            savedZone.GradientEndR, savedZone.GradientEndG, savedZone.GradientEndB);
                        zoneVm.IsGradient = savedZone.IsGradient;
                        
                        zoneVm.SelectedColor = System.Windows.Media.Color.FromRgb(
                            savedZone.ColorR, savedZone.ColorG, savedZone.ColorB);
                    }
                }
            }
        }
        catch
        {
            // Silently fail — if the file is corrupt, start fresh
        }
    }
}

/// <summary>Root JSON model for persisted RGB settings.</summary>
public class RgbPersistedState
{
    public List<RgbDeviceState> Devices { get; set; } = new();
}

/// <summary>Per-device persisted state.</summary>
public class RgbDeviceState
{
    public string DeviceName { get; set; } = "";
    public int DeviceId { get; set; }
    public string? SelectedMode { get; set; }
    public List<RgbZoneState> Zones { get; set; } = new();
}

/// <summary>Per-zone persisted colour state.</summary>
public class RgbZoneState
{
    public string ZoneName { get; set; } = "";
    public byte ColorR { get; set; }
    public byte ColorG { get; set; }
    public byte ColorB { get; set; }
    public bool IsGradient { get; set; }
    public byte GradientEndR { get; set; }
    public byte GradientEndG { get; set; }
    public byte GradientEndB { get; set; }
}
