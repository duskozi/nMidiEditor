using System;
using System.Collections.Generic;
using System.Windows.Media;
using NinePatchWpf;

namespace nMIDI.Controls.Primitives;

/// <summary>How a generated nine-patch frame is shaded.</summary>
public enum FrameStyle
{
    /// <summary>Plain fill and outline.</summary>
    Flat,
    /// <summary>Light top edge and shaded bottom edge: buttons, fader caps, raised surfaces.</summary>
    Raised,
    /// <summary>Inner shadow along the top: grooves, LCDs, meter wells, text fields.</summary>
    Sunken,
    /// <summary>Bright top edge, for lit LEDs and active step cells.</summary>
    Glow,
}

/// <summary>Everything that decides how a generated frame looks; equal specs share one image.</summary>
/// <param name="Top">Fill colour at the top (straight alpha).</param>
/// <param name="Bottom">Fill colour at the bottom.</param>
/// <param name="Stroke">Outline colour.</param>
/// <param name="Radius">Corner radius in pixels (0–12).</param>
/// <param name="StrokeWidth">Outline width in pixels (0–3).</param>
/// <param name="Style">Shading.</param>
internal readonly record struct FrameSpec(Color Top, Color Bottom, Color Stroke, int Radius, int StrokeWidth, FrameStyle Style)
{
    public static FrameSpec Solid(Color fill, Color stroke, int radius, int strokeWidth = 1, FrameStyle style = FrameStyle.Flat)
        => new(fill, fill, stroke, radius, strokeWidth, style);
}

/// <summary>
/// Builds nine-patch images in memory from colours (no image files): an anti-aliased rounded rectangle
/// with an outline, a vertical gradient and optional bevel shading. Only the middle stretches, so the
/// corners and edges stay crisp at any size, and every control that asks for the same look shares one
/// image and its rendered bitmaps.
/// <para>Used from the UI thread only.</para>
/// </summary>
internal static class NineSkins
{
    private const int StretchSize = 8;   // rows/columns of the stretchable middle; carries the gradient
    private const int MaxEntries = 4096;

    private static readonly Dictionary<FrameSpec, NinePatchImage> s_cache = new();

    /// <summary>Number of images currently generated.</summary>
    public static int Count => s_cache.Count;

    private static bool s_unavailable;

    /// <summary>
    /// False once the native renderer (NinePatchLib.dll) has failed to load or run: the DLL is missing, the
    /// VC++ runtime is missing, or the process is not 64-bit. Controls then draw plain vector shapes.
    /// </summary>
    public static bool IsNativeAvailable => !s_unavailable;

    /// <summary>True for the exceptions that mean the native renderer cannot be used.</summary>
    internal static bool IsNativeFailure(Exception ex) =>
        ex is NinePatchException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException
           or TypeInitializationException;

    /// <summary>Note that the native renderer failed, reporting it once.</summary>
    internal static void MarkUnavailable(object owner, Exception ex)
    {
        if (ex is NinePatchException) return;   // a single image failed; the renderer itself works
        if (s_unavailable) return;
        s_unavailable = true;
        NinePatchDiagnostics.Report(owner,
            "nMIDI.Controls: the native nine-patch renderer (NinePatchLib.dll, x64, needs the VC++ runtime) is not available; drawing vector shapes instead.", ex);
    }

    /// <summary>Like <see cref="Get"/>, but null when the native renderer cannot be used (reported once).</summary>
    public static NinePatchImage? TryGet(in FrameSpec spec)
    {
        if (s_unavailable) return null;
        try { return Get(spec); }
        catch (Exception ex) when (IsNativeFailure(ex))
        {
            MarkUnavailable(typeof(NineSkins), ex);
            return null;
        }
    }

    /// <summary>The shared image for <paramref name="spec"/>, generated on first use.</summary>
    public static NinePatchImage Get(in FrameSpec spec)
    {
        var key = Normalize(spec);
        if (s_cache.TryGetValue(key, out var image)) return image;
        if (s_cache.Count >= MaxEntries) Clear();
        image = Create(key);
        s_cache[key] = image;
        return image;
    }

    /// <summary>Forget all generated images (they are disposed once nothing renders them any more).</summary>
    public static void Clear()
    {
        // The images are not disposed here: a render cache may still hold bitmaps keyed by them, and an
        // element may be about to draw one. They are released when the GC collects them.
        s_cache.Clear();
    }

    /// <summary>
    /// Fixed (non-stretching) margin of the generated image on every side, in pixels: corner + outline +
    /// the bevel rows, so the bevel never lands in the stretched middle.
    /// </summary>
    public static int Margin(in FrameSpec s) =>
        Math.Max(Math.Clamp(s.Radius, 0, 12), s.Style == FrameStyle.Flat ? 0 : 2) + Math.Clamp(s.StrokeWidth, 0, 3) + 1;

