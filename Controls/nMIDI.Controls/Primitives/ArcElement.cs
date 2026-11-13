using System;
using System.Windows;
using System.Windows.Media;

namespace nMIDI.Controls.Primitives;

/// <summary>
/// Draws a circular arc between two normalized positions of a sweep (used by knob templates
/// for the track and the value ring). Angles are in degrees, 0 = 12 o'clock, clockwise positive.
/// </summary>
public class ArcElement : FrameworkElement
{
    /// <summary>Identifies the <see cref="Stroke"/> dependency property.</summary>
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(ArcElement),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnPenChanged));

    /// <summary>Brush of the arc.</summary>
    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>Identifies the <see cref="Thickness"/> dependency property.</summary>
    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(ArcElement),
        new FrameworkPropertyMetadata(3.0, FrameworkPropertyMetadataOptions.AffectsRender, OnPenChanged),
        v => v is double d && d >= 0 && !double.IsInfinity(d));

    /// <summary>Width of the arc in pixels.</summary>
    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    /// <summary>Identifies the <see cref="LineCap"/> dependency property.</summary>
    public static readonly DependencyProperty LineCapProperty = DependencyProperty.Register(
        nameof(LineCap), typeof(PenLineCap), typeof(ArcElement),
        new FrameworkPropertyMetadata(PenLineCap.Round, FrameworkPropertyMetadataOptions.AffectsRender, OnPenChanged));

    /// <summary>Shape of the arc's ends.</summary>
    public PenLineCap LineCap
    {
        get => (PenLineCap)GetValue(LineCapProperty);
        set => SetValue(LineCapProperty, value);
    }

    /// <summary>Identifies the <see cref="MinAngle"/> dependency property.</summary>
    public static readonly DependencyProperty MinAngleProperty = DependencyProperty.Register(
        nameof(MinAngle), typeof(double), typeof(ArcElement),
        new FrameworkPropertyMetadata(-135.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Angle in degrees where the arc range starts (0 = up, clockwise).</summary>
    public double MinAngle
    {
        get => (double)GetValue(MinAngleProperty);
        set => SetValue(MinAngleProperty, value);
    }

    /// <summary>Identifies the <see cref="MaxAngle"/> dependency property.</summary>
    public static readonly DependencyProperty MaxAngleProperty = DependencyProperty.Register(
        nameof(MaxAngle), typeof(double), typeof(ArcElement),
        new FrameworkPropertyMetadata(135.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Angle in degrees where the arc range ends (0 = up, clockwise).</summary>
    public double MaxAngle
    {
        get => (double)GetValue(MaxAngleProperty);
        set => SetValue(MaxAngleProperty, value);
    }

    /// <summary>Identifies the <see cref="From"/> dependency property.</summary>
    public static readonly DependencyProperty FromProperty = DependencyProperty.Register(
        nameof(From), typeof(double), typeof(ArcElement),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Normalized start position in the MinAngle..MaxAngle sweep.</summary>
    public double From
    {
        get => (double)GetValue(FromProperty);
        set => SetValue(FromProperty, value);
    }

    /// <summary>Identifies the <see cref="To"/> dependency property.</summary>
    public static readonly DependencyProperty ToProperty = DependencyProperty.Register(
        nameof(To), typeof(double), typeof(ArcElement),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Normalized end position in the MinAngle..MaxAngle sweep.</summary>
    public double To
    {
        get => (double)GetValue(ToProperty);
        set => SetValue(ToProperty, value);
    }

    private Pen? _pen;

    private static void OnPenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((ArcElement)d)._pen = null;

    /// <inheritdoc/>
    protected override void OnRender(DrawingContext dc)
    {
        Brush? stroke = Stroke;
        double thickness = Thickness;
        if (stroke is null || thickness <= 0) return;

        double radius = Math.Min(ActualWidth, ActualHeight) / 2 - thickness / 2;
        if (radius <= 0) return;

        double from = Math.Clamp(Math.Min(From, To), 0, 1);
        double to = Math.Clamp(Math.Max(From, To), 0, 1);
        double a0 = MinAngle + (MaxAngle - MinAngle) * from;
        double a1 = MinAngle + (MaxAngle - MinAngle) * to;
        double sweep = a1 - a0;
        if (Math.Abs(sweep) < 0.05) return;

        _pen ??= CreatePen(stroke, thickness, LineCap);
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        dc.DrawGeometry(null, _pen, CreateArc(center, radius, a0, a1));
    }

    private static Pen CreatePen(Brush brush, double thickness, PenLineCap cap)
    {
        var pen = new Pen(brush, thickness) { StartLineCap = cap, EndLineCap = cap };
        if (pen.CanFreeze) pen.Freeze();
        return pen;
    }

    /// <summary>Builds an arc geometry. Sweeps ≥ 360° are drawn as two half arcs.</summary>
    public static Geometry CreateArc(Point center, double radius, double startDeg, double endDeg)
    {
        var geo = new StreamGeometry();
        using (StreamGeometryContext ctx = geo.Open())
        {
            double sweep = endDeg - startDeg;
            ctx.BeginFigure(PointOnCircle(center, radius, startDeg), false, false);
            if (Math.Abs(sweep) >= 359.9)
            {
                double mid = startDeg + sweep / 2;
                var dir = sweep > 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise;
                ctx.ArcTo(PointOnCircle(center, radius, mid), new Size(radius, radius), 0, false, dir, true, false);
                ctx.ArcTo(PointOnCircle(center, radius, endDeg - Math.Sign(sweep) * 0.01), new Size(radius, radius), 0, false, dir, true, false);
            }
            else
            {
                ctx.ArcTo(PointOnCircle(center, radius, endDeg), new Size(radius, radius), 0,
                    Math.Abs(sweep) > 180, sweep > 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true, false);
            }
        }
        geo.Freeze();
        return geo;
    }

    /// <summary>The point on a circle at an angle in degrees (0 = up, clockwise).</summary>
    public static Point PointOnCircle(Point center, double radius, double angleDeg)
    {
        double rad = angleDeg * Math.PI / 180.0;
        return new Point(center.X + radius * Math.Sin(rad), center.Y - radius * Math.Cos(rad));
    }
}
