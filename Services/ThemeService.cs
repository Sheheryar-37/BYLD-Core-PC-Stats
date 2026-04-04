using System;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PcStatsMonitor.Models;

namespace PcStatsMonitor.Services;

public interface IThemeService
{
    ThemeConfig CurrentTheme { get; }
    void ReloadTheme();
    void SaveTheme(bool writeToDisk = true);
    void NotifyThemeUpdated();
    event EventHandler<ThemeConfig>? ThemeChanged;
}

public class ThemeService : IThemeService
{
    private readonly ILogger<ThemeService> _logger;
    private readonly string _themePath = "theme.json";
    public ThemeConfig CurrentTheme { get; private set; } = new();

    public event EventHandler<ThemeConfig>? ThemeChanged;

    public ThemeService(ILogger<ThemeService> logger)
    {
        _logger = logger;
        ReloadTheme();
    }

    public void ReloadTheme()
    {
        if (File.Exists(_themePath))
        {
            try
            {
                var json = File.ReadAllText(_themePath);
                // Allow string enum values (e.g. "Auto") from older theme.json files
                var opts = new JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
                var config = JsonSerializer.Deserialize<ThemeConfig>(json, opts);
                if (config != null)
                {
                    CurrentTheme = config;
                    NotifyThemeUpdated();
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Theme] Failed to parse theme.json — resetting to defaults and overwriting.");
            }
        }

        // Either file doesn't exist, or it was corrupt: reset to defaults and write fresh file.
        CurrentTheme = new ThemeConfig();
        SaveTheme(true);
    }

    public void SaveTheme(bool writeToDisk = true)
    {
        if (writeToDisk)
        {
            var json = JsonSerializer.Serialize(CurrentTheme, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_themePath, json);
        }
        NotifyThemeUpdated();
    }

    public void NotifyThemeUpdated()
    {
        ThemeChanged?.Invoke(this, CurrentTheme);
    }
}
