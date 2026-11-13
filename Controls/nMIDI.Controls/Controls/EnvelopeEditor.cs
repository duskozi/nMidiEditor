using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using nMIDI.Controls.Primitives;

namespace nMIDI.Controls;

/// <summary>
/// Graphical ADSR editor. Drag the handles: attack peak (time), decay/sustain corner (time + level),
/// sustain end (level), release end (time). Times are seconds, sustain is 0..1.
/// Double-click a handle to reset its parameter(s) to the default ADSR.
/// </summary>
public class EnvelopeEditor : DawRenderControl
{
    private enum Handle { None = -1, Attack = 0, Decay = 1, Sustain = 2, Release = 3 }

    private const double HandleRadius = 5;
    private const double HitRadius = 9;
    private const double SustainFraction = 0.18; // width reserved for the sustain plateau

    private Handle _hover = Handle.None;
    private Handle _drag = Handle.None;
    private Point _dragStart;
    private double _startA, _startD, _startS, _startR;

    static EnvelopeEditor()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(EnvelopeEditor), new FrameworkPropertyMetadata(typeof(EnvelopeEditor)));
        FocusableProperty.OverrideMetadata(typeof(EnvelopeEditor), new FrameworkPropertyMetadata(true));
    }

    #region Properties

    /// <summary>Identifies the <see cref="Attack"/> dependency property.</summary>
    public static readonly DependencyProperty AttackProperty = RegisterTime(nameof(Attack), 0.01);
    /// <summary>Attack time in seconds.</summary>
    public double Attack { get => (double)GetValue(AttackProperty); set => SetValue(AttackProperty, value); }

    /// <summary>Identifies the <see cref="Decay"/> dependency property.</summary>
    public static readonly DependencyProperty DecayProperty = RegisterTime(nameof(Decay), 0.3);
    /// <summary>Decay time in seconds.</summary>
    public double Decay { get => (double)GetValue(DecayProperty); set => SetValue(DecayProperty, value); }

    /// <summary>Identifies the <see cref="Release"/> dependency property.</summary>
    public static readonly DependencyProperty ReleaseProperty = RegisterTime(nameof(Release), 0.5);
    /// <summary>Release time in seconds.</summary>
    public double Release { get => (double)GetValue(ReleaseProperty); set => SetValue(ReleaseProperty, value); }

    /// <summary>Identifies the <see cref="Sustain"/> dependency property.</summary>
    public static readonly DependencyProperty SustainProperty = DependencyProperty.Register(
        nameof(Sustain), typeof(double), typeof(EnvelopeEditor),
        new FrameworkPropertyMetadata(0.7, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            null, (_, v) => v is double d && !double.IsNaN(d) ? Math.Clamp(d, 0, 1) : 0.0));

    /// <summary>Sustain level, 0 to 1.</summary>
    public double Sustain { get => (double)GetValue(SustainProperty); set => SetValue(SustainProperty, value); }

    private static DependencyProperty RegisterTime(string name, double def) => DependencyProperty.Register(
        name, typeof(double), typeof(EnvelopeEditor),
        new FrameworkPropertyMetadata(def, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            null, CoerceTime));

    private static object CoerceTime(DependencyObject d, object v)
    {
        double max = ((EnvelopeEditor)d).MaxSegmentTime;
        return v is double t && !double.IsNaN(t) ? Math.Clamp(t, 0, max) : 0.0;
    }

    /// <summary>Identifies the <see cref="MaxSegmentTime"/> dependency property.</summary>
    public static readonly DependencyProperty MaxSegmentTimeProperty = DependencyProperty.Register(
        nameof(MaxSegmentTime), typeof(double), typeof(EnvelopeEditor),
        new FrameworkPropertyMetadata(4.0, FrameworkPropertyMetadataOptions.AffectsRender, OnMaxChanged),
        v => v is double d && d > 0 && !double.IsInfinity(d));

    /// <summary>Longest attack/decay/release (seconds) – the full width of one segment lane.</summary>
    public double MaxSegmentTime { get => (double)GetValue(MaxSegmentTimeProperty); set => SetValue(MaxSegmentTimeProperty, value); }

    private static void OnMaxChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        d.CoerceValue(AttackProperty);
        d.CoerceValue(DecayProperty);
        d.CoerceValue(ReleaseProperty);
    }

    /// <summary>Identifies the <see cref="Curvature"/> dependency property.</summary>
    public static readonly DependencyProperty CurvatureProperty = DependencyProperty.Register(
        nameof(Curvature), typeof(double), typeof(EnvelopeEditor),
        new FrameworkPropertyMetadata(0.6, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>0 = straight lines, 1 = strongly exponential (analog-style) segments. Display only.</summary>
    public double Curvature { get => (double)GetValue(CurvatureProperty); set => SetValue(CurvatureProperty, value); }

    /// <summary>Identifies the <see cref="TimeScale"/> dependency property.</summary>
    public static readonly DependencyProperty TimeScaleProperty = DependencyProperty.Register(
        nameof(TimeScale), typeof(ValueTaper), typeof(EnvelopeEditor),
        new FrameworkPropertyMetadata(new SquareTaper(), FrameworkPropertyMetadataOptions.AffectsRender), v => v is ValueTaper);

    /// <summary>
    /// How time maps to segment width. Default is a square-law curve so short attacks (1–50 ms) get usable
    /// resolution while multi-second releases still fit.
    /// </summary>
    public ValueTaper TimeScale { get => (ValueTaper)GetValue(TimeScaleProperty); set => SetValue(TimeScaleProperty, value); }

    /// <summary>Identifies the <see cref="StrokeBrush"/> dependency property.</summary>
    public static readonly DependencyProperty StrokeBrushProperty = RegisterBrush(nameof(StrokeBrush), typeof(EnvelopeEditor));
    /// <summary>Brush for the curve.</summary>
    public Brush? StrokeBrush { get => (Brush?)GetValue(StrokeBrushProperty); set => SetValue(StrokeBrushProperty, value); }

    /// <summary>Identifies the <see cref="FillBrush"/> dependency property.</summary>
    public static readonly DependencyProperty FillBrushProperty = RegisterBrush(nameof(FillBrush), typeof(EnvelopeEditor));
    /// <summary>Brush for the area under the values or the curve.</summary>
    public Brush? FillBrush { get => (Brush?)GetValue(FillBrushProperty); set => SetValue(FillBrushProperty, value); }

    /// <summary>Identifies the <see cref="HandleBrush"/> dependency property.</summary>
    public static readonly DependencyProperty HandleBrushProperty = RegisterBrush(nameof(HandleBrush), typeof(EnvelopeEditor));
    /// <summary>Brush for the drag handles.</summary>
    public Brush? HandleBrush { get => (Brush?)GetValue(HandleBrushProperty); set => SetValue(HandleBrushProperty, value); }

    /// <summary>Identifies the <see cref="GridBrush"/> dependency property.</summary>
    public static readonly DependencyProperty GridBrushProperty = RegisterBrush(nameof(GridBrush), typeof(EnvelopeEditor));
    /// <summary>Brush for the grid lines.</summary>
    public Brush? GridBrush { get => (Brush?)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }

    /// <summary>Identifies the <see cref="DragStarted"/> routed event.</summary>
    public static readonly RoutedEvent DragStartedEvent = DawRangeControl.DragStartedEvent.AddOwner(typeof(EnvelopeEditor));
    /// <summary>Raised when the user starts dragging a handle.</summary>
    public event RoutedEventHandler DragStarted { add => AddHandler(DragStartedEvent, value); remove => RemoveHandler(DragStartedEvent, value); }

    /// <summary>Identifies the <see cref="DragCompleted"/> routed event.</summary>
    public static readonly RoutedEvent DragCompletedEvent = DawRangeControl.DragCompletedEvent.AddOwner(typeof(EnvelopeEditor));
    /// <summary>Raised when the user releases a handle.</summary>
    public event RoutedEventHandler DragCompleted { add => AddHandler(DragCompletedEvent, value); remove => RemoveHandler(DragCompletedEvent, value); }

    #endregion

    #region Geometry

    private Rect Plot
    {
        get
        {
            const double pad = HandleRadius + 4;
            return new Rect(pad, pad + 12, Math.Max(0, ActualWidth - 2 * pad), Math.Max(0, ActualHeight - 2 * pad - 12));
        }
    }

    private static double SegmentWidth(Rect plot) => plot.Width * (1 - SustainFraction) / 3;

    private double TimeToWidth(double t, Rect plot) => TimeScale.ToNormalized(t, 0, MaxSegmentTime) * SegmentWidth(plot);

    private double WidthToTime(double w, Rect plot)
    {
        double seg = SegmentWidth(plot);
        return seg <= 0 ? 0 : TimeScale.FromNormalized(w / seg, 0, MaxSegmentTime);
    }

    private (Point start, Point a, Point d, Point s, Point r) Points(Rect plot)
    {
        double sy = plot.Bottom - Sustain * plot.Height;
        var start = new Point(plot.Left, plot.Bottom);
        var a = new Point(plot.Left + TimeToWidth(Attack, plot), plot.Top);
        var d = new Point(a.X + TimeToWidth(Decay, plot), sy);
        var s = new Point(d.X + plot.Width * SustainFraction, sy);
        var r = new Point(s.X + TimeToWidth(Release, plot), plot.Bottom);
        return (start, a, d, s, r);
    }

    private Handle HitTestHandle(Point p)
    {
        var (_, a, d, s, r) = Points(Plot);
        Point[] pts = { a, d, s, r };
        Handle best = Handle.None;
        double bestDist = HitRadius;
        for (int i = 0; i < pts.Length; i++)
        {
            double dist = (pts[i] - p).Length;
            if (dist <= bestDist) { bestDist = dist; best = (Handle)i; }
        }
        return best;
    }

    #endregion

    #region Rendering

    /// <inheritdoc/>
    protected override void OnRender(DrawingContext dc)
    {
        var snap = new PixelSnapper(this);
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        dc.DrawRoundedRectangle(Background ?? Brushes.Transparent, FrozenPens.Create(BorderBrush, snap.OnePixel), bounds, 3, 3);

        Rect plot = Plot;
        if (plot.Width <= 0 || plot.Height <= 0) return;
        var (p0, pa, pd, ps, pr) = Points(plot);

        if (FrozenPens.Create(GridBrush, snap.OnePixel) is { } grid)
        {
            for (int i = 0; i <= 4; i++)
            {
                double y = snap.LineY(plot.Top + plot.Height * i / 4);
                dc.DrawLine(grid, new Point(plot.Left, y), new Point(plot.Right, y));
            }
            foreach (Point x in new[] { pa, pd, ps })
                dc.DrawLine(grid, new Point(snap.LineX(x.X), plot.Top), new Point(snap.LineX(x.X), plot.Bottom));
        }

        double k = Math.Clamp(Curvature, 0, 1);
        var geo = new StreamGeometry();
        using (StreamGeometryContext ctx = geo.Open())
        {
            ctx.BeginFigure(p0, true, false);
            ctx.QuadraticBezierTo(Control(p0, pa, k * 0.6), pa, true, true); // attack: convex rise
            ctx.QuadraticBezierTo(Control(pa, pd, k), pd, true, true);       // decay: exponential fall
            ctx.LineTo(ps, true, true);                                     // sustain plateau
            ctx.QuadraticBezierTo(Control(ps, pr, k), pr, true, true);       // release
        }
        geo.Freeze();

        if (FillBrush is { } fill)
        {
            var area = new StreamGeometry();
            using (StreamGeometryContext ctx = area.Open())
            {
                ctx.BeginFigure(p0, true, true);
                ctx.QuadraticBezierTo(Control(p0, pa, k * 0.6), pa, false, true);
                ctx.QuadraticBezierTo(Control(pa, pd, k), pd, false, true);
                ctx.LineTo(ps, false, true);
                ctx.QuadraticBezierTo(Control(ps, pr, k), pr, false, true);
            }
            area.Freeze();
            dc.DrawGeometry(fill, null, area);
        }

        if (StrokeBrush is { } stroke)
        {
            var pen = new Pen(stroke, 2) { LineJoin = PenLineJoin.Round };
            pen.Freeze();
            dc.DrawGeometry(null, pen, geo);
        }

        if (HandleBrush is { } hb)
        {
            Pen? outline = FrozenPens.Create(Background, 2);
            Point[] pts = { pa, pd, ps, pr };
            for (int i = 0; i < pts.Length; i++)
            {
                bool active = (Handle)i == _drag || (_drag == Handle.None && (Handle)i == _hover);
                double radius = active ? HandleRadius + 1.5 : HandleRadius;
                dc.DrawEllipse(hb, outline, pts[i], radius, radius);
            }
        }

        // Readout of the parameter being edited (or a compact summary).
        string text = _drag switch
        {
            Handle.Attack => "Attack " + ValueFormatters.Time(Attack),
            Handle.Decay => $"Decay {ValueFormatters.Time(Decay)}  Sustain {ValueFormatters.Percent(Sustain)}",
            Handle.Sustain => "Sustain " + ValueFormatters.Percent(Sustain),
            Handle.Release => "Release " + ValueFormatters.Time(Release),
            _ => $"A {ValueFormatters.Time(Attack)}  D {ValueFormatters.Time(Decay)}  S {ValueFormatters.Percent(Sustain)}  R {ValueFormatters.Time(Release)}",
        };
        dc.DrawText(MakeText(text, snap.PixelsPerDip), new Point(plot.Left, 3));
    }

    /// <summary>Quadratic control point: k=0 straight, k=1 bends toward the (p0.X, p1.Y) corner.</summary>
    private static Point Control(Point p0, Point p1, double k)
    {
        var mid = new Point((p0.X + p1.X) / 2, (p0.Y + p1.Y) / 2);
        var corner = new Point(p0.X, p1.Y);
        return new Point(mid.X + (corner.X - mid.X) * k, mid.Y + (corner.Y - mid.Y) * k);
    }

    #endregion

    #region Input

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Point p = e.GetPosition(this);
        if (_drag == Handle.None)
        {
            Handle h = HitTestHandle(p);
            if (h != _hover)
            {
                _hover = h;
                SetCurrentValue(CursorProperty, h == Handle.None ? null : Cursors.Hand);
                InvalidateVisual();
            }
            return;
        }

        Rect plot = Plot;
        double fine = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 0.1 : 1;
        Vector delta = (p - _dragStart) * fine;

        double TimeAfter(double start) => WidthToTime(TimeToWidth(start, plot) + delta.X, plot);

        switch (_drag)
        {
            case Handle.Attack:
                SetCurrentValue(AttackProperty, TimeAfter(_startA));
                break;
            case Handle.Decay:
                SetCurrentValue(DecayProperty, TimeAfter(_startD));
                SetCurrentValue(SustainProperty, _startS - delta.Y / Math.Max(1, plot.Height));
                break;
            case Handle.Sustain:
                SetCurrentValue(SustainProperty, _startS - delta.Y / Math.Max(1, plot.Height));
                break;
            case Handle.Release:
                SetCurrentValue(ReleaseProperty, TimeAfter(_startR));
                break;
        }
    }

    /// <inheritdoc/>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        Point p = e.GetPosition(this);
        Handle h = HitTestHandle(p);
        if (h == Handle.None) return;

        if (e.ClickCount == 2)
        {
            switch (h)
            {
                // SetCurrentValue (not ClearValue) so data bindings survive the reset.
                case Handle.Attack: ResetToDefault(AttackProperty); break;
                case Handle.Decay: ResetToDefault(DecayProperty); ResetToDefault(SustainProperty); break;
                case Handle.Sustain: ResetToDefault(SustainProperty); break;
                case Handle.Release: ResetToDefault(ReleaseProperty); break;
            }
            e.Handled = true;
            return;
        }

        if (!CaptureMouse()) return;
        _drag = h;
        _dragStart = p;
        (_startA, _startD, _startS, _startR) = (Attack, Decay, Sustain, Release);
        RaiseEvent(new RoutedEventArgs(DragStartedEvent, this));
        InvalidateVisual();
        e.Handled = true;
    }

    private void ResetToDefault(DependencyProperty dp) => SetCurrentValue(dp, dp.GetMetadata(this).DefaultValue);

    /// <inheritdoc/>
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    /// <inheritdoc/>
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_drag == Handle.None) return;
        _drag = Handle.None;
        InvalidateVisual();
        RaiseEvent(new RoutedEventArgs(DragCompletedEvent, this));
    }

    /// <inheritdoc/>
    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_drag == Handle.None && _hover != Handle.None)
        {
            _hover = Handle.None;
            SetCurrentValue(CursorProperty, null);
            InvalidateVisual();
        }
    }

    #endregion

    /// <summary>n² mapping: fine resolution for short times.</summary>
    private sealed class SquareTaper : ValueTaper
    {
        public override double ToNormalized(double value, double minimum, double maximum)
            => Math.Sqrt(Linear.ToNormalized(value, minimum, maximum));

        public override double FromNormalized(double normalized, double minimum, double maximum)
        {
            double n = Clamp01(normalized);
            return Linear.FromNormalized(n * n, minimum, maximum);
        }
    }
}
