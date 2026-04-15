using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace PcStatsMonitor.Controls
{
    public partial class WeatherGallery : UserControl
    {
        private readonly string[] _iconFiles = new string[]
        {
            "01d Clear Sky.png", "01n Clear Sky.png", "02d Few Clouds.png", "02n Few Clouds.png",
            "03d Scattered Clouds.png", "03n Scattered Clouds.png", "04d Broken Clouds.png", "04n Broken Clouds.png",
            "50d Mist.png", "50n Mist.png", "09d Shower Rain.png", "09n Shower Rain.png",
            "10d Rain.png", "10n Rain.png", "11d Thunderstorm.png", "11n Thunderstorm.png",
            "13d Snow.png", "13n Snow.png"
        };
        private int _currentIndex = 0;

        public WeatherGallery()
        {
            InitializeComponent();
            _currentIndex = 0;
            UpdateGalleryIcon();
        }

        private void BtnPrev_Click(object sender, RoutedEventArgs e)
        {
            _currentIndex--;
            if (_currentIndex < 0) _currentIndex = _iconFiles.Length - 1;
            UpdateGalleryIcon();
        }

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            _currentIndex++;
            if (_currentIndex >= _iconFiles.Length) _currentIndex = 0;
            UpdateGalleryIcon();
        }

        private void BtnNightToggle_Click(object sender, RoutedEventArgs e)
        {
            // Night mode removed as per client request
        }

        private void ModeToggle_Changed(object sender, RoutedEventArgs e)
        {
            // Outline mode removed
        }

        private void UpdateGalleryIcon()
        {
            if (_iconFiles == null || _iconFiles.Length == 0 || ImgGalleryIcon == null) return;
            string fileName = _iconFiles[_currentIndex];
            string bareName = fileName.Replace(".png", "");
            string iconPrefix = bareName.Substring(0, 3); // e.g. "01d"
            string description = bareName.Substring(4); // e.g. "Clear Sky"
            
            TxtIndex.Text = $"{_currentIndex + 1} of {_iconFiles.Length}";
            TxtName.Text = description;
            TxtId.Text = $"OWM Icon: {iconPrefix}";
            TxtDescription.Text = $"Direct Image Binding: {fileName}";

            try
            {
                var uri = new Uri($"pack://application:,,,/Assets/Weather Icons/{fileName}", UriKind.Absolute);
                ImgGalleryIcon.Source = new BitmapImage(uri);
            }
            catch (Exception ex)
            {
                TxtDescription.Text = $"Error loading {fileName}: {ex.Message}";
            }
        }
    }
}
