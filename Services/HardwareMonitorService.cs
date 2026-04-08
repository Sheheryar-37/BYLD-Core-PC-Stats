using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PcStatsMonitor.Models;

namespace PcStatsMonitor.Services;

public interface IHardwareMonitorService
{
    HardwareMetrics GetCurrentMetrics();
    event EventHandler<HardwareMetrics>? MetricsUpdated;
}

    /// <summary>
    /// Background service that continuously polls hardware sensors and updates the UI metrics.
    /// </summary>
    public class HardwareMonitorService : BackgroundService, IHardwareMonitorService
    {
        private readonly ILogger<HardwareMonitorService> _logger;
        private readonly IThemeService _themeService;
        private readonly Computer _computer;
        private HardwareMetrics _currentMetrics = new();
        private System.Diagnostics.PerformanceCounter? _cpuClockCounter;

        // ── WMI GPU throttle: only query every N cycles to avoid hammering the system ──
        private int _wmiGpuCycleCounter = 0;
        private const int WmiGpuQueryInterval = 5; // Query WMI GPU load every 5 seconds (5 × 1000ms)
        private float _cachedWmiGpuLoad = 0f;

        // ── AMD iGPU SoC temp: used as CPU temp proxy when WinRing0 is blocked by HVCI ──
        private float _cachedIgpuSocTemp = 0f;
    
    public event EventHandler<HardwareMetrics>? MetricsUpdated;

    public HardwareMonitorService(ILogger<HardwareMonitorService> logger, IThemeService themeService)
    {
        _logger = logger;
        _themeService = themeService;
        
        var config = _themeService.CurrentTheme;

        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsStorageEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true,
            IsNetworkEnabled = true,
            IsBatteryEnabled = true
        };
        
        try
        {
            _computer.Open();
            // Allow 2 seconds for initial polling to complete before taking a diagnostic snapshot.
            // This ensures that the snapshot contains actual sensor values instead of 0s.
            System.Threading.Tasks.Task.Run(async () => {
                await System.Threading.Tasks.Task.Delay(2000);
                SensorStartupLogger.LogHardwareSnapshot(_computer, _logger);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize LibreHardwareMonitor");
        }

        // Warm up PerformanceCounter — first call always returns 0, second call returns real data.
        // Do this in background so startup is not delayed.
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                _cpuClockCounter = new System.Diagnostics.PerformanceCounter("Processor Information", "Processor Frequency", "_Total", true);
                _cpuClockCounter.NextValue(); // Discard warmup value — always 0
                _logger.LogInformation("[CPU] PerformanceCounter warmed up successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[CPU] PerformanceCounter warmup failed — WMI fallback will be used.");
                _cpuClockCounter = null;
            }
        });
    }

    /// <summary>
    /// Direct access to the most recently polled hardware state.
    /// </summary>
    public HardwareMetrics GetCurrentMetrics() => _currentMetrics;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                UpdateMetrics();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating hardware metrics");
            }

            // Update once per second — hardware stats don't change fast enough to need 10Hz.
            // This reduces CPU usage by ~90% compared to the previous 100ms interval.
            await Task.Delay(1000, stoppingToken);
        }
        
        SensorStartupLogger.LogHardwareSnapshot(_computer, _logger, "EXIT");
        _computer.Close();
    }

    /// <summary>
    /// Iterates through all detected hardware components and triggers sensor reads.
    /// </summary>
    private void UpdateMetrics()
    {
        var metrics = new HardwareMetrics();
        
        foreach (var hardware in _computer.Hardware)
        {
            hardware.Update();

            // Also update any sub-hardware (common on modern CPUs with chiplets)
            foreach (var sub in hardware.SubHardware)
                sub.Update();

            ReadHardware(hardware, metrics);

            // Also read sub-hardware sensors
            foreach (var sub in hardware.SubHardware)
                ReadHardware(sub, metrics);
        }

        // ── Post-loop fallback: use AMD iGPU SoC temp as CPU temp proxy ──────────────
        if (metrics.CpuTemp <= 0 && _cachedIgpuSocTemp > 0)
        {
            metrics.CpuTemp = _cachedIgpuSocTemp;
            _logger.LogDebug("[CPU] Using AMD iGPU SoC temperature as CPU proxy: {t:F0}°C", _cachedIgpuSocTemp);
        }

        // Try to map DriveInfo sizes (Logical partitions) to the Physical Drives listed by LHM
        try
        {
            var driveInfos = System.IO.DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == System.IO.DriveType.Fixed).ToList();
            for (int i = 0; i < Math.Min(metrics.Drives.Count, driveInfos.Count); i++)
            {
                var dInfo = driveInfos[i];
                var totalGb = dInfo.TotalSize / (1024.0 * 1024.0 * 1024.0);
                metrics.Drives[i].SizeString = $"{Math.Round(totalGb)} GB (Drive {dInfo.Name.Replace("\\", "")})";
            }
        }
        catch { }

        // Ensure exactly 2 drives for the UI layout
        if (metrics.Drives.Count == 0)
        {
            metrics.Drives.Add(new DriveMetrics { Vendor = Constants.FallbackDriveVendor, Model = Constants.FallbackPrimaryDriveModel, SizeString = "" });
            metrics.Drives.Add(new DriveMetrics { Vendor = Constants.FallbackDriveVendor, Model = Constants.FallbackSecondaryDriveModel, SizeString = "" });
        }
        else if (metrics.Drives.Count == 1)
        {
            metrics.Drives.Add(new DriveMetrics { Vendor = Constants.FallbackDriveVendor, Model = Constants.FallbackSecondaryDriveModel, SizeString = "" });
        }
        else if (metrics.Drives.Count > 2)
        {
            metrics.Drives = metrics.Drives.Take(2).ToList();
        }

        _currentMetrics = metrics;
        MetricsUpdated?.Invoke(this, metrics);

        // Increment WMI GPU throttle counter
        _wmiGpuCycleCounter++;

        // ── Resolved metrics summary (one line per poll cycle) ──────────────────
        _logger.LogDebug(
            "[METRICS] CPU: {ct:F0}°C / {cl:F0}% / {cc:F0}MHz | GPU: {gt:F0}°C / {gl:F0}% / {gc:F0}MHz | RAM: {rl:F0}% ({ru:F1}/{rt:F1}GB) | Net↑{nu:F0}B/s ↓{nd:F0}B/s",
            metrics.CpuTemp, metrics.CpuLoad, metrics.CpuClock,
            metrics.GpuTemp, metrics.GpuLoad, metrics.GpuClock,
            metrics.RamLoad, metrics.RamUsedGb, metrics.RamTotalGb,
            metrics.NetworkUp, metrics.NetworkDown);
    }

    private void ReadHardware(IHardware hardware, HardwareMetrics metrics)
    {
        // Dump all sensor names to log on first read for diagnostics
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            foreach (var s in hardware.Sensors)
                _logger.LogDebug("[{hw}] Sensor: [{type}] \"{name}\" = {val}", hardware.Name, s.SensorType, s.Name, s.Value);
        }

        if (hardware.HardwareType == HardwareType.Cpu)
        {
            _logger.LogDebug("[CPU] Processing '{hw}' (Type: {t})", hardware.Name, hardware.HardwareType);

            // ── CPU Temperature ──
            // For Zen 4 (Ryzen 7000): LHM exposes 'Core (Tctl/Tdie)' and per-core temps.
            // Scan ALL temperature sensors to find the first non-zero reading (handles Zen 4 naming).
            var lhmTemp = GetSensorValue(hardware, SensorType.Temperature, _themeService.CurrentTheme.SensorNames.CpuTemp);
            
            if (!lhmTemp.HasValue || lhmTemp.Value <= 0)
            {
                // Try all temperature sensors and pick the first non-zero value
                var fallbackTemp = hardware.Sensors
                    .Where(s => s.SensorType == SensorType.Temperature && s.Value.HasValue && s.Value.Value > 0)
                    .OrderBy(s => (s.Name.Contains("Tctl") || s.Name.Contains("Tdie")) ? 0 : 1) // Prefer Tctl/Tdie on AMD
                    .FirstOrDefault();

                if (fallbackTemp != null)
                {
                    lhmTemp = fallbackTemp.Value;
                    _logger.LogDebug("[CPU] Temp resolved via fallback sensor '{n}': {v:F1}°C", fallbackTemp.Name, lhmTemp);
                }
            }

            if (lhmTemp.HasValue && lhmTemp.Value > 0)
            {
                metrics.CpuTemp = lhmTemp.Value;
                _logger.LogDebug("[CPU] Temp resolved via LHM: {v:F1}°C", metrics.CpuTemp);
            }
            else
            {
                // Even if LHM returned 0, it might be that the sensor is just initialized. 
                // Only try WMI if LHM returned nothing or we are consistently getting 0.
                _logger.LogDebug("[CPU] LHM temp = {v} (zero/null). Trying WMI fallback...", lhmTemp);
                var wmiTemp = WmiSensorService.GetCpuTemperatureCelsius(_logger);
                if (wmiTemp.HasValue && wmiTemp.Value > 0)
                {
                    metrics.CpuTemp = wmiTemp.Value;
                }
                else if (lhmTemp.HasValue) 
                {
                    // If WMI also failed but LHM at least gave us 0, use 0 (don't override with null/prev)
                    metrics.CpuTemp = 0;
                }
            }

            // ── CPU Load ──
            if (metrics.CpuLoad == 0)
            {
                var lhmLoad = GetSensorValue(hardware, SensorType.Load, _themeService.CurrentTheme.SensorNames.CpuLoad)
                           ?? GetFirstSensorValue(hardware, SensorType.Load);
                metrics.CpuLoad = lhmLoad ?? 0;
                _logger.LogDebug("[CPU] Load = {v:F1}% (LHM)", metrics.CpuLoad);
            }

            // ── CPU Clock ──
            if (metrics.CpuClock == 0)
            {
                var lhmClock = GetSensorValue(hardware, SensorType.Clock, _themeService.CurrentTheme.SensorNames.CpuClock);
                
                if (!lhmClock.HasValue || lhmClock.Value <= 0)
                    lhmClock = GetFirstSensorValue(hardware, SensorType.Clock);

                if (lhmClock.HasValue && lhmClock.Value > 0)
                {
                    metrics.CpuClock = lhmClock.Value;
                    _logger.LogDebug("[CPU] Clock resolved via LHM: {v:F0} MHz", metrics.CpuClock);
                }
                else
                {
                    _logger.LogDebug("[CPU] LHM clock = {v} (zero/null). Trying WMI fallback...", lhmClock);
                    
                    // ── Fallback 1: WMI ProcessorInformation (most reliable, no warmup needed) ──
                    var wmiClock = WmiSensorService.GetCpuClockMhz(_logger);
                    if (wmiClock.HasValue && wmiClock.Value > 0)
                    {
                        metrics.CpuClock = wmiClock.Value;
                    }
                    else
                    {
                        // ── Fallback 2: PerformanceCounter (requires warmup — done at startup) ──
                        try
                        {
                            float perfVal = _cpuClockCounter?.NextValue() ?? 0;
                            if (perfVal > 0)
                            {
                                metrics.CpuClock = perfVal;
                            }
                            else if (lhmClock.HasValue)
                            {
                                metrics.CpuClock = lhmClock.Value; // Last resort: even if 0, keep it from LHM
                            }
                        }
                        catch { }
                    }
                }
            }
        }
        else if (hardware.HardwareType == HardwareType.GpuNvidia
              || hardware.HardwareType == HardwareType.GpuAmd
              || hardware.HardwareType == HardwareType.GpuIntel)
        {
            // ── Discrete GPU preference: skip integrated GPUs if a discrete GPU exists ──
            // Integrated GPUs (e.g. "AMD Radeon(TM) Graphics") report 512MB VRAM.
            // Discrete GPUs (e.g. "AMD Radeon RX 9070") report 16GB+ VRAM.
            var totalVram = hardware.Sensors
                .Where(s => s.SensorType == SensorType.SmallData && s.Name == "GPU Memory Total")
                .Select(s => s.Value ?? 0)
                .FirstOrDefault();

            bool isLikelyIntegrated = totalVram > 0 && totalVram < 2048; // < 2GB = integrated
            bool haveDiscreteData = metrics.GpuTemp > 0 || metrics.GpuLoad > 0 || metrics.GpuClock > 0;

            // ── AMD iGPU SoC temp: always cache it for CPU temp fallback, even if we skip the iGPU for display ──
            if (isLikelyIntegrated && hardware.HardwareType == HardwareType.GpuAmd)
            {
                var socTemp = hardware.Sensors
                    .Where(s => s.SensorType == SensorType.Temperature && s.Name.Contains("SoC", StringComparison.OrdinalIgnoreCase))
                    .Select(s => s.Value ?? 0)
                    .FirstOrDefault();

                if (socTemp > 0)
                {
                    _cachedIgpuSocTemp = socTemp;
                    _logger.LogDebug("[GPU] Cached AMD iGPU SoC temp: {t:F0}°C (for CPU fallback)", socTemp);
                }
            }

            if (isLikelyIntegrated && haveDiscreteData)
            {
                _logger.LogDebug("[GPU] Skipping integrated GPU '{hw}' (VRAM={v}MB) — discrete GPU already has data.", hardware.Name, totalVram);
                return; // Don't overwrite discrete GPU values with integrated GPU values
            }

            _logger.LogDebug("[GPU] Processing '{hw}' (Type: {t}, VRAM={v}MB)", hardware.Name, hardware.HardwareType, totalVram);

            // ── GPU Temperature ──
            {
                var lhmTemp = GetSensorValue(hardware, SensorType.Temperature, _themeService.CurrentTheme.SensorNames.GpuTemp)
                           ?? GetFirstSensorValue(hardware, SensorType.Temperature);
                if (lhmTemp.HasValue && lhmTemp.Value > 0)
                {
                    metrics.GpuTemp = lhmTemp.Value;
                    _logger.LogDebug("[GPU] Temp resolved via LHM: {v:F1}°C", metrics.GpuTemp);
                }
                else if (metrics.GpuTemp == 0)
                {
                    _logger.LogDebug("[GPU] LHM temp = {v}. Trying WMI fallback...", lhmTemp);
                    var wmiTemp = WmiSensorService.GetGpuTemperatureCelsius(_logger);
                    if (wmiTemp.HasValue && wmiTemp.Value > 0)
                    {
                        metrics.GpuTemp = wmiTemp.Value;
                    }
                }
            }

            // ── GPU Load (throttled WMI fallback) ──
            {
                var lhmLoad = GetSensorValue(hardware, SensorType.Load, _themeService.CurrentTheme.SensorNames.GpuLoad)
                           ?? GetFirstSensorValue(hardware, SensorType.Load);
                if (lhmLoad.HasValue && lhmLoad.Value > 0)
                {
                    metrics.GpuLoad = lhmLoad.Value;
                    _logger.LogDebug("[GPU] Load resolved via LHM: {v:F1}%", metrics.GpuLoad);
                }
                else if (metrics.GpuLoad == 0)
                {
                    // Only query WMI every N cycles to avoid hammering the system
                    if (_wmiGpuCycleCounter % WmiGpuQueryInterval == 0)
                    {
                        _logger.LogDebug("[GPU] LHM load = {v}. Querying WMI (throttled, every {n}s)...", lhmLoad, WmiGpuQueryInterval);
                        var wmiLoad = WmiSensorService.GetGpuLoadPercent(_logger);
                        _cachedWmiGpuLoad = wmiLoad ?? 0f;
                    }
                    metrics.GpuLoad = _cachedWmiGpuLoad;
                }
            }

            // ── GPU Clock (prefer discrete GPU's non-zero value) ──
            {
                var lhmClock = GetSensorValue(hardware, SensorType.Clock, _themeService.CurrentTheme.SensorNames.GpuClock)
                            ?? GetFirstSensorValue(hardware, SensorType.Clock);
                if (lhmClock.HasValue && lhmClock.Value > 0)
                {
                    metrics.GpuClock = lhmClock.Value;
                    _logger.LogDebug("[GPU] Clock resolved via LHM: {v:F0} MHz", metrics.GpuClock);
                }
                else if (metrics.GpuClock == 0)
                {
                    _logger.LogDebug("[GPU] LHM clock = {v}. No WMI fallback available for GPU clock.", lhmClock);
                }
            }
        }
        else if (hardware.HardwareType == HardwareType.Memory || hardware.Name.Contains("Memory", StringComparison.OrdinalIgnoreCase))
        {
            var load = GetSensorValue(hardware, SensorType.Load, _themeService.CurrentTheme.SensorNames.RamLoad) 
                    ?? GetFirstSensorValue(hardware, SensorType.Load);
            
            if (load.HasValue) metrics.RamLoad = load.Value;

            metrics.RamUsedGb = GetSensorValue(hardware, SensorType.Data, Constants.SensorDataMemoryUsed) ?? 0;
            var available = GetSensorValue(hardware, SensorType.Data, Constants.SensorDataMemoryAvailable) ?? 0;
            metrics.RamTotalGb = metrics.RamUsedGb + available;
            _logger.LogDebug("[RAM] Load={l:F1}% | Used={u:F2} GB / Total={t:F2} GB", metrics.RamLoad, metrics.RamUsedGb, metrics.RamTotalGb);
        }
        else if (hardware.HardwareType == HardwareType.Storage)
        {
            var load = GetSensorValue(hardware, SensorType.Load, Constants.SensorLoadTotalActivity) ?? 0;
            var used = GetSensorValue(hardware, SensorType.Data, Constants.SensorDataUsedSpace) ?? 0;
            
            var vendor = hardware.Name;
            var model = Constants.DriveTypeSolidState;
            
            var parts = hardware.Name.Split(new[] { ' ' }, 2);
            if (parts.Length == 2)
            {
                vendor = parts[0].ToUpper();
                model = parts[1];
            }

            _logger.LogDebug("[Storage] '{n}' | Load={l:F1}% | Used={u:F2} GB", hardware.Name, load, used);

            metrics.Drives.Add(new DriveMetrics
            {
                Name = hardware.Name,
                Vendor = vendor,
                Model = model,
                Load = load,
                UsedSpaceGb = used,
                SizeString = ""
            });
        }
        else if (hardware.HardwareType == HardwareType.SuperIO)
        {
            if (metrics.MotherboardTemp == 0)
                metrics.MotherboardTemp = GetFirstSensorValue(hardware, SensorType.Temperature) ?? 0;
            
            if (metrics.FanSpeed == 0)
                metrics.FanSpeed = GetFirstSensorValue(hardware, SensorType.Fan) ?? 0;

            _logger.LogDebug("[SuperIO] '{n}' | MBTemp={t:F1}°C | Fan={f:F0} RPM", hardware.Name, metrics.MotherboardTemp, metrics.FanSpeed);
        }
        else if (hardware.HardwareType == HardwareType.Network)
        {
            metrics.NetworkUp += GetSensorValue(hardware, SensorType.Throughput, Constants.NetUpload) ?? 0;
            metrics.NetworkDown += GetSensorValue(hardware, SensorType.Throughput, Constants.NetDownload) ?? 0;
        }
    }


    private float? GetSensorValue(IHardware hardware, SensorType type, IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            var val = hardware.Sensors.FirstOrDefault(s => s.SensorType == type && s.Name.Contains(name, StringComparison.OrdinalIgnoreCase))?.Value;
            if (val.HasValue)
                return val;
        }
        return null;
    }

    private float? GetSensorValue(IHardware hardware, SensorType type, string name)
    {
        return hardware.Sensors.FirstOrDefault(s => s.SensorType == type && s.Name.Contains(name, StringComparison.OrdinalIgnoreCase))?.Value;
    }

    /// <summary>Fallback: returns the value of the very first sensor of a given type.</summary>
    private float? GetFirstSensorValue(IHardware hardware, SensorType type)
    {
        return hardware.Sensors.FirstOrDefault(s => s.SensorType == type)?.Value;
    }
}
