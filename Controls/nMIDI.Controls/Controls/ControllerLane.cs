using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using nMIDI.Controls.Models;
using nMIDI.Controls.Primitives;

namespace nMIDI.Controls;

/// <summary>What a <see cref="ControllerLane"/> shows and edits.</summary>
public enum LaneMode
{
    /// <summary>Edits the velocity of the piano roll's notes (stalks at each note start).</summary>
    Velocity,
    /// <summary>Edits a list of 0..127 controller points (CC number is informational).</summary>
    Controller,
    /// <summary>Edits a list of -8192..8191 pitch-bend points, drawn from the centre line.</summary>
    PitchBend,
}

/// <summary>
/// Velocity / CC / pitch-bend editor that sits under a <see cref="PianoRoll"/> and follows its scroll, zoom,
/// snap and playhead (<c>Roll="{Binding ElementName=MyRoll}"</c>).
/// <para><b>Velocity</b>: drag across stalks to paint velocities. If notes are selected only those are edited.</para>
/// <para><b>CC / pitch bend</b>: drag to draw (points every <see cref="PaintStep"/> beats), right-drag to erase,
/// Shift+drag draws a straight line from the press point, Alt = no snap.</para>
/// </summary>
public class ControllerLane : DawRenderControl
{
    private readonly DrawingVisual _playheadLayer = new();
    private readonly HashSet<ControllerPoint> _subscribed = new();
    private IList<ControllerPoint>? _points;
    private PianoRoll? _roll;

    private enum Gesture { None, Paint, Erase }
    private Gesture _gesture;
    private Point _anchor;
    private Point _last;
    private bool _edited;
    private string? _readout;

