using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace nMIDI.Controls.Primitives;

/// <summary>
/// Drop-in replacement for <see cref="Border"/> that draws its background and outline as a generated
/// nine-patch (<see cref="NineSkins"/>): same property names (Background, BorderBrush, BorderThickness,
/// CornerRadius, Padding), so templates and their triggers work unchanged, plus <see cref="Bevel"/>.
/// <para>Solid and two-stop vertical gradient brushes (not animated) with a uniform border and corner radius become
/// a nine-patch. Everything else — one-sided borders, mixed corner radii, animated brushes, more gradient
/// stops, image or visual brushes — is drawn with WPF geometry, the way <see cref="Border"/> draws it.</para>
/// </summary>
public class NineFrame : Decorator
{
    /// <summary>Identifies the <see cref="Background"/> dependency property.</summary>
    public static readonly DependencyProperty BackgroundProperty = Panel.BackgroundProperty.AddOwner(typeof(NineFrame),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Identifies the <see cref="BorderBrush"/> dependency property.</summary>
    public static readonly DependencyProperty BorderBrushProperty = Border.BorderBrushProperty.AddOwner(typeof(NineFrame),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Identifies the <see cref="BorderThickness"/> dependency property.</summary>
    public static readonly DependencyProperty BorderThicknessProperty = Border.BorderThicknessProperty.AddOwner(typeof(NineFrame),
        new FrameworkPropertyMetadata(new Thickness(), FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Identifies the <see cref="CornerRadius"/> dependency property.</summary>
    public static readonly DependencyProperty CornerRadiusProperty = Border.CornerRadiusProperty.AddOwner(typeof(NineFrame),
        new FrameworkPropertyMetadata(new CornerRadius(), FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Identifies the <see cref="Padding"/> dependency property.</summary>
    public static readonly DependencyProperty PaddingProperty = Border.PaddingProperty.AddOwner(typeof(NineFrame),
        new FrameworkPropertyMetadata(new Thickness(), FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Identifies the <see cref="Bevel"/> dependency property.</summary>
    public static readonly DependencyProperty BevelProperty = DependencyProperty.Register(nameof(Bevel), typeof(FrameStyle), typeof(NineFrame),
        new FrameworkPropertyMetadata(FrameStyle.Flat, FrameworkPropertyMetadataOptions.AffectsRender));

    static NineFrame()
    {
        SnapsToDevicePixelsProperty.OverrideMetadata(typeof(NineFrame), new FrameworkPropertyMetadata(true));
    }

    /// <summary>Fill: a solid colour or a two-stop vertical gradient becomes the nine-patch.</summary>
    public Brush? Background { get => (Brush?)GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }
    /// <summary>Outline colour (solid).</summary>
    public Brush? BorderBrush { get => (Brush?)GetValue(BorderBrushProperty); set => SetValue(BorderBrushProperty, value); }
    /// <summary>Outline width in pixels; a different width per side is drawn like <see cref="System.Windows.Controls.Border"/>.</summary>
    public Thickness BorderThickness { get => (Thickness)GetValue(BorderThicknessProperty); set => SetValue(BorderThicknessProperty, value); }
    /// <summary>Corner radius in pixels.</summary>
    public CornerRadius CornerRadius { get => (CornerRadius)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
    /// <summary>Space between the frame and its child.</summary>
    public Thickness Padding { get => (Thickness)GetValue(PaddingProperty); set => SetValue(PaddingProperty, value); }

    /// <summary>Shading of the generated frame.</summary>
    public FrameStyle Bevel { get => (FrameStyle)GetValue(BevelProperty); set => SetValue(BevelProperty, value); }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size constraint)
    {
        Thickness b = BorderThickness, p = Padding;
        double w = b.Left + b.Right + p.Left + p.Right, h = b.Top + b.Bottom + p.Top + p.Bottom;
        if (Child is { } child)
        {
            child.Measure(new Size(Math.Max(0, constraint.Width - w), Math.Max(0, constraint.Height - h)));
            return new Size(child.DesiredSize.Width + w, child.DesiredSize.Height + h);
        }
        return new Size(w, h);
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size arrangeSize)
    {
        Thickness b = BorderThickness, p = Padding;
        Child?.Arrange(new Rect(b.Left + p.Left, b.Top + p.Top,
            Math.Max(0, arrangeSize.Width - b.Left - b.Right - p.Left - p.Right),
            Math.Max(0, arrangeSize.Height - b.Top - b.Bottom - p.Top - p.Bottom)));
        return arrangeSize;
    }

    /// <summary>Redraw at the new monitor's DPI: the frame is rendered at device-pixel size.</summary>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        InvalidateVisual();
    }

    /// <inheritdoc/>
    protected override void OnRender(DrawingContext dc)
    {
        var rect = new Rect(RenderSize);
        if (rect.Width <= 0 || rect.Height <= 0) return;

        Brush? bg = Background, stroke = BorderBrush;
        Thickness t = BorderThickness;
        CornerRadius cr = CornerRadius;
        bool uniformThickness = t.Left == t.Top && t.Top == t.Right && t.Right == t.Bottom;
        bool uniformRadius = cr.TopLeft == cr.TopRight && cr.TopRight == cr.BottomRight && cr.BottomRight == cr.BottomLeft;
        int strokeWidth = stroke is null ? 0 : (int)Math.Round(t.Left);
        if (bg is null && (stroke is null || IsZero(t))) return;

        // The nine-patch path covers uniform borders and radii with frozen solid or two-stop brushes. Anything
        // else (one-sided borders, mixed corners, animated or multi-stop brushes, images) is drawn like Border.
        if (uniformThickness && uniformRadius && t.Left == strokeWidth && IsStatic(bg) && IsStatic(stroke)
            && TryFrame(bg, stroke, strokeWidth, cr.TopLeft, Bevel, out var spec))
        {
            NinePainter.Draw(dc, this, spec, rect);
            return;
        }
        DrawLikeBorder(dc, rect, bg, stroke, t, cr);
    }

    private static bool IsZero(Thickness t) => t.Left == 0 && t.Top == 0 && t.Right == 0 && t.Bottom == 0;

    /// <summary>
    /// A brush the nine-patch can stand for: not being animated (each animation frame would make a new image),
    /// solid or with at most two stops. A brush changed in code just redraws with its new colours.
    /// </summary>
    private static bool IsStatic(Brush? b) => b switch
    {
        null => true,
        SolidColorBrush s => s.IsFrozen || !s.HasAnimatedProperties,
        LinearGradientBrush g => (g.IsFrozen || !Animated(g)) && g.GradientStops.Count <= 2,
        _ => false,
    };

    private static bool Animated(GradientBrush g)
    {
        if (g.HasAnimatedProperties) return true;
        foreach (var stop in g.GradientStops) if (stop.HasAnimatedProperties) return true;
        return false;
    }

    /// <summary>Background and outline drawn as WPF geometry, with per-side thickness and per-corner radius.</summary>
    private static void DrawLikeBorder(DrawingContext dc, Rect rect, Brush? bg, Brush? stroke, Thickness t, CornerRadius cr)
    {
        var outer = RoundedRect(rect, cr);
        if (stroke is not null && !IsZero(t))
        {
            var innerRect = new Rect(rect.X + t.Left, rect.Y + t.Top,
                Math.Max(0, rect.Width - t.Left - t.Right), Math.Max(0, rect.Height - t.Top - t.Bottom));
            var innerRadius = new CornerRadius(
                Math.Max(0, cr.TopLeft - Math.Max(t.Left, t.Top) / 2), Math.Max(0, cr.TopRight - Math.Max(t.Right, t.Top) / 2),
                Math.Max(0, cr.BottomRight - Math.Max(t.Right, t.Bottom) / 2), Math.Max(0, cr.BottomLeft - Math.Max(t.Left, t.Bottom) / 2));
            var inner = RoundedRect(innerRect, innerRadius);
            if (bg is not null) dc.DrawGeometry(bg, null, inner);
            dc.DrawGeometry(stroke, null, new CombinedGeometry(GeometryCombineMode.Exclude, outer, inner));
        }
        else if (bg is not null)
        {
            dc.DrawGeometry(bg, null, outer);
        }
    }

    private static StreamGeometry RoundedRect(Rect r, CornerRadius cr)
    {
        double max = Math.Min(r.Width, r.Height) / 2;
        double tl = Math.Min(cr.TopLeft, max), tr = Math.Min(cr.TopRight, max), br = Math.Min(cr.BottomRight, max), bl = Math.Min(cr.BottomLeft, max);
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(r.Left + tl, r.Top), true, true);
            c.LineTo(new Point(r.Right - tr, r.Top), true, false);
            if (tr > 0) c.ArcTo(new Point(r.Right, r.Top + tr), new Size(tr, tr), 0, false, SweepDirection.Clockwise, true, false);
            c.LineTo(new Point(r.Right, r.Bottom - br), true, false);
            if (br > 0) c.ArcTo(new Point(r.Right - br, r.Bottom), new Size(br, br), 0, false, SweepDirection.Clockwise, true, false);
            c.LineTo(new Point(r.Left + bl, r.Bottom), true, false);
            if (bl > 0) c.ArcTo(new Point(r.Left, r.Bottom - bl), new Size(bl, bl), 0, false, SweepDirection.Clockwise, true, false);
            c.LineTo(new Point(r.Left, r.Top + tl), true, false);
            if (tl > 0) c.ArcTo(new Point(r.Left + tl, r.Top), new Size(tl, tl), 0, false, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        return g;
    }

    /// <summary>Turn brushes into a frame spec; false when a brush is not a solid colour or a simple gradient.</summary>
    internal static bool TryFrame(Brush? background, Brush? stroke, int strokeWidth, double radius, FrameStyle style, out FrameSpec spec)
    {
        spec = default;
        if (!BrushColors.TryGet(background, out var top, out var bottom)) return false;
        Color strokeColor = Colors.Transparent;
        if (strokeWidth > 0 && !BrushColors.TryGetSolid(stroke, out strokeColor)) return false;
        spec = new FrameSpec(top, bottom, strokeColor, (int)Math.Round(radius), strokeWidth, style);
        return true;
    }
}

/// <summary>Reads the colours of the brushes skins use, for building nine-patches from them.</summary>
internal static class BrushColors
{
    /// <summary>Solid → one colour twice; linear/radial gradient → first and last stop (top/bottom). Null → transparent.</summary>
    public static bool TryGet(Brush? brush, out Color top, out Color bottom)
    {
        switch (brush)
        {
            case null:
                top = bottom = Colors.Transparent;
                return true;
            case SolidColorBrush s:
                top = bottom = WithOpacity(s.Color, s.Opacity);
                return true;
            case GradientBrush g when g.GradientStops.Count > 0:
                GradientStop first = g.GradientStops[0], last = g.GradientStops[0];
                foreach (var stop in g.GradientStops)
                {
                    if (stop.Offset < first.Offset) first = stop;
                    if (stop.Offset >= last.Offset) last = stop;
                }
                // A horizontal linear gradient has no top/bottom; use its average so the look stays close.
                if (g is LinearGradientBrush lin && Math.Abs(lin.EndPoint.X - lin.StartPoint.X) > Math.Abs(lin.EndPoint.Y - lin.StartPoint.Y))
                {
                    top = bottom = WithOpacity(Mix(first.Color, last.Color), g.Opacity);
                    return true;
                }
                top = WithOpacity(first.Color, g.Opacity);
                bottom = WithOpacity(last.Color, g.Opacity);
                return true;
            default:
                top = bottom = default;
                return false;
        }
    }

    /// <summary>The single colour of a brush (a gradient gives its average).</summary>
    public static bool TryGetSolid(Brush? brush, out Color color)
    {
        if (TryGet(brush, out var a, out var b)) { color = Mix(a, b); return true; }
        color = default;
        return false;
    }

    public static Color WithOpacity(Color c, double opacity) =>
        opacity >= 1 ? c : Color.FromArgb((byte)Math.Round(c.A * Math.Clamp(opacity, 0, 1)), c.R, c.G, c.B);

    public static Color Mix(Color a, Color b) =>
        Color.FromArgb((byte)((a.A + b.A) / 2), (byte)((a.R + b.R) / 2), (byte)((a.G + b.G) / 2), (byte)((a.B + b.B) / 2));
}
