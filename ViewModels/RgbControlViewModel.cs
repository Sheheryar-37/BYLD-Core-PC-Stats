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

    /// <summary>The device this zone belongs to, so a per-zone colour pick can
    /// switch the whole device into a colour-capable mode (mode is device-level).</summary>
    public RgbDeviceViewModel? Owner { get; set; }

    private System.Windows.Media.Color _selectedColor;
    public System.Windows.Media.Color SelectedColor
    {
        get => _selectedColor;
        set
        {
            if (SetProperty(ref _selectedColor, value))
            {
                // Switch the device into a colour-capable mode FIRST, otherwise a device sitting
                // in an effect (Rainbow, etc.) ignores the per-LED write and the pick does nothing
                // — only "apply to all" used to do this (client round 18, item 8).
                Owner?.EnsureColorCapableMode(value);
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

    private void ApplyColor(bool force = false)
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
            _hardwareService.UpdateRgbZoneColors(_deviceId, _zoneId, colors, force);
        }
        else
        {
            var orgbColor = new OpenRGB.NET.Color(SelectedColor.R, SelectedColor.G, SelectedColor.B);
            _hardwareService.UpdateRgbZoneColor(_deviceId, _zoneId, orgbColor, force);
        }

        // Persist the change
        RgbSettingsPersistence.SaveCurrentState();
    }

    /// <summary>Re-sends the currently selected colours to the hardware, forcing
    /// the write through the duplicate guard — this is a deliberate user action
    /// (apply-to-all / colour pick), not the automatic binding cascade.</summary>
    public void ReapplyColor() => ApplyColor(force: true);

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

    private bool _showOnWidget = true;
    /// <summary>Whether this device appears on the 7" RGB screen. Hidden devices still show in
    /// RGB Control so they can be re-shown (client round 18, item 9).</summary>
    public bool ShowOnWidget
    {
        get => _showOnWidget;
        set => SetProperty(ref _showOnWidget, value);
    }

    private string? _selectedMode;
    public string? SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (SetProperty(ref _selectedMode, value) && value != null)
                ApplyModeToHardware(value);
        }
    }

    /// <summary>
    /// Sends a mode to the hardware and persists it, WITHOUT requiring the selection to have
    /// changed. Re-picking the mode a device is already nominally in has to reach the hardware:
    /// the device may have been left on something else by an effect or a colour apply, and the
    /// silent no-op made effect selection look broken (client round 19, item 9).
    /// </summary>
    public void ApplyModeToHardware(string modeName)
    {
        SetProperty(ref _selectedMode, modeName, nameof(SelectedMode));

        if (!HardwareControlService.IsDemoMode)
            SendModeToHardware(modeName);
        RgbSettingsPersistence.SaveCurrentState();
    }

    /// <summary>
    /// Sends a mode change to the hardware, carrying the current zone colour for
    /// colour-capable modes. Without the colour, a mode-only switch to e.g. ENE
    /// DRAM "Static" leaves each identical stick showing its own STORED colour —
    /// so two RAM sticks disagreed, one going dark (client round 9, item 8).
    /// </summary>
    private void SendModeToHardware(string modeName)
    {
        bool colorMode = modeName.Equals("Static", StringComparison.OrdinalIgnoreCase) ||
                         modeName.Equals("Direct", StringComparison.OrdinalIgnoreCase);
        var firstZone = Zones.FirstOrDefault();

        if (colorMode && firstZone != null)
        {
            var c = firstZone.SelectedColor;
            // force: a per-device mode change is a deliberate user action and must
            // bypass the duplicate guard, or switching back to a mode the device is
            // already nominally in does nothing (client round 11, item 5).
            _hardwareService.RequestRgbEffect(DeviceId, modeName, new OpenRGB.NET.Color(c.R, c.G, c.B), force: true);
        }
        else
        {
            _hardwareService.RequestRgbEffect(DeviceId, modeName);
        }
    }

    /// <summary>
    /// Switches the lighting mode and sends the colour WITH the mode change, for
    /// modes that carry mode-specific colours (e.g. ENE DRAM "Static"). Used by
    /// apply-to-all so the device doesn't light up with its stale stored colour.
    /// </summary>
    /// <summary>
    /// Switches the device into a colour-capable mode (preferring "Static", else
    /// "Direct") and sends the colour WITH the switch, so a following per-LED write
    /// actually displays. Devices sitting in a hardware effect (Rainbow, Breathing…)
    /// ignore or black out on direct LED writes — ENE DRAM does. "Static" is
    /// preferred because the hardware stores it, while "Direct" needs continuous
    /// refresh and drops back to black on ENE DRAM once writes stop. No-op when the
    /// device offers neither mode.
    /// </summary>
    public void EnsureColorCapableMode(System.Windows.Media.Color color)
    {
        var colorMode = PickColorCapableMode();
        if (colorMode != null)
            ApplyModeWithColor(colorMode, color);
    }

    /// <summary>
    /// Chooses the mode used to display a chosen colour: "Static" (which the hardware stores)
    /// in preference to "Direct" (which is volatile — OpenRGB greys out "Save to Device" in
    /// Direct, and ENE DRAM reverts once writes stop).
    ///
    /// Deliberately NOT special-cased to force DRAM onto Direct: the "(active mode: Rainbow)"
    /// in the client's log is written from the CACHED device snapshot taken at connect time,
    /// so it is a stale-logging artifact and NOT evidence that the Static switch failed.
    /// Switching DRAM to Direct on that basis risked leaving the client's RAM dark.
    /// </summary>
    private string? PickColorCapableMode()
    {
        return Modes.FirstOrDefault(m => m == "Static") ?? Modes.FirstOrDefault(m => m == "Direct");
    }

    public void ApplyModeWithColor(string modeName, System.Windows.Media.Color color)
    {
        if (!Modes.Contains(modeName)) return;

        if (!HardwareControlService.IsDemoMode)
        {
            var orgb = new OpenRGB.NET.Color(color.R, color.G, color.B);
            _hardwareService.RequestRgbEffect(DeviceId, modeName, orgb, force: true);
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
            Zones.Add(new RgbZoneViewModel(device.Zones[i], deviceId, i, hardwareService) { Owner = this });
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
            // Do NOT gate on SetProperty returning true. Choosing a mode is a deliberate action
            // and must always reach the hardware — a device whose SelectedMode already equalled
            // the choice (very common, because applying a colour puts every device into Static)
            // silently did nothing, which is why "apply to all" effects appeared dead and never
            // even produced a log line (client round 19, item 9).
            SetProperty(ref _selectedCommonMode, value);
            if (value == null) return;

            foreach (var device in Devices)
            {
                if (device.Modes.Contains(value)) device.ApplyModeToHardware(value);
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

        // Apply-all touches every zone, and each write otherwise persists synchronously —
        // that fired ~10 JSON saves on the UI thread and made a second apply-all feel stuck
        // (client round 15, item 8). Suppress the per-write saves and persist once at the end.
        RgbSettingsPersistence.SuppressSave = true;
        try
        {
            foreach (var device in Devices)
                ApplyColorToDevice(device, color, isGradient, endColor);
        }
        finally
        {
            RgbSettingsPersistence.SuppressSave = false;
        }
        RgbSettingsPersistence.SaveCurrentState();
    }

    private static void ApplyColorToDevice(RgbDeviceViewModel device,
        System.Windows.Media.Color color, bool isGradient, System.Windows.Media.Color endColor)
    {
        // Switch the device to a colour-capable mode first (shared with the
        // per-zone picker), then write each zone once.
        device.EnsureColorCapableMode(color);

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

    /// <summary>
    /// The "apply to all" list offers ONLY the modes every detected device supports, so choosing
    /// one always affects the whole system. Round 18 briefly widened this to the union of all
    /// modes, but that listed effects most devices could not honour and made the bar look broken;
    /// the client asked for common-only back (round 19, item 8). Per-device dropdowns still list
    /// every mode that device supports.
    /// </summary>
    private void RebuildCommonModes()
    {
        CommonModes.Clear();
        if (Devices.Count == 0) return;

        var common = Devices.Select(d => d.Modes.AsEnumerable())
                            .Aggregate((a, b) => a.Intersect(b));

        foreach (var mode in common)
            CommonModes.Add(mode);
    }

    public ICommand ConnectCommand { get; }
    public ICommand RefreshCommand { get; }

    private System.Windows.Threading.DispatcherTimer? _autoRefreshTimer;

    private readonly IThemeService? _themeService;

    /// <summary>
    /// The devices shown on the 7" RGB screen: the user's visible selection, in the user's chosen
    /// order (client round 18, items 9 and 13). Kept separate from <see cref="Devices"/> so RGB
    /// Control still lists every device even when some are hidden from the 7" display.
    /// </summary>
    public ObservableCollection<RgbDeviceViewModel> VisibleDevices { get; } = new();

    /// <summary>
    /// EVERY device in the user's chosen order — what the RGB Control list binds to. The raw
    /// <see cref="Devices"/> collection stays in hardware-detection order (the auto-refresh
    /// comparison depends on it), so binding the UI straight to it meant reordering a device
    /// changed the 7" display but not the list the user was looking at, which read as the
    /// buttons doing nothing (client round 18 follow-up).
    /// </summary>
    public ObservableCollection<RgbDeviceViewModel> OrderedDevices { get; } = new();

    public RgbControlViewModel(HardwareControlService hardwareService, IThemeService? themeService = null)
    {
        _hardwareService = hardwareService;
        _themeService = themeService;
        ConnectCommand = new RelayCommand(_ => Connect());
        RefreshCommand = new RelayCommand(_ => LoadDevices(), _ => IsConnected);
        Devices.CollectionChanged += (_, _) => RebuildDeviceViews();
        StartAutoRefresh();
    }

    /// <summary>Rebuilds the ordered and visible device views from the saved order and hidden set.</summary>
    public void RebuildDeviceViews()
    {
        var theme = _themeService?.CurrentTheme;
        IEnumerable<RgbDeviceViewModel> all = Devices;
        if (theme != null)
            all = all.OrderBy(d => Models.ThemeConfig.DisplayOrderIndex(theme.RgbDisplayOrder, d.Name));

        var ordered = all.ToList();
        ReplaceAll(OrderedDevices, ordered);
        ReplaceAll(VisibleDevices, ordered.Where(d => !IsHiddenFromWidget(d.Name)));
    }

    private static void ReplaceAll(ObservableCollection<RgbDeviceViewModel> target,
        IEnumerable<RgbDeviceViewModel> items)
    {
        target.Clear();
        foreach (var item in items) target.Add(item);
    }

    private bool IsHiddenFromWidget(string deviceName) =>
        _themeService?.CurrentTheme.HiddenRgbDeviceNames.Contains(deviceName) == true;

    /// <summary>Applies each device's saved 7"-display visibility after the list is (re)built.</summary>
    private void ApplySavedWidgetVisibility()
    {
        foreach (var device in Devices)
            device.ShowOnWidget = !IsHiddenFromWidget(device.Name);
        RebuildDeviceViews();
    }

    /// <summary>Shows or hides a device on the 7" RGB screen and persists the choice.</summary>
    public void SetDeviceWidgetVisibility(RgbDeviceViewModel device, bool visible)
    {
        device.ShowOnWidget = visible;
        if (_themeService == null) return;

        var hidden = _themeService.CurrentTheme.HiddenRgbDeviceNames;
        if (visible) hidden.Remove(device.Name);
        else if (!hidden.Contains(device.Name)) hidden.Add(device.Name);

        _themeService.SaveTheme();
        RebuildDeviceViews();
    }

    /// <summary>
    /// Moves a device one place earlier/later on the 7" display and persists the new order
    /// (client round 18, item 13). The order list is rewritten from the current visible order so
    /// devices the user never touched keep a stable position.
    /// </summary>
    public void MoveDeviceOnWidget(RgbDeviceViewModel device, int delta)
    {
        if (_themeService == null) return;

        // Order the FULL list, not just the visible one, so hidden devices keep a stable
        // position and re-showing one puts it back where the user left it.
        var names = OrderedDevices.Select(d => d.Name).ToList();
        int from = names.IndexOf(device.Name);
        int to = from + delta;
        if (from < 0 || to < 0 || to >= names.Count) return;

        names.RemoveAt(from);
        names.Insert(to, device.Name);
        _themeService.CurrentTheme.RgbDisplayOrder = names;
        _themeService.SaveTheme();
        RebuildDeviceViews();
    }

    /// <summary>
    /// Periodically re-enumerates OpenRGB devices so newly connected hardware
    /// appears without the user pressing Refresh. The list is only rebuilt
    /// when the device set actually changes, to avoid UI flicker.
    /// </summary>
    /// <summary>
    /// True only while a screen that needs a live device LIST is on show (the Settings RGB tab).
    /// Re-enumerating devices makes OpenRGB re-read every controller over the SMBus, which stalls
    /// the whole machine on DRAM modules; before this view-model became session-long that only
    /// happened while Settings was open, and running it 24/7 is what made the client's system
    /// bog down badly (client round 19, item 1). The 7" screen only displays already-loaded
    /// devices, so it does not need this.
    /// </summary>
    public bool EnableDeviceListPolling { get; set; }

    private void StartAutoRefresh()
    {
        if (HardwareControlService.IsDemoMode) return;

        _autoRefreshTimer = new System.Windows.Threading.DispatcherTimer
        {
            // Device add/remove is rare, and each tick can cost an SMBus sweep — keep it slow.
            Interval = TimeSpan.FromSeconds(30)
        };
        _autoRefreshTimer.Tick += async (s, e) => await AutoRefreshDevicesAsync();
        _autoRefreshTimer.Start();
    }

    /// <summary>Stops the auto-refresh timer. Call when the hosting view closes.</summary>
    public void StopAutoRefresh()
    {
        _autoRefreshTimer?.Stop();
    }

    private int _reconnectTicks;

    private async Task AutoRefreshDevicesAsync()
    {
        // Demo mode fakes IsConnected — polling the real server here would
        // return an empty list and wipe the demo cards.
        if (HardwareControlService.IsDemoMode) return;
        if (IsLoading || _connecting) return;

        // A write can find the transport dead and drop the service-side connection while our
        // flag still reads "connected". Reconnect promptly when that happens, rather than
        // letting every following write silently no-op (client round 17: "only worked once").
        if (IsConnected && !_hardwareService.IsRgbConnected)
        {
            IsConnected = false;
            Connect();
            return;
        }

        if (!IsConnected)
        {
            TryReconnect();
            return;
        }

        // Skip the device sweep unless a screen that needs a live list is open. This is the
        // expensive part: it makes OpenRGB re-read every controller over the SMBus.
        if (!EnableDeviceListPolling) return;

        // Network round-trip on the thread pool; UI updates back on the dispatcher.
        var devices = await Task.Run(() => _hardwareService.GetRgbDevices());
        if (!HasDeviceListChanged(devices)) return;

        Devices.Clear();
        for (int i = 0; i < devices.Count; i++)
            Devices.Add(new RgbDeviceViewModel(devices[i], i, _hardwareService));

        RebuildCommonModes();
        ApplySavedWidgetVisibility();
        // Property-level restore only — the full hardware reapply happens once
        // per connect in LoadDevicesAsync, not on every device-list change.
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

    private bool _connecting;

    /// <summary>
    /// Fire-and-forget wrapper for existing callers; the real work runs in
    /// <see cref="ConnectAsync"/> off the UI thread.
    /// </summary>
    public void Connect() => _ = ConnectAsync();

    /// <summary>
    /// Connects to the OpenRGB server and loads devices. All socket work runs
    /// on the thread pool — the round-6 client logs showed the UI thread
    /// hard-blocked for 52 s inside a synchronous OpenRGB call, so no OpenRGB
    /// I/O may ever run on the dispatcher thread.
    /// </summary>
    public async Task ConnectAsync()
    {
        if (_connecting) return;
        _connecting = true;

        try
        {
            IsLoading = true;
            IsConnected = HardwareControlService.IsDemoMode ||
                          await Task.Run(() => _hardwareService.ConnectRgbServer());
            if (IsConnected)
                await LoadDevicesAsync();
        }
        finally
        {
            IsLoading = false;
            _connecting = false;
        }
    }

    /// <summary>Fire-and-forget wrapper for existing callers.</summary>
    public void LoadDevices() => _ = LoadDevicesAsync();

    public async Task LoadDevicesAsync()
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
            // Fetch on the thread pool — GetRgbDevices is a network round-trip.
            var devices = await Task.Run(() => _hardwareService.GetRgbDevices());

            // If we connected but found 0 devices, it's highly likely OpenRGB
            // needs to run as Admin to see SMBus/USB RGB controllers.
            if (devices.Count == 0)
            {
                ShowAdminWarning = true;
                devices = await Task.Run(RetryDevicesAsAdmin);
            }

            for (int i = 0; i < devices.Count; i++)
            {
                Devices.Add(new RgbDeviceViewModel(devices[i], i, _hardwareService));
            }
            ShowAdminWarning = devices.Count == 0;
        }

        RebuildCommonModes();
        ApplySavedWidgetVisibility();

        // Restore any previously-saved settings (colours + modes). The hardware
        // reapply must ONLY run when a saved state genuinely existed — on a fresh
        // install the view-model defaults (white, whatever mode the device woke
        // up in) would otherwise be forced onto the hardware (client round 7).
        if (RgbSettingsPersistence.RestoreState(this))
            ReapplyStateToHardware();

        IsLoading = false;
    }

    /// <summary>
    /// Restarts OpenRGB elevated, waits for the server, and re-fetches the
    /// device list. Runs on the thread pool — it sleeps for seconds.
    /// </summary>
    private List<Device> RetryDevicesAsAdmin()
    {
        _hardwareService.RestartOpenRgbAsAdmin();
        System.Threading.Thread.Sleep(3000);
        return _hardwareService.ConnectRgbServer()
            ? _hardwareService.GetRgbDevices()
            : new List<Device>();
    }

    /// <summary>
    /// Pushes the restored modes and colours to the hardware unconditionally.
    /// After a PC shutdown the devices boot with their own defaults; restoring
    /// only the view-model properties can no-op when values look unchanged, so
    /// the client's saved colours never reached the hardware (round-6 item 1).
    /// The writes go through the deduped background queue, so this is cheap.
    /// </summary>
    private void ReapplyStateToHardware()
    {
        if (HardwareControlService.IsDemoMode) return;

        foreach (var device in Devices)
            ReapplyDevice(device);
    }

    private static void ReapplyDevice(RgbDeviceViewModel device)
    {
        var firstZone = device.Zones.FirstOrDefault();
        if (firstZone == null) return;

        ApplyDeviceMode(device, firstZone.SelectedColor);

        foreach (var zone in device.Zones)
            zone.ReapplyColor();
    }

    /// <summary>
    /// Re-applies a device's saved mode. A saved colour mode (Static/Direct) is routed through
    /// <see cref="RgbDeviceViewModel.EnsureColorCapableMode"/> so the DRAM lands on Direct (which
    /// actually shows the colour) rather than the stored-but-broken Static; a saved effect
    /// (Rainbow, Breathing…) is re-applied as-is (client round 18, item 9).
    /// </summary>
    private static void ApplyDeviceMode(RgbDeviceViewModel device, System.Windows.Media.Color color)
    {
        if (device.SelectedMode is not { } mode) return;
        if (IsColorMode(mode)) device.EnsureColorCapableMode(color);
        else device.ApplyModeWithColor(mode, color);
    }

    private static bool IsColorMode(string mode) =>
        mode.Equals("Static", StringComparison.OrdinalIgnoreCase) ||
        mode.Equals("Direct", StringComparison.OrdinalIgnoreCase);
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

    /// <summary>Absolute path of the saved RGB settings file, so named profiles can bundle it.</summary>
    public static string SettingsFilePath => SettingsFile;

    /// <summary>Reference to the active ViewModel for saving.</summary>
    private static RgbControlViewModel? _activeViewModel;

    /// <summary>When true, SaveCurrentState is a no-op — used to coalesce the burst of
    /// writes from an apply-to-all into a single save at the end.</summary>
    public static bool SuppressSave { get; set; }

    /// <summary>
    /// Saves the current state of all RGB devices (mode + zone colours) to disk.
    /// Called automatically whenever a colour or mode is changed.
    /// </summary>
    public static void SaveCurrentState()
    {
        if (_activeViewModel == null || SuppressSave) return;

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
    /// <returns>True when a saved state existed and was applied — callers must
    /// not push anything to the hardware when this is false.</returns>
    public static bool RestoreState(RgbControlViewModel viewModel)
    {
        _activeViewModel = viewModel;

        if (!File.Exists(SettingsFile)) return false;

        try
        {
            var json = File.ReadAllText(SettingsFile);
            var state = JsonSerializer.Deserialize<RgbPersistedState>(json);
            if (state?.Devices == null) return false;

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

                // Restore zone colours SILENTLY — no hardware write or mode switch during load.
                // Setting the colour through its property would switch the device into a colour
                // mode here and clobber a restored effect; ReapplyStateToHardware applies the
                // correct mode + colour once, afterwards (client round 18, item 9).
                foreach (var zoneVm in deviceVm.Zones)
                {
                    var savedZone = savedDevice.Zones?.FirstOrDefault(
                        z => string.Equals(z.ZoneName, zoneVm.Name, StringComparison.OrdinalIgnoreCase));
                    if (savedZone != null)
                        zoneVm.SetColorsSilently(
                            System.Windows.Media.Color.FromRgb(savedZone.ColorR, savedZone.ColorG, savedZone.ColorB),
                            savedZone.IsGradient,
                            System.Windows.Media.Color.FromRgb(savedZone.GradientEndR, savedZone.GradientEndG, savedZone.GradientEndB));
                }
            }

            return true;
        }
        catch
        {
            // Silently fail — if the file is corrupt, start fresh
            return false;
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
