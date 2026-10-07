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

    /// <summary>
    /// Writes a UI-level event to the same hardware log as the lighting commands.
    ///
    /// The RGB failure has repeatedly shown ONE successful batch followed by no further
    /// commands being attempted at all — no stall, no dedupe, no dropped writes. Everything
    /// below this point is already instrumented, so the remaining blind spot is the UI layer
    /// itself: whether the button fires, whether the picker opens, and what it returns. This
    /// makes that visible instead of inferred (client round 27).
    /// </summary>
    public void LogUiEvent(string message) => Log(message);

    /// <summary>Serialises hardware-log writes. File.AppendAllText opens the file exclusively,
    /// so two threads appending at once made one of them throw — and the catch below discarded
    /// the line silently.</summary>
    private static readonly object _logFileLock = new();

    /// <summary>
    /// Appends one line to the hardware log, and does NOT lose it under concurrency.
    ///
    /// The UI thread (button/picker events) and the lighting worker (command execution) write
    /// here at the same time during an apply-to-all. File.AppendAllText takes an exclusive
    /// handle, so the loser threw IOException "used by another process" and the line was
    /// dropped — the client's round-30 log contains exactly that exception, at the same second
    /// as the first colour batch, and its command lines are visibly out of order as a result.
    /// Several rounds of theories were built on this log; it has to be trustworthy, so writes
    /// are now serialised and retried rather than silently discarded.
    /// </summary>
    private void Log(string message)
    {
        if (!AppLogging.Enabled) return;

        try
        {
            string logDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            if (!System.IO.Directory.Exists(logDir)) System.IO.Directory.CreateDirectory(logDir);
            string logFile = System.IO.Path.Combine(logDir, "hardware.log");
            AppendLine(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
        }
        catch { }
    }

    /// <summary>Appends under a lock, retrying briefly if another process holds the file.</summary>
    private static void AppendLine(string logFile, string line)
    {
        lock (_logFileLock)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    using var stream = new System.IO.FileStream(
                        logFile, System.IO.FileMode.Append, System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite);
                    using var writer = new System.IO.StreamWriter(stream);
                    writer.Write(line);
                    return;
                }
                catch (System.IO.IOException)
                {
                    System.Threading.Thread.Sleep(15);
                }
            }
        }
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
        // Free the LPC/ISA bus BEFORE opening: a resident WinRing0 (registered by our own
        // bundled OpenRGB) holds that access, and LibreHardwareMonitor then reports
        // SubHardware=0 — no Super I/O, no case fans at all, even with PawnIO installed.
        // Confirmed on the client's PC, where the app itself reported WinRing0_1_2_0 and
        // WinRing0x64 present (client round 23, items 1 and 3).
        ReleaseConflictingBusDrivers("startup");

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

    private readonly object _fanWorkerLock = new();

    /// <summary>
    /// Queues a fan write, restarting the consumer if it has stopped. Same single-start flaw as
    /// the RGB worker had: once that task ended, fan writes would queue forever in silence
    /// (client round 28).
    /// </summary>
    private void EnqueueFanOp(Action op)
    {
        lock (_fanWorkerLock)
        {
            if (_fanWorker is not { IsCompleted: false })
            {
                if (_fanWorker != null)
                    Log($"[FAN→] The fan worker had stopped " +
                        $"({_fanWorker.Exception?.GetBaseException().Message ?? "ended without an error"}) — restarting it.");
                _fanWorker = Task.Factory.StartNew(ProcessFanQueue, TaskCreationOptions.LongRunning);
            }
        }

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

        // Remembered so we can tell below whether THIS call launched a new server.
        int serverPidBefore = _ownOpenRgbPid;

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
            else if (OwnsEveryRunningOpenRgb(rgbProcesses))
            {
                // OUR server, still running and still healthy — only the socket went. Restarting
                // it here is what turned a recoverable stall into "the RGB section disappeared":
                // the stall watchdog reconnects mid-session, this code called that live server an
                // orphan, killed it, and the replacement took ~9s to scan the SMBus, by which time
                // the watchdog fired again and killed THAT one too (client round 31, item 3).
                Log($"[RGB] Reusing the server this app started (PID={_ownOpenRgbPid}) — " +
                    "reconnecting the socket only.");
            }
            else
            {
                // A server we did NOT start is an ORPHAN from a previous session (older builds
                // started it detached, so it survived exit and kept loading the SMBus). Replace it
                // with one we own, so this install self-heals and the server cannot outlive the app.
                foreach (var p in rgbProcesses)
                    Log($"  [RGB] PID={p.Id} — orphaned server from an earlier session; replacing it.");

                StopOpenRgbServer();
                System.Threading.Thread.Sleep(500);
                EnsureOpenRgbRunningAsAdmin();
            }
        }
        catch (Exception ex) { Log($"[RGB] Could not check OpenRGB process: {ex.Message}"); }

        try
        {
            // The OpenRGB server takes a variable time to accept connections after launch —
            // it scans the SMBus first, which is slow on some boards. A single fixed wait +
            // one attempt either stalled or failed outright (client round 14: "takes a very
            // long time, sometimes doesn't connect"). Retry until it answers instead.
            _rgbIp = ip;
            _rgbPort = port;
            _lastRgbWriteUtc = DateTime.MinValue;   // a new connection has carried nothing yet
            if (!ConnectWithRetries(ip, port))
            {
                Log("[RGB] OpenRGB did not accept a connection after retries.");
                Log("═══════════════════════════════════════════════════════════");
                _isConnectedToRgb = false;
                return false;
            }
            Log($"[RGB] Connection success: {_isConnectedToRgb}");

            bool launchedServer = _ownOpenRgbPid != 0 && _ownOpenRgbPid != serverPidBefore;
            if (_isConnectedToRgb && launchedServer)
                WaitForDeviceDetectionToSettle();

            if (_isConnectedToRgb)
            {
                try
                {
                    var devices = _rgbClient!.GetAllControllerData();
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

    // ── OpenRGB 1.0: the server answers before detection has finished ───────
    /// <summary>A device count unchanged for this long, with devices found, means detection is done.</summary>
    private static readonly TimeSpan DetectionSettleWindow = TimeSpan.FromSeconds(5);

    /// <summary>How long a count of ZERO must hold before concluding the PC has no lighting devices.</summary>
    private static readonly TimeSpan DetectionNoDevicesWindow = TimeSpan.FromSeconds(15);

    /// <summary>Upper bound on the wait, however detection behaves.</summary>
    private static readonly TimeSpan DetectionMaxWait = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Waits for a freshly launched OpenRGB to finish detecting devices before the device list
    /// is read.
    ///
    /// OpenRGB 1.0 (bundled from round 32, for PawnIO) starts its SDK server BEFORE device
    /// detection; the build it replaces started it after. Measured on the dev machine: network
    /// thread at 9 ms, detection starting at 13 ms, client accepted at ~0.5 s, detection completed
    /// at 1.6 s — longer on a PC with SMBus RAM and USB lighting to scan. Reading the list straight
    /// after connecting would therefore return a partial or EMPTY list, and an empty list makes the
    /// view-model restart OpenRGB (RetryDevicesAsAdmin), which would kill detection mid-scan on every
    /// launch. So the count is polled until it has stopped changing. Only used when this call
    /// launched the server — a socket-only reconnect finds detection long finished.
    /// </summary>
    private void WaitForDeviceDetectionToSettle()
    {
        var started = DateTime.UtcNow;
        var lastChange = started;
        int count = ReadControllerCount();
        var progression = new List<int> { count };

        while (!DetectionHasSettled(count, DateTime.UtcNow - lastChange, DateTime.UtcNow - started))
        {
            System.Threading.Thread.Sleep(500);
            RestartStallBudget();   // on the lighting worker, this wait is progress, not a stall
            int now = ReadControllerCount();
            if (now == count) continue;
            count = now;
            lastChange = DateTime.UtcNow;
            progression.Add(now);
        }

        Log($"[RGB] Device detection settled after {(DateTime.UtcNow - started).TotalSeconds:F1}s " +
            $"(device count: {string.Join(" → ", progression)}).");
    }

    /// <summary>The server's current device count, or -1 if the connection cannot answer.</summary>
    private int ReadControllerCount()
    {
        try
        {
            lock (_rgbClientLock) return _rgbClient?.GetControllerCount() ?? -1;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>True once the count has held steady long enough, the connection failed, or the cap hit.</summary>
    private static bool DetectionHasSettled(int count, TimeSpan unchangedFor, TimeSpan elapsed)
    {
        if (count < 0 || elapsed >= DetectionMaxWait) return true;
        return unchangedFor >= (count > 0 ? DetectionSettleWindow : DetectionNoDevicesWindow);
    }

    // ── Fresh connection before a reply-bearing command ─────────────────────
    // Where the current connection lives, so a refresh reconnects to the same server.
    private string _rgbIp = "127.0.0.1";
    private int _rgbPort = 6742;

    /// <summary>UTC time the current connection last carried a write; MinValue if none yet.</summary>
    private DateTime _lastRgbWriteUtc = DateTime.MinValue;

    /// <summary>Writes further apart than this are separate user actions, not one burst.</summary>
    private static readonly TimeSpan RgbWriteBurstGap = TimeSpan.FromSeconds(1);

    /// <summary>Records that the current connection has just carried a lighting write.</summary>
    private void MarkRgbConnectionWritten() => _lastRgbWriteUtc = DateTime.UtcNow;

    /// <summary>
    /// Opens a fresh connection before a command that has to wait for a reply, when the current
    /// connection has carried writes and then gone quiet.
    ///
    /// Every round of the client's logs (29, 30, 31 and 32) shows the same invariant, on every
    /// device — ENE DRAM over SMBus in earlier rounds, the Razer keyboard over USB in round 32:
    ///   • a connection that has only done reads handles a whole apply-to-all perfectly, including
    ///     reply-bearing calls made milliseconds after that batch's own writes;
    ///   • the FIRST reply-bearing call of the NEXT apply-to-all, seconds later, never gets a reply;
    ///   • a new connection to the SAME server process answers immediately (round 32, 17:11:36).
    /// OpenRGB.NET matches replies to requests only by command type, and its read loop survives
    /// nothing but cancellation, so a connection in that state stays deaf until it is replaced.
    ///
    /// Rather than wait for the watchdog to detect it (a 5s pause the client reads as "nothing
    /// happened"), the connection is replaced up front, before it is relied on. Within one burst
    /// the writes are milliseconds apart, so this only happens once per user action. The device
    /// cache and remembered modes are kept: it is the same server process, so device indices and
    /// device state are unchanged — only the socket is new.
    /// </summary>
    private void RefreshRgbConnectionIfQuietAfterWrites()
    {
        if (_lastRgbWriteUtc == DateTime.MinValue) return;
        if (DateTime.UtcNow - _lastRgbWriteUtc < RgbWriteBurstGap) return;

        string oldReader = DescribeRgbReadLoop();
        bool ok = TryConnectOnce(_rgbIp, _rgbPort);
        _lastRgbWriteUtc = DateTime.MinValue;
        Log(ok
            ? $"[RGB→] Opened a fresh connection for this action (previous connection's reader: {oldReader})."
            : $"[RGB→] Could not open a fresh connection (previous reader: {oldReader}); the next write will retry.");
    }

    /// <summary>
    /// Reports the state of OpenRGB.NET's background reply reader for the current connection
    /// ("Running", or "Faulted: …" with the error that stopped it). Diagnostic only: it tells the
    /// next set of logs whether a deaf connection is caused on the client side (a dead reader)
    /// or on the server side (a live reader that is simply never sent a reply).
    /// </summary>
    private string DescribeRgbReadLoop()
    {
        try
        {
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var connection = typeof(OpenRGB.NET.OpenRgbClient).GetField("_connection", flags)?.GetValue(_rgbClient);
            var reader = connection?.GetType().GetField("_readLoopTask", flags)?.GetValue(connection) as Task;
            if (reader == null) return "unknown";
            string error = reader.Exception?.GetBaseException().Message ?? "";
            return error.Length == 0 ? reader.Status.ToString() : $"{reader.Status}: {error}";
        }
        catch
        {
            return "unknown";
        }
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

    /// <summary>Kernel-enforced cleanup: any OpenRGB we start dies when this process does,
    /// even on a crash or force-kill.</summary>
    private readonly ChildProcessJob _openRgbJob = new();

    /// <summary>
    /// Bus drivers that take exclusive low-level port access and therefore block the Super I/O
    /// read. Our bundled OpenRGB registers WinRing0 itself, so the app was installing the very
    /// driver that broke its own fan detection.
    /// </summary>
    private static readonly string[] ConflictingBusServices =
    {
        "WinRing0_1_2_0", "WinRing0x64", "WinRing0"
    };

    /// <summary>
    /// Stops and deregisters any resident WinRing0 service so the LPC/ISA bus is free for
    /// PawnIO. Called before LibreHardwareMonitor opens and again on shutdown, so the driver
    /// cannot linger and break the NEXT run either. Best-effort: failures are logged, never
    /// thrown — losing fan detection is bad, but crashing startup is worse.
    /// </summary>
    public void ReleaseConflictingBusDrivers(string reason)
    {
        foreach (var service in ConflictingBusServices)
        {
            if (!BusServiceExists(service)) continue;

            Log($"[Driver] '{service}' is registered and can block motherboard fan access — removing ({reason}).");
            RunServiceCommand($"stop {service}");
            RunServiceCommand($"delete {service}");
            Log($"[Driver] '{service}' {(BusServiceExists(service) ? "could NOT be removed" : "removed")}.");
        }
    }

    private static bool BusServiceExists(string serviceName)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine
                .OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
            return key != null;
        }
        catch { return false; }
    }

    private void RunServiceCommand(string arguments)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("sc.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            proc?.WaitForExit(4000);
        }
        catch (Exception ex)
        {
            Log($"[Driver] sc.exe {arguments} failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Whether memory/motherboard (SMBus) lighting is allowed. Set from the saved theme at
    /// startup. When false the WinRing0 driver OpenRGB needs for SMBus is kept out of reach, so
    /// no low-level bus driver is ever loaded (client round 24, item 1).
    /// </summary>
    public bool EnableSmbusLighting { get; set; }

    private const string WinRing0SysName = "WinRing0x64.sys";
    private const string WinRing0DisabledSuffix = ".disabled";

    /// <summary>
    /// Enables or disables OpenRGB's SMBus capability by making its WinRing0 driver file
    /// available or unavailable before the server starts. Renaming is used rather than deleting
    /// so the choice is reversible without a reinstall. USB lighting is unaffected.
    /// </summary>
    private void ApplySmbusLightingPreference(string openRgbDirectory)
    {
        try
        {
            string active = System.IO.Path.Combine(openRgbDirectory, WinRing0SysName);
            string parked = active + WinRing0DisabledSuffix;

            if (EnableSmbusLighting && System.IO.File.Exists(parked) && !System.IO.File.Exists(active))
            {
                System.IO.File.Move(parked, active);
                Log("[RGB] Memory/motherboard lighting ENABLED — WinRing0 restored for OpenRGB.");
            }
            else if (!EnableSmbusLighting && System.IO.File.Exists(active))
            {
                if (System.IO.File.Exists(parked)) System.IO.File.Delete(parked);
                System.IO.File.Move(active, parked);
                Log("[RGB] Memory/motherboard lighting OFF — WinRing0 parked so no low-level " +
                    "bus driver is loaded. USB lighting is unaffected.");
            }
        }
        catch (Exception ex)
        {
            // Never block RGB startup over this; worst case the previous state persists.
            Log($"[RGB] Could not apply the SMBus lighting preference: {ex.Message}");
        }
    }

    /// <summary>PID of the OpenRGB server THIS app started, or 0 when it has not started one.
    /// Used to tell our own live server apart from an orphan left by an earlier session.</summary>
    private int _ownOpenRgbPid;

    /// <summary>
    /// True when every running OpenRGB is the server this app started and it is still alive.
    /// Only then is a reconnect safe to make without replacing the process — killing our own
    /// server mid-session drops the device list and takes ~9s of SMBus scanning to come back.
    /// </summary>
    private bool OwnsEveryRunningOpenRgb(System.Diagnostics.Process[] running)
    {
        if (_ownOpenRgbPid == 0 || running.Length == 0) return false;
        return running.All(p => p.Id == _ownOpenRgbPid && !HasExited(p));
    }

    /// <summary>Whether the process has exited, treating an inaccessible process as gone.</summary>
    private static bool HasExited(System.Diagnostics.Process process)
    {
        try { return process.HasExited; }
        catch { return true; }
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
                // UseShellExecute=false (no "runas"): this app already runs elevated per its
                // manifest, so the child inherits that token — and a direct child handle is what
                // lets us put it in the kill-on-close job below. Started via the shell it was
                // detached, and a crash or force-kill left it running for the rest of the
                // machine's uptime, sweeping the SMBus (client round 20, item 8).
                string openRgbDir = System.IO.Path.GetDirectoryName(openRgbPath) ?? string.Empty;

                // Decide BEFORE launch whether OpenRGB can load its low-level bus driver.
                ApplySmbusLightingPreference(openRgbDir);

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = openRgbPath,
                    Arguments = "--server",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = openRgbDir
                };

                var child = System.Diagnostics.Process.Start(psi);
                if (child != null)
                {
                    bool tracked = _openRgbJob.Assign(child);
                    _ownOpenRgbPid = child.Id;
                    Log($"[RGB] Launched OpenRGB.exe from {openRgbPath} in server mode " +
                        $"(PID={child.Id}, auto-terminates with this app: {tracked}).");
                }
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

    private readonly object _rgbWorkerLock = new();

    /// <summary>
    /// Queues a lighting command and GUARANTEES a live consumer for it.
    ///
    /// This used to be "_rgbWorker ??= StartNew(...)", which starts the consumer exactly once
    /// for the life of the process. If that task ever ended — returning normally or faulting —
    /// the field stayed non-null, so no replacement was ever started and every later command was
    /// queued and silently never executed. That is exactly what the client saw: the first batch
    /// of colours applied perfectly, and every colour and effect afterwards did nothing, with no
    /// error anywhere because the code that logs is the code that never ran (client round 28).
    /// </summary>
    /// <summary>
    /// Ticks (UTC) when the command currently executing started, or 0 when the worker is idle.
    /// A long is used so the watchdog and the worker can read and update it atomically.
    /// </summary>
    private long _rgbOpInFlightSinceTicks;

    /// <summary>A single command still running after this long means the server has stopped
    /// answering: every OpenRGB call is a blocking request/response with no timeout of its own.</summary>
    private static readonly TimeSpan RgbStallTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Drives <see cref="CheckForStalledRgbCommand"/> independently of user activity.</summary>
    private System.Threading.Timer? _rgbStallWatchdog;

    private void EnqueueRgbOp(string description, Action op)
    {
        EnsureRgbWorkerAlive();

        if (!_rgbQueue.TryAdd(op))
            Log($"[RGB→] queue full — dropped: {description}");
        else
            Log($"[RGB→] queued: {description} (waiting: {_rgbQueue.Count})");
    }

    /// <summary>
    /// Frees a lighting command that is blocked waiting for a reply that is never coming.
    ///
    /// OpenRGB.NET sends a request and then blocks on the matching response with NO timeout
    /// (OpenRgbConnection.Receive waits on a BlockingCollection). When the server stops
    /// answering mid-burst, the worker parks there forever, so every later colour and effect
    /// queues behind it and nothing is logged — the code that logs is the code that never runs.
    ///
    /// The client's round-30 log shows precisely this: batch 1 executes and drains; batch 2
    /// takes ONE command (the depth stays at 1 across two enqueues) and stops; batches 2 and 3
    /// then pile up to a depth of 19 with no fault and no "worker had stopped" line.
    ///
    /// Two things had to change from the previous attempt at this:
    ///  • it was only invoked from EnqueueRgbOp, so recovery required the user to click AGAIN
    ///    after the timeout had already elapsed — in that session he clicked at +4s and +8s and
    ///    closed the app at +14s, so a 15s check on the enqueue path could never fire. It now
    ///    runs on a timer, and a single hung command recovers with nothing queued behind it.
    ///  • it started a replacement consumer unconditionally. The old one is BLOCKED, not dead:
    ///    once the connection is dropped its call fails out and it carries on with the queue, so
    ///    starting another left two consumers racing on one queue for the rest of the session.
    ///
    /// Disposing the client cancels the connection's token, which is what unblocks the waiting
    /// call; the next command then reconnects (EnsureRgbConnectedForWrite) and the backlog drains.
    /// </summary>
    private void CheckForStalledRgbCommand()
    {
        long startedTicks = System.Threading.Interlocked.Read(ref _rgbOpInFlightSinceTicks);
        if (startedTicks == 0) return;
        if (DateTime.UtcNow.Ticks - startedTicks < RgbStallTimeout.Ticks) return;

        // Re-arm rather than clear: if dropping the connection does not free it, the next tick
        // tries again instead of giving up on the command for the rest of the session. Only the
        // thread that wins this exchange performs the reset.
        if (System.Threading.Interlocked.CompareExchange(
                ref _rgbOpInFlightSinceTicks, DateTime.UtcNow.Ticks, startedTicks) != startedTicks)
            return;

        Log($"[RGB→] A lighting command has not completed in {RgbStallTimeout.TotalSeconds:F0}s " +
            $"({_rgbQueue.Count} more waiting; connection reader: {DescribeRgbReadLoop()}). " +
            "Dropping the stuck connection so it fails out; the worker reconnects and continues with the backlog.");

        ResetStalledRgbConnection();
    }

    /// <summary>Starts the queue consumer, or replaces one that has stopped.</summary>
    private void EnsureRgbWorkerAlive()
    {
        lock (_rgbWorkerLock)
        {
            StartRgbStallWatchdog();

            if (_rgbWorker is { IsCompleted: false }) return;

            if (_rgbWorker != null)
            {
                // It stopped. Say why, then replace it so lighting keeps working.
                string reason = _rgbWorker.Exception?.GetBaseException().Message ?? "ended without an error";
                Log($"[RGB→] The lighting worker had stopped ({reason}) — starting a new one so " +
                    "queued commands are processed.");
            }

            _rgbWorker = Task.Factory.StartNew(ProcessRgbQueue, TaskCreationOptions.LongRunning);
        }
    }

    private void ProcessRgbQueue()
    {
        // Anything escaping this loop kills the consumer, so record it rather than letting the
        // lighting go quiet without explanation. EnsureRgbWorkerAlive will start a replacement.
        try
        {
            foreach (var op in _rgbQueue.GetConsumingEnumerable())
                RunRgbOp(op);

            Log("[RGB→] Lighting worker exited: the queue was marked complete (app shutting down).");
        }
        catch (Exception ex)
        {
            Log($"[RGB→] Lighting worker STOPPED unexpectedly: {ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }

    /// <summary>Starts the stall watchdog once, on the first lighting command of the session.
    /// Caller holds <see cref="_rgbWorkerLock"/>.</summary>
    private void StartRgbStallWatchdog()
    {
        _rgbStallWatchdog ??= new System.Threading.Timer(
            _ => CheckForStalledRgbCommand(), null,
            dueTime: TimeSpan.FromSeconds(1), period: TimeSpan.FromSeconds(1));
    }

    private void RunRgbOp(Action op)
    {
        // Marks this command in flight so the watchdog can free it if the server stops replying.
        System.Threading.Interlocked.Exchange(ref _rgbOpInFlightSinceTicks, DateTime.UtcNow.Ticks);
        try
        {
            if (!TryRunRgbOp(op)) RetryRgbOpAfterRecovery(op);
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref _rgbOpInFlightSinceTicks, 0);
        }
    }

    /// <summary>Runs one lighting command. False when it failed and is worth one retry.</summary>
    private bool TryRunRgbOp(Action op)
    {
        try
        {
            op();
            return true;
        }
        catch (Exception ex)
        {
            Log($"[RGB→] Worker operation failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Sends a command once more after a failure. Freeing a stalled queue works by dropping the
    /// connection, which makes the command that was waiting on it throw — so without this, the
    /// very command the user was waiting on is the one command discarded by its own recovery.
    /// The retry reconnects first (every write path calls EnsureRgbConnectedForWrite), and is
    /// deliberately attempted only once so a command that always fails cannot loop.
    /// </summary>
    private void RetryRgbOpAfterRecovery(Action op)
    {
        // A fresh attempt gets its own stall budget rather than inheriting the failed one's.
        System.Threading.Interlocked.Exchange(ref _rgbOpInFlightSinceTicks, DateTime.UtcNow.Ticks);
        Log("[RGB→] Re-sending that command once, on a fresh connection.");
        TryRunRgbOp(op);
    }

    /// <summary>How long a lighting command waits for the client before declaring it stuck.</summary>
    private static readonly TimeSpan RgbLockTimeout = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Runs a lighting command against the OpenRGB client, but never waits on it forever.
    ///
    /// The OpenRGB writes are blocking socket calls made while holding <see cref="_rgbClientLock"/>.
    /// If the server stalls mid-write, that thread stays stuck HOLDING the lock, so every later
    /// command blocks behind it — silently, because the logging lives inside the command. That is
    /// why exactly one batch of colours applied per session and everything afterwards did nothing
    /// with no error: the picker still returned and the choice was still saved (which is why the
    /// right colour appeared after a restart), but the write never ran (client round 26).
    ///
    /// On timeout the connection is reset so the NEXT command reconnects and works.
    /// </summary>
    private bool TryWithRgbClient(string description, Action action)
    {
        if (!System.Threading.Monitor.TryEnter(_rgbClientLock, RgbLockTimeout))
        {
            Log($"[RGB→] '{description}' could not start — a previous lighting command is stuck. " +
                "Resetting the connection so this and later commands can run.");
            ResetStalledRgbConnection();
            return false;
        }

        // Time spent QUEUING for the client must not count against the stall budget: a device
        // enumeration (GetAllControllerData) legitimately holds this lock for several seconds on
        // an SMBus board, and without this the watchdog would drop a connection that is working
        // perfectly well. Waiting too long for the lock is already handled above, on its own
        // timeout. Only re-stamp for the worker, which is the thread that sets the marker.
        RestartStallBudget();

        try { action(); return true; }
        finally { System.Threading.Monitor.Exit(_rgbClientLock); }
    }

    /// <summary>Restarts the in-flight command's stall budget, if one is in flight.</summary>
    private void RestartStallBudget()
    {
        long inFlight = System.Threading.Interlocked.Read(ref _rgbOpInFlightSinceTicks);
        if (inFlight == 0) return;
        System.Threading.Interlocked.CompareExchange(
            ref _rgbOpInFlightSinceTicks, DateTime.UtcNow.Ticks, inFlight);
    }

    /// <summary>
    /// Drops the wedged client WITHOUT taking the lock — the stuck thread still holds it.
    /// Disposing the client makes that blocked socket call fail out and release the lock, and
    /// clearing the flag makes the next write re-establish a fresh connection.
    /// </summary>
    private void ResetStalledRgbConnection()
    {
        _isConnectedToRgb = false;
        ForgetAppliedModes();        // a reconnected server may not hold the same device state
        try { _rgbClient?.Dispose(); }
        catch { /* already dead — that is the point */ }
        _rgbClient = null;
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

    // ── Redundant mode-switch suppression ───────────────────────────────────
    // The mode WE last put each device into, on the CURRENT connection. Authoritative because
    // we set it ourselves and the read-back confirmed it, unlike the cached device snapshot.
    private readonly Dictionary<int, string> _lastAppliedMode = new();

    /// <summary>
    /// True when sending this mode would change nothing on the device and can be skipped.
    ///
    /// Every apply-to-all re-sends "Static" to all four devices, even when the previous
    /// apply-to-all already put them there. On the DRAM and motherboard controllers that mode
    /// switch carries no colour at all (modeColors=unchanged — the colour is delivered by the
    /// zone write that follows), so it is a pure no-op that still costs a blocking SMBus round
    /// trip. In every round of the client's logs it is that redundant switch, on the SECOND
    /// apply-to-all, that the server stops answering. Skipping it removes the call that hangs
    /// and four SMBus round trips per apply.
    ///
    /// Only skipped when the mode carries no colours; a mode that DOES carry colours is always
    /// sent, so nothing that could change the lighting is ever suppressed.
    /// </summary>
    private bool IsRedundantModeSwitch(int deviceId, string modeName, OpenRGB.NET.Color[]? modeColors)
    {
        if (modeColors != null) return false;
        lock (_lastAppliedMode)
        {
            return _lastAppliedMode.TryGetValue(deviceId, out var applied) &&
                   applied.Equals(modeName, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Records the mode a device is now in, after the switch succeeded.</summary>
    private void RememberAppliedMode(int deviceId, string modeName)
    {
        lock (_lastAppliedMode) _lastAppliedMode[deviceId] = modeName;
    }

    /// <summary>Forgets what we believe each device's mode is. Called whenever the connection is
    /// dropped or replaced: a reconnected (or restarted) server may not hold the same state.</summary>
    private void ForgetAppliedModes()
    {
        lock (_lastAppliedMode) _lastAppliedMode.Clear();
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

    /// <summary>
    /// Called on the RGB worker before every write. When the connection has gone (which used to
    /// make every later command return silently, so the lighting "only worked the first time"
    /// and nothing at all appeared in the log), this logs it and re-establishes the connection
    /// so the command still lands (client round 20, items 6 and 7).
    /// </summary>
    private bool EnsureRgbConnectedForWrite()
    {
        if (_isConnectedToRgb && _rgbClient != null) return true;

        Log("[RGB→] Connection was down at write time — reconnecting before sending.");
        bool ok = ConnectRgbServer();
        Log(ok ? "[RGB→] Reconnected; continuing with the write."
               : "[RGB→] Reconnect FAILED — command dropped.");
        return ok;
    }

    public void UpdateRgbZoneColor(int deviceId, int zoneId, OpenRGB.NET.Color color, bool force = false)
    {
        if (IsDuplicateRgbWrite($"zone:{deviceId}:{zoneId}", Hex(color), force))
        {
            Log($"[RGB→] deduped identical zone write dev={deviceId} zone={zoneId} {Hex(color)}");
            return;
        }

        EnqueueRgbOp($"zone write dev={deviceId} zone={zoneId}", () => WriteZoneColor(deviceId, zoneId, color));
    }

    private void WriteZoneColor(int deviceId, int zoneId, OpenRGB.NET.Color color)
    {
        if (!EnsureRgbConnectedForWrite()) return;
        try
        {
            var device = GetCachedDevice(deviceId);
            uint ledCount = 0;
            string deviceName = "?";
            string zoneName = "?";
            if (!TryWithRgbClient($"zone write dev={deviceId} zone={zoneId}", () =>
            {
                device ??= _rgbClient!.GetControllerData(deviceId);
                var zone = device.Zones[zoneId];
                ledCount = zone.LedCount;
                deviceName = device.Name;
                zoneName = zone.Name;
                var leds = Enumerable.Repeat(color, (int)ledCount).ToArray();
                WriteZoneLedsReliably(deviceId, zoneId, leds);
            })) return;
            MarkRgbConnectionWritten();
            // NB: the mode here comes from the CACHED device snapshot taken at the last device
            // scan, not a live read — labelled accordingly so it is not misread as the device's
            // current mode (that misreading caused a wrong DRAM diagnosis in round 18).
            Log($"[RGB→] '{deviceName}' zone '{zoneName}': wrote {ledCount} LEDs = {Hex(color)} " +
                $"(mode at last scan: {(device == null ? "?" : ActiveModeName(device))})");
        }
        catch (Exception ex)
        {
            Log($"[RGB→] Zone write FAILED dev={deviceId} zone={zoneId} color={Hex(color)}: {ex.Message}");
            MarkRgbDeadIfTransportError(ex);
            throw;   // let RunRgbOp retry it once on a fresh connection
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
        if (IsDuplicateRgbWrite($"zone:{deviceId}:{zoneId}", $"{Hex(colors[0])}→{Hex(colors[^1])}×{colors.Length}", force))
        {
            Log($"[RGB→] deduped identical gradient write dev={deviceId} zone={zoneId}");
            return;
        }

        EnqueueRgbOp($"gradient write dev={deviceId} zone={zoneId}", () => WriteZoneGradient(deviceId, zoneId, colors));
    }

    private void WriteZoneGradient(int deviceId, int zoneId, OpenRGB.NET.Color[] colors)
    {
        if (!EnsureRgbConnectedForWrite()) return;
        try
        {
            if (!TryWithRgbClient($"gradient write dev={deviceId} zone={zoneId}",
                    () => WriteZoneLedsReliably(deviceId, zoneId, colors))) return;
            MarkRgbConnectionWritten();
            Log($"[RGB→] dev={deviceId} zone={zoneId}: wrote {colors.Length} LEDs gradient {Hex(colors[0])} → {Hex(colors[^1])}");
        }
        catch (Exception ex)
        {
            Log($"[RGB→] Gradient zone write FAILED dev={deviceId} zone={zoneId}: {ex.Message}");
            MarkRgbDeadIfTransportError(ex);
            throw;   // let RunRgbOp retry it once on a fresh connection
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
        if (IsDuplicateRgbWrite($"mode:{deviceId}", $"{effectName}|{(color == null ? "" : Hex(color.Value))}", force))
        {
            Log($"[RGB→] deduped identical mode switch dev={deviceId} '{effectName}'");
            return;
        }

        EnqueueRgbOp($"mode switch dev={deviceId} '{effectName}'", () => RunRgbEffectSwitch(deviceId, effectName, color));
    }

    private void RunRgbEffectSwitch(int deviceId, string effectName, OpenRGB.NET.Color? color)
    {
        if (!EnsureRgbConnectedForWrite()) return;
        try
        {
            ApplyRgbEffect(deviceId, effectName, color);
        }
        catch (Exception ex)
        {
            Log($"[RGB→] Mode switch FAILED dev={deviceId} mode='{effectName}': {ex.Message}");
            MarkRgbDeadIfTransportError(ex);
            // Rethrown so RunRgbOp's single retry re-sends it. Swallowing it here meant the retry
            // never fired: in the client's round-32 log the cancelled Razer "Static" switch —
            // the command that carries the keyboard's colour — was simply dropped.
            throw;
        }
    }

    private void ApplyRgbEffect(int deviceId, string effectName, OpenRGB.NET.Color? color)
    {
        var device = GetCachedDevice(deviceId);
        if (device == null &&
            !TryWithRgbClient($"read device {deviceId}", () => device = _rgbClient!.GetControllerData(deviceId)))
            return;
        if (device == null) return;

        var modeIndex = Array.FindIndex(device.Modes, m => m.Name.Equals(effectName, StringComparison.OrdinalIgnoreCase));
        if (modeIndex < 0)
        {
            Log($"[RGB→] '{device.Name}': mode '{effectName}' NOT FOUND — device offers: " +
                string.Join(", ", device.Modes.Select(m => m.Name)));
            return;
        }

        var mode = device.Modes[modeIndex];
        var modeColors = BuildModeColors(mode, color);
        if (IsRedundantModeSwitch(deviceId, mode.Name, modeColors))
        {
            Log($"[RGB→] '{device.Name}': already in '{mode.Name}' and this mode carries no " +
                "colour — skipping the redundant switch; the zone write carries the colour.");
            return;
        }

        // UpdateMode is NOT fire-and-forget: OpenRGB.NET calls GetControllerData inside it and
        // waits for the reply before sending the mode. It is the only reply-bearing call in an
        // apply-to-all, and it is the call that hangs — see RefreshRgbConnectionIfQuietAfterWrites.
        RefreshRgbConnectionIfQuietAfterWrites();

        if (!TryWithRgbClient($"mode switch dev={deviceId} '{mode.Name}'", () =>
        {
            // Only force Custom Mode if we are switching to direct LED control
            if (mode.Name.Contains("Direct", StringComparison.OrdinalIgnoreCase) ||
                mode.Name.Contains("Custom", StringComparison.OrdinalIgnoreCase))
            {
                _rgbClient!.SetCustomMode(deviceId);
            }

            _rgbClient!.UpdateMode(deviceId, modeIndex, colors: modeColors);
        })) return;

        RememberAppliedMode(deviceId, mode.Name);
        MarkRgbConnectionWritten();
        Log($"[RGB→] '{device.Name}': mode → '{mode.Name}' " +
            $"(modeColors={(modeColors == null ? "unchanged" : $"{modeColors.Length}×{Hex(modeColors[0])}")})");

        // The read-back is a diagnostic, and it costs a full GetControllerData round trip per
        // device. During an apply-to-all that is four extra blocking reads over the SMBus in one
        // burst — on this board the server stops answering partway through exactly such a burst.
        // Skip it while commands are still waiting and take it when the queue is idle, so single
        // changes still record whether the controller accepted the mode.
        if (_rgbQueue.Count == 0)
            LogModeReadback(deviceId, device.Name, mode.Name);
    }

    /// <summary>
    /// Reads the device back LIVE after a mode switch and logs what it actually reports. Earlier
    /// rounds guessed at DRAM behaviour from a cached snapshot and got it wrong; this records
    /// whether the controller genuinely accepted the mode, so the next set of client logs is
    /// evidence rather than inference.
    /// </summary>
    private void LogModeReadback(int deviceId, string deviceName, string requested)
    {
        try
        {
            OpenRGB.NET.Device? fresh = null;
            if (!TryWithRgbClient($"mode read-back dev={deviceId}",
                    () => fresh = _rgbClient!.GetControllerData(deviceId)) || fresh == null)
                return;

            // Refresh the cache too, so later log lines stop reporting a stale mode.
            var cache = _rgbDeviceCache;
            if (deviceId >= 0 && deviceId < cache.Length) cache[deviceId] = fresh;

            string actual = ActiveModeName(fresh);
            string verdict = actual.Equals(requested, StringComparison.OrdinalIgnoreCase) ? "ACCEPTED" : "REJECTED";
            Log($"[RGB✓] '{deviceName}': requested '{requested}', device now reports '{actual}' — {verdict}");
        }
        catch (Exception ex)
        {
            Log($"[RGB✓] '{deviceName}': mode read-back failed: {ex.Message}");
        }
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
        _rgbStallWatchdog?.Dispose();
        _rgbStallWatchdog = null;
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

    /// <summary>
    /// Terminates the bundled OpenRGB server. Public and safe to call at any time, including
    /// from a crash handler: the server must NEVER outlive the app. A leaked OpenRGB keeps
    /// sweeping the SMBus for as long as the machine is on, which the client experienced as
    /// heavy system-wide load "even when the app is not running" (client round 20, item 8).
    /// </summary>
    public void ShutdownOpenRgbServer()
    {
        StopOpenRgbServer();
        // OpenRGB leaves WinRing0 registered behind it. Clear it here as well as at startup, so
        // a machine that never reopens this app is not left with the bus held (round 23).
        ReleaseConflictingBusDrivers("shutdown");
    }

    private void StopOpenRgbServer()
    {
        try
        {
            _ownOpenRgbPid = 0;      // whatever we started is going away with the rest
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
