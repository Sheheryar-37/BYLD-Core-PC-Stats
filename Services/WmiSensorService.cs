using System;
using System.Linq;
using System.Management;
using Microsoft.Extensions.Logging;

namespace PcStatsMonitor.Services;

/// <summary>
/// Reads CPU and GPU sensors via Windows WMI — no kernel driver required.
///
/// WHY THIS EXISTS:
///   Core Temp uses a properly EV-signed kernel driver that Windows allows.
///   LibreHardwareMonitor's WinRing0x64.sys is on Microsoft's Vulnerable Driver
///   Blocklist (CVE-2020-14979) and is silently blocked on Windows 11 22H2+ —
///   even with admin rights. This service uses WMI/ACPI as a fallback, the
///   same interfaces that Windows Task Manager and Windows Security Center use.
/// </summary>
public static class WmiSensorService
{
    // ACPI thermal zone values are in tenths of Kelvin (2732 = 273.2 K → 0 °C)
    private const float KelvinTenthsOffset = 2732f;

    // ── CPU Temperature ─────────────────────────────────────────────────────

    /// <summary>
    /// Reads CPU temperature from ACPI Thermal Zones via WMI root\WMI.
    /// Works on HP/Dell/Lenovo laptops where WinRing0 MSR access is blocked.
    /// </summary>
    public static float? GetCpuTemperatureCelsius(ILogger? logger = null)
    {
        // Attempt 1: ACPI Thermal Zones (works on many laptops)
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI",
                "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");

            float best = float.MinValue;
            bool found = false;

            foreach (ManagementObject obj in searcher.Get())
            {
                var raw     = Convert.ToSingle(obj["CurrentTemperature"]);
                var celsius = (raw - KelvinTenthsOffset) / 10f;

                logger?.LogDebug("[WMI] ThermalZone raw={r} → {c:F1}°C", raw, celsius);

                if (celsius > 0 && celsius < 110 && celsius > best)
                {
                    best  = celsius;
                    found = true;
                }
            }

