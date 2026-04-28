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

            // ── HVCI / Memory Integrity Status ──
            try
            {
                var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity");
                if (key != null)
                {
                    var enabled = key.GetValue("Enabled")?.ToString() ?? "not found";
                    sb.AppendLine($"[HVCI] Memory Integrity (HVCI) Registry Enabled = {enabled} (1=Active/Blocking, 0=Disabled)");
                }
                else
                {
                    sb.AppendLine("[HVCI] Registry key not found — Memory Integrity likely not configured.");
                }
            }
            catch (Exception ex) { sb.AppendLine($"[HVCI] Check failed: {ex.Message}"); }
            sb.AppendLine();

            // ── WMI Fan Query (Win32_Fan) ──
            try
            {
                using var fanSearcher = new System.Management.ManagementObjectSearcher("SELECT * FROM Win32_Fan");
                int fanCount = 0;
                foreach (System.Management.ManagementObject obj in fanSearcher.Get())
                {
                    var name = obj["Name"]?.ToString() ?? "Unknown";
                    var status = obj["Status"]?.ToString() ?? "Unknown";
                    var deviceId = obj["DeviceID"]?.ToString() ?? "Unknown";
                    sb.AppendLine($"[WMI Fan {fanCount}] Name: {name}, Status: {status}, DeviceID: {deviceId}");
                    fanCount++;
                }
                if (fanCount == 0)
                    sb.AppendLine("[WMI Fan] No fans detected via Win32_Fan WMI class.");
            }
            catch (Exception ex) { sb.AppendLine($"[WMI Fan] Query failed: {ex.Message}"); }
            sb.AppendLine();

            // ── Physical Disk Inventory ──
            try
            {
                using var diskSearcher = new System.Management.ManagementObjectSearcher(@"Root\Microsoft\Windows\Storage", "SELECT DeviceId, FriendlyName, MediaType, BusType, Size FROM MSFT_PhysicalDisk");
                foreach (System.Management.ManagementObject obj in diskSearcher.Get())
                {
                    var id = obj["DeviceId"]?.ToString() ?? "?";
                    var name = obj["FriendlyName"]?.ToString() ?? "Unknown";
                    var media = Convert.ToInt32(obj["MediaType"] ?? 0);
                    var bus = Convert.ToInt32(obj["BusType"] ?? 0);
                    var size = Convert.ToInt64(obj["Size"] ?? 0) / (1024L * 1024L * 1024L);
                    string mediaStr = media == 4 ? "SSD" : media == 3 ? "HDD" : "Unknown";
                    string busStr = bus == 17 ? "NVMe" : bus == 11 ? "SATA" : bus == 7 ? "USB" : $"Bus({bus})";
                    sb.AppendLine($"[Disk {id}] {name} | {busStr} {mediaStr} | {size} GB");
                }
            }
            catch (Exception ex) { sb.AppendLine($"[Disk] Query failed: {ex.Message}"); }
            sb.AppendLine();
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
