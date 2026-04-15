using System;
using System.IO;

namespace PcStatsMonitor.Services;

/// <summary>
/// Persistence helper that records user configuration changes to a separate log file.
/// Provides an audit trail for troubleshooting settings-related issues.
/// </summary>
public static class UserActionLogger
{
    private const string LogFolderName = "Logs";

    public static void LogAction(string actionDescription)
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var logDir = Path.Combine(baseDir, LogFolderName);

            if (!Directory.Exists(logDir))
                Directory.CreateDirectory(logDir);

            var dateStr = DateTime.Now.ToString("yyyy-MM-dd");
            var filename = $"actions_{dateStr}.log";
            var filePath = Path.Combine(logDir, filename);

            var message = $"[{DateTime.Now:HH:mm:ss}] {actionDescription}{Environment.NewLine}";
            File.AppendAllText(filePath, message);
        }
        catch
        {
            // Fail silently to avoid interrupting the UI flow for a non-critical logging failure.
        }
    }
}
