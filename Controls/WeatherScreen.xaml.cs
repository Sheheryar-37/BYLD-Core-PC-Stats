using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PcStatsMonitor.Models;

namespace PcStatsMonitor.Controls;

public partial class WeatherScreen : UserControl
{
    private WeatherConfig _config = new();
    private DispatcherTimer _refreshTimer = new();
    private static readonly HttpClient _httpClient = new();

    public WeatherScreen()
    {
        InitializeComponent();
        
        _refreshTimer.Tick += async (s, e) => await UpdateWeatherData();
    }

    public async void ApplyConfig(WeatherConfig config, ThemeConfig theme)
    {
        _config = config;
        
        // Reset timer
        _refreshTimer.Stop();
        if (_config.UpdateIntervalMinutes > 0)
        {
            _refreshTimer.Interval = TimeSpan.FromMinutes(_config.UpdateIntervalMinutes);
            _refreshTimer.Start();
        }

        await UpdateWeatherData();
    }

    private async Task UpdateWeatherData()
    {
        if (string.IsNullOrEmpty(_config.ApiKey) || string.IsNullOrEmpty(_config.City))
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            TxtCity.Text = "MISSING CONFIG";
            TxtDate.Text = "Set API Key & City in Settings";
            return;
        }

        try
        {
            // Subtle indicator at the top
            LoadingOverlay.Visibility = Visibility.Visible;

            string units = _config.Units ?? "metric";
            string cityEncoded = Uri.EscapeDataString(_config.City);
            string currentUrl = $"https://api.openweathermap.org/data/2.5/weather?q={cityEncoded}&appid={_config.ApiKey}&units={units}";
            string forecastUrl = $"https://api.openweathermap.org/data/2.5/forecast?q={cityEncoded}&appid={_config.ApiKey}&units={units}";

            var currentResp = await _httpClient.GetAsync(currentUrl);
            var forecastResp = await _httpClient.GetAsync(forecastUrl);

            if (!currentResp.IsSuccessStatusCode)
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
                if (currentResp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    TxtCity.Text = "API ERROR";
                    TxtDate.Text = "Invalid or Expired API Key";
                }
                else if (currentResp.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    TxtCity.Text = "NOT FOUND";
                    TxtDate.Text = "City Not Found";
                }
                else
                {
                    TxtCity.Text = "ERROR";
                    TxtDate.Text = $"Status: {currentResp.StatusCode}";
                }
                return;
            }

            var currentJson = await currentResp.Content.ReadAsStringAsync();
            var forecastJson = await forecastResp.Content.ReadAsStringAsync();

            var current = JsonSerializer.Deserialize<WeatherResponse>(currentJson);
            var forecast = JsonSerializer.Deserialize<ForecastResponse>(forecastJson);

            // ONLY update UI if we successfully got everything
            if (current != null) UpdateCurrentUI(current);
            if (forecast != null) UpdateForecastUI(forecast);

            TxtDate.Text = $"Last updated: {DateTime.Now:t}";
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            // Don't override TxtCity if we already have data, just update the status line
            if (TxtCity.Text == "LOADING...") TxtCity.Text = "ERROR";
            TxtDate.Text = "Check Connection or Settings";
            LoadingOverlay.Visibility = Visibility.Collapsed;
            System.Diagnostics.Debug.WriteLine($"Weather Error: {ex.Message}");
        }
    }

    private void UpdateCurrentUI(WeatherResponse data)
    {
        TxtCity.Text = data.name.ToUpper();
        string unitSymbol = (_config.Units == "imperial") ? "°F" : "°C";
        TxtTemp.Text = $"{Math.Round(data.main.temp)}{unitSymbol}";
        TxtCondition.Text = data.weather[0].description.ToUpper();
        TxtHigh.Text = $"{Math.Round(data.main.temp_max)}°";
        TxtLow.Text = $"{Math.Round(data.main.temp_min)}°";
        
        TxtHumidity.Text = $"{data.main.humidity}%";
        string speedUnit = (_config.Units == "imperial") ? "mph" : "m/s";
        TxtWind.Text = $"{data.wind.speed} {speedUnit}";
        TxtPressure.Text = $"{data.main.pressure} hPa";
        TxtFeelsLike.Text = $"{Math.Round(data.main.feels_like)}°";

        TxtWeatherIcon.Text = GetWeatherEmoji(data.weather[0].icon);
    }

    private void UpdateForecastUI(ForecastResponse data)
    {
        var forecastItems = new List<ForecastViewItem>();
        
        // Pick one entry per day for the next 5 days (usually entries at 12:00:00)
        var seenDays = new HashSet<string>();
        foreach (var item in data.list)
        {
            var date = DateTimeOffset.FromUnixTimeSeconds(item.dt).LocalDateTime;
            var dayStr = date.ToString("ddd").ToUpper();
            
            if (date.Date > DateTime.Now.Date && !seenDays.Contains(dayStr) && forecastItems.Count < 5)
            {
                forecastItems.Add(new ForecastViewItem
                {
                    Day = dayStr,
                    Icon = GetWeatherEmoji(item.weather[0].icon),
                    Temp = $"{Math.Round(item.main.temp)}°"
                });
                seenDays.Add(dayStr);
            }
        }
        
        LstForecast.ItemsSource = forecastItems;
    }

    private string GetWeatherEmoji(string iconCode)
    {
        return iconCode switch
        {
            "01d" => "☀️", "01n" => "🌙",
            "02d" => "⛅", "02n" => "☁️",
            "03d" or "03n" => "☁️",
            "04d" or "04n" => "☁️",
            "09d" or "09n" => "🌧️",
            "10d" => "🌦️", "10n" => "🌧️",
            "11d" or "11n" => "⚡",
            "13d" or "13n" => "❄️",
            "50d" or "50n" => "🌫️",
            _ => "☀️"
        };
    }

    // Models for JSON
    public class WeatherResponse { 
        public string name { get; set; }
        public List<WeatherInfo> weather { get; set; }
        public MainInfo main { get; set; }
        public WindInfo wind { get; set; }
    }
    public class ForecastResponse { public List<ForecastItem> list { get; set; } }
    public class ForecastItem { 
        public long dt { get; set; }
        public MainInfo main { get; set; }
        public List<WeatherInfo> weather { get; set; }
    }
    public class WeatherInfo { public string main { get; set; } public string description { get; set; } public string icon { get; set; } }
    public class MainInfo { public double temp { get; set; } public double temp_min { get; set; } public double temp_max { get; set; } public double feels_like { get; set; } public int humidity { get; set; } public int pressure { get; set; } }
    public class WindInfo { public double speed { get; set; } }
    
    public class ForecastViewItem { public string Day { get; set; } public string Icon { get; set; } public string Temp { get; set; } }
}