    /// <summary>
    /// The spec adjusted so its fixed margins fit a target of <paramref name="minPixels"/> (the smaller side):
    /// smaller corners first, then no bevel, then no outline. Returns false if even that does not fit.
    /// </summary>
    public static bool TryFit(ref FrameSpec s, int minPixels)
    {
        while (2 * Margin(s) > minPixels && s.Radius > 0) s = s with { Radius = s.Radius - 1 };
        if (2 * Margin(s) > minPixels && s.Style != FrameStyle.Flat) s = s with { Style = FrameStyle.Flat };
        while (2 * Margin(s) > minPixels && s.StrokeWidth > 0) s = s with { StrokeWidth = s.StrokeWidth - 1 };
        return 2 * Margin(s) <= minPixels;
    }

    private static FrameSpec Normalize(FrameSpec s) =>
        s with { Radius = Math.Clamp(s.Radius, 0, 12), StrokeWidth = Math.Clamp(s.StrokeWidth, 0, 3) };

    private static NinePatchImage Create(FrameSpec s)
    {
        int r = s.Radius, b = s.StrokeWidth;
        int m = Margin(s);
        int w = 2 * m + StretchSize, h = 2 * m + StretchSize;
        var px = new byte[w * h * 4];

        for (int y = 0; y < h; y++)
        {
            // Gradient runs over the stretchable rows only, so it spans the whole height once stretched.
            double t = y < m ? 0 : y >= m + StretchSize ? 1 : (y - m + 0.5) / StretchSize;
            Color fill = Lerp(s.Top, s.Bottom, t);

            for (int x = 0; x < w; x++)
            {
                double d = RoundRectDistance(x + 0.5, y + 0.5, w, h, r);
                double coverage = Math.Clamp(0.5 - d, 0, 1);
                if (coverage <= 0) continue;

                Color c = fill;
                int inner = y - b;              // row index inside the outline (0 = first inner row)
                int innerBottom = h - 1 - b - y;
                switch (s.Style)
                {
                    case FrameStyle.Raised:
                        if (inner == 0) c = Blend(c, Colors.White, 0.14);
                        else if (inner == 1) c = Blend(c, Colors.White, 0.05);
                        if (innerBottom == 0) c = Blend(c, Colors.Black, 0.22);
                        break;
                    case FrameStyle.Sunken:
                        if (inner == 0) c = Blend(c, Colors.Black, 0.45);
                        else if (inner == 1) c = Blend(c, Colors.Black, 0.2);
                        if (innerBottom == 0) c = Blend(c, Colors.White, 0.06);
                        break;
                    case FrameStyle.Glow:
                        if (inner == 0) c = Blend(c, Colors.White, 0.45);
                        else if (inner == 1) c = Blend(c, Colors.White, 0.18);
                        if (innerBottom == 0) c = Blend(c, Colors.Black, 0.15);
                        break;
                }

                if (b > 0)
                {
                    // Outline band: distance from the edge between 0 and b; soften its inner edge too.
                    double strokeAmount = Math.Clamp(d + b + 0.5, 0, 1);
                    c = Over(c, s.Stroke, strokeAmount);
                }

                int i = (y * w + x) * 4;
                px[i] = c.R;
                px[i + 1] = c.G;
                px[i + 2] = c.B;
                px[i + 3] = (byte)Math.Round(c.A * coverage);
            }
        }

        return new NinePatchImage(px, w, h, new NinePatchInfo
        {
            StretchLeft = m,
            StretchRight = m + StretchSize,
            StretchTop = m,
            StretchBottom = m + StretchSize,
        });
    }

    /// <summary>Signed distance from a point to a rounded rectangle [0,w]×[0,h] (negative inside).</summary>
    private static double RoundRectDistance(double x, double y, double w, double h, double r)
    {
        double hx = w / 2, hy = h / 2;
        double qx = Math.Abs(x - hx) - (hx - r), qy = Math.Abs(y - hy) - (hy - r);
        double ox = Math.Max(qx, 0), oy = Math.Max(qy, 0);
        return Math.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(qx, qy), 0) - r;
    }

    private static Color Lerp(Color a, Color b, double t) => Color.FromArgb(
        (byte)Math.Round(a.A + (b.A - a.A) * t), (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));

    /// <summary>Mix the colour toward <paramref name="toward"/>, keeping alpha.</summary>
    private static Color Blend(Color c, Color toward, double amount) => Color.FromArgb(c.A,
        (byte)Math.Round(c.R + (toward.R - c.R) * amount), (byte)Math.Round(c.G + (toward.G - c.G) * amount),
        (byte)Math.Round(c.B + (toward.B - c.B) * amount));

    /// <summary>Paint <paramref name="top"/> over <paramref name="under"/> with extra opacity <paramref name="amount"/> (straight alpha).</summary>
    private static Color Over(Color under, Color top, double amount)
    {
        double ta = top.A / 255.0 * amount, ua = under.A / 255.0;
        double a = ta + ua * (1 - ta);
        if (a <= 0) return Colors.Transparent;
        byte Ch(byte u, byte t) => (byte)Math.Round((t * ta + u * ua * (1 - ta)) / a);
        return Color.FromArgb((byte)Math.Round(a * 255), Ch(under.R, top.R), Ch(under.G, top.G), Ch(under.B, top.B));
    }
}
