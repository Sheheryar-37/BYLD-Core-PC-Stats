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

        // LEFT ARC: Starts at bottom-left (135 deg) and goes clockwise up to top (270 deg). 
        // Total span = 135 degrees.
        // We want it to fill from top to bottom (like the image, the gap is at top-left, and it fills downwards? 
        // Wait, looking at the image, left arc usually starts from top (225 degrees approx?) and sweeps down?
        // Actually, the client image shows:
        // GPU TEMP: Left arc is nearly full. It starts at top-center and goes counter-clockwise? Or bottom-center goes clockwise?
        // In the mockups, both arcs seem to emerge from the bottom gap or top gap? 
        // "guage start from middle right and middle left , the midle right goes upward showing load, and left one goes downward shoeing temp"
        // Ah! Left starts at middle-left (180 degrees) and goes downward (counter-clockwise) to bottom-left (135 degrees)? No, 180 to 90 is downward.
        // Let's make Left arc start at 180 deg (middle left) and sweep DOWN to 90 deg. (Span = 90 deg).
        // Let's make Right arc start at 0 deg (middle right) and sweep UP to -90/270 deg. (Span = 90 deg).

        // LEFT ARC: Middle Left (180 deg) going DOWN (Counter-Clockwise) to Bottom Center (90 deg). Span = 90
        double leftSpan = Math.Min(100, Math.Max(0, LeftValue)) / 100.0 * 90.0;
        UpdateArc(LeftValuePath, center, radius, 180, 180 - leftSpan, SweepDirection.Counterclockwise);

        // RIGHT ARC: Middle Right (0 deg = 360 deg) going UP (Counter-Clockwise) to Top Center (270 deg). Span = 90
        double rightSpan = Math.Min(100, Math.Max(0, RightValue)) / 100.0 * 90.0;
        UpdateArc(RightValuePath, center, radius, 360, 360 - rightSpan, SweepDirection.Counterclockwise);
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
