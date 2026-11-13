using System;
using System.Windows;
using System.Windows.Controls;

namespace nMIDI.Controls.Primitives;

/// <summary>The part an element plays in a <see cref="FaderPanel"/>.</summary>
public enum FaderPartRole
{
    /// <summary>Stretched over the whole travel (groove, tick marks).</summary>
    Track,
    /// <summary>Positioned along the travel according to the value (the fader cap).</summary>
    Thumb,
    /// <summary>Spans from the start of travel to the thumb centre (level fill).</summary>
    Fill,
}

/// <summary>
/// Layout panel used inside fader templates. Skins can put any visuals in it and tag them with
/// <see cref="RoleProperty"/>; the panel does the geometry so templates stay purely visual.
/// </summary>
public class FaderPanel : Panel
{
    /// <summary>Identifies the <see cref="NormalizedValue"/> dependency property.</summary>
    public static readonly DependencyProperty NormalizedValueProperty = DependencyProperty.Register(
        nameof(NormalizedValue), typeof(double), typeof(FaderPanel),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsArrange));

    /// <summary>Cap position, 0 to 1.</summary>
    public double NormalizedValue
    {
        get => (double)GetValue(NormalizedValueProperty);
        set => SetValue(NormalizedValueProperty, value);
    }

    /// <summary>Identifies the <see cref="Orientation"/> dependency property.</summary>
    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        nameof(Orientation), typeof(Orientation), typeof(FaderPanel),
        new FrameworkPropertyMetadata(Orientation.Vertical, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Vertical (default) or horizontal.</summary>
    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>Identifies the Role attached property (see <see cref="GetRole"/>).</summary>
    public static readonly DependencyProperty RoleProperty = DependencyProperty.RegisterAttached(
        "Role", typeof(FaderPartRole), typeof(FaderPanel),
        new FrameworkPropertyMetadata(FaderPartRole.Track, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    /// <summary>The part an element plays in the fader.</summary>
    public static FaderPartRole GetRole(UIElement element) => (FaderPartRole)element.GetValue(RoleProperty);

    /// <summary>Sets the part an element plays in the fader.</summary>
    public static void SetRole(UIElement element, FaderPartRole value) => element.SetValue(RoleProperty, value);

    /// <summary>Length the thumb centre can travel, in DIPs (track length minus thumb length).</summary>
    public double TravelLength { get; private set; }

    /// <summary>Thumb length along the travel axis (from the last arrange).</summary>
    public double ThumbLength { get; private set; }

    /// <summary>The element tagged as thumb, if any.</summary>
    public UIElement? Thumb { get; private set; }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        double w = 0, h = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(availableSize);
            w = Math.Max(w, child.DesiredSize.Width);
            h = Math.Max(h, child.DesiredSize.Height);
        }
        return new Size(w, h);
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        bool vertical = Orientation == Orientation.Vertical;
        double length = vertical ? finalSize.Height : finalSize.Width;

        Thumb = null;
        ThumbLength = 0;
        foreach (UIElement child in InternalChildren)
        {
            if (GetRole(child) == FaderPartRole.Thumb)
            {
                Thumb = child;
                ThumbLength = vertical ? child.DesiredSize.Height : child.DesiredSize.Width;
                break;
            }
        }

        TravelLength = Math.Max(0, length - ThumbLength);
        double n = Math.Clamp(NormalizedValue, 0, 1);
        // Vertical: value 1 at the top. Horizontal: value 1 at the right.
        double thumbStart = vertical ? (1 - n) * TravelLength : n * TravelLength;
        double thumbCenter = thumbStart + ThumbLength / 2;

        foreach (UIElement child in InternalChildren)
        {
            Rect slot;
            switch (GetRole(child))
            {
                case FaderPartRole.Thumb:
                    slot = vertical
                        ? new Rect(0, thumbStart, finalSize.Width, ThumbLength)
                        : new Rect(thumbStart, 0, ThumbLength, finalSize.Height);
                    break;
                case FaderPartRole.Fill:
                    slot = vertical
                        ? new Rect(0, thumbCenter, finalSize.Width, Math.Max(0, length - thumbCenter - ThumbLength / 2))
                        : new Rect(ThumbLength / 2, 0, Math.Max(0, thumbCenter - ThumbLength / 2), finalSize.Height);
                    break;
                default:
                    slot = vertical
                        ? new Rect(0, ThumbLength / 2, finalSize.Width, TravelLength)
                        : new Rect(ThumbLength / 2, 0, TravelLength, finalSize.Height);
                    break;
            }
            child.Arrange(slot);
        }
        return finalSize;
    }

    /// <summary>Converts a point in panel coordinates to a normalized value (thumb centre under the point).</summary>
    public double PointToNormalized(Point p)
    {
        if (TravelLength <= 0) return 0;
        bool vertical = Orientation == Orientation.Vertical;
        double pos = (vertical ? p.Y : p.X) - ThumbLength / 2;
        double n = Math.Clamp(pos / TravelLength, 0, 1);
        return vertical ? 1 - n : n;
    }
}
