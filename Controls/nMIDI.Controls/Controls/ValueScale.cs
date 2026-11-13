using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using nMIDI.Controls.Primitives;

namespace nMIDI.Controls;

/// <summary>Side of a <see cref="ValueScale"/> the ticks are drawn on.</summary>
public enum ScaleTickPlacement
{
    /// <summary>Ticks on the left (vertical) / top (horizontal) edge, labels beside them.</summary>
    Start,
    /// <summary>Ticks on the right / bottom edge.</summary>
    End,
    /// <summary>Labels centred, short ticks on both edges.</summary>
    Both,
}

/// <summary>
/// Tick + label scale for faders and meters. Uses the same <see cref="ValueTaper"/> as the control it sits
/// next to, so labels line up exactly. Use <see cref="Control.Padding"/> to match the control's travel
/// (e.g. half the fader cap height top and bottom).
/// </summary>
public class ValueScale : DawRenderControl
{
    static ValueScale()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ValueScale), new FrameworkPropertyMetadata(typeof(ValueScale)));
        FocusableProperty.OverrideMetadata(typeof(ValueScale), new FrameworkPropertyMetadata(false));
        IsHitTestVisibleProperty.OverrideMetadata(typeof(ValueScale), new UIPropertyMetadata(false));
        PaddingProperty.OverrideMetadata(typeof(ValueScale), new FrameworkPropertyMetadata(new Thickness(0), FrameworkPropertyMetadataOptions.AffectsRender));
    }

    /// <summary>Identifies the <see cref="Minimum"/> dependency property.</summary>
    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(ValueScale),
        new FrameworkPropertyMetadata(-70.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Value at the bottom (or left) end.</summary>
    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }

    /// <summary>Identifies the <see cref="Maximum"/> dependency property.</summary>
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(ValueScale),
        new FrameworkPropertyMetadata(6.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Value at the top (or right) end.</summary>
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }

    /// <summary>Identifies the <see cref="Taper"/> dependency property.</summary>
    public static readonly DependencyProperty TaperProperty = DependencyProperty.Register(
        nameof(Taper), typeof(ValueTaper), typeof(ValueScale),
        new FrameworkPropertyMetadata(ValueTaper.Linear, FrameworkPropertyMetadataOptions.AffectsRender), v => v is ValueTaper);

    /// <summary>Mapping from value to position; use the same taper as the control the scale belongs to.</summary>
    public ValueTaper Taper { get => (ValueTaper)GetValue(TaperProperty); set => SetValue(TaperProperty, value); }

    /// <summary>Identifies the <see cref="Ticks"/> dependency property.</summary>
    public static readonly DependencyProperty TicksProperty = DependencyProperty.Register(
        nameof(Ticks), typeof(DoubleCollection), typeof(ValueScale),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Values to label, e.g. <c>Ticks="6 0 -6 -12 -24 -48 -70"</c>.</summary>
    public DoubleCollection? Ticks { get => (DoubleCollection?)GetValue(TicksProperty); set => SetValue(TicksProperty, value); }

    /// <summary>Identifies the <see cref="Orientation"/> dependency property.</summary>
    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        nameof(Orientation), typeof(Orientation), typeof(ValueScale),
        new FrameworkPropertyMetadata(Orientation.Vertical, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Vertical (default) or horizontal.</summary>
    public Orientation Orientation { get => (Orientation)GetValue(OrientationProperty); set => SetValue(OrientationProperty, value); }

    /// <summary>Identifies the <see cref="Placement"/> dependency property.</summary>
    public static readonly DependencyProperty PlacementProperty = DependencyProperty.Register(
        nameof(Placement), typeof(ScaleTickPlacement), typeof(ValueScale),
        new FrameworkPropertyMetadata(ScaleTickPlacement.Start, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Side of the scale the ticks are on.</summary>
    public ScaleTickPlacement Placement { get => (ScaleTickPlacement)GetValue(PlacementProperty); set => SetValue(PlacementProperty, value); }

    /// <summary>Identifies the <see cref="TickLength"/> dependency property.</summary>
    public static readonly DependencyProperty TickLengthProperty = DependencyProperty.Register(
        nameof(TickLength), typeof(double), typeof(ValueScale),
        new FrameworkPropertyMetadata(4.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Length of the tick marks in pixels.</summary>
    public double TickLength { get => (double)GetValue(TickLengthProperty); set => SetValue(TickLengthProperty, value); }

    /// <summary>Identifies the <see cref="TickBrush"/> dependency property.</summary>
    public static readonly DependencyProperty TickBrushProperty = DependencyProperty.Register(
        nameof(TickBrush), typeof(Brush), typeof(ValueScale),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Brush for the ticks and labels.</summary>
    public Brush? TickBrush { get => (Brush?)GetValue(TickBrushProperty); set => SetValue(TickBrushProperty, value); }

    /// <summary>Identifies the <see cref="LabelFormat"/> dependency property.</summary>
    public static readonly DependencyProperty LabelFormatProperty = DependencyProperty.Register(
        nameof(LabelFormat), typeof(string), typeof(ValueScale),
        new FrameworkPropertyMetadata("{0:0}", FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Format of the labels, for example "{0:0}" or "{0:0.0} dB".</summary>
    public string LabelFormat { get => (string)GetValue(LabelFormatProperty); set => SetValue(LabelFormatProperty, value); }

    /// <summary>Identifies the <see cref="MinimumText"/> dependency property.</summary>
    public static readonly DependencyProperty MinimumTextProperty = DependencyProperty.Register(
        nameof(MinimumText), typeof(string), typeof(ValueScale),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Label used for a tick at <see cref="Minimum"/> (e.g. "-∞").</summary>
    public string? MinimumText { get => (string?)GetValue(MinimumTextProperty); set => SetValue(MinimumTextProperty, value); }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size constraint)
    {
        // Reserve room for the widest label across the axis.
        double labelExtent = 0;
        double ppd = new PixelSnapper(this).PixelsPerDip;
        if (Ticks is { Count: > 0 } ticks)
        {
            foreach (double t in ticks)
            {
                FormattedText ft = MakeText(Label(t), ppd);
                labelExtent = Math.Max(labelExtent, Orientation == Orientation.Vertical ? ft.Width : ft.Height);
            }
        }
        double cross = labelExtent + TickLength + 3;
        return Orientation == Orientation.Vertical
            ? new Size(Math.Min(cross, constraint.Width), 0)
            : new Size(0, Math.Min(cross, constraint.Height));
    }

    /// <inheritdoc/>
    protected override void OnRender(DrawingContext dc)
    {
        if (Ticks is not { Count: > 0 } ticks) return;
        var snap = new PixelSnapper(this);
        Pen? pen = FrozenPens.Create(TickBrush, snap.OnePixel);
        bool vertical = Orientation == Orientation.Vertical;
        Thickness pad = Padding;
        double w = ActualWidth, h = ActualHeight;
        double start = vertical ? pad.Top : pad.Left;
        double length = vertical ? h - pad.Top - pad.Bottom : w - pad.Left - pad.Right;
        if (length <= 0) return;
        ValueTaper taper = Taper ?? ValueTaper.Linear;
        double tick = TickLength;

        foreach (double t in ticks)
        {
            double n = taper.ToNormalized(t, Minimum, Maximum);
            double pos = vertical ? start + (1 - n) * length : start + n * length;
            FormattedText ft = MakeText(Label(t), snap.PixelsPerDip);

            if (vertical)
            {
                double y = snap.LineY(pos);
                switch (Placement)
                {
                    case ScaleTickPlacement.Start:
                        if (pen is not null) dc.DrawLine(pen, new Point(0, y), new Point(tick, y));
                        dc.DrawText(ft, new Point(tick + 2, pos - ft.Height / 2));
                        break;
                    case ScaleTickPlacement.End:
                        if (pen is not null) dc.DrawLine(pen, new Point(w - tick, y), new Point(w, y));
                        dc.DrawText(ft, new Point(w - tick - 2 - ft.Width, pos - ft.Height / 2));
                        break;
                    default:
                        if (pen is not null)
                        {
                            dc.DrawLine(pen, new Point(0, y), new Point(tick / 2, y));
                            dc.DrawLine(pen, new Point(w - tick / 2, y), new Point(w, y));
                        }
                        dc.DrawText(ft, new Point((w - ft.Width) / 2, pos - ft.Height / 2));
                        break;
                }
            }
            else
            {
                double x = snap.LineX(pos);
                bool atStart = Placement != ScaleTickPlacement.End;
                if (pen is not null)
                    dc.DrawLine(pen, new Point(x, atStart ? 0 : h - tick), new Point(x, atStart ? tick : h));
                dc.DrawText(ft, new Point(pos - ft.Width / 2, atStart ? tick + 1 : h - tick - 1 - ft.Height));
            }
        }
    }

    private string Label(double value)
    {
        if (MinimumText is { } minText && value <= Minimum) return minText;
        try
        {
            return string.Format(CultureInfo.CurrentCulture, LabelFormat ?? "{0}", value);
        }
        catch (FormatException)
        {
            return value.ToString(CultureInfo.CurrentCulture);
        }
    }
}
