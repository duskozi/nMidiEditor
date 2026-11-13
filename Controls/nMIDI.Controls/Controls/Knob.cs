using System.Windows;
using System.Windows.Media;
using nMIDI.Controls.Primitives;

namespace nMIDI.Controls;

/// <summary>
/// Rotary parameter knob. Drag vertically (Shift = fine), wheel, arrows; double-click / Ctrl+click resets.
/// Set <see cref="IsBipolar"/> for pan/detune style knobs whose ring grows from <see cref="DawRangeControl.DefaultValue"/>.
/// </summary>
public class Knob : DawRangeControl
{
    static Knob()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Knob), new FrameworkPropertyMetadata(typeof(Knob)));
    }

    /// <summary>Identifies the <see cref="MinAngle"/> dependency property.</summary>
    public static readonly DependencyProperty MinAngleProperty = DependencyProperty.Register(
        nameof(MinAngle), typeof(double), typeof(Knob), new FrameworkPropertyMetadata(-135.0, OnGeometryChanged));

    /// <summary>Angle at minimum, degrees from 12 o'clock (default -135).</summary>
    public double MinAngle
    {
        get => (double)GetValue(MinAngleProperty);
        set => SetValue(MinAngleProperty, value);
    }

    /// <summary>Identifies the <see cref="MaxAngle"/> dependency property.</summary>
    public static readonly DependencyProperty MaxAngleProperty = DependencyProperty.Register(
        nameof(MaxAngle), typeof(double), typeof(Knob), new FrameworkPropertyMetadata(135.0, OnGeometryChanged));

    /// <summary>Angle at maximum, degrees from 12 o'clock (default 135).</summary>
    public double MaxAngle
    {
        get => (double)GetValue(MaxAngleProperty);
        set => SetValue(MaxAngleProperty, value);
    }

    /// <summary>Identifies the <see cref="IsBipolar"/> dependency property.</summary>
    public static readonly DependencyProperty IsBipolarProperty = DependencyProperty.Register(
        nameof(IsBipolar), typeof(bool), typeof(Knob), new FrameworkPropertyMetadata(false, OnGeometryChanged));

    /// <summary>When true the value ring starts at the default value instead of the minimum.</summary>
    public bool IsBipolar
    {
        get => (bool)GetValue(IsBipolarProperty);
        set => SetValue(IsBipolarProperty, value);
    }

    /// <summary>Identifies the <see cref="ArcBrush"/> dependency property.</summary>
    public static readonly DependencyProperty ArcBrushProperty = DependencyProperty.Register(
        nameof(ArcBrush), typeof(Brush), typeof(Knob), new FrameworkPropertyMetadata(null));

    /// <summary>Colour of the value ring (defaults to the skin accent; set per knob to colour-code parameters).</summary>
    public Brush? ArcBrush
    {
        get => (Brush?)GetValue(ArcBrushProperty);
        set => SetValue(ArcBrushProperty, value);
    }

    private static readonly DependencyPropertyKey IndicatorAnglePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IndicatorAngle), typeof(double), typeof(Knob), new FrameworkPropertyMetadata(-135.0));

    /// <summary>Identifies the <see cref="IndicatorAngle"/> dependency property.</summary>
    public static readonly DependencyProperty IndicatorAngleProperty = IndicatorAnglePropertyKey.DependencyProperty;

    /// <summary>Current pointer angle in degrees (bind a RotateTransform to it).</summary>
    public double IndicatorAngle => (double)GetValue(IndicatorAngleProperty);

    private static readonly DependencyPropertyKey ArcOriginPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(ArcOrigin), typeof(double), typeof(Knob), new FrameworkPropertyMetadata(0.0));

    /// <summary>Identifies the <see cref="ArcOrigin"/> dependency property.</summary>
    public static readonly DependencyProperty ArcOriginProperty = ArcOriginPropertyKey.DependencyProperty;

    /// <summary>Normalized start of the value ring: 0, or the default value when bipolar.</summary>
    public double ArcOrigin => (double)GetValue(ArcOriginProperty);

    private static void OnGeometryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((Knob)d).UpdateGeometry();

    /// <inheritdoc/>
    protected override void OnDerivedValuesUpdated() => UpdateGeometry();

    private void UpdateGeometry()
    {
        SetValue(IndicatorAnglePropertyKey, MinAngle + (MaxAngle - MinAngle) * NormalizedValue);
        SetValue(ArcOriginPropertyKey, IsBipolar ? NormalizedDefault : 0.0);
    }
}
