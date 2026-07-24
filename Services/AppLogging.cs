using System;
using System.IO;
using Serilog.Core;
using Serilog.Events;

namespace PcStatsMonitor.Services;

/// <summary>
/// Central on/off switch for diagnostic logging (the Serilog file log plus the hardware,
/// sensor, display and user-action logs).
///
/// Logging writes to disk continuously, so it is off by default and only turned on when
/// diagnostics are actually needed. The user's choice is persisted in the theme config and
/// re-applied on the next launch; the first run starts with logging OFF.
///
/// Crash logs are deliberately NOT gated — they are rare, small, and are exactly what is
/// needed when something goes wrong.
/// </summary>
public static class AppLogging
{
    /// <summary>Drives the Serilog file sink at runtime without rebuilding the logger.</summary>
    public static LoggingLevelSwitch LevelSwitch { get; } = new(LogEventLevel.Fatal);

    /// <summary>True while diagnostic logging is enabled. Custom loggers check this.</summary>
    public static bool Enabled { get; private set; }

    /// <summary>Turns diagnostic logging on or off for the whole application.</summary>
    public static void SetEnabled(bool enabled)
    {
        Enabled = enabled;
        LevelSwitch.MinimumLevel = enabled ? LogEventLevel.Information : LogEventLevel.Fatal;
    }

    /// <summary>
    /// A way to force logging on BEFORE the app starts, for capturing a crash that happens
    /// at launch (before Settings can be opened). Active when either the environment
    /// variable <c>BYLD_LOG=1</c> is set, or an empty file named <c>logging.on</c> sits
    /// next to the executable. Checked at startup in addition to the saved setting.
    /// </summary>
    public static bool OverrideActive()
    {
        try
        {
            if (Environment.GetEnvironmentVariable("BYLD_LOG") == "1") return true;
            return File.Exists(Path.Combine(AppContext.BaseDirectory, "logging.on"));
        }
        catch
        {
            return false;
        }
    }
}
