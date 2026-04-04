using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Point = System.Windows.Point;
using UserControl = System.Windows.Controls.UserControl;
namespace PcStatsMonitor.Controls;

public partial class CircularGauge : UserControl
{
    // The actual value being animated to — separate from the dependency property
    private double _animatedValue = 0;

    public CircularGauge()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        "Value", typeof(double), typeof(CircularGauge), new PropertyMetadata(0.0, OnValueChanged));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public static readonly DependencyProperty ValueStringProperty = DependencyProperty.Register(
        "ValueString", typeof(string), typeof(CircularGauge), new PropertyMetadata("", OnValueStringChanged));

    public string ValueString
    {
        get => (string)GetValue(ValueStringProperty);
        set => SetValue(ValueStringProperty, value);
    }

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        "Title", typeof(string), typeof(CircularGauge), new PropertyMetadata("", OnTitleChanged));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    // Animated internal property that the arc actually tracks
    private static readonly DependencyProperty AnimatedValueProperty = DependencyProperty.Register(
        "AnimatedValue", typeof(double), typeof(CircularGauge), new PropertyMetadata(0.0, OnAnimatedValueChanged));

    private double AnimatedValue
    {
        get => (double)GetValue(AnimatedValueProperty);
        set => SetValue(AnimatedValueProperty, value);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var gauge = (CircularGauge)d;
        double newVal = (double)e.NewValue;
        double oldVal = gauge.AnimatedValue;

        // Animate the internal value — the arc redraws on each frame via OnAnimatedValueChanged
        var animation = new DoubleAnimation(oldVal, newVal, new Duration(TimeSpan.FromMilliseconds(80)))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        gauge.BeginAnimation(AnimatedValueProperty, animation);
    }

    private static void OnAnimatedValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((CircularGauge)d).UpdateArc((double)e.NewValue);
    }

    private static void OnValueStringChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var gauge = (CircularGauge)d;
        if (gauge.ValueText != null) gauge.ValueText.Text = e.NewValue as string;
    }

    private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var gauge = (CircularGauge)d;
        if (gauge.TitleText != null) gauge.TitleText.Text = e.NewValue as string;
    }

    private void UpdateArc(double value)
    {
        if (ValuePath == null) return;
        
        // Value is treated as percentage 0-100.
        var angle = (value / 100.0) * 270.0; // The gauge spans about 270 degrees in the design
        
        var radius = 110.0;
        var center = new Point(125, 125); // Based on 250x250 canvas
        
        // Start angle at 135 degrees (bottom left)
        var startAngle = 135.0 * Math.PI / 180.0;
        var endAngle = (135.0 + angle) * Math.PI / 180.0;

        var startPoint = new Point(
            center.X + radius * Math.Cos(startAngle),
            center.Y + radius * Math.Sin(startAngle));

        var endPoint = new Point(
            center.X + radius * Math.Cos(endAngle),
            center.Y + radius * Math.Sin(endAngle));

        var isLargeArc = angle > 180.0;

        var size = new Size(radius, radius);

        var segment = new ArcSegment(endPoint, size, 0, isLargeArc, SweepDirection.Clockwise, true);
        var figure = new PathFigure(startPoint, new[] { segment }, false);
        var geometry = new PathGeometry(new[] { figure });

        ValuePath.Data = geometry;
    }
}
