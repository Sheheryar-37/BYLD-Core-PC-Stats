using System;
using System.IO;
using System.Text;

namespace PcStatsMonitor.Services;

public static class CrashLogger
{
    private const string LogFolderName = "Logs";

    public static void LogCrash(Exception ex, string context)
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var logDir = Path.Combine(baseDir, LogFolderName);

            if (!Directory.Exists(logDir))
                Directory.CreateDirectory(logDir);

            var dateStr = DateTime.Now.ToString("yyyy-MM-dd");
            var filename = $"crash_{dateStr}.log";
            var filePath = Path.Combine(logDir, filename);

            var sb = new StringBuilder();
            sb.AppendLine("==================================================");
            sb.AppendLine($"[CRASH EVENT] Timestamp: {DateTime.Now:O}");
            sb.AppendLine($"[CONTEXT] {context}");
            sb.AppendLine($"[EXCEPTION TYPE] {ex.GetType().FullName}");
            sb.AppendLine($"[MESSAGE] {ex.Message}");
            sb.AppendLine($"[STACK TRACE]");
            sb.AppendLine(ex.StackTrace);
            
            Exception? inner = ex.InnerException;
            while (inner != null)
            {
                sb.AppendLine("--- INNER EXCEPTION ---");
                sb.AppendLine($"[EXCEPTION TYPE] {inner.GetType().FullName}");
                sb.AppendLine($"[MESSAGE] {inner.Message}");
                sb.AppendLine($"[STACK TRACE]");
                sb.AppendLine(inner.StackTrace);
                inner = inner.InnerException;
            }
            sb.AppendLine("==================================================");
            sb.AppendLine();

            File.AppendAllText(filePath, sb.ToString());
        }
        catch 
        {
            // Failsafe: if writing the crash log fails, we can't do much.
        }
    }
}
