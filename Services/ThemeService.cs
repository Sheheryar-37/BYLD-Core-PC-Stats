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
        try
        {
            if (File.Exists(_themePath))
            {
                var json = File.ReadAllText(_themePath);
                var config = JsonSerializer.Deserialize<ThemeConfig>(json);
                if (config != null)
                {
                    CurrentTheme = config;
                    NotifyThemeUpdated();
                }
            }
            else
            {
                SaveTheme(true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load theme.json");
        }
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
