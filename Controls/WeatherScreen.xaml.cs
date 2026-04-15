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
using System.Windows.Media.Imaging;
using System.Linq;

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
        string timeFmt = (_config.TimeFormat == "24h") ? "HH:mm" : "h:mm tt";
        TxtDateHeader.Text = $"LAST UPDATED: {DateTime.Now.ToString(timeFmt)}";
        string unitSymbol = (_config.Units == "imperial") ? "°F" : "°C";
        TxtTemp.Text = $"{Math.Round(data.main.temp)}{unitSymbol}";
        TxtCondition.Text = data.weather[0].description.ToUpper();
        
        TxtHumidity.Text = $"{data.main.humidity}%";
        string speedUnit = (_config.Units == "imperial") ? "mph" : "m/s";
        TxtWind.Text = $"{data.wind.speed} {speedUnit}";
        TxtPressure.Text = $"{data.main.pressure} hPa";

        UpdateLargeWeatherIcon(data.weather[0].icon);
    }

    private ImageSource GetImageFromOWMIcon(string iconCode)
    {
        string[] allFiles = {
            "01d Clear Sky.png", "01n Clear Sky.png", "02d Few Clouds.png", "02n Few Clouds.png",
            "03d Scattered Clouds.png", "03n Scattered Clouds.png", "04d Broken Clouds.png", "04n Broken Clouds.png",
            "50d Mist.png", "50n Mist.png", "09d Shower Rain.png", "09n Shower Rain.png",
            "10d Rain.png", "10n Rain.png", "11d Thunderstorm.png", "11n Thunderstorm.png",
            "13d Snow.png", "13n Snow.png"
        };
        string file = allFiles.FirstOrDefault(f => f.StartsWith(iconCode)) ?? "01d Clear Sky.png";
        try {
            return new BitmapImage(new Uri($"pack://application:,,,/Assets/Weather Icons/{file}", UriKind.Absolute));
        } catch { return null; }
    }

    private void UpdateLargeWeatherIcon(string iconCode)
    {
        ImgMainWeather.Source = GetImageFromOWMIcon(iconCode);
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
                                    .Select(g => g.OrderBy(x => Math.Abs(DateTimeOffset.FromUnixTimeSeconds(x.dt).LocalDateTime.Hour - 14)).First())
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
            string timeFmt = (_config.TimeFormat == "24h") ? "HH:mm" : "h:mm tt";
            string dayLabel = (selected == "NextDays") ? date.ToString("ddd").ToUpper() : date.ToString(timeFmt);
            ImageSource imgSrc = GetImageFromOWMIcon(item.weather[0].icon);
            
            string tempHigh = $"{Math.Round(item.main.temp)}°";
            string tempLow = "";

            if (selected == "NextDays")
            {
                var dailySlices = _fullForecast.list.Where(x => DateTimeOffset.FromUnixTimeSeconds(x.dt).LocalDateTime.Date == date.Date).ToList();
                if (dailySlices.Any())
                {
                    double min = dailySlices.Min(x => x.main.temp);
                    double max = dailySlices.Max(x => x.main.temp);
                    tempHigh = $"{Math.Round(max)}°";
                    tempLow = $"{Math.Round(min)}°";
                }
            }

            forecastItems.Add(new ForecastViewItem
            {
                Day = dayLabel,
                DayBrush = subTextBrush,
                IconImage = imgSrc,
                Temp = tempHigh,
                TempBrush = textBrush,
                TempLow = tempLow,
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
        
        // Internal padding (20% top, 10% bottom) so trends never hit zero boundaries
        double innerHeight = canvasHeight * 0.70; 
        double topPadding = canvasHeight * 0.20; 

        for (int i = 0; i < temps.Count; i++)
        {
            double x = i * xStep;
            double normalized = (maxTemp == minTemp) ? 0.5 : (temps[i] - minTemp) / (maxTemp - minTemp);
            
            // Map temperature into the safe 'middle' vertical 70% of the canvas
            double y = (canvasHeight - topPadding) - (normalized * innerHeight);
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
            int targetIdx = temps.IndexOf(temps.Max()); // Focus on the highest point
            var focusPoint = points[targetIdx];
            var focusItem = items[targetIdx];

            GraphPointContainer.Margin = new Thickness(focusPoint.X - 8, focusPoint.Y - 8, 0, 0);
            
            TxtTooltipTime.Text = focusItem.Day;
            TxtTooltipTemp.Text = focusItem.Temp;
            GraphTooltip.Opacity = 1;
            
            // Size resolution for clamping constraints
            TooltipBox.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double tWidth = Math.Max(50, TooltipBox.DesiredSize.Width);
            
            // Center naturally, but clamp gracefully against canvas constraints
            double idealLeft = focusPoint.X - (tWidth / 2);
            double clampedLeft = Math.Max(0, Math.Min(canvasWidth - tWidth, idealLeft));
            
            // Fixed height estimate (~48px total). Subtracted manually to align arrow correctly.
            GraphTooltip.Margin = new Thickness(clampedLeft, focusPoint.Y - 56, 0, 0);
            
            // Dynamically slide the TooltipArrow to accurately hit focusPoint.X regardless of the box clamping
            double arrowOffset = focusPoint.X - clampedLeft - 6; // Center offset for the 12px wide arrow
            arrowOffset = Math.Max(8, Math.Min(tWidth - 20, arrowOffset)); // Padding so the corner avoids clipping the arrow tail bounds
            TooltipArrow.Margin = new Thickness(arrowOffset, 0, 0, 0);
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
    public class ForecastViewItem
    {
        public string Day { get; set; }
        public Brush DayBrush { get; set; }
        public ImageSource IconImage { get; set; }
        public string Temp { get; set; }
        public Brush TempBrush { get; set; }
        public string TempLow { get; set; }
        public Brush CardBrush { get; set; }
    }

    }
}
