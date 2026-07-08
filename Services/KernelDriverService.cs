using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace PcStatsMonitor.Services;

/// <summary>
/// Ensures WinRing0x64.sys (shipped alongside the exe) is registered and
/// started as a kernel service before LibreHardwareMonitor opens the computer.
///
/// Without this driver, MSR-based sensors (CPU temperature, CPU clock speed)
/// and motherboard Super I/O fan sensors return null/0 even when the app runs
/// as Administrator.
///
/// This service:
///   1. Short-circuits if a working WinRing0 device is already available
///   2. Adds a Windows Defender exclusion for the application folder
///   3. Registers WinRing0x64.sys with the Service Control Manager and starts it
///   4. Recovers from the common conflict where another program (typically a
///      leftover OpenRGB instance launched on a previous run) has already
///      loaded its own copy of WinRing0, which makes StartService fail with
///      ERROR_ALREADY_EXISTS (183) and leaves LibreHardwareMonitor without
///      Ring0 access (CPU temp reads 0, no case fans)
///   5. Verifies the device is actually openable and reports the true cause
///      to the user when it is not
/// </summary>
public static class KernelDriverService
{
    private const string DriverName    = "WinRing0_1_2_0";
    private const string DriverSysFile = "WinRing0x64.sys";
    private const string DevicePath    = @"\\.\WinRing0_1_2_0";
    private const string ServiceRegKey = @"SYSTEM\CurrentControlSet\Services\WinRing0_1_2_0";

    /// <summary>True when the driver could not be started or its device could not be opened.</summary>
    public static bool DriverLoadFailed { get; private set; }

