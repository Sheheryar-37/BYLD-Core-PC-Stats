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
    private ThemeConfig _theme = new();
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
        _theme = theme;
        
        // Reset timer
        _refreshTimer.Stop();
        if (_config.UpdateIntervalMinutes > 0)
        {
            _refreshTimer.Interval = TimeSpan.FromMinutes(_config.UpdateIntervalMinutes);
            _refreshTimer.Start();
        }

        ApplyTheme();
        await UpdateWeatherData();
    }

    private void ApplyTheme()
    {
        string mode = _config.WeatherTheme ?? "Dark";
        if (mode == "System")
        {
            mode = GetWindowsTheme();
        }

        bool isLight = mode == "Light";

        // Colors
        var bgBrush = new System.Windows.Media.SolidColorBrush(isLight ? System.Windows.Media.Color.FromRgb(245, 245, 245) : System.Windows.Media.Color.FromRgb(18, 18, 18));
        var cardBrush = new System.Windows.Media.SolidColorBrush(isLight ? System.Windows.Media.Colors.White : System.Windows.Media.Color.FromRgb(30, 30, 30));
        var textBrush = new System.Windows.Media.SolidColorBrush(isLight ? System.Windows.Media.Color.FromRgb(18, 18, 18) : System.Windows.Media.Colors.White);
        var subTextBrush = new System.Windows.Media.SolidColorBrush(isLight ? System.Windows.Media.Color.FromRgb(102, 102, 102) : System.Windows.Media.Color.FromRgb(136, 136, 136));
        var graphBrush = new System.Windows.Media.SolidColorBrush(isLight ? System.Windows.Media.Colors.Black : System.Windows.Media.Colors.White);

        this.Foreground = textBrush;
        WeatherBg.Background = bgBrush;
        HeroCard.Background = cardBrush;
        GraphTooltip.Background = cardBrush;

        // Named elements also benefit from explicit setting for clarity, 
        // though many will now inherit from 'this.Foreground'
        TxtCity.Foreground = textBrush;
        TxtTemp.Foreground = textBrush;
        TxtWind.Foreground = textBrush;
        TxtHumidity.Foreground = textBrush;
        TxtPressure.Foreground = textBrush;
        
        // Titles/Dates
        TxtDateHeader.Foreground = subTextBrush;
        TxtCondition.Foreground = subTextBrush;
        
        // Graph
        GraphPath.Stroke = graphBrush;
        GraphPoint.Fill = graphBrush;

        // We can't easily reach LstForecast card backgrounds via code-behind without visual tree walking, 
        // so we'll handle that via a DynamicResource or by just setting a property on the items if we had a ViewModel.
        // For now, let's refresh the ItemsSource which will trigger the template to re-bind if we use a value converter 
        // or just accept that cards look okay as they are (dark-ish).
        // Actually, let's just make the forecast cards semi-transparent in XAML so they work on both.
    }

    private string GetWindowsTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var val = key?.GetValue("AppsUseLightTheme");
            return (val is int i && i == 1) ? "Light" : "Dark";
        }
        catch { return "Dark"; }
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
        TxtDateHeader.Text = DateTime.Now.ToString("dd MMMM yyyy").ToUpper();
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

        TxtWeatherIconLarge.Text = GetWeatherEmoji(data.weather[0].icon);
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
