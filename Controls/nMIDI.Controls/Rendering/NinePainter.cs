using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NinePatchWpf;

namespace nMIDI.Controls.Primitives;

/// <summary>
/// Draws a nine-patch into a rectangle of a <see cref="DrawingContext"/>.
/// <para>Small rectangles (buttons, LEDs, caps, cells) are rendered by NinePatchLib at the exact device-pixel
/// size and shared through a cache, so a hundred equal buttons cost one bitmap. Large rectangles (panels,
/// whole lanes) are drawn as nine slices of one natural-size render, stretched by the GPU, so resizing a
/// window allocates nothing.</para>
/// </summary>
internal static class NinePainter
{
    /// <summary>Rendered bitmaps up to this size go through the shared cache.</summary>
    public const long MaxCachedBytes = 256 * 1024;

    private static readonly NinePatchSkinCache s_cache = new(maxRenderedEntries: 2048, maxRenderedBytes: 48L * 1024 * 1024)
    {
        MaxCacheableBitmapBytes = MaxCachedBytes,
    };

    private static readonly ConditionalWeakTable<NinePatchImage, Slices> s_slices = new();

    /// <summary>Draw <paramref name="image"/> stretched over <paramref name="rect"/> (DIPs), snapped to device pixels.</summary>
    public static void Draw(DrawingContext dc, Visual owner, NinePatchImage image, Rect rect)
    {
        if (image.IsDisposed || rect.Width <= 0 || rect.Height <= 0 || double.IsInfinity(rect.Width) || double.IsInfinity(rect.Height))
            return;

        DpiScale dpi = Dpi(owner);
        // Snap to device pixels so the 1-px outline is never blurred across two pixels.
        double x0 = Math.Round(rect.X * dpi.DpiScaleX), y0 = Math.Round(rect.Y * dpi.DpiScaleY);
        double x1 = Math.Round(rect.Right * dpi.DpiScaleX), y1 = Math.Round(rect.Bottom * dpi.DpiScaleY);
        int pw = (int)(x1 - x0), ph = (int)(y1 - y0);
        if (pw <= 0 || ph <= 0) return;
        var snapped = new Rect(x0 / dpi.DpiScaleX, y0 / dpi.DpiScaleY, pw / dpi.DpiScaleX, ph / dpi.DpiScaleY);

        try
        {
            if ((long)pw * ph * 4 <= MaxCachedBytes)
            {
                var bmp = s_cache.GetRendered(image, pw, ph, dpi.PixelsPerInchX, dpi.PixelsPerInchY, NinePatchScaling.Bilinear);
                dc.DrawImage(bmp, snapped);
            }
            else
            {
                s_slices.GetValue(image, i => new Slices(i)).Draw(dc, snapped, dpi);
            }
        }
        catch (Exception ex) when (NineSkins.IsNativeFailure(ex))
        {
            // Never throw from OnRender: report once and draw nothing.
            if (ex is NinePatchException) NinePatchDiagnostics.Report(owner, "nMIDI.Controls: cannot render a nine-patch.", ex);
            else NineSkins.MarkUnavailable(owner, ex);
        }
    }

    /// <summary>Draw the generated frame <paramref name="spec"/> over <paramref name="rect"/>.</summary>
    public static void Draw(DrawingContext dc, Visual owner, in FrameSpec spec, Rect rect)
    {
        var dpi = Dpi(owner);
        int minPixels = (int)Math.Floor(Math.Min(rect.Width * dpi.DpiScaleX, rect.Height * dpi.DpiScaleY));
        var fitted = spec;
        bool fits = NineSkins.TryFit(ref fitted, minPixels);
        if (fits && NineSkins.TryGet(fitted) is { } image)
        {
            Draw(dc, owner, image, rect);
        }
        else if (rect.Width > 0 && rect.Height > 0)
        {
            if (fits) DrawVector(dc, fitted, rect);   // native renderer unavailable
            else
            {
                // Too small for any frame (a few pixels): a plain rectangle in the fill colour.
                var fill = spec.Top.A == 0 && spec.StrokeWidth > 0 ? spec.Stroke : spec.Top;
                dc.DrawRectangle(Solid(fill), null, rect);
            }
        }
    }

