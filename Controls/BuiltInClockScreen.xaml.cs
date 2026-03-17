using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using PcStatsMonitor.Models;

namespace PcStatsMonitor.Controls
{
    public partial class BuiltInClockScreen : UserControl
    {
        private DispatcherTimer? _timer;
        private ClockConfig _config = new();
        private string _themeBackground = "#060606";

        public BuiltInClockScreen()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _timer.Tick += (_, _) => UpdateTime();
            _timer.Start();
            UpdateTime();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e) => _timer?.Stop();

        // ── Public API ──────────────────────────────────────────────────────────
        public void ApplyConfig(ClockConfig cfg, ThemeConfig theme)
        {
            _config = cfg ?? new ClockConfig();
            _themeBackground = theme?.BackgroundColor ?? "#060606";

            ApplyBackground();
            ApplyAnalogFace();
            ApplyHandColors();
            ApplyClockTransform();
            ApplyDigitalStyle();
            ApplyDateStyle();
            UpdateTime();
        }

        // ── Time Update ─────────────────────────────────────────────────────────
        public void UpdateTime()
        {
            var now = DateTime.Now;

            RotSec.Angle = now.Second * 6.0;
            RotSecTail.Angle = now.Second * 6.0;
            RotMin.Angle = (now.Minute * 6.0) + (now.Second * 0.1);
            RotHour.Angle = (now.Hour % 12 * 30.0) + (now.Minute * 0.5);

            bool is24h = _config.DigitalFormat == "24h";
            TxtDigital.Text = is24h ? now.ToString("HH:mm") : now.ToString("hh:mm");
            TxtAmPm.Visibility = is24h ? Visibility.Collapsed : Visibility.Visible;
            TxtAmPm.Text = now.ToString("tt");

            TxtDate.Text = _config.DateFormat switch
            {
                "Short"   => now.ToString("dd/MM/yyyy"),
                "Numeric" => now.ToString("MM/dd/yyyy"),
                _         => now.ToString("dddd, dd MMM yyyy")
            };
        }

        // ── Apply Methods ───────────────────────────────────────────────────────
        private void ApplyBackground()
        {
            if (_config.UseCustomBackground)
            {
                CustomBgBlock.Visibility = Visibility.Visible;
                CustomBgColor.Fill = ParseBrush(_config.CustomBackgroundColor, "#060606");
                if (!string.IsNullOrWhiteSpace(_config.CustomBackgroundImagePath))
                {
                    CustomBgImage.Source = new System.Windows.Media.Imaging.BitmapImage(
                        new Uri(_config.CustomBackgroundImagePath, UriKind.RelativeOrAbsolute));
                    CustomBgImage.Opacity = _config.CustomBackgroundOpacity;
                }
                else
                {
                    CustomBgImage.Source = null;
                }
            }
            else
            {
                CustomBgBlock.Visibility = Visibility.Collapsed;
                RootClockGrid.Background = ParseBrush(_themeBackground, "#060606");
            }
        }

        private void ApplyHandColors()
        {
            HandHour.Fill = ParseBrush(_config.HourHandColor, "#FFFFFF");
            HandMin.Fill  = ParseBrush(_config.MinuteHandColor, "#FFFFFF");
            HandSec.Fill  = ParseBrush(_config.SecondHandColor, "#3b82f6");
            HandSecTail.Fill = ParseBrush(_config.SecondHandColor, "#3b82f6");
            CenterPin.Fill   = ParseBrush(_config.SecondHandColor, "#3b82f6");
        }

        private void ApplyClockTransform()
        {
            ClockScale.ScaleX = _config.ClockScale;
            ClockScale.ScaleY = _config.ClockScale;
            ClockTranslate.X = _config.ClockOffsetX;
            ClockTranslate.Y = _config.ClockOffsetY;
        }

        private void ApplyDigitalStyle()
        {
            PnlDigitalClock.Visibility = _config.ShowDigitalClock ? Visibility.Visible : Visibility.Collapsed;
            if (!_config.ShowDigitalClock) return;

            TxtDigital.Foreground = ParseBrush(_config.DigitalColor, "#FFFFFF");
            TxtAmPm.Foreground = ParseBrush(_config.DigitalColor, "#FFFFFF");
            TxtDigital.FontSize = _config.DigitalFontSize > 0 ? _config.DigitalFontSize : 80;
            TxtDigital.FontFamily = ParseFont(_config.DigitalFontFamily);
            DigitalTranslate.X = _config.DigitalOffsetX;
            DigitalTranslate.Y = _config.DigitalOffsetY;
        }

