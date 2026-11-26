using NinePatchWpf;
using nMIDI.Model;
using nMIDI.Theory;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace nMIDI.PianoRoll
{
    /// <summary>
    /// Drawn over the piano roll: the finger on each note (a small badge in the hand's colour), a ring on
    /// fingers the user fixed, and a red outline on the notes the fingering report marks as different.
    /// </summary>
    public sealed class FingerOverlay : FrameworkElement
    {
        public static readonly Color LeftColor = Color.FromRgb(0x3B, 0x82, 0xF6);
        public static readonly Color RightColor = Color.FromRgb(0xF9, 0x73, 0x16);

        private static readonly Brush LeftBrush = Freeze(new SolidColorBrush(LeftColor));
        private static readonly Brush RightBrush = Freeze(new SolidColorBrush(RightColor));
        private static readonly Brush NoHandBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)));
        private static readonly Pen LockPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xFA, 0xCC, 0x15)), 1.5));
        private static readonly Pen DiffPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)), 2));
        private static readonly Pen CurrentDiffPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)), 2) { DashStyle = DashStyles.Dash });
        private static readonly Typeface Face = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

        private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

        public NinePatchZoomViewer? Viewer { get; set; }

        /// <summary>Tracks whose fingers are drawn.</summary>
        public IReadOnlyList<SongTrack> Tracks { get; set; } = Array.Empty<SongTrack>();

        /// <summary>Notes outlined in red (by reference).</summary>
        public ISet<SongNote>? Differences { get; set; }

        /// <summary>The difference the report's Next/Previous is on.</summary>
        public SongNote? CurrentDifference { get; set; }


        public FingerOverlay() { IsHitTestVisible = false; }

        protected override void OnRender(DrawingContext dc)
        {
            if (Viewer is not { } v) return;
            var size = v.ViewportSize;
            if (size.Width <= 0 || size.Height <= 0) return;
            double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            double rowHeight = v.ZoomY;
            double leftTick = v.ViewToWorldX(0), rightTick = v.ViewToWorldX(size.Width);
            bool badges = rowHeight >= 8;
            double fontSize = Math.Clamp(rowHeight - 3, 7, 11);

            foreach (var track in Tracks)
            {
                var notes = track.Notes;
                var fingers = track.Fingering;
                if (fingers.Length != notes.Count) continue;
                for (int i = 0; i < notes.Count; i++)
                {
                    var n = notes[i];
                    if (n.Start > rightTick) break;
                    if (n.End < leftTick) continue;
                    int row = PianoRollStyle.Rows.RowOf(n.Number);
                    if (row < 0) continue;
                    double top = v.WorldToViewY(row), bottom = v.WorldToViewY(row + 1);
                    if (bottom < 0 || top > size.Height) continue;
                    double x1 = v.WorldToViewX(n.Start), x2 = v.WorldToViewX(n.End);
                    var rect = new Rect(x1, top, Math.Max(2, x2 - x1), Math.Max(2, bottom - top));

                    if (Differences != null && Differences.Contains(n))
                    {
                        var r = rect; r.Inflate(2, 2);
                        dc.DrawRectangle(null, ReferenceEquals(n, CurrentDifference) ? CurrentDiffPen : DiffPen, r);
                    }

                    var f = fingers[i];
                    if (!badges || f.Finger == 0) continue;
                    double w = Math.Min(Math.Max(rowHeight - 1, 9), 14);
                    if (rect.Width < 6) continue;
                    var badge = new Rect(x1 + 1, top + (rect.Height - w) / 2, w, w);
                    var fill = f.Hand == Hand.Left ? LeftBrush : f.Hand == Hand.Right ? RightBrush : NoHandBrush;
                    dc.DrawRoundedRectangle(fill, f.Locked ? LockPen : null, badge, 3, 3);
                    var text = new FormattedText(f.Finger.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, Face, fontSize, Brushes.White, ppd);
                    dc.DrawText(text, new Point(badge.X + (badge.Width - text.Width) / 2, badge.Y + (badge.Height - text.Height) / 2));
                }
            }
        }
    }
}
