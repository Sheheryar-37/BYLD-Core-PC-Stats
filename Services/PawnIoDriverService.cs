using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace PcStatsMonitor.Services;

/// <summary>
/// Ensures the PawnIO driver is present before LibreHardwareMonitor opens the
/// computer.
///
/// Since LibreHardwareMonitorLib 0.9.5-pre454 the library reaches CPU MSRs
/// (temperature, clocks) and the motherboard Super I/O (case-fan RPM and
/// control, VRM/chipset temperatures) through <c>PawnIO</c> — a
/// Microsoft-signed, sandboxed driver — instead of the blocklisted WinRing0.
/// PawnIO is not bundled inside the library and must be installed on the
/// machine; when it is missing, those sensors read 0 even under Administrator.
///
/// This service:
///   1. Detects an existing PawnIO installation (its service entry or device).
///   2. Silently installs the bundled <c>PawnIO_setup.exe</c> when it is absent.
///   3. Verifies the device is reachable and, when it is not, tells the user
///      exactly what to install.
/// </summary>
public static class PawnIoDriverService
{
    private const string DevicePath    = @"\\.\PawnIO";
    private const string ServiceRegKey = @"SYSTEM\CurrentControlSet\Services\PawnIO";
    private const string SetupFileName = "PawnIO_setup.exe";
    private const string DownloadUrl   = "https://pawnio.eu/";

