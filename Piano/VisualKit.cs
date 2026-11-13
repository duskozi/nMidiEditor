using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using nMIDI.Theory;

namespace nMIDI.Piano
{
    /// <summary>Small colour helpers for the hand-drawn controls.</summary>
    public static class Palette
    {
        public static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

        public static Color WithAlpha(this Color c, double a) => Color.FromArgb((byte)Math.Clamp(a * 255, 0, 255), c.R, c.G, c.B);

        public static Color Lerp(Color a, Color b, double t)
        {
            t = Math.Clamp(t, 0, 1);
            return Color.FromArgb(
                (byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t),
                (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
        }

        public static Color Lighten(Color c, double t) => Lerp(c, Colors.White, t);
        public static Color Darken(Color c, double t) => Lerp(c, Colors.Black, t);
    }

    /// <summary>
    /// Caches frozen brushes, pens and text so OnRender does not allocate on every frame.
    /// Only used from the UI thread, so the dictionaries need no locking.
    /// </summary>
    public static class Kit
    {
        private static readonly Dictionary<Color, SolidColorBrush> Solids = new();
        private static readonly Dictionary<(Color, double), Pen> Pens = new();
        private static readonly Dictionary<(Color, Color, bool), LinearGradientBrush> Linears = new();
        private static readonly Dictionary<(Color, Color), RadialGradientBrush> Radials = new();
        private static readonly Dictionary<(string, double, Color, bool, double), FormattedText> Texts = new();
        private static readonly FontFamily UiFont = new("Segoe UI Variable Display, Segoe UI");
        private static readonly Typeface UiFace = new(UiFont, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        private static readonly Typeface UiBold = new(UiFont, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

        public static SolidColorBrush Solid(Color c)
        {
            if (Solids.TryGetValue(c, out var b)) return b;
            b = new SolidColorBrush(c);
            b.Freeze();
            if (Solids.Count < 4096) Solids[c] = b;
            return b;
        }

        public static Pen Pen(Color c, double thickness)
        {
            if (Pens.TryGetValue((c, thickness), out var p)) return p;
            p = new Pen(Solid(c), thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            p.Freeze();
            if (Pens.Count < 4096) Pens[(c, thickness)] = p;
            return p;
        }

        /// <summary>Vertical (top→bottom) or horizontal (left→right) two-stop gradient.</summary>
        public static LinearGradientBrush Linear(Color a, Color b, bool vertical = true)
        {
            if (Linears.TryGetValue((a, b, vertical), out var br)) return br;
            br = vertical
                ? new LinearGradientBrush(a, b, new Point(0, 0), new Point(0, 1))
                : new LinearGradientBrush(a, b, new Point(0, 0), new Point(1, 0));
            br.Freeze();
            if (Linears.Count < 4096) Linears[(a, b, vertical)] = br;
            return br;
        }

        public static LinearGradientBrush Linear(bool vertical, params (Color c, double o)[] stops)
        {
            var g = new GradientStopCollection();
            foreach (var (c, o) in stops) g.Add(new GradientStop(c, o));
            var br = vertical
                ? new LinearGradientBrush(g, new Point(0, 0), new Point(0, 1))
                : new LinearGradientBrush(g, new Point(0, 0), new Point(1, 0));
            br.Freeze();
            return br;
        }

        /// <summary>Glossy sphere-like radial gradient (highlight at top-left).</summary>
        public static RadialGradientBrush Sphere(Color highlight, Color body)
        {
            if (Radials.TryGetValue((highlight, body), out var br)) return br;
            br = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.35, 0.3),
                Center = new Point(0.45, 0.45),
                RadiusX = 0.65,
                RadiusY = 0.65,
                GradientStops =
                {
                    new GradientStop(highlight, 0),
                    new GradientStop(body, 0.55),
                    new GradientStop(Palette.Darken(body, 0.35), 1),
                }
            };
            br.Freeze();
            if (Radials.Count < 2048) Radials[(highlight, body)] = br;
            return br;
        }

        public static RadialGradientBrush Glow(Color c)
        {
            if (Radials.TryGetValue((c, Colors.Transparent), out var br)) return br;
            br = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(c, 0),
                    new GradientStop(c.WithAlpha(c.A / 255.0 * 0.35), 0.45),
                    new GradientStop(c.WithAlpha(0), 1),
                }
            };
            br.Freeze();
            if (Radials.Count < 2048) Radials[(c, Colors.Transparent)] = br;
            return br;
        }

        public static FormattedText Text(string s, double size, Color color, Visual v, bool bold = false)
        {
            double dpi = 1.0;
            try { dpi = VisualTreeHelper.GetDpi(v).PixelsPerDip; }
            catch (InvalidOperationException) { }   // not in a visual tree yet
            var key = (s, size, color, bold, dpi);
            if (Texts.TryGetValue(key, out var ft)) return ft;
            ft = new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                                   bold ? UiBold : UiFace, size, Solid(color), dpi);
            if (Texts.Count > 8192) Texts.Clear();
            Texts[key] = ft;
            return ft;
        }

        public static void DrawCentered(DrawingContext dc, FormattedText t, Point center)
            => dc.DrawText(t, new Point(center.X - t.Width / 2, center.Y - t.Height / 2));

    }

    /// <summary>Where each key of the on-screen piano sits, for drawing and for hit testing.</summary>
    public sealed class KeyboardLayout
    {
        public int Low { get; }
        public int High { get; }
        public double Width { get; }
        public double WhiteWidth { get; }
        public double BlackWidth => WhiteWidth * 0.6;

        private readonly double[] _x;
        private readonly double[] _w;

        public KeyboardLayout(int low, int high, double width)
        {
            // Snap ends to white keys.
            while (NoteHelper.IsBlackKey(low)) low--;
            while (NoteHelper.IsBlackKey(high)) high++;
            Low = low;
            High = high;
            Width = width;
            int whites = 0;
            for (int n = low; n <= high; n++) if (!NoteHelper.IsBlackKey(n)) whites++;
            WhiteWidth = width / Math.Max(1, whites);
            _x = new double[128];
            _w = new double[128];

            int wi = 0;
            for (int n = low; n <= high; n++)
            {
                if (!NoteHelper.IsBlackKey(n))
                {
                    _x[n] = wi * WhiteWidth;
                    _w[n] = WhiteWidth;
                    wi++;
                }
                else
                {
                    // Classic black-key offsets: C#/F# lean left, D#/A# lean right.
                    double offset = NoteHelper.PC(n) switch { 1 => -0.12, 3 => 0.12, 6 => -0.15, 8 => 0, 10 => 0.15, _ => 0 };
                    double boundary = wi * WhiteWidth;
                    _x[n] = boundary - BlackWidth / 2 + offset * WhiteWidth;
                    _w[n] = BlackWidth;
                }
            }
        }

        public double X(int note) => _x[Math.Clamp(note, 0, 127)];
        public double W(int note) => _w[Math.Clamp(note, 0, 127)];
    }
}
