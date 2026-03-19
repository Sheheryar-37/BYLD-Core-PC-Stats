using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PcStatsMonitor.Models;

namespace PcStatsMonitor.Controls
{
    public partial class WeatherGallery : UserControl
    {
        private List<WeatherCondition> _conditions;
        private int _currentIndex = 0;
        private bool _isNight = false;

        public WeatherGallery()
        {
            InitializeComponent();
            _conditions = WeatherCondition.GetAllConditions();
            UpdateGalleryIcon();
        }

        private void BtnPrev_Click(object sender, RoutedEventArgs e)
        {
            _currentIndex--;
            if (_currentIndex < 0) _currentIndex = _conditions.Count - 1;
            UpdateGalleryIcon();
        }

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            _currentIndex++;
            if (_currentIndex >= _conditions.Count) _currentIndex = 0;
            UpdateGalleryIcon();
        }

        private void BtnNightToggle_Click(object sender, RoutedEventArgs e)
        {
            _isNight = BtnNightToggle.IsChecked == true;
            UpdateGalleryIcon();
        }

        private void UpdateGalleryIcon()
        {
            if (_conditions == null || _conditions.Count == 0) return;
            var current = _conditions[_currentIndex];
            
            TxtIndex.Text = $"{_currentIndex + 1} of {_conditions.Count}";
            TxtName.Text = current.Name;
            TxtId.Text = $"ID: {current.Id}";
            TxtDescription.Text = current.Description;

            string iconCode = _isNight ? "01n" : "01d";
            var layers = CalculateGalleryLayers(iconCode, current.Id);
            
            PathBack.Visibility = layers.Back != null ? Visibility.Visible : Visibility.Collapsed;
            PathBack.Data = layers.Back;
            PathBack.Fill = layers.BackFill;
            
            PathCloud.Visibility = layers.Cloud != null ? Visibility.Visible : Visibility.Collapsed;
            PathCloud.Data = layers.Cloud;
            PathCloud.Fill = layers.CloudFill ?? Brushes.White;
            
            PathFront.Visibility = layers.Front != null ? Visibility.Visible : Visibility.Collapsed;
            PathFront.Data = layers.Front;
            PathFront.Fill = layers.FrontFill ?? Brushes.White;

            // Positioning logic mirroring WeatherScreen
            if (layers.Cloud != null && layers.Back != null)
            {
                PathBack.HorizontalAlignment = HorizontalAlignment.Right;
                PathBack.VerticalAlignment = VerticalAlignment.Top;
                PathBack.Margin = new Thickness(0, 10, 10, 0);
                PathBack.Width = 120; PathBack.Height = 120; // Gallery is larger
            }
            else if (layers.Back != null)
            {
                PathBack.HorizontalAlignment = HorizontalAlignment.Center;
                PathBack.VerticalAlignment = VerticalAlignment.Center;
                PathBack.Margin = new Thickness(0);
                PathBack.Width = 180; PathBack.Height = 180;
            }
        }

        private (Geometry Back, Brush BackFill, Geometry Cloud, Brush CloudFill, Geometry Front, Brush FrontFill) CalculateGalleryLayers(string iconCode, int id)
        {
            var sunPath = Application.Current.FindResource("SunIcon") as Geometry;
            var moonPath = Application.Current.FindResource("MoonIcon") as Geometry;
            var cloudPath = Application.Current.FindResource("CloudIcon") as Geometry;
            var smallCloudPath = Application.Current.FindResource("SmallCloudIcon") as Geometry;
            var rainPath = Application.Current.FindResource("RainDrops") as Geometry;
            var snowPath = Application.Current.FindResource("Snowflakes") as Geometry;
            var hazePath = Application.Current.FindResource("HazeLines") as Geometry;
            var fogPath = Application.Current.FindResource("FogLines") as Geometry;
            var boltPath = Application.Current.FindResource("LightningIcon") as Geometry;
            var hailPath = Application.Current.FindResource("HailPellets") as Geometry;
            var windPath = Application.Current.FindResource("WindLines") as Geometry;
            var tornadoPath = Application.Current.FindResource("TornadoFunnel") as Geometry;

            bool isNight = iconCode.EndsWith("n");
            var backIcon = isNight ? moonPath : sunPath;
            var backFill = isNight ? Brushes.LightYellow : new SolidColorBrush(Color.FromRgb(255, 171, 0));

            Geometry back = null, cloud = null, front = null;
            Brush cloudFill = Brushes.White, frontFill = Brushes.White;

            if (id == 800) { back = backIcon; }
            else if (id == 801) { back = backIcon; cloud = smallCloudPath; }
            else if (id == 802) { back = backIcon; cloud = cloudPath; }
            else if (id >= 803 && id <= 804) { cloud = cloudPath; if (id == 804) cloudFill = Brushes.DarkGray; }
            else if (id >= 200 && id < 300) { cloud = cloudPath; cloudFill = Brushes.SlateGray; front = boltPath; frontFill = Brushes.Yellow; }
            else if (id >= 300 && id < 400) { back = backIcon; cloud = cloudPath; front = rainPath; frontFill = Brushes.DodgerBlue; }
            else if (id >= 500 && id < 600) { cloud = cloudPath; front = rainPath; frontFill = Brushes.DodgerBlue; if (id >= 502) cloudFill = Brushes.SlateGray; if (id == 511) { front = snowPath; frontFill = Brushes.LightCyan; } }
            else if (id >= 600 && id < 700) { cloud = cloudPath; front = snowPath; frontFill = Brushes.White; if (id == 602 || id == 622) cloudFill = Brushes.SlateGray; if (id == 611 || id == 612) frontFill = Brushes.LightBlue; }
            else if (id == 701 || id == 741) { cloud = cloudPath; front = fogPath; frontFill = Brushes.LightGray; }
            else if (id == 721) { back = backIcon; front = hazePath; frontFill = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)); }
            else if (id == 781) { front = tornadoPath; frontFill = Brushes.Gray; }
            else if (id == 771) { front = windPath; frontFill = Brushes.LightSkyBlue; }
            else { back = backIcon; }

            return (back, backFill, cloud, cloudFill, front, frontFill);
        }
    }
}
