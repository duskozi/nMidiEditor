using nMIDI.Model;
using nMIDI.Piano;
using nMIDI.PianoRoll;
using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace nMIDI.Lanes
{
    /// <summary>Colours and drawing helpers shared by the lanes and their ruler.</summary>
    internal static class LaneStyle
    {
        /// <summary>Takes the colours from the current theme (resource keys "Lanes…"); missing keys keep their defaults.</summary>
        public static void Reload()
        {
            Background = ThemeColors.Brush("LanesBackground", D.Background);
            Separator = ThemeColors.Brush("LanesSeparator", D.Separator);
            BarLine = ThemeColors.Brush("LanesBarLine", D.BarLine);
            BeatLine = ThemeColors.Brush("LanesBeatLine", D.BeatLine);
        }
        /// <summary>The built-in colours, kept for keys a theme does not define.</summary>
        private static class D
        {
            public static readonly Brush Background = LaneStyle.Background;
            public static readonly Brush Separator = LaneStyle.Separator;
            public static readonly Brush BarLine = LaneStyle.BarLine;
            public static readonly Brush BeatLine = LaneStyle.BeatLine;
        }

        public static Brush Background { get; private set; } = Kit.Solid(Color.FromRgb(0x1B, 0x1C, 0x1F));
        public static Brush Separator { get; private set; } = Kit.Solid(Color.FromRgb(0x0E, 0x0F, 0x11));
        public static Brush BarLine { get; private set; } = Kit.Solid(Color.FromRgb(0x33, 0x34, 0x39));
        public static Brush BeatLine { get; private set; } = Kit.Solid(Color.FromRgb(0x24, 0x25, 0x29));
        public static readonly Brush Range = Kit.Solid(Color.FromArgb(0x38, 0x2D, 0x8C, 0xFF));
        public static readonly Brush RangeMove = Kit.Solid(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
        public static readonly Pen RangeEdge = Kit.Pen(Color.FromArgb(0xC0, 0x7F, 0xB8, 0xFF), 1);

        /// <summary>Bar lines, and beat lines when they are far enough apart, across a rectangle.</summary>
        public static void DrawGrid(DrawingContext dc, TimeAxis axis, double top, double height, double pixelsPerDip)
        {
            long ticksPerBar = axis.TicksPerBeat * axis.BeatsPerBar;
            double barPixels = ticksPerBar * axis.PixelsPerTick;
            bool beats = axis.TicksPerBeat * axis.PixelsPerTick >= 8;
            long step = beats ? axis.TicksPerBeat : ticksPerBar * PianoRollStyle.BarStep(barPixels, 8);
            double onePixel = 1 / pixelsPerDip;

            for (long tick = (long)Math.Floor(axis.Start / step) * step; tick <= axis.End; tick += step)
            {
                double x = PianoRollStyle.Snap(axis.TickToX(tick), pixelsPerDip);
                dc.DrawRectangle(tick % ticksPerBar == 0 ? BarLine : BeatLine, null, new Rect(x, top, onePixel, height));
            }
        }
    }

    /// <summary>
    /// One track's notes in a strip, like an item in a DAW arrange view. The strip always fits the
    /// track's own note range, so a bass line and a flute part both fill their lane. Taller lanes
    /// simply give each note row more room.
    /// </summary>
    /// <summary>A time range selected in one lane of the track lanes window, shared by all lanes.</summary>
    public sealed class LaneRange
    {
        public TrackViewModel? Track { get; private set; }
        public long Start { get; private set; }
        public long End { get; private set; }

        /// <summary>While the range is dragged: how far it moves, in ticks.</summary>
        public long MoveBy { get; private set; }

        public bool IsEmpty => Track == null || End <= Start;

        public event EventHandler? Changed;

        public void Set(TrackViewModel? track, long start, long end)
        {
            Track = track;
            Start = Math.Max(0, Math.Min(start, end));
            End = Math.Max(start, end);
            MoveBy = 0;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void SetMove(long by)
        {
            MoveBy = by;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Clear() => Set(null, 0, 0);
    }

    public sealed class TrackLane : FrameworkElement
    {
        public static readonly DependencyProperty RangeProperty = DependencyProperty.Register(
            nameof(Range), typeof(LaneRange), typeof(TrackLane), new PropertyMetadata(null, OnRangeChanged));

        public LaneRange? Range
        {
            get => (LaneRange?)GetValue(RangeProperty);
            set => SetValue(RangeProperty, value);
        }

        /// <summary>The dragged range was dropped this many ticks later (or earlier).</summary>
        public event EventHandler<long>? RangeMoved;

        private enum LaneDrag { None, Pending, Select, Move }
        private LaneDrag _drag;
        private Point _down;
        private bool _downInRange;

        private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var lane = (TrackLane)d;
            if (e.OldValue is LaneRange old) old.Changed -= lane.OnRangeMoved;
            if (e.NewValue is LaneRange range) range.Changed += lane.OnRangeMoved;
            lane.InvalidateVisual();
        }

        private void OnRangeMoved(object? sender, EventArgs e) => InvalidateVisual();
        public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
            nameof(Track), typeof(TrackViewModel), typeof(TrackLane), new PropertyMetadata(null, OnTrackChanged));

        public static readonly DependencyProperty AxisProperty = DependencyProperty.Register(
            nameof(Axis), typeof(TimeAxis), typeof(TrackLane), new PropertyMetadata(null, OnAxisChanged));

        private int _low, _high;          // note range shown, inclusive
        private long _longestNote;        // lets the visible-note search start early enough

        public TrackLane()
        {
            SnapsToDevicePixels = true;
            ClipToBounds = true;
            Unloaded += (_, _) => Detach();
            Loaded += (_, _) => Attach();
        }

        public TrackViewModel? Track
        {
            get => (TrackViewModel?)GetValue(TrackProperty);
            set => SetValue(TrackProperty, value);
        }

        public TimeAxis? Axis
        {
            get => (TimeAxis?)GetValue(AxisProperty);
            set => SetValue(AxisProperty, value);
        }

        /// <summary>The user clicked the lane at this tick.</summary>
        public event EventHandler<double>? Seek;

        private static void OnTrackChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var lane = (TrackLane)d;
            if (e.OldValue is TrackViewModel old) old.PropertyChanged -= lane.OnTrackPropertyChanged;
            if (e.NewValue is TrackViewModel vm)
            {
                vm.PropertyChanged += lane.OnTrackPropertyChanged;
                lane.MeasureNotes(vm.Track);
            }
            lane.InvalidateVisual();
        }

        private static void OnAxisChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var lane = (TrackLane)d;
            if (e.OldValue is TimeAxis old) old.Changed -= lane.OnAxisMoved;
            if (e.NewValue is TimeAxis axis) axis.Changed += lane.OnAxisMoved;
            lane.InvalidateVisual();
        }

        // Lanes are recreated when a new song is loaded, so they must not stay subscribed after that.
        private void Detach()
        {
            if (Range != null) Range.Changed -= OnRangeMoved;
            if (Axis != null) Axis.Changed -= OnAxisMoved;
            if (Track != null) Track.PropertyChanged -= OnTrackPropertyChanged;
        }

        private void Attach()
        {
            Detach();
            if (Range != null) Range.Changed += OnRangeMoved;
            if (Axis != null) Axis.Changed += OnAxisMoved;
            if (Track != null) Track.PropertyChanged += OnTrackPropertyChanged;
            InvalidateVisual();
        }

        private void OnAxisMoved(object? sender, EventArgs e) => InvalidateVisual();

        private void OnTrackPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TrackViewModel.IsAudible)) InvalidateVisual();
            else if (e.PropertyName == nameof(TrackViewModel.NotesText) && Track != null)
            {
                // The notes were edited.
                MeasureNotes(Track.Track);
                InvalidateVisual();
            }
        }

        private void MeasureNotes(SongTrack track)
        {
            _longestNote = 0;
            if (track.Notes.Count == 0)
            {
                _low = 48;
                _high = 72;
                return;
            }

            int low = 127, high = 0;
            foreach (var note in track.Notes)
            {
                low = Math.Min(low, note.Number);
                high = Math.Max(high, note.Number);
                _longestNote = Math.Max(_longestNote, note.End - note.Start);
            }

            // At least an octave, so a part that repeats one note does not draw one huge bar.
            int missing = 12 - (high - low);
            if (missing > 0)
            {
                low -= missing / 2;
                high += missing - missing / 2;
            }
            _low = low;
            _high = high;
        }

        protected override void OnRender(DrawingContext dc)
        {
            var size = RenderSize;
            dc.DrawRectangle(LaneStyle.Background, null, new Rect(size));
            if (Track is not { } vm || Axis is not { } axis || size.Width <= 0 || size.Height <= 2) return;

            double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            LaneStyle.DrawGrid(dc, axis, 0, size.Height, ppd);

            // Notes: a little padding at the top and bottom, one row per semitone in between.
            const double pad = 3;
            int rows = _high - _low + 1;
            double rowHeight = (size.Height - 2 * pad) / rows;
            double noteHeight = Math.Max(1, rowHeight - (rowHeight >= 4 ? 1 : 0));
            var color = vm.IsAudible ? vm.Color : NoteSkins.Dimmed(vm.Color);
            var fill = Kit.Solid(color);
            var edge = rowHeight >= 5 ? Kit.Pen(Palette.Darken(color, 0.45), 1) : null;

            var notes = vm.Track.Notes;
            double end = axis.End;
            for (int i = FirstNoteFrom(notes, axis.Start - _longestNote); i < notes.Count; i++)
            {
                var note = notes[i];
                if (note.Start > end) break;
                if (note.End < axis.Start) continue;

                double x1 = axis.TickToX(note.Start);
                double x2 = axis.TickToX(note.End);
                double y = pad + (_high - note.Number) * rowHeight;
                var rect = new Rect(x1, y, Math.Max(1, x2 - x1 - 1), noteHeight);
                dc.DrawRectangle(fill, rect.Width >= 4 ? edge : null, rect);
            }

            // The selected range, and where it goes while it is dragged.
            if (Range is { IsEmpty: false } range && range.Track == vm)
            {
                double r1 = axis.TickToX(range.Start), r2 = axis.TickToX(range.End);
                dc.DrawRectangle(LaneStyle.Range, LaneStyle.RangeEdge, new Rect(r1, 0.5, Math.Max(1, r2 - r1), size.Height - 2));
                if (range.MoveBy != 0)
                {
                    double m1 = axis.TickToX(range.Start + range.MoveBy), m2 = axis.TickToX(range.End + range.MoveBy);
                    dc.DrawRectangle(LaneStyle.RangeMove, LaneStyle.RangeEdge, new Rect(m1, 0.5, Math.Max(1, m2 - m1), size.Height - 2));
                }
            }

            dc.DrawRectangle(LaneStyle.Separator, null, new Rect(0, size.Height - 1, size.Width, 1));
        }

        /// <summary>Index of the first note that starts at or after <paramref name="tick"/> (notes are sorted by start).</summary>
        private static int FirstNoteFrom(System.Collections.Generic.IReadOnlyList<SongNote> notes, double tick)
        {
            int lo = 0, hi = notes.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (notes[mid].Start < tick) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        // A click moves the playhead; a drag selects a time range of this track; dragging inside the
        // selected range moves it. Positions snap to beats (Alt: no snapping).
        private long SnapTick(double x)
        {
            if (Axis == null) return 0;
            double tick = Math.Max(0, Axis.XToTick(x));
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) return (long)Math.Round(tick);
            long beat = Math.Max(1, Axis.TicksPerBeat);
            return (long)Math.Round(tick / beat) * beat;
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (Axis == null) return;
            e.Handled = true;
            Focus();
            _down = e.GetPosition(this);
            double tick = Axis.XToTick(_down.X);
            _downInRange = Range is { IsEmpty: false } r && r.Track == Track && tick >= r.Start && tick <= r.End;
            _drag = LaneDrag.Pending;
            CaptureMouse();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_drag == LaneDrag.None || Axis == null || Range == null) return;
            var p = e.GetPosition(this);
            if (_drag == LaneDrag.Pending)
            {
                if (Math.Abs(p.X - _down.X) < 4) return;
                _drag = _downInRange ? LaneDrag.Move : LaneDrag.Select;
            }
            if (_drag == LaneDrag.Select) Range.Set(Track, SnapTick(_down.X), SnapTick(p.X));
            else
            {
                long by = SnapTick(p.X) - SnapTick(_down.X);
                Range.SetMove(Math.Max(by, -Range.Start));
            }
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            var drag = _drag;
            _drag = LaneDrag.None;
            if (IsMouseCaptured) ReleaseMouseCapture();
            if (Axis == null) return;
            switch (drag)
            {
                case LaneDrag.Pending:
                    Range?.Clear();
                    Seek?.Invoke(this, Math.Max(0, Axis.XToTick(e.GetPosition(this).X)));
                    break;
                case LaneDrag.Move when Range is { MoveBy: not 0 } range:
                    long by = range.MoveBy;
                    range.SetMove(0);
                    RangeMoved?.Invoke(this, by);
                    break;
            }
            e.Handled = true;
        }
    }

    /// <summary>Bar numbers above the lanes. Click or drag on it to move the playhead.</summary>
    public sealed class LaneRuler : FrameworkElement
    {
        private TimeAxis? _axis;

        public LaneRuler()
        {
            Cursor = Cursors.Hand;
            ToolTip = "Click or drag to move the playhead";
        }

        public TimeAxis? Axis
        {
            get => _axis;
            set
            {
                if (_axis != null) _axis.Changed -= OnAxisMoved;
                _axis = value;
                if (_axis != null) _axis.Changed += OnAxisMoved;
                InvalidateVisual();
            }
        }

        /// <summary>The user clicked or dragged to this tick.</summary>
        public event EventHandler<double>? Seek;

        private void OnAxisMoved(object? sender, EventArgs e) => InvalidateVisual();

        protected override void OnRender(DrawingContext dc)
        {
            var size = RenderSize;
            double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            dc.DrawRectangle(PianoRollStyle.RulerBackground, null, new Rect(size));
            dc.DrawRectangle(PianoRollStyle.RulerEdge, null, new Rect(0, size.Height - 1, size.Width, 1));
            if (_axis is not { } axis) return;

            long ticksPerBar = axis.TicksPerBeat * axis.BeatsPerBar;
            double barPixels = ticksPerBar * axis.PixelsPerTick;
            long labelStep = PianoRollStyle.BarStep(barPixels, 44);
            bool beats = axis.TicksPerBeat * axis.PixelsPerTick >= 8;
            long step = beats ? axis.TicksPerBeat : ticksPerBar * PianoRollStyle.BarStep(barPixels, 8);

            dc.PushClip(new RectangleGeometry(new Rect(size)));
            for (long tick = (long)Math.Floor(axis.Start / step) * step; tick <= axis.End; tick += step)
            {
                double x = PianoRollStyle.Snap(axis.TickToX(tick), ppd);
                long bar = tick / ticksPerBar;
                bool isBar = tick % ticksPerBar == 0;
                bool labelled = isBar && bar % labelStep == 0;
                double height = labelled ? size.Height * 0.55 : isBar ? size.Height * 0.35 : size.Height * 0.2;
                dc.DrawRectangle(PianoRollStyle.RulerTick, null, new Rect(x, size.Height - 1 - height, 1, height));

                if (labelled)
                {
                    var text = new FormattedText((bar + 1).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                                                 FlowDirection.LeftToRight, PianoRollStyle.Typeface, 10, PianoRollStyle.RulerText, ppd);
                    dc.DrawText(text, new Point(x + 3, 2));
                }
            }
            dc.Pop();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            CaptureMouse();
            RaiseSeek(e.GetPosition(this).X);
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (IsMouseCaptured) RaiseSeek(e.GetPosition(this).X);
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (IsMouseCaptured) ReleaseMouseCapture();
        }

        private void RaiseSeek(double x)
        {
            if (_axis == null) return;
            Seek?.Invoke(this, Math.Clamp(_axis.XToTick(x), 0, _axis.ContentTicks));
        }
    }
}
