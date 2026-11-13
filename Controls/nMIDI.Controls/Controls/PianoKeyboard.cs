using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using nMIDI.Controls.Models;
using nMIDI.Controls.Primitives;

namespace nMIDI.Controls;

/// <summary>How a <see cref="PianoKeyboard"/> draws its keys.</summary>
public enum PianoKeyStyle
{
    /// <summary>Flat keys.</summary>
    Flat,
    /// <summary>Shaded keys with depth.</summary>
    ThreeD,
    /// <summary>
    /// Hand-drawn grand piano: ivory keys with a front lip, glossy black keys, a red felt strip, and lit keys
    /// in their own colour with the finger number (and hand) or note name on them. Ignores the key brushes.
    /// </summary>
    Grand,
}

/// <summary>
/// Playable on-screen piano keyboard.
/// <list type="bullet">
/// <item>Mouse: click to play, drag for glissando; velocity follows how far down the key you click.</item>
/// <item>Computer keyboard (when focused and <see cref="IsComputerKeyboardEnabled"/>): A W S E D F T G Y H U J K O L P, Z/X = octave.</item>
/// <item><see cref="SetHighlighted(int, int)"/> lights keys from incoming MIDI or playback (brightness = velocity) without raising events.</item>
/// <item><see cref="KeyStyle"/>: sculpted 3D keys (default) or flat.</item>
/// </list>
/// Raises <see cref="NoteOn"/> / <see cref="NoteOff"/> routed events.
/// </summary>
public class PianoKeyboard : DawRenderControl
{
    private static readonly Key[] s_computerKeys =
    {
        Key.A, Key.W, Key.S, Key.E, Key.D, Key.F, Key.T, Key.G, Key.Y, Key.H, Key.U, Key.J, Key.K, Key.O, Key.L, Key.P,
    };

    // Black key x-offsets (fraction of black key width) for a more realistic layout.
    private static readonly double[] s_blackOffset = { 0, -0.12, 0, 0.12, 0, 0, -0.15, 0, 0, 0, 0.15, 0 };

    private readonly byte[] _highlightVelocity = new byte[128]; // 0 = not lit
    private readonly Brush?[] _highlightBrush = new Brush?[128]; // null = HighlightKeyBrush
    private readonly byte[] _hand = new byte[128];   // Grand labels: 0 none, 1 left, 2 right
    private readonly byte[] _finger = new byte[128]; // Grand labels: 1–5, 0 unknown
    private int _mouseVelocity;
    private readonly Dictionary<Key, int> _computerHeld = new();
    private readonly Rect[] _keyRects = new Rect[128];
    private int _layoutLow = -1, _layoutHigh = -1;
    private Size _layoutSize;
    private int _mouseNote = -1;

