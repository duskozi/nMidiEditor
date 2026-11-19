using nMIDI.Model;
using nMIDI.Theory;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace nMIDI.Piano
{
    /// <summary>A note of the falling-notes view, in seconds of the song.</summary>
    public readonly record struct FallingNote(double Start, double End, int Note, Color Color, int Hand, int Finger, bool Dim);

    /// <summary>A pedal pressed between two times (seconds), with its value.</summary>
    public readonly record struct FallingPedal(Pedal Pedal, double Start, double End, int Value);

    /// <summary>
    /// Falling notes over the keyboard panel. With a song, notes fall towards the keyboard and reach it when they
    /// sound, so you see what comes next; bar lines and the pedals fall with them. Without a song it shows what
    /// you play (clicked keys and the MIDI input) the other way round: a bar grows up from the key while it is held
    /// and then rises away. Each key column lines up with the keyboard panel below. It redraws every frame while it
    /// is visible, so the motion is smooth; the mouse wheel changes how many seconds are shown.
    /// </summary>
    public sealed class FallingNotesView : FrameworkElement
    {
        private static readonly Typeface Face = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        private static readonly Pedal[] PedalColumns = { Pedal.Soft, Pedal.Sostenuto, Pedal.Sustain };

        private FallingNote[] _notes = Array.Empty<FallingNote>();
        private double[] _starts = Array.Empty<double>();
        private double _longest;
        private FallingPedal[] _pedals = Array.Empty<FallingPedal>();
        private (double Time, bool Bar)[] _lines = Array.Empty<(double, bool)>();

        // Live notes and pedals (no song): open ones have End = NaN.
        private readonly List<FallingNote> _live = new();
        private readonly List<FallingPedal> _livePedals = new();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private bool _rendering;

        public FallingNotesView()
        {
            ClipToBounds = true;
            IsVisibleChanged += (_, _) => UpdateRendering();
            Loaded += (_, _) => UpdateRendering();
            Unloaded += (_, _) => StopRendering();
        }

        /// <summary>The keyboard the key columns line up with.</summary>
        public PianoKeyboard? Keyboard { get; set; }

        /// <summary>The song's time at the playhead, in seconds, or null when there is no song (live mode).</summary>
        public Func<double>? SongClock { get; set; }

        /// <summary>Seconds shown from the keyboard to the top.</summary>
        public double Seconds { get; set; } = 3;

        /// <summary>Raised when the mouse wheel changes <see cref="Seconds"/>.</summary>
        public event Action<double>? SecondsChanged;

        /// <summary>The song's notes, bar lines and pedals, in seconds (call when the song or the shown tracks change).</summary>
        public void SetSong(IEnumerable<FallingNote> notes, IEnumerable<(double Time, bool Bar)> lines, IEnumerable<FallingPedal> pedals)
        {
            var list = new List<FallingNote>(notes);
            list.Sort((a, b) => a.Start.CompareTo(b.Start));
            _notes = list.ToArray();
            _starts = new double[_notes.Length];
            _longest = 0;
            for (int i = 0; i < _notes.Length; i++)
            {
                _starts[i] = _notes[i].Start;
                _longest = Math.Max(_longest, _notes[i].End - _notes[i].Start);
            }
            _lines = new List<(double, bool)>(lines).ToArray();
            _pedals = new List<FallingPedal>(pedals).ToArray();
            InvalidateVisual();
        }

        private double LiveNow => _clock.Elapsed.TotalSeconds;

        /// <summary>A key went down (clicked or on the MIDI input).</summary>
        public void LiveNoteOn(int note, int velocity, Color color)
        {
            LiveNoteOff(note);
            var c = Color.FromArgb((byte)Math.Clamp(140 + velocity, 160, 255), color.R, color.G, color.B);
            _live.Add(new FallingNote(LiveNow, double.NaN, note, c, 0, 0, false));
            Trim();
        }

        public void LiveNoteOff(int note)
        {
            for (int i = _live.Count - 1; i >= 0; i--)
                if (_live[i].Note == note && double.IsNaN(_live[i].End)) _live[i] = _live[i] with { End = LiveNow };
        }

        /// <summary>A pedal moved on the MIDI input.</summary>
        public void LivePedal(Pedal pedal, int value)
        {
            double now = LiveNow;
            for (int i = _livePedals.Count - 1; i >= 0; i--)
                if (_livePedals[i].Pedal == pedal && double.IsNaN(_livePedals[i].End)) _livePedals[i] = _livePedals[i] with { End = now };
            if (value > 0) _livePedals.Add(new FallingPedal(pedal, now, double.NaN, value));
            Trim();
        }

        /// <summary>Release everything still held (the input was closed or Panic).</summary>
        public void LiveAllOff()
        {
            for (int i = 0; i < _live.Count; i++) if (double.IsNaN(_live[i].End)) _live[i] = _live[i] with { End = LiveNow };
            for (int i = 0; i < _livePedals.Count; i++) if (double.IsNaN(_livePedals[i].End)) _livePedals[i] = _livePedals[i] with { End = LiveNow };
        }

        private void Trim()
        {
            // Drop what has risen out of sight.
            double gone = LiveNow - Math.Max(Seconds, 1) * 1.2;
            _live.RemoveAll(n => !double.IsNaN(n.End) && n.End < gone);
            _livePedals.RemoveAll(p => !double.IsNaN(p.End) && p.End < gone);
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            Seconds = Math.Clamp(Seconds * (e.Delta > 0 ? 1 / 1.15 : 1.15), 1, 12);
            SecondsChanged?.Invoke(Seconds);
            e.Handled = true;
        }

        private void UpdateRendering()
        {
            if (IsVisible && IsLoaded) StartRendering(); else StopRendering();
        }

        private void StartRendering()
        {
            if (_rendering) return;
            _rendering = true;
            CompositionTarget.Rendering += OnRendering;
        }

        private void StopRendering()
        {
            if (!_rendering) return;
            _rendering = false;
            CompositionTarget.Rendering -= OnRendering;
        }

        private void OnRendering(object? sender, EventArgs e) => InvalidateVisual();

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w < 10 || h < 10) return;
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x10, 0x12, 0x18), Color.FromRgb(0x06, 0x07, 0x0A), 90), null, new Rect(0, 0, w, h));

            var kb = Keyboard;
            if (kb == null || kb.ActualWidth < 10) return;
            var layout = kb.Layout;
            double x0;
            try { x0 = kb.TranslatePoint(new Point(0, 0), this).X; }
            catch (InvalidOperationException) { return; }   // not in the same visual tree yet
            double pps = h / Math.Max(0.5, Seconds);
            double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            // Octave guides: a line at every C, fainter at F.
            var cLine = new Pen(new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), 1);
            var fLine = new Pen(new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)), 1);
            for (int n = layout.Low; n <= layout.High; n++)
            {
                int pc = NoteHelper.PC(n);
                if (pc != 0 && pc != 5) continue;
                double x = Math.Round(x0 + layout.X(n)) + 0.5;
                dc.DrawLine(pc == 0 ? cLine : fLine, new Point(x, 0), new Point(x, h));
            }

            bool live = SongClock == null;
            if (live) Trim();
            double now = live ? LiveNow : SongClock!();
            double kbLeft = x0, kbRight = x0 + layout.Width;

            // Y of a time: song mode falls (future above the keyboard), live mode rises (past above it).
            double Y(double t) => live ? h - (now - t) * pps : h - (t - now) * pps;

            if (!live)
            {
                var bar = new Pen(new SolidColorBrush(Color.FromArgb(0x3C, 0xFF, 0xFF, 0xFF)), 1);
                var beat = new Pen(new SolidColorBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF)), 1);
                foreach (var (time, isBar) in _lines)
                {
                    if (time < now || time > now + Seconds) continue;
                    double y = Math.Round(Y(time)) + 0.5;
                    dc.DrawLine(isBar ? bar : beat, new Point(kbLeft, y), new Point(kbRight, y));
                }
            }

            // Pedals: a faint band across the keys while sustain is down, and a column each at the left.
            var pedals = live ? (IReadOnlyList<FallingPedal>)_livePedals : _pedals;
            double columnWidth = Math.Clamp((kbLeft - 6) / 3, 5, 14);
            double columnsRight = kbLeft > 3 * 5 + 6 ? kbLeft - 3 : columnWidth * 3 + 3;
            foreach (var p in pedals)
            {
                double end = double.IsNaN(p.End) ? now : p.End;
                double yA = Y(p.Start), yB = Y(end);
                double top = Math.Max(0, Math.Min(yA, yB)), bottom = Math.Min(h, Math.Max(yA, yB));
                if (bottom <= 0 || top >= h || bottom - top < 0.5) continue;
                var color = PianoRoll.PedalOverlay.ColorOf(p.Pedal);
                byte alpha = (byte)(p.Value >= 64 ? 0xE0 : 0x80);
                if (p.Pedal == Pedal.Sustain)
                    dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x10, color.R, color.G, color.B)), null, new Rect(kbLeft, top, layout.Width, bottom - top));
                int col = Array.IndexOf(PedalColumns, p.Pedal);
                double cx = columnsRight - (3 - col) * columnWidth;
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B)), null,
                                        new Rect(cx + 1, top, columnWidth - 2, bottom - top), 2, 2);
            }

            // Notes: white-key notes first, black-key notes on top.
            for (int pass = 0; pass < 2; pass++)
            {
                if (live)
                    foreach (var n in _live) DrawNote(dc, n, pass, layout, x0, Y, h, now, ppd, live: true);
                else
                {
                    double from = now - _longest - 0.01;
                    int i = Array.BinarySearch(_starts, from);
                    if (i < 0) i = ~i;
                    for (; i < _notes.Length && _notes[i].Start <= now + Seconds; i++)
                        DrawNote(dc, _notes[i], pass, layout, x0, Y, h, now, ppd, live: false);
                }
            }

            // The line where notes meet the keys.
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)), null, new Rect(kbLeft, h - 1, layout.Width, 1));
        }

        private static void DrawNote(DrawingContext dc, FallingNote n, int pass, KeyboardLayout layout, double x0,
                                     Func<double, double> y, double h, double now, double ppd, bool live)
        {
            bool black = NoteHelper.IsBlackKey(n.Note);
            if ((pass == 1) != black || n.Note < layout.Low || n.Note > layout.High) return;
            double end = double.IsNaN(n.End) ? now : n.End;
            double yStart = y(n.Start), yEnd = y(end);
            double top = Math.Min(yStart, yEnd), bottom = Math.Max(yStart, yEnd);
            if (bottom < 0 || top > h) return;
            bottom = Math.Min(bottom, h);
            double height = Math.Max(3, bottom - top);
            double kw = layout.W(n.Note);
            double x = x0 + layout.X(n.Note) + (black ? 0.5 : 1.5);
            double width = Math.Max(2, kw - (black ? 1 : 3));

            // Sounding now (song) or held (live): brighter.
            bool sounding = live ? double.IsNaN(n.End) : n.Start <= now && n.End > now;
            var c = n.Color;
            if (black) c = Color.FromRgb((byte)(c.R * 0.8), (byte)(c.G * 0.8), (byte)(c.B * 0.8));
            byte alpha = n.Dim ? (byte)0x70 : sounding ? (byte)0xFF : (byte)0xE0;
            var fill = new LinearGradientBrush(
                Color.FromArgb(alpha, Lift(c.R), Lift(c.G), Lift(c.B)),
                Color.FromArgb(alpha, c.R, c.G, c.B), 0);
            var edge = new Pen(new SolidColorBrush(sounding ? Colors.White : Color.FromArgb(0x90, 0, 0, 0)), sounding ? 1.2 : 1);
            double r = Math.Min(4, width / 3);
            dc.DrawRoundedRectangle(fill, edge, new Rect(x, bottom - height, width, height), r, r);

            if (n.Finger > 0 && height >= 15 && width >= 9)
            {
                var label = new FormattedText(n.Finger.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                                              FlowDirection.LeftToRight, Face, Math.Min(12, width * 0.75),
                                              new SolidColorBrush(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF)), ppd);
                dc.DrawText(label, new Point(x + (width - label.Width) / 2, bottom - label.Height - 2));
            }
        }

        private static byte Lift(byte v) => (byte)Math.Min(255, v + (255 - v) * 0.35);
    }
}
