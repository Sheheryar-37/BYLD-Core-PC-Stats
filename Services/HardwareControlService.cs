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

    public HardwareControlService()
    {
        Log("Initializing HardwareControlService...");
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true // Required for Fan Control
        };
        try
        {
            _computer.Open();
            Log("LibreHardwareMonitor Computer opened successfully.");
            VerifyRing0OrReclaim();
        }
        catch (Exception ex)
        {
            Log($"Error opening Computer: {ex.Message}");
        }
    }

    /// <summary>
    /// Ring0 sanity check: a foreign WinRing0 instance (e.g. OpenRGB's) lets
    /// Computer.Open() succeed while every MSR-based sensor reads 0. When the
    /// CPU temperature is unreadable, reclaim the driver and reopen once.
    /// </summary>
    private void VerifyRing0OrReclaim()
    {
        if (CanReadCpuTemperature()) return;

        Log("[Ring0] CPU temperature unreadable after open — reclaiming WinRing0 and reopening…");
        bool reclaimed = KernelDriverService.ForceReclaim(
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        if (!reclaimed)
        {
            Log("[Ring0] Reclaim failed — MSR sensors will stay unavailable this session.");
            return;
        }

        ReopenComputer();
        Log(CanReadCpuTemperature()
            ? "[Ring0] Reclaim successful — CPU temperature now readable ✓"
            : "[Ring0] Reclaim did not restore sensor reads.");
    }

    private bool CanReadCpuTemperature()
    {
        var cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        if (cpu == null) return false;

        cpu.Update();
        return cpu.Sensors.Any(s => s.SensorType == SensorType.Temperature && s.Value > 0);
    }

    private void ReopenComputer()
    {
        try
        {
            _computer.Close();
            _computer.Open();
        }
        catch (Exception ex)
        {
            Log($"[Ring0] Reopen failed: {ex.Message}");
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

    public void SetFanSpeed(ISensor controlSensor, float percentage)
    {
        if (controlSensor == null || controlSensor.SensorType != SensorType.Control) return;
        
        try
        {
            if (controlSensor.Control != null)
            {
                // percentage is 0-100
                controlSensor.Control.SetSoftware(percentage);
                Log($"[FAN] Set control '{controlSensor.Name}' to {percentage}%");
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

    /// <summary>
    /// Connects to the local OpenRGB server.
    /// </summary>
    public bool ConnectRgbServer(string ip = "127.0.0.1", int port = 6742)
    {
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
            _rgbClient = new OpenRGB.NET.OpenRgbClient(name: "PC Stats Monitor", ip: ip, port: port);
            _rgbClient.Connect();
            _isConnectedToRgb = _rgbClient.Connected;
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

    public List<OpenRGB.NET.Device> GetRgbDevices()
    {
        Log("Requesting all OpenRGB devices from server...");
        if (!_isConnectedToRgb || _rgbClient == null) 
        {
            Log("Not connected to OpenRGB.");
            return new List<OpenRGB.NET.Device>();
        }
        
        try
        {
            var devices = _rgbClient.GetAllControllerData().ToList();
            Log($"Received {devices.Count} OpenRGB devices.");
            return devices;
        }
        catch (Exception ex)
        {
            Log($"Error fetching OpenRGB devices: {ex.Message}");
            return new List<OpenRGB.NET.Device>();
        }
    }

    public void UpdateRgbZoneColor(int deviceId, int zoneId, OpenRGB.NET.Color color)
    {
        if (!_isConnectedToRgb || _rgbClient == null) return;
        
        try
        {
            var device = _rgbClient.GetControllerData(deviceId);
            var zone = device.Zones[zoneId];
            var colors = Enumerable.Repeat(color, (int)zone.LedCount).ToArray();
            _rgbClient.UpdateZoneLeds(deviceId, zoneId, colors);
        }
        catch { /* Handle connection or index errors */ }
    }

    public void UpdateRgbZoneColors(int deviceId, int zoneId, OpenRGB.NET.Color[] colors)
    {
        if (!_isConnectedToRgb || _rgbClient == null) return;
        
        try
        {
            _rgbClient.UpdateZoneLeds(deviceId, zoneId, colors);
        }
        catch { /* Handle connection or index errors */ }
    }

    public void RequestRgbEffect(int deviceId, string effectName)
    {
        if (!_isConnectedToRgb || _rgbClient == null) return;
        
        try
        {
            var device = _rgbClient.GetControllerData(deviceId);
            var modeIndex = Array.FindIndex(device.Modes, m => m.Name.Equals(effectName, StringComparison.OrdinalIgnoreCase));
            if (modeIndex >= 0)
            {
                var mode = device.Modes[modeIndex];
                // Only force Custom Mode if we are switching to direct LED control
                if (mode.Name.Contains("Direct", StringComparison.OrdinalIgnoreCase) || 
                    mode.Name.Contains("Custom", StringComparison.OrdinalIgnoreCase))
                {
                    _rgbClient.SetCustomMode(deviceId); 
                }
                _rgbClient.UpdateMode(deviceId, modeIndex);
            }
        }
        catch { }
    }

    public void Dispose()
    {
        _computer.Close();
        if (_rgbClient != null && _rgbClient.Connected)
        {
            _rgbClient.Dispose();
        }
    }
}
