using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PcStatsMonitor.Controls
{
    public partial class CircularSlider : UserControl
    {
        private bool _isDragging = false;
        private const double Radius = 70;
        private const double CenterX = 100; // 80 + 20 margin
        private const double CenterY = 100;
        private const double StartAngle = 135; // Bottom left
        private const double EndAngle = 45;   // Bottom right

        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register("Value", typeof(double), typeof(CircularSlider),
                new PropertyMetadata(0.0, OnValueChanged));

        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register("Title", typeof(string), typeof(CircularSlider),
                new PropertyMetadata("Fan", (d,e) => ((CircularSlider)d).TxtTitle.Text = (string)e.NewValue));

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }
        
        public static readonly DependencyProperty ColorStyleProperty =
            DependencyProperty.Register("ColorStyle", typeof(Brush), typeof(CircularSlider),
                new PropertyMetadata(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#d97746")), 
                (d,e) => ((CircularSlider)d).ActiveArc.Stroke = (Brush)e.NewValue));

        public Brush ColorStyle
        {
            get => (Brush)GetValue(ColorStyleProperty);
            set => SetValue(ColorStyleProperty, value);
        }

        public event RoutedEventHandler ValueChanged;

        public CircularSlider()
        {
            InitializeComponent();
            UpdateVisuals();
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CircularSlider slider)
            {
                slider.UpdateVisuals();
                slider.ValueChanged?.Invoke(slider, new RoutedEventArgs());
            }
        }

        private void UpdateVisuals()
        {
            double val = Math.Max(0, Math.Min(100, Value));
            TxtValue.Text = $"{Math.Round(val)}%";

            // Calculate angle spanning from StartAngle to EndAngle (spanning 270 degrees total)
            double totalDegrees = 270;
            double currentAngle = StartAngle + (totalDegrees * (val / 100.0));
            if (currentAngle >= 360) currentAngle -= 360;

            var point = GetPointFromAngle(currentAngle);
            
            ActiveArcSegment.Point = point;
            // 270 degrees total. 180 degrees falls exactly at 66.6667% (since 180/270 = 0.6666...)
            ActiveArcSegment.IsLargeArc = (val > 66.6667);

            Canvas.SetLeft(Thumb, point.X - 10); // Offset by half width
            Canvas.SetTop(Thumb, point.Y - 10);
        }

        private Point GetPointFromAngle(double angleDegrees)
        {
            double rad = angleDegrees * Math.PI / 180.0;
            double x = CenterX + (Radius * Math.Cos(rad));
            double y = CenterY + (Radius * Math.Sin(rad));
            return new Point(x, y);
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            Mouse.Capture(this);
            UpdateFromMouse(e.GetPosition(this));
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                UpdateFromMouse(e.GetPosition(this));
            }
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            Mouse.Capture(null);
        }

        private void UpdateFromMouse(Point p)
        {
            // Viewbox scales, so we need relative coordinates to center
            double dx = p.X - (ActualWidth / 2);
            double dy = p.Y - (ActualHeight / 2);
            double angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
            if (angle < 0) angle += 360;

            // Map angle back to 0-100%
            double normalizedAngle = angle - StartAngle;
            if (normalizedAngle < 0) normalizedAngle += 360;

            if (normalizedAngle > 270)
            {
                // Snap to min/max if pulled down past the gap
                if (normalizedAngle < 315) Value = 100;
                else Value = 0;
            }
            else
            {
                Value = (normalizedAngle / 270.0) * 100.0;
            }
        }
    }
}
