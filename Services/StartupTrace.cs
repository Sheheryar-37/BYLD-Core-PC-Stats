using System;
using System.IO;

namespace PcStatsMonitor.Services;

/// <summary>
/// A synchronous, unbuffered breadcrumb trail through application startup.
///
/// WHY THIS EXISTS: Serilog buffers writes, so when the process dies hard — an
/// uncatchable StackOverflowException or a native access violation in a driver/GPU
/// library — the final entries never reach disk and no crash log is written either.
/// The app simply vanishes with the log stopping a second short of the real cause.
///
/// Each call here does a direct File.AppendAllText, which is flushed by the time it
/// returns, so the LAST line in startup-trace.log is always the last step that
/// actually completed. Deliberately NOT gated by <see cref="AppLogging"/>: it is a
/// dozen short lines per launch and it is exactly what is needed when startup fails.
/// </summary>
public static class StartupTrace
{
    private const string FileName = "startup-trace.log";

    /// <summary>Records a completed startup step. Never throws.</summary>
    public static void Write(string step)
    {
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "logs");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, FileName),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {step}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never break startup.
        }
    }

    /// <summary>Starts a fresh trail so each launch is read in isolation.</summary>
    public static void BeginSession()
    {
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "logs");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, FileName),
                $"=== SESSION {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ==={Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never break startup.
        }
    }
}
