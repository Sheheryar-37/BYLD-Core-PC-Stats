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
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(_config.ContinuousMotion ? 30 : 500) };
            _timer.Tick += (_, _) => UpdateTime();
            _timer.Start();
            UpdateTime();
            ApplyGlow();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e) => _timer?.Stop();

        // ── Public API ──────────────────────────────────────────────────────────
        public void ApplyConfig(ClockConfig cfg, ThemeConfig theme)
        {
            _config = cfg ?? new ClockConfig();
            _themeBackground = theme?.BackgroundColor ?? "#060606";

            if (_timer != null)
            {
                _timer.Interval = TimeSpan.FromMilliseconds(_config.ContinuousMotion ? 30 : 500);
            }

            ApplyBackground();
            ApplyAnalogFace();
            ApplyHandColors();
            ApplyClockTransform();
            ApplyDigitalStyle();
            ApplyDateStyle();
            ApplyGlow();
            UpdateTime();
        }

        // ── Time Update ─────────────────────────────────────────────────────────
        public void UpdateTime()
        {
            var now = DateTime.Now;
            double ms = now.Millisecond;
            double sec = now.Second + (ms / 1000.0);
            
            double secAngle = _config.ContinuousMotion ? sec * 6.0 : now.Second * 6.0;
            RotSec.Angle = secAngle;
            RotSecTail.Angle = secAngle;
            
            RotMin.Angle = (now.Minute * 6.0) + (sec * 0.1);
            RotHour.Angle = (now.Hour % 12 * 30.0) + (now.Minute * 0.5) + (sec * (0.5 / 60.0));

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
                // Use Transparent so the global background image from MainWindow shows through.
                // The Window's own Background color (from theme) is already visible underneath.
                RootClockGrid.Background = Brushes.Transparent;
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

        private bool IsSquareFace()
        {
            if (_config == null) return false;
            return _config.FaceName.StartsWith("Square");
        }

        private Shape CurrentRing => IsSquareFace() ? (Shape)OuterRingRect : (Shape)OuterRing;

        private void ApplyGlow()
        {
            // Reset both
            ClockFaceGlow.Visibility = Visibility.Collapsed;
            ClockFaceGlowRect.Visibility = Visibility.Collapsed;

            if (_config.ShowGlow)
            {
                bool isSquare = IsSquareFace();
                var glowElement = isSquare ? (FrameworkElement)ClockFaceGlowRect : (FrameworkElement)ClockFaceGlow;
                var effect = isSquare ? FaceGlowEffectRect : FaceGlowEffect;

                glowElement.Visibility = Visibility.Visible;
                effect.Color = ((SolidColorBrush)ParseBrush(_config.GlowColor, Constants.DefaultClockGlowColor)).Color;
                effect.BlurRadius = _config.GlowWidth > 0 ? _config.GlowWidth : 20;
            }
        }

        // ── Analog Face Rendering ───────────────────────────────────────────────
        private void ApplyAnalogFace()
        {
            TickCanvas.Children.Clear();
            
            // Default: circular face background
            bool isSquare = IsSquareFace();
            ClockFaceEllipse.Visibility = isSquare ? Visibility.Collapsed : Visibility.Visible;
            ClockFaceRect.Visibility = isSquare ? Visibility.Visible : Visibility.Collapsed;
            
            var faceBg = isSquare ? (Shape)ClockFaceRect : (Shape)ClockFaceEllipse;
            faceBg.Fill = ParseBrush(_config.ClockFaceColor, "#1A1A1A");

            // Handle Outer Ring Visibility and Shape
            OuterRing.Visibility = (!isSquare && _config.ShowOuterRing) ? Visibility.Visible : Visibility.Collapsed;
            OuterRingRect.Visibility = (isSquare && _config.ShowOuterRing) ? Visibility.Visible : Visibility.Collapsed;

            var currentRing = isSquare ? (Shape)OuterRingRect : (Shape)OuterRing;
            currentRing.Fill = Brushes.Transparent;
            currentRing.Stroke = Brushes.Transparent;
            currentRing.StrokeThickness = 0;
            currentRing.Effect = null;
            currentRing.StrokeDashArray = null;

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
                case "Square Bold":    DrawSquareBoldFace();    break;
                case "Square Neon":    DrawSquareNeonFace();    break;
                default:        DrawClassicFace(); break;
            }
        }

        private void DrawClassicFace()
        {
            // Standard white hour markers + minute dots
            CurrentRing.Fill       = ParseBrush(_config.ClockFaceColor, "#1A1A1A");
            CurrentRing.Stroke     = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
            CurrentRing.StrokeThickness = 2;
            CurrentRing.Effect     = null;

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
                    Stroke = ParseBrush(_config.MarkerColor, "#FFFFFF"),
                    StrokeThickness = isHour ? 2.5 : 1.0,
                    Opacity = isHour ? 0.9 : 0.3 };
                TickCanvas.Children.Add(line);
            }
        }

        private void DrawNeonFace()
        {
            // Electric blue neon glow ring
            CurrentRing.Fill = new SolidColorBrush(Colors.Transparent);
            CurrentRing.Stroke = new SolidColorBrush(Color.FromArgb(200, 0, 200, 255));
            CurrentRing.StrokeThickness = 3;
            CurrentRing.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Color.FromRgb(0, 180, 255), BlurRadius = 20, ShadowDepth = 0, Opacity = 1
            };

            for (int i = 0; i < 12; i++)
            {
                double angle = i * 30.0 * Math.PI / 180.0;
                double cx = 140, cy = 140, r = 128;
                var dot = new Ellipse { Width = 6, Height = 6,
                    Fill = ParseBrush(_config.MarkerColor, "#00C8FF") };
                Canvas.SetLeft(dot, cx + r * Math.Sin(angle) - 3);
                Canvas.SetTop(dot,  cy - r * Math.Cos(angle) - 3);
                TickCanvas.Children.Add(dot);
            }
        }

        private void DrawMinimalFace()
        {
            // Hairline ring + just 4 dots
            CurrentRing.Fill = new SolidColorBrush(Colors.Transparent);
            CurrentRing.Stroke = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
            CurrentRing.StrokeThickness = 1;
            CurrentRing.Effect = null;

            foreach (int h in new[] { 0, 3, 6, 9 })
            {
                double angle = h * 30.0 * Math.PI / 180.0;
                double cx = 140, cy = 140, r = 122;
                var dot = new Ellipse { Width = 8, Height = 8, Fill = ParseBrush(_config.MarkerColor, "#FFFFFF") };
                Canvas.SetLeft(dot, cx + r * Math.Sin(angle) - 4);
                Canvas.SetTop(dot,  cy - r * Math.Cos(angle) - 4);
                TickCanvas.Children.Add(dot);
            }
        }

        private void DrawGlowFace()
        {
            // Warm amber glow
            CurrentRing.Fill = new SolidColorBrush(Colors.Transparent);
            CurrentRing.Stroke = new SolidColorBrush(Color.FromArgb(180, 255, 180, 50));
            CurrentRing.StrokeThickness = 3;
            CurrentRing.Effect = new System.Windows.Media.Effects.DropShadowEffect
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
                    Stroke = ParseBrush(_config.MarkerColor, "#FFB432"),
                    StrokeThickness = 3
                };
                TickCanvas.Children.Add(line);
            }
        }

        private void DrawBoldFace()
        {
            CurrentRing.Fill = ParseBrush(_config.ClockFaceColor, "#1A1A1A");
            CurrentRing.Stroke = new SolidColorBrush(Color.FromArgb(80, 59, 130, 246));
            CurrentRing.StrokeThickness = 5;
            CurrentRing.Effect = null;

            for (int i = 0; i < 12; i++)
            {
                double angle = i * 30.0 * Math.PI / 180.0;
                double cx = 140, cy = 140;
                double r1 = 128, r2 = 108;
                var rect = new Rectangle
                {
                    Width = 4, Height = r1 - r2, RadiusX = 2, RadiusY = 2,
                    Fill = ParseBrush(_config.MarkerColor, "#3B82F6"),
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
            CurrentRing.Fill = Brushes.Transparent;
            CurrentRing.Stroke = new SolidColorBrush(Color.FromRgb(212, 175, 55)); // Gold
            CurrentRing.StrokeThickness = 2;
            CurrentRing.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(212, 175, 55), BlurRadius = 15, ShadowDepth = 0, Opacity = 0.5 };

            for (int i = 0; i < 12; i++)
            {
                double angle = i * 30.0 * Math.PI / 180.0;
                double cx = 140, cy = 140, r1 = 125, r2 = 110;
                var line = new Line {
                    X1 = cx + r1 * Math.Sin(angle), Y1 = cy - r1 * Math.Cos(angle),
                    X2 = cx + r2 * Math.Sin(angle), Y2 = cy - r2 * Math.Cos(angle),
                    Stroke = ParseBrush(_config.MarkerColor, "#D4AF37"), StrokeThickness = 1.5
                };
                TickCanvas.Children.Add(line);
            }
        }

        private void DrawSquareFace()
        {
            CurrentRing.Fill = ParseBrush(_config.ClockFaceColor, "#1A1A1A");
            CurrentRing.Stroke = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
            CurrentRing.StrokeThickness = 2;
            CurrentRing.Effect = null;
            
            for (int i = 0; i < 12; i++)
            {
                double angle = i * 30.0 * Math.PI / 180.0;
                double cx = 140, cy = 140, r = 120;
                var rect = new Rectangle { Width = 6, Height = 6, Fill = ParseBrush(_config.MarkerColor, "#FFFFFF"), Opacity = 0.8 };
                Canvas.SetLeft(rect, cx + r * Math.Sin(angle) - 3);
                Canvas.SetTop(rect,  cy - r * Math.Cos(angle) - 3);
                TickCanvas.Children.Add(rect);
            }
        }

        private void DrawTechnoFace()
        {
            // Cyberpunk - radial lines from center + tick marks at edge
            CurrentRing.Stroke = new SolidColorBrush(Color.FromRgb(0, 255, 150));
            CurrentRing.StrokeThickness = 1;
            
            for (int i = 0; i < 12; i++)
            {
                double angle = i * 30.0;
                double cx = 140, cy = 140;
                
                // Tick marks at edge - using Rectangle for perfect radial alignment
                var tick = new Rectangle {
                    Width = 2, Height = 14,
                    Fill = ParseBrush(_config.MarkerColor, "#00FF96"),
                    RenderTransformOrigin = new Point(0.5, 0)
                };
                tick.RenderTransform = new RotateTransform(angle);
                Canvas.SetLeft(tick, cx - 1);
                Canvas.SetTop(tick, cy - 128);
                TickCanvas.Children.Add(tick);

                // Faint radial grid line
                var gridLine = new Rectangle {
                    Width = 1, Height = 120,
                    Fill = new SolidColorBrush(Color.FromArgb(30, 0, 255, 150)),
                    RenderTransformOrigin = new Point(0.5, 0),
                    Opacity = 0.5
                };
                gridLine.RenderTransform = new RotateTransform(angle);
                Canvas.SetLeft(gridLine, cx - 0.5);
                Canvas.SetTop(gridLine, cy - 120);
                TickCanvas.Children.Add(gridLine);
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
                    Text = roman[i], Foreground = ParseBrush(_config.MarkerColor, "#FFFFFF"), FontSize = 14, FontWeight = FontWeights.Bold,
                    TextAlignment = TextAlignment.Center, Width = 30
                };
                Canvas.SetLeft(txt, cx + r * Math.Sin(angle) - 15);
                Canvas.SetTop(txt,  cy - r * Math.Cos(angle) - 10);
                TickCanvas.Children.Add(txt);
            }
        }

        private void DrawGoldFace()
        {
            CurrentRing.Stroke = new SolidColorBrush(Color.FromRgb(255, 215, 0));
            CurrentRing.StrokeThickness = 4;
            for (int i = 0; i < 60; i++)
            {
                if (i % 5 != 0) continue;
                double angle = i * 6.0 * Math.PI / 180.0;
                double cx = 140, cy = 140, r = 125;
                var dot = new Ellipse { Width = 4, Height = 4, Fill = ParseBrush(_config.MarkerColor, "#FFD700") };
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
                    Fill = ParseBrush(_config.MarkerColor, "#FFFFFF"), Opacity = isHour ? 1.0 : 0.4
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
                var dot = new Ellipse { Width = 6, Height = 6, Fill = ParseBrush(_config.MarkerColor, "#3B82F6") };
                Canvas.SetLeft(dot, cx + r * Math.Sin(angle) - 3);
                Canvas.SetTop(dot,  cy - r * Math.Cos(angle) - 3);
                TickCanvas.Children.Add(dot);
            }
        }

        private void DrawIndustrialFace()
        {
            CurrentRing.Stroke = new SolidColorBrush(Color.FromRgb(100, 100, 100));
            CurrentRing.StrokeThickness = 8;
            CurrentRing.StrokeDashArray = new DoubleCollection { 1, 2 };
            for (int i = 0; i < 12; i++)
            {
                double angle = i * 30.0 * Math.PI / 180.0;
                double cx = 140, cy = 140;
                // Properly radial thick tick marks using Line
                var line = new Line {
                    X1 = cx + 125 * Math.Sin(angle), Y1 = cy - 125 * Math.Cos(angle),
                    X2 = cx + 108 * Math.Sin(angle), Y2 = cy - 108 * Math.Cos(angle),
                    Stroke = ParseBrush(_config.MarkerColor, "#808080"), StrokeThickness = 8
                };
                TickCanvas.Children.Add(line);
            }
        }

        private void DrawRetroFace()
        {
            // Orange retro vibe - thick rounded dashes
            for (int i = 0; i < 12; i++)
            {
                double angle = i * 30.0;
                double cx = 140, cy = 140;
                
                var marker = new Rectangle {
                    Width = 5, Height = 20,
                    Fill = ParseBrush(_config.MarkerColor, "#FF6400"),
                    RadiusX = 2.5, RadiusY = 2.5,
                    RenderTransformOrigin = new Point(0.5, 0)
                };
                marker.RenderTransform = new RotateTransform(angle);
                Canvas.SetLeft(marker, cx - 2.5);
                Canvas.SetTop(marker, cy - 128);
                TickCanvas.Children.Add(marker);
            }
        }

        private void DrawFuturaFace()
        {
            CurrentRing.Stroke = Brushes.White;
            CurrentRing.StrokeThickness = 0.5;
            for (int i = 0; i < 4; i++)
            {
                double angle = i * 90.0 * Math.PI / 180.0;
                double cx = 140, cy = 140, r = 120;
                var txt = new TextBlock {
                    Text = (i == 0 ? 12 : i * 3).ToString(), Foreground = ParseBrush(_config.MarkerColor, "#FFFFFF"),
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
            ClockFaceRect.Fill = ParseBrush(_config.ClockFaceColor, "#1A1A1A");
            CurrentRing.Stroke = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
            CurrentRing.StrokeThickness = 1;
            CurrentRing.Effect = null;
            
            for (int i = 0; i < 12; i++)
            {
                double angle = i * 30.0; // Use degrees for RotateTransform
                double cx = 140, cy = 140;
                var line = new Rectangle {
                    Width = 3, Height = 16,
                    Fill = ParseBrush(_config.MarkerColor, "#FFFFFF"), Opacity = 0.9,
                    RenderTransformOrigin = new Point(0.5, 0)
                };
                line.RenderTransform = new RotateTransform(angle);
                Canvas.SetLeft(line, cx - 1.5);
                Canvas.SetTop(line, cy - 126);
                TickCanvas.Children.Add(line);
            }
        }

        private void DrawSquareLuxuryFace()
        {
            // Square face background + gold markers
            ClockFaceRect.Fill = ParseBrush(_config.ClockFaceColor, "#1A1A1A");
            CurrentRing.Stroke = new SolidColorBrush(Color.FromRgb(212, 175, 55));
            CurrentRing.StrokeThickness = 2;
            CurrentRing.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(212, 175, 55), BlurRadius = 15, ShadowDepth = 0, Opacity = 0.5 };
            
            for (int i = 0; i < 12; i++)
            {
                double angle = i * 30.0;
                double cx = 140, cy = 140;
                var line = new Rectangle {
                    Width = 2, Height = 16,
                    Fill = ParseBrush(_config.MarkerColor, "#D4AF25"),
                    RenderTransformOrigin = new Point(0.5, 0)
                };
                line.RenderTransform = new RotateTransform(angle);
                Canvas.SetLeft(line, cx - 1);
                Canvas.SetTop(line, cy - 126);
                TickCanvas.Children.Add(line);
            }
        }

        private void DrawSquareBoldFace()
        {
            ClockFaceRect.Fill = ParseBrush(_config.ClockFaceColor, "#111111");
            CurrentRing.Stroke = new SolidColorBrush(Colors.DimGray);
            CurrentRing.StrokeThickness = 1;
            CurrentRing.Effect = null;

            for (int i = 0; i < 12; i++)
            {
                double angle = i * 30.0;
                double cx = 140, cy = 140;
                var rect = new Rectangle {
                    Width = 6, Height = 22, RadiusX = 3, RadiusY = 3,
                    Fill = ParseBrush(_config.MarkerColor, "#3b82f6"),
                    RenderTransformOrigin = new Point(0.5, 0)
                };
                rect.RenderTransform = new RotateTransform(angle);
                Canvas.SetLeft(rect, cx - 3);
                Canvas.SetTop(rect, cy - 128);
                TickCanvas.Children.Add(rect);
            }
        }

        private void DrawSquareNeonFace()
        {
            ClockFaceRect.Fill = Brushes.Black;
            CurrentRing.Stroke = new SolidColorBrush(Color.FromRgb(255, 0, 100));
            CurrentRing.StrokeThickness = 2;
            CurrentRing.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(255, 0, 100), BlurRadius = 20, ShadowDepth = 0, Opacity = 1 };

            for (int i = 0; i < 12; i++)
            {
                double angle = i * 30.0;
                double cx = 140, cy = 140;
                var dot = new Ellipse {
                    Width = 8, Height = 8,
                    Fill = ParseBrush(_config.MarkerColor, "#FF0064"),
                    Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(255, 0, 100), BlurRadius = 15, ShadowDepth = 0 }
                };
                Canvas.SetLeft(dot, cx + 115 * Math.Sin(angle * Math.PI / 180.0) - 4);
                Canvas.SetTop(dot,  cy - 115 * Math.Cos(angle * Math.PI / 180.0) - 4);
                TickCanvas.Children.Add(dot);
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
