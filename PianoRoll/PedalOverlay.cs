using NinePatchWpf;
using nMIDI.Model;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace nMIDI.PianoRoll
{
    /// <summary>
    /// A strip along the bottom of the piano roll with the piano pedals of the edited tracks: one row each for
    /// sustain, sostenuto and soft (only the pedals the song uses). A bar is drawn while the pedal is pressed; its
    /// height follows the value, so half-pedalling shows as a lower bar.
    /// </summary>
    public sealed class PedalOverlay : FrameworkElement
    {
        public const double RowHeight = 9;

        private static readonly Typeface Face = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        public static Color ColorOf(Pedal pedal) => pedal switch
        {
            Pedal.Sustain => Color.FromRgb(0xF5, 0x9E, 0x0B),
            Pedal.Sostenuto => Color.FromRgb(0x14, 0xB8, 0xA6),
            _ => Color.FromRgb(0xA7, 0x8B, 0xFA),
        };

        public NinePatchZoomViewer? Viewer { get; set; }

        /// <summary>The pedals to draw, or null for none.</summary>
        public PedalTimeline? Pedals { get; set; }

        /// <summary>End of the song, for a pedal still held after its last event.</summary>
        public long SongEnd { get; set; }

        public PedalOverlay() { IsHitTestVisible = false; }

        protected override void OnRender(DrawingContext dc)
        {
            if (Viewer is not { } v || Pedals is not { IsEmpty: false } pedals) return;
            var size = v.ViewportSize;
            if (size.Width <= 0 || size.Height < 60) return;

            int rows = 0;
            foreach (var p in PedalTimeline.All) if (pedals.Has(p)) rows++;
            double stripTop = size.Height - rows * RowHeight - 2;
            var back = new SolidColorBrush(Color.FromArgb(0x99, 0x08, 0x09, 0x0C));
            dc.DrawRectangle(back, null, new Rect(0, stripTop, size.Width, size.Height - stripTop));
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)), null, new Rect(0, stripTop, size.Width, 1));

            long left = (long)Math.Floor(v.ViewToWorldX(0)), right = (long)Math.Ceiling(v.ViewToWorldX(size.Width));
            double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            double y = stripTop + 1.5;
            foreach (var pedal in PedalTimeline.All)
            {
                if (!pedals.Has(pedal)) continue;
                var color = ColorOf(pedal);
                var down = new SolidColorBrush(color);
                var partial = new SolidColorBrush(Color.FromArgb(0x90, color.R, color.G, color.B));
                double h = RowHeight - 2;
                foreach (var span in pedals.Spans(pedal, left, right, SongEnd))
                {
                    double x1 = Math.Max(-2, v.WorldToViewX(span.Start)), x2 = Math.Min(size.Width + 2, v.WorldToViewX(span.End));
                    if (x2 <= x1) continue;
                    double bar = Math.Max(1.5, h * span.Value / 127.0);
                    dc.DrawRectangle(span.IsDown ? down : partial, null, new Rect(x1, y + h - bar, Math.Max(1, x2 - x1 - 1), bar));
                }
                var label = new FormattedText(PedalTimeline.ShortName(pedal), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                              Face, 7, new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF)), ppd);
                dc.DrawRectangle(back, null, new Rect(2, y, label.Width + 4, h));
                dc.DrawText(label, new Point(4, y + (h - label.Height) / 2));
                y += RowHeight;
            }
        }
    }
}