        private void ApplyDateStyle()
        {
            TxtDate.Visibility = _config.ShowDate ? Visibility.Visible : Visibility.Collapsed;
            if (!_config.ShowDate) return;

            TxtDate.Foreground = ParseBrush(_config.DateColor, "#AAAAAA");
            TxtDate.FontSize = _config.DateFontSize > 0 ? _config.DateFontSize : 18;
            TxtDate.FontFamily = ParseFont(_config.DateFontFamily);
            DateTranslate.X = _config.DateOffsetX;
            DateTranslate.Y = _config.DateOffsetY;
        }

        // ── Analog Face Rendering ───────────────────────────────────────────────
        private void ApplyAnalogFace()
        {
            TickCanvas.Children.Clear();
            
            // Default: circular face background
            ClockFaceEllipse.Visibility = Visibility.Visible;
            ClockFaceRect.Visibility = Visibility.Collapsed;
            ClockFaceEllipse.Fill = ParseBrush(_config.ClockFaceColor, "#1A1A1A");
            OuterRing.Fill = Brushes.Transparent;
            OuterRing.Stroke = Brushes.Transparent;
            OuterRing.StrokeThickness = 0;
            OuterRing.Effect = null;
            OuterRing.StrokeDashArray = null;

            switch (_config.FaceName)
            {
                case "Neon":    DrawNeonFace();    break;
                case "Minimal": DrawMinimalFace(); break;
                case "Glow":    DrawGlowFace();    break;
                case "Bold":    DrawBoldFace();    break;
                case "Luxury":  DrawLuxuryFace();  break;
                case "Square":  DrawSquareFace();  break;
                case "Techno":  DrawTechnoFace();  break;
                case "Roman":   DrawRomanFace();   break;
                case "Gold":    DrawGoldFace();    break;
                case "Dot":     DrawDotFace();     break;
                case "Orbit":   DrawOrbitFace();   break;
                case "Industrial": DrawIndustrialFace(); break;
                case "Retro":   DrawRetroFace();   break;
                case "Futura":  DrawFuturaFace();  break;
                case "Square Minimal": DrawSquareMinimalFace(); break;
                case "Square Luxury":  DrawSquareLuxuryFace();  break;
                default:        DrawClassicFace(); break;
            }
        }

