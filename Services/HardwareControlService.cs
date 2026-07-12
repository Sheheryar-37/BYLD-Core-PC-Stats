using LibreHardwareMonitor.Hardware;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PcStatsMonitor.Services;

public enum FanDetectionStatus
{
    Unknown,
    Success,
    NoFansDetected,
    SuperIoUnsupported,
    DriverBlocked
}

public class HardwareControlService : IDisposable
{
    private readonly Computer _computer;
    private OpenRGB.NET.OpenRgbClient? _rgbClient;
    private bool _isConnectedToRgb;
    
    public FanDetectionStatus FanStatus { get; private set; } = FanDetectionStatus.Unknown;
    
    public static bool IsDemoMode { get; set; } = false;

    private void Log(string message)
    {
        try
        {
            string logDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            if (!System.IO.Directory.Exists(logDir)) System.IO.Directory.CreateDirectory(logDir);
            string logFile = System.IO.Path.Combine(logDir, "hardware.log");
            System.IO.File.AppendAllText(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
        }
        catch { }
    }

    /// <summary>
    /// The single LibreHardwareMonitor Computer for the whole process, shared with
    /// HardwareMonitorService. LHM's Ring0 driver state and AMD's ADL library are
    /// process-global: a second Computer instance either corrupts the first or
    /// silently loses GPU fan/control sensors (whichever opens first wins ADL).
    /// </summary>
    public Computer Computer => _computer;

    public HardwareControlService()
    {
        Log("Initializing HardwareControlService...");
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true, // Required for Fan Control
            IsMemoryEnabled = true,     // The following are read by HardwareMonitorService,
            IsStorageEnabled = true,    // which shares this Computer instance.
            IsNetworkEnabled = true,
            IsBatteryEnabled = true
        };
        try
        {
            // Ring0 health is verified (and reclaimed if needed) at app startup by
            // KernelDriverService.VerifyRing0WithProbe, BEFORE this instance opens.
            // Never Close/reopen the Computer here at runtime: LibreHardwareMonitor's
            // Ring0 state is process-global and other instances would be corrupted.
            _computer.Open();
            Log("LibreHardwareMonitor Computer opened successfully.");
        }
        catch (Exception ex)
        {
            Log($"Error opening Computer: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets fans that specifically expose fan speed/RPM sensors (ISensor of type Fan)
    /// </summary>
    public List<ISensor> GetFanSensors()
    {
        var fanSensors = new List<ISensor>();
        FanStatus = FanDetectionStatus.Unknown;
        Log("═══════════════════════════════════════════════════════════");
        Log("DEEP FAN SCAN — Starting comprehensive fan detection...");
        Log($"Total hardware items in Computer: {_computer.Hardware.Count}");
        
        // Check HVCI/VBS status
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(@"root\CIMv2", "SELECT * FROM Win32_DeviceGuard");
            foreach (System.Management.ManagementObject obj in searcher.Get())
            {
                var vbsState = obj["VirtualizationBasedSecurityStatus"]?.ToString() ?? "Unknown";
                Log($"[HVCI] VirtualizationBasedSecurityStatus = {vbsState} (0=Off, 1=Enabled, 2=Active)");
            }
        }
        catch (Exception ex) 
        { 
            Log($"[HVCI] Could not query DeviceGuard: {ex.Message}");
        }
        
        // Try reading HVCI from registry
        try
        {
            var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity");
            if (key != null)
            {
                var enabled = key.GetValue("Enabled")?.ToString() ?? "not found";
                Log($"[HVCI] Registry HVCI Enabled = {enabled} (1=Blocked, 0=Off)");
            }
            else
            {
                Log("[HVCI] Registry key not found — HVCI likely disabled or not configured.");
            }
        }
        catch (Exception ex) { Log($"[HVCI] Registry check failed: {ex.Message}"); }
        
        foreach (var hardware in _computer.Hardware)
        {
            Log($"────────────────────────────────────────");
            Log($"[HW] Name: {hardware.Name}");
            Log($"[HW] Type: {hardware.HardwareType}");
            Log($"[HW] Identifier: {hardware.Identifier}");
            Log($"[HW] Sensors count: {hardware.Sensors.Length}");
            Log($"[HW] Sub-hardware count: {hardware.SubHardware.Length}");
            hardware.Update();
            
            // Log ALL sensor types on this hardware for complete visibility
            foreach (var sensor in hardware.Sensors)
            {
                string tag = (sensor.SensorType == SensorType.Fan || sensor.SensorType == SensorType.Control) ? "★ FAN/CTRL" : "  sensor";
                Log($"  [{tag}] {sensor.SensorType}: \"{sensor.Name}\" = {sensor.Value} (ID: {sensor.Identifier})");
                
                if (sensor.SensorType == SensorType.Fan || sensor.SensorType == SensorType.Control)
                {
                    fanSensors.Add(sensor);
                }
            }
            
            if (hardware.HardwareType == HardwareType.Motherboard && hardware.SubHardware.Length == 0)
            {
                // No Super I/O access has two very different causes: the WinRing0
                // driver failed to load (fixable), or the I/O chip is genuinely
                // unsupported by LibreHardwareMonitor. Report the right one.
                FanStatus = KernelDriverService.DriverLoadFailed
                    ? FanDetectionStatus.DriverBlocked
                    : FanDetectionStatus.SuperIoUnsupported;
                Log($"  ⚠ MOTHERBOARD HAS NO SUB-HARDWARE — Super I/O chip is NOT accessible.");
                Log($"  ⚠ This means case fans (CPU_FAN, SYS_FAN, etc.) cannot be read.");
                Log(KernelDriverService.DriverLoadFailed
                    ? "  ⚠ Cause: the WinRing0 kernel driver failed to load this session."
                    : "  ⚠ Most likely cause: The I/O chip is not supported by LibreHardwareMonitor yet.");
            }
            
            foreach (var subHardware in hardware.SubHardware)
            {
                Log($"  [SUB-HW] Name: {subHardware.Name}");
                Log($"  [SUB-HW] Type: {subHardware.HardwareType}");
                Log($"  [SUB-HW] Identifier: {subHardware.Identifier}");
                Log($"  [SUB-HW] Sensors count: {subHardware.Sensors.Length}");
                subHardware.Update();
                foreach (var sensor in subHardware.Sensors)
                {
                    string tag = (sensor.SensorType == SensorType.Fan || sensor.SensorType == SensorType.Control) ? "★ FAN/CTRL" : "  sensor";
                    Log($"    [{tag}] {sensor.SensorType}: \"{sensor.Name}\" = {sensor.Value} (ID: {sensor.Identifier})");
                    
                    if (sensor.SensorType == SensorType.Fan || sensor.SensorType == SensorType.Control)
                    {
                        fanSensors.Add(sensor);
                    }
                }
            }
        }
        
        Log($"────────────────────────────────────────");
        Log($"DEEP FAN SCAN COMPLETE — Total fan/control sensors found: {fanSensors.Count}");
        
        if (FanStatus == FanDetectionStatus.Unknown)
        {
            if (KernelDriverService.DriverLoadFailed)
                FanStatus = FanDetectionStatus.DriverBlocked;
            else if (fanSensors.Count > 0)
                FanStatus = FanDetectionStatus.Success;
            else
                FanStatus = FanDetectionStatus.NoFansDetected;
        }

        if (fanSensors.Count == 0)
            Log("⚠ NO FAN SENSORS DETECTED.");
            
        Log("═══════════════════════════════════════════════════════════");
        return fanSensors;
    }

    /// <summary>
    /// Returns every temperature sensor with a live value, across all hardware
    /// and sub-hardware. Used to build the fan-curve temperature source list.
    /// </summary>
    public List<ISensor> GetTemperatureSensors()
    {
        var list = new List<ISensor>();
        if (IsDemoMode) return list;

        foreach (var hardware in _computer.Hardware)
            CollectTemperatureSensors(hardware, list);
        return list;
    }

    private static void CollectTemperatureSensors(IHardware hardware, List<ISensor> list)
    {
        hardware.Update();
        list.AddRange(hardware.Sensors.Where(
            s => s.SensorType == SensorType.Temperature && s.Value.HasValue));

        foreach (var sub in hardware.SubHardware)
            CollectTemperatureSensors(sub, list);
    }

    public void SetFanSpeed(ISensor controlSensor, float percentage) => SetFanSpeed(controlSensor, percentage, null);

    /// <summary>Sets a fan control to a percentage; <paramref name="reason"/> adds log context (e.g. which curve).</summary>
    public void SetFanSpeed(ISensor controlSensor, float percentage, string? reason)
    {
        if (controlSensor == null || controlSensor.SensorType != SensorType.Control) return;
        
        try
        {
            if (controlSensor.Control != null)
            {
                // percentage is 0-100
                controlSensor.Control.SetSoftware(percentage);
                Log($"[FAN] Set control '{controlSensor.Name}' to {percentage:F0}%{(reason == null ? "" : $" ({reason})")}");
            }
        }
        catch (Exception ex)
        {
            Log($"[FAN] ERROR setting fan control '{controlSensor.Name}' to {percentage}%: {ex.Message}\n{ex.StackTrace}");
        }
    }

    public void SetFanAuto(ISensor controlSensor)
    {
        if (controlSensor == null || controlSensor.SensorType != SensorType.Control) return;
        
        try
        {
            if (controlSensor.Control != null)
            {
                controlSensor.Control.SetDefault();
                Log($"[FAN] Set control '{controlSensor.Name}' to Auto");
            }
        }
        catch (Exception ex)
        {
            Log($"[FAN] ERROR setting fan control '{controlSensor.Name}' to Auto: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>Serializes every use of the OpenRGB client (create/replace, reads,
    /// writes). Two view-models plus the write worker share this service; without
    /// serialization a reconnect could replace the client under an in-flight
    /// operation, leaving the worker wedged on a dead socket (client round 7).</summary>
    private readonly object _rgbClientLock = new();

    /// <summary>
    /// Connects to the local OpenRGB server. Idempotent: when the current client
    /// is already connected, returns immediately instead of replacing it — both
    /// view-models call this and client churn breaks in-flight writes.
    /// </summary>
    public bool ConnectRgbServer(string ip = "127.0.0.1", int port = 6742)
    {
        lock (_rgbClientLock)
        {
            if (_isConnectedToRgb && _rgbClient?.Connected == true)
                return true;
        }

        Log("═══════════════════════════════════════════════════════════");
        Log($"OPENRGB CONNECTION — Attempting {ip}:{port}...");

        // Check if OpenRGB process is running
        try
        {
            var rgbProcesses = System.Diagnostics.Process.GetProcessesByName("OpenRGB");
            Log($"[RGB] OpenRGB processes currently running: {rgbProcesses.Length}");
            
            if (rgbProcesses.Length == 0)
            {
                Log("[RGB] OpenRGB not running. Attempting to start it as Administrator...");
                EnsureOpenRgbRunningAsAdmin();
                // Wait a moment for the server to start
                System.Threading.Thread.Sleep(3000);
            }
            else
            {
                foreach (var p in rgbProcesses)
                    Log($"  [RGB] PID={p.Id}");
            }
        }
        catch (Exception ex) { Log($"[RGB] Could not check OpenRGB process: {ex.Message}"); }
        
        try
        {
            lock (_rgbClientLock)
            {
                DisposeRgbClientQuietly();
                _rgbClient = new OpenRGB.NET.OpenRgbClient(name: "PC Stats Monitor", ip: ip, port: port, timeoutMs: 5000);
                _rgbClient.Connect();
                _isConnectedToRgb = _rgbClient.Connected;
            }
            Log($"[RGB] Connection success: {_isConnectedToRgb}");
            
            if (_isConnectedToRgb)
            {
                try
                {
                    var devices = _rgbClient.GetAllControllerData();
                    Log($"[RGB] Total RGB devices detected: {devices.Length}");
                    for (int i = 0; i < devices.Length; i++)
                    {
                        var d = devices[i];
                        Log($"  [RGB Device {i}] Name: {d.Name}, Type: {d.Type}, Zones: {d.Zones.Length}, LEDs: {d.Leds.Length}, Modes: {d.Modes.Length}");
                        foreach (var zone in d.Zones)
                            Log($"    [Zone] \"{zone.Name}\" — LEDs: {zone.LedCount}, Type: {zone.Type}");
                        foreach (var mode in d.Modes)
                            Log($"    [Mode] \"{mode.Name}\"");
                    }
                }
                catch (Exception ex) { Log($"[RGB] Error listing devices after connect: {ex.Message}"); }
            }
            
            Log("═══════════════════════════════════════════════════════════");
            return _isConnectedToRgb;
        }
        catch (Exception ex)
        {
            Log($"[RGB] OpenRGB connection FAILED: {ex.Message}");
            Log("[RGB] ⚠ OpenRGB server is not running. Ensure OpenRGB is installed and started in Server mode.");
            Log("═══════════════════════════════════════════════════════════");
            _isConnectedToRgb = false;
            return false;
        }
    }

    /// <summary>Disposes the previous client before a replacement — abandoned
    /// sockets otherwise linger and surface as unobserved socket exceptions.</summary>
    private void DisposeRgbClientQuietly()
    {
        try
        {
            _rgbClient?.Dispose();
        }
        catch
        {
            // Old client may already be dead — nothing to do.
        }
        _rgbClient = null;
        _isConnectedToRgb = false;
    }

    public void EnsureOpenRgbRunningAsAdmin()
    {
        try
        {
            // First check if OpenRGB was bundled by the installer
            string openRgbPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OpenRGB", "OpenRGB.exe");
            
            if (!System.IO.File.Exists(openRgbPath))
            {
                // Fallback to Program Files
                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                openRgbPath = System.IO.Path.Combine(programFiles, "OpenRGB", "OpenRGB.exe");
                
                if (!System.IO.File.Exists(openRgbPath))
                {
                    openRgbPath = @"C:\Program Files\OpenRGB\OpenRGB.exe";
                }
            }

            if (System.IO.File.Exists(openRgbPath))
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = openRgbPath,
                    Arguments = "--server",
                    UseShellExecute = true,
                    Verb = "runas", // Forces UAC prompt if not already admin; inherits if parent is admin
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                };
                System.Diagnostics.Process.Start(psi);
                Log($"[RGB] Launched OpenRGB.exe from {openRgbPath} in server mode.");
            }
            else
            {
                Log("[RGB] ⚠ OpenRGB.exe not found in Program Files. Cannot start automatically.");
            }
        }
        catch (Exception ex)
        {
            Log($"[RGB] ERROR starting OpenRGB.exe: {ex.Message}");
        }
    }

    public void RestartOpenRgbAsAdmin()
    {
        try
        {
            var processes = System.Diagnostics.Process.GetProcessesByName("OpenRGB");
            foreach (var p in processes)
            {
                p.Kill();
            }
            System.Threading.Thread.Sleep(1000);
            EnsureOpenRgbRunningAsAdmin();
        }
        catch (Exception ex)
        {
            Log($"[RGB] ERROR restarting OpenRGB as admin: {ex.Message}");
        }
    }

    private int _lastRgbDeviceCount = -1;
    private bool _loggedRgbDisconnected;

    public List<OpenRGB.NET.Device> GetRgbDevices()
    {
        if (!_isConnectedToRgb || _rgbClient == null)
        {
            LogRgbDisconnectedOnce();
            return new List<OpenRGB.NET.Device>();
        }

        try
        {
            _loggedRgbDisconnected = false;
            List<OpenRGB.NET.Device> devices;
            lock (_rgbClientLock)
            {
                devices = _rgbClient.GetAllControllerData().ToList();
            }
            _rgbDeviceCache = devices.ToArray();
            LogRgbDeviceCountChange(devices.Count);
            return devices;
        }
        catch (Exception ex)
        {
            Log($"[RGB] Error fetching OpenRGB devices: {ex.Message}");
            return new List<OpenRGB.NET.Device>();
        }
    }

    private void LogRgbDisconnectedOnce()
    {
        if (_loggedRgbDisconnected) return;
        Log("[RGB] Not connected to OpenRGB — device queries paused until reconnect.");
        _loggedRgbDisconnected = true;
    }

    private void LogRgbDeviceCountChange(int count)
    {
        if (count == _lastRgbDeviceCount) return;
        Log($"[RGB] Device list changed: {count} devices (was {_lastRgbDeviceCount}).");
        _lastRgbDeviceCount = count;
    }

    private static string Hex(OpenRGB.NET.Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    // ── RGB worker ──────────────────────────────────────────────────────────
    // Every OpenRGB socket operation runs on this single background worker.
    // The client's round-6 logs showed the UI thread hard-blocked for 52s in a
    // synchronous socket call after a large write burst: when the OpenRGB
    // server stalls, Send() blocks with no timeout — the UI must never wait
    // on it. Bounded queue: when the server is stuck we drop (and log) new
    // writes instead of building an unbounded backlog.
    private readonly System.Collections.Concurrent.BlockingCollection<Action> _rgbQueue =
        new(boundedCapacity: 64);
    private Task? _rgbWorker;

    private void EnqueueRgbOp(string description, Action op)
    {
        _rgbWorker ??= Task.Factory.StartNew(ProcessRgbQueue, TaskCreationOptions.LongRunning);
        if (!_rgbQueue.TryAdd(op))
            Log($"[RGB→] queue full — dropped: {description}");
    }

    private void ProcessRgbQueue()
    {
        foreach (var op in _rgbQueue.GetConsumingEnumerable())
            RunRgbOp(op);
    }

    private void RunRgbOp(Action op)
    {
        try
        {
            op();
        }
        catch (Exception ex)
        {
            Log($"[RGB→] Worker operation failed: {ex.Message}");
        }
    }

    // ── Device metadata cache ───────────────────────────────────────────────
    // Zone LED counts and mode lists come from here instead of a blocking
    // GetControllerData round-trip before every write.
    private volatile OpenRGB.NET.Device[] _rgbDeviceCache = Array.Empty<OpenRGB.NET.Device>();

    private OpenRGB.NET.Device? GetCachedDevice(int deviceId)
    {
        var cache = _rgbDeviceCache;
        return deviceId >= 0 && deviceId < cache.Length ? cache[deviceId] : null;
    }

    // ── Duplicate-write suppression ─────────────────────────────────────────
    // Guards against the automatic binding cascade (auto-refresh restore colliding
    // with settling bindings) that once hammered the DRAM SMBus. Kept SHORT so it
    // never blocks a deliberate re-apply, and user-initiated actions (apply-all,
    // colour picks, mode changes) pass force=true to bypass it entirely — the
    // 1-second window used to swallow the client's second apply-to-all.
    private static readonly TimeSpan DuplicateWriteWindow = TimeSpan.FromMilliseconds(250);
    private readonly Dictionary<string, (string payload, DateTime at)> _lastRgbWrites = new();

    /// <summary>True when a duplicate write should be suppressed. Always records
    /// the payload so the next automatic duplicate is caught; never suppresses
    /// when <paramref name="force"/> (a user-initiated action) is set.</summary>
    private bool IsDuplicateRgbWrite(string target, string payload, bool force)
    {
        lock (_lastRgbWrites)
        {
            bool duplicate = !force &&
                             _lastRgbWrites.TryGetValue(target, out var last) &&
                             last.payload == payload &&
                             DateTime.UtcNow - last.at < DuplicateWriteWindow;
            _lastRgbWrites[target] = (payload, DateTime.UtcNow);
            return duplicate;
        }
    }

    private static string ActiveModeName(OpenRGB.NET.Device device)
    {
        bool valid = device.ActiveModeIndex >= 0 && device.ActiveModeIndex < device.Modes.Length;
        return valid ? device.Modes[device.ActiveModeIndex].Name : "?";
    }

    public void UpdateRgbZoneColor(int deviceId, int zoneId, OpenRGB.NET.Color color, bool force = false)
    {
        if (!_isConnectedToRgb || _rgbClient == null) return;
        if (IsDuplicateRgbWrite($"zone:{deviceId}:{zoneId}", Hex(color), force))
        {
            Log($"[RGB→] deduped identical zone write dev={deviceId} zone={zoneId} {Hex(color)}");
            return;
        }

        EnqueueRgbOp($"zone write dev={deviceId} zone={zoneId}", () => WriteZoneColor(deviceId, zoneId, color));
    }

    private void WriteZoneColor(int deviceId, int zoneId, OpenRGB.NET.Color color)
    {
        try
        {
            var device = GetCachedDevice(deviceId);
            uint ledCount;
            string deviceName;
            string zoneName;
            lock (_rgbClientLock)
            {
                device ??= _rgbClient!.GetControllerData(deviceId);
                var zone = device.Zones[zoneId];
                ledCount = zone.LedCount;
                deviceName = device.Name;
                zoneName = zone.Name;
                _rgbClient!.UpdateZoneLeds(deviceId, zoneId, Enumerable.Repeat(color, (int)ledCount).ToArray());
            }
            Log($"[RGB→] '{deviceName}' zone '{zoneName}': wrote {ledCount} LEDs = {Hex(color)} " +
                $"(active mode: {ActiveModeName(device)})");
        }
        catch (Exception ex)
        {
            Log($"[RGB→] Zone write FAILED dev={deviceId} zone={zoneId} color={Hex(color)}: {ex.Message}");
        }
    }

    public void UpdateRgbZoneColors(int deviceId, int zoneId, OpenRGB.NET.Color[] colors, bool force = false)
    {
        if (!_isConnectedToRgb || _rgbClient == null) return;
        if (IsDuplicateRgbWrite($"zone:{deviceId}:{zoneId}", $"{Hex(colors[0])}→{Hex(colors[^1])}×{colors.Length}", force))
        {
            Log($"[RGB→] deduped identical gradient write dev={deviceId} zone={zoneId}");
            return;
        }

        EnqueueRgbOp($"gradient write dev={deviceId} zone={zoneId}", () => WriteZoneGradient(deviceId, zoneId, colors));
    }

    private void WriteZoneGradient(int deviceId, int zoneId, OpenRGB.NET.Color[] colors)
    {
        try
        {
            lock (_rgbClientLock)
            {
                _rgbClient!.UpdateZoneLeds(deviceId, zoneId, colors);
            }
            Log($"[RGB→] dev={deviceId} zone={zoneId}: wrote {colors.Length} LEDs gradient {Hex(colors[0])} → {Hex(colors[^1])}");
        }
        catch (Exception ex)
        {
            Log($"[RGB→] Gradient zone write FAILED dev={deviceId} zone={zoneId}: {ex.Message}");
        }
    }

    public void RequestRgbEffect(int deviceId, string effectName)
    {
        RequestRgbEffect(deviceId, effectName, null, force: true);
    }

    /// <summary>
    /// Switches a device's lighting mode. When <paramref name="color"/> is provided
    /// and the mode carries mode-specific colours (e.g. ENE DRAM "Static"), the
    /// colour is sent WITH the mode change — without this the device applies its
    /// stale stored mode colour (ENE defaults to red, which is what showed up on
    /// the client's Trident Z Neo during apply-to-all).
    /// </summary>
    public void RequestRgbEffect(int deviceId, string effectName, OpenRGB.NET.Color? color, bool force = false)
    {
        if (!_isConnectedToRgb || _rgbClient == null) return;
        if (IsDuplicateRgbWrite($"mode:{deviceId}", $"{effectName}|{(color == null ? "" : Hex(color.Value))}", force))
        {
            Log($"[RGB→] deduped identical mode switch dev={deviceId} '{effectName}'");
            return;
        }

        EnqueueRgbOp($"mode switch dev={deviceId} '{effectName}'", () => RunRgbEffectSwitch(deviceId, effectName, color));
    }

    private void RunRgbEffectSwitch(int deviceId, string effectName, OpenRGB.NET.Color? color)
    {
        try
        {
            ApplyRgbEffect(deviceId, effectName, color);
        }
        catch (Exception ex)
        {
            Log($"[RGB→] Mode switch FAILED dev={deviceId} mode='{effectName}': {ex.Message}");
        }
    }

    private void ApplyRgbEffect(int deviceId, string effectName, OpenRGB.NET.Color? color)
    {
        var device = GetCachedDevice(deviceId);
        lock (_rgbClientLock)
        {
            device ??= _rgbClient!.GetControllerData(deviceId);
        }

        var modeIndex = Array.FindIndex(device.Modes, m => m.Name.Equals(effectName, StringComparison.OrdinalIgnoreCase));
        if (modeIndex < 0)
        {
            Log($"[RGB→] '{device.Name}': mode '{effectName}' NOT FOUND — device offers: " +
                string.Join(", ", device.Modes.Select(m => m.Name)));
            return;
        }

        var mode = device.Modes[modeIndex];
        var modeColors = BuildModeColors(mode, color);
        lock (_rgbClientLock)
        {
            // Only force Custom Mode if we are switching to direct LED control
            if (mode.Name.Contains("Direct", StringComparison.OrdinalIgnoreCase) ||
                mode.Name.Contains("Custom", StringComparison.OrdinalIgnoreCase))
            {
                _rgbClient!.SetCustomMode(deviceId);
            }

            _rgbClient!.UpdateMode(deviceId, modeIndex, colors: modeColors);
        }
        Log($"[RGB→] '{device.Name}': mode → '{mode.Name}' " +
            $"(modeColors={(modeColors == null ? "unchanged" : $"{modeColors.Length}×{Hex(modeColors[0])}")})");
    }

    /// <summary>
    /// Builds the colour slots for a mode change: the target colour repeated for
    /// however many colours the mode expects. Null when no colour was requested
    /// or the mode has no colour slots (e.g. Rainbow, Spectrum Cycle).
    /// </summary>
    private static OpenRGB.NET.Color[]? BuildModeColors(OpenRGB.NET.Mode mode, OpenRGB.NET.Color? color)
    {
        if (color == null) return null;

        int slots = (int)Math.Max(mode.ColorMax, (uint)(mode.Colors?.Length ?? 0));
        if (slots == 0) return null;
        return Enumerable.Repeat(color.Value, slots).ToArray();
    }

    public void Dispose()
    {
        _rgbQueue.CompleteAdding();
        _computer.Close();
        if (_rgbClient != null && _rgbClient.Connected)
        {
            _rgbClient.Dispose();
        }
    }
}