    static ControllerLane()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ControllerLane), new FrameworkPropertyMetadata(typeof(ControllerLane)));
        FocusableProperty.OverrideMetadata(typeof(ControllerLane), new FrameworkPropertyMetadata(true));
        ClipToBoundsProperty.OverrideMetadata(typeof(ControllerLane), new FrameworkPropertyMetadata(true));
    }

    /// <summary>Creates a <see cref="ControllerLane"/>.</summary>
    public ControllerLane()
    {
        AddVisualChild(_playheadLayer);
        Loaded += (_, _) => { AttachRoll(Roll); AttachPoints(Points); };
        IsVisibleChanged += (_, _) => { if (IsVisible) InvalidateVisual(); };
        Unloaded += (_, _) => { DetachRoll(); DetachPoints(); };
    }

    #region Properties

    /// <summary>Identifies the <see cref="Roll"/> dependency property.</summary>
    public static readonly DependencyProperty RollProperty = DependencyProperty.Register(
        nameof(Roll), typeof(PianoRoll), typeof(ControllerLane),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnRollChanged));

    /// <summary>The piano roll this lane is aligned with (scroll, zoom, snap, notes, playhead).</summary>
    public PianoRoll? Roll { get => (PianoRoll?)GetValue(RollProperty); set => SetValue(RollProperty, value); }

    /// <summary>Identifies the <see cref="Mode"/> dependency property.</summary>
    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(
        nameof(Mode), typeof(LaneMode), typeof(ControllerLane),
        new FrameworkPropertyMetadata(LaneMode.Velocity, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>What the lane shows and edits: velocities, a controller or pitch bend.</summary>
    public LaneMode Mode { get => (LaneMode)GetValue(ModeProperty); set => SetValue(ModeProperty, value); }

    /// <summary>Identifies the <see cref="Points"/> dependency property.</summary>
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points), typeof(IList<ControllerPoint>), typeof(ControllerLane),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnPointsChanged));

    /// <summary>Controller / pitch-bend events for <see cref="LaneMode.Controller"/> and <see cref="LaneMode.PitchBend"/>. Kept sorted by position.</summary>
    public IList<ControllerPoint>? Points { get => (IList<ControllerPoint>?)GetValue(PointsProperty); set => SetValue(PointsProperty, value); }

    /// <summary>Identifies the <see cref="ControllerNumber"/> dependency property.</summary>
    public static readonly DependencyProperty ControllerNumberProperty = DependencyProperty.Register(
        nameof(ControllerNumber), typeof(int), typeof(ControllerLane),
        new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsRender), v => v is int i && i is >= 0 and <= 127);

    /// <summary>CC number shown in the header (1 = modulation, 7 = volume, 11 = expression, 64 = sustain…).</summary>
    public int ControllerNumber { get => (int)GetValue(ControllerNumberProperty); set => SetValue(ControllerNumberProperty, value); }

    /// <summary>Identifies the <see cref="PaintStep"/> dependency property.</summary>
    public static readonly DependencyProperty PaintStepProperty = DependencyProperty.Register(
        nameof(PaintStep), typeof(double), typeof(ControllerLane), new FrameworkPropertyMetadata(0.125),
        v => v is double d && d > 0 && !double.IsInfinity(d));

    /// <summary>Spacing of points created while drawing, in beats (default 1/32 note).</summary>
    public double PaintStep { get => (double)GetValue(PaintStepProperty); set => SetValue(PaintStepProperty, value); }

    /// <summary>Identifies the <see cref="ValueBrush"/> dependency property.</summary>
    public static readonly DependencyProperty ValueBrushProperty = RegisterBrush(nameof(ValueBrush), typeof(ControllerLane));
    /// <summary>Brush for the value bars and points.</summary>
    public Brush? ValueBrush { get => (Brush?)GetValue(ValueBrushProperty); set => SetValue(ValueBrushProperty, value); }

    /// <summary>Identifies the <see cref="FillBrush"/> dependency property.</summary>
    public static readonly DependencyProperty FillBrushProperty = RegisterBrush(nameof(FillBrush), typeof(ControllerLane));
    /// <summary>Brush for the area under the values or the curve.</summary>
    public Brush? FillBrush { get => (Brush?)GetValue(FillBrushProperty); set => SetValue(FillBrushProperty, value); }

    /// <summary>Identifies the <see cref="GridBrush"/> dependency property.</summary>
    public static readonly DependencyProperty GridBrushProperty = RegisterBrush(nameof(GridBrush), typeof(ControllerLane));
    /// <summary>Brush for the grid lines.</summary>
    public Brush? GridBrush { get => (Brush?)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }

    /// <summary>Identifies the <see cref="HeaderBrush"/> dependency property.</summary>
    public static readonly DependencyProperty HeaderBrushProperty = RegisterBrush(nameof(HeaderBrush), typeof(ControllerLane));
    /// <summary>Brush for the header column.</summary>
    public Brush? HeaderBrush { get => (Brush?)GetValue(HeaderBrushProperty); set => SetValue(HeaderBrushProperty, value); }

    /// <summary>Identifies the <see cref="PlayheadBrush"/> dependency property.</summary>
    public static readonly DependencyProperty PlayheadBrushProperty = RegisterBrush(nameof(PlayheadBrush), typeof(ControllerLane));
    /// <summary>Brush for the playhead line.</summary>
    public Brush? PlayheadBrush { get => (Brush?)GetValue(PlayheadBrushProperty); set => SetValue(PlayheadBrushProperty, value); }

    /// <summary>Identifies the <see cref="LaneEdited"/> routed event.</summary>
    public static readonly RoutedEvent LaneEditedEvent = EventManager.RegisterRoutedEvent(
        nameof(LaneEdited), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(ControllerLane));

    /// <summary>Raised once per completed paint/erase gesture (hook undo here).</summary>
    public event RoutedEventHandler LaneEdited { add => AddHandler(LaneEditedEvent, value); remove => RemoveHandler(LaneEditedEvent, value); }

    private int MinValue => Mode switch { LaneMode.PitchBend => -8192, LaneMode.Velocity => 1, _ => 0 };

    private int MaxValue => Mode switch { LaneMode.PitchBend => 8191, _ => 127 };

    #endregion

    #region Wiring

    private static void OnRollChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var lane = (ControllerLane)d;
        lane.DetachRoll();
        if (lane.IsLoaded) lane.AttachRoll(e.NewValue as PianoRoll);
    }

    private void AttachRoll(PianoRoll? roll)
    {
        if (roll is null || ReferenceEquals(_roll, roll)) return;
        DetachRoll();
        _roll = roll;
        roll.ViewChanged += OnRollViewChanged;
        roll.PlayheadMoved += OnRollPlayheadMoved;
        InvalidateVisual();
    }

    private void DetachRoll()
    {
        if (_roll is null) return;
        _roll.ViewChanged -= OnRollViewChanged;
        _roll.PlayheadMoved -= OnRollPlayheadMoved;
        _roll = null;
    }

    // Hidden lanes do no work; they redraw once when shown again.
    private void OnRollViewChanged(object? sender, EventArgs e) { if (IsVisible) InvalidateVisual(); }

    private void OnRollPlayheadMoved(object? sender, EventArgs e) { if (IsVisible) RenderPlayheadLayer(); }

    private static void OnPointsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var lane = (ControllerLane)d;
        lane.DetachPoints();
        if (lane.IsLoaded) lane.AttachPoints(e.NewValue as IList<ControllerPoint>);
    }

    private void AttachPoints(IList<ControllerPoint>? points)
    {
        if (points is null || ReferenceEquals(_points, points)) return;
        DetachPoints();
        _points = points;
        if (points is INotifyCollectionChanged incc) incc.CollectionChanged += OnPointsCollectionChanged;
        foreach (ControllerPoint p in points)
            if (_subscribed.Add(p)) p.PropertyChanged += OnPointChanged;
        InvalidateVisual();
    }

    private void DetachPoints()
    {
        if (_points is null) return;
        if (_points is INotifyCollectionChanged incc) incc.CollectionChanged -= OnPointsCollectionChanged;
        foreach (ControllerPoint p in _subscribed) p.PropertyChanged -= OnPointChanged;
        _subscribed.Clear();
        _points = null;
    }

    private void OnPointsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (ControllerPoint p in _subscribed) p.PropertyChanged -= OnPointChanged;
            _subscribed.Clear();
            if (_points is not null)
                foreach (ControllerPoint p in _points)
                    if (_subscribed.Add(p)) p.PropertyChanged += OnPointChanged;
        }
        else
        {
            if (e.OldItems is not null)
                foreach (ControllerPoint p in e.OldItems)
                    if (_subscribed.Remove(p)) p.PropertyChanged -= OnPointChanged;
            if (e.NewItems is not null)
                foreach (ControllerPoint p in e.NewItems)
                    if (_subscribed.Add(p)) p.PropertyChanged += OnPointChanged;
        }
        InvalidateVisual();
    }

    private void OnPointChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();

    #endregion

    #region Coordinates (shared with the roll)

    private double HeaderWidth => Roll?.KeyboardWidth ?? 56;

    private double PixelsPerBeat => Roll?.PixelsPerBeat ?? 80;

    private double Offset => Roll?.HorizontalOffset ?? 0;

    private double BeatToX(double beat) => HeaderWidth + beat * PixelsPerBeat - Offset;

    private double XToBeat(double x) => (x - HeaderWidth + Offset) / PixelsPerBeat;

    private Rect Plot => new(HeaderWidth, 4, Math.Max(0, ActualWidth - HeaderWidth), Math.Max(0, ActualHeight - 8));

    private double ValueToY(double v)
    {
        Rect p = Plot;
        double n = (v - MinValue) / (double)(MaxValue - MinValue);
        return p.Bottom - Math.Clamp(n, 0, 1) * p.Height;
    }

    private int YToValue(double y)
    {
        Rect p = Plot;
        double n = p.Height <= 0 ? 0 : (p.Bottom - y) / p.Height;
        int v = (int)Math.Round(MinValue + Math.Clamp(n, 0, 1) * (MaxValue - MinValue));
        // Make the centre easy to hit for pitch bend.
        if (Mode == LaneMode.PitchBend && Math.Abs(v) < 160) v = 0;
        return v;
    }

    /// <summary>Point spacing while drawing: <see cref="PaintStep"/>, or the roll's snap if that is finer.</summary>
    private double EffectiveStep()
    {
        double snap = Roll?.Snap ?? 0;
        double step = snap > 0 ? Math.Min(PaintStep, snap) : PaintStep;
        // Never closer than 2 pixels (or 1/1920 beat): a tiny step would insert thousands of points per move.
        double minStep = Math.Max(1.0 / 1920, PixelsPerBeat > 0 ? 2 / PixelsPerBeat : 0);
        return Math.Max(step, minStep);
    }

    private double SnapBeat(double beat)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) return beat;
        double step = EffectiveStep();
        return Math.Round(beat / step) * step;
    }

    #endregion

    #region Rendering

    // Control's default Measure/Arrange cast the first visual child to UIElement; our child is a DrawingVisual
    // (the playhead layer), so both must be overridden or layout throws InvalidCastException.
    /// <inheritdoc/>
    protected override Size MeasureOverride(Size constraint)
        => new(Math.Min(constraint.Width, 200), Math.Min(constraint.Height, 100)); // natural size; stretch fills

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size arrangeBounds) => arrangeBounds;

    /// <inheritdoc/>
    protected override int VisualChildrenCount => base.VisualChildrenCount + 1;

    /// <inheritdoc/>
    protected override Visual GetVisualChild(int index)
        => index < base.VisualChildrenCount ? base.GetVisualChild(index) : _playheadLayer;

    /// <inheritdoc/>
    protected override void OnRender(DrawingContext dc)
    {
        var snap = new PixelSnapper(this);
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        Rect plot = Plot;
        var area = new Rect(HeaderWidth, 0, Math.Max(0, w - HeaderWidth), h);

        if (Background is { } bg) dc.DrawRectangle(bg, null, area);

        dc.PushClip(new RectangleGeometry(area));
        RenderGrid(dc, snap, plot, area);
        if (Mode == LaneMode.Velocity) RenderVelocities(dc, snap, plot);
        else RenderPoints(dc, snap, plot);
        dc.Pop();

        RenderHeader(dc, snap, plot);
        RenderPlayheadLayer();
    }

    private void RenderGrid(DrawingContext dc, PixelSnapper snap, Rect plot, Rect area)
    {
        Pen? grid = FrozenPens.Create(GridBrush, snap.OnePixel);
        if (grid is null) return;

        // Horizontal guides (quarters; centre emphasised for pitch bend).
        for (int i = 1; i < 4; i++)
        {
            double y = snap.LineY(plot.Bottom - plot.Height * i / 4);
            dc.PushOpacity(Mode == LaneMode.PitchBend && i == 2 ? 1 : 0.5);
            dc.DrawLine(grid, new Point(area.Left, y), new Point(area.Right, y));
            dc.Pop();
        }

        // Vertical lines aligned with the roll: bars strong, beats weaker.
        double step = Roll?.GetGridStep() ?? 0.25;
        int bpb = Roll?.BeatsPerBar ?? 4;
        double start = Math.Max(0, XToBeat(area.Left)), end = XToBeat(area.Right);
        for (long i = (long)Math.Floor(start / step); i * step <= end; i++)
        {
            double beat = i * step;
            bool bar = Math.Abs(beat / bpb - Math.Round(beat / bpb)) < 1e-6;
            bool isBeat = Math.Abs(beat - Math.Round(beat)) < 1e-6;
            if (!bar && !isBeat && step * PixelsPerBeat < 10) continue;
            double x = snap.LineX(BeatToX(beat));
            dc.PushOpacity(bar ? 1 : isBeat ? 0.55 : 0.25);
            dc.DrawLine(grid, new Point(x, area.Top), new Point(x, area.Bottom));
            dc.Pop();
        }
    }

    private void RenderVelocities(DrawingContext dc, PixelSnapper snap, Rect plot)
    {
        if (Roll?.Notes is not { } notes) return;
        Brush? normal = Roll.NoteBrush ?? ValueBrush;
        Brush? selected = Roll.SelectedNoteBrush ?? normal;
        double left = plot.Left - 8, right = plot.Right + 8;
        double stalk = Math.Max(snap.OnePixel * 2, 2);

        foreach (MidiNote n in notes)
        {
            double x = BeatToX(n.Start);
            if (x < left || x > right) continue;
            Brush? b = n.IsSelected ? selected : n.Brush ?? normal;
            if (b is null) continue;
            double y = ValueToY(n.Velocity);
            dc.PushOpacity(0.45 + 0.55 * n.Velocity / 127.0);
            dc.DrawRectangle(b, null, snap.Rect(x, y, stalk, plot.Bottom - y));
            // Head: a short bar showing the note's length (capped) so overlapping notes stay readable.
            double headW = Math.Clamp(n.Length * PixelsPerBeat - 2, 4, 14);
            dc.DrawRoundedRectangle(b, null, snap.Rect(x, y - 1.5, headW, 3), 1, 1);
            dc.Pop();
        }
    }

    private void RenderPoints(DrawingContext dc, PixelSnapper snap, Rect plot)
    {
        IList<ControllerPoint>? pts = _points ?? Points;
        double baseY = Mode == LaneMode.PitchBend ? ValueToY(0) : plot.Bottom;
        if (pts is null || pts.Count == 0) return;

        // Step-hold curve: each value holds until the next point, the last holds to the edge.
        var line = new StreamGeometry();
        var area = new StreamGeometry();
        using (StreamGeometryContext l = line.Open())
        using (StreamGeometryContext a = area.Open())
        {
            double x0 = BeatToX(pts[0].Position), y0 = ValueToY(pts[0].Value);
            l.BeginFigure(new Point(x0, y0), false, false);
            a.BeginFigure(new Point(x0, baseY), true, true);
            a.LineTo(new Point(x0, y0), false, false);
            double prevY = y0;
            for (int i = 1; i < pts.Count; i++)
            {
                double x = BeatToX(pts[i].Position), y = ValueToY(pts[i].Value);
                l.LineTo(new Point(x, prevY), true, false);
                l.LineTo(new Point(x, y), true, false);
                a.LineTo(new Point(x, prevY), false, false);
                a.LineTo(new Point(x, y), false, false);
                prevY = y;
            }
            double xEnd = Math.Max(BeatToX(pts[^1].Position), ActualWidth);
            l.LineTo(new Point(xEnd, prevY), true, false);
            a.LineTo(new Point(xEnd, prevY), false, false);
            a.LineTo(new Point(xEnd, baseY), false, false);
        }
        line.Freeze();
        area.Freeze();

        if (FillBrush is { } fill) dc.DrawGeometry(fill, null, area);
        if (ValueBrush is { } vb)
        {
            var pen = new Pen(vb, 1.5) { LineJoin = PenLineJoin.Miter };
            pen.Freeze();
            dc.DrawGeometry(null, pen, line);
            if (PixelsPerBeat * PaintStep >= 5)
            {
                foreach (ControllerPoint p in pts)
                {
                    double x = BeatToX(p.Position);
                    if (x < plot.Left - 4 || x > plot.Right + 4) continue;
                    dc.DrawEllipse(vb, null, new Point(x, ValueToY(p.Value)), 2, 2);
                }
            }
        }
    }

    private void RenderHeader(DrawingContext dc, PixelSnapper snap, Rect plot)
    {
        double hw = HeaderWidth;
        if (hw <= 0) return;
        var header = new Rect(0, 0, hw, ActualHeight);
        if (HeaderBrush is { } hb) dc.DrawRectangle(hb, null, header);
        if (FrozenPens.Create(BorderBrush, snap.OnePixel) is { } sep)
            dc.DrawLine(sep, new Point(snap.LineX(hw - 0.5), 0), new Point(snap.LineX(hw - 0.5), ActualHeight));

        string title = Mode switch
        {
            LaneMode.Velocity => "Velocity",
            LaneMode.PitchBend => "Pitch",
            _ => "CC " + ControllerNumber.ToString(CultureInfo.InvariantCulture),
        };
        FormattedText ft = MakeText(_readout ?? title, snap.PixelsPerDip);
        ft.MaxTextWidth = Math.Max(1, hw - 6);
        ft.Trimming = TextTrimming.CharacterEllipsis;
        dc.DrawText(ft, new Point(4, 4));

        string top = Mode == LaneMode.PitchBend ? "+8191" : "127";
        string bottom = Mode == LaneMode.PitchBend ? "-8192" : Mode == LaneMode.Velocity ? "1" : "0";
        double small = Math.Max(8, FontSize - 2);
        FormattedText tt = MakeText(top, snap.PixelsPerDip, size: small);
        FormattedText bt = MakeText(bottom, snap.PixelsPerDip, size: small);
        dc.PushOpacity(0.7);
        if (plot.Height > 3 * ft.Height) dc.DrawText(tt, new Point(hw - tt.Width - 4, plot.Top + ft.Height + 2));
        dc.DrawText(bt, new Point(hw - bt.Width - 4, plot.Bottom - bt.Height));
        dc.Pop();
    }

    private void RenderPlayheadLayer()
    {
        using DrawingContext dc = _playheadLayer.RenderOpen();
        if (Roll is not { } roll || PlayheadBrush is not { } pb || ActualWidth <= HeaderWidth) return;
        double x = BeatToX(roll.PlayheadPosition);
        if (x < HeaderWidth || x > ActualWidth) return;
        var snap = new PixelSnapper(this);
        dc.DrawLine(FrozenPens.Create(pb, snap.OnePixel)!, new Point(snap.LineX(x), 0), new Point(snap.LineX(x), ActualHeight));
    }

    #endregion

    #region Editing

    /// <inheritdoc/>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) => BeginGesture(e, Gesture.Paint);

    /// <inheritdoc/>
    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        if (Mode == LaneMode.Velocity) { base.OnMouseRightButtonDown(e); return; }
        BeginGesture(e, Gesture.Erase);
    }

    private void BeginGesture(MouseButtonEventArgs e, Gesture g)
    {
        // A second button during a gesture is ignored: restarting would lose the edit flag (and LaneEdited).
        if (_gesture != Gesture.None) { e.Handled = true; return; }
        Focus();
        Point p = e.GetPosition(this);
        if (p.X < HeaderWidth || !CaptureMouse()) return;
        _gesture = g;
        _anchor = _last = p;
        _edited = false;
        Apply(p, p);
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_gesture == Gesture.None || !IsMouseCaptured) return;
        Point p = e.GetPosition(this);
        // Shift: straight line from the press point (re-applied from the anchor each move).
        Point from = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 && _gesture == Gesture.Paint ? _anchor : _last;
        Apply(from, p);
        _last = p;
    }

    /// <inheritdoc/>
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    /// <inheritdoc/>
    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        if (_gesture == Gesture.Erase && IsMouseCaptured)
        {
            ReleaseMouseCapture();
            e.Handled = true; // don't open a context menu after an erase stroke
        }
    }

    /// <inheritdoc/>
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_gesture == Gesture.None) return;
        _gesture = Gesture.None;
        _readout = null;
        InvalidateVisual();
        if (_edited)
        {
            _edited = false;
            RaiseEvent(new RoutedEventArgs(LaneEditedEvent, this));
        }
    }

    /// <summary>Applies the current gesture to the stroke segment a→b.</summary>
    private void Apply(Point a, Point b)
    {
        if (Mode == LaneMode.Velocity) PaintVelocity(a, b);
        else if (_gesture == Gesture.Erase) ErasePoints(a, b);
        else PaintPoints(a, b);
    }

    private void PaintVelocity(Point a, Point b)
    {
        if (Roll?.Notes is not { } notes || notes.Count == 0) return;
        bool anySelected = notes.Any(n => n.IsSelected);
        double x0 = Math.Min(a.X, b.X) - 3, x1 = Math.Max(a.X, b.X) + 3;
        foreach (MidiNote n in notes)
        {
            if (anySelected && !n.IsSelected) continue;
            double x = BeatToX(n.Start);
            if (x < x0 || x > x1) continue;
            // Interpolate along the stroke so a fast drag draws a ramp, not a step.
            double t = Math.Abs(b.X - a.X) < 0.5 ? 1 : Math.Clamp((x - a.X) / (b.X - a.X), 0, 1);
            int v = Math.Clamp(YToValue(a.Y + (b.Y - a.Y) * t), 1, 127);
            if (n.Velocity != v) { n.Velocity = v; _edited = true; }
            _readout = "Vel " + v.ToString(CultureInfo.CurrentCulture);
        }
        InvalidateVisual();
    }

    private void PaintPoints(Point a, Point b)
    {
        if (Points is not { IsReadOnly: false } list) return;
        if (b.X < a.X) (a, b) = (b, a);
        double beatA = Math.Max(0, SnapBeat(XToBeat(a.X)));
        double beatB = Math.Max(0, SnapBeat(XToBeat(b.X)));
        double step = EffectiveStep();

        RemoveRange(list, beatA, beatB);
        int count = Math.Max(0, (int)Math.Round((beatB - beatA) / step));
        for (int i = 0; i <= count; i++)
        {
            double beat = beatA + i * step;
            double t = count == 0 ? 1 : (double)i / count;
            int v = YToValue(a.Y + (b.Y - a.Y) * t);
            InsertSorted(list, new ControllerPoint(beat, v));
            _readout = (Mode == LaneMode.PitchBend ? v.ToString("+0;-0;0", CultureInfo.CurrentCulture) : v.ToString(CultureInfo.CurrentCulture));
        }
        _edited = true;
        InvalidateVisual();
    }

    private void ErasePoints(Point a, Point b)
    {
        if (Points is not { IsReadOnly: false } list) return;
        double b0 = XToBeat(Math.Min(a.X, b.X) - 4), b1 = XToBeat(Math.Max(a.X, b.X) + 4);
        if (RemoveRange(list, b0, b1)) _edited = true;
    }

    /// <summary>Removes points with position in [from, to]. Returns true if anything was removed.</summary>
    private static bool RemoveRange(IList<ControllerPoint> list, double from, double to)
    {
        const double eps = 1e-9;
        bool removed = false;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            double pos = list[i].Position;
            if (pos >= from - eps && pos <= to + eps)
            {
                list.RemoveAt(i);
                removed = true;
            }
        }
        return removed;
    }

    private static void InsertSorted(IList<ControllerPoint> list, ControllerPoint point)
    {
        int lo = 0, hi = list.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (list[mid].Position <= point.Position) lo = mid + 1; else hi = mid;
        }
        list.Insert(lo, point);
    }

    #endregion
}
