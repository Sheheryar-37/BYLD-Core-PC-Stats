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
    
    // Profiles
    string[] GetProfiles();
    void SaveProfile(string profileName);
    bool LoadProfile(string profileName);
    void DeleteProfile(string profileName);
}

public class ThemeService : IThemeService
{
    private readonly ILogger<ThemeService> _logger;
    private readonly string _themePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "theme.json");
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

    private string GetProfilesDirectory()
    {
        var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Profiles");
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        return dir;
    }

    public string[] GetProfiles()
    {
        try
        {
            var dir = GetProfilesDirectory();
            var files = Directory.GetFiles(dir, "*.json");
            for (int i = 0; i < files.Length; i++)
            {
                files[i] = Path.GetFileNameWithoutExtension(files[i]);
            }
            return files;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get profiles list.");
            return Array.Empty<string>();
        }
    }

    public void SaveProfile(string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName)) return;
        try
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            foreach (var c in invalidChars) profileName = profileName.Replace(c.ToString(), "");

            var path = Path.Combine(GetProfilesDirectory(), $"{profileName}.json");
            var json = JsonSerializer.Serialize(CurrentTheme, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"Failed to save profile {profileName}");
        }
    }

    public bool LoadProfile(string profileName)
    {
        try
        {
            var path = Path.Combine(GetProfilesDirectory(), $"{profileName}.json");
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var opts = new JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
                var config = JsonSerializer.Deserialize<ThemeConfig>(json, opts);
                if (config != null)
                {
                    CurrentTheme = config;
                    SaveTheme(true); // Persist as main theme
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"Failed to load profile {profileName}");
        }
        return false;
    }

    public void DeleteProfile(string profileName)
    {
        try
        {
            var path = Path.Combine(GetProfilesDirectory(), $"{profileName}.json");
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"Failed to delete profile {profileName}");
        }
    }
}
