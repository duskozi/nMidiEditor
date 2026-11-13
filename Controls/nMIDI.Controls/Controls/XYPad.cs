using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using nMIDI.Controls.Primitives;

namespace nMIDI.Controls;

/// <summary>
/// Two-parameter pad (e.g. filter cutoff × resonance). Values are normalized 0..1 (Y = 1 at the top);
/// map them to real ranges in your view-model. Click/drag positions the puck absolutely, Shift = fine
/// relative movement, double-click resets, arrow keys nudge.
/// </summary>
public class XYPad : DawRenderControl
{
    private Point _lastPoint;
    private bool _dragging;

    static XYPad()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(XYPad), new FrameworkPropertyMetadata(typeof(XYPad)));
        FocusableProperty.OverrideMetadata(typeof(XYPad), new FrameworkPropertyMetadata(true));
    }

    /// <summary>Identifies the <see cref="XValue"/> dependency property.</summary>
    public static readonly DependencyProperty XValueProperty = RegisterValue(nameof(XValue));
    /// <summary>Horizontal value, 0 (left) to 1 (right).</summary>
    public double XValue { get => (double)GetValue(XValueProperty); set => SetValue(XValueProperty, value); }

    /// <summary>Identifies the <see cref="YValue"/> dependency property.</summary>
    public static readonly DependencyProperty YValueProperty = RegisterValue(nameof(YValue));
    /// <summary>Vertical value, 0 (bottom) to 1 (top).</summary>
    public double YValue { get => (double)GetValue(YValueProperty); set => SetValue(YValueProperty, value); }

    private static DependencyProperty RegisterValue(string name) => DependencyProperty.Register(
        name, typeof(double), typeof(XYPad),
        new FrameworkPropertyMetadata(0.5, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            null, (_, v) => v is double d && !double.IsNaN(d) ? Math.Clamp(d, 0, 1) : 0.0));

    /// <summary>Identifies the <see cref="DefaultX"/> dependency property.</summary>
    public static readonly DependencyProperty DefaultXProperty = DependencyProperty.Register(
        nameof(DefaultX), typeof(double), typeof(XYPad), new FrameworkPropertyMetadata(0.5));
    /// <summary>Horizontal value restored by a double-click, 0 to 1.</summary>
    public double DefaultX { get => (double)GetValue(DefaultXProperty); set => SetValue(DefaultXProperty, value); }

    /// <summary>Identifies the <see cref="DefaultY"/> dependency property.</summary>
    public static readonly DependencyProperty DefaultYProperty = DependencyProperty.Register(
        nameof(DefaultY), typeof(double), typeof(XYPad), new FrameworkPropertyMetadata(0.5));
    /// <summary>Vertical value restored by a double-click, 0 to 1.</summary>
    public double DefaultY { get => (double)GetValue(DefaultYProperty); set => SetValue(DefaultYProperty, value); }

    /// <summary>Identifies the <see cref="GridDivisions"/> dependency property.</summary>
    public static readonly DependencyProperty GridDivisionsProperty = DependencyProperty.Register(
        nameof(GridDivisions), typeof(int), typeof(XYPad),
        new FrameworkPropertyMetadata(4, FrameworkPropertyMetadataOptions.AffectsRender), v => v is int i && i >= 0 && i <= 64);
    /// <summary>Number of grid cells along each axis.</summary>
    public int GridDivisions { get => (int)GetValue(GridDivisionsProperty); set => SetValue(GridDivisionsProperty, value); }

    /// <summary>Identifies the <see cref="PuckSize"/> dependency property.</summary>
    public static readonly DependencyProperty PuckSizeProperty = DependencyProperty.Register(
        nameof(PuckSize), typeof(double), typeof(XYPad),
        new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.AffectsRender));
    /// <summary>Diameter of the puck in pixels.</summary>
    public double PuckSize { get => (double)GetValue(PuckSizeProperty); set => SetValue(PuckSizeProperty, value); }

    /// <summary>Identifies the <see cref="XLabel"/> dependency property.</summary>
    public static readonly DependencyProperty XLabelProperty = DependencyProperty.Register(
        nameof(XLabel), typeof(string), typeof(XYPad), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    /// <summary>Label of the horizontal axis.</summary>
    public string? XLabel { get => (string?)GetValue(XLabelProperty); set => SetValue(XLabelProperty, value); }

    /// <summary>Identifies the <see cref="YLabel"/> dependency property.</summary>
    public static readonly DependencyProperty YLabelProperty = DependencyProperty.Register(
        nameof(YLabel), typeof(string), typeof(XYPad), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    /// <summary>Label of the vertical axis.</summary>
    public string? YLabel { get => (string?)GetValue(YLabelProperty); set => SetValue(YLabelProperty, value); }

    /// <summary>Identifies the <see cref="GridBrush"/> dependency property.</summary>
    public static readonly DependencyProperty GridBrushProperty = RegisterBrush(nameof(GridBrush), typeof(XYPad));
    /// <summary>Brush for the grid lines.</summary>
    public Brush? GridBrush { get => (Brush?)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }

    /// <summary>Identifies the <see cref="PuckBrush"/> dependency property.</summary>
    public static readonly DependencyProperty PuckBrushProperty = RegisterBrush(nameof(PuckBrush), typeof(XYPad));
    /// <summary>Brush for the puck.</summary>
    public Brush? PuckBrush { get => (Brush?)GetValue(PuckBrushProperty); set => SetValue(PuckBrushProperty, value); }

    /// <summary>Identifies the <see cref="CrosshairBrush"/> dependency property.</summary>
    public static readonly DependencyProperty CrosshairBrushProperty = RegisterBrush(nameof(CrosshairBrush), typeof(XYPad));
    /// <summary>Brush for the crosshair lines.</summary>
    public Brush? CrosshairBrush { get => (Brush?)GetValue(CrosshairBrushProperty); set => SetValue(CrosshairBrushProperty, value); }

    /// <summary>Identifies the <see cref="DragStarted"/> routed event.</summary>
    public static readonly RoutedEvent DragStartedEvent = DawRangeControl.DragStartedEvent.AddOwner(typeof(XYPad));
    /// <summary>Raised when the user grabs the puck.</summary>
    public event RoutedEventHandler DragStarted { add => AddHandler(DragStartedEvent, value); remove => RemoveHandler(DragStartedEvent, value); }

    /// <summary>Identifies the <see cref="DragCompleted"/> routed event.</summary>
    public static readonly RoutedEvent DragCompletedEvent = DawRangeControl.DragCompletedEvent.AddOwner(typeof(XYPad));
    /// <summary>Raised when the user lets go of the puck.</summary>
    public event RoutedEventHandler DragCompleted { add => AddHandler(DragCompletedEvent, value); remove => RemoveHandler(DragCompletedEvent, value); }

    private Rect Inner
    {
        get
        {
            double r = PuckSize / 2;
            return new Rect(r, r, Math.Max(0, ActualWidth - 2 * r), Math.Max(0, ActualHeight - 2 * r));
        }
    }

    /// <inheritdoc/>
    protected override void OnRender(DrawingContext dc)
    {
        var snap = new PixelSnapper(this);
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        dc.DrawRoundedRectangle(Background ?? Brushes.Transparent, FrozenPens.Create(BorderBrush, snap.OnePixel), bounds, 3, 3);

        Rect inner = Inner;
        if (inner.Width <= 0 || inner.Height <= 0) return;

        if (GridBrush is { } gb && GridDivisions > 1)
        {
            Pen gp = FrozenPens.Create(gb, snap.OnePixel)!;
            for (int i = 1; i < GridDivisions; i++)
            {
                double x = snap.LineX(inner.X + inner.Width * i / GridDivisions);
                double y = snap.LineY(inner.Y + inner.Height * i / GridDivisions);
                dc.DrawLine(gp, new Point(x, 0), new Point(x, ActualHeight));
                dc.DrawLine(gp, new Point(0, y), new Point(ActualWidth, y));
            }
        }

        if (XLabel is { Length: > 0 } xl)
        {
            FormattedText ft = MakeText(xl, snap.PixelsPerDip);
            dc.DrawText(ft, new Point(ActualWidth - ft.Width - 6, ActualHeight - ft.Height - 4));
        }
        if (YLabel is { Length: > 0 } yl)
            dc.DrawText(MakeText(yl, snap.PixelsPerDip), new Point(6, 4));

        double px = inner.X + XValue * inner.Width;
        double py = inner.Y + (1 - YValue) * inner.Height;

        if (CrosshairBrush is { } cb)
        {
            Pen cp = FrozenPens.Create(cb, snap.OnePixel)!;
            dc.DrawLine(cp, new Point(snap.LineX(px), 0), new Point(snap.LineX(px), ActualHeight));
            dc.DrawLine(cp, new Point(0, snap.LineY(py)), new Point(ActualWidth, snap.LineY(py)));
        }

        if (PuckBrush is { } pb)
        {
            double r = PuckSize / 2;
            if (_dragging || IsKeyboardFocused)
            {
                dc.PushOpacity(0.25);
                dc.DrawEllipse(pb, null, new Point(px, py), r * 1.8, r * 1.8);
                dc.Pop();
            }
            dc.DrawEllipse(pb, FrozenPens.Create(Background, 2), new Point(px, py), r, r);
        }
    }

    private void SetFromPoint(Point p)
    {
        Rect inner = Inner;
        if (inner.Width <= 0 || inner.Height <= 0) return;
        SetCurrentValue(XValueProperty, (p.X - inner.X) / inner.Width);
        SetCurrentValue(YValueProperty, 1 - (p.Y - inner.Y) / inner.Height);
    }

    /// <inheritdoc/>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (e.ClickCount == 2)
        {
            SetCurrentValue(XValueProperty, DefaultX);
            SetCurrentValue(YValueProperty, DefaultY);
            e.Handled = true;
            return;
        }
        if (!CaptureMouse()) return;
        _dragging = true;
        _lastPoint = e.GetPosition(this);
        if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0) SetFromPoint(_lastPoint);
        RaiseEvent(new RoutedEventArgs(DragStartedEvent, this));
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;
        Point p = e.GetPosition(this);
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            Rect inner = Inner;
            if (inner.Width > 0 && inner.Height > 0)
            {
                SetCurrentValue(XValueProperty, XValue + (p.X - _lastPoint.X) / inner.Width * 0.1);
                SetCurrentValue(YValueProperty, YValue - (p.Y - _lastPoint.Y) / inner.Height * 0.1);
            }
        }
        else
        {
            SetFromPoint(p);
        }
        _lastPoint = p;
    }

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
        if (!_dragging) return;
        _dragging = false;
        InvalidateVisual();
        RaiseEvent(new RoutedEventArgs(DragCompletedEvent, this));
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        double step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 0.002 : 0.02;
        switch (e.Key)
        {
            case Key.Left: SetCurrentValue(XValueProperty, XValue - step); break;
            case Key.Right: SetCurrentValue(XValueProperty, XValue + step); break;
            case Key.Up: SetCurrentValue(YValueProperty, YValue + step); break;
            case Key.Down: SetCurrentValue(YValueProperty, YValue - step); break;
            default: return;
        }
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); InvalidateVisual(); }

    /// <inheritdoc/>
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); InvalidateVisual(); }
}
