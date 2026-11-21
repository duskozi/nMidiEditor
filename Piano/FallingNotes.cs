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
    /// and then rises away. Each key column lines up with the keyboard panel below. The mouse wheel (or
    /// <see cref="Zoom"/>) changes how many seconds are shown.
    ///
    /// For smooth motion the song's notes are drawn once into strips of a few seconds each (frozen drawings),
    /// and every frame only moves those strips; nothing is allocated per frame. The time shown advances with the
    /// frame clock (and follows the player softly), so each frame moves by the same amount.
    /// </summary>
    public sealed class FallingNotesView : FrameworkElement
    {
        public const double MinSeconds = 0.5, MaxSeconds = 30;
        private const double ChunkSeconds = 4;

        private static readonly Typeface Face = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        private static readonly Pedal[] PedalColumns = { Pedal.Soft, Pedal.Sostenuto, Pedal.Sustain };

        private static readonly Brush Back = Freeze(new LinearGradientBrush(Color.FromRgb(0x10, 0x12, 0x18), Color.FromRgb(0x06, 0x07, 0x0A), 90));
        private static readonly Pen CLine = Freeze(new Pen(Solid(0x30, Colors.White), 1));
        private static readonly Pen FLine = Freeze(new Pen(Solid(0x14, Colors.White), 1));
        private static readonly Pen BarLine = Freeze(new Pen(Solid(0x3C, Colors.White), 1));
        private static readonly Pen BeatLine = Freeze(new Pen(Solid(0x12, Colors.White), 1));
        private static readonly Pen NoteEdge = Freeze(new Pen(Solid(0x90, Colors.Black), 1));
        private static readonly Pen SoundingEdge = Freeze(new Pen(Brushes.White, 1.2));
        private static readonly Brush HitLine = Solid(0x50, Colors.White);
        private static readonly Brush LabelBrush = Solid(0xF0, Colors.White);

        private FallingNote[] _notes = Array.Empty<FallingNote>();
        private double[] _starts = Array.Empty<double>();
        private double _longest;
        private FallingPedal[] _pedals = Array.Empty<FallingPedal>();
        private (double Time, bool Bar)[] _lines = Array.Empty<(double, bool)>();
        private double[] _lineTimes = Array.Empty<double>();

        // Strips of the song drawn in their own coordinates (y = -(time - strip start) × pixels per second).
        private readonly Dictionary<int, (Drawing White, Drawing Black)> _chunks = new();
        private (KeyboardLayout? Layout, double X0, double Pps, double Ppd) _chunkKey;

        // Live notes and pedals (no song): open ones have End = NaN.
        private readonly List<FallingNote> _live = new();
        private readonly List<FallingPedal> _livePedals = new();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private bool _rendering;

        // Frame clock: the shown song time and the frame it was shown at.
        private TimeSpan _lastFrame;
        private double _shown = double.NaN;
        private double _lastRaw = double.NaN;

        // Brushes by colour, alpha and kind, made once.
        private readonly Dictionary<(Color, byte, bool), Brush> _fills = new();
        private readonly Dictionary<(Color, byte), Brush> _solids = new();
        private readonly Dictionary<(int, double), FormattedText> _labels = new();

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

        /// <summary>How fast song time runs now: the playback speed while playing, 0 when stopped.</summary>
        public Func<double>? SongRate { get; set; }

        private double _seconds = 3;

        /// <summary>Seconds shown from the keyboard to the top.</summary>
        public double Seconds
        {
            get => _seconds;
            set
            {
                value = Math.Clamp(value, MinSeconds, MaxSeconds);
                if (value == _seconds) return;
                _seconds = value;
                InvalidateVisual();
            }
        }

        /// <summary>Raised when the mouse wheel or <see cref="Zoom"/> changes <see cref="Seconds"/>.</summary>
        public event Action<double>? SecondsChanged;

        /// <summary>Show more (steps &gt; 0: longer notes) or fewer seconds, by steps of 15 %.</summary>
        public void Zoom(int steps)
        {
            Seconds = _seconds * Math.Pow(1.15, -steps);
            SecondsChanged?.Invoke(_seconds);
        }

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
            var lineList = new List<(double Time, bool Bar)>(lines);
            lineList.Sort((a, b) => a.Time.CompareTo(b.Time));
            _lines = lineList.ToArray();
            _lineTimes = Array.ConvertAll(_lines, l => l.Time);
            _pedals = new List<FallingPedal>(pedals).ToArray();
            _chunks.Clear();
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
            double gone = LiveNow - Math.Max(_seconds, 1) * 1.2;
            _live.RemoveAll(n => !double.IsNaN(n.End) && n.End < gone);
            _livePedals.RemoveAll(p => !double.IsNaN(p.End) && p.End < gone);
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            Zoom(e.Delta > 0 ? 1 : -1);
            e.Handled = true;
        }

        // --- Frame clock ---

        private void UpdateRendering()
        {
            if (IsVisible && IsLoaded) StartRendering(); else StopRendering();
        }

        private void StartRendering()
        {
            if (_rendering) return;
            _rendering = true;
            _shown = double.NaN;
            CompositionTarget.Rendering += OnRendering;
        }

        private void StopRendering()
        {
            if (!_rendering) return;
            _rendering = false;
            CompositionTarget.Rendering -= OnRendering;
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            // Rendering can be raised more than once per frame: redraw once per frame time.
            var frame = e is RenderingEventArgs r ? r.RenderingTime : _clock.Elapsed;
            if (frame == _lastFrame) return;
            double dt = (frame - _lastFrame).TotalSeconds;
            _lastFrame = frame;
            AdvanceShownTime(dt);
            InvalidateVisual();
        }

        /// <summary>
        /// Move the shown time on by the frame's length at the playback speed, and pull it gently towards the
        /// player's clock. Reading the clock at a slightly different moment each frame would make the steps
        /// uneven (a visible judder); a jump (seek, loop, pause) is followed at once.
        /// </summary>
        private void AdvanceShownTime(double dt)
        {
            if (SongClock is not { } clock) { _shown = double.NaN; return; }
            double raw = clock();
            double rate = SongRate?.Invoke() ?? 0;
            if (double.IsNaN(_shown) || rate <= 0 || dt <= 0 || dt > 0.25 || raw == _lastRaw && rate <= 0)
                _shown = raw;
            else
            {
                double expected = _shown + dt * rate;
                double error = raw - expected;
                _shown = Math.Abs(error) > 0.06 ? raw : expected + error * 0.12;
            }
            _lastRaw = raw;
        }

        // --- Brushes ---

        private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

        private static SolidColorBrush Solid(byte alpha, Color c) => Freeze(new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B)));

        private Brush SolidOf(Color c, byte alpha)
        {
            if (!_solids.TryGetValue((c, alpha), out var b)) _solids[(c, alpha)] = b = Solid(alpha, c);
            return b;
        }

        private Brush FillOf(Color c, byte alpha, bool black)
        {
            if (_fills.TryGetValue((c, alpha, black), out var b)) return b;
            var k = black ? Color.FromRgb((byte)(c.R * 0.8), (byte)(c.G * 0.8), (byte)(c.B * 0.8)) : c;
            b = Freeze(new LinearGradientBrush(Color.FromArgb(alpha, Lift(k.R), Lift(k.G), Lift(k.B)), Color.FromArgb(alpha, k.R, k.G, k.B), 0));
            _fills[(c, alpha, black)] = b;
            return b;
        }

        private FormattedText Label(int finger, double size, double ppd)
        {
            size = Math.Round(size * 2) / 2;
            if (!_labels.TryGetValue((finger, size), out var t))
            {
                if (_labels.Count > 200) _labels.Clear();
                _labels[(finger, size)] = t = new FormattedText(finger.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                                                               FlowDirection.LeftToRight, Face, size, LabelBrush, ppd);
            }
            return t;
        }

        private static byte Lift(byte v) => (byte)Math.Min(255, v + (255 - v) * 0.35);

        // --- Drawing ---

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w < 10 || h < 10) return;
            dc.DrawRectangle(Back, null, new Rect(0, 0, w, h));

            var kb = Keyboard;
            if (kb == null || kb.ActualWidth < 10) return;
            var layout = kb.Layout;
            double x0;
            try { x0 = kb.TranslatePoint(new Point(0, 0), this).X; }
            catch (InvalidOperationException) { return; }   // not in the same visual tree yet
            double pps = h / _seconds;
            double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            // Octave guides: a line at every C, fainter at F.
            for (int n = layout.Low; n <= layout.High; n++)
            {
                int pc = NoteHelper.PC(n);
                if (pc != 0 && pc != 5) continue;
                double x = Math.Round(x0 + layout.X(n)) + 0.5;
                dc.DrawLine(pc == 0 ? CLine : FLine, new Point(x, 0), new Point(x, h));
            }

            bool live = SongClock == null;
            if (live) Trim();
            double now = live ? LiveNow : double.IsNaN(_shown) ? SongClock!() : _shown;
            double kbLeft = x0;

            if (live) DrawLive(dc, layout, x0, h, pps, now, ppd);
            else DrawSong(dc, layout, x0, h, pps, now, ppd);

            // The line where notes meet the keys.
            dc.DrawRectangle(HitLine, null, new Rect(kbLeft, h - 1, layout.Width, 1));
        }

        private void DrawSong(DrawingContext dc, KeyboardLayout layout, double x0, double h, double pps, double now, double ppd)
        {
            var key = (layout, x0, pps, ppd);
            if (key != _chunkKey)
            {
                _chunks.Clear();
                _chunkKey = key;
            }
            double Y(double t) => h - (t - now) * pps;

            // Bar and beat lines (only the visible ones; a handful per frame).
            int li = Array.BinarySearch(_lineTimes, now);
            if (li < 0) li = ~li;
            for (; li < _lines.Length && _lines[li].Time <= now + _seconds; li++)
            {
                double y = Y(_lines[li].Time);
                dc.DrawLine(_lines[li].Bar ? BarLine : BeatLine, new Point(x0, y), new Point(x0 + layout.Width, y));
            }

            DrawPedals(dc, _pedals, layout, x0, h, Y, now);

            if (_notes.Length == 0) return;
            // The strips that reach into view: from notes that started up to the longest note ago, to the top.
            int first = (int)Math.Floor((now - _longest - 0.01) / ChunkSeconds), last = (int)Math.Floor((now + _seconds) / ChunkSeconds);
            first = Math.Max(first, (int)Math.Floor(_starts[0] / ChunkSeconds));
            last = Math.Min(last, (int)Math.Floor(_starts[^1] / ChunkSeconds));
            for (int pass = 0; pass < 2; pass++)
            {
                for (int c = first; c <= last; c++)
                {
                    var chunk = Chunk(c, layout, x0, pps, ppd);
                    var drawing = pass == 0 ? chunk.White : chunk.Black;
                    if (drawing == null) continue;
                    dc.PushTransform(new TranslateTransform(0, Y(c * ChunkSeconds)));
                    dc.DrawDrawing(drawing);
                    dc.Pop();
                }
                // Notes sounding now get a bright outline, on top of the strips of their kind.
                DrawSounding(dc, pass == 1, layout, x0, h, Y, now, ppd);
            }
        }

        /// <summary>The white-key and black-key notes that start in strip c, drawn once.</summary>
        private (Drawing White, Drawing Black) Chunk(int c, KeyboardLayout layout, double x0, double pps, double ppd)
        {
            if (_chunks.TryGetValue(c, out var cached)) return cached;
            double t0 = c * ChunkSeconds, t1 = t0 + ChunkSeconds;
            var white = new DrawingGroup();
            var black = new DrawingGroup();
            using (var wdc = white.Open())
            using (var bdc = black.Open())
            {
                int i = Array.BinarySearch(_starts, t0);
                if (i < 0) i = ~i;
                while (i > 0 && _starts[i - 1] >= t0) i--;
                for (; i < _notes.Length && _notes[i].Start < t1; i++)
                {
                    var n = _notes[i];
                    if (n.Note < layout.Low || n.Note > layout.High) continue;
                    bool isBlack = NoteHelper.IsBlackKey(n.Note);
                    // Strip coordinates: the start is at y = -(start - t0) × pps, the note goes up from there.
                    double bottom = -(n.Start - t0) * pps;
                    double height = Math.Max(3, (n.End - n.Start) * pps);
                    var rect = NoteRect(n.Note, isBlack, layout, x0, bottom, height);
                    byte alpha = n.Dim ? (byte)0x70 : (byte)0xE0;
                    double r = Math.Min(4, rect.Width / 3);
                    var target = isBlack ? bdc : wdc;
                    target.DrawRoundedRectangle(FillOf(n.Color, alpha, isBlack), NoteEdge, rect, r, r);
                    if (n.Finger > 0 && rect.Height >= 15 && rect.Width >= 9)
                    {
                        var label = Label(n.Finger, Math.Min(12, rect.Width * 0.75), ppd);
                        target.DrawText(label, new Point(rect.X + (rect.Width - label.Width) / 2, rect.Bottom - label.Height - 2));
                    }
                }
            }
            white.Freeze();
            black.Freeze();
            var result = (White: (Drawing)white, Black: (Drawing)black);
            // Keep the cache small: strips far behind the playhead are dropped.
            if (_chunks.Count > 64) _chunks.Clear();
            _chunks[c] = result;
            return result;
        }

        private static Rect NoteRect(int note, bool black, KeyboardLayout layout, double x0, double bottom, double height)
        {
            double kw = layout.W(note);
            double x = x0 + layout.X(note) + (black ? 0.5 : 1.5);
            double width = Math.Max(2, kw - (black ? 1 : 3));
            return new Rect(x, bottom - height, width, height);
        }

        private void DrawSounding(DrawingContext dc, bool black, KeyboardLayout layout, double x0, double h, Func<double, double> y, double now, double ppd)
        {
            int i = Array.BinarySearch(_starts, now - _longest - 0.01);
            if (i < 0) i = ~i;
            for (; i < _notes.Length && _notes[i].Start <= now; i++)
            {
                var n = _notes[i];
                if (n.End <= now || n.Dim || NoteHelper.IsBlackKey(n.Note) != black || n.Note < layout.Low || n.Note > layout.High) continue;
                double top = y(n.End), bottom = Math.Min(h, y(n.Start));
                var rect = NoteRect(n.Note, black, layout, x0, bottom, Math.Max(3, bottom - top));
                double r = Math.Min(4, rect.Width / 3);
                dc.DrawRoundedRectangle(FillOf(n.Color, 0xFF, black), SoundingEdge, rect, r, r);
                // The finger stays readable at the keys while the note sounds.
                if (n.Finger > 0 && rect.Height >= 15 && rect.Width >= 9)
                {
                    var label = Label(n.Finger, Math.Min(12, rect.Width * 0.75), ppd);
                    dc.DrawText(label, new Point(rect.X + (rect.Width - label.Width) / 2, rect.Bottom - label.Height - 2));
                }
            }
        }

        private void DrawPedals(DrawingContext dc, IReadOnlyList<FallingPedal> pedals, KeyboardLayout layout, double x0, double h,
                                Func<double, double> y, double now)
        {
            // A faint band across the keys while sustain is down, and a column each at the left.
            double kbLeft = x0;
            double columnWidth = Math.Clamp((kbLeft - 6) / 3, 5, 14);
            double columnsRight = kbLeft > 3 * 5 + 6 ? kbLeft - 3 : columnWidth * 3 + 3;
            for (int i = 0; i < pedals.Count; i++)
            {
                var p = pedals[i];
                double end = double.IsNaN(p.End) ? now : p.End;
                double yA = y(p.Start), yB = y(end);
                double top = Math.Max(0, Math.Min(yA, yB)), bottom = Math.Min(h, Math.Max(yA, yB));
                if (bottom <= 0 || top >= h || bottom - top < 0.5) continue;
                var color = PianoRoll.PedalOverlay.ColorOf(p.Pedal);
                if (p.Pedal == Pedal.Sustain)
                    dc.DrawRectangle(SolidOf(color, 0x10), null, new Rect(kbLeft, top, layout.Width, bottom - top));
                int col = Array.IndexOf(PedalColumns, p.Pedal);
                double cx = columnsRight - (3 - col) * columnWidth;
                dc.DrawRoundedRectangle(SolidOf(color, p.Value >= 64 ? (byte)0xE0 : (byte)0x80), null,
                                        new Rect(cx + 1, top, columnWidth - 2, bottom - top), 2, 2);
            }
        }

        private void DrawLive(DrawingContext dc, KeyboardLayout layout, double x0, double h, double pps, double now, double ppd)
        {
            // Live mode rises: the past is above the keyboard.
            double Y(double t) => h - (now - t) * pps;
            DrawPedals(dc, _livePedals, layout, x0, h, Y, now);
            for (int pass = 0; pass < 2; pass++)
                foreach (var n in _live)
                {
                    bool black = NoteHelper.IsBlackKey(n.Note);
                    if ((pass == 1) != black || n.Note < layout.Low || n.Note > layout.High) continue;
                    bool held = double.IsNaN(n.End);
                    double top = Y(n.Start), bottom = Y(held ? now : n.End);
                    if (bottom < 0 || top > h) continue;
                    var rect = NoteRect(n.Note, black, layout, x0, bottom, Math.Max(3, bottom - top));
                    double r = Math.Min(4, rect.Width / 3);
                    dc.DrawRoundedRectangle(FillOf(n.Color, held ? (byte)0xFF : (byte)0xE0, black), held ? SoundingEdge : NoteEdge, rect, r, r);
                }
        }
    }
}
