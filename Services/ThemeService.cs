using System;
using System.IO;
using System.Linq;
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

    /// <summary>Raised immediately BEFORE the theme is written to disk, so holders
    /// of transient in-memory colour swaps (per-screen overrides) can restore the
    /// user's real colours and keep them from being persisted.</summary>
    event EventHandler? ThemeSaving;
    
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
    public event EventHandler? ThemeSaving;

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
                    // Seed the current mode's palette from the live colours — they
                    // reflect whatever mode was active when saved. (Migrates configs
                    // that predate the saved palettes.)
                    config.CaptureUserPalette(config.IsWidgetThemeLight);
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
            // Let per-screen theme overrides restore the user's real colours first,
            // so transient in-memory swaps are never persisted.
            ThemeSaving?.Invoke(this, EventArgs.Empty);
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
            // Exclude the .fans.json / .rgb.json sidecars bundled with each profile —
            // only the top-level "{name}.json" files are real profiles.
            return Directory.GetFiles(dir, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(n => n != null && !n.EndsWith(".fans") && !n.EndsWith(".rgb"))
                .ToArray()!;
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

            // Bundle the current fan curves and RGB settings into the profile, so a profile
            // captures the whole look — theme, layout, fan curves and lighting (client
            // round 14, item 12).
            CopyFile(ViewModels.FanCurvePersistence.CurveFilePath, ProfileSidecar(profileName, "fans"));
            CopyFile(ViewModels.RgbSettingsPersistence.SettingsFilePath, ProfileSidecar(profileName, "rgb"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"Failed to save profile {profileName}");
        }
    }

    private string ProfileSidecar(string profileName, string kind) =>
        Path.Combine(GetProfilesDirectory(), $"{profileName}.{kind}.json");

    private static void CopyFile(string source, string dest)
    {
        if (File.Exists(source)) File.Copy(source, dest, overwrite: true);
    }

    private static void DeleteFile(string path)
    {
        if (File.Exists(path)) File.Delete(path);
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
                    // Restore the profile's fan curves and RGB settings over the live files
                    // BEFORE the view-models reload them (SettingsWindow reloads after this).
                    CopyFile(ProfileSidecar(profileName, "fans"), ViewModels.FanCurvePersistence.CurveFilePath);
                    CopyFile(ProfileSidecar(profileName, "rgb"), ViewModels.RgbSettingsPersistence.SettingsFilePath);

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
            DeleteFile(Path.Combine(GetProfilesDirectory(), $"{profileName}.json"));
            DeleteFile(ProfileSidecar(profileName, "fans"));
            DeleteFile(ProfileSidecar(profileName, "rgb"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"Failed to delete profile {profileName}");
        }
    }
}
