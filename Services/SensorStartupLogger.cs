using System;
using System.IO;
using System.Linq;
using System.Text;
using LibreHardwareMonitor.Hardware;
using Microsoft.Extensions.Logging;

namespace PcStatsMonitor.Services;

/// <summary>
/// Diagnostic utility that records all available hardware sensors to a log file on startup.
/// Helps identify sensors that could be read via WMI/native APIs instead of drivers.
/// </summary>
public static class SensorStartupLogger
{
    private const string LogFolderName = "Logs";

    public static void LogHardwareSnapshot(Computer computer, ILogger? logger = null, string eventName = "STARTUP")
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var logDir = Path.Combine(baseDir, LogFolderName);

            if (!Directory.Exists(logDir))
                Directory.CreateDirectory(logDir);

            var dateStr = DateTime.Now.ToString("yyyy-MM-dd");
            var filename = $"sensors_{dateStr}.log";
            var filePath = Path.Combine(logDir, filename);

            var sb = new StringBuilder();
            sb.AppendLine($"=== PC STATS MONITOR SENSOR SNAPSHOT [{eventName}] ===");
            sb.AppendLine($"Timestamp: {DateTime.Now:O}");
            sb.AppendLine($"OS Version: {Environment.OSVersion}");
            sb.AppendLine($"64-Bit OS: {Environment.Is64BitOperatingSystem}");
            sb.AppendLine("-----------------------------------------");
            sb.AppendLine();

            foreach (var hardware in computer.Hardware)
            {
                LogHardwareItem(hardware, sb, "");
            }

            File.AppendAllText(filePath, sb.ToString());
            logger?.LogInformation("Sensor snapshot [{event}] written to: {path}", eventName, filePath);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to write sensor snapshot.");
        }
    }

    private static void LogHardwareItem(IHardware hardware, StringBuilder sb, string indent)
    {
        sb.AppendLine($"{indent}[HW] Name: {hardware.Name}, Type: {hardware.HardwareType}");
        
        foreach (var sensor in hardware.Sensors)
        {
            sb.AppendLine($"{indent}  - [Sensor] Name: \"{sensor.Name}\", Type: {sensor.SensorType}, Value: {sensor.Value}");
        }

        foreach (var sub in hardware.SubHardware)
        {
            LogHardwareItem(sub, sb, indent + "    ");
        }
    }
}