    static PianoKeyboard()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(PianoKeyboard), new FrameworkPropertyMetadata(typeof(PianoKeyboard)));
        FocusableProperty.OverrideMetadata(typeof(PianoKeyboard), new FrameworkPropertyMetadata(true));
    }

    #region Properties

    /// <summary>Identifies the <see cref="LowestNote"/> dependency property.</summary>
    public static readonly DependencyProperty LowestNoteProperty = DependencyProperty.Register(
        nameof(LowestNote), typeof(int), typeof(PianoKeyboard),
        new FrameworkPropertyMetadata(36, FrameworkPropertyMetadataOptions.AffectsRender, OnRangeChanged), IsMidiNote);

    /// <summary>Lowest MIDI note shown (widened to a white key).</summary>
    public int LowestNote { get => (int)GetValue(LowestNoteProperty); set => SetValue(LowestNoteProperty, value); }

    /// <summary>Identifies the <see cref="HighestNote"/> dependency property.</summary>
    public static readonly DependencyProperty HighestNoteProperty = DependencyProperty.Register(
        nameof(HighestNote), typeof(int), typeof(PianoKeyboard),
        new FrameworkPropertyMetadata(96, FrameworkPropertyMetadataOptions.AffectsRender, OnRangeChanged), IsMidiNote);

    /// <summary>Highest MIDI note shown (widened to a white key).</summary>
    public int HighestNote { get => (int)GetValue(HighestNoteProperty); set => SetValue(HighestNoteProperty, value); }

    /// <summary>Identifies the <see cref="BlackKeyHeightRatio"/> dependency property.</summary>
    public static readonly DependencyProperty BlackKeyHeightRatioProperty = DependencyProperty.Register(
        nameof(BlackKeyHeightRatio), typeof(double), typeof(PianoKeyboard),
        new FrameworkPropertyMetadata(0.62, FrameworkPropertyMetadataOptions.AffectsRender, OnRangeChanged));

    /// <summary>Height of the black keys as a fraction of the white keys.</summary>
    public double BlackKeyHeightRatio { get => (double)GetValue(BlackKeyHeightRatioProperty); set => SetValue(BlackKeyHeightRatioProperty, value); }

    /// <summary>Identifies the <see cref="ShowOctaveLabels"/> dependency property.</summary>
    public static readonly DependencyProperty ShowOctaveLabelsProperty = DependencyProperty.Register(
        nameof(ShowOctaveLabels), typeof(bool), typeof(PianoKeyboard),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Whether C keys are labelled with their octave (C3, C4 …).</summary>
    public bool ShowOctaveLabels { get => (bool)GetValue(ShowOctaveLabelsProperty); set => SetValue(ShowOctaveLabelsProperty, value); }

    /// <summary>Identifies the <see cref="MiddleCOctave"/> dependency property.</summary>
    public static readonly DependencyProperty MiddleCOctaveProperty = DependencyProperty.Register(
        nameof(MiddleCOctave), typeof(int), typeof(PianoKeyboard),
        new FrameworkPropertyMetadata(4, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Octave number of MIDI note 60 in labels: 4 (Roland/scientific) or 3 (Yamaha/Cubase).</summary>
    public int MiddleCOctave { get => (int)GetValue(MiddleCOctaveProperty); set => SetValue(MiddleCOctaveProperty, value); }

    /// <summary>Identifies the <see cref="IsComputerKeyboardEnabled"/> dependency property.</summary>
    public static readonly DependencyProperty IsComputerKeyboardEnabledProperty = DependencyProperty.Register(
        nameof(IsComputerKeyboardEnabled), typeof(bool), typeof(PianoKeyboard), new FrameworkPropertyMetadata(true));

    /// <summary>Whether the computer keyboard plays notes while the control has focus (A W S E D … rows).</summary>
    public bool IsComputerKeyboardEnabled { get => (bool)GetValue(IsComputerKeyboardEnabledProperty); set => SetValue(IsComputerKeyboardEnabledProperty, value); }

    /// <summary>Identifies the <see cref="ComputerKeyboardOctave"/> dependency property.</summary>
    public static readonly DependencyProperty ComputerKeyboardOctaveProperty = DependencyProperty.Register(
        nameof(ComputerKeyboardOctave), typeof(int), typeof(PianoKeyboard),
        new FrameworkPropertyMetadata(4), v => v is int i && i >= -1 && i <= 9);

    /// <summary>Octave (in MIDI octaves, C4 = 60) the "A" key plays.</summary>
    public int ComputerKeyboardOctave { get => (int)GetValue(ComputerKeyboardOctaveProperty); set => SetValue(ComputerKeyboardOctaveProperty, value); }

    /// <summary>Identifies the <see cref="FixedVelocity"/> dependency property.</summary>
    public static readonly DependencyProperty FixedVelocityProperty = DependencyProperty.Register(
        nameof(FixedVelocity), typeof(int), typeof(PianoKeyboard),
        new FrameworkPropertyMetadata(0), v => v is int i && i >= 0 && i <= 127);

    /// <summary>When &gt; 0 every note uses this velocity instead of the click position.</summary>
    public int FixedVelocity { get => (int)GetValue(FixedVelocityProperty); set => SetValue(FixedVelocityProperty, value); }

    /// <summary>Identifies the <see cref="WhiteKeyBrush"/> dependency property.</summary>
    public static readonly DependencyProperty WhiteKeyBrushProperty = RegisterBrush(nameof(WhiteKeyBrush), typeof(PianoKeyboard));
    /// <summary>Brush for the white keys.</summary>
    public Brush? WhiteKeyBrush { get => (Brush?)GetValue(WhiteKeyBrushProperty); set => SetValue(WhiteKeyBrushProperty, value); }

    /// <summary>Identifies the <see cref="BlackKeyBrush"/> dependency property.</summary>
    public static readonly DependencyProperty BlackKeyBrushProperty = RegisterBrush(nameof(BlackKeyBrush), typeof(PianoKeyboard));
    /// <summary>Brush for the black keys.</summary>
    public Brush? BlackKeyBrush { get => (Brush?)GetValue(BlackKeyBrushProperty); set => SetValue(BlackKeyBrushProperty, value); }

    /// <summary>Identifies the <see cref="PressedKeyBrush"/> dependency property.</summary>
    public static readonly DependencyProperty PressedKeyBrushProperty = RegisterBrush(nameof(PressedKeyBrush), typeof(PianoKeyboard));
    /// <summary>Brush for the pressed keys.</summary>
    public Brush? PressedKeyBrush { get => (Brush?)GetValue(PressedKeyBrushProperty); set => SetValue(PressedKeyBrushProperty, value); }

    /// <summary>Identifies the <see cref="HighlightKeyBrush"/> dependency property.</summary>
    public static readonly DependencyProperty HighlightKeyBrushProperty = RegisterBrush(nameof(HighlightKeyBrush), typeof(PianoKeyboard));
    /// <summary>Brush for the keys lit from playback.</summary>
    public Brush? HighlightKeyBrush { get => (Brush?)GetValue(HighlightKeyBrushProperty); set => SetValue(HighlightKeyBrushProperty, value); }

    /// <summary>Identifies the <see cref="KeyStyle"/> dependency property.</summary>
    public static readonly DependencyProperty KeyStyleProperty = DependencyProperty.Register(
        nameof(KeyStyle), typeof(PianoKeyStyle), typeof(PianoKeyboard),
        new FrameworkPropertyMetadata(PianoKeyStyle.ThreeD, FrameworkPropertyMetadataOptions.AffectsRender, OnRangeChanged));

    /// <summary>Flat keys, or sculpted keys with front lips, bevelled black keys, shadows and press depth.</summary>
    public PianoKeyStyle KeyStyle { get => (PianoKeyStyle)GetValue(KeyStyleProperty); set => SetValue(KeyStyleProperty, value); }

    /// <summary>Identifies the <see cref="VelocitySensitiveLighting"/> dependency property.</summary>
    public static readonly DependencyProperty VelocitySensitiveLightingProperty = DependencyProperty.Register(
        nameof(VelocitySensitiveLighting), typeof(bool), typeof(PianoKeyboard),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Pressed / highlighted keys glow brighter the harder the note was hit.</summary>
    public bool VelocitySensitiveLighting { get => (bool)GetValue(VelocitySensitiveLightingProperty); set => SetValue(VelocitySensitiveLightingProperty, value); }

    /// <summary>Identifies the <see cref="LabelMode"/> dependency property.</summary>
    public static readonly DependencyProperty LabelModeProperty = DependencyProperty.Register(
        nameof(LabelMode), typeof(KeyLabelMode), typeof(PianoKeyboard),
        new FrameworkPropertyMetadata(KeyLabelMode.Fingers, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>What lit keys show in the <see cref="PianoKeyStyle.Grand"/> style: finger numbers, note names or nothing.</summary>
    public KeyLabelMode LabelMode { get => (KeyLabelMode)GetValue(LabelModeProperty); set => SetValue(LabelModeProperty, value); }

    private static bool IsMidiNote(object v) => v is int i && i >= 0 && i <= 127;

    private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((PianoKeyboard)d)._layoutLow = -1;

    #endregion

    #region Events

    /// <summary>Identifies the <see cref="NoteOn"/> routed event.</summary>
    public static readonly RoutedEvent NoteOnEvent = EventManager.RegisterRoutedEvent(
        nameof(NoteOn), RoutingStrategy.Bubble, typeof(NoteEventHandler), typeof(PianoKeyboard));

    /// <summary>Raised when a key is played with the mouse or computer keyboard.</summary>
    public event NoteEventHandler NoteOn { add => AddHandler(NoteOnEvent, value); remove => RemoveHandler(NoteOnEvent, value); }

    /// <summary>Identifies the <see cref="NoteOff"/> routed event.</summary>
    public static readonly RoutedEvent NoteOffEvent = EventManager.RegisterRoutedEvent(
        nameof(NoteOff), RoutingStrategy.Bubble, typeof(NoteEventHandler), typeof(PianoKeyboard));

    /// <summary>Raised when a key played with the mouse or computer keyboard is released.</summary>
    public event NoteEventHandler NoteOff { add => AddHandler(NoteOffEvent, value); remove => RemoveHandler(NoteOffEvent, value); }

    #endregion

    #region Public API

    /// <summary>Lights or clears a key (e.g. from incoming MIDI) at velocity 100. UI thread only. Does not raise events.</summary>
    public void SetHighlighted(int note, bool on) => SetHighlighted(note, on ? 100 : 0);

    /// <summary>
    /// Lights a key with a brightness that follows <paramref name="velocity"/> (1..127); 0 clears it.
    /// Feed it note-on velocities from MIDI input or playback. UI thread only. Does not raise events.
    /// </summary>
    public void SetHighlighted(int note, int velocity) => SetHighlighted(note, velocity, null);

    /// <summary>
    /// Lights a key in a specific colour (e.g. the colour of the track playing it); null = <see cref="HighlightKeyBrush"/>.
    /// Velocity 0 clears the key. UI thread only. Does not raise events.
    /// </summary>
    public void SetHighlighted(int note, int velocity, Brush? brush)
    {
        if (note < 0 || note > 127) return;
        byte v = (byte)Math.Clamp(velocity, 0, 127);
        Brush? b = v == 0 ? null : brush;
        if (_highlightVelocity[note] == v && ReferenceEquals(_highlightBrush[note], b)) return;
        _highlightVelocity[note] = v;
        _highlightBrush[note] = b;
        InvalidateVisual();
    }

    /// <summary>Highlight velocity of a key (0 = not lit).</summary>
    public int GetHighlightVelocity(int note) => note is >= 0 and <= 127 ? _highlightVelocity[note] : 0;

    /// <summary>Turns off every key lit with <see cref="SetHighlighted(int, int)"/>.</summary>
    public void ClearHighlights()
    {
        Array.Clear(_highlightVelocity);
        Array.Clear(_highlightBrush);
        Array.Clear(_hand);
        Array.Clear(_finger);
        InvalidateVisual();
    }

    /// <summary>
    /// Replaces every highlight with <paramref name="keys"/> in one redraw: colour, brightness, and the hand and
    /// finger shown in the Grand style. When a note is listed twice the brighter one wins. UI thread only.
    /// </summary>
    public void SetState(IEnumerable<KeyHighlight> keys)
    {
        // Remember the old state, and only redraw when something changed.
        Array.Copy(_highlightVelocity, _prevVelocity, 128);
        Array.Copy(_highlightBrush, _prevBrush, 128);
        Array.Copy(_hand, _prevHand, 128);
        Array.Copy(_finger, _prevFinger, 128);
        Array.Clear(_highlightVelocity);
        Array.Clear(_highlightBrush);
        Array.Clear(_hand);
        Array.Clear(_finger);
        foreach (var k in keys)
        {
            if (k.Note is < 0 or > 127) continue;
            byte v = (byte)Math.Clamp((int)Math.Round(k.Intensity * 127), 1, 127);
            if (_highlightVelocity[k.Note] >= v) continue;
            _highlightVelocity[k.Note] = v;
            _highlightBrush[k.Note] = Kit.Solid(k.Color);
            _hand[k.Note] = (byte)Math.Clamp(k.Hand, 0, 2);
            _finger[k.Note] = (byte)Math.Clamp(k.Finger, 0, 5);
        }
        for (int n = 0; n < 128; n++)
        {
            if (_highlightVelocity[n] != _prevVelocity[n] || !ReferenceEquals(_highlightBrush[n], _prevBrush[n])
                || _hand[n] != _prevHand[n] || _finger[n] != _prevFinger[n])
            {
                InvalidateVisual();
                return;
            }
        }
    }

    private readonly byte[] _prevVelocity = new byte[128], _prevHand = new byte[128], _prevFinger = new byte[128];
    private readonly Brush?[] _prevBrush = new Brush?[128];

    /// <summary>Shows notes <paramref name="low"/> to <paramref name="high"/> (widened to white keys).</summary>
    public void SetRange(int low, int high)
    {
        SetCurrentValue(LowestNoteProperty, Math.Clamp(Math.Min(low, high), 0, 127));
        SetCurrentValue(HighestNoteProperty, Math.Clamp(Math.Max(low, high), 0, 127));
    }

    /// <summary>Releases every note currently held by mouse or computer keyboard (raises NoteOff for each).</summary>
    public void ReleaseAll()
    {
        if (_mouseNote >= 0) Release(ref _mouseNote);
        ReleaseComputerNotes();
    }

    #endregion

    #region Layout

    private (int low, int high) EffectiveRange()
    {
        int low = Math.Min(LowestNote, HighestNote);
        int high = Math.Max(LowestNote, HighestNote);
        if (MidiNoteName.IsBlackKey(low)) low--;      // always start and end on a white key
        if (MidiNoteName.IsBlackKey(high)) high++;
        return (Math.Clamp(low, 0, 127), Math.Clamp(high, 0, 127));
    }

    private void EnsureLayout()
    {
        var (low, high) = EffectiveRange();
        var size = new Size(ActualWidth, ActualHeight);
        if (low == _layoutLow && high == _layoutHigh && size == _layoutSize) return;
        _layoutLow = low;
        _layoutHigh = high;
        _layoutSize = size;

        int whiteCount = 0;
        for (int n = low; n <= high; n++)
            if (!MidiNoteName.IsBlackKey(n)) whiteCount++;
        if (whiteCount == 0 || size.Width <= 0 || size.Height <= 0) return;

        double ww = size.Width / whiteCount;
        double bw = ww * 0.6;
        bool grand = KeyStyle == PianoKeyStyle.Grand;
        // Grand: the felt strip covers the top of the keys, and the black keys are 63 % of the white ones.
        double top = grand ? GrandFelt(size.Height) : 0;
        double whiteH = grand ? Math.Max(0, size.Height - top - 2) : size.Height;
        double bh = grand ? whiteH * 0.63 : size.Height * Math.Clamp(BlackKeyHeightRatio, 0.2, 0.95);
        int whiteIndex = 0;
        for (int n = low; n <= high; n++)
        {
            if (!MidiNoteName.IsBlackKey(n))
            {
                _keyRects[n] = new Rect(whiteIndex * ww, top, ww, whiteH);
                whiteIndex++;
            }
            else
            {
                double x = whiteIndex * ww - bw / 2 + s_blackOffset[n % 12] * bw;
                _keyRects[n] = new Rect(x, top, bw, bh);
            }
        }
    }

    /// <summary>Returns the MIDI note at a point, or -1.</summary>
    public int HitTestNote(Point p)
    {
        EnsureLayout();
        var (low, high) = EffectiveRange();
        for (int n = low; n <= high; n++)
            if (MidiNoteName.IsBlackKey(n) && _keyRects[n].Contains(p)) return n;
        for (int n = low; n <= high; n++)
            if (!MidiNoteName.IsBlackKey(n) && _keyRects[n].Contains(p)) return n;
        return -1;
    }

    #endregion

    #region Rendering

    // Skin-independent shading: translucent black/white over the skin's key brushes, so every skin gets depth.
    private static readonly Brush s_shade = Frozen(new SolidColorBrush(Color.FromArgb(255, 0, 0, 0)));
    private static readonly Brush s_light = Frozen(new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)));
    private static readonly Brush s_pressShadow = Frozen(new LinearGradientBrush(
        Color.FromArgb(90, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), 90));
    private static readonly Brush s_blackTopFace = Frozen(new LinearGradientBrush(
        Color.FromArgb(70, 255, 255, 255), Color.FromArgb(8, 255, 255, 255), 90));

    private static Brush Frozen(Brush b) { b.Freeze(); return b; }

    /// <summary>Opacity of a light overlay for a given velocity.</summary>
    private double LightOpacity(int velocity)
        => VelocitySensitiveLighting ? 0.3 + 0.7 * Math.Clamp(velocity, 1, 127) / 127.0 : 1.0;

    /// <summary>(brush, velocity) of the light on a key, or (null, 0) if unlit. Played-by-user wins over highlight.</summary>
    private (Brush? brush, int velocity) KeyLight(int note)
    {
        int held = HeldVelocity(note);
        if (held > 0) return (PressedKeyBrush, held);
        int hv = _highlightVelocity[note];
        return hv > 0 ? (_highlightBrush[note] ?? HighlightKeyBrush, hv) : (null, 0);
    }

    /// <inheritdoc/>
    protected override void OnRender(DrawingContext dc)
    {
        EnsureLayout();
        var (low, high) = EffectiveRange();
        if (KeyStyle == PianoKeyStyle.Grand)
        {
            RenderGrand(dc, low, high);
            return;
        }
        var snap = new PixelSnapper(this);
        Pen? border = FrozenPens.Create(BorderBrush, snap.OnePixel);
        bool threeD = KeyStyle == PianoKeyStyle.ThreeD;

        if (Background is { } bg)
            dc.DrawRectangle(bg, null, new Rect(0, 0, ActualWidth, ActualHeight));

        for (int n = low; n <= high; n++)
        {
            if (MidiNoteName.IsBlackKey(n)) continue;
            Rect r = _keyRects[n];
            Rect sr = Deflate(snap.Rect(r.X, r.Y, r.Width, r.Height), snap.OnePixel / 2);
            if (threeD) DrawWhiteKey3D(dc, snap, sr, n, border);
            else DrawWhiteKeyFlat(dc, sr, n, border);

            if (ShowOctaveLabels && n % 12 == 0 && r.Width >= 10)
            {
                FormattedText ft = MakeText(MidiNoteName.Get(n, MiddleCOctave), snap.PixelsPerDip,
                    size: Math.Min(FontSize, r.Width * 0.45));
                double lip = threeD ? WhiteLip(sr.Height) : 0;
                dc.DrawText(ft, new Point(r.X + (r.Width - ft.Width) / 2, r.Bottom - lip - ft.Height - 3));
            }
        }

        // Black keys cast a shadow onto the white keys first, then draw on top.
        if (threeD)
        {
            for (int n = low; n <= high; n++)
            {
                if (!MidiNoteName.IsBlackKey(n)) continue;
                Rect r = _keyRects[n];
                bool down = HeldVelocity(n) > 0;
                double dx = r.Width * (down ? 0.08 : 0.18), dy = down ? 1 : 3;
                dc.PushOpacity(down ? 0.22 : 0.32);
                dc.DrawRoundedRectangle(s_shade, null, new Rect(r.X + dx, r.Y, r.Width, r.Height + dy), 2.5, 2.5);
                dc.Pop();
            }
        }

        for (int n = low; n <= high; n++)
        {
            if (!MidiNoteName.IsBlackKey(n)) continue;
            Rect r = _keyRects[n];
            Rect sr = snap.Rect(r.X, r.Y, r.Width, r.Height);
            if (threeD) DrawBlackKey3D(dc, sr, n, border);
            else DrawBlackKeyFlat(dc, sr, n, border);
        }
    }

    private static double WhiteLip(double keyHeight) => Math.Clamp(keyHeight * 0.075, 3, 9);

    private void DrawWhiteKeyFlat(DrawingContext dc, Rect sr, int n, Pen? border)
    {
        dc.DrawRectangle(WhiteKeyBrush, border, sr);
        DrawLight(dc, sr, n, 0);
    }

    private void DrawBlackKeyFlat(DrawingContext dc, Rect sr, int n, Pen? border)
    {
        dc.DrawRoundedRectangle(BlackKeyBrush, border, sr, 2, 2);
        DrawLight(dc, Deflate(sr, 1.5), n, 1.5);
    }

    private void DrawWhiteKey3D(DrawingContext dc, PixelSnapper snap, Rect sr, int n, Pen? border)
    {
        bool down = HeldVelocity(n) > 0;
        double lip = WhiteLip(sr.Height) * (down ? 0.35 : 1);

        dc.DrawRoundedRectangle(WhiteKeyBrush, border, sr, 0, 0);
        if (sr.Width < 4 || sr.Height < 12) { DrawLight(dc, sr, n, 0); return; }

        var face = new Rect(sr.X, sr.Y, sr.Width, Math.Max(0, sr.Height - lip));
        DrawLight(dc, face, n, 0);

        // Left edge catches light, right edge falls into shade.
        double px = snap.OnePixel;
        Fill(dc, s_light, 0.55, new Rect(sr.X + px, sr.Y, px, face.Height));
        Fill(dc, s_shade, 0.10, new Rect(sr.Right - 2 * px, sr.Y, px, face.Height));

        // Front lip (the rounded front edge of the key) – shorter when pressed.
        var lipRect = new Rect(sr.X, face.Bottom, sr.Width, lip);
        Fill(dc, s_shade, down ? 0.22 : 0.13, lipRect);
        Fill(dc, s_shade, 0.22, new Rect(sr.X, face.Bottom, sr.Width, px));
        Fill(dc, s_light, 0.35, new Rect(sr.X, face.Bottom + px, sr.Width, px));

        if (down)
        {
            // Key tilts into the keybed: shadow from the frame at the top, slightly darker overall.
            dc.DrawRectangle(s_pressShadow, null, new Rect(sr.X, sr.Y, sr.Width, Math.Min(face.Height, sr.Height * 0.3)));
            Fill(dc, s_shade, 0.05, face);
        }
    }

    private void DrawBlackKey3D(DrawingContext dc, Rect sr, int n, Pen? border)
    {
        bool down = HeldVelocity(n) > 0;
        dc.DrawRoundedRectangle(BlackKeyBrush, border, sr, 2, 2);
        if (sr.Width < 5 || sr.Height < 12) { DrawLight(dc, Deflate(sr, 1.5), n, 1.5); return; }

        // Sculpted top face, inset from the sides; the strip below it is the sloped front.
        double side = Math.Max(1.2, sr.Width * 0.13);
        double front = sr.Height * (down ? 0.05 : 0.11);
        var top = new Rect(sr.X + side, sr.Y + 1, Math.Max(0, sr.Width - 2 * side), Math.Max(0, sr.Height - front - 1));
        dc.DrawRoundedRectangle(s_blackTopFace, null, top, 1.5, 1.5);
        DrawLight(dc, top, n, 1.5);

        // Bevel highlights: left side and the front slope.
        Fill(dc, s_light, 0.10, new Rect(sr.X + 1, sr.Y + 1, side - 1, top.Height));
        Fill(dc, s_light, down ? 0.04 : 0.12, new Rect(sr.X + 1, top.Bottom, sr.Width - 2, Math.Max(0, sr.Bottom - top.Bottom - 1)));
        Fill(dc, s_light, 0.28, new Rect(top.X, top.Y, Math.Max(0, top.Width), 1));
    }

    /// <summary>Velocity-scaled light (played or highlighted) over the given key face.</summary>
    private void DrawLight(DrawingContext dc, Rect face, int note, double radius)
    {
        var (brush, velocity) = KeyLight(note);
        if (brush is null || face.Width <= 0 || face.Height <= 0) return;
        dc.PushOpacity(LightOpacity(velocity));
        dc.DrawRoundedRectangle(brush, null, face, radius, radius);
        dc.Pop();
    }

    private static void Fill(DrawingContext dc, Brush brush, double opacity, Rect r)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        dc.PushOpacity(opacity);
        dc.DrawRectangle(brush, null, r);
        dc.Pop();
    }

    /// <summary>Velocity the user is holding the key with (mouse or computer keyboard), 0 if not held.</summary>
    private int HeldVelocity(int note)
    {
        if (note == _mouseNote) return Math.Max(1, _mouseVelocity);
        return _computerHeld.ContainsValue(note) ? (FixedVelocity > 0 ? FixedVelocity : ComputerKeyboardVelocity) : 0;
    }

    private const int ComputerKeyboardVelocity = 100;

    private static Rect Deflate(Rect r, double by)
        => r.Width > 2 * by && r.Height > 2 * by ? new Rect(r.X + by, r.Y + by, r.Width - 2 * by, r.Height - 2 * by) : r;

    #endregion

    #region Grand style

    private static double GrandFelt(double height) => Math.Max(4, height * 0.035);

    private static readonly Color s_ivoryEdge = Palette.C("#BDBDB6"), s_ivoryFront = Palette.C("#E2E1DA");

    /// <summary>The lit colour and brightness of a key in the Grand style (user-played wins), or false.</summary>
    private bool GrandLight(int note, out Color color, out double intensity)
    {
        var (brush, velocity) = KeyLight(note);
        color = default;
        intensity = 0;
        if (brush is null || !BrushColors.TryGetSolid(brush, out color)) return false;
        color.A = 255;
        intensity = VelocitySensitiveLighting ? Math.Clamp(velocity, 1, 127) / 127.0 : 1.0;
        return true;
    }

    private void RenderGrand(DrawingContext dc, int low, int high)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 10 || h < 10) return;

        // Case + felt strip
        dc.DrawRectangle(Kit.Linear(Palette.C("#1A1D24"), Palette.C("#0B0C10")), null, new Rect(0, 0, w, h));
        double felt = GrandFelt(h);
        double top = felt;
        double whiteH = h - top - 2;
        double blackH = whiteH * 0.63;

        // ── White keys ──
        for (int n = low; n <= high; n++)
        {
            if (MidiNoteName.IsBlackKey(n)) continue;
            double x = _keyRects[n].X, kw = _keyRects[n].Width;
            bool down = GrandLight(n, out Color hl, out double intensity);
            double lip = down ? whiteH * 0.025 : whiteH * 0.055;
            var body = new Rect(x + 0.6, top, Math.Max(0, kw - 1.2), Math.Max(0, whiteH - lip));
            var front = new Rect(x + 0.6, top + whiteH - lip - 3, Math.Max(0, kw - 1.2), lip + 3);

            if (down)
            {
                var c = Palette.Lerp(Colors.White, hl, 0.35 + 0.65 * intensity);   // full intensity = exactly the key colour
                dc.DrawRoundedRectangle(Kit.Linear(false, (Palette.Darken(c, 0.05), 0), (Palette.Lighten(c, 0.25), 0.5), (Palette.Darken(c, 0.05), 1)),
                                        null, front, 3, 3);
                dc.DrawRoundedRectangle(Kit.Linear(true, (Palette.Darken(c, 0.35), 0), (Palette.Darken(c, 0.05), 0.08), (c, 0.55), (Palette.Lighten(c, 0.2), 1)),
                                        Kit.Pen(Palette.Darken(c, 0.4), 0.8), body, 2.5, 2.5);
                // Inner glow
                dc.DrawRectangle(Kit.Linear(hl.WithAlpha(0.0), hl.WithAlpha(0.35)), null,
                                 new Rect(body.X + 2, body.Bottom - body.Height * 0.4, Math.Max(0, body.Width - 4), Math.Max(0, body.Height * 0.4 - 2)));
            }
            else
            {
                dc.DrawRoundedRectangle(Kit.Linear(false, (s_ivoryEdge, 0), (s_ivoryFront, 0.5), (s_ivoryEdge, 1)), null, front, 3, 3);
                dc.DrawRoundedRectangle(Kit.Linear(true, (Palette.C("#CFCFC8"), 0), (Palette.C("#FBFBF7"), 0.06), (Palette.C("#F4F3EE"), 0.7), (Palette.C("#E6E5DE"), 1)),
                                        Kit.Pen(Palette.C("#9C9C96"), 0.7), body, 2.5, 2.5);
            }

            if (down && kw > 11 && LabelMode != KeyLabelMode.None)
                DrawGrandLabel(dc, n, hl, intensity, x + kw / 2, body.Bottom, kw, black: false);
            else if (ShowOctaveLabels && n % 12 == 0 && kw > 9)
            {
                var t = Kit.Text(MidiNoteName.Get(n, MiddleCOctave), Math.Min(11, kw * 0.42), Palette.C("#8D8D86"), this);
                Kit.DrawCentered(dc, t, new Point(x + kw / 2, body.Bottom - t.Height * 0.9));
            }
        }

        // Shadow cast by the felt / case onto the keys.
        dc.DrawRectangle(Kit.Linear(Colors.Black.WithAlpha(0.45), Colors.Black.WithAlpha(0)), null, new Rect(0, top, w, whiteH * 0.06));

        // ── Black keys ──
        for (int n = low; n <= high; n++)
        {
            if (!MidiNoteName.IsBlackKey(n)) continue;
            double x = _keyRects[n].X, kw = _keyRects[n].Width;
            bool down = GrandLight(n, out Color hl, out double intensity);
            double kh = down ? blackH * 0.985 : blackH;
            dc.DrawRoundedRectangle(Kit.Solid(Colors.Black.WithAlpha(0.35)), null, new Rect(x + 1.5, top, kw, kh + 4), 3, 3);

            var body = new Rect(x, top, kw, kh);
            if (down)
            {
                var c = Palette.Lerp(Palette.C("#202020"), hl, 0.45 + 0.55 * intensity);
                dc.DrawRoundedRectangle(Kit.Linear(true, (Palette.Darken(c, 0.55), 0), (Palette.Darken(c, 0.15), 0.6), (c, 1)),
                                        Kit.Pen(Palette.Darken(c, 0.6), 0.8), body, 2.5, 2.5);
                var cap = new Rect(x + kw * 0.14, top + 2, kw * 0.72, Math.Max(0, kh - kh * 0.1 - 2));
                dc.DrawRoundedRectangle(Kit.Linear(true, (Palette.Darken(c, 0.25), 0), (Palette.Lighten(c, 0.15), 1)), null, cap, 2, 2);
                if (kw > 8 && LabelMode != KeyLabelMode.None)
                    DrawGrandLabel(dc, n, hl, intensity, x + kw / 2, top + kh, kw, black: true);
            }
            else
            {
                dc.DrawRoundedRectangle(Kit.Linear(true, (Palette.C("#0C0C0C"), 0), (Palette.C("#1C1C1C"), 0.85), (Palette.C("#3A3A3A"), 1)),
                                        Kit.Pen(Colors.Black, 0.8), body, 2.5, 2.5);
                // Glossy cap with bevel
                var cap = new Rect(x + kw * 0.14, top + 2, kw * 0.72, Math.Max(0, kh - kh * 0.11 - 2));
                dc.DrawRoundedRectangle(Kit.Linear(true, (Palette.C("#151515"), 0), (Palette.C("#2C2C2C"), 0.75), (Palette.C("#4A4A4A"), 1)), null, cap, 2, 2);
                dc.DrawRectangle(Kit.Linear(Colors.White.WithAlpha(0.18), Colors.White.WithAlpha(0)), null,
                                 new Rect(cap.X + 1, cap.Y + cap.Height * 0.08, cap.Width * 0.3, cap.Height * 0.8));
            }
        }

        // Felt strip on top of everything
        dc.DrawRectangle(Kit.Linear(true, (Palette.C("#5A0C18"), 0), (Palette.C("#8E1A2A"), 0.5), (Palette.C("#3C0710"), 1)), null, new Rect(0, 0, w, felt));
        dc.DrawRectangle(Kit.Solid(Colors.Black.WithAlpha(0.5)), null, new Rect(0, felt - 1, w, 1));
    }

    private void DrawGrandLabel(DrawingContext dc, int n, Color color, double intensity, double cx, double bottom, double kw, bool black)
    {
        if (intensity < 0.2) return;   // very soft notes stay unlabelled
        int finger = _finger[n], hand = _hand[n];
        switch (LabelMode)
        {
            case KeyLabelMode.Fingers when finger > 0:
            {
                // Finger number in a badge of the hand colour, with L / R above it.
                var hc = HandColors.Of(hand);
                double r = Math.Min(black ? 9 : 11, kw * (black ? 0.46 : 0.42));
                var cpt = new Point(cx, bottom - r - (black ? 6 : 8));
                dc.DrawEllipse(Kit.Glow(hc.WithAlpha(0.6)), null, cpt, r * 1.8, r * 1.8);
                dc.DrawEllipse(Kit.Sphere(Palette.Lighten(hc, 0.45), hc), Kit.Pen(Colors.White, 1.4), cpt, r, r);
                Kit.DrawCentered(dc, Kit.Text(finger.ToString(System.Globalization.CultureInfo.InvariantCulture), r * 1.2, Colors.White, this, bold: true), cpt);
                var handText = Kit.Text(hand == 1 ? "L" : "R", Math.Max(8, r * 0.8), black ? Colors.White : Palette.Darken(hc, 0.4), this, bold: true);
                Kit.DrawCentered(dc, handText, new Point(cx, cpt.Y - r - handText.Height * 0.55));
                break;
            }
            case KeyLabelMode.Notes:
            {
                var t = Kit.Text(MidiNoteName.Pretty(n), Math.Min(13, kw * 0.48), black ? Colors.White : Palette.Darken(color, 0.7), this, bold: true);
                Kit.DrawCentered(dc, t, new Point(cx, bottom - t.Height * 0.9));
                break;
            }
        }
    }

    #endregion

    #region Mouse

    private int VelocityAt(int note, Point p)
    {
        if (FixedVelocity > 0) return FixedVelocity;
        Rect r = _keyRects[note];
        double t = r.Height > 0 ? Math.Clamp((p.Y - r.Top) / r.Height, 0, 1) : 0.75;
        return Math.Clamp((int)Math.Round(30 + t * 97), 1, 127);
    }

    /// <inheritdoc/>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        Point p = e.GetPosition(this);
        int note = HitTestNote(p);
        if (note < 0) return;
        CaptureMouse();
        Press(note, VelocityAt(note, p));
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed) return;
        Point p = e.GetPosition(this);
        int note = HitTestNote(p);
        if (note == _mouseNote) return;
        if (_mouseNote >= 0) Release(ref _mouseNote);
        if (note >= 0) Press(note, VelocityAt(note, p));
    }

    /// <inheritdoc/>
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    /// <inheritdoc/>
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_mouseNote >= 0) Release(ref _mouseNote);
    }

    private void Press(int note, int velocity)
    {
        _mouseNote = note;
        _mouseVelocity = velocity;
        RaiseNote(NoteOnEvent, note, velocity);
        InvalidateVisual();
    }

    private void Release(ref int noteField)
    {
        int note = noteField;
        noteField = -1;
        RaiseNote(NoteOffEvent, note, 0);
        InvalidateVisual();
    }

    private void RaiseNote(RoutedEvent evt, int note, int velocity)
        => RaiseEvent(new NoteEventArgs(evt, this, note, velocity));

    #endregion

    #region Computer keyboard

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || !IsComputerKeyboardEnabled || Keyboard.Modifiers != ModifierKeys.None) return;

        if (e.Key == Key.Z) { ShiftOctave(-1); e.Handled = true; return; }
        if (e.Key == Key.X) { ShiftOctave(+1); e.Handled = true; return; }

        int index = Array.IndexOf(s_computerKeys, e.Key);
        if (index < 0) return;
        e.Handled = true;
        if (e.IsRepeat || _computerHeld.ContainsKey(e.Key)) return;

        int note = (ComputerKeyboardOctave + 1) * 12 + index;
        if (note > 127) return;
        _computerHeld[e.Key] = note;
        RaiseNote(NoteOnEvent, note, FixedVelocity > 0 ? FixedVelocity : ComputerKeyboardVelocity);
        InvalidateVisual();
    }

    /// <inheritdoc/>
    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (_computerHeld.Remove(e.Key, out int note))
        {
            RaiseNote(NoteOffEvent, note, 0);
            InvalidateVisual();
            e.Handled = true;
        }
    }

    /// <inheritdoc/>
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        ReleaseComputerNotes(); // never leave hanging notes when focus moves away mid-press
    }

    private void ReleaseComputerNotes()
    {
        if (_computerHeld.Count == 0) return;
        int[] notes = new int[_computerHeld.Count];
        _computerHeld.Values.CopyTo(notes, 0);
        _computerHeld.Clear(); // clear first: NoteOff handlers may re-enter
        foreach (int note in notes)
            RaiseNote(NoteOffEvent, note, 0);
        InvalidateVisual();
    }

    private void ShiftOctave(int delta)
    {
        ReleaseComputerNotes(); // shifting must never strand a note-on
        SetCurrentValue(ComputerKeyboardOctaveProperty, Math.Clamp(ComputerKeyboardOctave + delta, -1, 9));
    }

    #endregion
}
