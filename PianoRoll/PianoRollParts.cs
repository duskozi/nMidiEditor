using NinePatchWpf;
using nMIDI.Piano;
using nMIDI.Theory;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace nMIDI.PianoRoll
{
    /// <summary>Colours, pens and helpers shared by the piano roll parts.</summary>
    internal static class PianoRollStyle
    {
        /// <summary>Takes the colours from the current theme (resource keys "Roll…"); missing keys keep their defaults.</summary>
        public static void Reload()
        {
            RowBlack = ThemeColors.Brush("RollRowBlack", D.RowBlack);
            RowWhite = ThemeColors.Brush("RollRowWhite", D.RowWhite);
            OctaveLine = ThemeColors.Brush("RollOctaveLine", D.OctaveLine);
            BarLine = ThemeColors.Brush("RollBarLine", D.BarLine);
            BeatLine = ThemeColors.Brush("RollBeatLine", D.BeatLine);
            RowOutOfScale = ThemeColors.Brush("RollRowOutOfScale", D.RowOutOfScale);
            RowRoot = ThemeColors.Brush("RollRowRoot", D.RowRoot);
            SnapLine = ThemeColors.Brush("RollSnapLine", D.SnapLine);
            RulerBackground = ThemeColors.Brush("RollRulerBackground", D.RulerBackground);
            RulerText = ThemeColors.Brush("RollRulerText", D.RulerText);
            RulerTick = ThemeColors.Brush("RollRulerTick", D.RulerTick);
            RulerEdge = ThemeColors.Brush("RollRulerEdge", D.RulerEdge);
            Playhead = ThemeColors.Brush("RollPlayhead", D.Playhead);
            RulerBand = ThemeColors.Brush("RollRulerBand", D.RulerBand);
            LoopOn = ThemeColors.Brush("RollLoopOn", D.LoopOn);
            LoopOff = ThemeColors.Brush("RollLoopOff", D.LoopOff);
            FlagTempo = ThemeColors.Brush("RollFlagTempo", D.FlagTempo);
            FlagSignature = ThemeColors.Brush("RollFlagSignature", D.FlagSignature);
            FlagKey = ThemeColors.Brush("RollFlagKey", D.FlagKey);
            FlagMarker = ThemeColors.Brush("RollFlagMarker", D.FlagMarker);
        }
        /// <summary>The built-in colours, kept for keys a theme does not define.</summary>
        private static class D
        {
            public static readonly Brush RowBlack = PianoRollStyle.RowBlack;
            public static readonly Brush RowWhite = PianoRollStyle.RowWhite;
            public static readonly Brush OctaveLine = PianoRollStyle.OctaveLine;
            public static readonly Brush BarLine = PianoRollStyle.BarLine;
            public static readonly Brush BeatLine = PianoRollStyle.BeatLine;
            public static readonly Brush RowOutOfScale = PianoRollStyle.RowOutOfScale;
            public static readonly Brush RowRoot = PianoRollStyle.RowRoot;
            public static readonly Brush SnapLine = PianoRollStyle.SnapLine;
            public static readonly Brush RulerBackground = PianoRollStyle.RulerBackground;
            public static readonly Brush RulerText = PianoRollStyle.RulerText;
            public static readonly Brush RulerTick = PianoRollStyle.RulerTick;
            public static readonly Brush RulerEdge = PianoRollStyle.RulerEdge;
            public static readonly Brush Playhead = PianoRollStyle.Playhead;
            public static readonly Brush RulerBand = PianoRollStyle.RulerBand;
            public static readonly Brush LoopOn = PianoRollStyle.LoopOn;
            public static readonly Brush LoopOff = PianoRollStyle.LoopOff;
            public static readonly Brush FlagTempo = PianoRollStyle.FlagTempo;
            public static readonly Brush FlagSignature = PianoRollStyle.FlagSignature;
            public static readonly Brush FlagKey = PianoRollStyle.FlagKey;
            public static readonly Brush FlagMarker = PianoRollStyle.FlagMarker;
        }

        public const int MaxNote = 127;

        public static bool IsBlack(int note) => NoteHelper.IsBlackKey(note);

        public static readonly Typeface Typeface = new("Segoe UI");

        public static readonly Brush WhiteKey = Gradient(Color.FromRgb(0xC9, 0xCC, 0xD1), Color.FromRgb(0xF2, 0xF3, 0xF5), horizontal: true);
        public static readonly Brush BlackKey = Gradient(Color.FromRgb(0x14, 0x14, 0x16), Color.FromRgb(0x3A, 0x3B, 0x3F), horizontal: true);
        public static readonly Brush KeyLabel = Frozen(new SolidColorBrush(Color.FromRgb(0x55, 0x58, 0x5E)));
        public static readonly Pen KeySeparator = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x9A, 0x9D, 0xA3))), 1));

        public static Brush RowBlack { get; private set; } = Frozen(new SolidColorBrush(Color.FromRgb(0x19, 0x19, 0x1B)));
        public static Brush RowWhite { get; private set; } = Frozen(new SolidColorBrush(Color.FromRgb(0x21, 0x21, 0x24)));
        public static Brush OctaveLine { get; private set; } = Frozen(new SolidColorBrush(Color.FromRgb(0x3A, 0x3B, 0x40)));
        public static Brush BarLine { get; private set; } = Frozen(new SolidColorBrush(Color.FromRgb(0x48, 0x4A, 0x50)));
        public static Brush BeatLine { get; private set; } = Frozen(new SolidColorBrush(Color.FromArgb(0x50, 0x48, 0x4A, 0x50)));
        public static Brush RowOutOfScale { get; private set; } = Frozen(new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x14)));
        public static Brush RowRoot { get; private set; } = Frozen(new SolidColorBrush(Color.FromRgb(0x23, 0x2A, 0x36)));
        public static Brush SnapLine { get; private set; } = Frozen(new SolidColorBrush(Color.FromArgb(0x26, 0x60, 0x64, 0x6C)));

        public static Brush RulerBackground { get; private set; } =
            Gradient(Color.FromRgb(0x30, 0x31, 0x35), Color.FromRgb(0x26, 0x27, 0x2A), horizontal: false);
        public static Brush RulerText { get; private set; } = Frozen(new SolidColorBrush(Color.FromRgb(0xB8, 0xBC, 0xC4)));
        public static Brush RulerTick { get; private set; } = Frozen(new SolidColorBrush(Color.FromRgb(0x6A, 0x6E, 0x76)));
        public static Brush RulerEdge { get; private set; } = Frozen(new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)));
        public static Brush Playhead { get; private set; } = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0x4D, 0x4D)));
        public static Brush RulerBand { get; private set; } = Frozen(new SolidColorBrush(Color.FromRgb(0x22, 0x23, 0x27)));
        public static Brush LoopOn { get; private set; } = Frozen(new SolidColorBrush(Color.FromArgb(0xB0, 0x2D, 0x8C, 0xFF)));
        public static Brush LoopOff { get; private set; } = Frozen(new SolidColorBrush(Color.FromArgb(0x50, 0x8D, 0x91, 0x9A)));
        public static Brush FlagTempo { get; private set; } = Frozen(new SolidColorBrush(Color.FromRgb(0xF5, 0xB0, 0x41)));
        public static Brush FlagSignature { get; private set; } = Frozen(new SolidColorBrush(Color.FromRgb(0x7F, 0xD4, 0xFF)));
        public static Brush FlagKey { get; private set; } = Frozen(new SolidColorBrush(Color.FromRgb(0xB0, 0x9C, 0xFF)));
        public static Brush FlagMarker { get; private set; } = Frozen(new SolidColorBrush(Color.FromRgb(0x6E, 0xE0, 0x9A)));

        /// <summary>Round a DIP coordinate to the nearest device pixel.</summary>
        public static double Snap(double value, double pixelsPerDip) => Math.Round(value * pixelsPerDip) / pixelsPerDip;

        private static LinearGradientBrush Gradient(Color from, Color to, bool horizontal)
        {
            var brush = new LinearGradientBrush(from, to, new Point(0, 0), horizontal ? new Point(1, 0) : new Point(0, 1));
            brush.Freeze();
            return brush;
        }

        private static T Frozen<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }

        /// <summary>
        /// Background of the note area: darker rows for black keys, a line between octaves,
        /// and bar / beat lines thinned out so they stay at least a few pixels apart.
        /// </summary>
        public static void DrawGrid(DrawingContext dc, NinePatchZoomViewer viewer, Size size, long ticksPerBeat, int beatsPerBar, long snapTicks = 0,
                                    Func<int, bool>? inScale = null, int scaleRoot = 0,
                                    Func<long, long, IEnumerable<(long Tick, long Bar, long BeatTicks, int Beats)>>? bars = null)
        {
            double ppd = VisualTreeHelper.GetDpi(viewer).PixelsPerDip;
            double onePixel = 1 / ppd;

            int firstRow = Math.Max(0, (int)Math.Floor(viewer.OffsetY));
            int lastRow = Math.Min(MaxNote, (int)Math.Ceiling(viewer.ViewToWorldY(size.Height)));
            for (int row = firstRow; row <= lastRow; row++)
            {
                int note = MaxNote - row;
                double top = Snap(viewer.WorldToViewY(row), ppd);
                double bottom = Snap(viewer.WorldToViewY(row + 1), ppd);
                var fill = inScale == null ? (IsBlack(note) ? RowBlack : RowWhite)
                         : !inScale(note) ? RowOutOfScale
                         : ((note - scaleRoot) % 12 + 12) % 12 == 0 ? RowRoot : RowWhite;
                dc.DrawRectangle(fill, null, new Rect(0, top, size.Width, Math.Max(0, bottom - top)));
                if (note % 12 == 0)
                    dc.DrawRectangle(OctaveLine, null, new Rect(0, bottom - onePixel, size.Width, onePixel));
            }

            if (ticksPerBeat <= 0) return;
            long ticksPerBar = ticksPerBeat * Math.Max(1, beatsPerBar);
            double beatPixels = ticksPerBeat * viewer.ZoomX;
            double barPixels = ticksPerBar * viewer.ZoomX;

            // Faint lines on the snap grid when it is finer than a beat and the lines are far enough apart.
            if (snapTicks > 0 && snapTicks < ticksPerBeat && snapTicks * viewer.ZoomX >= 8)
            {
                long firstSnap = (long)Math.Floor(viewer.OffsetX / snapTicks) * snapTicks;
                double rightEdge = viewer.ViewToWorldX(size.Width);
                for (long tick = firstSnap; tick <= rightEdge; tick += snapTicks)
                {
                    if (tick % ticksPerBeat == 0) continue;
                    double x = Snap(viewer.WorldToViewX(tick), ppd);
                    dc.DrawRectangle(SnapLine, null, new Rect(x, 0, onePixel, size.Height));
                }
            }

            if (bars != null)
            {
                // Time signature changes: the song's own bar and beat lines.
                double rightTick = viewer.ViewToWorldX(size.Width);
                foreach (var (tick, _, beatTicks, beats) in bars((long)viewer.OffsetX - ticksPerBar * 4, (long)rightTick + 1))
                {
                    double x = Snap(viewer.WorldToViewX(tick), ppd);
                    dc.DrawRectangle(BarLine, null, new Rect(x, 0, onePixel, size.Height));
                    if (beatTicks * viewer.ZoomX < 6) continue;
                    for (int b = 1; b < beats; b++)
                        dc.DrawRectangle(BeatLine, null, new Rect(Snap(viewer.WorldToViewX(tick + b * beatTicks), ppd), 0, onePixel, size.Height));
                }
                return;
            }

            bool drawBeats = beatPixels >= 6;
            long step = drawBeats ? ticksPerBeat : ticksPerBar * BarStep(barPixels, 6);
            long first = (long)Math.Floor(viewer.OffsetX / step) * step;
            double right = viewer.ViewToWorldX(size.Width);
            for (long tick = first; tick <= right; tick += step)
            {
                double x = Snap(viewer.WorldToViewX(tick), ppd);
                bool bar = tick % ticksPerBar == 0;
                dc.DrawRectangle(bar ? BarLine : BeatLine, null, new Rect(x, 0, onePixel, size.Height));
            }
        }

        /// <summary>Smallest power-of-two number of bars that is at least <paramref name="minPixels"/> wide.</summary>
        public static long BarStep(double barPixels, double minPixels)
        {
            long bars = 1;
            while (barPixels * bars < minPixels && bars < 1 << 20) bars *= 2;
            return bars;
        }
    }

    /// <summary>Piano keyboard beside the note area, one row per note, following the viewer's vertical zoom.</summary>
    public sealed class PianoKeys : FrameworkElement
    {
        public NinePatchZoomViewer? Viewer { get; set; }

        /// <summary>Colour of each sounding key (index = note), as in the piano roll; null = up.</summary>
        public Color?[] KeyColors { get; } = new Color?[128];

        private readonly Color?[] _drawn = new Color?[128];

        /// <summary>A key was clicked (note, Shift held).</summary>
        public event Action<int, bool>? KeyClicked;

        public PianoKeys()
        {
            Cursor = Cursors.Hand;
            ToolTip = "Click a key to hear it and select all its notes in the edited track (Shift adds)";
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (Viewer == null) return;
            int note = PianoRollStyle.MaxNote - (int)Math.Floor(Viewer.ViewToWorldY(e.GetPosition(this).Y));
            if (note is >= 0 and <= PianoRollStyle.MaxNote)
                KeyClicked?.Invoke(note, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            e.Handled = true;
        }

        /// <summary>Redraw only if a key changed since the last draw.</summary>
        public void RefreshKeys()
        {
            for (int note = 0; note < 128; note++)
            {
                if (KeyColors[note] != _drawn[note])
                {
                    InvalidateVisual();
                    return;
                }
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            var size = RenderSize;
            dc.DrawRectangle(PianoRollStyle.BlackKey, null, new Rect(size));
            if (Viewer == null) return;

            var viewer = Viewer;
            Array.Copy(KeyColors, _drawn, 128);
            double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            double rowHeight = viewer.ZoomY;
            int firstRow = Math.Max(0, (int)Math.Floor(viewer.OffsetY));
            int lastRow = Math.Min(PianoRollStyle.MaxNote, (int)Math.Ceiling(viewer.ViewToWorldY(size.Height)));

            dc.PushClip(new RectangleGeometry(new Rect(size)));

            // White keys first, full width, then the black keys on top, shorter.
            for (int row = firstRow; row <= lastRow; row++)
            {
                int note = PianoRollStyle.MaxNote - row;
                if (PianoRollStyle.IsBlack(note)) continue;
                double top = PianoRollStyle.Snap(viewer.WorldToViewY(row), ppd);
                double bottom = PianoRollStyle.Snap(viewer.WorldToViewY(row + 1), ppd);

                // A white key next to a black one reaches halfway under it, like a real keyboard.
                if (note < PianoRollStyle.MaxNote && PianoRollStyle.IsBlack(note + 1)) top -= rowHeight / 2;
                if (note > 0 && PianoRollStyle.IsBlack(note - 1)) bottom += rowHeight / 2;

                var key = new Rect(0, top, size.Width - 1, Math.Max(0, bottom - top));
                dc.DrawRectangle(_drawn[note] is { } down ? Kit.Solid(down) : PianoRollStyle.WhiteKey, null, key);
                if (rowHeight >= 3)
                    dc.DrawLine(PianoRollStyle.KeySeparator, new Point(0, bottom - 0.5), new Point(size.Width - 1, bottom - 0.5));

                if (note % 12 == 0 && rowHeight >= 7)
                {
                    var text = new FormattedText($"C{NoteHelper.Octave(note)}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                                 PianoRollStyle.Typeface, Math.Min(10, rowHeight * 0.85), PianoRollStyle.KeyLabel, ppd);
                    double rowTop = viewer.WorldToViewY(row);
                    dc.DrawText(text, new Point(size.Width - text.Width - 4, rowTop + (rowHeight - text.Height) / 2));
                }
            }

            double blackWidth = Math.Round(size.Width * 0.6);
            for (int row = firstRow; row <= lastRow; row++)
            {
                int note = PianoRollStyle.MaxNote - row;
                if (!PianoRollStyle.IsBlack(note)) continue;
                double top = PianoRollStyle.Snap(viewer.WorldToViewY(row), ppd);
                double bottom = PianoRollStyle.Snap(viewer.WorldToViewY(row + 1), ppd);
                var fill = _drawn[note] is { } down ? Kit.Solid(Palette.Darken(down, 0.25)) : PianoRollStyle.BlackKey;
                dc.DrawRoundedRectangle(fill, null, new Rect(0, top, blackWidth, Math.Max(1, bottom - top)), 1.5, 1.5);
            }

            dc.Pop();
            dc.DrawRectangle(PianoRollStyle.RulerEdge, null, new Rect(size.Width - 1, 0, 1, size.Height));
        }
    }

    public enum FlagKind { Tempo, TimeSignature, KeySignature, Marker }

    /// <summary>A conductor event shown on the ruler: tempo, time or key signature, or a marker.</summary>
    public sealed record RulerFlag(long Tick, FlagKind Kind, string Label, object Source);

    /// <summary>
    /// Bar ruler above the note area. The lower part moves the playhead (click or drag). The upper
    /// band shows the loop range, tempo and signature changes and markers: drag there to set the
    /// loop, click a flag to edit it, right-click for the ruler menu.
    /// </summary>
    public sealed class TimelineRuler : FrameworkElement
    {
        private const double BandHeight = 15;

        public NinePatchZoomViewer? Viewer { get; set; }
        public long TicksPerBeat { get; set; } = 480;
        public int BeatsPerBar { get; set; } = 4;
        public long PlayheadTick { get; set; }

        /// <summary>Conductor events to draw in the upper band.</summary>
        public IReadOnlyList<RulerFlag> Flags { get; set; } = Array.Empty<RulerFlag>();

        /// <summary>Bar lines of the song (ticks), when the time signature changes; null = a regular grid.</summary>
        public Func<long, long, IEnumerable<(long Tick, long Bar)>>? Bars { get; set; }

        public long LoopStart { get; set; }
        public long LoopEnd { get; set; }
        public bool LoopEnabled { get; set; }

        /// <summary>Grid for the loop range, in ticks.</summary>
        public Func<long>? Snap { get; set; }

        /// <summary>The user clicked or dragged to this tick.</summary>
        public event EventHandler<long>? Seek;

        /// <summary>The user dragged a new loop range (start, end).</summary>
        public event EventHandler<(long Start, long End)>? LoopDragged;

        /// <summary>A flag was clicked.</summary>
        public event EventHandler<RulerFlag>? FlagClicked;

        /// <summary>Right click: the tick under the mouse and the flag there, if any.</summary>
        public event EventHandler<(long Tick, RulerFlag? Flag)>? MenuRequested;

        private bool _seeking, _looping;
        private long _loopAnchor;

        public TimelineRuler()
        {
            Cursor = Cursors.Hand;
            ToolTip = DefaultTip;
        }

        protected override void OnRender(DrawingContext dc)
        {
            var size = RenderSize;
            double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            dc.DrawRectangle(PianoRollStyle.RulerBackground, null, new Rect(size));
            dc.DrawRectangle(PianoRollStyle.RulerBand, null, new Rect(0, 0, size.Width, BandHeight));
            dc.DrawRectangle(PianoRollStyle.RulerEdge, null, new Rect(0, BandHeight, size.Width, 1));
            dc.DrawRectangle(PianoRollStyle.RulerEdge, null, new Rect(0, size.Height - 1, size.Width, 1));
            if (Viewer == null || TicksPerBeat <= 0) return;

            var viewer = Viewer;
            dc.PushClip(new RectangleGeometry(new Rect(size)));

            // Loop range.
            if (LoopEnd > LoopStart)
            {
                double x1 = viewer.WorldToViewX(LoopStart), x2 = viewer.WorldToViewX(LoopEnd);
                dc.DrawRectangle(LoopEnabled ? PianoRollStyle.LoopOn : PianoRollStyle.LoopOff, null, new Rect(x1, 1, Math.Max(1, x2 - x1), BandHeight - 2));
            }

            // Bar numbers and beat ticks in the lower part.
            double lowerTop = BandHeight + 1, lowerHeight = size.Height - lowerTop;
            long ticksPerBar = TicksPerBeat * Math.Max(1, BeatsPerBar);
            double barPixels = ticksPerBar * viewer.ZoomX;
            long labelStep = PianoRollStyle.BarStep(barPixels, 44);
            double right = viewer.ViewToWorldX(size.Width);

            if (Bars != null)
            {
                // Time signature changes: draw the song's own bar lines.
                double lastLabel = double.NegativeInfinity;
                foreach (var (tick, bar) in Bars((long)viewer.OffsetX, (long)right + 1))
                {
                    double x = PianoRollStyle.Snap(viewer.WorldToViewX(tick), ppd);
                    bool labelled = x - lastLabel >= 40;
                    double height = labelled ? lowerHeight * 0.55 : lowerHeight * 0.35;
                    dc.DrawRectangle(PianoRollStyle.RulerTick, null, new Rect(x, size.Height - 1 - height, 1, height));
                    if (labelled)
                    {
                        lastLabel = x;
                        DrawText(dc, (bar + 1).ToString(CultureInfo.InvariantCulture), new Point(x + 3, lowerTop + 1), PianoRollStyle.RulerText, ppd);
                    }
                }
            }
            else
            {
                long tickStep = PianoRollStyle.BarStep(barPixels, 8);
                bool beats = TicksPerBeat * viewer.ZoomX >= 8;
                long step = beats ? TicksPerBeat : ticksPerBar * tickStep;
                long first = (long)Math.Floor(viewer.OffsetX / step) * step;
                for (long tick = first; tick <= right; tick += step)
                {
                    double x = PianoRollStyle.Snap(viewer.WorldToViewX(tick), ppd);
                    long bar = tick / ticksPerBar;
                    bool isBar = tick % ticksPerBar == 0;
                    bool labelled = isBar && bar % labelStep == 0;
                    double height = labelled ? lowerHeight * 0.55 : isBar ? lowerHeight * 0.35 : lowerHeight * 0.2;
                    dc.DrawRectangle(PianoRollStyle.RulerTick, null, new Rect(x, size.Height - 1 - height, 1, height));
                    if (labelled) DrawText(dc, (bar + 1).ToString(CultureInfo.InvariantCulture), new Point(x + 3, lowerTop + 1), PianoRollStyle.RulerText, ppd);
                }
            }

            // Flags in the upper band. Flags whose labels would overlap are merged into one label ("♩85.7 +12")
            // with every flag still marked by a thin line; the tooltip lists them. Dense tempo changes also get
            // a tempo line across the band, so the shape of a rubato passage is visible at a glance.
            _clusters.Clear();
            FlagCluster? open = null;
            foreach (var flag in Flags)
            {
                double x = viewer.WorldToViewX(flag.Tick);
                if (x < -400 || x > size.Width + 4) continue;
                if (open != null && x < open.X + open.Width + 6)
                {
                    open.Flags.Add(flag);
                    open.Width = Math.Max(open.Width, LabelWidth(open.Label, ppd) + 4);
                    continue;
                }
                open = new FlagCluster(x);
                open.Flags.Add(flag);
                open.Width = LabelWidth(open.Label, ppd) + 4;
                _clusters.Add(open);
            }

            if (_clusters.Any(c => c.Flags.Count(f => f.Kind == FlagKind.Tempo) >= 2) && Tempos.Count > 2) DrawTempoLine(dc, viewer, size, ppd);

            foreach (var c in _clusters)
            {
                var first = c.Flags[0];
                var brush = FlagBrush(first.Kind);
                foreach (var f in c.Flags.Skip(1))
                    dc.DrawRectangle(FlagBrush(f.Kind), null, new Rect(PianoRollStyle.Snap(viewer.WorldToViewX(f.Tick), ppd), BandHeight - 5, 1, 4));
                dc.DrawRectangle(brush, null, new Rect(PianoRollStyle.Snap(c.X, ppd), 1, 2, BandHeight - 2));
                if (c.Flags.Count > 1)
                {
                    // Keep the label readable over the tick marks.
                    var labelRect = new Rect(c.X + 3, 1, LabelWidth(c.Label, ppd) + 2, BandHeight - 7);
                    dc.DrawRectangle(PianoRollStyle.RulerBand, null, labelRect);
                }
                DrawText(dc, c.Label, new Point(c.X + 4, 1), brush, ppd, 9.5);
            }

            // Playhead marker: a small downward triangle.
            double px = viewer.WorldToViewX(PlayheadTick);
            if (px >= -6 && px <= size.Width + 6)
            {
                var marker = new StreamGeometry();
                using (var g = marker.Open())
                {
                    g.BeginFigure(new Point(px - 5, size.Height - 9), true, true);
                    g.LineTo(new Point(px + 5, size.Height - 9), true, false);
                    g.LineTo(new Point(px, size.Height - 2), true, false);
                }
                marker.Freeze();
                dc.DrawGeometry(PianoRollStyle.Playhead, null, marker);
            }
            dc.Pop();
        }

        private static void DrawText(DrawingContext dc, string text, Point at, Brush brush, double ppd, double size = 10)
        {
            var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, PianoRollStyle.Typeface, size, brush, ppd);
            dc.DrawText(formatted, at);
        }

        private double TickAt(double x) => Viewer == null ? 0 : Math.Clamp(Viewer.ViewToWorldX(x), 0, Viewer.ContentWidth);

        /// <summary>The flag (the first of a merged label) whose label is under the point, if any.</summary>
        private RulerFlag? FlagAt(Point p) => ClusterAt(p)?.Flags[0];

        private FlagCluster? ClusterAt(Point p)
        {
            if (Viewer == null || p.Y > BandHeight) return null;
            FlagCluster? found = null;
            foreach (var c in _clusters)
                if (p.X >= c.X - 3 && p.X <= c.X + c.Width + 2) found = c;
            return found;
        }

        private sealed class FlagCluster
        {
            public FlagCluster(double x) { X = x; }
            public double X;
            public double Width;
            public readonly List<RulerFlag> Flags = new();
            public string Label => Flags.Count == 1 ? Flags[0].Label : $"{Flags[0].Label} +{Flags.Count - 1}";
        }

        private readonly List<FlagCluster> _clusters = new();

        /// <summary>Tempo changes (tick, BPM) for the tempo line.</summary>
        public IReadOnlyList<(long Tick, double Bpm)> Tempos { get; set; } = Array.Empty<(long, double)>();

        /// <summary>Formats a tick as bars (for tooltips).</summary>
        public Func<long, string>? FormatTick { get; set; }

        private static Brush FlagBrush(FlagKind kind) => kind switch
        {
            FlagKind.Tempo => PianoRollStyle.FlagTempo,
            FlagKind.TimeSignature => PianoRollStyle.FlagSignature,
            FlagKind.KeySignature => PianoRollStyle.FlagKey,
            _ => PianoRollStyle.FlagMarker,
        };

        private static double LabelWidth(string text, double ppd) =>
            new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, PianoRollStyle.Typeface, 9.5, Brushes.White, ppd)
                .WidthIncludingTrailingWhitespace;

        /// <summary>A step line of the tempo across the band, scaled between the song's slowest and fastest tempo.</summary>
        private void DrawTempoLine(DrawingContext dc, NinePatchZoomViewer viewer, Size size, double ppd)
        {
            double min = Tempos.Min(t => t.Bpm), max = Tempos.Max(t => t.Bpm);
            if (max - min < 0.5) return;
            double Y(double bpm) => BandHeight - 2 - (bpm - min) / (max - min) * (BandHeight - 4);
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                bool started = false;
                double lastY = 0;
                for (int i = 0; i < Tempos.Count; i++)
                {
                    double x = viewer.WorldToViewX(Tempos[i].Tick);
                    double nextX = i + 1 < Tempos.Count ? viewer.WorldToViewX(Tempos[i + 1].Tick) : size.Width;
                    if (nextX < 0) continue;
                    if (x > size.Width) break;
                    double y = Y(Tempos[i].Bpm);
                    if (!started) { g.BeginFigure(new Point(Math.Max(0, x), y), false, false); started = true; }
                    else { g.LineTo(new Point(x, lastY), true, false); g.LineTo(new Point(x, y), true, false); }
                    g.LineTo(new Point(Math.Min(size.Width, nextX), y), true, false);
                    lastY = y;
                }
            }
            geometry.Freeze();
            var pen = new Pen(PianoRollStyle.FlagTempo, 1) { DashStyle = DashStyles.Solid };
            dc.PushOpacity(0.45);
            dc.DrawGeometry(null, pen, geometry);
            dc.Pop();
        }

        private FlagCluster? _tipCluster;

        private void UpdateFlagTip(Point p)
        {
            var c = ClusterAt(p);
            if (ReferenceEquals(c, _tipCluster)) return;
            _tipCluster = c;
            if (c == null || c.Flags.Count < 2) { ToolTip = DefaultTip; return; }
            var lines = c.Flags.Take(14).Select(f => $"{FormatTick?.Invoke(f.Tick) ?? f.Tick.ToString(CultureInfo.InvariantCulture)}   {f.Label}");
            ToolTip = $"{c.Flags.Count} changes here (zoom in to see them one by one):\n" + string.Join("\n", lines) + (c.Flags.Count > 14 ? "\n…" : "");
        }

        private const string DefaultTip =
            "Click or drag to move the playhead. Drag in the upper band to set the loop range, click a flag to edit it, right-click for more.";

        private long SnapTick(double tick)
        {
            long grid = Snap?.Invoke() ?? TicksPerBeat;
            if (grid <= 0) grid = TicksPerBeat;
            return (long)Math.Round(tick / grid) * grid;
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            var p = e.GetPosition(this);
            e.Handled = true;
            if (p.Y <= BandHeight)
            {
                if (FlagAt(p) is { } flag)
                {
                    FlagClicked?.Invoke(this, flag);
                    return;
                }
                _looping = true;
                _loopAnchor = SnapTick(TickAt(p.X));
                CaptureMouse();
                return;
            }
            _seeking = true;
            CaptureMouse();
            Seek?.Invoke(this, (long)TickAt(p.X));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var p = e.GetPosition(this);
            if (!IsMouseCaptured)
            {
                Cursor = p.Y <= BandHeight ? (FlagAt(p) != null ? Cursors.Hand : Cursors.IBeam) : Cursors.Hand;
                UpdateFlagTip(p);
                return;
            }
            if (_seeking) Seek?.Invoke(this, (long)TickAt(p.X));
            else if (_looping)
            {
                long tick = SnapTick(TickAt(p.X));
                if (tick != _loopAnchor) LoopDragged?.Invoke(this, (Math.Min(tick, _loopAnchor), Math.Max(tick, _loopAnchor)));
            }
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            _seeking = _looping = false;
            if (IsMouseCaptured) ReleaseMouseCapture();
        }

        protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonUp(e);
            var p = e.GetPosition(this);
            MenuRequested?.Invoke(this, ((long)TickAt(p.X), FlagAt(p)));
            e.Handled = true;
        }
    }
}
