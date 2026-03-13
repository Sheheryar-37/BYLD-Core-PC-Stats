using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Point = System.Windows.Point;
using UserControl = System.Windows.Controls.UserControl;

namespace PcStatsMonitor.Controls;

public partial class DualCircularGauge : UserControl
{
    public DualCircularGauge()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty LeftValueProperty = DependencyProperty.Register(
        "LeftValue", typeof(double), typeof(DualCircularGauge), new PropertyMetadata(0.0, OnValueChanged));

    // Expected to be 0-100
    public double LeftValue
    {
        get => (double)GetValue(LeftValueProperty);
        set => SetValue(LeftValueProperty, value);
    }
    
    public static readonly DependencyProperty RightValueProperty = DependencyProperty.Register(
        "RightValue", typeof(double), typeof(DualCircularGauge), new PropertyMetadata(0.0, OnValueChanged));

    // Expected to be 0-100
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

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var gauge = (DualCircularGauge)d;
        gauge.UpdateArcs();
    }
    
    private void UpdateArcs()
    {
        if (LeftValuePath == null || RightValuePath == null) return;
        
        var radius = 106.0;
        var center = new Point(125, 125); 

        // 0 = Right (East). Measurement is anticlockwise (decreasing angle in WPF polar space).
        
        // LEFT ARC: 140 to 325 CCW. Span = 185 deg.
        // Start -140, sweep CCW (decrease angle) by lSpan.
        double lSpan = Math.Min(100, Math.Max(0, LeftValue)) / 100.0 * 185.0;
        UpdateArc(LeftValuePath, center, radius, -140, -140 - lSpan, SweepDirection.Counterclockwise);

        // RIGHT ARC: 0 to 130 CCW. Span = 130 deg.
        // Start 0, sweep CCW (decrease angle) by rSpan.
        double rSpan = Math.Min(100, Math.Max(0, RightValue)) / 100.0 * 130.0;
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