    /// <summary>
    /// True when PawnIO could not be found or installed this session. Ring-0
    /// dependent reads (CPU temperature/clock, motherboard fans and temperatures)
    /// will report 0 and the app falls back to its GPU/iGPU proxies.
    /// </summary>
    public static bool DriverUnavailable { get; private set; }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share,
        nint security, uint disposition, uint flags, nint template);

    private const uint GENERIC_RW    = 0xC0000000; // GENERIC_READ | GENERIC_WRITE
    private const uint OPEN_EXISTING = 0x3;

    /// <summary>
    /// Call once at startup, before <c>Computer.Open()</c>. Safe to call on every
    /// launch — returns immediately when PawnIO is already installed.
    /// </summary>
    public static void EnsureAvailable(ILogger logger)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        try
        {
            EnsureAvailableCore(logger);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Driver] PawnIO check failed. " +
                "CPU temp/clock and motherboard fan sensors may be unavailable.");
        }
    }

    private static void EnsureAvailableCore(ILogger logger)
    {
        if (IsPawnIoPresent())
        {
            logger.LogInformation("[Driver] PawnIO present ✓ — CPU temperature, clocks and " +
                "motherboard sensors are available.");
            return;
        }

        logger.LogWarning("[Driver] PawnIO not detected — attempting a silent install of the bundled setup…");
        if (TryInstallPawnIo(logger) && IsPawnIoPresent())
        {
            logger.LogInformation("[Driver] PawnIO installed and verified ✓");
            return;
        }

        DriverUnavailable = true;
        ReportMissing(logger);
    }

    // ── Detection ───────────────────────────────────────────────────────────

    /// <summary>
    /// True when PawnIO is installed. Accepts either a registered kernel service
    /// or an openable device: the device may not be reachable until the service
    /// has started, while the service entry proves the setup already ran.
    /// </summary>
    private static bool IsPawnIoPresent()
    {
        return ServiceRegistered() || TryOpenDevice();
    }

    private static bool ServiceRegistered()
    {
        using var key = Registry.LocalMachine.OpenSubKey(ServiceRegKey);
        return key != null;
    }

    /// <summary>Opens <c>\\.\PawnIO</c> — the same device handle LibreHardwareMonitor needs.</summary>
    private static bool TryOpenDevice()
    {
        using var handle = CreateFile(DevicePath, GENERIC_RW, 0, 0, OPEN_EXISTING, 0, 0);
        return !handle.IsInvalid;
    }

    // ── Silent install ──────────────────────────────────────────────────────

    private static bool TryInstallPawnIo(ILogger logger)
    {
        var setupPath = Path.Combine(AppContext.BaseDirectory, SetupFileName);
        if (!File.Exists(setupPath))
        {
            logger.LogWarning("[Driver] Bundled {f} not found next to the exe — cannot auto-install PawnIO. " +
                "The user must install it from {url}.", SetupFileName, DownloadUrl);
            return false;
        }

        if (RunSetup(setupPath, "-install -silent", logger)) return true;

        // Exit code 183 is ERROR_ALREADY_EXISTS: the setup believes PawnIO is already installed
        // while the driver is NOT actually usable — the state left behind when the service was
        // removed but PawnIO's own files remain (e.g. after a manual cleanup). A plain re-install
        // refuses forever, so the prompt reappeared on every launch no matter what the user did
        // (client round 25, item 4). Uninstall first, then install, to repair it automatically.
        if (_lastSetupExitCode != ErrorAlreadyExists) return false;

        logger.LogWarning("[Driver] PawnIO reports as already installed but is not usable — " +
            "repairing with a clean uninstall/reinstall…");
        RunSetup(setupPath, "-uninstall -silent", logger);
        System.Threading.Thread.Sleep(2000);
        return RunSetup(setupPath, "-install -silent", logger);
    }

    /// <summary>Windows ERROR_ALREADY_EXISTS — PawnIO's setup returns this when it thinks the
    /// driver is already present.</summary>
    private const int ErrorAlreadyExists = 183;

    private static int _lastSetupExitCode;

    /// <summary>
    /// Runs the PawnIO installer unattended. PawnIO_setup.exe uses its own CLI
    /// ("-install -silent"), not Inno flags. The app already holds Administrator,
    /// which the driver install requires.
    /// </summary>
    private static bool RunSetup(string setupPath, string arguments, ILogger logger)
    {
        var psi = new ProcessStartInfo
        {
            FileName        = setupPath,
            Arguments       = arguments,
            UseShellExecute = false,
            CreateNoWindow  = true
        };

        using var proc = Process.Start(psi);
        if (proc == null)
            return false;

        bool exited = proc.WaitForExit(90000);
        return exited && ReportSetupExit(proc, logger);
    }

    private static bool ReportSetupExit(Process proc, ILogger logger)
    {
        int code = proc.ExitCode;
        _lastSetupExitCode = code;
        logger.LogInformation("[Driver] PawnIO_setup.exe exited with code {c}.", code);
        return code == 0;
    }

    // ── User-facing reporting ───────────────────────────────────────────────

    private static void ReportMissing(ILogger logger)
    {
        logger.LogWarning("[Driver] PawnIO unavailable — CPU temp/clock and motherboard fan " +
            "sensors will read 0 this session. Install PawnIO from {url}.", DownloadUrl);

        ShowDriverWarning(
            "BYLD Core needs the PawnIO hardware driver to read your CPU temperature and your " +
            "motherboard's fan and temperature sensors.\n\n" +
            "PawnIO is Microsoft-signed and installs in a few seconds. Click “Get PawnIO” " +
            $"to download it from {DownloadUrl}, then restart BYLD Core.");
    }

    private static void ShowDriverWarning(string message)
    {
        var app = System.Windows.Application.Current;
        if (app == null)
            return;

        app.Dispatcher.Invoke(() => ShowWarningDialog(app, message));
    }

    private static void ShowWarningDialog(System.Windows.Application app, string message)
    {
        // Lower every splash so the themed dialog is not hidden behind the Topmost splash.
        foreach (var splash in app.Windows.OfType<System.Windows.Window>()
                                  .Where(w => w.GetType().Name == "SplashWindow"))
            splash.Topmost = false;

        new PcStatsMonitor.DriverWarningWindow("Hardware Sensor Driver Required", message, DownloadUrl)
            .ShowDialog();
    }
}
