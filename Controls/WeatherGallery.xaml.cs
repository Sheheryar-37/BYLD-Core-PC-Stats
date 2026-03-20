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
            _currentIndex = 0;
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

        private void ModeToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (ImgGalleryIcon == null) return;
            UpdateGalleryIcon();
        }

        private void UpdateGalleryIcon()
        {
            if (_conditions == null || _conditions.Count == 0 || ImgGalleryIcon == null) return;
            var current = _conditions[_currentIndex];
            
            TxtIndex.Text = $"{_currentIndex + 1} of {_conditions.Count}";
            TxtName.Text = current.Name;
            TxtId.Text = $"ID: {current.Id} | Icon: #{current.IconNumber}";
            TxtDescription.Text = current.Description;

            bool isOutline = ChkOutlineMode.IsChecked == true;
            string searchPrefix = isOutline ? $"Out_{current.IconNumber}_" : $"Icon_{current.IconNumber}_";
            
            // Try to find the exact resource using the new IconNumber mapping
            DrawingGroup foundDrawing = null;
            
            // We use a broader search because keys vary slightly between libraries (Icon_N_Name vs Out_N_Name)
            // The most reliable way in WPF is to iterate merged dictionaries or use a naming convention.
            // Since we know our keys start with Out_N_ or Icon_N_, let's try some common patterns.
            
            string fullKey = isOutline ? $"Out_{current.IconNumber}_{current.Name.Replace(" ", "_")}" : $"Icon_{current.IconNumber}_{current.ResourceKey}";
            
            foundDrawing = Application.Current.TryFindResource(fullKey) as DrawingGroup;

            if (foundDrawing == null)
            {
                // Fallback: try searching by just the number prefix if name mapping is slightly off
                // This is a bit expensive but okay for a gallery tool
                foreach (ResourceDictionary dict in Application.Current.Resources.MergedDictionaries)
                {
                    foreach (var key in dict.Keys)
                    {
                        if (key is string s && s.StartsWith(searchPrefix))
                        {
                            foundDrawing = dict[key] as DrawingGroup;
                            break;
                        }
                    }
                    if (foundDrawing != null) break;
                }
            }

            if (foundDrawing != null)
            {
                ImgGalleryIcon.Source = new DrawingImage(foundDrawing);
            }
            else
            {
                // Absolute fallback
                if (Application.Current.TryFindResource(isOutline ? "Out_2_Cloud" : "Icon_1_Cloud_Pure") is DrawingGroup fallback)
                    ImgGalleryIcon.Source = new DrawingImage(fallback);
            }
        }
    }
}
