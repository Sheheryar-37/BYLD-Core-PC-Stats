using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace PcStatsMonitor.Services;

/// <summary>
/// Ensures WinRing0x64.sys (shipped alongside the exe) is registered and
/// started as a kernel service before LibreHardwareMonitor opens the computer.
///
/// Without this driver, MSR-based sensors (CPU temperature, CPU clock speed)
/// return null even when the app runs as Administrator, because Windows Defender
/// often silently blocks or quarantines the driver file.
///
/// This service:
///   1. Adds a Windows Defender exclusion for the application folder
///   2. Registers WinRing0x64.sys with the Service Control Manager
///   3. Starts the kernel driver
/// </summary>
public static class KernelDriverService
{
    private const string DriverName    = "WinRing0_1_2_0";
    private const string DriverSysFile = "WinRing0x64.sys";

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
    private static extern bool CloseServiceHandle(nint h);

    private const uint ALL_ACCESS    = 0xF003F;
    private const uint SVC_ALL       = 0xF01FF;
    private const uint KERNEL_DRV    = 0x1;
    private const uint DEMAND_START  = 0x3;
    private const uint ERR_IGNORE    = 0x0;
    private const int  E_EXISTS      = 1073;  // ERROR_SERVICE_EXISTS
    private const int  E_RUNNING     = 1056;  // ERROR_SERVICE_ALREADY_RUNNING

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
            var driverPath = Path.Combine(AppContext.BaseDirectory, DriverSysFile);

            if (!File.Exists(driverPath))
            {
                logger.LogWarning("[Driver] {f} not found next to exe at {p}. " +
                    "CPU temp/clock sensors will not be available.", DriverSysFile, driverPath);
                return;
            }

            logger.LogDebug("[Driver] Driver found at {p}", driverPath);

            // Step 1: Windows Defender exclusion (must be done BEFORE SCM install
            // or Defender will quarantine the driver as HackTool:Win32/Winring0)
            AddDefenderExclusion(AppContext.BaseDirectory, logger);

            // Steps 2+3: Register as kernel service and start it
            InstallAndStartDriver(driverPath, logger);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Driver] Unexpected error. CPU temp/clock may be unavailable.");
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

    private static void InstallAndStartDriver(string driverPath, ILogger logger)
    {
        var hScm = OpenSCManager(null, null, ALL_ACCESS);
        if (hScm == 0)
        {
            logger.LogWarning("[Driver] Cannot open SCM (err={e}). App must run as Administrator.",
                              Marshal.GetLastWin32Error());
            return;
        }

        try
        {
            var hSvc = CreateService(hScm, DriverName, DriverName,
                SVC_ALL, KERNEL_DRV, DEMAND_START, ERR_IGNORE, driverPath,
                null, 0, null, null, null);

            if (hSvc == 0)
            {
                int err = Marshal.GetLastWin32Error();
                if (err == E_EXISTS)
                {
                    logger.LogDebug("[Driver] Service already registered, opening existing entry…");
                    hSvc = OpenService(hScm, DriverName, SVC_ALL);
                }
                else
                {
                    logger.LogWarning("[Driver] CreateService failed (err={e}). " +
                        "CPU temp/clock may be unavailable.", err);
                    return;
                }
            }

            if (hSvc == 0) return;

            try
            {
                bool started = StartService(hSvc, 0, null);
                if (started)
                {
                    logger.LogInformation("[Driver] WinRing0 kernel driver started ✓ — " +
                        "CPU temperature and clock speed sensors should now work.");
                }
                else
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err == E_RUNNING)
                    {
                        logger.LogDebug("[Driver] WinRing0 already running ✓");
                    }
                    else
                    {
                        logger.LogWarning("[Driver] StartService failed (err={e}). " +
                            "Check Windows Security → Protection History for blocked drivers.", err);
                            
                        // 1275 = ERROR_DRIVER_BLOCKED
                        if (err == 1275)
                        {
                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                System.Windows.MessageBox.Show(
                                    "Windows Security (Memory Integrity / Core Isolation) is blocking the hardware sensor driver (WinRing0x64.sys).\n\n" +
                                    "Because of this, CPU Temperature and CPU Clock speeds cannot be read from your processor and will show as 0.\n\n" +
                                    "To fix this, you must temporarily disable 'Memory Integrity' in Windows Security -> Core Isolation settings.",
                                    "Sensor Driver Blocked", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                            });
                        }
                    }
                }
            }
            finally { CloseServiceHandle(hSvc); }
        }
        finally { CloseServiceHandle(hScm); }
    }
}
