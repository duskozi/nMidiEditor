using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using nMIDI.Controls.Models;
using nMIDI.Controls.Primitives;

namespace nMIDI.Controls;

/// <summary>
/// MIDI note editor with built-in keyboard gutter and bar ruler. Virtualized: only the visible part is drawn,
/// so it handles long songs and all 128 notes. Put it directly in a <see cref="ScrollViewer"/> with
/// <c>CanContentScroll="True"</c> to get scrollbars (it implements <see cref="IScrollInfo"/>).
/// <para><b>Mouse</b>: click empty grid = new note (drag to place) · drag note = move · drag right edge = resize ·
/// Ctrl+click = toggle selection · Ctrl+drag empty = marquee · right-click = delete · Alt = bypass snap ·
/// click keyboard = audition · click/drag ruler = move playhead.</para>
/// <para><b>Wheel</b>: scroll · Shift = horizontal · Ctrl = zoom time · Alt = zoom pitch.</para>
/// <para><b>Keys</b>: Delete · Ctrl+A · ↑/↓ transpose (Shift = octave) · ←/→ move by snap · Ctrl+D duplicate.</para>
/// </summary>
public class PianoRoll : DawRenderControl, IScrollInfo
{
    private enum DragMode { None, Move, Resize, Marquee, Scrub, Audition }

    private const int NoteCount = 128;
    private const double EdgeGrip = 6;

    private IList<MidiNote>? _notes;
    private DragMode _mode;
    private Point _anchor;
    private MidiNote? _primary;
    private readonly Dictionary<MidiNote, (int pitch, double start, double length)> _original = new();
    private HashSet<MidiNote>? _marqueeBase;
    private Rect _marquee;
    private bool _edited;
    private int _auditionNote = -1;
    private double _lastLength = 1;
    private bool _initialScroll;
    private readonly Dictionary<int, FormattedText> _noteLabelCache = new();
    // The playhead moves every frame during playback; drawing it on its own visual means the grid and
    // notes are NOT re-rendered 60 times a second.
    private readonly DrawingVisual _playheadLayer = new();
    private double _labelCacheDip;

