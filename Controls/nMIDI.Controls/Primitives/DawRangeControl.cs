using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace nMIDI.Controls.Primitives;

/// <summary>
/// Shared behaviour for every continuous DAW parameter control (knob, fader, value box):
/// <list type="bullet">
/// <item>relative drag (no value jump when grabbing), Shift = fine adjust</item>
/// <item>double-click or Ctrl+click resets to <see cref="DefaultValue"/></item>
/// <item>mouse wheel and keyboard (arrows, PageUp/Down, Home/End, Delete = reset)</item>
/// <item>pluggable <see cref="Taper"/> (linear, log, dB) and <see cref="Step"/> quantization</item>
/// <item><see cref="DragStarted"/>/<see cref="DragCompleted"/> for automation touch &amp; undo grouping</item>
/// </list>
/// Value semantics come from <see cref="RangeBase"/>, so binding, coercion and UI Automation behave like Slider.
/// </summary>
public abstract class DawRangeControl : RangeBase
{
    /// <summary>Normalized change per wheel notch / arrow key press.</summary>
    protected const double CoarseStep = 0.01;
    /// <summary>Fraction of the range moved by Page Up / Page Down.</summary>
    protected const double PageStep = 0.1;

    private Point _lastDragPoint;
    private double _dragNormalized;
    private bool _dragMoved;
    private bool _updatingDerived;

    static DawRangeControl()
    {
        FocusableProperty.OverrideMetadata(typeof(DawRangeControl), new FrameworkPropertyMetadata(true));
    }

    /// <summary>Creates a <see cref="DawRangeControl"/>.</summary>
    protected DawRangeControl()
    {
        UpdateDerivedValues();
    }

    #region Dependency properties

    /// <summary>Identifies the <see cref="Taper"/> dependency property.</summary>
    public static readonly DependencyProperty TaperProperty = DependencyProperty.Register(
        nameof(Taper), typeof(ValueTaper), typeof(DawRangeControl),
        new FrameworkPropertyMetadata(ValueTaper.Linear, OnDerivedInputChanged), v => v is ValueTaper);

    /// <summary>Mapping between value and control position. Never null.</summary>
    public ValueTaper Taper
    {
        get => (ValueTaper)GetValue(TaperProperty);
        set => SetValue(TaperProperty, value);
    }

    /// <summary>Identifies the <see cref="DefaultValue"/> dependency property.</summary>
    public static readonly DependencyProperty DefaultValueProperty = DependencyProperty.Register(
        nameof(DefaultValue), typeof(double), typeof(DawRangeControl),
        new FrameworkPropertyMetadata(0.0, OnDerivedInputChanged), IsFinite);

    /// <summary>Value restored by double-click / Ctrl+click / Delete. Also the origin of bipolar arcs.</summary>
    public double DefaultValue
    {
        get => (double)GetValue(DefaultValueProperty);
        set => SetValue(DefaultValueProperty, value);
    }

