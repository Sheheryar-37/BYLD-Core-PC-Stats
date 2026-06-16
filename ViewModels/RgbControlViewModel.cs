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

    public RgbZoneViewModel(Zone zone, int deviceId, int zoneId, HardwareControlService hardwareService)
    {
        Name = ZoneNameMapper.GetDisplayName(zone.Name);
        LedCount = zone.LedCount;
        _deviceId = deviceId;
        _zoneId = zoneId;
        _hardwareService = hardwareService;
        _selectedColor = System.Windows.Media.Colors.White; // Default
    }

    public RgbZoneViewModel(string demoName, uint demoLedCount)
    {
        Name = demoName;
        LedCount = demoLedCount;
        _deviceId = -1;
        _zoneId = -1;
        _hardwareService = null!;
        _selectedColor = System.Windows.Media.Colors.White;
    }

    private void ApplyColor()
    {
        if (HardwareControlService.IsDemoMode) return;
        var orgbColor = new OpenRGB.NET.Color(SelectedColor.R, SelectedColor.G, SelectedColor.B);
        _hardwareService.UpdateRgbZoneColor(_deviceId, _zoneId, orgbColor);

        // Persist the change
        RgbSettingsPersistence.SaveCurrentState();
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

    public ICommand ConnectCommand { get; }
    public ICommand RefreshCommand { get; }

    public RgbControlViewModel(HardwareControlService hardwareService)
    {
        _hardwareService = hardwareService;
        ConnectCommand = new RelayCommand(_ => Connect());
        RefreshCommand = new RelayCommand(_ => LoadDevices(), _ => IsConnected);
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
                        ColorB = z.SelectedColor.B
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
}