    // ── Win32 SCM P/Invokes ─────────────────────────────────────────────────
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint OpenSCManager(string? machine, string? db, uint access);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint CreateService(nint hScm, string name, string display,
        uint access, uint type, uint start, uint error, string binary,
        string? group, nint tag, string? deps, string? user, string? pwd);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint OpenService(nint hScm, string name, uint access);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool StartService(nint hSvc, uint argc, string[]? argv);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool ControlService(nint hSvc, uint control, ref SERVICE_STATUS status);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DeleteService(nint hSvc);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(nint h);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share,
        nint security, uint disposition, uint flags, nint template);

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_STATUS
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
    }

    private const uint ALL_ACCESS    = 0xF003F;
    private const uint SVC_ALL       = 0xF01FF;
    private const uint KERNEL_DRV    = 0x1;
    private const uint DEMAND_START  = 0x3;
    private const uint ERR_IGNORE    = 0x0;
    private const uint SVC_STOP      = 0x1;        // SERVICE_CONTROL_STOP
    private const uint GENERIC_RW    = 0xC0000000; // GENERIC_READ | GENERIC_WRITE
    private const uint OPEN_EXISTING = 0x3;

    private const int E_EXISTS         = 1073; // ERROR_SERVICE_EXISTS
    private const int E_RUNNING        = 1056; // ERROR_SERVICE_ALREADY_RUNNING
    private const int E_ALREADY_EXISTS = 183;  // ERROR_ALREADY_EXISTS — another WinRing0 copy is loaded
    private const int E_MARKED_DELETE  = 1072; // ERROR_SERVICE_MARKED_FOR_DELETE
    private const int E_BLOCKED        = 1275; // ERROR_DRIVER_BLOCKED

    /// <summary>
    /// Call once at startup before <c>Computer.Open()</c>.
    /// Safe to call on every launch — exits immediately if already running.
    /// </summary>
    public static void EnsureInstalled(ILogger logger)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        try
        {
            EnsureInstalledCore(logger);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Driver] Unexpected error. CPU temp/clock may be unavailable.");
        }
    }

    private static void EnsureInstalledCore(ILogger logger)
    {
        var driverPath = Path.Combine(AppContext.BaseDirectory, DriverSysFile);

        if (!File.Exists(driverPath))
        {
            DriverLoadFailed = true;
            logger.LogWarning("[Driver] {f} not found next to exe at {p}. " +
                "CPU temp/clock sensors will not be available.", DriverSysFile, driverPath);
            return;
        }

        logger.LogDebug("[Driver] Driver found at {p}", driverPath);

        // If a WinRing0 device is already present (loaded by us on a prior run,
        // or by another tool under a different service name such as WinRing0x64),
        // accept it. Whether it actually works is proven functionally after the
        // hardware layer opens — HardwareControlService reclaims only if the CPU
        // temperature reads zero. Fighting a working foreign driver here caused a
        // false "driver unavailable" alarm on machines with a foreign WinRing0.
        if (TryOpenDevice())
        {
            logger.LogInformation("[Driver] WinRing0 device already available — " +
                "sensor reads will be verified after hardware init.");
            return;
        }

        // No device yet: install and start our own driver.
        // Defender exclusion must be applied BEFORE the SCM install,
        // or Defender may quarantine the driver as HackTool:Win32/Winring0.
        AddDefenderExclusion(AppContext.BaseDirectory, logger);
        LogRegisteredImagePath(logger);

        int err = InstallAndStartDriver(driverPath, logger);
        if (err == E_ALREADY_EXISTS || err == E_MARKED_DELETE)
            err = RecoverFromConflict(driverPath, logger);

        ReportOutcome(err, logger);
    }

    /// <summary>
    /// Forcefully unloads whatever WinRing0 instance is present (killing the
    /// owning OpenRGB process if needed) and installs our driver. Called as a
    /// runtime safety net when sensors read zero despite an openable device.
    /// OpenRGB is relaunched later by the RGB connection flow.
    /// </summary>
    /// <returns>True when our driver was started and its device is openable.</returns>
    public static bool ForceReclaim(ILogger logger)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return false;

        try
        {
            var driverPath = Path.Combine(AppContext.BaseDirectory, DriverSysFile);
            if (!File.Exists(driverPath)) return false;

            int err = RecoverFromConflict(driverPath, logger);
            return err == 0 && TryOpenDevice();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Driver] ForceReclaim failed.");
            return false;
        }
    }

    /// <summary>
    /// Functional Ring0 verification using a throwaway LibreHardwareMonitor
    /// Computer: reads a CPU temperature and, when it is zero (a foreign or
    /// broken WinRing0 instance serves the device), reclaims the driver and
    /// probes once more. MUST run before any long-lived Computer instance is
    /// created — LHM's Ring0 state is process-global, and closing/reopening
    /// around live instances corrupts them (endless NREs in cpu.Update()).
    /// </summary>
    public static void VerifyRing0WithProbe(ILogger logger)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        try
        {
            RunRing0Probe(logger);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Driver] Ring0 probe failed unexpectedly.");
        }
    }

    private static string ReclaimMarkerPath =>
        Path.Combine(AppContext.BaseDirectory, "settings", "ring0_reclaim_attempted.marker");

    private static void RunRing0Probe(ILogger logger)
    {
        if (ProbeCpuTemperature())
        {
            logger.LogInformation("[Driver] Ring0 probe OK — CPU temperature readable ✓");
            TryDeleteReclaimMarker();
            return;
        }

        // Reclaiming stops kernel services and (previously) killed OpenRGB. When it
        // cannot actually fix the reads — e.g. this Windows build blocks WinRing0's
        // sensor access altogether — repeating it on every launch is pure risk:
        // killing OpenRGB mid-SMBus-write can hard-freeze the whole system.
        if (File.Exists(ReclaimMarkerPath))
        {
            logger.LogWarning("[Driver] Ring0 probe: CPU temperature reads zero. A reclaim was already " +
                "attempted on this install without success — skipping. CPU temp and case fans will show 0.");
            return;
        }

        if (Process.GetProcessesByName("OpenRGB").Length > 0)
        {
            logger.LogWarning("[Driver] Ring0 probe: CPU temperature reads zero, but OpenRGB is running — " +
                "skipping reclaim (stopping OpenRGB mid-SMBus-write can hang the system). " +
                "Will retry on a launch where OpenRGB is not running.");
            return;
        }

        logger.LogWarning("[Driver] Ring0 probe: CPU temperature reads zero — reclaiming driver (one attempt per install)…");
        ForceReclaim(logger);
        ReportProbeOutcome(logger);
    }

    private static void ReportProbeOutcome(ILogger logger)
    {
        if (ProbeCpuTemperature())
        {
            logger.LogInformation("[Driver] Ring0 probe OK after reclaim ✓");
            return;
        }

        WriteReclaimMarker(logger);
        logger.LogWarning("[Driver] Ring0 probe still failing after reclaim — marked as attempted; " +
            "no further automatic reclaims on this install. CPU temp and case fans will show 0.");
    }

    private static void WriteReclaimMarker(ILogger logger)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ReclaimMarkerPath)!);
            File.WriteAllText(ReclaimMarkerPath,
                $"Reclaim attempted {DateTime.Now:yyyy-MM-dd HH:mm:ss} — probe still failing. " +
                "Delete this file to allow another automatic reclaim attempt.");
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[Driver] Could not write reclaim marker.");
        }
    }

    private static void TryDeleteReclaimMarker()
    {
        try
        {
            if (File.Exists(ReclaimMarkerPath)) File.Delete(ReclaimMarkerPath);
        }
        catch
        {
            // Non-fatal: marker cleanup only affects future reclaim attempts.
        }
    }

    private static bool ProbeCpuTemperature()
    {
        var computer = new LibreHardwareMonitor.Hardware.Computer { IsCpuEnabled = true };
        try
        {
            computer.Open();
            var cpu = computer.Hardware.FirstOrDefault(
                h => h.HardwareType == LibreHardwareMonitor.Hardware.HardwareType.Cpu);
            cpu?.Update();
            return cpu?.Sensors.Any(
                s => s.SensorType == LibreHardwareMonitor.Hardware.SensorType.Temperature && s.Value > 0) == true;
        }
        finally
        {
            computer.Close();
        }
    }

    // ── Device probe ────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to open \\.\WinRing0_1_2_0 — the same handle LibreHardwareMonitor
    /// needs. Proves a WinRing0 driver is loaded and the device is reachable.
    /// </summary>
    private static bool TryOpenDevice()
    {
        using var handle = CreateFile(DevicePath, GENERIC_RW, 0, 0, OPEN_EXISTING, 0, 0);
        return !handle.IsInvalid;
    }

    // ── Diagnostics ─────────────────────────────────────────────────────────

    private static void LogRegisteredImagePath(ILogger logger)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(ServiceRegKey);
            var imagePath = key?.GetValue("ImagePath")?.ToString() ?? "(no service entry)";
            logger.LogInformation("[Driver] Existing service ImagePath: {p}", imagePath);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[Driver] Could not read service ImagePath from registry.");
        }
    }

    private static bool IsHvciEnabled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity");
            return key?.GetValue("Enabled") is int enabled && enabled == 1;
        }
        catch
        {
            return false;
        }
    }

    // ── Step 1: Defender exclusion ──────────────────────────────────────────

    private static void AddDefenderExclusion(string folder, ILogger logger)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName  = "powershell.exe",
                Arguments = $"-NonInteractive -NoProfile -WindowStyle Hidden " +
                            $"-Command \"Add-MpPreference -ExclusionPath '{folder}' " +
                            $"-ExclusionProcess 'PcStatsMonitor.exe' -ErrorAction SilentlyContinue\"",
                UseShellExecute       = false,
                CreateNoWindow        = true,
                RedirectStandardError = true
            };

            using var ps = Process.Start(psi);
            ps?.WaitForExit(5000);
            logger.LogInformation("[Driver] Windows Defender exclusion applied for {d}", folder);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Driver] Could not add Defender exclusion (non-fatal).");
        }
    }

    // ── Steps 2 + 3: SCM register and start ────────────────────────────────

    /// <summary>Registers and starts the driver. Returns 0 on success or a Win32 error code.</summary>
    private static int InstallAndStartDriver(string driverPath, ILogger logger)
    {
        var hScm = OpenSCManager(null, null, ALL_ACCESS);
        if (hScm == 0)
        {
            int err = Marshal.GetLastWin32Error();
            logger.LogWarning("[Driver] Cannot open SCM (err={e}). App must run as Administrator.", err);
            return err;
        }

        try
        {
            return CreateAndStart(hScm, driverPath, logger);
        }
        finally
        {
            CloseServiceHandle(hScm);
        }
    }

    private static int CreateAndStart(nint hScm, string driverPath, ILogger logger)
    {
        var hSvc = CreateService(hScm, DriverName, DriverName,
            SVC_ALL, KERNEL_DRV, DEMAND_START, ERR_IGNORE, driverPath,
            null, 0, null, null, null);

        if (hSvc == 0)
        {
            int createErr = Marshal.GetLastWin32Error();
            if (createErr != E_EXISTS && createErr != E_ALREADY_EXISTS)
                return LogCreateFailure(createErr, logger);

            logger.LogDebug("[Driver] Service already registered (err={e}), opening existing entry…", createErr);
            hSvc = OpenService(hScm, DriverName, SVC_ALL);
        }

        if (hSvc == 0)
            return Marshal.GetLastWin32Error();

        try
        {
            return StartDriver(hSvc, logger);
        }
        finally
        {
            CloseServiceHandle(hSvc);
        }
    }

    private static int LogCreateFailure(int err, ILogger logger)
    {
        logger.LogWarning("[Driver] CreateService failed (err={e}). CPU temp/clock may be unavailable.", err);
        return err;
    }

    private static int StartDriver(nint hSvc, ILogger logger)
    {
        if (StartService(hSvc, 0, null))
        {
            logger.LogInformation("[Driver] WinRing0 kernel driver started ✓");
            return 0;
        }

        int err = Marshal.GetLastWin32Error();
        if (err == E_RUNNING)
        {
            logger.LogDebug("[Driver] WinRing0 service already running ✓");
            return 0;
        }

        logger.LogWarning("[Driver] StartService failed (err={e}).", err);
        return err;
    }

    // ── Step 4: conflict recovery (err 183) ─────────────────────────────────

    /// <summary>
    /// Handles the case where another WinRing0 instance already owns the device:
    /// a leftover OpenRGB process we launched previously (same service name), or
    /// a foreign auto-start service under a different name (e.g. WinRing0x64 left
    /// behind by another hardware tool). Kills OpenRGB, stops every foreign
    /// WinRing0 service, drops our stale entry, then reinstalls and starts ours.
    /// OpenRGB is relaunched later by HardwareControlService, so RGB is unaffected.
    /// </summary>
    private static int RecoverFromConflict(string driverPath, ILogger logger)
    {
        logger.LogWarning("[Driver] Conflict detected — another WinRing0 instance owns the device. " +
            "Attempting automatic recovery…");

        KillLeftoverOpenRgb(logger);
        StopForeignWinRingServices(logger);
        RemoveServiceEntry(logger);
        System.Threading.Thread.Sleep(500);

        return InstallAndStartDriver(driverPath, logger);
    }

    /// <summary>
    /// Stops every WinRing0 kernel service that isn't ours (matched by driver
    /// file name in the registry) and demotes any auto-start ones to demand-start
    /// so they stop winning the boot-time race for the device on the next launch.
    /// </summary>
    private static void StopForeignWinRingServices(ILogger logger)
    {
        foreach (var name in FindWinRingServiceNames())
        {
            if (string.Equals(name, DriverName, StringComparison.OrdinalIgnoreCase)) continue;
            StopAndDemoteService(name, logger);
        }
    }

    private static List<string> FindWinRingServiceNames()
    {
        var names = new List<string>();
        using var services = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
            @"SYSTEM\CurrentControlSet\Services");
        if (services == null) return names;

        foreach (var name in services.GetSubKeyNames())
            AddIfWinRingService(services, name, names);
        return names;
    }

    private static void AddIfWinRingService(Microsoft.Win32.RegistryKey services, string name, List<string> names)
    {
        using var key = services.OpenSubKey(name);
        var imagePath = key?.GetValue("ImagePath")?.ToString();
        if (imagePath != null && imagePath.EndsWith(DriverSysFile, StringComparison.OrdinalIgnoreCase))
            names.Add(name);
    }

    private static void StopAndDemoteService(string name, ILogger logger)
    {
        StopServiceByName(name, logger);
        DemoteAutoStart(name, logger);
    }

    private static void StopServiceByName(string name, ILogger logger)
    {
        var hScm = OpenSCManager(null, null, ALL_ACCESS);
        if (hScm == 0) return;

        try
        {
            SendStop(hScm, name, logger);
        }
        finally
        {
            CloseServiceHandle(hScm);
        }
    }

    private static void SendStop(nint hScm, string name, ILogger logger)
    {
        var hSvc = OpenService(hScm, name, SVC_ALL);
        if (hSvc == 0) return;

        var status = new SERVICE_STATUS();
        bool stopped = ControlService(hSvc, SVC_STOP, ref status);
        CloseServiceHandle(hSvc);
        logger.LogInformation("[Driver] Stopped foreign WinRing0 service '{n}' (ok={s}).", name, stopped);
    }

    private static void DemoteAutoStart(string name, ILogger logger)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Services\{name}", writable: true);
            if (key?.GetValue("Start") is int start && start == 2)
                DemoteKey(key, name, logger);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[Driver] Could not demote '{n}' to demand-start.", name);
        }
    }

    private static void DemoteKey(Microsoft.Win32.RegistryKey key, string name, ILogger logger)
    {
        key.SetValue("Start", 3); // AUTO_START (2) → DEMAND_START (3)
        logger.LogInformation("[Driver] Demoted '{n}' to demand-start to stop boot-time WinRing0 races.", name);
    }

    private static void KillLeftoverOpenRgb(ILogger logger)
    {
        var processes = Process.GetProcessesByName("OpenRGB");
        foreach (var process in processes)
            TryKillProcess(process, logger);

        // Give the kernel a moment to unload OpenRGB's WinRing0 instance.
        if (processes.Length > 0)
            System.Threading.Thread.Sleep(1500);
    }

    private static void TryKillProcess(Process process, ILogger logger)
    {
        try
        {
            process.Kill();
            process.WaitForExit(3000);
            logger.LogInformation("[Driver] Stopped conflicting OpenRGB process (PID={pid}). " +
                "It will be relaunched after driver init.", process.Id);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Driver] Could not stop OpenRGB (PID={pid}).", process.Id);
        }
    }

    private static void RemoveServiceEntry(ILogger logger)
    {
        var hScm = OpenSCManager(null, null, ALL_ACCESS);
        if (hScm == 0) return;

        try
        {
            StopAndDeleteService(hScm, logger);
        }
        finally
        {
            CloseServiceHandle(hScm);
        }
    }

    private static void StopAndDeleteService(nint hScm, ILogger logger)
    {
        var hSvc = OpenService(hScm, DriverName, SVC_ALL);
        if (hSvc == 0) return;

        var status = new SERVICE_STATUS();
        ControlService(hSvc, SVC_STOP, ref status);
        bool deleted = DeleteService(hSvc);
        CloseServiceHandle(hSvc);
        logger.LogInformation("[Driver] Stale WinRing0 service entry stopped, deleted={d}.", deleted);
    }

    // ── Step 5: verification and user-facing reporting ──────────────────────

    private static void ReportOutcome(int err, ILogger logger)
    {
        if (err == 0 && TryOpenDevice())
        {
            logger.LogInformation("[Driver] WinRing0 verified ✓ — CPU temperature, clocks and " +
                "motherboard fan sensors are available.");
            return;
        }

        DriverLoadFailed = true;
        logger.LogWarning("[Driver] Driver unavailable (last err={e}). " +
            "CPU temp/clock and motherboard case fans will NOT work this session.", err);
        ShowDriverWarning(BuildFailureMessage(err));
    }

    private static string BuildFailureMessage(int err)
    {
        if (IsHvciEnabled())
        {
            return "Windows Memory Integrity (Core Isolation) is blocking the hardware sensor driver (WinRing0x64.sys).\n\n" +
                   "CPU Temperature, CPU Clock and case fan sensors cannot be read and will show as 0.\n\n" +
                   "To fix this: open Windows Security → Device Security → Core Isolation, turn OFF 'Memory Integrity', then restart your PC.";
        }

        if (err == E_ALREADY_EXISTS || err == E_MARKED_DELETE)
        {
            return "Another program has loaded a conflicting hardware driver (WinRing0), and BYLD Core could not take it over automatically.\n\n" +
                   "CPU Temperature and case fan sensors will show as 0 this session.\n\n" +
                   "To fix this: close other hardware tools (OpenRGB, monitoring/RGB utilities), then restart BYLD Core. If it persists, restart your PC and launch BYLD Core first.";
        }

        if (err == E_BLOCKED)
        {
            return "Windows Security is blocking the hardware sensor driver (WinRing0x64.sys).\n\n" +
                   "CPU Temperature and case fan sensors will show as 0.\n\n" +
                   "To fix this: open Windows Security → Protection History, find the blocked driver entry, choose 'Allow on device', then restart BYLD Core.";
        }

        return $"The hardware sensor driver (WinRing0x64.sys) could not be started (Windows error {err}).\n\n" +
               "CPU Temperature and case fan sensors will show as 0 this session.\n\n" +
               "Try restarting your PC and launching BYLD Core first, before any other hardware tools.";
    }

    private static void ShowDriverWarning(string message)
    {
        var app = System.Windows.Application.Current;
        if (app == null) return;

        app.Dispatcher.Invoke(() =>
        {
            var splash = app.Windows.OfType<System.Windows.Window>()
                .FirstOrDefault(w => w.GetType().Name == "SplashWindow");
            if (splash != null) splash.Topmost = false;

            if (splash != null)
                System.Windows.MessageBox.Show(splash, message, "Hardware Sensor Driver Unavailable",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            else
                System.Windows.MessageBox.Show(message, "Hardware Sensor Driver Unavailable",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        });
    }
}
