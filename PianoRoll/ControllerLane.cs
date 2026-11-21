using NinePatchWpf;
using nMIDI.Model;
using nMIDI.Piano;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace nMIDI.PianoRoll
{
    public enum LaneKind { Velocity, Controller, PitchBend, ChannelPressure, Program, Tempo }

    /// <summary>What a lane shows: note velocities, one controller, pitch bend, channel pressure or program changes.</summary>
    public sealed record LaneMode(LaneKind Kind, int Controller = 0)
    {
        public int Max => Kind switch { LaneKind.PitchBend => 16383, LaneKind.Tempo => 300, _ => 127 };
        public int Centre => Kind == LaneKind.PitchBend ? 8192 : Kind == LaneKind.Controller && Controller == 10 ? 64 : -1;

        public bool Matches(SongCtrl c) => Kind switch
        {
            LaneKind.Controller => c.Kind == CtrlKind.Controller && c.Number == Controller,
            LaneKind.PitchBend => c.Kind == CtrlKind.PitchBend,
            LaneKind.ChannelPressure => c.Kind == CtrlKind.ChannelPressure,
            LaneKind.Program => c.Kind == CtrlKind.Program,
            _ => false,
        };

        public SongCtrl Make(long tick, int channel, int value) => Kind switch
        {
            LaneKind.Controller => new SongCtrl(tick, channel, CtrlKind.Controller, Controller, value),
            LaneKind.PitchBend => new SongCtrl(tick, channel, CtrlKind.PitchBend, 0, value),
            LaneKind.ChannelPressure => new SongCtrl(tick, channel, CtrlKind.ChannelPressure, 0, value),
            _ => new SongCtrl(tick, channel, CtrlKind.Program, 0, value),
        };

        public string Name => Kind switch
        {
            LaneKind.Velocity => "Velocity",
            LaneKind.PitchBend => "Pitch bend",
            LaneKind.ChannelPressure => "Channel pressure",
            LaneKind.Program => "Program change",
            LaneKind.Tempo => "Tempo",
            _ => ControllerNames.Label(Controller),
        };

        /// <summary>A value as it is shown: pitch bend relative to the centre, programs with their instrument.</summary>
        public string Format(int value) => Kind switch
        {
            LaneKind.PitchBend => (value - 8192).ToString("+0;−0;0", CultureInfo.InvariantCulture),
            LaneKind.Program => $"{value + 1} {GeneralMidi.Instruments[Math.Clamp(value, 0, 127)]}",
            LaneKind.Tempo => $"{value} BPM",
            LaneKind.Controller when Controller == 10 => value == 64 ? "C" : value < 64 ? $"L{64 - value}" : $"R{value - 64}",
            LaneKind.Controller when Controller is >= 64 and <= 69 => value >= 64 ? $"on ({value})" : $"off ({value})",
            _ => value.ToString(CultureInfo.InvariantCulture),
        };
    }

    public static class ControllerNames
    {
        private static readonly Dictionary<int, string> Names = new()
        {
            [0] = "Bank select", [1] = "Modulation", [2] = "Breath", [4] = "Foot pedal", [5] = "Portamento time",
            [6] = "Data entry", [7] = "Volume", [8] = "Balance", [10] = "Pan", [11] = "Expression",
            [12] = "Effect 1", [13] = "Effect 2", [32] = "Bank select LSB", [64] = "Sustain pedal", [65] = "Portamento",
            [66] = "Sostenuto", [67] = "Soft pedal", [68] = "Legato", [69] = "Hold 2", [71] = "Resonance",
            [72] = "Release time", [73] = "Attack time", [74] = "Brightness", [75] = "Decay time", [76] = "Vibrato rate",
            [77] = "Vibrato depth", [78] = "Vibrato delay", [84] = "Portamento control", [91] = "Reverb", [92] = "Tremolo",
            [93] = "Chorus", [94] = "Detune", [95] = "Phaser", [96] = "Data +", [97] = "Data −",
            [98] = "NRPN LSB", [99] = "NRPN MSB", [100] = "RPN LSB", [101] = "RPN MSB",
        };

        /// <summary>Controllers offered first in the lane list.</summary>
        public static readonly int[] Common = [1, 7, 10, 11, 64, 66, 67, 91, 93, 74, 71, 2, 0, 32];

        public static string Label(int cc) => Names.TryGetValue(cc, out var name) ? $"CC {cc} {name}" : $"CC {cc}";
    }

    /// <summary>What a lane needs from the window, besides the piano roll host.</summary>
    public interface ILaneHost : IRollHost
    {
        /// <summary>Selected notes of the active track.</summary>
        HashSet<SongNote> Selection { get; }

        /// <summary>Colour of the active track.</summary>
        Color ActiveColor { get; }

        /// <summary>Show a value under the mouse (or null to clear).</summary>
        void ShowLaneValue(string? text);

        /// <summary>Choose a sound (bank and program) for a program change at the tick on the active track.</summary>
        void PickInstrument(long tick);

        /// <summary>The text of a program change: its sound, with the bank in effect at the tick.</summary>
        string ProgramLabel(long tick, int program);
    }

    /// <summary>
    /// The lane under the piano roll, on the same time axis: note velocities as stems, or a
    /// controller, pitch bend, pressure or program curve of the active track.
    ///
    /// Drag to paint values (on the grid), Shift+drag to draw a straight line. In the velocity lane
    /// right-drag also draws a line; with notes selected, only those are changed. In the other lanes
    /// right-drag erases. Every drag is one undo step.
    /// </summary>
    public sealed class ControllerLane : FrameworkElement
    {
        private const double Pad = 5;

        private enum Drag { None, Paint, Line, Erase }

        private ILaneHost? _host;
        private NinePatchZoomViewer? _viewer;
        private FrameworkElement? _surface;

        private Drag _drag;
        private Point _downPoint;
        private Point _lastPoint;
        private readonly Dictionary<SongNote, int> _velocityPreview = new(ReferenceEqualityComparer.Instance);
        private readonly SortedDictionary<long, int> _paint = new();   // tick → value, while painting a controller
        private (long From, long To)? _eraseRange;

        public LaneMode Mode { get; private set; } = new(LaneKind.Velocity);

        public void Attach(ILaneHost host, NinePatchZoomViewer viewer, FrameworkElement surface)
        {
            _host = host;
            _viewer = viewer;
            _surface = surface;
            viewer.ViewChanged += (_, _) => InvalidateVisual();
            ClipToBounds = true;
            Cursor = Cursors.Pen;
        }

        public void SetMode(LaneMode mode)
        {
            CancelDrag();
            Mode = mode;
            ToolTip = mode.Kind == LaneKind.Velocity
                ? "Drag over the stems to set velocities (only the selected notes, if any). Shift+drag or right-drag draws a straight line."
                : mode.Kind == LaneKind.Tempo
                    ? "Tempo of the whole song, in BPM. Drag to draw tempo changes, Shift+drag for a gradual change, right-drag to erase."
                : mode.Kind == LaneKind.Program
                    ? "Click to choose an instrument (bank and program) at the grid line, or click a program change to change it. Right-drag erases."
                    : "Drag to draw values on the grid, Shift+drag for a straight line, right-drag to erase. Alt turns snapping off.";
            InvalidateVisual();
        }

        // --- Coordinates: the lane uses the piano roll's time axis, offset to where the note area starts ---

        private double AreaLeft => _surface != null && _surface.IsVisible ? _surface.TranslatePoint(new Point(0, 0), this).X : 60;
        private double AreaWidth => _surface?.ActualWidth ?? Math.Max(0, ActualWidth - 60);

        private double TickToX(double tick) => AreaLeft + (_viewer?.WorldToViewX(tick) ?? 0);
        private double XToTick(double x) => Math.Max(0, _viewer?.ViewToWorldX(x - AreaLeft) ?? 0);

        private double ValueToY(double value) => Pad + (1 - value / Mode.Max) * Math.Max(1, ActualHeight - 2 * Pad);

        private int YToValue(double y) =>
            (int)Math.Round(Math.Clamp(1 - (y - Pad) / Math.Max(1, ActualHeight - 2 * Pad), 0, 1) * Mode.Max);

        private long Step(bool line)
        {
            var song = _host?.Song;
            long tpq = song?.TicksPerQuarter ?? 480;
            long grid = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) ? 0 : _host?.SnapTicks ?? 0;
            if (grid <= 0) grid = Math.Max(1, tpq / 8);
            // Lines are drawn finer than the grid so ramps sound smooth.
            return line ? Math.Max(1, Math.Min(grid, tpq / 16)) : grid;
        }

        // --- Drawing ---

        protected override void OnRender(DrawingContext dc)
        {
            var size = RenderSize;
            dc.DrawRectangle(LaneBrushes.Background, null, new Rect(size));
            if (_host?.Song is not { } song || _viewer == null || _host.ActiveTrack is not { } track) return;
            bool tempo = Mode.Kind == LaneKind.Tempo;

            double left = AreaLeft, width = AreaWidth;
            double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            dc.PushClip(new RectangleGeometry(new Rect(left, 0, Math.Max(0, width), size.Height)));

            // Bar and beat lines.
            long beat = song.TicksPerBeat, bar = song.TicksPerBar;
            bool beats = beat * _viewer.ZoomX >= 6;
            long step = beats ? beat : bar * PianoRollStyle.BarStep(bar * _viewer.ZoomX, 6);
            double first = Math.Floor(_viewer.OffsetX / step) * step, last = _viewer.ViewToWorldX(width);
            for (double tick = first; tick <= last; tick += step)
            {
                double x = PianoRollStyle.Snap(TickToX(tick), ppd);
                dc.DrawRectangle((long)tick % bar == 0 ? LaneBrushes.Bar : LaneBrushes.Beat, null, new Rect(x, 0, 1 / ppd, size.Height));
            }

            // Centre (pitch bend, pan) or the 64 line (pedals), and the top and bottom lines.
            if (Mode.Centre >= 0) dc.DrawRectangle(LaneBrushes.Centre, null, new Rect(left, PianoRollStyle.Snap(ValueToY(Mode.Centre), ppd), width, 1 / ppd));
            dc.DrawRectangle(LaneBrushes.Bar, null, new Rect(left, PianoRollStyle.Snap(ValueToY(Mode.Max), ppd), width, 1 / ppd));

            var color = tempo ? Color.FromRgb(0xF5, 0xB0, 0x41) : _host.ActiveColor;
            if (Mode.Kind == LaneKind.Velocity) DrawVelocities(dc, track, color, ppd);
            else DrawCurve(dc, track, color, ppd);

            if (_eraseRange is { } range)
            {
                double x1 = TickToX(range.From), x2 = TickToX(range.To);
                dc.DrawRectangle(LaneBrushes.Erase, null, new Rect(Math.Min(x1, x2), 0, Math.Abs(x2 - x1), size.Height));
            }
            dc.Pop();
        }

        private void DrawVelocities(DrawingContext dc, SongTrack track, Color color, double ppd)
        {
            var fill = Kit.Solid(color);
            var selectedFill = Kit.Solid(Palette.Lighten(color, 0.55));
            var selection = _host!.Selection;
            double bottom = ValueToY(0);
            double t1 = _viewer!.OffsetX - 1, t2 = _viewer.ViewToWorldX(AreaWidth) + 1;
            bool wide = _viewer.ZoomX * (_host.Song!.TicksPerQuarter / 4.0) >= 7;   // room for a head on each stem

            foreach (var n in track.Notes)
            {
                if (n.Start < t1) continue;
                if (n.Start > t2) break;
                int velocity = _velocityPreview.TryGetValue(n, out var v) ? v : n.Velocity;
                double x = PianoRollStyle.Snap(TickToX(n.Start), ppd);
                double y = ValueToY(velocity);
                bool selected = selection.Contains(n);
                var brush = selected ? selectedFill : fill;
                dc.DrawRectangle(brush, null, new Rect(x, y, wide ? 2 : 1, Math.Max(1, bottom - y)));
                if (wide) dc.DrawRectangle(brush, null, new Rect(x - 2, y - 1, 6, 3));
            }
        }

        private void DrawCurve(DrawingContext dc, SongTrack track, Color color, double ppd)
        {
            var points = CurvePoints(track);
            if (points.Count == 0) return;

            var line = Kit.Pen(color, 1.5);
            var area = Kit.Solid(color.WithAlpha(0.22));
            var dot = Kit.Solid(Palette.Lighten(color, 0.35));
            double baseY = ValueToY(Mode.Centre >= 0 && Mode.Kind == LaneKind.PitchBend ? Mode.Centre : 0);
            double right = ActualWidth;

            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                for (int i = 0; i < points.Count; i++)
                {
                    double x = TickToX(points[i].Tick);
                    double x2 = i + 1 < points.Count ? TickToX(points[i + 1].Tick) : right;
                    if (x2 < 0 || x > right) continue;
                    double y = ValueToY(points[i].Value);
                    g.BeginFigure(new Point(x, baseY), true, true);
                    g.LineTo(new Point(x, y), false, false);
                    g.LineTo(new Point(x2, y), false, false);
                    g.LineTo(new Point(x2, baseY), false, false);
                }
            }
            geometry.Freeze();
            dc.DrawGeometry(area, null, geometry);

            bool dots = points.Count < 2 || (TickToX(points[^1].Tick) - TickToX(points[0].Tick)) / points.Count >= 4;
            for (int i = 0; i < points.Count; i++)
            {
                double x = TickToX(points[i].Tick);
                double x2 = i + 1 < points.Count ? TickToX(points[i + 1].Tick) : right;
                if (x2 < 0 || x > right) continue;
                double y = ValueToY(points[i].Value);
                dc.DrawLine(line, new Point(x, y), new Point(x2, y));
                if (i + 1 < points.Count) dc.DrawLine(line, new Point(x2, y), new Point(x2, ValueToY(points[i + 1].Value)));
                if (dots) dc.DrawEllipse(dot, null, new Point(x, y), 2.5, 2.5);

                if (Mode.Kind == LaneKind.Program && x2 - x > 40)
                {
                    var text = new FormattedText(_host!.ProgramLabel(points[i].Tick, points[i].Value), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                                 PianoRollStyle.Typeface, 10, LaneBrushes.Text, ppd);
                    text.MaxTextWidth = Math.Max(1, x2 - x - 8);
                    text.MaxLineCount = 1;
                    text.Trimming = TextTrimming.CharacterEllipsis;
                    dc.DrawText(text, new Point(x + 5, Math.Max(1, y - 14)));
                }
            }
        }

        /// <summary>The lane's events of the track (on any channel), with the painting preview applied.</summary>
        private List<(long Tick, int Value)> CurvePoints(SongTrack track)
        {
            var points = Mode.Kind == LaneKind.Tempo
                ? _host!.Song!.Tempos.Select(t => (t.Tick, (int)Math.Round(t.Bpm))).ToList()
                : track.Controls.Where(Mode.Matches).Select(c => (c.Tick, c.Value)).ToList();
            if (_paint.Count > 0)
            {
                long from = _paint.Keys.First(), to = _paint.Keys.Last();
                points.RemoveAll(p => p.Tick >= from && p.Tick <= to);
                points.AddRange(_paint.Select(p => (p.Key, p.Value)));
                points.Sort((a, b) => a.Tick.CompareTo(b.Tick));
            }
            if (_eraseRange is { } r)
            {
                long from = Math.Min(r.From, r.To), to = Math.Max(r.From, r.To);
                points.RemoveAll(p => p.Tick >= from && p.Tick <= to);
            }
            return points;
        }

        // --- Mouse ---

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) => Begin(e, right: false);
        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e) => Begin(e, right: true);

        private void Begin(MouseButtonEventArgs e, bool right)
        {
            e.Handled = true;
            if (_host?.ActiveTrack == null || _drag != Drag.None) return;
            var p = e.GetPosition(this);
            if (p.X < AreaLeft) return;
            _downPoint = _lastPoint = p;
            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

            if (Mode.Kind == LaneKind.Program && !right)
            {
                // A click opens the instrument picker: on a program change near the mouse, or at the grid line.
                var track = _host.ActiveTrack!;
                long tick = NoteOps.Snap(XToTick(p.X), Step(false));
                var near = track.Controls.Where(Mode.Matches).Select(c => c.Tick)
                                .Where(t => Math.Abs(TickToX(t) - p.X) <= 6).OrderBy(t => Math.Abs(TickToX(t) - p.X)).ToList();
                if (near.Count > 0) tick = near[0];
                _host.PickInstrument(Math.Max(0, tick));
                return;
            }

            if (Mode.Kind == LaneKind.Velocity) _drag = right || shift ? Drag.Line : Drag.Paint;
            else if (right) _drag = Drag.Erase;
            else _drag = shift && Mode.Kind != LaneKind.Program ? Drag.Line : Drag.Paint;

            CaptureMouse();
            Update(p);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var p = e.GetPosition(this);
            if (_drag == Drag.None)
            {
                _host?.ShowLaneValue(p.X < AreaLeft || _host.ActiveTrack == null ? null
                                     : Mode.Kind == LaneKind.Program ? "Click to choose an instrument (bank and program). Right-drag erases."
                                     : $"{Mode.Name}: {Mode.Format(YToValue(p.Y))}");
                return;
            }
            Update(p);
            _host?.ShowLaneValue($"{Mode.Name}: {Mode.Format(YToValue(p.Y))}");
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            if (_drag == Drag.None) _host?.ShowLaneValue(null);
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) => End(e);
        protected override void OnMouseRightButtonUp(MouseButtonEventArgs e) => End(e);

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            if (_drag != Drag.None) CancelDrag();
        }

        public void CancelDrag()
        {
            _drag = Drag.None;
            _velocityPreview.Clear();
            _paint.Clear();
            _eraseRange = null;
            if (IsMouseCaptured) ReleaseMouseCapture();
            InvalidateVisual();
        }

        private void Update(Point p)
        {
            var track = _host!.ActiveTrack!;
            switch (Mode.Kind, _drag)
            {
                case (LaneKind.Velocity, Drag.Paint):
                    PaintVelocities(track, _lastPoint, p);
                    break;
                case (LaneKind.Velocity, Drag.Line):
                    _velocityPreview.Clear();
                    PaintVelocities(track, _downPoint, p);
                    break;
                case (_, Drag.Paint):
                    PaintCurve(_lastPoint, p, line: false);
                    break;
                case (_, Drag.Line):
                    _paint.Clear();
                    PaintCurve(_downPoint, p, line: true);
                    break;
                case (_, Drag.Erase):
                    _eraseRange = ((long)XToTick(_downPoint.X), (long)XToTick(p.X));
                    break;
            }
            _lastPoint = p;
            InvalidateVisual();
        }

        /// <summary>Set the velocity of the notes whose stems lie between two mouse positions, interpolating the height.</summary>
        private void PaintVelocities(SongTrack track, Point a, Point b)
        {
            if (a.X > b.X) (a, b) = (b, a);
            double tolerance = 3;   // a click hits a stem within a few pixels
            double t1 = XToTick(a.X - tolerance), t2 = XToTick(b.X + tolerance);
            var selection = _host!.Selection;
            bool onlySelected = selection.Count > 0;

            foreach (var n in track.Notes)
            {
                if (n.Start < t1) continue;
                if (n.Start > t2) break;
                if (onlySelected && !selection.Contains(n)) continue;
                double x = TickToX(n.Start);
                double f = b.X - a.X < 1 ? 0 : Math.Clamp((x - a.X) / (b.X - a.X), 0, 1);
                _velocityPreview[n] = Math.Clamp(YToValue(a.Y + (b.Y - a.Y) * f), 1, 127);
            }
        }

        /// <summary>Put values on every step between two mouse positions, interpolating the height.</summary>
        private void PaintCurve(Point a, Point b, bool line)
        {
            if (Mode.Kind == LaneKind.Program)
            {
                // One program change at the grid line nearest the mouse.
                _paint.Clear();
                _paint[NoteOps.Snap(XToTick(b.X), Step(false))] = YToValue(b.Y);
                return;
            }

            long step = Step(line);
            if (a.X > b.X) (a, b) = (b, a);
            long s1 = NoteOps.Snap(XToTick(a.X), step), s2 = NoteOps.Snap(XToTick(b.X), step);
            for (long tick = s1; tick <= s2; tick += step)
            {
                double x = TickToX(tick);
                double f = b.X - a.X < 1 ? 0 : Math.Clamp((x - a.X) / (b.X - a.X), 0, 1);
                _paint[tick] = YToValue(a.Y + (b.Y - a.Y) * f);
            }
        }

        private void End(MouseButtonEventArgs e)
        {
            if (_drag == Drag.None) return;
            e.Handled = true;
            var drag = _drag;
            _drag = Drag.None;
            if (IsMouseCaptured) ReleaseMouseCapture();
            var track = _host!.ActiveTrack;
            if (track == null)
            {
                CancelDrag();
                return;
            }

            if (Mode.Kind == LaneKind.Velocity)
            {
                var changes = _velocityPreview.Where(kv => kv.Key.Velocity != kv.Value).ToList();
                _velocityPreview.Clear();
                if (changes.Count > 0)
                {
                    var old = changes.Select(kv => kv.Key).ToList();
                    var updated = changes.Select(kv => kv.Key with { Velocity = kv.Value }).ToList();
                    // The edit replaces the notes with new ones; selected notes stay selected.
                    var selection = _host.Selection.ToList();
                    var keep = selection.Where(n => !old.Contains(n, ReferenceEqualityComparer.Instance)).ToList();
                    for (int i = 0; i < old.Count; i++)
                        if (selection.Contains(old[i], ReferenceEqualityComparer.Instance)) keep.Add(updated[i]);
                    _host.Commit(changes.Count == 1 ? "Change velocity" : "Change velocities",
                                 () => track.Notes = NoteOps.Replace(track.Notes, old, updated));
                    if (selection.Count > 0) SelectionRequested?.Invoke(this, keep);
                }
            }
            else if (Mode.Kind == LaneKind.Tempo)
            {
                CommitTempo(drag);
            }
            else if (drag == Drag.Erase && _eraseRange is { } r)
            {
                long from = Math.Min(r.From, r.To), to = Math.Max(r.From, r.To);
                // Erasing a program change takes its bank select with it.
                var gone = Mode.Kind == LaneKind.Program ? SongOps.ProgramsWithBanks(track, from, to)
                         : track.Controls.Where(c => Mode.Matches(c) && c.Tick >= from && c.Tick <= to).ToList();
                if (gone.Count > 0)
                    _host.Commit($"Erase {Mode.Name.ToLowerInvariant()}",
                                 () => track.Controls = Without(track.Controls, gone));
            }
            else if (_paint.Count > 0)
            {
                long from = _paint.Keys.First(), to = _paint.Keys.Last();
                var paint = _paint.ToList();
                var gone = track.Controls.Where(c => Mode.Matches(c) && c.Tick >= from && c.Tick <= to).ToList();
                // Painting goes on the track's own channel; a value equal to the one before it is left out.
                var added = new List<SongCtrl>();
                int? previous = track.Controls.Where(c => Mode.Matches(c) && c.Tick < from).Select(c => (int?)c.Value).LastOrDefault();
                foreach (var (tick, value) in paint)
                {
                    if (value == previous && Mode.Kind != LaneKind.Program && paint.Count > 1) continue;
                    added.Add(Mode.Make(tick, track.Channel, value));
                    previous = value;
                }
                _host.Commit(drag == Drag.Line ? $"Draw {Mode.Name.ToLowerInvariant()} line" : $"Draw {Mode.Name.ToLowerInvariant()}",
                             () => track.Controls = Without(track.Controls, gone).Concat(added).OrderBy(c => c.Tick).ToList());
            }

            _paint.Clear();
            _eraseRange = null;
            InvalidateVisual();
        }

        /// <summary>Tempo painting and erasing change the song's tempo list (the tempo at tick 0 always stays).</summary>
        private void CommitTempo(Drag drag)
        {
            var song = _host!.Song!;
            if (drag == Drag.Erase && _eraseRange is { } r)
            {
                long from = Math.Min(r.From, r.To), to = Math.Max(r.From, r.To);
                if (song.Tempos.Any(t => t.Tick > 0 && t.Tick >= from && t.Tick <= to))
                    _host.Commit("Erase tempo changes", () => song.Tempos = song.Tempos.Where(t => t.Tick == 0 || t.Tick < from || t.Tick > to).ToList());
            }
            else if (_paint.Count > 0)
            {
                long from = _paint.Keys.First(), to = _paint.Keys.Last();
                var added = new List<TempoChange>();
                int? previous = song.Tempos.Where(t => t.Tick < from).Select(t => (int?)(int)Math.Round(t.Bpm)).LastOrDefault();
                foreach (var (tick, value) in _paint)
                {
                    int bpm = Math.Max(10, value);
                    if (bpm == previous && _paint.Count > 1) continue;
                    added.Add(new TempoChange(tick, TempoChange.FromBpm(bpm)));
                    previous = bpm;
                }
                _host.Commit(drag == Drag.Line ? "Draw tempo ramp" : "Draw tempo",
                             () => song.Tempos = song.Tempos
                                 .Where(t => t.Tick < from || t.Tick > to || (t.Tick == 0 && from > 0))
                                 .Concat(added)
                                 .OrderBy(t => t.Tick)
                                 .ToList());
            }
        }

        private static List<SongCtrl> Without(IEnumerable<SongCtrl> controls, IEnumerable<SongCtrl> remove)
        {
            var gone = new HashSet<SongCtrl>(remove, ReferenceEqualityComparer.Instance);
            return controls.Where(c => !gone.Contains(c)).ToList();
        }

        /// <summary>The lane wants these notes selected (after a velocity edit).</summary>
        public event EventHandler<List<SongNote>>? SelectionRequested;
    }

    internal static class LaneBrushes
    {
        /// <summary>Takes the colours from the current theme (resource keys "Lane…"); missing keys keep their defaults.</summary>
        public static void Reload()
        {
            Background = ThemeColors.Brush("LaneBackground", D.Background);
            Bar = ThemeColors.Brush("LaneBar", D.Bar);
            Beat = ThemeColors.Brush("LaneBeat", D.Beat);
            Centre = ThemeColors.Brush("LaneCentre", D.Centre);
            Text = ThemeColors.Brush("LaneText", D.Text);
        }
        /// <summary>The built-in colours, kept for keys a theme does not define.</summary>
        private static class D
        {
            public static readonly Brush Background = LaneBrushes.Background;
            public static readonly Brush Bar = LaneBrushes.Bar;
            public static readonly Brush Beat = LaneBrushes.Beat;
            public static readonly Brush Centre = LaneBrushes.Centre;
            public static readonly Brush Text = LaneBrushes.Text;
        }

        public static Brush Background { get; private set; } = Kit.Solid(Color.FromRgb(0x17, 0x18, 0x1B));
        public static Brush Bar { get; private set; } = Kit.Solid(Color.FromRgb(0x38, 0x3A, 0x40));
        public static Brush Beat { get; private set; } = Kit.Solid(Color.FromRgb(0x24, 0x25, 0x29));
        public static Brush Centre { get; private set; } = Kit.Solid(Color.FromRgb(0x50, 0x53, 0x5A));
        public static Brush Text { get; private set; } = Kit.Solid(Color.FromRgb(0xC8, 0xCC, 0xD4));
        public static readonly Brush Erase = Kit.Solid(Color.FromArgb(0x40, 0xF2, 0x4E, 0x6E));
    }
}
