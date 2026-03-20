using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PcStatsMonitor.Models;
using System.Windows.Media;

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
            var subTextBrush = new System.Windows.Media.SolidColorBrush(isLight ? System.Windows.Media.Color.FromRgb(40, 40, 40) : System.Windows.Media.Color.FromRgb(136, 136, 136));
            var graphBrush = new System.Windows.Media.SolidColorBrush(isLight ? System.Windows.Media.Color.FromRgb(120, 120, 120) : System.Windows.Media.Color.FromRgb(68, 68, 68));

            this.Foreground = textBrush;
            WeatherBg.Background = bgBrush;
            HeroCard.Background = cardBrush;
            GraphTooltip.Background = System.Windows.Media.Brushes.White; // Always white per inspiration bubble

            // Named elements
            TxtTemp.Foreground = textBrush;
            TxtWind.Foreground = textBrush;
            TxtHumidity.Foreground = textBrush;
            TxtPressure.Foreground = textBrush;
            TxtDateHeader.Foreground = subTextBrush;
            TxtCondition.Foreground = textBrush;
            TxtCity.Foreground = isLight ? System.Windows.Media.Brushes.Black : (System.Windows.Media.SolidColorBrush)System.Windows.Application.Current.Resources["BrandBlue"];
            
            // Forecast tab base foreground
            TabToday.Foreground = isLight ? System.Windows.Media.Brushes.Black : subTextBrush;
            TabTomorrow.Foreground = isLight ? System.Windows.Media.Brushes.Black : subTextBrush;
            TabNextDays.Foreground = isLight ? System.Windows.Media.Brushes.Black : subTextBrush;

            // Graph
            GraphPath.Stroke = graphBrush;
            
            // We can refresh the forecast items since they bind to UserControl.Foreground
            UpdateForecastUI();
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
            TxtCondition.Text = "MISSING CONFIG";
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
                    TxtCondition.Text = "API ERROR";
                    TxtDateHeader.Text = "Invalid or Expired API Key";
                }
                else if (currentResp.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    TxtCondition.Text = "NOT FOUND";
                    TxtDateHeader.Text = "City Not Found";
                }
                else
                {
                    TxtCondition.Text = "ERROR";
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
            // Don't override TxtCondition if we already have data, just update the status line
            if (TxtCondition.Text == "LOADING...") TxtCondition.Text = "ERROR";
            TxtDateHeader.Text = "Check Connection or Settings";
            LoadingOverlay.Visibility = Visibility.Collapsed;
            System.Diagnostics.Debug.WriteLine($"Weather Error: {ex.Message}");
        }
    }

    private void UpdateCurrentUI(WeatherResponse data)
    {
        TxtCity.Text = (data.name ?? _config.City ?? "UNKNOWN").ToUpper();
        TxtDateHeader.Text = $"LAST UPDATED: {DateTime.Now:t}";
        string unitSymbol = (_config.Units == "imperial") ? "°F" : "°C";
        TxtTemp.Text = $"{Math.Round(data.main.temp)}{unitSymbol}";
        TxtCondition.Text = data.weather[0].description.ToUpper();
        
        TxtHumidity.Text = $"{data.main.humidity}%";
        string speedUnit = (_config.Units == "imperial") ? "mph" : "m/s";
        TxtWind.Text = $"{data.wind.speed} {speedUnit}";
        TxtPressure.Text = $"{data.main.pressure} hPa";

        UpdateLargeWeatherIcon(data.weather[0].icon, data.weather[0].id);
    }

    private void UpdateLargeWeatherIcon(string iconCode, int conditionId)
    {
        bool isNight = iconCode.EndsWith("n");
        string resKey = GetIconResourceKey(conditionId, isNight);
        
        // Main hero card always uses Colored icons for premium look
        if (Application.Current.TryFindResource("Icon_" + resKey) is DrawingGroup dg)
        {
            ImgMainWeather.Source = new DrawingImage(dg);
        }
    }

    private string GetIconResourceKey(int id, bool isNight)
    {
        if (id == 800) return isNight ? "Moon_Pure" : "Sun_Pure";
        if (id == 801) return isNight ? "Moon_Cloud" : "Sun_Cloud";
        if (id == 802) return "Cloud_Double";
        if (id >= 803 && id <= 804) return "Cloud_Pure";
        if (id >= 200 && id < 300) return "Cloud_Lightning";
        if (id >= 500 && id < 600) return "Cloud_Rain_Heavy";
        if (id >= 600 && id < 700) return "Cloud_Snow";
        if (id == 771) return "Wind_Pure";
        return "Cloud_Pure";
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

        var todayList = (selected == "Today") 
            ? _fullForecast.list.Take(8).Where((x, i) => i % 2 == 0).ToList() // Take 4 items at 6h intervals
            : (selected == "Tomorrow")
                ? _fullForecast.list.Where(x => DateTimeOffset.FromUnixTimeSeconds(x.dt).LocalDateTime.Date == DateTime.Now.Date.AddDays(1)).Where((x, i) => i % 2 == 0).ToList()
                : _fullForecast.list.GroupBy(x => DateTimeOffset.FromUnixTimeSeconds(x.dt).LocalDateTime.Date)
                                    .Where(g => g.Key > DateTime.Now.Date)
                                    .Select(g => g.First())
                                    .Take(4).ToList();

        // Theme colors for cards
        bool isLight = false;
        try {
            var mode = _config.WeatherTheme ?? "Auto";
            if (mode == "Auto" || mode == "System") {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var val = key?.GetValue("AppsUseLightTheme");
                isLight = (val is int i && i == 1);
            } else {
                isLight = mode == "Light";
            }
        } catch { }

        var cardBrush = isLight ? new SolidColorBrush(Color.FromRgb(225, 225, 225)) : new SolidColorBrush(Color.FromArgb(26, 255, 255, 255));
        var textBrush = isLight ? Brushes.Black : Brushes.White;
        var subTextBrush = isLight ? new SolidColorBrush(Color.FromRgb(60, 60, 60)) : new SolidColorBrush(Color.FromArgb(136, 255, 255, 255));

        foreach (var item in todayList)
        {
            var date = DateTimeOffset.FromUnixTimeSeconds(item.dt).LocalDateTime;
            string dayLabel = (selected == "NextDays") ? date.ToString("ddd").ToUpper() : date.ToString("HH:mm");
            
            bool isNightItem = item.weather[0].icon.EndsWith("n");
            string resKey = GetIconResourceKey(item.weather[0].id, isNightItem);
            
            // Forecast small cards always use Outlined icons for minimalist look
            DrawingImage drawing = null;
            if (Application.Current.TryFindResource("Out_Icon_" + resKey) is DrawingGroup dg)
            {
                drawing = new DrawingImage(dg);
            }
            
            forecastItems.Add(new ForecastViewItem
            {
                Day = dayLabel,
                DayBrush = subTextBrush,
                IconDrawing = drawing,
                Temp = $"{Math.Round(item.main.temp)}°",
                TempBrush = textBrush,
                CardBrush = cardBrush
            });
        }
        
        LstForecast.ItemsSource = forecastItems;
        DrawWeatherGraph(forecastItems);
    }

    private void DrawWeatherGraph(List<ForecastViewItem> items)
    {
        if (items == null || items.Count < 2) return;

        double canvasWidth = 480; 
        double canvasHeight = 140; // Increased height
        
        double minTemp = double.MaxValue;
        double maxTemp = double.MinValue;
        List<double> temps = new();

        foreach (var item in items)
        {
            if (double.TryParse(item.Temp.Replace("°", ""), out double t))
            {
                temps.Add(t);
                if (t < minTemp) minTemp = t;
                if (t > maxTemp) maxTemp = t;
            }
        }

        if (temps.Count < 2) return;
        if (minTemp == maxTemp) { minTemp -= 2; maxTemp += 2; }
        else { double pad = (maxTemp - minTemp) * 0.4; minTemp -= pad; maxTemp += pad; }

        double xStep = canvasWidth / (temps.Count - 1);
        var points = new List<Point>();
        for (int i = 0; i < temps.Count; i++)
        {
            double x = i * xStep;
            double normalized = (temps[i] - minTemp) / (maxTemp - minTemp);
            double y = canvasHeight - (normalized * canvasHeight);
            points.Add(new Point(x, y));
        }

        // Build SVG Path string with smooth Bezier curves
        string lineData = $"M {points[0].X},{points[0].Y}";
        for (int i = 0; i < points.Count - 1; i++)
        {
            Point p1 = points[i];
            Point p2 = points[i+1];
            double cpX = (p1.X + p2.X) / 2;
            lineData += $" C {cpX},{p1.Y} {cpX},{p2.Y} {p2.X},{p2.Y}";
        }
        GraphPath.Data = System.Windows.Media.Geometry.Parse(lineData);

        // Fill area under graph with gradient
        string fillData = lineData + $" L {points[points.Count-1].X},{canvasHeight} L {points[0].X},{canvasHeight} Z";
        GraphFillPath.Data = System.Windows.Media.Geometry.Parse(fillData);

        // Focus Point & Tooltip Positioning (Smart offset for bubble tip)
        if (points.Count >= 3)
        {
            int targetIdx = (int)Math.Min(points.Count - 2, 2); // Focus on the 3rd item usually
            var focusPoint = points[targetIdx];
            var focusItem = items[targetIdx];

            GraphPointContainer.Margin = new Thickness(0, 0, canvasWidth - focusPoint.X - 8, canvasHeight - focusPoint.Y - 8);
            
            TxtTooltipTime.Text = focusItem.Day;
            TxtTooltipTemp.Text = focusItem.Temp;
            GraphTooltip.Opacity = 1;
            GraphTooltip.Margin = new Thickness(0, 0, canvasWidth - focusPoint.X - 25, canvasHeight - focusPoint.Y + 12);
        }
    }

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
    public class WeatherInfo { public int id { get; set; } public string main { get; set; } public string description { get; set; } public string icon { get; set; } }
    public class MainInfo { public double temp { get; set; } public double temp_min { get; set; } public double temp_max { get; set; } public double feels_like { get; set; } public int humidity { get; set; } public int pressure { get; set; } }
    public class WindInfo { public double speed { get; set; } }
    public class ForecastViewItem { 
        public string Day { get; set; } 
        public Brush DayBrush { get; set; }
        public DrawingImage IconDrawing { get; set; }
        public string Temp { get; set; } 
        public Brush TempBrush { get; set; }
        public Brush CardBrush { get; set; }
    }

    }
}
