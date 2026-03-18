using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PcStatsMonitor.Models;

namespace PcStatsMonitor.Controls
{
    public partial class WeatherScreen : UserControl
    {
    private WeatherConfig _config = new();
    private ThemeConfig _theme = new();
    private DispatcherTimer _refreshTimer = new();
    private static readonly HttpClient _httpClient = new();
    private ForecastResponse _fullForecast = null;

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
            var bgBrush = new System.Windows.Media.SolidColorBrush(isLight ? System.Windows.Media.Color.FromRgb(240, 240, 240) : System.Windows.Media.Color.FromRgb(18, 18, 18));
            var cardBrush = new System.Windows.Media.SolidColorBrush(isLight ? System.Windows.Media.Colors.White : System.Windows.Media.Color.FromRgb(30, 30, 30));
            var textBrush = new System.Windows.Media.SolidColorBrush(isLight ? System.Windows.Media.Color.FromRgb(20, 20, 20) : System.Windows.Media.Colors.White);
            var subTextBrush = new System.Windows.Media.SolidColorBrush(isLight ? System.Windows.Media.Color.FromRgb(80, 80, 80) : System.Windows.Media.Color.FromRgb(136, 136, 136));
            var graphBrush = new System.Windows.Media.SolidColorBrush(isLight ? System.Windows.Media.Color.FromRgb(180, 180, 180) : System.Windows.Media.Color.FromRgb(60, 60, 60));

            this.Foreground = textBrush;
            WeatherBg.Background = bgBrush;
            HeroCard.Background = cardBrush;
            GraphTooltip.Background = cardBrush;

            // Named elements
            TxtCity.Foreground = textBrush;
            TxtTemp.Foreground = textBrush;
            TxtWind.Foreground = textBrush;
            TxtHumidity.Foreground = textBrush;
            TxtPressure.Foreground = textBrush;
            TxtDateHeader.Foreground = subTextBrush;
            TxtCondition.Foreground = subTextBrush;
            TxtWeatherIconLarge.Foreground = textBrush;
            
            // Forecast tab base foreground
            TabToday.Foreground = subTextBrush;
            TabTomorrow.Foreground = subTextBrush;
            TabNextDays.Foreground = subTextBrush;

            // Forecast card background resource update (re-assign to handle frozen states)
            this.Resources["ForecastCardBackground"] = new System.Windows.Media.SolidColorBrush(isLight 
                ? System.Windows.Media.Color.FromRgb(225, 225, 225) 
                : System.Windows.Media.Color.FromArgb(20, 136, 136, 136));

            this.Resources["ForecastTabActiveForeground"] = new System.Windows.Media.SolidColorBrush(isLight 
                ? textBrush.Color 
                : System.Windows.Media.Colors.White);

            // Graph
            GraphPath.Stroke = graphBrush;
            GraphPoint.Fill = textBrush;

            // We can refresh the forecast items since they bind to UserControl.Foreground
            UpdateForecastUI();
        }

