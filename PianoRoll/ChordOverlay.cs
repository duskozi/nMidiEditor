using NinePatchWpf;
using nMIDI.Model;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace nMIDI.PianoRoll
{
    /// <summary>
    /// A strip along the top of the piano roll with the chord names of the visible tracks, one box per chord.
    /// The chord at the playhead is highlighted.
    /// </summary>
    public sealed class ChordOverlay : FrameworkElement
    {
        public const double StripHeight = 18;

        private static readonly Typeface Face = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        public NinePatchZoomViewer? Viewer { get; set; }

        /// <summary>The chords to draw, or null for none.</summary>
        public ChordTimeline? Chords { get; set; }

        /// <summary>The playhead, for the highlight.</summary>
        public long PlayheadTick { get; set; } = -1;

        /// <summary>Colour of the highlighted chord.</summary>
        public Color Accent { get; set; } = Color.FromRgb(0x2D, 0x8C, 0xFF);

        public ChordOverlay() { IsHitTestVisible = false; }

        protected override void OnRender(DrawingContext dc)
        {
            if (Viewer is not { } v || Chords is not { IsEmpty: false } chords) return;
            var size = v.ViewportSize;
            if (size.Width <= 0 || size.Height < 60) return;

            var back = new SolidColorBrush(Color.FromArgb(0x99, 0x08, 0x09, 0x0C));
            dc.DrawRectangle(back, null, new Rect(0, 0, size.Width, StripHeight));
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)), null, new Rect(0, StripHeight - 1, size.Width, 1));

            long left = (long)Math.Floor(v.ViewToWorldX(0)), right = (long)Math.Ceiling(v.ViewToWorldX(size.Width));
            double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var box = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
            var current = new SolidColorBrush(Color.FromArgb(0xC0, Accent.R, Accent.G, Accent.B));
            var text = new SolidColorBrush(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF));
            double lastTextEnd = double.NegativeInfinity;

            foreach (var span in chords.Between(left, right))
            {
                double x1 = v.WorldToViewX(span.Start), x2 = v.WorldToViewX(span.End);
                double bx1 = Math.Max(-2, x1), bx2 = Math.Min(size.Width + 2, x2);
                if (bx2 - bx1 < 1) continue;
                bool playing = PlayheadTick >= span.Start && PlayheadTick < span.End;
                dc.DrawRoundedRectangle(playing ? current : box, null, new Rect(bx1 + 1, 2, Math.Max(1, bx2 - bx1 - 2), StripHeight - 5), 3, 3);

                var label = new FormattedText(span.Name, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 11, text, ppd);
                // Keep the name in view while its chord is, and skip it when there is no room.
                double tx = Math.Max(bx1 + 4, Math.Min(x1 + 4, bx2 - label.Width - 4));
                if (tx < lastTextEnd + 4 || tx + label.Width > bx2 - 1) continue;
                dc.DrawText(label, new Point(tx, (StripHeight - 1 - label.Height) / 2));
                lastTextEnd = tx + label.Width;
            }
        }
    }
}
