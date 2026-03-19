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
            TxtCity.Foreground = isLight ? System.Windows.Media.Brushes.Black : (System.Windows.Media.SolidColorBrush)System.Windows.Application.Current.Resources["BrandBlueBrush"];
            
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
        TxtDateHeader.Text = DateTime.Now.ToString("dd MMMM yyyy").ToUpper();
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
        var layers = CalculateIconLayers(iconCode, conditionId);
        
        PathWeatherIconBack.Visibility = layers.Back != null ? Visibility.Visible : Visibility.Collapsed;
        PathWeatherIconBack.Data = layers.Back;
        PathWeatherIconBack.Fill = layers.BackFill;
        
        PathWeatherIconCloud.Visibility = layers.Cloud != null ? Visibility.Visible : Visibility.Collapsed;
        PathWeatherIconCloud.Data = layers.Cloud;
        PathWeatherIconCloud.Fill = layers.CloudFill ?? Brushes.White;
        
        PathWeatherIconFront.Visibility = layers.Front != null ? Visibility.Visible : Visibility.Collapsed;
        PathWeatherIconFront.Data = layers.Front;
        PathWeatherIconFront.Fill = layers.FrontFill ?? Brushes.White;

        if (layers.Cloud != null && layers.Back != null)
        {
            PathWeatherIconBack.HorizontalAlignment = HorizontalAlignment.Right;
            PathWeatherIconBack.VerticalAlignment = VerticalAlignment.Top;
            PathWeatherIconBack.Margin = new Thickness(0, 5, 5, 0);
            PathWeatherIconBack.Width = 70; PathWeatherIconBack.Height = 70;
        }
        else if (layers.Back != null)
        {
            PathWeatherIconBack.HorizontalAlignment = HorizontalAlignment.Center;
            PathWeatherIconBack.VerticalAlignment = VerticalAlignment.Center;
            PathWeatherIconBack.Margin = new Thickness(0);
            PathWeatherIconBack.Width = 100; PathWeatherIconBack.Height = 100;
        }
    }

    private void ForecastTab_Checked(object sender, RoutedEventArgs e)
    {
        UpdateForecastUI();
    }    private void UpdateForecastUI()
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
            var mode = _config.ThemeMode ?? "Auto";
            if (mode == "Auto") {
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
            
            var iconLayers = CalculateIconLayers(item.weather[0].icon, item.weather[0].id);
            
            forecastItems.Add(new ForecastViewItem
            {
                Day = dayLabel,
                DayBrush = subTextBrush,
                BackIcon = iconLayers.Back,
                BackFill = iconLayers.BackFill,
                CloudIcon = iconLayers.Cloud,
                CloudFill = iconLayers.CloudFill ?? (isLight ? Brushes.Black : Brushes.White),
                FrontIcon = iconLayers.Front,
                FrontFill = iconLayers.FrontFill,
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
        double canvasHeight = 120;
        
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
        else { double pad = (maxTemp - minTemp) * 0.3; minTemp -= pad; maxTemp += pad; }

        double xStep = canvasWidth / (temps.Count - 1);
        var points = new List<Point>();
        for (int i = 0; i < temps.Count; i++)
        {
            double x = i * xStep;
            double normalized = (temps[i] - minTemp) / (maxTemp - minTemp);
            double y = canvasHeight - (normalized * canvasHeight);
            points.Add(new Point(x, y));
        }

        // Build SVG Path string for line
        string lineData = $"M {points[0].X},{points[0].Y}";
        for (int i = 0; i < points.Count - 1; i++)
        {
            Point p1 = points[i];
            Point p2 = points[i+1];
            double cpX = (p1.X + p2.X) / 2;
            lineData += $" C {cpX},{p1.Y} {cpX},{p2.Y} {p2.X},{p2.Y}";
        }
        GraphPath.Data = System.Windows.Media.Geometry.Parse(lineData);

        // Build SVG Path string for filled area (closes the loop at the bottom)
        string fillData = lineData + $" L {points[points.Count-1].X},{canvasHeight} L {points[0].X},{canvasHeight} Z";
        GraphFillPath.Data = System.Windows.Media.Geometry.Parse(fillData);

        // Update Tooltip/Point to the ~75% entry (Index 2 in a 4-item list)
        if (points.Count >= 3)
        {
            int targetIdx = (int)Math.Min(points.Count - 2, Math.Ceiling(points.Count * 0.75) - 1);
            if (targetIdx < 0) targetIdx = points.Count - 1;

            var focusPoint = points[targetIdx];
            var focusItem = items[targetIdx];

            GraphPointContainer.Margin = new Thickness(0, 0, canvasWidth - focusPoint.X - 7, canvasHeight - focusPoint.Y - 7);
            
            TxtTooltipTime.Text = focusItem.Day.Contains(":") ? focusItem.Day : DateTime.Now.ToString("HH:mm");
            TxtTooltipTemp.Text = focusItem.Temp;
            GraphTooltip.Opacity = 1;
            GraphTooltip.Margin = new Thickness(0, 0, canvasWidth - focusPoint.X - 25, canvasHeight - focusPoint.Y + 15);
        }
    }

    private (Geometry Back, Brush BackFill, Geometry Cloud, Brush CloudFill, Geometry Front, Brush FrontFill) CalculateIconLayers(string iconCode, int id)
    {
        var sunPath = FindResource("SunIcon") as Geometry;
        var moonPath = FindResource("MoonIcon") as Geometry;
        var cloudPath = FindResource("CloudIcon") as Geometry;
        var smallCloudPath = FindResource("SmallCloudIcon") as Geometry;
        var rainPath = FindResource("RainDrops") as Geometry;
        var snowPath = FindResource("Snowflakes") as Geometry;
        var hazePath = FindResource("HazeLines") as Geometry;
        var fogPath = FindResource("FogLines") as Geometry;
        var boltPath = FindResource("LightningIcon") as Geometry;
        var hailPath = FindResource("HailPellets") as Geometry;
        var windPath = FindResource("WindLines") as Geometry;
        var tornadoPath = FindResource("TornadoFunnel") as Geometry;

        bool isNight = iconCode.EndsWith("n");
        var backIcon = isNight ? moonPath : sunPath;
        var backFill = isNight ? Brushes.LightYellow : new SolidColorBrush(Color.FromRgb(255, 171, 0));

        // Detect theme for neutral layers
        bool isLight = false;
        try {
            var mode = _config.ThemeMode ?? "Auto";
            if (mode == "Auto") {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var val = key?.GetValue("AppsUseLightTheme");
                isLight = (val is int i && i == 1);
            } else {
                isLight = mode == "Light";
            }
        } catch { }

        Geometry back = null;
        Geometry cloud = null;
        Geometry front = null;
        Brush cloudFill = isLight ? new SolidColorBrush(Color.FromRgb(40, 40, 40)) : Brushes.White;
        Brush frontFill = isLight ? new SolidColorBrush(Color.FromRgb(40, 40, 40)) : Brushes.White;

        // Exhaustive mapping based on OWM ID for iPhone accuracy
        if (id == 800) { back = backIcon; } // Clear
        else if (id == 801) { back = backIcon; cloud = smallCloudPath; } // Mostly Clear
        else if (id == 802) { back = backIcon; cloud = cloudPath; } // Partly Cloudy
        else if (id >= 803 && id <= 804) { cloud = cloudPath; if (id == 804) cloudFill = Brushes.DarkGray; } // Cloudy/Overcast
        else if (id >= 200 && id < 300) // Thunderstorms
        {
            cloud = cloudPath; cloudFill = Brushes.SlateGray;
            front = boltPath; frontFill = Brushes.Yellow;
            if (id >= 200 && id <= 202 || id >= 230) { /* With Rain */ front = boltPath; } 
        }
        else if (id >= 300 && id < 400) { back = backIcon; cloud = cloudPath; front = rainPath; frontFill = Brushes.DodgerBlue; } // Drizzle
        else if (id >= 500 && id < 600) // Rain
        {
            cloud = cloudPath; front = rainPath; frontFill = Brushes.DodgerBlue;
            if (id >= 502) cloudFill = Brushes.SlateGray; // Heavy rain
            if (id == 511) { front = snowPath; frontFill = Brushes.LightCyan; } // Freezing rain
        }
        else if (id >= 600 && id < 700) // Snow
        {
            cloud = cloudPath; front = snowPath;
            if (id == 602 || id == 622) cloudFill = Brushes.SlateGray; // Heavy snow
            if (id == 611 || id == 612) { frontFill = Brushes.LightBlue; } // Sleet
        }
        else if (id == 701 || id == 741) { cloud = cloudPath; front = fogPath; frontFill = Brushes.LightGray; } // Fog/Mist
        else if (id == 711 || id == 731 || id == 751 || id == 761) { front = hazePath; frontFill = Brushes.Tan; } // Smoke/Dust/Sand
        else if (id == 721) { back = backIcon; front = hazePath; frontFill = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)); } // Haze
        else if (id == 781) { front = tornadoPath; frontFill = Brushes.Gray; } // Tornado
        else if (id == 771) { front = windPath; frontFill = Brushes.LightSkyBlue; } // Windy/Squall
        else { back = backIcon; } // Fallback

        return (back, backFill, cloud, cloudFill, front, frontFill);
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
        public Geometry BackIcon { get; set; }
        public Brush BackFill { get; set; }
        public Geometry CloudIcon { get; set; }
        public Brush CloudFill { get; set; }
        public Geometry FrontIcon { get; set; }
        public Brush FrontFill { get; set; }
        public string Temp { get; set; } 
        public Brush TempBrush { get; set; }
        public Brush CardBrush { get; set; }
    }
}
}
