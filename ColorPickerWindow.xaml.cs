using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace PcStatsMonitor;

/// <summary>
/// Professional colour picker with an HSB colour wheel, brightness slider,
/// preset swatches, and manual RGB/Hex input. The wheel renders a circular
/// hue-saturation gradient that the user can click/drag on to pick a colour.
/// </summary>
public partial class ColorPickerWindow : Window
{
    public string SelectedHex { get; private set; } = "#FFFFFF";
    public bool IsGradient { get; private set; } = false;
    public string GradientEndHex { get; private set; } = "#FFFFFF";
    
    private bool _isUpdating = false;
    private bool _isDraggingWheel = false;
    private bool _editingEndColor = false;
    private Color _startColor = Colors.White;
    private Color _endColor = Colors.White;

    // Current HSB state tracked from wheel interaction
    private double _wheelHue = 0;
    private double _wheelSaturation = 0;
    private double _wheelBrightness = 100;

    public ColorPickerWindow(string initialHex, bool isGradient = false, string endHex = "#FFFFFF")
    {
        InitializeComponent();
        
        IsGradient = isGradient;
        SelectedHex = initialHex;
        GradientEndHex = endHex;
        
        ChkGradient.IsChecked = IsGradient;

        var colors = new List<string>
        {
            "#3b82f6", "#ef4444", "#10b981", "#f59e0b", "#6366f1",
            "#ec4899", "#8b5cf6", "#06b6d4", "#84cc16", "#f97316",
            "#ffffff", "#d1d5db", "#9ca3af", "#6b7280", "#4b5563",
            "#374151", "#1f2937", "#111827", "#000000", "#ff0000"
        };
        ColorGrid.ItemsSource = colors;

        // Render the colour wheel once the canvas is loaded
        ColorWheelCanvas.Loaded += (s, e) =>
        {
            RenderColorWheel();
            
            try 
            {
                _startColor = (Color)ColorConverter.ConvertFromString(SelectedHex);
                _endColor = (Color)ColorConverter.ConvertFromString(GradientEndHex);
            }
            catch {}
            
            SetColorFromHex(initialHex);
            UpdatePreviewInternal();
        };
    }

    private void ChkGradient_Changed(object sender, RoutedEventArgs e)
    {
        if (PnlGradientTargets == null) return;
        IsGradient = ChkGradient.IsChecked == true;
        PnlGradientTargets.Visibility = IsGradient ? Visibility.Visible : Visibility.Collapsed;
        UpdatePreviewInternal();
    }

