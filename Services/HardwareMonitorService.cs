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

            // Update once per second
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
            metrics.CpuTemp = GetSensorValue(hardware, SensorType.Temperature, _themeService.CurrentTheme.SensorNames.CpuTemp)
                           ?? GetFirstSensorValue(hardware, SensorType.Temperature)
                           ?? 0;

            if (metrics.CpuLoad == 0)
                metrics.CpuLoad = GetSensorValue(hardware, SensorType.Load, _themeService.CurrentTheme.SensorNames.CpuLoad)
                               ?? GetFirstSensorValue(hardware, SensorType.Load)
                               ?? 0;

            if (metrics.CpuClock == 0)
                metrics.CpuClock = GetSensorValue(hardware, SensorType.Clock, _themeService.CurrentTheme.SensorNames.CpuClock)
                                ?? GetFirstSensorValue(hardware, SensorType.Clock)
                                ?? 0;

            // WMI fallback: when WinRing0 driver is blocked by Windows Defender
            // (HP/Dell/Lenovo laptops with firmware security policy), LHM sensor
            // values stay null/0. WMI reads the same ACPI data Core Temp uses.
            if (metrics.CpuTemp == 0)
            {
                var wmiTemp = WmiSensorService.GetCpuTemperatureCelsius(_logger);
                if (wmiTemp.HasValue && wmiTemp.Value > 0)
                {
                    _logger.LogDebug("[WMI Fallback] Using WMI temp = {t:F1}°C", wmiTemp.Value);
                    metrics.CpuTemp = wmiTemp.Value;
                }
            }

            if (metrics.CpuClock == 0)
            {
                try 
                {
                    if (_cpuClockCounter == null) 
                    {
                        _cpuClockCounter = new System.Diagnostics.PerformanceCounter("Processor Information", "Processor Frequency", "_Total", true);
                    }
                    float val = _cpuClockCounter.NextValue();
                    if (val > 0) metrics.CpuClock = val;
                } 
                catch (Exception ex) 
                {
                    _logger.LogDebug(ex, "[Fallback] PerformanceCounter CPU Clock failed.");
                }
            }
        }
        else if (hardware.HardwareType == HardwareType.GpuNvidia
              || hardware.HardwareType == HardwareType.GpuAmd
              || hardware.HardwareType == HardwareType.GpuIntel)
        {
            if (metrics.GpuTemp == 0)
                metrics.GpuTemp = GetSensorValue(hardware, SensorType.Temperature, _themeService.CurrentTheme.SensorNames.GpuTemp)
                               ?? GetFirstSensorValue(hardware, SensorType.Temperature)
                               ?? 0;

            if (metrics.GpuLoad == 0)
                metrics.GpuLoad = GetSensorValue(hardware, SensorType.Load, _themeService.CurrentTheme.SensorNames.GpuLoad)
                               ?? GetFirstSensorValue(hardware, SensorType.Load)
                               ?? 0;

            if (metrics.GpuClock == 0)
                metrics.GpuClock = GetSensorValue(hardware, SensorType.Clock, _themeService.CurrentTheme.SensorNames.GpuClock)
                                ?? GetFirstSensorValue(hardware, SensorType.Clock)
                                ?? 0;

            // WMI fallback for Intel integrated GPU — WinRing0 doesn't work for
            // Intel iGPU either. Use Windows GPU Performance Counters (Task Manager source)
            // for load, and ACPI thermal zone max for temperature.
            if (metrics.GpuLoad == 0)
            {
                var wmiLoad = WmiSensorService.GetGpuLoadPercent(_logger);
                if (wmiLoad.HasValue && wmiLoad.Value > 0)
                {
                    _logger.LogDebug("[WMI Fallback] GPU load = {l:F1}%", wmiLoad.Value);
                    metrics.GpuLoad = wmiLoad.Value;
                }
            }

            if (metrics.GpuClock == 0)
            {
                var wmiClock = WmiSensorService.GetGpuClockMhz(_logger);
                if (wmiClock.HasValue && wmiClock.Value > 0)
                {
                    _logger.LogDebug("[WMI Fallback] GPU clock = {c:F0} MHz", wmiClock.Value);
                    metrics.GpuClock = wmiClock.Value;
                }
            }

            if (metrics.GpuTemp == 0)
            {
                var wmiTemp = WmiSensorService.GetGpuTemperatureCelsius(_logger);
                if (wmiTemp.HasValue && wmiTemp.Value > 0)
                {
                    _logger.LogDebug("[WMI Fallback] GPU temp = {t:F1}°C", wmiTemp.Value);
                    metrics.GpuTemp = wmiTemp.Value;
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
