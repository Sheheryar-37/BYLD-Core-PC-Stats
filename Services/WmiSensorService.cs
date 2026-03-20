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
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI",
                "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");

            float best = float.MinValue;
            bool found = false;

            foreach (ManagementObject obj in searcher.Get())
            {
                // CurrentTemperature is in tenths of Kelvin
                var raw     = Convert.ToSingle(obj["CurrentTemperature"]);
                var celsius = (raw - KelvinTenthsOffset) / 10f;

                logger?.LogDebug("[WMI] ThermalZone raw={r} → {c:F1}°C", raw, celsius);

                // Take the highest reasonable zone temp (up to 110C) as "CPU package" peak
                if (celsius > 0 && celsius < 110 && celsius > best)
                {
                    best  = celsius;
                    found = true;
                }
            }

            return found ? best : null;
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "[WMI] MSAcpi_ThermalZoneTemperature unavailable.");
            return null;
        }
    }

    // ── CPU Clock ───────────────────────────────────────────────────────────

    /// <summary>
    /// Reads current CPU clock speed (MHz) from Win32_Processor.
    /// Updates every few seconds and reflects P-state changes.
    /// </summary>
    public static float? GetCpuClockMhz(ILogger? logger = null)
    {
        try
        {
            // Prefer Win32_PerfFormattedData_Counters_ProcessorInformation for dynamic frequency
            using var searcherPerf = new ManagementObjectSearcher(
                "SELECT ProcessorFrequency FROM Win32_PerfFormattedData_Counters_ProcessorInformation WHERE Name='_Total'");
            
            foreach (ManagementObject obj in searcherPerf.Get())
            {
                var freq = Convert.ToSingle(obj["ProcessorFrequency"]);
                if (freq > 0) 
                {
                    logger?.LogDebug("[WMI] CPU ProcessorFrequency = {f} MHz", freq);
                    return freq;
                }
            }

            // Fallback to Win32_Processor
            using var searcherProc = new ManagementObjectSearcher("SELECT CurrentClockSpeed FROM Win32_Processor");
            foreach (ManagementObject obj in searcherProc.Get())
            {
                var clock = Convert.ToSingle(obj["CurrentClockSpeed"]);
                if (clock > 0) return clock;
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
            // Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine gives per-pid,
            // per-engine GPU utilization. We want the 3D engine, sum across all pids.
            using var searcher = new ManagementObjectSearcher(
                @"root\CIMv2",
                "SELECT Name, UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");

            float totalLoad = 0f;
            bool  found     = false;

            foreach (ManagementObject obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString() ?? "";
                // Only sum the 3D engine entries (matches Task Manager's "GPU 3D" column)
                if (!name.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
                    continue;

                var util = Convert.ToSingle(obj["UtilizationPercentage"]);
                logger?.LogDebug("[WMI] GPU 3D engine {n} = {u}%", name, util);
                totalLoad += util;
                found      = true;
            }

            // Clamp to 100% since individual engine entries can theoretically overlap
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