        private void ForecastScroller_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer sv)
            {
                if (e.Delta > 0) sv.LineLeft();
                else sv.LineRight();
                e.Handled = true;
            }
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
            TxtDateHeader.Text = "Set API Key & City in Settings";
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
                    TxtDateHeader.Text = "Invalid or Expired API Key";
                }
                else if (currentResp.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    TxtCity.Text = "NOT FOUND";
                    TxtDateHeader.Text = "City Not Found";
                }
                else
                {
                    TxtCity.Text = "ERROR";
                    TxtDateHeader.Text = $"Status: {currentResp.StatusCode}";
                }
                return;
            }

            var currentJson = await currentResp.Content.ReadAsStringAsync();
            var forecastJson = await forecastResp.Content.ReadAsStringAsync();

            var current = JsonSerializer.Deserialize<WeatherResponse>(currentJson);
            _fullForecast = JsonSerializer.Deserialize<ForecastResponse>(forecastJson);

            // ONLY update UI if we successfully got everything
            if (current != null) UpdateCurrentUI(current);
            UpdateForecastUI();

            TxtDateHeader.Text = $"Last updated: {DateTime.Now:t}";
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            // Don't override TxtCity if we already have data, just update the status line
            if (TxtCity.Text == "LOADING...") TxtCity.Text = "ERROR";
            TxtDateHeader.Text = "Check Connection or Settings";
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
        
        TxtHumidity.Text = $"{data.main.humidity}%";
        string speedUnit = (_config.Units == "imperial") ? "mph" : "m/s";
        TxtWind.Text = $"{data.wind.speed} {speedUnit}";
        TxtPressure.Text = $"{data.main.pressure} hPa";

        TxtWeatherIconLarge.Text = GetWeatherEmoji(data.weather[0].icon);
    }

    private void ForecastTab_Checked(object sender, RoutedEventArgs e)
    {
        UpdateForecastUI();
    }

    private void UpdateForecastUI()
    {
        if (_fullForecast == null || _fullForecast.list == null) return;
        var forecastItems = new List<ForecastViewItem>();
        
        string selected = "Today";
        if (TabTomorrow.IsChecked == true) selected = "Tomorrow";
        else if (TabNextDays.IsChecked == true) selected = "NextDays";

        if (selected == "Today")
        {
            // First 6 entries (next 18 hours)
            foreach (var item in _fullForecast.list.Take(6))
            {
                var date = DateTimeOffset.FromUnixTimeSeconds(item.dt).LocalDateTime;
                forecastItems.Add(new ForecastViewItem
                {
                    Day = date.ToString("HH:mm"),
                    Icon = GetWeatherEmoji(item.weather[0].icon),
                    Temp = $"{Math.Round(item.main.temp)}°"
                });
            }
        }
        else if (selected == "Tomorrow")
        {
            // First 6 entries of tomorrow
            var tomorrow = DateTime.Now.Date.AddDays(1);
            var tomorrowItems = _fullForecast.list.Where(x => DateTimeOffset.FromUnixTimeSeconds(x.dt).LocalDateTime.Date == tomorrow).Take(6);
            foreach (var item in tomorrowItems)
            {
                var date = DateTimeOffset.FromUnixTimeSeconds(item.dt).LocalDateTime;
                forecastItems.Add(new ForecastViewItem
                {
                    Day = date.ToString("HH:mm"),
                    Icon = GetWeatherEmoji(item.weather[0].icon),
                    Temp = $"{Math.Round(item.main.temp)}°"
                });
            }
        }
        else // Next 5 Days
        {
            var seenDays = new HashSet<string>();
            foreach (var item in _fullForecast.list)
            {
                var date = DateTimeOffset.FromUnixTimeSeconds(item.dt).LocalDateTime;
                var dayStr = date.ToString("ddd").ToUpper();
                
                if (date.Date > DateTime.Now.Date && !seenDays.Contains(dayStr))
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
        }
        
        LstForecast.ItemsSource = forecastItems;
    }

    private string GetWeatherEmoji(string iconCode)
    {
        // Segoe MDL2 Assets character codes for modern minimalist icons
        return iconCode switch
        {
            "01d"           => "\uE706", // Clear Day (Sun)
            "01n"           => "\uE708", // Clear Night (Moon)
            "02d"           => "\uE783", // Partly Cloudy Day
            "02n"           => "\uE708", // Partly Cloudy Night
            "03d" or "03n"  => "\uE309", // Cloudy
            "04d" or "04n"  => "\uE312", // Overcast
            "09d" or "09n"  => "\uE318", // Showers
            "10d"           => "\uE783", // Rain Day
            "10n"           => "\uE318", // Rain Night
            "11d" or "11n"  => "\uE31D", // Thunderstorm
            "13d" or "13n"  => "\uE31A", // Snow
            "50d" or "50n"  => "\uE31C", // Fog/Mist
            _               => "\uE706"  // Default: Sun
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
}
