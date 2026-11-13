using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using nMIDI.Controls.Primitives;

namespace nMIDI.Controls;

/// <summary>
/// Linear fader. Grab anywhere and drag (relative, no jump) or enable <see cref="IsMoveToPointEnabled"/>.
/// For a mixer volume fader use <c>Taper="Decibel" Minimum="-70" Maximum="6" MinimumText="-∞"</c>.
/// The template must contain a <see cref="FaderPanel"/> named <c>PART_Track</c>.
/// </summary>
[TemplatePart(Name = PartTrack, Type = typeof(FaderPanel))]
public class Fader : DawRangeControl
{
    /// <summary>Template part name of the track the cap moves along.</summary>
    public const string PartTrack = "PART_Track";

    private FaderPanel? _track;

    static Fader()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Fader), new FrameworkPropertyMetadata(typeof(Fader)));
    }

    /// <summary>Identifies the <see cref="Orientation"/> dependency property.</summary>
    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        nameof(Orientation), typeof(Orientation), typeof(Fader), new FrameworkPropertyMetadata(Orientation.Vertical));

    /// <summary>Vertical (default) or horizontal.</summary>
    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>Identifies the <see cref="ShowValueText"/> dependency property.</summary>
    public static readonly DependencyProperty ShowValueTextProperty = DependencyProperty.Register(
        nameof(ShowValueText), typeof(bool), typeof(Fader), new FrameworkPropertyMetadata(true));

    /// <summary>Show the value readout under the fader (turn off when the host shows it elsewhere).</summary>
    public bool ShowValueText
    {
        get => (bool)GetValue(ShowValueTextProperty);
        set => SetValue(ShowValueTextProperty, value);
    }

    /// <summary>Identifies the <see cref="IsMoveToPointEnabled"/> dependency property.</summary>
    public static readonly DependencyProperty IsMoveToPointEnabledProperty = DependencyProperty.Register(
        nameof(IsMoveToPointEnabled), typeof(bool), typeof(Fader), new FrameworkPropertyMetadata(false));

    /// <summary>Clicking the track (not the cap) jumps the cap to the mouse.</summary>
    public bool IsMoveToPointEnabled
    {
        get => (bool)GetValue(IsMoveToPointEnabledProperty);
        set => SetValue(IsMoveToPointEnabledProperty, value);
    }

    /// <inheritdoc/>
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _track = GetTemplateChild(PartTrack) as FaderPanel;
    }

    /// <inheritdoc/>
    protected override void OnDragBegin(MouseButtonEventArgs e)
    {
        if (!IsMoveToPointEnabled || _track is null) return;
        if (_track.Thumb is UIElement thumb && thumb.IsMouseOver) return; // grabbing the cap never jumps
        SetNormalizedValue(_track.PointToNormalized(e.GetPosition(_track)));
        base.OnDragBegin(e);
    }

    /// <summary>1:1 tracking: the cap stays under the mouse while dragging.</summary>
    protected override double GetDragDelta(Point previous, Point current)
    {
        double travel = _track?.TravelLength ?? 0;
        if (travel <= 1)
            return base.GetDragDelta(previous, current);
        return Orientation == Orientation.Vertical
            ? (previous.Y - current.Y) / travel
            : (current.X - previous.X) / travel;
    }
}
