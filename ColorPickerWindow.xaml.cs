using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PcStatsMonitor;

public partial class ColorPickerWindow : Window
{
    public string SelectedHex { get; private set; } = "#FFFFFF";
    private bool _isUpdating = false;

    public ColorPickerWindow(string initialHex)
    {
        InitializeComponent();
        
        var colors = new List<string>
        {
            "#3b82f6", "#ef4444", "#10b981", "#f59e0b", "#6366f1",
            "#ec4899", "#8b5cf6", "#06b6d4", "#84cc16", "#f97316",
            "#ffffff", "#d1d5db", "#9ca3af", "#6b7280", "#4b5563",
            "#374151", "#1f2937", "#111827", "#000000", "#ff0000"
        };
        ColorGrid.ItemsSource = colors;

        SetColorFromHex(initialHex);
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void SetColorFromHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex) || !hex.StartsWith("#") || _isUpdating) return;
        
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            _isUpdating = true;
            
            SldR.Value = color.R;
            SldG.Value = color.G;
            SldB.Value = color.B;
            
            TxtR.Text = color.R.ToString();
            TxtG.Text = color.G.ToString();
            TxtB.Text = color.B.ToString();
            
            TxtHex.Text = hex.ToUpper();
            UpdatePreview(color, hex.ToUpper());
            
            _isUpdating = false;
        }
        catch { }
    }

    private void SyncFromSliders()
    {
        if (_isUpdating) return;
        _isUpdating = true;

        byte r = (byte)SldR.Value;
        byte g = (byte)SldG.Value;
        byte b = (byte)SldB.Value;

        TxtR.Text = r.ToString();
        TxtG.Text = g.ToString();
        TxtB.Text = b.ToString();

        var color = Color.FromRgb(r, g, b);
        var hex = $"#{r:X2}{g:X2}{b:X2}";
        
        TxtHex.Text = hex;
        UpdatePreview(color, hex);

        _isUpdating = false;
    }

    private void SyncFromTextBoxes()
    {
        if (_isUpdating) return;

        if (byte.TryParse(TxtR.Text, out byte r) && 
            byte.TryParse(TxtG.Text, out byte g) && 
            byte.TryParse(TxtB.Text, out byte b))
        {
            _isUpdating = true;
            SldR.Value = r;
            SldG.Value = g;
            SldB.Value = b;
            
            var color = Color.FromRgb(r, g, b);
            var hex = $"#{r:X2}{g:X2}{b:X2}";
            TxtHex.Text = hex;
            UpdatePreview(color, hex);
            _isUpdating = false;
        }
    }

    private void UpdatePreview(Color color, string hex)
    {
        PreviewBorder.Background = new SolidColorBrush(color);
        SelectedHex = hex;
        
        // Dynamic contrasting text for Preview panel
        double luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;
        LblPreview.Foreground = luminance > 0.5 ? new SolidColorBrush(Colors.Black) : new SolidColorBrush(Colors.White);
    }

    private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => SyncFromSliders();
    private void Rgb_TextChanged(object sender, TextChangedEventArgs e) => SyncFromTextBoxes();

    private void TxtHex_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isUpdating && TxtHex.Text.Length >= 7 && TxtHex.Text.StartsWith("#"))
        {
            SetColorFromHex(TxtHex.Text);
        }
    }

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string hex)
        {
            SetColorFromHex(hex);
        }
    }

    private void Select_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
