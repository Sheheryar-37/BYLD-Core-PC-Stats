using System;
using System.IO;
using System.Management;
using System.Text;
using System.Windows.Forms;

namespace PcStatsMonitor.Services;

public static class DisplayDiagnosticLogger
{
    private const string LogFolderName = "Logs";

    public static void LogDisplays(string triggerEvent)
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var logDir = Path.Combine(baseDir, LogFolderName);

            if (!Directory.Exists(logDir))
                Directory.CreateDirectory(logDir);

            var dateStr = DateTime.Now.ToString("yyyy-MM-dd");
            var filename = $"displays_{dateStr}.log";
            var filePath = Path.Combine(logDir, filename);

            var sb = new StringBuilder();
            sb.AppendLine("==================================================");
            sb.AppendLine($"[DISPLAY SNAPSHOT] Timestamp: {DateTime.Now:O} | Trigger: {triggerEvent}");
            
            // 1. Windows Forms Screens
            sb.AppendLine("--- System.Windows.Forms.Screen.AllScreens ---");
            var screens = Screen.AllScreens;
            sb.AppendLine($"Count: {screens.Length}");
            for (int i = 0; i < screens.Length; i++)
            {
                var s = screens[i];
                sb.AppendLine($"  Screen {i}: DeviceName={s.DeviceName}, Bounds={s.Bounds}, WorkingArea={s.WorkingArea}, Primary={s.Primary}, BitsPerPixel={s.BitsPerPixel}");
            }
            sb.AppendLine();

            // 2. WMI Win32_DesktopMonitor
            sb.AppendLine("--- WMI Win32_DesktopMonitor ---");
            LogWmiClass(sb, "Win32_DesktopMonitor", new[] { "DeviceID", "Name", "MonitorType", "MonitorManufacturer", "ScreenWidth", "ScreenHeight", "Status" });

            // 3. WMI Win32_VideoController
            sb.AppendLine("--- WMI Win32_VideoController ---");
            LogWmiClass(sb, "Win32_VideoController", new[] { "DeviceID", "Name", "AdapterRAM", "DriverVersion", "VideoModeDescription", "Status" });

            // 4. Check for DisplayLink or generic USB displays
            sb.AppendLine("--- WMI Win32_PnPEntity (USB Displays / DisplayLink) ---");
            LogUsbDisplays(sb);

            sb.AppendLine("==================================================");
            sb.AppendLine();

            File.AppendAllText(filePath, sb.ToString());
        }
        catch (Exception ex)
        {
            CrashLogger.LogCrash(ex, $"DisplayDiagnosticLogger.LogDisplays({triggerEvent})");
        }
    }

    private static void LogWmiClass(StringBuilder sb, string className, string[] properties)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT * FROM {className}");
            using var collection = searcher.Get();
            int count = 0;
            foreach (var item in collection)
            {
                sb.AppendLine($"  Item {count++}:");
                foreach (var prop in properties)
                {
                    try
                    {
                        var val = item[prop];
                        sb.AppendLine($"    - {prop}: {val}");
                    }
                    catch { }
                }
            }
            if (count == 0) sb.AppendLine("  No items found.");
            sb.AppendLine();
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  [ERROR query {className}] {ex.Message}");
            sb.AppendLine();
        }
    }

    private static void LogUsbDisplays(StringBuilder sb)
    {
        try
        {
            string query = "SELECT * FROM Win32_PnPEntity WHERE Service='USBVideo' OR Name LIKE '%DisplayLink%' OR Name LIKE '%USB Display%'";
            using var searcher = new ManagementObjectSearcher(query);
            using var collection = searcher.Get();
            int count = 0;
            foreach (var item in collection)
            {
                sb.AppendLine($"  Item {count++}:");
                sb.AppendLine($"    - DeviceID: {item["DeviceID"]}");
                sb.AppendLine($"    - Name: {item["Name"]}");
                sb.AppendLine($"    - Service: {item["Service"]}");
                sb.AppendLine($"    - Status: {item["Status"]}");
            }
            if (count == 0) sb.AppendLine("  No specific USB display devices found by generic search.");
            sb.AppendLine();
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  [ERROR query USB Displays] {ex.Message}");
            sb.AppendLine();
        }
    }
}