    static PianoRoll()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(PianoRoll), new FrameworkPropertyMetadata(typeof(PianoRoll)));
        FocusableProperty.OverrideMetadata(typeof(PianoRoll), new FrameworkPropertyMetadata(true));
        ClipToBoundsProperty.OverrideMetadata(typeof(PianoRoll), new FrameworkPropertyMetadata(true));
    }

    /// <summary>Creates a <see cref="PianoRoll"/>.</summary>
    public PianoRoll()
    {
        SetCurrentValue(NotesProperty, new ObservableCollection<MidiNote>());
        AddVisualChild(_playheadLayer);
        Loaded += (_, _) => Attach(Notes);
        Unloaded += (_, _) => Detach();
    }

    #region Properties

    /// <summary>Identifies the <see cref="Notes"/> dependency property.</summary>
    public static readonly DependencyProperty NotesProperty = DependencyProperty.Register(
        nameof(Notes), typeof(IList<MidiNote>), typeof(PianoRoll),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnNotesChanged));

    /// <summary>The notes being edited. Use an <see cref="ObservableCollection{T}"/> for live updates.</summary>
    public IList<MidiNote>? Notes { get => (IList<MidiNote>?)GetValue(NotesProperty); set => SetValue(NotesProperty, value); }

    /// <summary>Identifies the <see cref="PixelsPerBeat"/> dependency property.</summary>
    public static readonly DependencyProperty PixelsPerBeatProperty = DependencyProperty.Register(
        nameof(PixelsPerBeat), typeof(double), typeof(PianoRoll),
        new FrameworkPropertyMetadata(80.0, OnExtentChanged, (_, v) => Math.Clamp((double)v, 4, 4000)));

    /// <summary>Horizontal zoom.</summary>
    public double PixelsPerBeat { get => (double)GetValue(PixelsPerBeatProperty); set => SetValue(PixelsPerBeatProperty, value); }

    /// <summary>Identifies the <see cref="KeyHeight"/> dependency property.</summary>
    public static readonly DependencyProperty KeyHeightProperty = DependencyProperty.Register(
        nameof(KeyHeight), typeof(double), typeof(PianoRoll),
        new FrameworkPropertyMetadata(14.0, OnExtentChanged, (_, v) => Math.Clamp((double)v, 4, 60)));

    /// <summary>Vertical zoom (row height per semitone).</summary>
    public double KeyHeight { get => (double)GetValue(KeyHeightProperty); set => SetValue(KeyHeightProperty, value); }

    /// <summary>Identifies the <see cref="LengthInBeats"/> dependency property.</summary>
    public static readonly DependencyProperty LengthInBeatsProperty = DependencyProperty.Register(
        nameof(LengthInBeats), typeof(double), typeof(PianoRoll),
        new FrameworkPropertyMetadata(64.0, OnExtentChanged), v => v is double d && d > 0 && !double.IsInfinity(d));

    /// <summary>Length of the song in beats; the roll scrolls this far.</summary>
    public double LengthInBeats { get => (double)GetValue(LengthInBeatsProperty); set => SetValue(LengthInBeatsProperty, value); }

    /// <summary>Identifies the <see cref="BeatsPerBar"/> dependency property.</summary>
    public static readonly DependencyProperty BeatsPerBarProperty = DependencyProperty.Register(
        nameof(BeatsPerBar), typeof(int), typeof(PianoRoll),
        new FrameworkPropertyMetadata(4, FrameworkPropertyMetadataOptions.AffectsRender), v => v is int i && i > 0);

    /// <summary>Beats per bar, for bar lines and the ruler.</summary>
    public int BeatsPerBar { get => (int)GetValue(BeatsPerBarProperty); set => SetValue(BeatsPerBarProperty, value); }

    /// <summary>Identifies the <see cref="Snap"/> dependency property.</summary>
    public static readonly DependencyProperty SnapProperty = DependencyProperty.Register(
        nameof(Snap), typeof(double), typeof(PianoRoll),
        new FrameworkPropertyMetadata(0.25, FrameworkPropertyMetadataOptions.AffectsRender), v => v is double d && d >= 0 && double.IsFinite(d));

    /// <summary>Grid/snap size in beats (0.25 = 1/16 note). 0 disables snapping.</summary>
    public double Snap { get => (double)GetValue(SnapProperty); set => SetValue(SnapProperty, value); }

    /// <summary>Identifies the <see cref="PlayheadPosition"/> dependency property.</summary>
    public static readonly DependencyProperty PlayheadPositionProperty = DependencyProperty.Register(
        nameof(PlayheadPosition), typeof(double), typeof(PianoRoll),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPlayheadChanged));

    /// <summary>Playhead in beats. Two-way: ruler clicks set it.</summary>
    public double PlayheadPosition { get => (double)GetValue(PlayheadPositionProperty); set => SetValue(PlayheadPositionProperty, value); }

    /// <summary>Identifies the <see cref="FollowPlayhead"/> dependency property.</summary>
    public static readonly DependencyProperty FollowPlayheadProperty = DependencyProperty.Register(
        nameof(FollowPlayhead), typeof(bool), typeof(PianoRoll), new FrameworkPropertyMetadata(false));

    /// <summary>Page-scroll when the playhead leaves the view (enable during playback).</summary>
    public bool FollowPlayhead { get => (bool)GetValue(FollowPlayheadProperty); set => SetValue(FollowPlayheadProperty, value); }

    /// <summary>Identifies the <see cref="KeyboardWidth"/> dependency property.</summary>
    public static readonly DependencyProperty KeyboardWidthProperty = DependencyProperty.Register(
        nameof(KeyboardWidth), typeof(double), typeof(PianoRoll), new FrameworkPropertyMetadata(56.0, OnExtentChanged));

    /// <summary>Width of the keyboard on the left, in pixels.</summary>
    public double KeyboardWidth { get => (double)GetValue(KeyboardWidthProperty); set => SetValue(KeyboardWidthProperty, value); }

    /// <summary>Identifies the <see cref="RulerHeight"/> dependency property.</summary>
    public static readonly DependencyProperty RulerHeightProperty = DependencyProperty.Register(
        nameof(RulerHeight), typeof(double), typeof(PianoRoll), new FrameworkPropertyMetadata(22.0, OnExtentChanged));

    /// <summary>Height of the bar ruler at the top, in pixels.</summary>
    public double RulerHeight { get => (double)GetValue(RulerHeightProperty); set => SetValue(RulerHeightProperty, value); }

    /// <summary>Identifies the <see cref="MiddleCOctave"/> dependency property.</summary>
    public static readonly DependencyProperty MiddleCOctaveProperty = DependencyProperty.Register(
        nameof(MiddleCOctave), typeof(int), typeof(PianoRoll), new FrameworkPropertyMetadata(4, FrameworkPropertyMetadataOptions.AffectsRender, OnLabelLookChanged));

    private static void OnLabelLookChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((PianoRoll)d)._noteLabelCache.Clear();

    /// <inheritdoc/>
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        // Labels are cached as FormattedText: drop them when their colour or font changes.
        if (e.Property == NoteTextBrushProperty || e.Property == FontFamilyProperty || e.Property == FontSizeProperty
            || e.Property == FontWeightProperty || e.Property == FontStyleProperty || e.Property == FontStretchProperty)
            _noteLabelCache.Clear();
    }

    /// <summary>Octave number of middle C (MIDI note 60) in labels: 4 (C4, default) or 3 (C3).</summary>
    public int MiddleCOctave { get => (int)GetValue(MiddleCOctaveProperty); set => SetValue(MiddleCOctaveProperty, value); }

    /// <summary>Identifies the <see cref="DefaultVelocity"/> dependency property.</summary>
    public static readonly DependencyProperty DefaultVelocityProperty = DependencyProperty.Register(
        nameof(DefaultVelocity), typeof(int), typeof(PianoRoll), new FrameworkPropertyMetadata(100), v => v is int i && i is >= 1 and <= 127);

    /// <summary>Velocity of notes drawn with the mouse.</summary>
    public int DefaultVelocity { get => (int)GetValue(DefaultVelocityProperty); set => SetValue(DefaultVelocityProperty, value); }

    /// <summary>Identifies the <see cref="IsReadOnly"/> dependency property.</summary>
    public static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.Register(
        nameof(IsReadOnly), typeof(bool), typeof(PianoRoll), new FrameworkPropertyMetadata(false));

    /// <summary>Whether notes can be drawn, moved and deleted.</summary>
    public bool IsReadOnly { get => (bool)GetValue(IsReadOnlyProperty); set => SetValue(IsReadOnlyProperty, value); }

    /// <summary>Identifies the <see cref="BlackRowBrush"/> dependency property.</summary>
    public static readonly DependencyProperty BlackRowBrushProperty = RegisterBrush(nameof(BlackRowBrush), typeof(PianoRoll));
    /// <summary>Brush for the rows of black keys.</summary>
    public Brush? BlackRowBrush { get => (Brush?)GetValue(BlackRowBrushProperty); set => SetValue(BlackRowBrushProperty, value); }

    /// <summary>Identifies the <see cref="BarLineBrush"/> dependency property.</summary>
    public static readonly DependencyProperty BarLineBrushProperty = RegisterBrush(nameof(BarLineBrush), typeof(PianoRoll));
    /// <summary>Brush for the bar lines.</summary>
    public Brush? BarLineBrush { get => (Brush?)GetValue(BarLineBrushProperty); set => SetValue(BarLineBrushProperty, value); }

    /// <summary>Identifies the <see cref="BeatLineBrush"/> dependency property.</summary>
    public static readonly DependencyProperty BeatLineBrushProperty = RegisterBrush(nameof(BeatLineBrush), typeof(PianoRoll));
    /// <summary>Brush for the beat lines.</summary>
    public Brush? BeatLineBrush { get => (Brush?)GetValue(BeatLineBrushProperty); set => SetValue(BeatLineBrushProperty, value); }

    /// <summary>Identifies the <see cref="SubLineBrush"/> dependency property.</summary>
    public static readonly DependencyProperty SubLineBrushProperty = RegisterBrush(nameof(SubLineBrush), typeof(PianoRoll));
    /// <summary>Brush for the grid lines between beats.</summary>
    public Brush? SubLineBrush { get => (Brush?)GetValue(SubLineBrushProperty); set => SetValue(SubLineBrushProperty, value); }

    /// <summary>Identifies the <see cref="NoteBrush"/> dependency property.</summary>
    public static readonly DependencyProperty NoteBrushProperty = RegisterBrush(nameof(NoteBrush), typeof(PianoRoll));
    /// <summary>Brush for the notes.</summary>
    public Brush? NoteBrush { get => (Brush?)GetValue(NoteBrushProperty); set => SetValue(NoteBrushProperty, value); }

    /// <summary>Identifies the <see cref="SelectedNoteBrush"/> dependency property.</summary>
    public static readonly DependencyProperty SelectedNoteBrushProperty = RegisterBrush(nameof(SelectedNoteBrush), typeof(PianoRoll));
    /// <summary>Brush for the selected notes.</summary>
    public Brush? SelectedNoteBrush { get => (Brush?)GetValue(SelectedNoteBrushProperty); set => SetValue(SelectedNoteBrushProperty, value); }

    /// <summary>Identifies the <see cref="NoteBorderBrush"/> dependency property.</summary>
    public static readonly DependencyProperty NoteBorderBrushProperty = RegisterBrush(nameof(NoteBorderBrush), typeof(PianoRoll));
    /// <summary>Brush for the outline of notes.</summary>
    public Brush? NoteBorderBrush { get => (Brush?)GetValue(NoteBorderBrushProperty); set => SetValue(NoteBorderBrushProperty, value); }

    /// <summary>Identifies the <see cref="NoteTextBrush"/> dependency property.</summary>
    public static readonly DependencyProperty NoteTextBrushProperty = RegisterBrush(nameof(NoteTextBrush), typeof(PianoRoll));
    /// <summary>Brush for the note names on notes.</summary>
    public Brush? NoteTextBrush { get => (Brush?)GetValue(NoteTextBrushProperty); set => SetValue(NoteTextBrushProperty, value); }

    /// <summary>Identifies the <see cref="PlayheadBrush"/> dependency property.</summary>
    public static readonly DependencyProperty PlayheadBrushProperty = RegisterBrush(nameof(PlayheadBrush), typeof(PianoRoll));
    /// <summary>Brush for the playhead line.</summary>
    public Brush? PlayheadBrush { get => (Brush?)GetValue(PlayheadBrushProperty); set => SetValue(PlayheadBrushProperty, value); }

    /// <summary>Identifies the <see cref="RulerBrush"/> dependency property.</summary>
    public static readonly DependencyProperty RulerBrushProperty = RegisterBrush(nameof(RulerBrush), typeof(PianoRoll));
    /// <summary>Brush for the ruler.</summary>
    public Brush? RulerBrush { get => (Brush?)GetValue(RulerBrushProperty); set => SetValue(RulerBrushProperty, value); }

    /// <summary>Identifies the <see cref="WhiteKeyBrush"/> dependency property.</summary>
    public static readonly DependencyProperty WhiteKeyBrushProperty = RegisterBrush(nameof(WhiteKeyBrush), typeof(PianoRoll));
    /// <summary>Brush for the white keys.</summary>
    public Brush? WhiteKeyBrush { get => (Brush?)GetValue(WhiteKeyBrushProperty); set => SetValue(WhiteKeyBrushProperty, value); }

    /// <summary>Identifies the <see cref="BlackKeyBrush"/> dependency property.</summary>
    public static readonly DependencyProperty BlackKeyBrushProperty = RegisterBrush(nameof(BlackKeyBrush), typeof(PianoRoll));
    /// <summary>Brush for the black keys.</summary>
    public Brush? BlackKeyBrush { get => (Brush?)GetValue(BlackKeyBrushProperty); set => SetValue(BlackKeyBrushProperty, value); }

    /// <summary>Identifies the <see cref="PressedKeyBrush"/> dependency property.</summary>
    public static readonly DependencyProperty PressedKeyBrushProperty = RegisterBrush(nameof(PressedKeyBrush), typeof(PianoRoll));
    /// <summary>Brush for the pressed keys.</summary>
    public Brush? PressedKeyBrush { get => (Brush?)GetValue(PressedKeyBrushProperty); set => SetValue(PressedKeyBrushProperty, value); }

    /// <summary>Identifies the <see cref="SelectionBrush"/> dependency property.</summary>
    public static readonly DependencyProperty SelectionBrushProperty = RegisterBrush(nameof(SelectionBrush), typeof(PianoRoll));
    /// <summary>Brush for the marquee selection.</summary>
    public Brush? SelectionBrush { get => (Brush?)GetValue(SelectionBrushProperty); set => SetValue(SelectionBrushProperty, value); }

    #endregion

    #region Events

    /// <summary>Identifies the <see cref="AuditionNoteOn"/> routed event.</summary>
    public static readonly RoutedEvent AuditionNoteOnEvent = EventManager.RegisterRoutedEvent(
        nameof(AuditionNoteOn), RoutingStrategy.Bubble, typeof(NoteEventHandler), typeof(PianoRoll));

    /// <summary>The user is touching a note or key – play it so they can hear the pitch.</summary>
    public event NoteEventHandler AuditionNoteOn { add => AddHandler(AuditionNoteOnEvent, value); remove => RemoveHandler(AuditionNoteOnEvent, value); }

    /// <summary>Identifies the <see cref="AuditionNoteOff"/> routed event.</summary>
    public static readonly RoutedEvent AuditionNoteOffEvent = EventManager.RegisterRoutedEvent(
        nameof(AuditionNoteOff), RoutingStrategy.Bubble, typeof(NoteEventHandler), typeof(PianoRoll));

    /// <summary>Raised when a key of the side keyboard, clicked to listen, is released.</summary>
    public event NoteEventHandler AuditionNoteOff { add => AddHandler(AuditionNoteOffEvent, value); remove => RemoveHandler(AuditionNoteOffEvent, value); }

    /// <summary>Identifies the <see cref="NotesEdited"/> routed event.</summary>
    public static readonly RoutedEvent NotesEditedEvent = EventManager.RegisterRoutedEvent(
        nameof(NotesEdited), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(PianoRoll));

    /// <summary>
    /// Raised after the roll redrew because of scrolling, zooming, note edits, snap or skin changes.
    /// Companion views (e.g. <see cref="ControllerLane"/>) use it to stay aligned.
    /// </summary>
    public event EventHandler? ViewChanged;

    /// <summary>Raised when <see cref="PlayheadPosition"/> changes (cheap; the grid is not redrawn).</summary>
    public event EventHandler? PlayheadMoved;

    /// <summary>Raised once per completed edit gesture (mouse-up, key press). Hook undo snapshots here.</summary>
    public event RoutedEventHandler NotesEdited { add => AddHandler(NotesEditedEvent, value); remove => RemoveHandler(NotesEditedEvent, value); }

    #endregion

    #region Collection tracking

    private static void OnNotesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var roll = (PianoRoll)d;
        roll.Detach();
        if (roll.IsLoaded) roll.Attach(e.NewValue as IList<MidiNote>);
    }

    // Notes we hold a PropertyChanged subscription on. Tracked explicitly because a Reset
    // notification does not say which items were removed.
    private readonly HashSet<MidiNote> _subscribed = new();

    private void Attach(IList<MidiNote>? notes)
    {
        if (ReferenceEquals(_notes, notes)) return;
        Detach();
        _notes = notes;
        if (notes is null) return;
        if (notes is INotifyCollectionChanged incc) incc.CollectionChanged += OnCollectionChanged;
        SubscribeAll();
        InvalidateVisual();
    }

    private void Detach()
    {
        if (_notes is null) return;
        if (_notes is INotifyCollectionChanged incc) incc.CollectionChanged -= OnCollectionChanged;
        UnsubscribeAll();
        _notes = null;
    }

    private void SubscribeAll()
    {
        if (_notes is null) return;
        foreach (MidiNote n in _notes)
            if (_subscribed.Add(n)) n.PropertyChanged += OnNotePropertyChanged;
    }

    private void UnsubscribeAll()
    {
        foreach (MidiNote n in _subscribed) n.PropertyChanged -= OnNotePropertyChanged;
        _subscribed.Clear();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            UnsubscribeAll();
            SubscribeAll();
        }
        else
        {
            if (e.OldItems is not null)
                foreach (MidiNote n in e.OldItems)
                    if (_subscribed.Remove(n)) n.PropertyChanged -= OnNotePropertyChanged;
            if (e.NewItems is not null)
                foreach (MidiNote n in e.NewItems)
                    if (_subscribed.Add(n)) n.PropertyChanged += OnNotePropertyChanged;
        }
        // A removed note can't stay part of an in-flight drag.
        if (_mode is DragMode.Move or DragMode.Resize && _primary is not null && !_subscribed.Contains(_primary))
            EndGesture();
        InvalidateVisual();
    }

    private void OnNotePropertyChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();

    private IEnumerable<MidiNote> AllNotes => (IEnumerable<MidiNote>?)_notes ?? Notes ?? (IEnumerable<MidiNote>)Array.Empty<MidiNote>();

    private IEnumerable<MidiNote> Selected => AllNotes.Where(n => n.IsSelected);

    #endregion

    #region Coordinates

    private Rect GridRect => new(KeyboardWidth, RulerHeight,
        Math.Max(0, ActualWidth - KeyboardWidth), Math.Max(0, ActualHeight - RulerHeight));

    /// <summary>The x coordinate of a position in beats.</summary>
    public double BeatToX(double beat) => KeyboardWidth + beat * PixelsPerBeat - HorizontalOffset;

    /// <summary>The position in beats at an x coordinate of the control.</summary>
    public double XToBeat(double x) => (x - KeyboardWidth + HorizontalOffset) / PixelsPerBeat;

    /// <summary>The y coordinate of the top of a note's row.</summary>
    public double PitchToY(int pitch) => RulerHeight + (NoteCount - 1 - pitch) * KeyHeight - VerticalOffset;

    /// <summary>The MIDI note at a y coordinate of the control.</summary>
    public int YToPitch(double y) => NoteCount - 1 - (int)Math.Floor((y - RulerHeight + VerticalOffset) / KeyHeight);

    private Rect NoteRect(MidiNote n)
        => new(BeatToX(n.Start), PitchToY(n.Pitch), Math.Max(2, n.Length * PixelsPerBeat), KeyHeight);

    private bool SnapActive => Snap > 0 && (Keyboard.Modifiers & ModifierKeys.Alt) == 0;

    private double SnapRound(double beat) => SnapActive ? Math.Round(beat / Snap) * Snap : beat;

    private double SnapFloor(double beat) => SnapActive ? Math.Floor(beat / Snap + 1e-9) * Snap : beat;

    /// <summary>Visible grid spacing in beats: the snap value, coarsened until lines are at least 6 px apart.</summary>
    public double GetGridStep()
    {
        double step = Snap > 0 ? Snap : 0.25;
        while (step * PixelsPerBeat < 6) step *= 2;
        return step;
    }

    #endregion

    #region Rendering

    /// <inheritdoc/>
    protected override void OnRender(DrawingContext dc)
    {
        var snap = new PixelSnapper(this);
        Rect grid = GridRect;
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        dc.PushClip(new RectangleGeometry(grid));
        RenderGrid(dc, snap, grid);
        RenderNotes(dc, snap, grid);
        if (_mode == DragMode.Marquee && SelectionBrush is { } sel)
            dc.DrawRectangle(sel, FrozenPens.Create(PlayheadBrush, snap.OnePixel), _marquee);
        dc.Pop();

        RenderRuler(dc, snap, grid);
        RenderKeyboard(dc, snap, grid);

        // Corner block
        if (RulerBrush is { } rb)
            dc.DrawRectangle(rb, null, new Rect(0, 0, KeyboardWidth, RulerHeight));

        RenderPlayheadLayer();

        // Tell followers (the controller lane) only when the view really moved, not on every note redraw.
        var view = (HorizontalOffset, VerticalOffset, PixelsPerBeat, KeyHeight, w, h, KeyboardWidth);
        if (view != _lastView)
        {
            _lastView = view;
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private (double, double, double, double, double, double, double) _lastView;

    /// <inheritdoc/>
    protected override int VisualChildrenCount => base.VisualChildrenCount + 1;

    /// <inheritdoc/>
    protected override Visual GetVisualChild(int index)
        => index < base.VisualChildrenCount ? base.GetVisualChild(index) : _playheadLayer;

    private void RenderPlayheadLayer()
    {
        using DrawingContext dc = _playheadLayer.RenderOpen();
        if (PlayheadBrush is not { } pb || ActualWidth <= KeyboardWidth || ActualHeight <= 0) return;
        double x = BeatToX(PlayheadPosition);
        if (x < KeyboardWidth - 6 || x > ActualWidth + 6) return;

        var snap = new PixelSnapper(this);
        dc.PushClip(new RectangleGeometry(new Rect(KeyboardWidth, 0, ActualWidth - KeyboardWidth, ActualHeight)));
        Pen pen = FrozenPens.Create(pb, snap.OnePixel)!;
        double lx = snap.LineX(x);
        dc.DrawLine(pen, new Point(lx, RulerHeight), new Point(lx, ActualHeight));

        var tri = new StreamGeometry();
        using (StreamGeometryContext ctx = tri.Open())
        {
            ctx.BeginFigure(new Point(x - 5, RulerHeight - 8), true, true);
            ctx.LineTo(new Point(x + 5, RulerHeight - 8), false, false);
            ctx.LineTo(new Point(x, RulerHeight), false, false);
        }
        tri.Freeze();
        dc.DrawGeometry(pb, null, tri);
        dc.Pop();
    }

    private (int top, int bottom) VisiblePitchRange(Rect grid)
        => (Math.Clamp(YToPitch(grid.Top), 0, NoteCount - 1), Math.Clamp(YToPitch(grid.Bottom - 0.01), 0, NoteCount - 1));

    private void RenderGrid(DrawingContext dc, PixelSnapper snap, Rect grid)
    {
        if (Background is { } bg) dc.DrawRectangle(bg, null, grid);

        var (top, bottom) = VisiblePitchRange(grid);
        Pen? octavePen = FrozenPens.Create(BeatLineBrush, snap.OnePixel);
        for (int p = top; p >= bottom; p--)
        {
            double y = PitchToY(p);
            if (MidiNoteName.IsBlackKey(p) && BlackRowBrush is { } br)
                dc.DrawRectangle(br, null, snap.Rect(grid.Left, y, grid.Width, KeyHeight));
            // Separator under every C and E (where two white keys meet).
            int pc = p % 12;
            if ((pc == 0 || pc == 5) && octavePen is not null)
            {
                double ly = snap.LineY(y + KeyHeight);
                dc.DrawLine(octavePen, new Point(grid.Left, ly), new Point(grid.Right, ly));
            }
        }

        Pen? barPen = FrozenPens.Create(BarLineBrush, snap.OnePixel);
        Pen? beatPen = FrozenPens.Create(BeatLineBrush, snap.OnePixel);
        Pen? subPen = FrozenPens.Create(SubLineBrush, snap.OnePixel);
        double step = GetGridStep();
        double startBeat = Math.Max(0, XToBeat(grid.Left));
        double endBeat = Math.Min(LengthInBeats, XToBeat(grid.Right));
        long first = (long)Math.Floor(startBeat / step);
        long last = (long)Math.Ceiling(endBeat / step);
        int bpb = BeatsPerBar;
        for (long i = first; i <= last; i++)
        {
            double beat = i * step;
            double x = snap.LineX(BeatToX(beat));
            Pen? pen = IsMultiple(beat, bpb) ? barPen : IsMultiple(beat, 1) ? beatPen : subPen;
            if (pen is not null) dc.DrawLine(pen, new Point(x, grid.Top), new Point(x, grid.Bottom));
        }

        // Dim the area past the end of the clip.
        double endX = BeatToX(LengthInBeats);
        if (endX < grid.Right)
        {
            dc.PushOpacity(0.5);
            dc.DrawRectangle(Brushes.Black, null, new Rect(Math.Max(grid.Left, endX), grid.Top, grid.Right - Math.Max(grid.Left, endX), grid.Height));
            dc.Pop();
        }
    }

    private static bool IsMultiple(double value, double of) => Math.Abs(value / of - Math.Round(value / of)) < 1e-6;

    private readonly NineBatch _noteBatch = new();
    private readonly NoteSkins _noteSkins = new();

    /// <summary>
    /// Notes are nine-patch skins (outline, light top edge, shaded bottom; see <see cref="NoteSkins"/>) rendered
    /// in one native batch call into one bitmap, instead of one rounded rectangle per note. Labels stay vector.
    /// </summary>
    private void RenderNotes(DrawingContext dc, PixelSnapper snap, Rect grid)
    {
        bool labels = KeyHeight >= 11;
        if (_labelCacheDip != snap.PixelsPerDip)
        {
            _noteLabelCache.Clear();
            _labelCacheDip = snap.PixelsPerDip;
        }

        BrushColors.TryGetSolid(NoteBrush, out Color noteColor);
        _noteBatch.Begin(this, grid.Size);
        List<(Rect rect, int pitch)>? labelList = labels && NoteTextBrush is not null ? new() : null;
        // Dependency-property reads are not free: read the view once, not per note.
        double ppb = PixelsPerBeat, kh = KeyHeight, x0 = KeyboardWidth - HorizontalOffset;
        double y0 = RulerHeight + (NoteCount - 1) * kh - VerticalOffset;
        foreach (MidiNote n in AllNotes)
        {
            double nx = x0 + n.Start * ppb, ny = y0 - n.Pitch * kh;
            if (ny + kh < grid.Top || ny > grid.Bottom) continue;
            double nw = Math.Max(2, n.Length * ppb);
            if (nx + nw < grid.Left || nx > grid.Right) continue;
            Rect r = new(nx, ny, nw, kh);
            Rect sr = snap.Rect(r.X, r.Y + 1, r.Width, r.Height - 1);
            Color c = n.Brush is { } nb && BrushColors.TryGetSolid(nb, out Color own) ? own : noteColor;
            c = NoteSkins.ByVelocity(c, n.Velocity);   // velocity → brightness, in 16 steps
            _noteBatch.Add(_noteSkins.TryGet(c, n.IsSelected), new Rect(sr.X - grid.X, sr.Y - grid.Y, sr.Width, sr.Height),
                FrameSpec.Solid(c, n.IsSelected ? Colors.White : Palette.Darken(c, 0.45), 2));
            if (labelList is not null && sr.Width > 26) labelList.Add((sr, n.Pitch));
        }
        _noteBatch.Render(dc, grid.TopLeft);

        if (labelList is null) return;
        foreach (var (sr, pitch) in labelList)
        {
            {
                FormattedText ft = NoteLabel(pitch, snap.PixelsPerDip);
                if (ft.Width + 6 < sr.Width)
                {
                    dc.PushClip(new RectangleGeometry(sr));
                    dc.DrawText(ft, new Point(sr.X + 4, sr.Y + (sr.Height - ft.Height) / 2));
                    dc.Pop();
                }
            }
        }
    }

    private FormattedText NoteLabel(int pitch, double ppd)
    {
        if (!_noteLabelCache.TryGetValue(pitch, out FormattedText? ft))
        {
            ft = MakeText(MidiNoteName.Get(pitch, MiddleCOctave), ppd, NoteTextBrush, Math.Min(FontSize, KeyHeight - 3));
            _noteLabelCache[pitch] = ft;
        }
        return ft;
    }

    private void RenderRuler(DrawingContext dc, PixelSnapper snap, Rect grid)
    {
        var ruler = new Rect(grid.Left, 0, grid.Width, RulerHeight);
        if (RulerBrush is { } rb) dc.DrawRectangle(rb, null, ruler);
        dc.PushClip(new RectangleGeometry(ruler));

        Pen? tickPen = FrozenPens.Create(BarLineBrush, snap.OnePixel);
        int bpb = BeatsPerBar;
        double barPx = bpb * PixelsPerBeat;
        int labelEvery = 1;
        while (labelEvery * barPx < 40) labelEvery *= 2;

        long firstBar = (long)Math.Floor(Math.Max(0, XToBeat(grid.Left)) / bpb);
        long lastBar = (long)Math.Ceiling(Math.Min(LengthInBeats, XToBeat(grid.Right)) / bpb);
        for (long bar = firstBar; bar <= lastBar; bar++)
        {
            double x = snap.LineX(BeatToX(bar * bpb));
            if (tickPen is not null)
                dc.DrawLine(tickPen, new Point(x, RulerHeight * 0.45), new Point(x, RulerHeight));
            if (bar % labelEvery == 0)
            {
                FormattedText ft = MakeText((bar + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), snap.PixelsPerDip);
                dc.DrawText(ft, new Point(x + 3, (RulerHeight * 0.55 - ft.Height) / 2 + 1));
            }
            if (PixelsPerBeat >= 12 && tickPen is not null)
            {
                for (int b = 1; b < bpb; b++)
                {
                    double bx = snap.LineX(BeatToX(bar * bpb + b));
                    dc.DrawLine(tickPen, new Point(bx, RulerHeight * 0.75), new Point(bx, RulerHeight));
                }
            }
        }

        dc.Pop();
    }

    private void RenderKeyboard(DrawingContext dc, PixelSnapper snap, Rect grid)
    {
        double kw = KeyboardWidth;
        if (kw <= 0) return;
        var area = new Rect(0, grid.Top, kw, grid.Height);
        dc.PushClip(new RectangleGeometry(area));
        if (WhiteKeyBrush is { } wk) dc.DrawRectangle(wk, null, area);

        Pen? sep = FrozenPens.Create(BorderBrush, snap.OnePixel);
        var (top, bottom) = VisiblePitchRange(grid);
        double blackW = kw * 0.62;
        for (int p = top; p >= bottom; p--)
        {
            double y = PitchToY(p);
            bool held = p == _auditionNote;
            if (MidiNoteName.IsBlackKey(p))
            {
                Brush? b = held ? PressedKeyBrush ?? BlackKeyBrush : BlackKeyBrush;
                if (b is not null) dc.DrawRoundedRectangle(b, null, snap.Rect(0, y + 1, blackW, KeyHeight - 2), 0, 2);
                continue;
            }
            if (held && PressedKeyBrush is { } pk)
                dc.DrawRectangle(pk, null, snap.Rect(0, y, kw, KeyHeight));
            int pc = p % 12;
            if ((pc == 0 || pc == 5) && sep is not null)
            {
                double ly = snap.LineY(y + KeyHeight);
                dc.DrawLine(sep, new Point(0, ly), new Point(kw, ly));
            }
            if (pc == 0 && KeyHeight >= 8)
            {
                FormattedText ft = MakeText(MidiNoteName.Get(p, MiddleCOctave), snap.PixelsPerDip,
                    BorderBrush ?? Brushes.Black, Math.Min(FontSize, KeyHeight - 2));
                dc.DrawText(ft, new Point(kw - ft.Width - 4, y + (KeyHeight - ft.Height) / 2));
            }
        }
        if (sep is not null)
        {
            double x = snap.LineX(kw - 0.5);
            dc.DrawLine(sep, new Point(x, grid.Top), new Point(x, grid.Bottom));
        }
        dc.Pop();
    }

    #endregion

    #region Hit testing

    private enum NoteZone { Body, RightEdge }

    private MidiNote? HitTestNote(Point p, out NoteZone zone)
    {
        zone = NoteZone.Body;
        if (_notes is null && Notes is null) return null;
        MidiNote? hit = null;
        foreach (MidiNote n in AllNotes) // later notes draw on top → keep the last hit
        {
            Rect r = NoteRect(n);
            if (!r.Contains(p)) continue;
            hit = n;
            double grip = Math.Min(EdgeGrip, r.Width / 3);
            zone = p.X >= r.Right - grip ? NoteZone.RightEdge : NoteZone.Body;
        }
        return hit;
    }

    #endregion

    #region Mouse

    /// <inheritdoc/>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        Point p = e.GetPosition(this);
        _anchor = p;
        _edited = false;
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

        if (p.Y < RulerHeight && p.X >= KeyboardWidth)
        {
            _mode = DragMode.Scrub;
            SetCurrentValue(PlayheadPositionProperty, Math.Clamp(SnapRound(XToBeat(p.X)), 0, LengthInBeats));
        }
        else if (p.X < KeyboardWidth && p.Y >= RulerHeight)
        {
            _mode = DragMode.Audition;
            StartAudition(YToPitch(p.Y));
        }
        else if (GridRect.Contains(p))
        {
            MidiNote? hit = HitTestNote(p, out NoteZone zone);
            if (hit is not null)
            {
                if (ctrl)
                {
                    hit.IsSelected = !hit.IsSelected;
                    e.Handled = true;
                    return;
                }
                if (!hit.IsSelected)
                {
                    foreach (MidiNote n in AllNotes) n.IsSelected = false;
                    hit.IsSelected = true;
                }
                if (IsReadOnly) { e.Handled = true; return; }
                BeginNoteDrag(hit, zone == NoteZone.RightEdge ? DragMode.Resize : DragMode.Move);
                if (zone == NoteZone.Body) StartAudition(hit.Pitch, hit.Velocity);
            }
            else if (ctrl)
            {
                _mode = DragMode.Marquee;
                _marquee = new Rect(p, p);
                _marqueeBase = new HashSet<MidiNote>(Selected);
            }
            else
            {
                foreach (MidiNote n in AllNotes) n.IsSelected = false;
                if (!IsReadOnly && Notes is { IsReadOnly: false } list)
                {
                    int pitch = Math.Clamp(YToPitch(p.Y), 0, NoteCount - 1);
                    double start = Math.Max(0, SnapFloor(XToBeat(p.X)));
                    var note = new MidiNote(pitch, start, _lastLength, DefaultVelocity) { IsSelected = true };
                    list.Add(note);
                    _edited = true;
                    BeginNoteDrag(note, DragMode.Move);
                    StartAudition(pitch, note.Velocity);
                }
            }
        }

        if (_mode != DragMode.None) CaptureMouse();
        e.Handled = true;
    }

    private void BeginNoteDrag(MidiNote primary, DragMode mode)
    {
        _mode = mode;
        _primary = primary;
        _original.Clear();
        foreach (MidiNote n in Selected) _original[n] = (n.Pitch, n.Start, n.Length);
        _original[primary] = (primary.Pitch, primary.Start, primary.Length);
    }

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Point p = e.GetPosition(this);

        if (_mode == DragMode.None)
        {
            UpdateHoverCursor(p);
            return;
        }
        if (!IsMouseCaptured) return;

        switch (_mode)
        {
            case DragMode.Scrub:
                SetCurrentValue(PlayheadPositionProperty, Math.Clamp(SnapRound(XToBeat(p.X)), 0, LengthInBeats));
                break;
            case DragMode.Audition:
                int pitch = Math.Clamp(YToPitch(p.Y), 0, NoteCount - 1);
                if (pitch != _auditionNote) StartAudition(pitch);
                break;
            case DragMode.Move:
                DragMove(p);
                break;
            case DragMode.Resize:
                DragResize(p);
                break;
            case DragMode.Marquee:
                _marquee = new Rect(_anchor, p);
                foreach (MidiNote n in AllNotes)
                    n.IsSelected = (_marqueeBase?.Contains(n) ?? false) || _marquee.IntersectsWith(NoteRect(n));
                InvalidateVisual();
                break;
        }
    }

    private void DragMove(Point p)
    {
        if (_primary is null || !_original.TryGetValue(_primary, out var po)) return;
        double dBeat = (p.X - _anchor.X) / PixelsPerBeat;
        int dPitch = -(int)Math.Round((p.Y - _anchor.Y) / KeyHeight);

        // Snap the primary note's start, then apply the same delta to the whole selection.
        double delta = SnapRound(po.start + dBeat) - po.start;
        double minStart = _original.Values.Min(v => v.start);
        delta = Math.Max(delta, -minStart);
        int minPitch = _original.Values.Min(v => v.pitch), maxPitch = _original.Values.Max(v => v.pitch);
        dPitch = Math.Clamp(dPitch, -minPitch, NoteCount - 1 - maxPitch);

        foreach (var (note, o) in _original)
        {
            note.Start = o.start + delta;
            note.Pitch = o.pitch + dPitch;
        }
        _edited |= delta != 0 || dPitch != 0;
        if (_primary.Pitch != _auditionNote) StartAudition(_primary.Pitch, _primary.Velocity);
    }

    private void DragResize(Point p)
    {
        if (_primary is null || !_original.TryGetValue(_primary, out var po)) return;
        double dBeat = (p.X - _anchor.X) / PixelsPerBeat;
        double end = SnapRound(po.start + po.length + dBeat);
        double minLen = SnapActive ? Snap : MidiNote.MinLength;
        double deltaLen = end - (po.start + po.length);
        foreach (var (note, o) in _original)
        {
            note.Length = Math.Max(minLen, o.length + deltaLen);
            // A note shorter than the snap grows to it even with no drag: that is an edit too.
            _edited |= note.Length != o.length;
        }
        _lastLength = _primary.Length;
    }

    private void UpdateHoverCursor(Point p)
    {
        Cursor? c = null;
        if (p.Y < RulerHeight && p.X >= KeyboardWidth) c = Cursors.IBeam;
        else if (GridRect.Contains(p) && !IsReadOnly && HitTestNote(p, out NoteZone zone) is not null)
            c = zone == NoteZone.RightEdge ? Cursors.SizeWE : Cursors.SizeAll;
        if (!ReferenceEquals(Cursor, c)) SetCurrentValue(CursorProperty, c);
    }

    /// <inheritdoc/>
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (IsMouseCaptured) ReleaseMouseCapture();
        else EndGesture();
    }

    /// <inheritdoc/>
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        EndGesture();
    }

    private void EndGesture()
    {
        if (_mode == DragMode.None) return;
        if (_mode == DragMode.Move && _primary is not null) _lastLength = _primary.Length;
        _mode = DragMode.None;
        _primary = null;
        _original.Clear();
        _marqueeBase = null;
        StopAudition();
        InvalidateVisual();
        if (_edited)
        {
            _edited = false;
            RaiseEvent(new RoutedEventArgs(NotesEditedEvent, this));
        }
    }

    /// <inheritdoc/>
    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        if (IsReadOnly || _mode != DragMode.None || Notes is not { IsReadOnly: false } list) return;
        MidiNote? hit = HitTestNote(e.GetPosition(this), out _);
        if (hit is null) return; // let the ContextMenu open on empty space
        MidiNote[] victims = hit.IsSelected ? Selected.ToArray() : new[] { hit };
        foreach (MidiNote n in victims) list.Remove(n);
        RaiseEvent(new RoutedEventArgs(NotesEditedEvent, this));
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (e.Handled) return;
        double notches = e.Delta / (double)Mouse.MouseWheelDeltaForOneLine;
        ModifierKeys mods = Keyboard.Modifiers;
        Point p = e.GetPosition(this);

        if ((mods & ModifierKeys.Control) != 0)
        {
            // Zoom time, keeping the beat under the mouse fixed.
            double beat = XToBeat(p.X);
            SetCurrentValue(PixelsPerBeatProperty, PixelsPerBeat * Math.Pow(1.15, notches));
            SetHorizontalOffset(beat * PixelsPerBeat - (p.X - KeyboardWidth));
        }
        else if ((mods & ModifierKeys.Alt) != 0)
        {
            double rowsFromTop = (p.Y - RulerHeight + VerticalOffset) / KeyHeight;
            SetCurrentValue(KeyHeightProperty, KeyHeight * Math.Pow(1.1, notches));
            SetVerticalOffset(rowsFromTop * KeyHeight - (p.Y - RulerHeight));
        }
        else if ((mods & ModifierKeys.Shift) != 0)
        {
            SetHorizontalOffset(HorizontalOffset - notches * PixelsPerBeat);
        }
        else
        {
            SetVerticalOffset(VerticalOffset - notches * 3 * KeyHeight);
        }
        e.Handled = true; // we did the scrolling; stop the ScrollViewer doing it again
    }

    #endregion

    #region Audition

    private void StartAudition(int pitch, int velocity = 100)
    {
        StopAudition();
        if (pitch < 0 || pitch >= NoteCount) return;
        _auditionNote = pitch;
        RaiseEvent(new NoteEventArgs(AuditionNoteOnEvent, this, pitch, velocity));
        InvalidateVisual();
    }

    private void StopAudition()
    {
        if (_auditionNote < 0) return;
        int note = _auditionNote;
        _auditionNote = -1;
        RaiseEvent(new NoteEventArgs(AuditionNoteOffEvent, this, note, 0));
        InvalidateVisual();
    }

    #endregion

    #region Keyboard

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        ModifierKeys mods = Keyboard.Modifiers;
        bool ctrl = (mods & ModifierKeys.Control) != 0;
        bool shift = (mods & ModifierKeys.Shift) != 0;

        if (ctrl && e.Key == Key.A)
        {
            foreach (MidiNote n in AllNotes) n.IsSelected = true;
            e.Handled = true;
            return;
        }
        if (IsReadOnly || Notes is not { IsReadOnly: false } list) return;

        MidiNote[] sel = Selected.ToArray();
        if (sel.Length == 0) return;
        double step = Snap > 0 ? Snap : 0.25;

        switch (e.Key)
        {
            case Key.Delete:
            case Key.Back:
                foreach (MidiNote n in sel) list.Remove(n);
                break;
            case Key.Up:
            case Key.Down:
                int dp = (e.Key == Key.Up ? 1 : -1) * (shift ? 12 : 1);
                if (sel.Any(n => n.Pitch + dp is < 0 or > NoteCount - 1)) { e.Handled = true; return; }
                foreach (MidiNote n in sel) n.Pitch += dp;
                break;
            case Key.Left:
            case Key.Right:
                double db = e.Key == Key.Right ? step : -step;
                if (sel.Any(n => n.Start + db < 0)) { e.Handled = true; return; }
                foreach (MidiNote n in sel) n.Start += db;
                break;
            case Key.D when ctrl:
                double span = sel.Max(n => n.End) - sel.Min(n => n.Start);
                double offset = Snap > 0 ? Math.Ceiling(span / Snap - 1e-9) * Snap : span;
                foreach (MidiNote n in sel)
                {
                    n.IsSelected = false;
                    MidiNote copy = n.Clone();
                    copy.Start += offset;
                    copy.IsSelected = true;
                    list.Add(copy);
                }
                break;
            default:
                return;
        }
        e.Handled = true;
        RaiseEvent(new RoutedEventArgs(NotesEditedEvent, this));
    }

    #endregion

    #region Playhead follow

    private static void OnPlayheadChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var roll = (PianoRoll)d;
        roll.RenderPlayheadLayer();
        roll.PlayheadMoved?.Invoke(roll, EventArgs.Empty);
        if (!roll.FollowPlayhead || roll._mode == DragMode.Scrub) return;
        double x = (double)e.NewValue * roll.PixelsPerBeat;
        double visible = Math.Max(0, roll.ViewportWidth - roll.KeyboardWidth);
        if (visible <= 0) return;
        if (x < roll.HorizontalOffset || x > roll.HorizontalOffset + visible)
            roll.SetHorizontalOffset(x - visible * 0.1); // page forward, keep a little context
    }

    #endregion

    #region IScrollInfo

    private Size _extent;
    private Size _viewport;
    private Vector _offset;

    /// <inheritdoc/>
    public bool CanVerticallyScroll { get; set; } = true;
    /// <inheritdoc/>
    public bool CanHorizontallyScroll { get; set; } = true;
    /// <inheritdoc/>
    public double ExtentWidth => _extent.Width;
    /// <inheritdoc/>
    public double ExtentHeight => _extent.Height;
    /// <inheritdoc/>
    public double ViewportWidth => _viewport.Width;
    /// <inheritdoc/>
    public double ViewportHeight => _viewport.Height;
    /// <inheritdoc/>
    public double HorizontalOffset => _offset.X;
    /// <inheritdoc/>
    public double VerticalOffset => _offset.Y;
    /// <inheritdoc/>
    public ScrollViewer? ScrollOwner { get; set; }

    private static void OnExtentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var roll = (PianoRoll)d;
        roll._noteLabelCache.Clear();
        roll.UpdateScrollInfo(roll.RenderSize);
        roll.InvalidateVisual();
    }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size constraint)
    {
        Size extent = ComputeExtent();
        // Without a width limit (a plain ScrollViewer, an Auto column) do not take the whole song: the notes
        // are drawn into one bitmap of the control's size. Put the roll in a sized cell; it scrolls by itself.
        return new Size(
            double.IsInfinity(constraint.Width) ? Math.Min(extent.Width, MaxUnconstrainedSize) : constraint.Width,
            double.IsInfinity(constraint.Height) ? Math.Min(extent.Height, 400) : constraint.Height);
    }

    /// <summary>Largest width the roll asks for when its parent does not limit it.</summary>
    public const double MaxUnconstrainedSize = 4096;

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size arrangeBounds)
    {
        UpdateScrollInfo(arrangeBounds);
        if (!_initialScroll && arrangeBounds.Height > 0)
        {
            _initialScroll = true;
            // Start centred around middle C (C3..C6 visible on a typical height).
            double centerY = (NoteCount - 1 - 66) * KeyHeight;
            SetVerticalOffset(centerY - (arrangeBounds.Height - RulerHeight) / 2);
        }
        return arrangeBounds;
    }

    private Size ComputeExtent() => new(
        KeyboardWidth + LengthInBeats * PixelsPerBeat,
        RulerHeight + NoteCount * KeyHeight);

    private void UpdateScrollInfo(Size viewport)
    {
        Size extent = ComputeExtent();
        bool changed = extent != _extent || viewport != _viewport;
        _extent = extent;
        _viewport = viewport;
        Vector clamped = ClampOffset(_offset);
        if (clamped != _offset)
        {
            _offset = clamped;
            changed = true;
        }
        if (changed) ScrollOwner?.InvalidateScrollInfo();
    }

    private Vector ClampOffset(Vector v) => new(
        Math.Clamp(v.X, 0, Math.Max(0, _extent.Width - _viewport.Width)),
        Math.Clamp(v.Y, 0, Math.Max(0, _extent.Height - _viewport.Height)));

    /// <inheritdoc/>
    public void SetHorizontalOffset(double offset)
    {
        if (double.IsNaN(offset)) return;
        Vector v = ClampOffset(new Vector(offset, _offset.Y));
        if (v == _offset) return;
        _offset = v;
        ScrollOwner?.InvalidateScrollInfo();
        InvalidateVisual();
    }

    /// <inheritdoc/>
    public void SetVerticalOffset(double offset)
    {
        if (double.IsNaN(offset)) return;
        Vector v = ClampOffset(new Vector(_offset.X, offset));
        if (v == _offset) return;
        _offset = v;
        ScrollOwner?.InvalidateScrollInfo();
        InvalidateVisual();
    }

    /// <inheritdoc/>
    public void LineUp() => SetVerticalOffset(VerticalOffset - KeyHeight);
    /// <inheritdoc/>
    public void LineDown() => SetVerticalOffset(VerticalOffset + KeyHeight);
    /// <inheritdoc/>
    public void LineLeft() => SetHorizontalOffset(HorizontalOffset - PixelsPerBeat / 4);
    /// <inheritdoc/>
    public void LineRight() => SetHorizontalOffset(HorizontalOffset + PixelsPerBeat / 4);
    /// <inheritdoc/>
    public void PageUp() => SetVerticalOffset(VerticalOffset - ViewportHeight * 0.9);
    /// <inheritdoc/>
    public void PageDown() => SetVerticalOffset(VerticalOffset + ViewportHeight * 0.9);
    /// <inheritdoc/>
    public void PageLeft() => SetHorizontalOffset(HorizontalOffset - ViewportWidth * 0.9);
    /// <inheritdoc/>
    public void PageRight() => SetHorizontalOffset(HorizontalOffset + ViewportWidth * 0.9);
    /// <inheritdoc/>
    public void MouseWheelUp() => SetVerticalOffset(VerticalOffset - 3 * KeyHeight);
    /// <inheritdoc/>
    public void MouseWheelDown() => SetVerticalOffset(VerticalOffset + 3 * KeyHeight);
    /// <inheritdoc/>
    public void MouseWheelLeft() => SetHorizontalOffset(HorizontalOffset - PixelsPerBeat);
    /// <inheritdoc/>
    public void MouseWheelRight() => SetHorizontalOffset(HorizontalOffset + PixelsPerBeat);

    /// <inheritdoc/>
    public Rect MakeVisible(Visual visual, Rect rectangle) => rectangle;

    /// <summary>Scrolls so the given beat/pitch is visible.</summary>
    public void ScrollIntoView(double beat, int pitch)
    {
        double x = beat * PixelsPerBeat, y = (NoteCount - 1 - pitch) * KeyHeight;
        double vw = Math.Max(0, ViewportWidth - KeyboardWidth), vh = Math.Max(0, ViewportHeight - RulerHeight);
        if (x < HorizontalOffset || x > HorizontalOffset + vw) SetHorizontalOffset(x - vw / 4);
        if (y < VerticalOffset || y + KeyHeight > VerticalOffset + vh) SetVerticalOffset(y - vh / 2);
    }

    #endregion
}