    /// <summary>Identifies the <see cref="Step"/> dependency property.</summary>
    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
        nameof(Step), typeof(double), typeof(DawRangeControl),
        new FrameworkPropertyMetadata(0.0), v => v is double d && d >= 0 && !double.IsInfinity(d));

    /// <summary>Quantization applied to user input, in value units. 0 = continuous.</summary>
    public double Step
    {
        get => (double)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    /// <summary>Identifies the <see cref="DragSensitivity"/> dependency property.</summary>
    public static readonly DependencyProperty DragSensitivityProperty = DependencyProperty.Register(
        nameof(DragSensitivity), typeof(double), typeof(DawRangeControl),
        new FrameworkPropertyMetadata(200.0), v => v is double d && d > 0 && !double.IsInfinity(d));

    /// <summary>Pixels of mouse travel for the full range (knobs and value boxes).</summary>
    public double DragSensitivity
    {
        get => (double)GetValue(DragSensitivityProperty);
        set => SetValue(DragSensitivityProperty, value);
    }

    /// <summary>Identifies the <see cref="FineFactor"/> dependency property.</summary>
    public static readonly DependencyProperty FineFactorProperty = DependencyProperty.Register(
        nameof(FineFactor), typeof(double), typeof(DawRangeControl),
        new FrameworkPropertyMetadata(0.1), v => v is double d && d > 0 && d <= 1);

    /// <summary>Speed multiplier while Shift is held.</summary>
    public double FineFactor
    {
        get => (double)GetValue(FineFactorProperty);
        set => SetValue(FineFactorProperty, value);
    }

    /// <summary>Identifies the <see cref="Label"/> dependency property.</summary>
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(DawRangeControl), new FrameworkPropertyMetadata(null));

    /// <summary>Parameter name shown by the template (e.g. "Cutoff").</summary>
    public string? Label
    {
        get => (string?)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>Identifies the <see cref="ValueStringFormat"/> dependency property.</summary>
    public static readonly DependencyProperty ValueStringFormatProperty = DependencyProperty.Register(
        nameof(ValueStringFormat), typeof(string), typeof(DawRangeControl),
        new FrameworkPropertyMetadata("{0:0.##}", OnDerivedInputChanged));

    /// <summary>Composite format used for <see cref="ValueText"/> when no <see cref="ValueFormatter"/> is set.</summary>
    public string ValueStringFormat
    {
        get => (string)GetValue(ValueStringFormatProperty);
        set => SetValue(ValueStringFormatProperty, value);
    }

    /// <summary>Identifies the <see cref="ValueFormatter"/> dependency property.</summary>
    public static readonly DependencyProperty ValueFormatterProperty = DependencyProperty.Register(
        nameof(ValueFormatter), typeof(Func<double, string>), typeof(DawRangeControl),
        new FrameworkPropertyMetadata(null, OnDerivedInputChanged));

    /// <summary>Custom value → text conversion. See <see cref="ValueFormatters"/>.</summary>
    public Func<double, string>? ValueFormatter
    {
        get => (Func<double, string>?)GetValue(ValueFormatterProperty);
        set => SetValue(ValueFormatterProperty, value);
    }

    /// <summary>Identifies the <see cref="MinimumText"/> dependency property.</summary>
    public static readonly DependencyProperty MinimumTextProperty = DependencyProperty.Register(
        nameof(MinimumText), typeof(string), typeof(DawRangeControl),
        new FrameworkPropertyMetadata(null, OnDerivedInputChanged));

    /// <summary>Text shown when the value sits at <see cref="RangeBase.Minimum"/> (e.g. "-∞" or "Off").</summary>
    public string? MinimumText
    {
        get => (string?)GetValue(MinimumTextProperty);
        set => SetValue(MinimumTextProperty, value);
    }

    private static readonly DependencyPropertyKey NormalizedValuePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(NormalizedValue), typeof(double), typeof(DawRangeControl), new FrameworkPropertyMetadata(0.0));

    /// <summary>Identifies the <see cref="NormalizedValue"/> dependency property.</summary>
    public static readonly DependencyProperty NormalizedValueProperty = NormalizedValuePropertyKey.DependencyProperty;

    /// <summary>Control position in [0,1] after applying the taper. Templates bind to this.</summary>
    public double NormalizedValue => (double)GetValue(NormalizedValueProperty);

    private static readonly DependencyPropertyKey NormalizedDefaultPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(NormalizedDefault), typeof(double), typeof(DawRangeControl), new FrameworkPropertyMetadata(0.0));

    /// <summary>Identifies the <see cref="NormalizedDefault"/> dependency property.</summary>
    public static readonly DependencyProperty NormalizedDefaultProperty = NormalizedDefaultPropertyKey.DependencyProperty;

    /// <summary><see cref="DefaultValue"/> mapped through the taper (used for bipolar arc origins and detents).</summary>
    public double NormalizedDefault => (double)GetValue(NormalizedDefaultProperty);

    private static readonly DependencyPropertyKey ValueTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(ValueText), typeof(string), typeof(DawRangeControl), new FrameworkPropertyMetadata(string.Empty));

    /// <summary>Identifies the <see cref="ValueText"/> dependency property.</summary>
    public static readonly DependencyProperty ValueTextProperty = ValueTextPropertyKey.DependencyProperty;

    /// <summary>Formatted current value.</summary>
    public string ValueText => (string)GetValue(ValueTextProperty);

    private static readonly DependencyPropertyKey IsDraggingPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsDragging), typeof(bool), typeof(DawRangeControl), new FrameworkPropertyMetadata(false));

    /// <summary>Identifies the <see cref="IsDragging"/> dependency property.</summary>
    public static readonly DependencyProperty IsDraggingProperty = IsDraggingPropertyKey.DependencyProperty;

    /// <summary>True while the user holds the control with the mouse (use in template triggers).</summary>
    public bool IsDragging
    {
        get => (bool)GetValue(IsDraggingProperty);
        private set => SetValue(IsDraggingPropertyKey, value);
    }

    #endregion

    #region Routed events

    /// <summary>Identifies the <see cref="DragStarted"/> routed event.</summary>
    public static readonly RoutedEvent DragStartedEvent = EventManager.RegisterRoutedEvent(
        nameof(DragStarted), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(DawRangeControl));

    /// <summary>User grabbed the control. Use for automation "touch" and to open an undo group.</summary>
    public event RoutedEventHandler DragStarted
    {
        add => AddHandler(DragStartedEvent, value);
        remove => RemoveHandler(DragStartedEvent, value);
    }

    /// <summary>Identifies the <see cref="DragCompleted"/> routed event.</summary>
    public static readonly RoutedEvent DragCompletedEvent = EventManager.RegisterRoutedEvent(
        nameof(DragCompleted), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(DawRangeControl));

    /// <summary>User released the control.</summary>
    public event RoutedEventHandler DragCompleted
    {
        add => AddHandler(DragCompletedEvent, value);
        remove => RemoveHandler(DragCompletedEvent, value);
    }

    #endregion

    #region Public API

    /// <summary>Sets the value from a normalized position, applying taper and step quantization.</summary>
    public void SetNormalizedValue(double normalized)
    {
        double v = Taper.FromNormalized(Math.Clamp(normalized, 0, 1), Minimum, Maximum);
        SetCurrentValue(ValueProperty, Quantize(v));
    }

    /// <summary>Restores <see cref="DefaultValue"/>.</summary>
    public void ResetToDefault() => SetCurrentValue(ValueProperty, Math.Clamp(DefaultValue, Minimum, Maximum));

    #endregion

    #region Derived values

    /// <inheritdoc/>
    protected override void OnValueChanged(double oldValue, double newValue)
    {
        base.OnValueChanged(oldValue, newValue);
        UpdateDerivedValues();
    }

    /// <inheritdoc/>
    protected override void OnMinimumChanged(double oldMinimum, double newMinimum)
    {
        base.OnMinimumChanged(oldMinimum, newMinimum);
        UpdateDerivedValues();
    }

    /// <inheritdoc/>
    protected override void OnMaximumChanged(double oldMaximum, double newMaximum)
    {
        base.OnMaximumChanged(oldMaximum, newMaximum);
        UpdateDerivedValues();
    }

    private static void OnDerivedInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((DawRangeControl)d).UpdateDerivedValues();

    private void UpdateDerivedValues()
    {
        if (_updatingDerived) return;
        _updatingDerived = true;
        try
        {
            ValueTaper taper = Taper ?? ValueTaper.Linear;
            SetValue(NormalizedValuePropertyKey, taper.ToNormalized(Value, Minimum, Maximum));
            SetValue(NormalizedDefaultPropertyKey, taper.ToNormalized(DefaultValue, Minimum, Maximum));
            SetValue(ValueTextPropertyKey, FormatValue(Value));
            OnDerivedValuesUpdated();
        }
        finally
        {
            _updatingDerived = false;
        }
    }

    /// <summary>Called after <see cref="NormalizedValue"/> / <see cref="ValueText"/> were refreshed.</summary>
    protected virtual void OnDerivedValuesUpdated() { }

    /// <summary>Formats a value with <see cref="ValueFormatter"/>, <see cref="MinimumText"/> or <see cref="ValueStringFormat"/>.</summary>
    public string FormatValue(double value)
    {
        if (MinimumText is { } minText && value <= Minimum)
            return minText;
        if (ValueFormatter is { } formatter)
            return formatter(value);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, ValueStringFormat ?? "{0}", value);
        }
        catch (FormatException)
        {
            return value.ToString(CultureInfo.CurrentCulture);
        }
    }

    /// <summary>Rounds a value to <see cref="Step"/> and clamps it to the range.</summary>
    protected double Quantize(double value)
    {
        double step = Step;
        if (step > 0)
            value = Minimum + Math.Round((value - Minimum) / step) * step;
        return Math.Clamp(value, Minimum, Maximum);
    }

    #endregion

    #region Mouse

    /// <summary>Converts a mouse movement into a normalized delta. Override for track-based controls (fader).</summary>
    protected virtual double GetDragDelta(Point previous, Point current)
        => (previous.Y - current.Y) / DragSensitivity;

    /// <summary>Hook for gestures that should not start a drag (e.g. text editing). Return true if handled.</summary>
    protected virtual bool OnResetGesture()
    {
        ResetToDefault();
        return true;
    }

    /// <inheritdoc/>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.Handled || !IsEnabled) return;

        Focus();

        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (e.ClickCount == 2 || ctrl)
        {
            if (OnResetGesture())
            {
                e.Handled = true;
                return;
            }
        }

        if (CaptureMouse())
        {
            _lastDragPoint = e.GetPosition(this);
            _dragMoved = false;
            OnDragBegin(e); // may move the value (fader move-to-point), so read the anchor afterwards
            _dragNormalized = NormalizedValue;
            IsDragging = true;
            RaiseEvent(new RoutedEventArgs(DragStartedEvent, this));
        }
        e.Handled = true;
    }

    /// <summary>Called right after mouse capture, before <see cref="DragStarted"/>.</summary>
    protected virtual void OnDragBegin(MouseButtonEventArgs e) { }

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!IsDragging || !IsMouseCaptured) return;

        Point p = e.GetPosition(this);
        double delta = GetDragDelta(_lastDragPoint, p);
        _lastDragPoint = p;
        if (delta == 0) return;

        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            delta *= FineFactor;

        // Accumulate in normalized space and clamp, so reversing direction at an end stop responds immediately.
        _dragNormalized = Math.Clamp(_dragNormalized + delta, 0, 1);
        _dragMoved = true;
        SetNormalizedValue(_dragNormalized);
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture(); // triggers OnLostMouseCapture → EndDrag
            e.Handled = true;
        }
    }

    /// <inheritdoc/>
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        EndDrag();
    }

    private void EndDrag()
    {
        if (!IsDragging) return;
        IsDragging = false;
        RaiseEvent(new RoutedEventArgs(DragCompletedEvent, this));
    }

    /// <summary>True if the last drag actually changed the value (vs. a plain click).</summary>
    protected bool LastDragMoved => _dragMoved;

    /// <inheritdoc/>
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (e.Handled || !IsEnabled) return;
        double notches = e.Delta / (double)Mouse.MouseWheelDeltaForOneLine;
        Nudge(notches, fine: (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
        e.Handled = true;
    }

    /// <summary>
    /// Moves the value by a number of steps: whole <see cref="Step"/> units when quantized,
    /// otherwise 1% of travel (or <see cref="FineFactor"/> of that when fine).
    /// </summary>
    protected void Nudge(double steps, bool fine)
    {
        if (Step > 0)
        {
            double whole = Math.Round(steps);
            if (whole == 0) whole = Math.Sign(steps); // high-resolution wheels send fractional notches
            SetCurrentValue(ValueProperty, Quantize(Value + whole * Step));
        }
        else
        {
            double n = NormalizedValue + steps * CoarseStep * (fine ? FineFactor : 1);
            SetNormalizedValue(n);
        }
    }

    #endregion

    #region Keyboard

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || !IsEnabled) return;
        bool fine = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        switch (e.Key)
        {
            case Key.Up:
            case Key.Right:
                Nudge(1, fine); break;
            case Key.Down:
            case Key.Left:
                Nudge(-1, fine); break;
            case Key.PageUp:
                SetNormalizedValue(NormalizedValue + PageStep); break;
            case Key.PageDown:
                SetNormalizedValue(NormalizedValue - PageStep); break;
            case Key.Home:
                SetCurrentValue(ValueProperty, Minimum); break;
            case Key.End:
                SetCurrentValue(ValueProperty, Maximum); break;
            case Key.Delete:
            case Key.Back:
                ResetToDefault(); break;
            default:
                return;
        }
        e.Handled = true;
    }

    #endregion

    /// <inheritdoc/>
    protected override AutomationPeer OnCreateAutomationPeer() => new DawRangeControlAutomationPeer(this);

    private static bool IsFinite(object v) => v is double d && !double.IsNaN(d) && !double.IsInfinity(d);
}

/// <summary>Exposes DAW range controls to screen readers / UI Automation as sliders.</summary>
public class DawRangeControlAutomationPeer : RangeBaseAutomationPeer
{
    /// <summary>Creates a <see cref="DawRangeControlAutomationPeer"/>.</summary>
    public DawRangeControlAutomationPeer(DawRangeControl owner) : base(owner) { }

    /// <inheritdoc/>
    protected override string GetClassNameCore() => Owner.GetType().Name;

    /// <inheritdoc/>
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;

    /// <inheritdoc/>
    protected override string GetNameCore()
    {
        string name = base.GetNameCore();
        if (string.IsNullOrEmpty(name) && Owner is DawRangeControl c && !string.IsNullOrEmpty(c.Label))
            return c.Label!;
        return name;
    }

    /// <inheritdoc/>
    protected override string GetItemStatusCore() => ((DawRangeControl)Owner).ValueText;
}
