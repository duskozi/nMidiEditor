using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using nMIDI.Controls.Primitives;

namespace nMIDI.Controls;

/// <summary>What a <see cref="PerformanceWheel"/> controls.</summary>
public enum WheelMode
{
    /// <summary>-8192..8191, centre detent, springs back to 0 on release.</summary>
    PitchBend,
    /// <summary>0..127 (CC 1), stays where it is left.</summary>
    Modulation,
}

/// <summary>
/// Hardware-style pitch-bend / modulation wheel drawn as a rotating cylinder in a slot.
/// Drag vertically (Shift = fine), mouse wheel, Up/Down keys; double-click returns to rest.
/// Pitch bend springs back to centre with a short ease when released (disable with <see cref="SpringReturn"/>).
/// </summary>
public class PerformanceWheel : DawRenderControl
{
    private const double MaxRotation = 62;   // degrees the wheel turns either side of centre
    private const double RibSpacing = 11;    // degrees between grip ribs
    private const double LabelHeight = 14;

    private static readonly Brush s_cylinderShade = CreateCylinderShade();

    private bool _dragging;
    private double _lastY;
    private double _dragNorm;
    private bool _springing;
    private TimeSpan _lastTick;

    static PerformanceWheel()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(PerformanceWheel), new FrameworkPropertyMetadata(typeof(PerformanceWheel)));
        FocusableProperty.OverrideMetadata(typeof(PerformanceWheel), new FrameworkPropertyMetadata(true));
    }

    /// <summary>Creates a <see cref="PerformanceWheel"/>.</summary>
    public PerformanceWheel()
    {
        Unloaded += (_, _) => StopSpring();
    }

    #region Properties

    /// <summary>Identifies the <see cref="Mode"/> dependency property.</summary>
    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(
        nameof(Mode), typeof(WheelMode), typeof(PerformanceWheel),
        new FrameworkPropertyMetadata(WheelMode.PitchBend, FrameworkPropertyMetadataOptions.AffectsRender, OnModeChanged));

    /// <summary>Pitch bend (springs back to the centre) or modulation (stays where it is left).</summary>
    public WheelMode Mode { get => (WheelMode)GetValue(ModeProperty); set => SetValue(ModeProperty, value); }

    private static void OnModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Only re-clamp to the new range: resetting here would overwrite a bound view-model value.
        ((PerformanceWheel)d).CoerceValue(ValueProperty);
    }

    /// <summary>Identifies the <see cref="Value"/> dependency property.</summary>
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(int), typeof(PerformanceWheel),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnValueChanged, CoerceValueToRange));

    /// <summary>Pitch bend: -8192..8191. Modulation: 0..127.</summary>
    public int Value { get => (int)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    private static object CoerceValueToRange(DependencyObject d, object v)
    {
        var w = (PerformanceWheel)d;
        return Math.Clamp((int)v, w.Minimum, w.Maximum);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var w = (PerformanceWheel)d;
        w.RaiseEvent(new RoutedPropertyChangedEventArgs<int>((int)e.OldValue, (int)e.NewValue, ValueChangedEvent));
    }

    /// <summary>Identifies the <see cref="SpringReturn"/> dependency property.</summary>
    public static readonly DependencyProperty SpringReturnProperty = DependencyProperty.Register(
        nameof(SpringReturn), typeof(bool), typeof(PerformanceWheel), new FrameworkPropertyMetadata(true));

    /// <summary>Pitch-bend mode only: return to centre on release (like a real spring-loaded wheel).</summary>
    public bool SpringReturn { get => (bool)GetValue(SpringReturnProperty); set => SetValue(SpringReturnProperty, value); }

    /// <summary>Identifies the <see cref="DragSensitivity"/> dependency property.</summary>
    public static readonly DependencyProperty DragSensitivityProperty = DependencyProperty.Register(
        nameof(DragSensitivity), typeof(double), typeof(PerformanceWheel), new FrameworkPropertyMetadata(140.0),
        v => v is double x && x > 0 && !double.IsInfinity(x));

    /// <summary>Pixels of vertical mouse travel for the full range.</summary>
    public double DragSensitivity { get => (double)GetValue(DragSensitivityProperty); set => SetValue(DragSensitivityProperty, value); }

    /// <summary>Identifies the <see cref="Label"/> dependency property.</summary>
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(PerformanceWheel),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Caption under the wheel ("PB", "MOD"). Null = derived from <see cref="Mode"/>; empty = none.</summary>
    public string? Label { get => (string?)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    /// <summary>Identifies the <see cref="SlotBrush"/> dependency property.</summary>
    public static readonly DependencyProperty SlotBrushProperty = RegisterBrush(nameof(SlotBrush), typeof(PerformanceWheel));
    /// <summary>Brush for the slot around the wheel.</summary>
    public Brush? SlotBrush { get => (Brush?)GetValue(SlotBrushProperty); set => SetValue(SlotBrushProperty, value); }

    /// <summary>Identifies the <see cref="WheelBrush"/> dependency property.</summary>
    public static readonly DependencyProperty WheelBrushProperty = RegisterBrush(nameof(WheelBrush), typeof(PerformanceWheel));
    /// <summary>Brush for the wheel body.</summary>
    public Brush? WheelBrush { get => (Brush?)GetValue(WheelBrushProperty); set => SetValue(WheelBrushProperty, value); }

    /// <summary>Identifies the <see cref="RibBrush"/> dependency property.</summary>
    public static readonly DependencyProperty RibBrushProperty = RegisterBrush(nameof(RibBrush), typeof(PerformanceWheel));
    /// <summary>Brush for the ribs of the wheel.</summary>
    public Brush? RibBrush { get => (Brush?)GetValue(RibBrushProperty); set => SetValue(RibBrushProperty, value); }

    /// <summary>Identifies the <see cref="MarkerBrush"/> dependency property.</summary>
    public static readonly DependencyProperty MarkerBrushProperty = RegisterBrush(nameof(MarkerBrush), typeof(PerformanceWheel));
    /// <summary>Brush for the centre marker.</summary>
    public Brush? MarkerBrush { get => (Brush?)GetValue(MarkerBrushProperty); set => SetValue(MarkerBrushProperty, value); }

    /// <summary>Smallest value: -8192 for pitch bend, 0 for modulation.</summary>
    public int Minimum => Mode == WheelMode.PitchBend ? -8192 : 0;

    /// <summary>Largest value: 8191 for pitch bend, 127 for modulation.</summary>
    public int Maximum => Mode == WheelMode.PitchBend ? 8191 : 127;

    /// <summary>Where the wheel rests: centre for pitch bend, 0 for modulation.</summary>
    public int RestValue => 0;

    #endregion

    #region Events

    /// <summary>Identifies the <see cref="ValueChanged"/> routed event.</summary>
    public static readonly RoutedEvent ValueChangedEvent = EventManager.RegisterRoutedEvent(
        nameof(ValueChanged), RoutingStrategy.Bubble, typeof(RoutedPropertyChangedEventHandler<int>), typeof(PerformanceWheel));

    /// <summary>Raised for every value change (drag, spring-back, binding). Send the MIDI message from here.</summary>
    public event RoutedPropertyChangedEventHandler<int> ValueChanged { add => AddHandler(ValueChangedEvent, value); remove => RemoveHandler(ValueChangedEvent, value); }

    /// <summary>Identifies the <see cref="DragStarted"/> routed event.</summary>
    public static readonly RoutedEvent DragStartedEvent = DawRangeControl.DragStartedEvent.AddOwner(typeof(PerformanceWheel));
    /// <summary>Raised when the user grabs the wheel.</summary>
    public event RoutedEventHandler DragStarted { add => AddHandler(DragStartedEvent, value); remove => RemoveHandler(DragStartedEvent, value); }

    /// <summary>Identifies the <see cref="DragCompleted"/> routed event.</summary>
    public static readonly RoutedEvent DragCompletedEvent = DawRangeControl.DragCompletedEvent.AddOwner(typeof(PerformanceWheel));
    /// <summary>Raised when the user lets go of the wheel (a pitch wheel then springs back).</summary>
    public event RoutedEventHandler DragCompleted { add => AddHandler(DragCompletedEvent, value); remove => RemoveHandler(DragCompletedEvent, value); }

    #endregion

    #region Value mapping

    /// <summary>Pitch bend → -1..1, modulation → 0..1.</summary>
    private double Normalized
        => Mode == WheelMode.PitchBend
            ? (Value >= 0 ? Value / 8191.0 : Value / 8192.0)
            : Value / 127.0;

    private int FromNormalized(double n)
        => Mode == WheelMode.PitchBend
            ? (int)Math.Round(n >= 0 ? Math.Min(n, 1) * 8191 : Math.Max(n, -1) * 8192)
            : (int)Math.Round(Math.Clamp(n, 0, 1) * 127);

    /// <summary>Rotation of the wheel in degrees (negative = rolled toward the player / down).</summary>
    private double Rotation
        => Mode == WheelMode.PitchBend ? Normalized * MaxRotation : (Normalized * 2 - 1) * MaxRotation;

    private double NormSpan => Mode == WheelMode.PitchBend ? 2.0 : 1.0;

    #endregion

    #region Rendering

    private static Brush CreateCylinderShade()
    {
        // Dark at the top and bottom where the cylinder turns away; clear in the middle.
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        b.GradientStops.Add(new GradientStop(Color.FromArgb(200, 0, 0, 0), 0));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(40, 0, 0, 0), 0.22));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.45));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(20, 255, 255, 255), 0.5));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(40, 0, 0, 0), 0.78));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(210, 0, 0, 0), 1));
        b.Freeze();
        return b;
    }

    /// <inheritdoc/>
    protected override void OnRender(DrawingContext dc)
    {
        var snap = new PixelSnapper(this);
        string? label = Label ?? (Mode == WheelMode.PitchBend ? "PB" : "MOD");
        double labelH = string.IsNullOrEmpty(label) ? 0 : LabelHeight;
        double w = ActualWidth, h = ActualHeight - labelH;
        if (w <= 4 || h <= 10) return;

        if (Background is { } bg) dc.DrawRectangle(bg, null, new Rect(0, 0, ActualWidth, ActualHeight));

        // Slot
        var slot = new Rect(0, 0, w, h);
        dc.DrawRoundedRectangle(SlotBrush, FrozenPens.Create(BorderBrush, snap.OnePixel), slot, 4, 4);

        // Wheel body (visible part of the cylinder)
        double inset = Math.Max(3, w * 0.16);
        var wheel = new Rect(inset, 4, Math.Max(0, w - 2 * inset), Math.Max(0, h - 8));
        double radius = wheel.Height / 2, cy = wheel.Top + radius;
        dc.PushClip(new RectangleGeometry(wheel, 2, 2));
        dc.DrawRectangle(WheelBrush, null, wheel);

        // Grip ribs rotate with the value; projection y = r·sin θ, fading as they turn away.
        double rot = Rotation;
        if (RibBrush is { } rib)
        {
            double phase = ((rot % RibSpacing) + RibSpacing) % RibSpacing;
            for (double theta = -90 + phase; theta < 90; theta += RibSpacing)
            {
                double t = theta * Math.PI / 180;
                double y = cy - radius * Math.Sin(t);
                double vis = Math.Cos(t);
                dc.PushOpacity(0.15 + 0.6 * vis);
                dc.DrawRectangle(rib, null, new Rect(wheel.X, snap.Y(y), wheel.Width, Math.Max(snap.OnePixel, 1.2 * vis)));
                dc.Pop();
            }
        }

        // Position marker painted on the wheel (rotates with it).
        if (MarkerBrush is { } marker)
        {
            double t = rot * Math.PI / 180;
            double y = cy - radius * Math.Sin(t);
            double thick = Math.Max(1.5, 3.2 * Math.Cos(t));
            dc.DrawRectangle(marker, null, new Rect(wheel.X, y - thick / 2, wheel.Width, thick));
        }

        dc.DrawRectangle(s_cylinderShade, null, wheel);
        dc.Pop();

        // Centre detent ticks for pitch bend.
        if (Mode == WheelMode.PitchBend && MarkerBrush is { } tick)
        {
            dc.PushOpacity(0.7);
            dc.DrawRectangle(tick, null, new Rect(0.5, cy - 1, inset - 1.5, 2));
            dc.DrawRectangle(tick, null, new Rect(w - inset + 1, cy - 1, inset - 1.5, 2));
            dc.Pop();
        }

        if (labelH > 0)
        {
            string text = _dragging || _springing ? ValueText() : label!;
            FormattedText ft = MakeText(text, snap.PixelsPerDip);
            dc.DrawText(ft, new Point((ActualWidth - ft.Width) / 2, h + (labelH - ft.Height) / 2 + 1));
        }
    }

    private string ValueText()
        => Value.ToString(Mode == WheelMode.PitchBend ? "+0;-0;0" : "0", System.Globalization.CultureInfo.CurrentCulture);

    #endregion

    #region Input

    /// <inheritdoc/>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (e.ClickCount == 2)
        {
            SetCurrentValue(ValueProperty, RestValue);
            e.Handled = true;
            return;
        }
        if (!CaptureMouse()) return;
        StopSpring();
        _dragging = true;
        _lastY = e.GetPosition(this).Y;
        _dragNorm = Normalized;
        RaiseEvent(new RoutedEventArgs(DragStartedEvent, this));
        InvalidateVisual();
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;
        double y = e.GetPosition(this).Y;
        double delta = (_lastY - y) / DragSensitivity * NormSpan;
        _lastY = y;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) delta *= 0.15;
        double min = Mode == WheelMode.PitchBend ? -1 : 0;
        _dragNorm = Math.Clamp(_dragNorm + delta, min, 1);
        SetCurrentValue(ValueProperty, FromNormalized(_dragNorm));
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
        RaiseEvent(new RoutedEventArgs(DragCompletedEvent, this));
        if (Mode == WheelMode.PitchBend && SpringReturn && Value != 0) StartSpring();
        InvalidateVisual();
    }

    /// <inheritdoc/>
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        double notches = e.Delta / (double)Mouse.MouseWheelDeltaForOneLine;
        double step = NormSpan / ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 256 : 32);
        double min = Mode == WheelMode.PitchBend ? -1 : 0;
        SetCurrentValue(ValueProperty, FromNormalized(Math.Clamp(Normalized + notches * step, min, 1)));
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        double step = NormSpan / ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 256 : 32);
        double min = Mode == WheelMode.PitchBend ? -1 : 0;
        switch (e.Key)
        {
            case Key.Up: SetCurrentValue(ValueProperty, FromNormalized(Math.Clamp(Normalized + step, min, 1))); break;
            case Key.Down: SetCurrentValue(ValueProperty, FromNormalized(Math.Clamp(Normalized - step, min, 1))); break;
            case Key.Home:
            case Key.Delete: SetCurrentValue(ValueProperty, RestValue); break;
            default: return;
        }
        e.Handled = true;
    }

    #endregion

    #region Spring return

    private void StartSpring()
    {
        if (_springing) return;
        _springing = true;
        _lastTick = TimeSpan.Zero;
        CompositionTarget.Rendering += OnSpringTick;
    }

    private void StopSpring()
    {
        if (!_springing) return;
        _springing = false;
        CompositionTarget.Rendering -= OnSpringTick;
    }

    private void OnSpringTick(object? sender, EventArgs e)
    {
        TimeSpan now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _lastTick) return;
        double dt = _lastTick == TimeSpan.Zero ? 1.0 / 60 : Math.Min((now - _lastTick).TotalSeconds, 0.05);
        _lastTick = now;

        // Critically-damped-ish exponential return: ~95% of the way back in 120 ms.
        double next = Value * Math.Exp(-dt / 0.04);
        if (Math.Abs(next) < 24) next = 0;
        SetCurrentValue(ValueProperty, (int)Math.Round(next));
        if (Value == 0)
        {
            StopSpring();
            InvalidateVisual();
        }
    }

    #endregion
}
