using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using nMIDI.Controls.Models;
using nMIDI.Controls.Primitives;

namespace nMIDI.Controls;

/// <summary>What the lanes of a <see cref="MidiActivityMonitor"/> stand for.</summary>
public enum MidiActivitySource
{
    /// <summary>One lane per MIDI channel (labels "1".."16"); raw messages map channel → lane automatically.</summary>
    Channels,
    /// <summary>One lane per track; the app passes the track index as the lane.</summary>
    Tracks,
}

/// <summary>How a <see cref="MidiActivityMonitor"/> arranges its lanes.</summary>
public enum MidiActivityLayout
{
    /// <summary>One row per lane: label, note LED, control LED, live note map, polyphony, last control message.</summary>
    Rows,
    /// <summary>One line of LED cells (note LED on top, control LED below) – fits a toolbar or a track header.</summary>
    Compact,
}

/// <summary>Data for <see cref="MidiActivityMonitor.LaneClicked"/>.</summary>
public class MidiLaneEventArgs : RoutedEventArgs
{
    /// <summary>Creates a <see cref="MidiLaneEventArgs"/>.</summary>
    public MidiLaneEventArgs(RoutedEvent e, object source, int lane) : base(e, source) => Lane = lane;

    /// <summary>The lane (channel or track index, from 0).</summary>
    public int Lane { get; }
}

/// <summary>Handles <see cref="MidiActivityMonitor.LaneClicked"/>.</summary>
public delegate void MidiLaneEventHandler(object sender, MidiLaneEventArgs e);

/// <summary>
/// Shows MIDI activity per channel or per track: a note LED that flashes with velocity and stays dimly lit while
/// notes are held, a separate control LED for CC / pitch bend / program change / aftertouch, a live note map
/// (which keys are down, fading after release), polyphony and the last control message.
/// <para>
/// <b>Thread-safe feeding:</b> <see cref="NoteOn"/>, <see cref="ControlChange"/>, <see cref="ProcessMessage"/> etc. can be
/// called from the MIDI input or playback thread. Events go into a lock-free queue and are applied once per frame,
/// so a busy MIDI stream never floods the Dispatcher.
/// </para>
/// A single lane in <see cref="MidiActivityLayout.Compact"/> layout is a one-LED activity light for a track header.
/// </summary>
public class MidiActivityMonitor : DawRenderControl
{
    private const int MaxQueued = 8192;

    private enum Kind : byte { NoteOn, NoteOff, Control, PitchBend, Program, ChannelPressure, PolyPressure, AllOff }

    private readonly record struct Ev(Kind Kind, byte Lane, byte A, short B);

    private sealed class LaneState
    {
        public readonly byte[] Velocity = new byte[128];   // > 0 while held
        public readonly float[] Fade = new float[128];     // 1 → 0 after release
        public int Held;
        public int Fading;
        public double NoteGlow;
        public double ControlGlow;
        public string? LastControl;
        public int LastVelocity;

        public void Clear()
        {
            Array.Clear(Velocity);
            Array.Clear(Fade);
            Held = Fading = 0;
            NoteGlow = ControlGlow = 0;
            LastControl = null;
        }
    }

    private readonly ConcurrentQueue<Ev> _queue = new();
    private int _queued;
    private LaneState[] _lanes = Array.Empty<LaneState>();
    private bool _ticking;
    private TimeSpan _lastTick;
    private int _hoverLane = -1;
    private volatile bool _accepting = true; // false while hidden: posts are ignored (no CPU spent)
    private TimeSpan _lastTip;