        private void DrawClassicFace()
        {
            // Standard white hour markers + minute dots
            OuterRing.Fill       = ParseBrush(_config.ClockFaceColor, "#1A1A1A");
            OuterRing.Stroke     = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
            OuterRing.StrokeThickness = 2;
            OuterRing.Effect     = null;

            for (int i = 0; i < 60; i++)
            {
                double angle = i * 6.0 * Math.PI / 180.0;
                bool isHour = i % 5 == 0;
                double cx = 140, cy = 140;
                double outerR = 130, innerR = isHour ? 115 : 124;
                double x1 = cx + outerR * Math.Sin(angle);
                double y1 = cy - outerR * Math.Cos(angle);
                double x2 = cx + innerR * Math.Sin(angle);
                double y2 = cy - innerR * Math.Cos(angle);

                var line = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
                    Stroke = Brushes.White,
                    StrokeThickness = isHour ? 2.5 : 1.0,
                    Opacity = isHour ? 0.9 : 0.3 };
                TickCanvas.Children.Add(line);
            }
        }

        private void DrawNeonFace()
        {
            // Electric blue neon glow ring
            OuterRing.Fill = new SolidColorBrush(Colors.Transparent);
            OuterRing.Stroke = new SolidColorBrush(Color.FromArgb(200, 0, 200, 255));
            OuterRing.StrokeThickness = 3;
            OuterRing.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Color.FromRgb(0, 180, 255), BlurRadius = 20, ShadowDepth = 0, Opacity = 1
            };

            for (int i = 0; i < 12; i++)
            {
                double angle = i * 30.0 * Math.PI / 180.0;
                double cx = 140, cy = 140, r = 128;
                var dot = new Ellipse { Width = 6, Height = 6,
                    Fill = new SolidColorBrush(Color.FromArgb(220, 0, 200, 255)) };
                Canvas.SetLeft(dot, cx + r * Math.Sin(angle) - 3);
                Canvas.SetTop(dot,  cy - r * Math.Cos(angle) - 3);
                TickCanvas.Children.Add(dot);
            }
        }

        private void DrawMinimalFace()
        {
            // Hairline ring + just 4 dots
            OuterRing.Fill = new SolidColorBrush(Colors.Transparent);
            OuterRing.Stroke = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
            OuterRing.StrokeThickness = 1;
            OuterRing.Effect = null;

            foreach (int h in new[] { 0, 3, 6, 9 })
            {
                double angle = h * 30.0 * Math.PI / 180.0;
                double cx = 140, cy = 140, r = 122;
                var dot = new Ellipse { Width = 8, Height = 8, Fill = Brushes.White };
                Canvas.SetLeft(dot, cx + r * Math.Sin(angle) - 4);
                Canvas.SetTop(dot,  cy - r * Math.Cos(angle) - 4);
                TickCanvas.Children.Add(dot);
            }
        }

        private void DrawGlowFace()
        {
            // Warm amber glow
            OuterRing.Fill = new SolidColorBrush(Colors.Transparent);
            OuterRing.Stroke = new SolidColorBrush(Color.FromArgb(180, 255, 180, 50));
            OuterRing.StrokeThickness = 3;
            OuterRing.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Color.FromRgb(255, 160, 0), BlurRadius = 25, ShadowDepth = 0, Opacity = 0.9
            };

            for (int i = 0; i < 12; i++)
            {
                double angle = i * 30.0 * Math.PI / 180.0;
                double cx = 140, cy = 140, r = 125;
                var line = new Line
                {
                    X1 = cx + r * Math.Sin(angle),       Y1 = cy - r * Math.Cos(angle),
                    X2 = cx + (r-14) * Math.Sin(angle),  Y2 = cy - (r-14) * Math.Cos(angle),
                    Stroke = new SolidColorBrush(Color.FromArgb(220, 255, 180, 50)),
                    StrokeThickness = 3
                };
                TickCanvas.Children.Add(line);
            }
        }

        private void DrawBoldFace()
        {
            OuterRing.Fill = ParseBrush(_config.ClockFaceColor, "#1A1A1A");
            OuterRing.Stroke = new SolidColorBrush(Color.FromArgb(80, 59, 130, 246));
            OuterRing.StrokeThickness = 5;
            OuterRing.Effect = null;

            for (int i = 0; i < 12; i++)
            {
                double angle = i * 30.0 * Math.PI / 180.0;
                double cx = 140, cy = 140;
                double r1 = 128, r2 = 108;
                var rect = new Rectangle
                {
                    Width = 4, Height = r1 - r2, RadiusX = 2, RadiusY = 2,
                    Fill = new SolidColorBrush(Color.FromArgb(230, 59, 130, 246)),
                    RenderTransformOrigin = new Point(0.5, 0)
                };
                rect.RenderTransform = new RotateTransform(i * 30.0);
                Canvas.SetLeft(rect, cx - 2);
                Canvas.SetTop(rect,  cy - r1);
                TickCanvas.Children.Add(rect);
            }
        }

        private void DrawLuxuryFace()
        {
            // Gold ring + thin markers
            OuterRing.Fill = Brushes.Transparent;
            OuterRing.Stroke = new SolidColorBrush(Color.FromRgb(212, 175, 55)); // Gold
            OuterRing.StrokeThickness = 2;
            OuterRing.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(212, 175, 55), BlurRadius = 15, ShadowDepth = 0, Opacity = 0.5 };

            for (int i = 0; i < 12; i++)
            {
                double angle = i * 30.0 * Math.PI / 180.0;
                double cx = 140, cy = 140, r1 = 125, r2 = 110;
                var line = new Line {
                    X1 = cx + r1 * Math.Sin(angle), Y1 = cy - r1 * Math.Cos(angle),
                    X2 = cx + r2 * Math.Sin(angle), Y2 = cy - r2 * Math.Cos(angle),
                    Stroke = new SolidColorBrush(Color.FromRgb(212, 175, 55)), StrokeThickness = 1.5
                };
                TickCanvas.Children.Add(line);
            }
        }

        private void DrawSquareFace()
        {
            // Square face background + square dot markers
            ClockFaceEllipse.Visibility = Visibility.Collapsed;
            ClockFaceRect.Visibility = Visibility.Visible;
            ClockFaceRect.Fill = ParseBrush(_config.ClockFaceColor, "#1A1A1A");
            ClockFaceRect.Stroke = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255));
            ClockFaceRect.StrokeThickness = 1;
            
            for (int i = 0; i < 12; i++)
            {
                double angle = i * 30.0 * Math.PI / 180.0;
                double cx = 140, cy = 140, r = 120;
                var rect = new Rectangle { Width = 6, Height = 6, Fill = Brushes.White, Opacity = 0.8 };
                Canvas.SetLeft(rect, cx + r * Math.Sin(angle) - 3);
                Canvas.SetTop(rect,  cy - r * Math.Cos(angle) - 3);
                TickCanvas.Children.Add(rect);
            }
        }

        private void DrawTechnoFace()
        {
            // Cyberpunk - radial lines from center + tick marks at edge using proper Line approach
            OuterRing.Stroke = new SolidColorBrush(Color.FromRgb(0, 255, 150));
            OuterRing.StrokeThickness = 1;
            
            for (int i = 0; i < 12; i++)
            {
                double angle = i * 30.0 * Math.PI / 180.0;
                double cx = 140, cy = 140;
                
                // Faint radial grid lines from center outward
                var gridLine = new Line {
                    X1 = cx, Y1 = cy, 
                    X2 = cx + 130 * Math.Sin(angle), Y2 = cy - 130 * Math.Cos(angle),
                    Stroke = new SolidColorBrush(Color.FromArgb(30, 0, 255, 150)), StrokeThickness = 1
                };
                TickCanvas.Children.Add(gridLine);
                
                // Radial tick marks at the edge (outer → inner, properly radial)
                var tick = new Line {
                    X1 = cx + 128 * Math.Sin(angle), Y1 = cy - 128 * Math.Cos(angle),
                    X2 = cx + 114 * Math.Sin(angle), Y2 = cy - 114 * Math.Cos(angle),
                    Stroke = new SolidColorBrush(Color.FromRgb(0, 255, 150)), StrokeThickness = 2
                };
                TickCanvas.Children.Add(tick);
            }
        }

        private void DrawRomanFace()
        {
            string[] roman = { "XII", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI" };
            for (int i = 0; i < 12; i++)
            {
                double angle = i * 30.0 * Math.PI / 180.0;
                double cx = 140, cy = 140, r = 110;
                var txt = new TextBlock {
                    Text = roman[i], Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.Bold,
                    TextAlignment = TextAlignment.Center, Width = 30
                };
                Canvas.SetLeft(txt, cx + r * Math.Sin(angle) - 15);
                Canvas.SetTop(txt,  cy - r * Math.Cos(angle) - 10);
                TickCanvas.Children.Add(txt);
            }
        }

        private void DrawGoldFace()
        {
            OuterRing.Stroke = new SolidColorBrush(Color.FromRgb(255, 215, 0));
            OuterRing.StrokeThickness = 4;
            for (int i = 0; i < 60; i++)
            {
                if (i % 5 != 0) continue;
                double angle = i * 6.0 * Math.PI / 180.0;
                double cx = 140, cy = 140, r = 125;
                var dot = new Ellipse { Width = 4, Height = 4, Fill = new SolidColorBrush(Color.FromRgb(255, 215, 0)) };
                Canvas.SetLeft(dot, cx + r * Math.Sin(angle) - 2);
                Canvas.SetTop(dot,  cy - r * Math.Cos(angle) - 2);
                TickCanvas.Children.Add(dot);
            }
        }

        private void DrawDotFace()
        {
            for (int i = 0; i < 60; i++)
            {
                double angle = i * 6.0 * Math.PI / 180.0;
                double cx = 140, cy = 140, r = 125;
                bool isHour = i % 5 == 0;
                var dot = new Ellipse {
                    Width = isHour ? 4 : 1.5, Height = isHour ? 4 : 1.5,
                    Fill = Brushes.White, Opacity = isHour ? 1.0 : 0.4
                };
                Canvas.SetLeft(dot, cx + r * Math.Sin(angle) - (isHour ? 2 : 0.75));
                Canvas.SetTop(dot,  cy - r * Math.Cos(angle) - (isHour ? 2 : 0.75));
                TickCanvas.Children.Add(dot);
            }
        }

        private void DrawOrbitFace()
        {
            for (int i = 0; i < 12; i++)
            {
                double angle = i * 30.0 * Math.PI / 180.0;
                double cx = 140, cy = 140;
                double r = 100 + (i % 3 * 12); // Pulsating orbit radius
                var dot = new Ellipse { Width = 6, Height = 6, Fill = new SolidColorBrush(Color.FromRgb(59, 130, 246)) };
                Canvas.SetLeft(dot, cx + r * Math.Sin(angle) - 3);
                Canvas.SetTop(dot,  cy - r * Math.Cos(angle) - 3);
                TickCanvas.Children.Add(dot);
            }
        }

        private void DrawIndustrialFace()
        {
            OuterRing.Stroke = new SolidColorBrush(Color.FromRgb(100, 100, 100));
            OuterRing.StrokeThickness = 8;
            OuterRing.StrokeDashArray = new DoubleCollection { 1, 2 };
            for (int i = 0; i < 12; i++)
            {
                double angle = i * 30.0 * Math.PI / 180.0;
                double cx = 140, cy = 140;
                // Properly radial thick tick marks using Line
                var line = new Line {
                    X1 = cx + 125 * Math.Sin(angle), Y1 = cy - 125 * Math.Cos(angle),
                    X2 = cx + 108 * Math.Sin(angle), Y2 = cy - 108 * Math.Cos(angle),
                    Stroke = Brushes.Gray, StrokeThickness = 8
                };
                TickCanvas.Children.Add(line);
            }
        }

        private void DrawRetroFace()
        {
            // Orange retro vibe - properly radial thick dashes using Line approach
            for (int i = 0; i < 12; i++)
            {
                double angle = i * 30.0 * Math.PI / 180.0;
                double cx = 140, cy = 140;
                bool isHour = true; // All are hour markers
                
                // Thick radial lines properly oriented: outer to inner
                var line = new Line {
                    X1 = cx + 128 * Math.Sin(angle), Y1 = cy - 128 * Math.Cos(angle),
                    X2 = cx + 108 * Math.Sin(angle), Y2 = cy - 108 * Math.Cos(angle),
                    Stroke = new SolidColorBrush(Color.FromRgb(255, 100, 0)),
                    StrokeThickness = 5,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                };
                TickCanvas.Children.Add(line);
            }
        }

        private void DrawFuturaFace()
        {
            OuterRing.Stroke = Brushes.White;
            OuterRing.StrokeThickness = 0.5;
            for (int i = 0; i < 4; i++)
            {
                double angle = i * 90.0 * Math.PI / 180.0;
                double cx = 140, cy = 140, r = 120;
                var txt = new TextBlock {
                    Text = (i == 0 ? 12 : i * 3).ToString(), Foreground = Brushes.White,
                    FontSize = 24, FontWeight = FontWeights.ExtraBold, Width = 40, TextAlignment = TextAlignment.Center
                };
                Canvas.SetLeft(txt, cx + r * Math.Sin(angle) - 20);
                Canvas.SetTop(txt,  cy - r * Math.Cos(angle) - 12);
                TickCanvas.Children.Add(txt);
            }
        }

        private void DrawSquareMinimalFace()
        {
            // Square face background + clean white square dot markers
            ClockFaceEllipse.Visibility = Visibility.Collapsed;
            ClockFaceRect.Visibility = Visibility.Visible;
            ClockFaceRect.Fill = ParseBrush(_config.ClockFaceColor, "#1A1A1A");
            ClockFaceRect.Stroke = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
            ClockFaceRect.StrokeThickness = 1;
            
            for (int i = 0; i < 12; i++)
            {
                double angle = i * 30.0 * Math.PI / 180.0;
                double cx = 140, cy = 140, r = 118;
                bool isHour = true;
                var rect = new Rectangle { Width = 10, Height = 3, Fill = Brushes.White, Opacity = 0.9 };
                // Use Line instead for proper radial alignment
                var line = new Line {
                    X1 = cx + 126 * Math.Sin(angle), Y1 = cy - 126 * Math.Cos(angle),
                    X2 = cx + 110 * Math.Sin(angle), Y2 = cy - 110 * Math.Cos(angle),
                    Stroke = Brushes.White, StrokeThickness = 3, Opacity = 0.9
                };
                TickCanvas.Children.Add(line);
            }
        }

        private void DrawSquareLuxuryFace()
        {
            // Square face background + gold markers
            ClockFaceEllipse.Visibility = Visibility.Collapsed;
            ClockFaceRect.Visibility = Visibility.Visible;
            ClockFaceRect.Fill = ParseBrush(_config.ClockFaceColor, "#1A1A1A");
            ClockFaceRect.Stroke = new SolidColorBrush(Color.FromRgb(212, 175, 55));
            ClockFaceRect.StrokeThickness = 2;
            
            for (int i = 0; i < 12; i++)
            {
                double angle = i * 30.0 * Math.PI / 180.0;
                double cx = 140, cy = 140;
                var line = new Line {
                    X1 = cx + 126 * Math.Sin(angle), Y1 = cy - 126 * Math.Cos(angle),
                    X2 = cx + 110 * Math.Sin(angle), Y2 = cy - 110 * Math.Cos(angle),
                    Stroke = new SolidColorBrush(Color.FromRgb(212, 175, 55)),
                    StrokeThickness = 2
                };
                TickCanvas.Children.Add(line);
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────
        private static SolidColorBrush ParseBrush(string hex, string fallback)
        {
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback)); }
        }

        private static FontFamily ParseFont(string name) =>
            string.IsNullOrWhiteSpace(name) ? new FontFamily("Segoe UI") : new FontFamily(name);
    }
}
