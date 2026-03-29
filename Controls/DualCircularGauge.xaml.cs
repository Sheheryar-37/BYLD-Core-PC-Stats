using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Point = System.Windows.Point;
using UserControl = System.Windows.Controls.UserControl;

namespace PcStatsMonitor.Controls;

public partial class DualCircularGauge : UserControl
{
    public DualCircularGauge()
    {
        InitializeComponent();
    }

    // ── Public binding properties ──────────────────────────────────────────────

    public static readonly DependencyProperty LeftValueProperty = DependencyProperty.Register(
        "LeftValue", typeof(double), typeof(DualCircularGauge), new PropertyMetadata(0.0, OnLeftValueChanged));

    public double LeftValue
    {
        get => (double)GetValue(LeftValueProperty);
        set => SetValue(LeftValueProperty, value);
    }

    public static readonly DependencyProperty RightValueProperty = DependencyProperty.Register(
        "RightValue", typeof(double), typeof(DualCircularGauge), new PropertyMetadata(0.0, OnRightValueChanged));

    public double RightValue
    {
        get => (double)GetValue(RightValueProperty);
        set => SetValue(RightValueProperty, value);
    }

    public static readonly DependencyProperty CenterTextStringProperty = DependencyProperty.Register(
        "CenterTextString", typeof(string), typeof(DualCircularGauge), new PropertyMetadata("", (d, e) => ((DualCircularGauge)d).CenterText.Text = e.NewValue as string));

    public string CenterTextString
    {
        get => (string)GetValue(CenterTextStringProperty);
        set => SetValue(CenterTextStringProperty, value);
    }

    public static readonly DependencyProperty CenterLabelStringProperty = DependencyProperty.Register(
        "CenterLabelString", typeof(string), typeof(DualCircularGauge), new PropertyMetadata("", (d, e) => ((DualCircularGauge)d).CenterLabel.Text = e.NewValue as string));

    public string CenterLabelString
    {
        get => (string)GetValue(CenterLabelStringProperty);
        set => SetValue(CenterLabelStringProperty, value);
    }

    public static readonly DependencyProperty RightValueTextStringProperty = DependencyProperty.Register(
        "RightValueTextString", typeof(string), typeof(DualCircularGauge), new PropertyMetadata("", (d, e) => ((DualCircularGauge)d).RightValueText.Text = e.NewValue as string));

    public string RightValueTextString
    {
        get => (string)GetValue(RightValueTextStringProperty);
        set => SetValue(RightValueTextStringProperty, value);
    }

    public static readonly DependencyProperty RightLabelTextStringProperty = DependencyProperty.Register(
        "RightLabelTextString", typeof(string), typeof(DualCircularGauge), new PropertyMetadata("", (d, e) => ((DualCircularGauge)d).RightLabelText.Text = e.NewValue as string));

    public string RightLabelTextString
    {
        get => (string)GetValue(RightLabelTextStringProperty);
        set => SetValue(RightLabelTextStringProperty, value);
    }

    // ── Animated internal properties (the arc actually tracks these) ───────────

    private static readonly DependencyProperty AnimatedLeftValueProperty = DependencyProperty.Register(
        "AnimatedLeftValue", typeof(double), typeof(DualCircularGauge),
        new PropertyMetadata(0.0, (d, e) => ((DualCircularGauge)d).RedrawLeft((double)e.NewValue)));

    private double AnimatedLeftValue
    {
        get => (double)GetValue(AnimatedLeftValueProperty);
        set => SetValue(AnimatedLeftValueProperty, value);
    }

    private static readonly DependencyProperty AnimatedRightValueProperty = DependencyProperty.Register(
        "AnimatedRightValue", typeof(double), typeof(DualCircularGauge),
        new PropertyMetadata(0.0, (d, e) => ((DualCircularGauge)d).RedrawRight((double)e.NewValue)));

    private double AnimatedRightValue
    {
        get => (double)GetValue(AnimatedRightValueProperty);
        set => SetValue(AnimatedRightValueProperty, value);
    }

    // ── Animation triggers ─────────────────────────────────────────────────────

    private static readonly Duration AnimDuration = new Duration(TimeSpan.FromMilliseconds(80));
    private static readonly IEasingFunction Ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };

    private static void OnLeftValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var g = (DualCircularGauge)d;
        var anim = new DoubleAnimation(g.AnimatedLeftValue, (double)e.NewValue, AnimDuration) { EasingFunction = Ease };
        g.BeginAnimation(AnimatedLeftValueProperty, anim);
    }

    private static void OnRightValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var g = (DualCircularGauge)d;
        var anim = new DoubleAnimation(g.AnimatedRightValue, (double)e.NewValue, AnimDuration) { EasingFunction = Ease };
        g.BeginAnimation(AnimatedRightValueProperty, anim);
    }

    // ── Arc drawing ────────────────────────────────────────────────────────────

    private void RedrawLeft(double value)
    {
        if (LeftValuePath == null) return;
        var radius = 106.0;
        var center = new Point(125, 125);
        double lSpan = Math.Min(100, Math.Max(0, value)) / 100.0 * 185.0;
        UpdateArc(LeftValuePath, center, radius, -140, -140 - lSpan, SweepDirection.Counterclockwise);
    }

    private void RedrawRight(double value)
    {
        if (RightValuePath == null) return;
        var radius = 106.0;
        var center = new Point(125, 125);
        double rSpan = Math.Min(100, Math.Max(0, value)) / 100.0 * 130.0;
        UpdateArc(RightValuePath, center, radius, 0, 0 - rSpan, SweepDirection.Counterclockwise);
    }

    private void UpdateArc(System.Windows.Shapes.Path path, Point center, double radius, double startAngleDeg, double endAngleDeg, SweepDirection dir)
    {
        if (Math.Abs(startAngleDeg - endAngleDeg) < 0.1)
        {
            path.Data = null;
            return;
        }

        var startAngle = startAngleDeg * Math.PI / 180.0;
        var endAngle = endAngleDeg * Math.PI / 180.0;

        var startPoint = new Point(
            center.X + radius * Math.Cos(startAngle),
            center.Y + radius * Math.Sin(startAngle));

        var endPoint = new Point(
            center.X + radius * Math.Cos(endAngle),
            center.Y + radius * Math.Sin(endAngle));

        var isLargeArc = Math.Abs(startAngleDeg - endAngleDeg) > 180.0;

        var segment = new ArcSegment(endPoint, new Size(radius, radius), 0, isLargeArc, dir, true);
        var figure = new PathFigure(startPoint, new[] { segment }, false);
        path.Data = new PathGeometry(new[] { figure });
    }
}