    static MidiActivityMonitor()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(MidiActivityMonitor), new FrameworkPropertyMetadata(typeof(MidiActivityMonitor)));
        FocusableProperty.OverrideMetadata(typeof(MidiActivityMonitor), new FrameworkPropertyMetadata(false));
    }

    /// <summary>Creates a <see cref="MidiActivityMonitor"/>.</summary>
    public MidiActivityMonitor()
    {
        EnsureLanes();
        Loaded += (_, _) => UpdateTicking();
        Unloaded += (_, _) => StopTicking();
        IsVisibleChanged += (_, _) => UpdateTicking();
    }

    #region Properties

    /// <summary>Identifies the <see cref="Source"/> dependency property.</summary>
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(MidiActivitySource), typeof(MidiActivityMonitor),
        new FrameworkPropertyMetadata(MidiActivitySource.Channels, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Whether lanes are MIDI channels or tracks.</summary>
    public MidiActivitySource Source { get => (MidiActivitySource)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }

    /// <summary>Identifies the <see cref="Layout"/> dependency property.</summary>
    public static readonly DependencyProperty LayoutProperty = DependencyProperty.Register(
        nameof(Layout), typeof(MidiActivityLayout), typeof(MidiActivityMonitor),
        new FrameworkPropertyMetadata(MidiActivityLayout.Rows, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>List of lanes, or a compact grid of LEDs.</summary>
    public MidiActivityLayout Layout { get => (MidiActivityLayout)GetValue(LayoutProperty); set => SetValue(LayoutProperty, value); }

    /// <summary>Identifies the <see cref="LaneCount"/> dependency property.</summary>
    public static readonly DependencyProperty LaneCountProperty = DependencyProperty.Register(
        nameof(LaneCount), typeof(int), typeof(MidiActivityMonitor),
        new FrameworkPropertyMetadata(16, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, OnLaneCountChanged),
        v => v is int i && i is >= 1 and <= 128);

    /// <summary>Number of lanes (16 for channels; number of tracks otherwise).</summary>
    public int LaneCount { get => (int)GetValue(LaneCountProperty); set => SetValue(LaneCountProperty, value); }

    private static void OnLaneCountChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((MidiActivityMonitor)d).EnsureLanes();

    /// <summary>Identifies the <see cref="LaneLabels"/> dependency property.</summary>
    public static readonly DependencyProperty LaneLabelsProperty = DependencyProperty.Register(
        nameof(LaneLabels), typeof(IList<string>), typeof(MidiActivityMonitor),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Lane names (track names). Null = "1".."16" for channels, "T1".. for tracks.</summary>
    public IList<string>? LaneLabels { get => (IList<string>?)GetValue(LaneLabelsProperty); set => SetValue(LaneLabelsProperty, value); }

    /// <summary>Identifies the <see cref="LaneBrushes"/> dependency property.</summary>
    public static readonly DependencyProperty LaneBrushesProperty = DependencyProperty.Register(
        nameof(LaneBrushes), typeof(IList<Brush>), typeof(MidiActivityMonitor),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Optional per-lane colours (track colours). Used for the colour chip, note LED and note map.</summary>
    public IList<Brush>? LaneBrushes { get => (IList<Brush>?)GetValue(LaneBrushesProperty); set => SetValue(LaneBrushesProperty, value); }

    /// <summary>Identifies the <see cref="RowHeight"/> dependency property.</summary>
    public static readonly DependencyProperty RowHeightProperty = DependencyProperty.Register(
        nameof(RowHeight), typeof(double), typeof(MidiActivityMonitor),
        new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender),
        v => v is double d && d >= 8 && d <= 60);

    /// <summary>Height of one lane in pixels (list layout).</summary>
    public double RowHeight { get => (double)GetValue(RowHeightProperty); set => SetValue(RowHeightProperty, value); }

    /// <summary>Identifies the <see cref="ShowHeader"/> dependency property.</summary>
    public static readonly DependencyProperty ShowHeaderProperty = DependencyProperty.Register(
        nameof(ShowHeader), typeof(bool), typeof(MidiActivityMonitor),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Whether the column header row is shown.</summary>
    public bool ShowHeader { get => (bool)GetValue(ShowHeaderProperty); set => SetValue(ShowHeaderProperty, value); }

    /// <summary>Identifies the <see cref="ShowNoteMap"/> dependency property.</summary>
    public static readonly DependencyProperty ShowNoteMapProperty = DependencyProperty.Register(
        nameof(ShowNoteMap), typeof(bool), typeof(MidiActivityMonitor),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Whether each lane shows a strip with its held notes.</summary>
    public bool ShowNoteMap { get => (bool)GetValue(ShowNoteMapProperty); set => SetValue(ShowNoteMapProperty, value); }

    /// <summary>Identifies the <see cref="ShowControlText"/> dependency property.</summary>
    public static readonly DependencyProperty ShowControlTextProperty = DependencyProperty.Register(
        nameof(ShowControlText), typeof(bool), typeof(MidiActivityMonitor),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Show the last control message ("CC 7 100", "PB +2048", "PC 12").</summary>
    public bool ShowControlText { get => (bool)GetValue(ShowControlTextProperty); set => SetValue(ShowControlTextProperty, value); }

    /// <summary>Identifies the <see cref="LowestNote"/> dependency property.</summary>
    public static readonly DependencyProperty LowestNoteProperty = DependencyProperty.Register(
        nameof(LowestNote), typeof(int), typeof(MidiActivityMonitor),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender), v => v is int i && i is >= 0 and <= 127);

    /// <summary>Range shown in the note map (default the full 0..127, so nothing is ever hidden).</summary>
    public int LowestNote { get => (int)GetValue(LowestNoteProperty); set => SetValue(LowestNoteProperty, value); }

    /// <summary>Identifies the <see cref="HighestNote"/> dependency property.</summary>
    public static readonly DependencyProperty HighestNoteProperty = DependencyProperty.Register(
        nameof(HighestNote), typeof(int), typeof(MidiActivityMonitor),
        new FrameworkPropertyMetadata(127, FrameworkPropertyMetadataOptions.AffectsRender), v => v is int i && i is >= 0 and <= 127);

    /// <summary>Highest note shown in the note map.</summary>
    public int HighestNote { get => (int)GetValue(HighestNoteProperty); set => SetValue(HighestNoteProperty, value); }

    /// <summary>Identifies the <see cref="NoteDecay"/> dependency property.</summary>
    public static readonly DependencyProperty NoteDecayProperty = DependencyProperty.Register(
        nameof(NoteDecay), typeof(double), typeof(MidiActivityMonitor), new FrameworkPropertyMetadata(0.3),
        v => v is double d && d > 0.01 && d < 10);

    /// <summary>Seconds for a note flash / released note to fade out.</summary>
    public double NoteDecay { get => (double)GetValue(NoteDecayProperty); set => SetValue(NoteDecayProperty, value); }

    /// <summary>Identifies the <see cref="NoteBrush"/> dependency property.</summary>
    public static readonly DependencyProperty NoteBrushProperty = RegisterBrush(nameof(NoteBrush), typeof(MidiActivityMonitor));
    /// <summary>Brush for the notes.</summary>
    public Brush? NoteBrush { get => (Brush?)GetValue(NoteBrushProperty); set => SetValue(NoteBrushProperty, value); }

    /// <summary>Identifies the <see cref="ControlBrush"/> dependency property.</summary>
    public static readonly DependencyProperty ControlBrushProperty = RegisterBrush(nameof(ControlBrush), typeof(MidiActivityMonitor));
    /// <summary>Brush for the controller LED and text.</summary>
    public Brush? ControlBrush { get => (Brush?)GetValue(ControlBrushProperty); set => SetValue(ControlBrushProperty, value); }

    /// <summary>Identifies the <see cref="LedOffBrush"/> dependency property.</summary>
    public static readonly DependencyProperty LedOffBrushProperty = RegisterBrush(nameof(LedOffBrush), typeof(MidiActivityMonitor));
    /// <summary>Brush for the LEDs while off.</summary>
    public Brush? LedOffBrush { get => (Brush?)GetValue(LedOffBrushProperty); set => SetValue(LedOffBrushProperty, value); }

    /// <summary>Identifies the <see cref="AlternateRowBrush"/> dependency property.</summary>
    public static readonly DependencyProperty AlternateRowBrushProperty = RegisterBrush(nameof(AlternateRowBrush), typeof(MidiActivityMonitor));
    /// <summary>Brush for the every other lane.</summary>
    public Brush? AlternateRowBrush { get => (Brush?)GetValue(AlternateRowBrushProperty); set => SetValue(AlternateRowBrushProperty, value); }

    /// <summary>Identifies the <see cref="TickBrush"/> dependency property.</summary>
    public static readonly DependencyProperty TickBrushProperty = RegisterBrush(nameof(TickBrush), typeof(MidiActivityMonitor));
    /// <summary>Octave marks in the note map.</summary>
    public Brush? TickBrush { get => (Brush?)GetValue(TickBrushProperty); set => SetValue(TickBrushProperty, value); }

    /// <summary>Identifies the <see cref="LaneClicked"/> routed event.</summary>
    public static readonly RoutedEvent LaneClickedEvent = EventManager.RegisterRoutedEvent(
        nameof(LaneClicked), RoutingStrategy.Bubble, typeof(MidiLaneEventHandler), typeof(MidiActivityMonitor));

    /// <summary>A lane was clicked (select the track / channel in your app).</summary>
    public event MidiLaneEventHandler LaneClicked { add => AddHandler(LaneClickedEvent, value); remove => RemoveHandler(LaneClickedEvent, value); }

    #endregion

    #region Thread-safe feeding API

    /// <summary>Note on (velocity 0 is treated as note off, as in MIDI). Any thread.</summary>
    public void NoteOn(int lane, int note, int velocity)
        => Post(new Ev(velocity <= 0 ? Kind.NoteOff : Kind.NoteOn, (byte)lane, (byte)Math.Clamp(note, 0, 127), (short)Math.Clamp(velocity, 0, 127)), lane, critical: velocity <= 0);

    /// <summary>Note off. Any thread. Never dropped, so notes can't get stuck on screen.</summary>
    public void NoteOff(int lane, int note)
        => Post(new Ev(Kind.NoteOff, (byte)lane, (byte)Math.Clamp(note, 0, 127), 0), lane, critical: true);

    /// <summary>Control change. CC 120 / 123 also clear held notes. Any thread.</summary>
    public void ControlChange(int lane, int controller, int value)
        => Post(new Ev(Kind.Control, (byte)lane, (byte)Math.Clamp(controller, 0, 127), (short)Math.Clamp(value, 0, 127)), lane,
            critical: controller is 120 or 123);

    /// <summary>Pitch bend, -8192..8191. Any thread.</summary>
    public void PitchBend(int lane, int value)
        => Post(new Ev(Kind.PitchBend, (byte)lane, 0, (short)Math.Clamp(value, -8192, 8191)), lane, critical: false);

    /// <summary>Program change. Any thread.</summary>
    public void ProgramChange(int lane, int program)
        => Post(new Ev(Kind.Program, (byte)lane, (byte)Math.Clamp(program, 0, 127), 0), lane, critical: false);

    /// <summary>Channel pressure (aftertouch). Any thread.</summary>
    public void ChannelPressure(int lane, int pressure)
        => Post(new Ev(Kind.ChannelPressure, (byte)lane, 0, (short)Math.Clamp(pressure, 0, 127)), lane, critical: false);

    /// <summary>Polyphonic aftertouch for one key. Any thread.</summary>
    public void PolyPressure(int lane, int note, int pressure)
        => Post(new Ev(Kind.PolyPressure, (byte)lane, (byte)Math.Clamp(note, 0, 127), (short)Math.Clamp(pressure, 0, 127)), lane, critical: false);

    /// <summary>Clears held notes on one lane, or all lanes when <paramref name="lane"/> is -1. Any thread.</summary>
    public void AllNotesOff(int lane = -1)
    {
        if (!_accepting || lane > 127) return;
        Interlocked.Increment(ref _queued);
        _queue.Enqueue(new Ev(Kind.AllOff, lane < 0 ? (byte)255 : (byte)lane, 0, 0));
    }

    /// <summary>
    /// Parses a raw channel message. Lane = MIDI channel (0-based) unless <paramref name="lane"/> is given
    /// (e.g. the track index for <see cref="MidiActivitySource.Tracks"/>). System messages are ignored. Any thread.
    /// </summary>
    public void ProcessMessage(int status, int data1, int data2, int lane = -1)
    {
        if (status < 0x80 || status >= 0xF0) return;
        int ln = lane >= 0 ? lane : status & 0x0F;
        switch (status & 0xF0)
        {
            case 0x80: NoteOff(ln, data1); break;
            case 0x90: NoteOn(ln, data1, data2); break;
            case 0xA0: PolyPressure(ln, data1, data2); break;
            case 0xB0: ControlChange(ln, data1, data2); break;
            case 0xC0: ProgramChange(ln, data1); break;
            case 0xD0: ChannelPressure(ln, data1); break;
            case 0xE0: PitchBend(ln, ((data2 & 0x7F) << 7 | (data1 & 0x7F)) - 8192); break;
        }
    }

    /// <summary>WinMM / NAudio packed short message: status | data1 &lt;&lt; 8 | data2 &lt;&lt; 16. Any thread.</summary>
    public void ProcessShortMessage(int message, int lane = -1)
        => ProcessMessage(message & 0xFF, (message >> 8) & 0x7F, (message >> 16) & 0x7F, lane);

    private void Post(Ev ev, int lane, bool critical)
    {
        if (!_accepting || lane is < 0 or > 127) return;
        // Under a flood (e.g. dense CC from a controller), drop non-critical events rather than grow without bound.
        if (!critical && Volatile.Read(ref _queued) > MaxQueued) return;
        Interlocked.Increment(ref _queued);
        _queue.Enqueue(ev);
    }

    #endregion

    #region Per-frame processing (UI thread)

    private void EnsureLanes()
    {
        int n = LaneCount;
        if (_lanes.Length == n) return;
        var lanes = new LaneState[n];
        for (int i = 0; i < n; i++) lanes[i] = i < _lanes.Length ? _lanes[i] : new LaneState();
        _lanes = lanes;
    }

    /// <summary>
    /// While hidden or unloaded the monitor ignores incoming events entirely and clears its state, so a collapsed
    /// panel costs nothing. When shown again it starts from a clean slate (call <see cref="NoteOn"/> for notes that
    /// are still held if you need them back, e.g. from your engine's held-note snapshot).
    /// </summary>
    private void UpdateTicking()
    {
        bool active = IsLoaded && IsVisible;
        // Events posted while hidden (racing with the hide) would light notes whose note-off never came.
        if (active && !_accepting)
            while (_queue.TryDequeue(out _)) Interlocked.Decrement(ref _queued);
        _accepting = active;
        if (active) StartTicking();
        else
        {
            StopTicking();
            Reset();
        }
    }

    /// <summary>True while the monitor is visible and accepting events. Thread-safe; feeders can check it to skip work.</summary>
    public bool IsAccepting => _accepting;

    private void StartTicking()
    {
        if (_ticking) return;
        _ticking = true;
        _lastTick = TimeSpan.Zero;
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopTicking()
    {
        if (!_ticking) return;
        _ticking = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        TimeSpan now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _lastTick) return;
        double dt = _lastTick == TimeSpan.Zero ? 1.0 / 60 : Math.Min((now - _lastTick).TotalSeconds, 0.25);
        _lastTick = now;

        bool changed = Drain();
        changed |= Decay(dt);
        if (changed)
        {
            InvalidateVisual();
            // Refresh the hover text at most 4×/s so it stays readable on busy lanes.
            if (_hoverLane >= 0 && now - _lastTip > TimeSpan.FromMilliseconds(250))
            {
                _lastTip = now;
                UpdateToolTip();
            }
        }
    }

    private bool Drain()
    {
        bool any = false;
        while (_queue.TryDequeue(out Ev ev))
        {
            Interlocked.Decrement(ref _queued);
            any = true;
            if (ev.Kind == Kind.AllOff && ev.Lane == 255)
            {
                foreach (LaneState l in _lanes) ReleaseAll(l);
                continue;
            }
            if (ev.Lane >= _lanes.Length) continue;
            Apply(_lanes[ev.Lane], ev);
        }
        return any;
    }

    private static void Apply(LaneState l, Ev ev)
    {
        switch (ev.Kind)
        {
            case Kind.NoteOn:
                if (l.Velocity[ev.A] == 0) l.Held++;
                if (l.Fade[ev.A] > 0) { l.Fade[ev.A] = 0; l.Fading--; }
                l.Velocity[ev.A] = (byte)ev.B;
                l.NoteGlow = Math.Max(l.NoteGlow, ev.B / 127.0);
                l.LastVelocity = ev.B;
                break;
            case Kind.NoteOff:
                Release(l, ev.A);
                break;
            case Kind.Control:
                l.ControlGlow = 1;
                l.LastControl = $"CC {ev.A} {ev.B}";
                if (ev.A is 120 or 123) ReleaseAll(l);
                break;
            case Kind.PitchBend:
                l.ControlGlow = 1;
                l.LastControl = "PB " + ev.B.ToString("+0;-0;0", CultureInfo.InvariantCulture);
                break;
            case Kind.Program:
                l.ControlGlow = 1;
                l.LastControl = "PC " + (ev.A + 1).ToString(CultureInfo.InvariantCulture);
                break;
            case Kind.ChannelPressure:
                l.ControlGlow = 1;
                l.LastControl = "AT " + ev.B.ToString(CultureInfo.InvariantCulture);
                break;
            case Kind.PolyPressure:
                l.ControlGlow = 1;
                l.LastControl = $"PAT {MidiNoteName.Get(ev.A)} {ev.B}";
                break;
            case Kind.AllOff:
                ReleaseAll(l);
                break;
        }
    }

    private static void Release(LaneState l, int note)
    {
        if (l.Velocity[note] == 0) return;
        l.Velocity[note] = 0;
        l.Held--;
        if (l.Fade[note] <= 0) l.Fading++;
        l.Fade[note] = 1;
    }

    private static void ReleaseAll(LaneState l)
    {
        for (int n = 0; n < 128; n++) Release(l, n);
    }

    private bool Decay(double dt)
    {
        double k = dt / NoteDecay;
        double kc = dt / 0.35;
        bool changed = false;
        foreach (LaneState l in _lanes)
        {
            if (l.NoteGlow > 0) { l.NoteGlow = Math.Max(0, l.NoteGlow - k); changed = true; }
            if (l.ControlGlow > 0) { l.ControlGlow = Math.Max(0, l.ControlGlow - kc); changed = true; }
            if (l.Fading > 0)
            {
                changed = true;
                for (int n = 0; n < 128; n++)
                {
                    if (l.Fade[n] <= 0) continue;
                    l.Fade[n] -= (float)k;
                    if (l.Fade[n] <= 0) { l.Fade[n] = 0; l.Fading--; }
                }
            }
        }
        return changed;
    }

    /// <summary>Clears all activity immediately (UI thread). Use when the MIDI port changes.</summary>
    public void Reset()
    {
        while (_queue.TryDequeue(out _)) Interlocked.Decrement(ref _queued);
        foreach (LaneState l in _lanes) l.Clear();
        InvalidateVisual();
    }

    /// <summary>Number of notes currently held on a lane (UI thread).</summary>
    public int GetHeldCount(int lane) => lane >= 0 && lane < _lanes.Length ? _lanes[lane].Held : 0;

    #endregion

    #region Layout

    private const double LabelWidth = 44;
    private const double PolyWidth = 22;
    private const double ControlTextWidth = 72;

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size constraint)
    {
        if (Layout == MidiActivityLayout.Compact)
            return new Size(Math.Min(constraint.Width, LaneCount * 14), Math.Min(constraint.Height, 22));
        // Ask for a natural width, never "all of it": in a DockPanel/StackPanel the constraint is the remaining
        // space, and claiming it starves the siblings. HorizontalAlignment=Stretch still fills the slot on arrange.
        double h = (ShowHeader ? RowHeight : 0) + LaneCount * RowHeight;
        return new Size(Math.Min(constraint.Width, 300), Math.Min(h, constraint.Height));
    }

    private string LaneLabel(int lane)
    {
        if (LaneLabels is { } labels && lane < labels.Count && labels[lane] is { } s) return s;
        return Source == MidiActivitySource.Channels
            ? (lane + 1).ToString(CultureInfo.InvariantCulture)
            : "T" + (lane + 1).ToString(CultureInfo.InvariantCulture);
    }

    private Brush? LaneBrush(int lane)
        => LaneBrushes is { } b && lane < b.Count && b[lane] is { } br ? br : NoteBrush;

    private int HitTestLane(Point p)
    {
        if (Layout == MidiActivityLayout.Compact)
        {
            double cw = ActualWidth / LaneCount;
            return cw <= 0 ? -1 : Math.Clamp((int)(p.X / cw), 0, LaneCount - 1);
        }
        double y = p.Y - (ShowHeader ? RowHeight : 0);
        if (y < 0) return -1;
        int lane = (int)(y / RowHeight);
        return lane < LaneCount ? lane : -1;
    }

    #endregion

    #region Rendering

    /// <inheritdoc/>
    protected override void OnRender(DrawingContext dc)
    {
        var snap = new PixelSnapper(this);
        if (Background is { } bg) dc.DrawRectangle(bg, null, new Rect(0, 0, ActualWidth, ActualHeight));
        // LEDs and held-note bars go into one nine-patch batch (a single native call per frame); text and
        // thin lines stay vector.
        _batch.Begin(this, RenderSize);
        if (Layout == MidiActivityLayout.Compact) RenderCompact(dc, snap);
        else RenderRows(dc, snap);
    }

    private readonly NineBatch _batch = new();

    private void Led(DrawingContext dc, Rect r, Brush? lit, double level, double radius)
    {
        int rad = (int)Math.Round(radius);
        if (BrushColors.TryGetSolid(LedOffBrush, out Color off) && LedOffBrush is not null)
            _batch.Add(new FrameSpec(off, off, Color.FromArgb(0x80, 0, 0, 0), rad, 1, FrameStyle.Sunken), r);
        if (lit is null || level <= 0.01 || !BrushColors.TryGetSolid(lit, out Color c)) return;
        _batch.Add(new FrameSpec(Level(c, level), Level(c, level), Color.FromArgb(0x60, 0, 0, 0), rad, 1, FrameStyle.Glow), r);
    }

    /// <summary>The colour at a brightness level, in 16 steps so a handful of skins cover every flash.</summary>
    private static Color Level(Color c, double level)
    {
        double q = Math.Ceiling(Math.Clamp(level, 0, 1) * 16) / 16;
        return Color.FromArgb((byte)(c.A * q), c.R, c.G, c.B);
    }

    /// <summary>Note LED level: flash with velocity, never below a dim "held" glow while notes are down.</summary>
    private static double NoteLevel(LaneState l)
        => Math.Max(l.NoteGlow, l.Held > 0 ? 0.35 + 0.35 * l.LastVelocity / 127.0 : 0);

    private void RenderRows(DrawingContext dc, PixelSnapper snap)
    {
        double rh = RowHeight, w = ActualWidth;
        double y = 0;
        double led = Math.Max(4, rh - 5);
        double mapX = 3 + LabelWidth + 2 * (led + 4) + 4;
        double rightCols = PolyWidth + (ShowControlText ? ControlTextWidth : 0);
        double mapW = Math.Max(0, w - mapX - rightCols - 4);
        double small = Math.Max(8, Math.Min(FontSize, rh - 3));

        if (ShowHeader)
        {
            Brush dim = Foreground ?? Brushes.Gray;
            dc.PushOpacity(0.7);
            dc.DrawText(CachedText(Source == MidiActivitySource.Channels ? "CH" : "TRACK", snap.PixelsPerDip, dim, small - 1), new Point(6, y + 1));
            dc.DrawText(CachedText("N", snap.PixelsPerDip, dim, small - 1), new Point(3 + LabelWidth + 1, y + 1));
            dc.DrawText(CachedText("C", snap.PixelsPerDip, dim, small - 1), new Point(3 + LabelWidth + led + 5, y + 1));
            if (ShowNoteMap && mapW > 30)
            {
                dc.DrawText(CachedText(MidiNoteName.Get(LowestNote), snap.PixelsPerDip, dim, small - 1), new Point(mapX, y + 1));
                FormattedText hi = CachedText(MidiNoteName.Get(HighestNote), snap.PixelsPerDip, dim, small - 1);
                dc.DrawText(hi, new Point(mapX + mapW - hi.Width, y + 1));
            }
            FormattedText poly = CachedText("POLY", snap.PixelsPerDip, dim, small - 2);
            dc.DrawText(poly, new Point(mapX + mapW + 4 + PolyWidth - poly.Width - 2, y + 1));
            if (ShowControlText) dc.DrawText(CachedText("LAST CTRL", snap.PixelsPerDip, dim, small - 2), new Point(w - ControlTextWidth + 4, y + 1));
            dc.Pop();
            y += rh;
        }

        int low = Math.Min(LowestNote, HighestNote), high = Math.Max(LowestNote, HighestNote);
        int span = high - low + 1;
        double noteW = span > 0 ? mapW / span : 0;
        Pen? tick = FrozenPens.Create(TickBrush, snap.OnePixel);

        for (int lane = 0; lane < _lanes.Length; lane++, y += rh)
        {
            if (y > ActualHeight) break;
            LaneState l = _lanes[lane];
            if (lane % 2 == 1 && AlternateRowBrush is { } alt) dc.DrawRectangle(alt, null, new Rect(0, y, w, rh));
            if (lane == _hoverLane) { dc.PushOpacity(0.06); dc.DrawRectangle(Brushes.White, null, new Rect(0, y, w, rh)); dc.Pop(); }

            Brush? laneBrush = LaneBrush(lane);
            if (LaneBrushes is not null && laneBrush is not null)
                dc.DrawRectangle(laneBrush, null, snap.Rect(0, y + 1, 3, rh - 2));

            FormattedText label = CachedText(LaneLabel(lane), snap.PixelsPerDip, size: small, maxWidth: LabelWidth - 4);
            dc.DrawText(label, new Point(6, y + (rh - label.Height) / 2));

            double ly = y + (rh - led) / 2;
            Led(dc, snap.Rect(3 + LabelWidth, ly, led, led), laneBrush, NoteLevel(l), 2);
            Led(dc, snap.Rect(3 + LabelWidth + led + 4, ly, led, led), ControlBrush, l.ControlGlow, 2);

            // Note map: dim track, octave marks, held notes (velocity brightness), released notes fading.
            if (ShowNoteMap && mapW > 10)
            {
                var track = new Rect(mapX, y + 2, mapW, rh - 4);
                if (LedOffBrush is { } off) dc.DrawRectangle(off, null, track);
                if (tick is not null)
                {
                    for (int n = low; n <= high; n++)
                    {
                        if (n % 12 != 0) continue;
                        double x = snap.LineX(mapX + (n - low) * noteW);
                        dc.PushOpacity(0.5);
                        dc.DrawLine(tick, new Point(x, track.Top), new Point(x, track.Bottom));
                        dc.Pop();
                    }
                }
                if ((l.Held > 0 || l.Fading > 0) && BrushColors.TryGetSolid(laneBrush, out Color laneColor))
                {
                    double bw = Math.Max(1.5, noteW);
                    for (int n = low; n <= high; n++)
                    {
                        double level = l.Velocity[n] > 0 ? 0.35 + 0.65 * l.Velocity[n] / 127.0 : l.Fade[n] * 0.6;
                        if (level <= 0.01) continue;
                        var c = Level(laneColor, level);
                        _batch.Add(FrameSpec.Solid(c, Colors.Transparent, 0, 0), new Rect(mapX + (n - low) * noteW, track.Top, bw, track.Height));
                    }
                }
            }

            if (l.Held > 0)
            {
                FormattedText pt = CachedText(l.Held.ToString(CultureInfo.InvariantCulture), snap.PixelsPerDip, size: small);
                dc.DrawText(pt, new Point(mapX + mapW + 4 + PolyWidth - pt.Width - 2, y + (rh - pt.Height) / 2));
            }

            if (ShowControlText && l.LastControl is { } ctrl)
            {
                FormattedText ct = CachedText(ctrl, snap.PixelsPerDip, ControlBrush, small, maxWidth: ControlTextWidth - 6);
                dc.PushOpacity(0.45 + 0.55 * l.ControlGlow);
                dc.DrawText(ct, new Point(w - ControlTextWidth + 4, y + (rh - ct.Height) / 2));
                dc.Pop();
            }
        }
        _batch.Render(dc);
    }

    private void RenderCompact(DrawingContext dc, PixelSnapper snap)
    {
        int n = _lanes.Length;
        double cw = ActualWidth / n, h = ActualHeight;
        if (cw <= 2 || h <= 4) return;
        double gap = Math.Min(2, cw * 0.15);
        double noteH = Math.Round(h * 0.62), ctrlH = Math.Max(2, h - noteH - 2);

        for (int lane = 0; lane < n; lane++)
        {
            LaneState l = _lanes[lane];
            double x = lane * cw + gap / 2, w = cw - gap;
            Led(dc, snap.Rect(x, 0, w, noteH), LaneBrush(lane), NoteLevel(l), 2);
            Led(dc, snap.Rect(x, noteH + 2, w, ctrlH), ControlBrush, l.ControlGlow, 1);
        }
        _batch.Render(dc);

        // Labels on top of the LEDs.
        for (int lane = 0; lane < n; lane++)
        {
            LaneState l = _lanes[lane];
            double x = lane * cw + gap / 2, w = cw - gap;

            if (w >= 8 && noteH >= 8 && n > 1)
            {
                string text = Source == MidiActivitySource.Channels ? (lane + 1).ToString(CultureInfo.InvariantCulture) : LaneLabel(lane);
                // Shrink to fit the cell (two-digit channels in narrow cells); skip the label if it can't fit legibly.
                double size = Math.Min(9, noteH - 2);
                FormattedText ft = CachedText(text, snap.PixelsPerDip, size: size);
                if (ft.Width > w - 1)
                {
                    size = Math.Max(6, size * (w - 1) / ft.Width);
                    ft = CachedText(text, snap.PixelsPerDip, size: size);
                }
                if (ft.Width <= w)
                {
                    dc.PushOpacity(NoteLevel(l) > 0.5 ? 0.9 : 0.6);
                    dc.DrawText(ft, new Point(x + (w - ft.Width) / 2, (noteH - ft.Height) / 2));
                    dc.Pop();
                }
            }
        }
    }

    #endregion

    #region Mouse

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int lane = HitTestLane(e.GetPosition(this));
        if (lane == _hoverLane) return;
        _hoverLane = lane;
        UpdateToolTip();
        InvalidateVisual();
    }

    /// <inheritdoc/>
    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverLane = -1;
        InvalidateVisual();
    }

    /// <inheritdoc/>
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        int lane = HitTestLane(e.GetPosition(this));
        if (lane >= 0) RaiseEvent(new MidiLaneEventArgs(LaneClickedEvent, this, lane));
    }

    private void UpdateToolTip()
    {
        if (_hoverLane < 0 || _hoverLane >= _lanes.Length) { SetAutoToolTip(null); return; }
        LaneState l = _lanes[_hoverLane];
        var sb = new StringBuilder();
        sb.Append(Source == MidiActivitySource.Channels ? "Channel " : "").Append(LaneLabel(_hoverLane));
        if (l.Held == 0) sb.Append(" · no notes");
        else
        {
            sb.Append(" · ").Append(l.Held).Append(l.Held == 1 ? " note: " : " notes: ");
            int shown = 0;
            for (int n = 0; n < 128 && shown < 8; n++)
            {
                if (l.Velocity[n] == 0) continue;
                if (shown++ > 0) sb.Append(' ');
                sb.Append(MidiNoteName.Get(n));
            }
            if (l.Held > 8) sb.Append(" …");
        }
        if (l.LastControl is { } c) sb.Append(" · last ").Append(c);
        SetAutoToolTip(sb.ToString());
    }

    #endregion
}