    /// <summary>The frame drawn with plain WPF shapes, for when the native renderer is unavailable.</summary>
    internal static void DrawVector(DrawingContext dc, in FrameSpec s, Rect rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;
        Brush fill = s.Top == s.Bottom ? Solid(s.Top) : Kit.Linear(s.Top, s.Bottom);
        double half = s.StrokeWidth / 2.0;
        var r = new Rect(rect.X + half, rect.Y + half, Math.Max(0, rect.Width - s.StrokeWidth), Math.Max(0, rect.Height - s.StrokeWidth));
        dc.DrawRoundedRectangle(fill, s.StrokeWidth > 0 ? Kit.Pen(s.Stroke, s.StrokeWidth) : null, r, s.Radius, s.Radius);
    }

    private static readonly System.Collections.Generic.Dictionary<Color, SolidColorBrush> s_solids = new();

    private static SolidColorBrush Solid(Color c)
    {
        if (s_solids.TryGetValue(c, out var b)) return b;
        b = new SolidColorBrush(c);
        b.Freeze();
        if (s_solids.Count < 1024) s_solids[c] = b;
        return b;
    }

    /// <summary>Drop all rendered bitmaps (generated images are kept).</summary>
    public static void InvalidateRendered() => s_cache.InvalidateRendered();

    internal static DpiScale Dpi(Visual owner)
    {
        try { return VisualTreeHelper.GetDpi(owner); }
        catch (InvalidOperationException) { return new DpiScale(1, 1); }
    }

    /// <summary>Nine cropped pieces of the image rendered at its natural size, for large rectangles.</summary>
    private sealed class Slices
    {
        private readonly NinePatchImage _image;
        private double _dpiX, _dpiY;
        private BitmapSource?[]? _pieces;
        private int _l, _t, _r, _b;   // fixed margins in pixels

        public Slices(NinePatchImage image) => _image = image;

        public void Draw(DrawingContext dc, Rect rect, DpiScale dpi)
        {
            if (_pieces is null || _dpiX != dpi.PixelsPerInchX || _dpiY != dpi.PixelsPerInchY) Build(dpi);
            var p = _pieces!;
            double sx = dpi.DpiScaleX, sy = dpi.DpiScaleY;
            double l = _l / sx, t = _t / sy, r = _r / sx, b = _b / sy;
            double midW = Math.Max(0, rect.Width - l - r), midH = Math.Max(0, rect.Height - t - b);
            double[] xs = [rect.X, rect.X + l, rect.X + l + midW];
            double[] ws = [l, midW, r];
            double[] ys = [rect.Y, rect.Y + t, rect.Y + t + midH];
            double[] hs = [t, midH, b];
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 3; col++)
                {
                    var piece = p[row * 3 + col];
                    if (piece is null || ws[col] <= 0 || hs[row] <= 0) continue;
                    dc.DrawImage(piece, new Rect(xs[col], ys[row], ws[col], hs[row]));
                }
        }

        private void Build(DpiScale dpi)
        {
            _dpiX = dpi.PixelsPerInchX;
            _dpiY = dpi.PixelsPerInchY;
            int w = _image.Width, h = _image.Height;
            var full = NinePatchRenderer.RenderToBitmap(_image, w, h, _dpiX, _dpiY, NinePatchScaling.Bilinear);

            // Generated images have one stretch region in the middle; split there (a third each otherwise).
            _l = _t = Math.Max(1, Math.Min(w, h) / 3);
            _r = _b = _l;
            if (_image.HorizontalStretchRegions == 1 && _image.VerticalStretchRegions == 1 && _image.Width == _image.Height && w > 8)
            {
                // NineSkins images: size = 2 × margin + 8.
                _l = _t = _r = _b = (w - 8) / 2;
            }

            int[] cx = [0, _l, w - _r], cw = [_l, w - _l - _r, _r];
            int[] cy = [0, _t, h - _b], ch = [_t, h - _t - _b, _b];
            _pieces = new BitmapSource?[9];
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 3; col++)
                {
                    if (cw[col] <= 0 || ch[row] <= 0) continue;
                    var crop = new CroppedBitmap(full, new Int32Rect(cx[col], cy[row], cw[col], ch[row]));
                    crop.Freeze();
                    _pieces[row * 3 + col] = crop;
                }
        }
    }
}
