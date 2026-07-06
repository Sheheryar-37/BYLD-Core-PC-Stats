using System;
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

        if (TryOpenDevice())
        {
            logger.LogInformation("[Driver] WinRing0 device already available ✓");
            return;
        }

        // Defender exclusion must be applied BEFORE the SCM install,
        // or Defender may quarantine the driver as HackTool:Win32/Winring0.
        AddDefenderExclusion(AppContext.BaseDirectory, logger);
        LogRegisteredImagePath(logger);

        int err = InstallAndStartDriver(driverPath, logger);
        if (err == E_ALREADY_EXISTS || err == E_MARKED_DELETE)
            err = RecoverFromConflict(driverPath, logger);

        ReportOutcome(err, logger);
    }

    // ── Device probe ────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to open \\.\WinRing0_1_2_0 — the same handle LibreHardwareMonitor
    /// needs. This is the only reliable proof the driver is loaded and usable.
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
    /// Handles ERROR_ALREADY_EXISTS: another service has already loaded a copy
    /// of WinRing0 — typically a leftover OpenRGB instance we launched on a
    /// previous run (it keeps running after BYLD Core exits). Stops OpenRGB,
    /// removes the stale service entry, and retries the install once.
    /// OpenRGB is relaunched later by HardwareControlService when the RGB
    /// connection is established, so RGB control is unaffected.
    /// </summary>
    private static int RecoverFromConflict(string driverPath, ILogger logger)
    {
        logger.LogWarning("[Driver] Conflict detected — another WinRing0 instance is loaded " +
            "(usually a leftover OpenRGB process). Attempting automatic recovery…");

        KillLeftoverOpenRgb(logger);
        RemoveServiceEntry(logger);
        System.Threading.Thread.Sleep(500);

        return InstallAndStartDriver(driverPath, logger);
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
