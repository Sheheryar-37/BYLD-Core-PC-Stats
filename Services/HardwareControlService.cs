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
        if (!AppLogging.Enabled) return;

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
            // PawnIO availability is checked at app startup by
            // PawnIoDriverService.EnsureAvailable, BEFORE this instance opens.
            // Never Close/reopen the Computer here at runtime: LibreHardwareMonitor's
            // driver state is process-global and other instances would be corrupted.
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
        
        // The HVCI/VBS probe that used to run here is gone: it only mattered while the
        // app used WinRing0, which Windows blocks under Memory Integrity. PawnIO is
        // Microsoft-signed and loads regardless, and the Win32_DeviceGuard query threw
        // "Invalid class" on the client's machine on every scan.

        foreach (var hardware in _computer.Hardware)
        {
            Log($"────────────────────────────────────────");
            Log($"[HW] Name: {hardware.Name}");
            Log($"[HW] Type: {hardware.HardwareType}");
            Log($"[HW] Identifier: {hardware.Identifier}");
            Log($"[HW] Sensors count: {hardware.Sensors.Length}");
            Log($"[HW] Sub-hardware count: {hardware.SubHardware.Length}");
            hardware.Update();
            
            // Log only fan/control sensors. Dumping every temp/voltage wrote ~675 lines
            // at startup once the Super I/O (37 sensors) came online — pure I/O that
            // added to the "throttle on open" report — while only fans matter here.
            foreach (var sensor in hardware.Sensors)
            {
                if (sensor.SensorType != SensorType.Fan && sensor.SensorType != SensorType.Control)
                    continue;
                Log($"  [★ FAN/CTRL] {sensor.SensorType}: \"{sensor.Name}\" = {sensor.Value} (ID: {sensor.Identifier})");
                fanSensors.Add(sensor);
            }
            
            if (hardware.HardwareType == HardwareType.Motherboard && hardware.SubHardware.Length == 0)
            {
                // No Super I/O access has two very different causes: PawnIO is not
                // installed (fixable), or the I/O chip is genuinely unsupported by
                // LibreHardwareMonitor. Report the right one.
                FanStatus = PawnIoDriverService.DriverUnavailable
                    ? FanDetectionStatus.DriverBlocked
                    : FanDetectionStatus.SuperIoUnsupported;
                Log($"  ⚠ MOTHERBOARD HAS NO SUB-HARDWARE — Super I/O chip is NOT accessible.");
                Log($"  ⚠ This means case fans (CPU_FAN, SYS_FAN, etc.) cannot be read.");
                Log(PawnIoDriverService.DriverUnavailable
                    ? "  ⚠ Cause: the PawnIO driver is not installed this session."
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
                    if (sensor.SensorType != SensorType.Fan && sensor.SensorType != SensorType.Control)
                        continue;
                    Log($"    [★ FAN/CTRL] {sensor.SensorType}: \"{sensor.Name}\" = {sensor.Value} (ID: {sensor.Identifier})");
                    fanSensors.Add(sensor);
                }
            }
        }
        
        Log($"────────────────────────────────────────");
        Log($"DEEP FAN SCAN COMPLETE — Total fan/control sensors found: {fanSensors.Count}");
        
        if (FanStatus == FanDetectionStatus.Unknown)
        {
            if (PawnIoDriverService.DriverUnavailable)
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

    /// <summary>
    /// Queues a fan control to a percentage; <paramref name="reason"/> adds log context
    /// (e.g. which curve). The driver write is ISA-bus port I/O that can block for tens of
    /// milliseconds, so it runs on a background worker — enabling app fan control writes
    /// every fan at once and doing that on the UI thread froze the app for a moment
    /// (client round 17, item 4). The <c>epoch</c> captured here lets the worker discard the
    /// write if the fan is released to automatic control before it runs.
    /// </summary>
    public void SetFanSpeed(ISensor controlSensor, float percentage, string? reason)
    {
        if (controlSensor == null || controlSensor.SensorType != SensorType.Control) return;

        long epoch = System.Threading.Interlocked.Read(ref _fanReleaseEpoch);
        EnqueueFanOp(() => WriteFanSpeed(controlSensor, percentage, reason, epoch));
    }

    /// <summary>
    /// The actual driver write, on the fan worker thread. The add-to-set, the epoch
    /// re-check and the <c>SetSoftware</c> call are done under the same lock that
    /// <see cref="RestoreAllFansToAuto"/> takes, so a release can never interleave and
    /// strand a fan: either this write completes and the fan is in the released snapshot,
    /// or the release bumps the epoch first and this write is discarded.
    /// </summary>
    private void WriteFanSpeed(ISensor sensor, float percentage, string? reason, long epoch)
    {
        lock (_softwareControlled)
        {
            if (epoch != System.Threading.Interlocked.Read(ref _fanReleaseEpoch) || sensor.Control == null) return;
            _softwareControlled.Add(sensor);
            sensor.Control.SetSoftware(percentage); // percentage is 0-100
        }

        Log($"[FAN] Set control '{sensor.Name}' to {percentage:F0}%{(reason == null ? "" : $" ({reason})")}");
    }

    public void SetFanAuto(ISensor controlSensor)
    {
        if (controlSensor == null || controlSensor.SensorType != SensorType.Control) return;

        // Invalidate any queued speed writes before releasing, so a stale background
        // "set 30%" cannot re-pin this fan after it is handed back to automatic control.
        System.Threading.Interlocked.Increment(ref _fanReleaseEpoch);

        try
        {
            if (controlSensor.Control != null)
            {
                controlSensor.Control.SetDefault();
                lock (_softwareControlled) _softwareControlled.Remove(controlSensor);
                Log($"[FAN] Set control '{controlSensor.Name}' to Auto");
            }
        }
        catch (Exception ex)
        {
            Log($"[FAN] ERROR setting fan control '{controlSensor.Name}' to Auto: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>Every control this app switched to software mode, so they can be handed back.</summary>
    private readonly HashSet<ISensor> _softwareControlled = new();

    /// <summary>
    /// Returns every fan this app took over to the motherboard's own control. MUST run
    /// on shutdown: a control left in software mode stays pinned at the last value the
    /// app wrote, so exiting used to leave the client's fans stopped at 30%. Runs
    /// synchronously on the calling thread (never queued) so it is guaranteed to finish
    /// on exit or crash even after the fan worker has stopped.
    /// </summary>
    public void RestoreAllFansToAuto()
    {
        // One epoch bump invalidates every queued speed write up front, so nothing the
        // worker still holds can re-pin a fan after this restore.
        System.Threading.Interlocked.Increment(ref _fanReleaseEpoch);

        ISensor[] controlled;
        lock (_softwareControlled)
        {
            controlled = _softwareControlled.ToArray();
        }

        foreach (var sensor in controlled)
            SetFanAuto(sensor);
    }

    // ── Fan-control write worker ────────────────────────────────────────────────
    // Every fan SPEED write runs on this single background worker so the UI thread never
    // blocks on the driver. Releasing a fan to automatic control (SetFanAuto /
    // RestoreAllFansToAuto) stays synchronous so it is guaranteed to complete on exit.
    private readonly System.Collections.Concurrent.BlockingCollection<Action> _fanQueue =
        new(new System.Collections.Concurrent.ConcurrentQueue<Action>());
    private Task? _fanWorker;

    // Bumped whenever a fan (or all fans) is released to automatic control. A queued speed
    // write captures the epoch at enqueue time and the worker discards it on a mismatch.
    private long _fanReleaseEpoch;

    private void EnqueueFanOp(Action op)
    {
        _fanWorker ??= Task.Factory.StartNew(ProcessFanQueue, TaskCreationOptions.LongRunning);
        if (!_fanQueue.IsAddingCompleted) _fanQueue.TryAdd(op);
    }

    // Small gap between consecutive fan writes so a bulk "apply to all fans" doesn't hold the
    // slow Super I/O / LPC bus in one continuous burst — that starved other bus traffic and made
    // the system stutter for a moment when enabling app fan control (client round 18, item 4).
    private const int FanWriteSpacingMs = 25;

    private void ProcessFanQueue()
    {
        foreach (var op in _fanQueue.GetConsumingEnumerable())
        {
            RunFanOp(op);
            System.Threading.Thread.Sleep(FanWriteSpacingMs);
        }
    }

    private void RunFanOp(Action op)
    {
        try { op(); }
        catch (Exception ex) { Log($"[FAN→] Worker operation failed: {ex.Message}"); }
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
            // The OpenRGB server takes a variable time to accept connections after launch —
            // it scans the SMBus first, which is slow on some boards. A single fixed wait +
            // one attempt either stalled or failed outright (client round 14: "takes a very
            // long time, sometimes doesn't connect"). Retry until it answers instead.
            if (!ConnectWithRetries(ip, port))
            {
                Log("[RGB] OpenRGB did not accept a connection after retries.");
                Log("═══════════════════════════════════════════════════════════");
                _isConnectedToRgb = false;
                return false;
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

    /// <summary>
    /// Tries to connect to OpenRGB repeatedly for up to ~20 seconds, so a server that is
    /// still starting up or mid-SMBus-scan is waited out rather than failing on the first
    /// try. Runs on the RGB connect task (off the UI thread), so the retries never freeze
    /// the interface. Returns true as soon as a connection is established.
    /// </summary>
    private bool ConnectWithRetries(string ip, int port)
    {
        const int maxAttempts = 20;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (TryConnectOnce(ip, port))
                return true;

            if (attempt < maxAttempts)
                System.Threading.Thread.Sleep(1000);
        }
        return false;
    }

    private bool TryConnectOnce(string ip, int port)
    {
        lock (_rgbClientLock)
        {
            try
            {
                DisposeRgbClientQuietly();
                _rgbClient = new OpenRGB.NET.OpenRgbClient(name: "PC Stats Monitor", ip: ip, port: port, timeoutMs: 2000);
                _rgbClient.Connect();
                _isConnectedToRgb = _rgbClient.Connected;
                return _isConnectedToRgb;
            }
            catch
            {
                DisposeRgbClientQuietly();
                return false;
            }
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

    /// <summary>True while the OpenRGB client holds a live connection. Flips to false when a
    /// write hits a dead transport, so the view-model can re-establish the connection instead
    /// of silently no-op'ing every subsequent write (client round 17: "only worked once").</summary>
    public bool IsRgbConnected => _isConnectedToRgb;

    /// <summary>
    /// Marks the RGB connection dead when an exception means the transport itself is gone
    /// (socket closed/reset, client disposed) — NOT for ordinary device/protocol errors, which
    /// are transient. A dead connection would otherwise stay flagged "connected" and swallow
    /// every following write, which is why RGB control stopped responding after the first use.
    /// </summary>
    private void MarkRgbDeadIfTransportError(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is System.Net.Sockets.SocketException || e is System.IO.IOException || e is ObjectDisposedException)
            {
                _isConnectedToRgb = false;
                Log("[RGB] Connection lost (dead transport) — will reconnect on the next refresh.");
                return;
            }
        }
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
                var leds = Enumerable.Repeat(color, (int)ledCount).ToArray();
                WriteZoneLedsReliably(deviceId, zoneId, leds);
            }
            // NB: the mode here comes from the CACHED device snapshot taken at the last device
            // scan, not a live read — labelled accordingly so it is not misread as the device's
            // current mode (that misreading caused a wrong DRAM diagnosis in round 18).
            Log($"[RGB→] '{deviceName}' zone '{zoneName}': wrote {ledCount} LEDs = {Hex(color)} " +
                $"(mode at last scan: {ActiveModeName(device)})");
        }
        catch (Exception ex)
        {
            Log($"[RGB→] Zone write FAILED dev={deviceId} zone={zoneId} color={Hex(color)}: {ex.Message}");
            MarkRgbDeadIfTransportError(ex);
        }
    }

    /// <summary>
    /// Writes a zone's LEDs, then writes them once more after a short pause. ENE DRAM
    /// (the client's RGB RAM) is driven over SMBus and drops occasional LEDs on a single
    /// write — he saw a colour change leave the top LEDs on their previous colour. A second
    /// identical write catches the LEDs the first one missed. Caller already holds the RGB
    /// client lock, and this runs on the RGB queue worker, so the pause never touches the UI.
    /// </summary>
    private void WriteZoneLedsReliably(int deviceId, int zoneId, OpenRGB.NET.Color[] leds)
    {
        _rgbClient!.UpdateZoneLeds(deviceId, zoneId, leds);
        System.Threading.Thread.Sleep(20);
        _rgbClient!.UpdateZoneLeds(deviceId, zoneId, leds);
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
                WriteZoneLedsReliably(deviceId, zoneId, colors);
            }
            Log($"[RGB→] dev={deviceId} zone={zoneId}: wrote {colors.Length} LEDs gradient {Hex(colors[0])} → {Hex(colors[^1])}");
        }
        catch (Exception ex)
        {
            Log($"[RGB→] Gradient zone write FAILED dev={deviceId} zone={zoneId}: {ex.Message}");
            MarkRgbDeadIfTransportError(ex);
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
            MarkRgbDeadIfTransportError(ex);
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
        _fanQueue.CompleteAdding();
        _computer.Close();
        if (_rgbClient != null && _rgbClient.Connected)
        {
            _rgbClient.Dispose();
        }

        // Shut down the OpenRGB server we launched, so the next run starts a FRESH one.
        // A reused server accumulated SMBus state across sessions and came back with its
        // controls dead on the second launch (client round 14, item 10). The app relaunches
        // it on connect, which is the same clean state as a first run.
        StopOpenRgbServer();
    }

    private void StopOpenRgbServer()
    {
        try
        {
            foreach (var proc in System.Diagnostics.Process.GetProcessesByName("OpenRGB"))
                TryKillOpenRgb(proc);
        }
        catch
        {
            // Best-effort on shutdown — a lingering server is preferable to blocking exit.
        }
    }

    private void TryKillOpenRgb(System.Diagnostics.Process proc)
    {
        try
        {
            proc.Kill();
            proc.WaitForExit(2000);
        }
        catch
        {
            // Already gone or access denied — nothing to do.
        }
    }
}