            if (found) return best;
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "[WMI] MSAcpi_ThermalZoneTemperature unavailable.");
        }

        // Attempt 2: Win32_PerfFormattedData_Counters_ThermalZoneInformation (Windows 11 desktops)
        try
        {
            using var searcher2 = new ManagementObjectSearcher(
                "SELECT Temperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation");

            float best2 = float.MinValue;
            bool found2 = false;

            foreach (ManagementObject obj in searcher2.Get())
            {
                // Temperature is in Kelvin (integer) on this counter
                var kelvin = Convert.ToSingle(obj["Temperature"]);
                var celsius = kelvin - 273.15f;

                logger?.LogDebug("[WMI] ThermalZoneInfo = {c:F1}°C (Kelvin={k})", celsius, kelvin);

                // Reject 24–26°C as likely BIOS default (e.g., 298K = 24.85°C on many AMD boards)
                if (celsius > 26 && celsius < 110 && celsius > best2)
                {
                    best2  = celsius;
                    found2 = true;
                }
            }

            if (found2) return best2;
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "[WMI] ThermalZoneInformation counter unavailable.");
        }

        return null;
    }

    // ── CPU Clock ───────────────────────────────────────────────────────────

    /// <summary>
    /// Reads current CPU clock speed (MHz) using dynamic performance counters.
    /// On AMD Ryzen with HVCI, Win32_Processor.CurrentClockSpeed returns a static
    /// base frequency.  We use PercentofMaximumFrequency instead — it updates in
    /// real time with boost/throttle and works without a kernel driver.
    /// </summary>
    public static float? GetCpuClockMhz(ILogger? logger = null)
    {
        try
        {
            // ── Attempt 1: Dynamic clock via PercentofMaximumFrequency ──
            // This counter updates every second and reflects actual P-state / boost.
            // Actual MHz = MaxClockSpeed × (PercentofMaximumFrequency / 100)
            float percentOfMax = 0f;
            try
            {
                using var searcherPct = new ManagementObjectSearcher(
                    "SELECT PercentofMaximumFrequency FROM Win32_PerfFormattedData_Counters_ProcessorInformation WHERE Name='_Total'");

                foreach (ManagementObject obj in searcherPct.Get())
                {
                    percentOfMax = Convert.ToSingle(obj["PercentofMaximumFrequency"]);
                    break;
                }
            }
            catch (Exception ex)
            {
                logger?.LogDebug(ex, "[WMI] PercentofMaximumFrequency unavailable.");
            }

            if (percentOfMax > 0)
            {
                // Get the base reference frequency from Win32_Processor
                using var searcherMax = new ManagementObjectSearcher("SELECT MaxClockSpeed FROM Win32_Processor");
                foreach (ManagementObject obj in searcherMax.Get())
                {
                    var maxClock = Convert.ToSingle(obj["MaxClockSpeed"]);
                    if (maxClock > 0)
                    {
                        var actualClock = maxClock * (percentOfMax / 100f);
                        logger?.LogDebug("[WMI] CPU dynamic clock = {f:F0} MHz ({p}% of {m} MHz base)", actualClock, percentOfMax, maxClock);
                        return actualClock;
                    }
                }
            }

            // ── Attempt 2: Static fallback — CurrentClockSpeed ──
            using var searcherProc = new ManagementObjectSearcher("SELECT CurrentClockSpeed FROM Win32_Processor");
            foreach (ManagementObject obj in searcherProc.Get())
            {
                var clock = Convert.ToSingle(obj["CurrentClockSpeed"]);
                if (clock > 0)
                {
                    logger?.LogDebug("[WMI] CPU CurrentClockSpeed = {f} MHz (static fallback)", clock);
                    return clock;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "[WMI] CPU Clock detection failed.");
            return null;
        }
    }

    // ── GPU Load ────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads GPU 3D engine load (%) via Windows GPU Performance Counters.
    /// Works on all GPUs (Intel integrated, AMD, Nvidia) without any driver.
    /// This is the same data source Windows Task Manager uses for GPU %.
    /// </summary>
    public static float? GetGpuLoadPercent(ILogger? logger = null)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\CIMv2",
                "SELECT Name, UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");

            float totalLoad = 0f;
            bool  found     = false;

            foreach (ManagementObject obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString() ?? "";
                if (!name.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
                    continue;

                var util = Convert.ToSingle(obj["UtilizationPercentage"]);
                // Only log non-zero entries to avoid hundreds of spam lines
                if (util > 0)
                    logger?.LogDebug("[WMI] GPU 3D engine {n} = {u}%", name, util);
                totalLoad += util;
                found      = true;
            }

            if (found)
                logger?.LogDebug("[WMI] GPU total 3D load = {l:F1}%", Math.Min(totalLoad, 100f));

            return found ? Math.Min(totalLoad, 100f) : null;
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "[WMI] GPU performance counters unavailable.");
            return null;
        }
    }

    // ── GPU Temperature ─────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to read GPU temperature via ACPI thermal zones.
    /// On many laptops, a thermal zone corresponds to the GPU/dGPU.
    /// Returns the highest zone temperature (most likely to be GPU on multi-zone systems).
    /// </summary>
    public static float? GetGpuTemperatureCelsius(ILogger? logger = null)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI",
                "SELECT InstanceName, CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
 
            float best = float.MinValue;
            bool  found = false;
 
            foreach (ManagementObject obj in searcher.Get())
            {
                var raw     = Convert.ToSingle(obj["CurrentTemperature"]);
                var celsius = (raw - KelvinTenthsOffset) / 10f;
                var name    = obj["InstanceName"]?.ToString() ?? "";
 
                logger?.LogDebug("[WMI] GPU ThermalZone [{n}] = {c:F1}°C", name, celsius);
 
                // On laptops with both CPU and GPU thermal zones, the GPU zone
                // is usually the hotter one or named "TZ00"/"GPU"
                if (celsius > 0 && celsius > best)
                {
                    best  = celsius;
                    found = true;
                }
            }
 
            return found ? best : null;
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "[WMI] GPU thermal zone unavailable.");
            return null;
        }
    }

    /// <summary>
    /// GPU clock is not natively exposed by standard WMI classes.
    /// Returns null to signal it's unreadable via WMI fallback.
    /// </summary>
    public static float? GetGpuClockMhz(ILogger? logger = null)
    {
        return null; // Standard WMI does not provide dynamic GPU clock without a driver.
    }
}