    private void ColorTarget_Changed(object sender, RoutedEventArgs e)
    {
        if (RbStartColor == null || RbEndColor == null) return;
        _editingEndColor = RbEndColor.IsChecked == true;
        
        RbStartColor.Foreground = _editingEndColor ? new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)) : new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));
        RbEndColor.Foreground = _editingEndColor ? new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)) : new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF));
        
        SetColorFromHex(_editingEndColor ? GradientEndHex : SelectedHex);
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    #region ══════ Colour Wheel Rendering ══════

    /// <summary>
    /// Renders a bitmap-based HSB colour wheel onto the canvas.
    /// Each pixel is computed from its polar coordinates relative to the centre:
    ///   - Angle → Hue (0–360°)
    ///   - Distance from centre → Saturation (0–1)
    ///   - Brightness is controlled by the separate slider
    /// </summary>
    private void RenderColorWheel()
    {
        int size = (int)ColorWheelCanvas.Width;
        if (size <= 0) size = 260;

        var bitmap = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
        var pixels = new byte[size * size * 4];
        int cx = size / 2;
        int cy = size / 2;
        double radius = size / 2.0;
        double brightness = SldBrightness.Value / 100.0;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double dx = x - cx;
                double dy = y - cy;
                double dist = Math.Sqrt(dx * dx + dy * dy);

                int idx = (y * size + x) * 4;

                if (dist <= radius)
                {
                    double hue = Math.Atan2(dy, dx) * (180.0 / Math.PI) + 180.0;
                    double sat = dist / radius;
                    HsbToRgb(hue, sat, brightness, out byte r, out byte g, out byte b);

                    // Anti-aliased edge
                    double alpha = Math.Min(1.0, radius - dist);
                    byte a = (byte)(alpha * 255);

                    pixels[idx] = b;     // B
                    pixels[idx + 1] = g; // G
                    pixels[idx + 2] = r; // R
                    pixels[idx + 3] = a; // A
                }
                else
                {
                    pixels[idx] = 0;
                    pixels[idx + 1] = 0;
                    pixels[idx + 2] = 0;
                    pixels[idx + 3] = 0;
                }
            }
        }

        bitmap.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);

        // Remove any previous wheel image and add the new one
        for (int i = ColorWheelCanvas.Children.Count - 1; i >= 0; i--)
        {
            if (ColorWheelCanvas.Children[i] is System.Windows.Controls.Image)
                ColorWheelCanvas.Children.RemoveAt(i);
        }

        var img = new System.Windows.Controls.Image
        {
            Source = bitmap,
            Width = size,
            Height = size
        };
        Canvas.SetLeft(img, 0);
        Canvas.SetTop(img, 0);
        ColorWheelCanvas.Children.Insert(0, img);
    }

    #endregion

    #region ══════ Colour Wheel Mouse Interaction ══════

    private void ColorWheel_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingWheel = true;
        ((UIElement)sender).CaptureMouse();
        PickColorFromWheel(e.GetPosition(ColorWheelCanvas));
        e.Handled = true; // Prevent bubbling to Window's DragMove handler
    }

    private void ColorWheel_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingWheel) return;
        PickColorFromWheel(e.GetPosition(ColorWheelCanvas));
        e.Handled = true;
    }

    private void ColorWheel_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDraggingWheel = false;
        ((UIElement)sender).ReleaseMouseCapture();
        e.Handled = true;
    }

    /// <summary>
    /// Picks a colour from the wheel at the given mouse position.
    /// Converts cartesian coordinates to polar (hue, saturation) and
    /// combines with the brightness slider to produce the final RGB.
    /// </summary>
    private void PickColorFromWheel(Point pos)
    {
        double size = ColorWheelCanvas.Width;
        double cx = size / 2.0;
        double cy = size / 2.0;
        double radius = size / 2.0;

        double dx = pos.X - cx;
        double dy = pos.Y - cy;
        double dist = Math.Sqrt(dx * dx + dy * dy);

        // Clamp to within the circle
        if (dist > radius) dist = radius;

        _wheelHue = Math.Atan2(dy, dx) * (180.0 / Math.PI) + 180.0;
        _wheelSaturation = dist / radius;
        _wheelBrightness = SldBrightness.Value;

        HsbToRgb(_wheelHue, _wheelSaturation, _wheelBrightness / 100.0, out byte r, out byte g, out byte b);

        // Update selector position (clamped to circle edge)
        double selectorX = cx + (dist / radius) * radius * Math.Cos((_wheelHue - 180.0) * Math.PI / 180.0);
        double selectorY = cy + (dist / radius) * radius * Math.Sin((_wheelHue - 180.0) * Math.PI / 180.0);
        WheelSelector.Margin = new Thickness(selectorX - 6, selectorY - 6, 0, 0);

        // Push to sliders/hex without re-entering
        _isUpdating = true;
        SldR.Value = r;
        SldG.Value = g;
        SldB.Value = b;
        TxtR.Text = r.ToString();
        TxtG.Text = g.ToString();
        TxtB.Text = b.ToString();
        var hex = $"#{r:X2}{g:X2}{b:X2}";
        TxtHex.Text = hex;
        
        var color = Color.FromRgb(r, g, b);
        if (_editingEndColor)
        {
            _endColor = color;
            GradientEndHex = hex;
        }
        else
        {
            _startColor = color;
            SelectedHex = hex;
        }
        
        UpdatePreviewInternal();
        _isUpdating = false;
    }

    private void SldBrightness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdating || ColorWheelCanvas == null || SldR == null) return;
        _wheelBrightness = SldBrightness.Value;
        RenderColorWheel();

        // Re-pick the current wheel position at new brightness
        HsbToRgb(_wheelHue, _wheelSaturation, _wheelBrightness / 100.0, out byte r, out byte g, out byte b);
        _isUpdating = true;
        SldR.Value = r;
        SldG.Value = g;
        SldB.Value = b;
        TxtR.Text = r.ToString();
        TxtG.Text = g.ToString();
        TxtB.Text = b.ToString();
        var hex = $"#{r:X2}{g:X2}{b:X2}";
        TxtHex.Text = hex;
        
        var color = Color.FromRgb(r, g, b);
        if (_editingEndColor)
        {
            _endColor = color;
            GradientEndHex = hex;
        }
        else
        {
            _startColor = color;
            SelectedHex = hex;
        }
        
        UpdatePreviewInternal();
        _isUpdating = false;
    }

    #endregion

    #region ══════ HSB ↔ RGB Conversion ══════

    /// <summary>
    /// Converts HSB (Hue 0–360, Saturation 0–1, Brightness 0–1) to RGB bytes.
    /// Standard algorithm for colour wheel rendering.
    /// </summary>
    private static void HsbToRgb(double h, double s, double b, out byte r, out byte g, out byte bl)
    {
        h = h % 360;
        if (h < 0) h += 360;

        double c = b * s;
        double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
        double m = b - c;

        double r1, g1, b1;
        if (h < 60) { r1 = c; g1 = x; b1 = 0; }
        else if (h < 120) { r1 = x; g1 = c; b1 = 0; }
        else if (h < 180) { r1 = 0; g1 = c; b1 = x; }
        else if (h < 240) { r1 = 0; g1 = x; b1 = c; }
        else if (h < 300) { r1 = x; g1 = 0; b1 = c; }
        else { r1 = c; g1 = 0; b1 = x; }

        r = (byte)Math.Round((r1 + m) * 255);
        g = (byte)Math.Round((g1 + m) * 255);
        bl = (byte)Math.Round((b1 + m) * 255);
    }

    #endregion

    #region ══════ Existing Slider / TextBox / Hex Sync ══════

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
            
            if (_editingEndColor)
            {
                _endColor = color;
                GradientEndHex = hex.ToUpper();
            }
            else
            {
                _startColor = color;
                SelectedHex = hex.ToUpper();
            }
            
            UpdatePreviewInternal();

            // Position the wheel selector to match this colour
            UpdateWheelSelectorFromRgb(color.R, color.G, color.B);
            
            _isUpdating = false;
        }
        catch { }
    }

    /// <summary>
    /// Reverse-maps an RGB colour back to the wheel position and moves the selector there.
    /// </summary>
    private void UpdateWheelSelectorFromRgb(byte r, byte g, byte b)
    {
        // RGB → HSB
        double rd = r / 255.0, gd = g / 255.0, bd = b / 255.0;
        double max = Math.Max(rd, Math.Max(gd, bd));
        double min = Math.Min(rd, Math.Min(gd, bd));
        double delta = max - min;

        double hue = 0;
        if (delta > 0.0001)
        {
            if (max == rd) hue = 60.0 * (((gd - bd) / delta) % 6);
            else if (max == gd) hue = 60.0 * (((bd - rd) / delta) + 2);
            else hue = 60.0 * (((rd - gd) / delta) + 4);
        }
        if (hue < 0) hue += 360;

        double sat = max > 0 ? delta / max : 0;
        double bri = max;

        _wheelHue = hue;
        _wheelSaturation = sat;
        _wheelBrightness = bri * 100;

        if (SldBrightness != null)
        {
            SldBrightness.Value = _wheelBrightness;
        }

        // Position selector on wheel
        double size = ColorWheelCanvas?.Width ?? 260;
        double cx = size / 2.0;
        double cy = size / 2.0;
        double radius = size / 2.0;

        double angle = (hue - 180.0) * Math.PI / 180.0;
        double dist = sat * radius;
        double sx = cx + dist * Math.Cos(angle);
        double sy = cy + dist * Math.Sin(angle);

        if (WheelSelector != null)
            WheelSelector.Margin = new Thickness(sx - 6, sy - 6, 0, 0);
    }

    private void SyncFromSliders()
    {
        if (_isUpdating || SldR == null || SldG == null || SldB == null || TxtR == null) return;
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
        
        if (_editingEndColor)
        {
            _endColor = color;
            GradientEndHex = hex;
        }
        else
        {
            _startColor = color;
            SelectedHex = hex;
        }
        
        UpdatePreviewInternal();
        UpdateWheelSelectorFromRgb(r, g, b);

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
            
            if (_editingEndColor)
            {
                _endColor = color;
                GradientEndHex = hex;
            }
            else
            {
                _startColor = color;
                SelectedHex = hex;
            }
            
            UpdatePreviewInternal();
            UpdateWheelSelectorFromRgb(r, g, b);
            _isUpdating = false;
        }
    }

    private void UpdatePreviewInternal()
    {
        if (IsGradient)
        {
            PreviewBorder.Background = new LinearGradientBrush(_startColor, _endColor, 0.0);
            LblPreview.Foreground = new SolidColorBrush(Colors.White); // Assuming dark for gradient text generally
        }
        else
        {
            PreviewBorder.Background = new SolidColorBrush(_startColor);
            double luminance = (0.299 * _startColor.R + 0.587 * _startColor.G + 0.114 * _startColor.B) / 255;
            LblPreview.Foreground = luminance > 0.5 ? new SolidColorBrush(Colors.Black) : new SolidColorBrush(Colors.White);
        }
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

    #endregion

    #region ══════ Dialog Actions ══════

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

    #endregion
}
