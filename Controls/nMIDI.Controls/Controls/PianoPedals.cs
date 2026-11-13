using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using nMIDI.Controls.Primitives;

namespace nMIDI.Controls;

/// <summary>The three piano pedals, left to right as on a grand piano.</summary>
public enum PianoPedal
{
    /// <summary>Una corda, CC 67.</summary>
    Soft,
    /// <summary>CC 66.</summary>
    Sostenuto,
    /// <summary>Damper / sustain, CC 64.</summary>
    Sustain,
}

/// <summary>Handles <see cref="PianoPedals.PedalChanged"/>.</summary>
public delegate void PedalEventHandler(object sender, PedalEventArgs e);

/// <summary>Data for <see cref="PianoPedals.PedalChanged"/>.</summary>
public class PedalEventArgs : RoutedEventArgs
{
    /// <summary>Creates a <see cref="PedalEventArgs"/>.</summary>
    public PedalEventArgs(RoutedEvent routedEvent, object source, PianoPedal pedal, int value, bool isUserChange = false)
        : base(routedEvent, source)
    {
        Pedal = pedal;
        Value = value;
        IsUserChange = isUserChange;
    }

    /// <summary>
    /// True when the user moved the pedal with the mouse; false for code and bindings (for example a value that
    /// came in from MIDI). Send the CC only for user changes to avoid echoing incoming messages back out.
    /// </summary>
    public bool IsUserChange { get; }

    /// <summary>The pedal that changed.</summary>
    public PianoPedal Pedal { get; }

    /// <summary>0 (up) … 127 (fully down). Values in between are half-pedalling.</summary>
    public int Value { get; }

    /// <summary>MIDI controller number for <see cref="Pedal"/> (64, 66 or 67).</summary>
    public int Controller => PianoPedals.ControllerOf(Pedal);
}

/// <summary>
/// Compact piano pedal indicator (~50×18 px by default). Each pedal is a small foot-plate glyph that
/// fills from the bottom with its depth, so full and half pedalling read at a glance; a pressed pedal
/// also sinks slightly, like the 3D keyboard. Primarily a display for incoming CC 64/66/67, but also playable:
/// click and hold = press, Ctrl+click = latch on/off, mouse wheel = partial depth, right-click = release.
/// Hide pedals you don't need with <see cref="ShowSoft"/> / <see cref="ShowSostenuto"/>.
/// </summary>
public class PianoPedals : DawRenderControl
{
    private const double Gap = 4;
    private readonly bool[] _latched = new bool[3];
    private int _held = -1;
    private int _hover = -1;

    static PianoPedals()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(PianoPedals), new FrameworkPropertyMetadata(typeof(PianoPedals)));
        FocusableProperty.OverrideMetadata(typeof(PianoPedals), new FrameworkPropertyMetadata(false));
    }

    #region Properties

    /// <summary>Identifies the <see cref="SoftValue"/> dependency property.</summary>
    public static readonly DependencyProperty SoftValueProperty = RegisterPedal(nameof(SoftValue));
    /// <summary>Soft pedal depth 0..127 (CC 67).</summary>
    public int SoftValue { get => (int)GetValue(SoftValueProperty); set => SetValue(SoftValueProperty, value); }

    /// <summary>Identifies the <see cref="SostenutoValue"/> dependency property.</summary>
    public static readonly DependencyProperty SostenutoValueProperty = RegisterPedal(nameof(SostenutoValue));
    /// <summary>Sostenuto depth 0..127 (CC 66).</summary>
    public int SostenutoValue { get => (int)GetValue(SostenutoValueProperty); set => SetValue(SostenutoValueProperty, value); }

    /// <summary>Identifies the <see cref="SustainValue"/> dependency property.</summary>
    public static readonly DependencyProperty SustainValueProperty = RegisterPedal(nameof(SustainValue));
    /// <summary>Sustain depth 0..127 (CC 64). Feed incoming CC 64 straight into this.</summary>
    public int SustainValue { get => (int)GetValue(SustainValueProperty); set => SetValue(SustainValueProperty, value); }

    private static DependencyProperty RegisterPedal(string name) => DependencyProperty.Register(
        name, typeof(int), typeof(PianoPedals),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnPedalValueChanged, (_, v) => Math.Clamp((int)v, 0, 127)));

    /// <summary>Identifies the <see cref="ShowSoft"/> dependency property.</summary>
    public static readonly DependencyProperty ShowSoftProperty = DependencyProperty.Register(
        nameof(ShowSoft), typeof(bool), typeof(PianoPedals), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));
    /// <summary>Whether the soft (una corda) pedal is shown.</summary>
    public bool ShowSoft { get => (bool)GetValue(ShowSoftProperty); set => SetValue(ShowSoftProperty, value); }

    /// <summary>Identifies the <see cref="ShowSostenuto"/> dependency property.</summary>
    public static readonly DependencyProperty ShowSostenutoProperty = DependencyProperty.Register(
        nameof(ShowSostenuto), typeof(bool), typeof(PianoPedals), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));
    /// <summary>Many digital pianos have no sostenuto; hide it to save space.</summary>
    public bool ShowSostenuto { get => (bool)GetValue(ShowSostenutoProperty); set => SetValue(ShowSostenutoProperty, value); }

    /// <summary>Identifies the <see cref="IsInteractive"/> dependency property.</summary>
    public static readonly DependencyProperty IsInteractiveProperty = DependencyProperty.Register(
        nameof(IsInteractive), typeof(bool), typeof(PianoPedals), new FrameworkPropertyMetadata(true));
    /// <summary>False = display only (ignores the mouse).</summary>
    public bool IsInteractive { get => (bool)GetValue(IsInteractiveProperty); set => SetValue(IsInteractiveProperty, value); }

    /// <summary>Identifies the <see cref="PedalBrush"/> dependency property.</summary>
    public static readonly DependencyProperty PedalBrushProperty = RegisterBrush(nameof(PedalBrush), typeof(PianoPedals));
    /// <summary>Pedal plate at rest.</summary>
    public Brush? PedalBrush { get => (Brush?)GetValue(PedalBrushProperty); set => SetValue(PedalBrushProperty, value); }

    /// <summary>Identifies the <see cref="LitBrush"/> dependency property.</summary>
    public static readonly DependencyProperty LitBrushProperty = RegisterBrush(nameof(LitBrush), typeof(PianoPedals));
    /// <summary>Depth fill / pressed colour.</summary>
    public Brush? LitBrush { get => (Brush?)GetValue(LitBrushProperty); set => SetValue(LitBrushProperty, value); }

    /// <summary>Identifies the <see cref="PedalChanged"/> routed event.</summary>
    public static readonly RoutedEvent PedalChangedEvent = EventManager.RegisterRoutedEvent(
        nameof(PedalChanged), RoutingStrategy.Bubble, typeof(PedalEventHandler), typeof(PianoPedals));

    /// <summary>
    /// Raised whenever a pedal value changes (user or binding). Send the CC from here when
    /// <see cref="PedalEventArgs.IsUserChange"/> is true.
    /// </summary>
    public event PedalEventHandler PedalChanged { add => AddHandler(PedalChangedEvent, value); remove => RemoveHandler(PedalChangedEvent, value); }

    private static void OnPedalValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var p = (PianoPedals)d;
        PianoPedal pedal = e.Property == SoftValueProperty ? PianoPedal.Soft
            : e.Property == SostenutoValueProperty ? PianoPedal.Sostenuto : PianoPedal.Sustain;
        if ((int)e.NewValue == 0) p._latched[(int)pedal] = false;
        p.RaiseEvent(new PedalEventArgs(PedalChangedEvent, p, pedal, (int)e.NewValue, p._userChange));
        // _hover is a slot index; map it (slots shift when Soft/Sostenuto are hidden).
        if (p._hover >= 0 && p.VisiblePedal(p._hover) == pedal) p.UpdateToolTip();
    }

    #endregion

    #region Public API

    /// <summary>MIDI controller number of a pedal: 64, 66 or 67.</summary>
    public static int ControllerOf(PianoPedal pedal) => pedal switch
    {
        PianoPedal.Soft => 67,
        PianoPedal.Sostenuto => 66,
        _ => 64,
    };

    /// <summary>The value of a pedal, 0 (up) to 127 (down).</summary>
    public int GetPedal(PianoPedal pedal) => pedal switch
    {
        PianoPedal.Soft => SoftValue,
        PianoPedal.Sostenuto => SostenutoValue,
        _ => SustainValue,
    };

    /// <summary>Sets a pedal, 0 (up) to 127 (down), as a value from code: <see cref="PedalEventArgs.IsUserChange"/> is false.</summary>
    public void SetPedal(PianoPedal pedal, int value) => SetCurrentValue(PropertyOf(pedal), Math.Clamp(value, 0, 127));

    private bool _userChange;

    private void SetPedalByUser(PianoPedal pedal, int value)
    {
        _userChange = true;
        try { SetPedal(pedal, value); }
        finally { _userChange = false; }
    }

    /// <summary>Applies an incoming MIDI controller message; returns false if it isn't a pedal CC.</summary>
    public bool ApplyControlChange(int controller, int value)
    {
        switch (controller)
        {
            case 64: SetPedal(PianoPedal.Sustain, value); return true;
            case 66: SetPedal(PianoPedal.Sostenuto, value); return true;
            case 67: SetPedal(PianoPedal.Soft, value); return true;
            default: return false;
        }
    }

    private static DependencyProperty PropertyOf(PianoPedal pedal) => pedal switch
    {
        PianoPedal.Soft => SoftValueProperty,
        PianoPedal.Sostenuto => SostenutoValueProperty,
        _ => SustainValueProperty,
    };

    #endregion

    #region Layout

    private int VisibleCount => 1 + (ShowSoft ? 1 : 0) + (ShowSostenuto ? 1 : 0);

    private PianoPedal VisiblePedal(int slot)
    {
        if (ShowSoft)
        {
            if (slot == 0) return PianoPedal.Soft;
            slot--;
        }
        if (ShowSostenuto)
        {
            if (slot == 0) return PianoPedal.Sostenuto;
        }
        return PianoPedal.Sustain;
    }

    /// <summary>Rects of the visible pedal slots, centred as a group. Pedals never get wider than they are tall.</summary>
    private Rect SlotRect(int slot)
    {
        int n = VisibleCount;
        double h = ActualHeight;
        double w = Math.Min((ActualWidth - Gap * (n - 1)) / n, h * 0.9);
        if (w <= 0 || h <= 0) return Rect.Empty;
        double total = n * w + (n - 1) * Gap;
        double x0 = (ActualWidth - total) / 2;
        return new Rect(x0 + slot * (w + Gap), 0, w, h);
    }

    private int HitTestSlot(Point p)
    {
        for (int i = 0; i < VisibleCount; i++)
        {
            Rect r = SlotRect(i);
            if (!r.IsEmpty && new Rect(r.X - Gap / 2, r.Y, r.Width + Gap, r.Height).Contains(p)) return i;
        }
        return -1;
    }

    #endregion

    #region Rendering

    private static readonly Brush s_shade = Freeze(new SolidColorBrush(Colors.Black));
    private static readonly Brush s_light = Freeze(new SolidColorBrush(Colors.White));

    private static Brush Freeze(Brush b) { b.Freeze(); return b; }

    /// <inheritdoc/>
    protected override void OnRender(DrawingContext dc)
    {
        if (Background is { } bg) dc.DrawRectangle(bg, null, new Rect(0, 0, ActualWidth, ActualHeight));
        var snap = new PixelSnapper(this);
        Pen? outline = FrozenPens.Create(BorderBrush, snap.OnePixel);

        for (int slot = 0; slot < VisibleCount; slot++)
        {
            Rect r = SlotRect(slot);
            if (r.IsEmpty) continue;
            PianoPedal pedal = VisiblePedal(slot);
            int value = GetPedal(pedal);
            double depth = value / 127.0;

            // Glyph: thin rod at the top, rounded foot plate below. The plate sinks up to 2 px with depth.
            double sink = Math.Round(depth * 2);
            double rodW = Math.Max(2, r.Width * 0.22);
            double rodH = r.Height * 0.28;
            var rod = snap.Rect(r.X + (r.Width - rodW) / 2, r.Y, rodW, rodH + sink);
            var foot = snap.Rect(r.X, r.Y + rodH + sink, r.Width, r.Height - rodH - sink);
            double radius = Math.Min(3, foot.Height / 3);

            dc.DrawRectangle(PedalBrush, null, rod);
            dc.DrawRoundedRectangle(PedalBrush, outline, foot, radius, radius);

            // Depth fill from the bottom: half pedal = half full.
            if (depth > 0 && LitBrush is { } lit)
            {
                double fh = Math.Max(snap.OnePixel * 2, foot.Height * depth);
                var fill = new Rect(foot.X, foot.Bottom - fh, foot.Width, fh);
                dc.PushClip(new RectangleGeometry(foot, radius, radius));
                dc.DrawRectangle(lit, null, fill);
                dc.Pop();
                if (value >= 127) dc.DrawRectangle(lit, null, rod);
            }

            // Top highlight + front shadow for a little relief (skin-independent).
            Push(dc, s_light, 0.25, new Rect(foot.X + radius / 2, foot.Y + snap.OnePixel, Math.Max(0, foot.Width - radius), snap.OnePixel));
            Push(dc, s_shade, 0.25, new Rect(foot.X + radius / 2, foot.Bottom - 2 * snap.OnePixel, Math.Max(0, foot.Width - radius), snap.OnePixel));

            // Latched marker: small dot above the plate.
            if (_latched[(int)pedal] && LitBrush is { } dot)
                dc.DrawEllipse(dot, null, new Point(r.X + r.Width - 2.5, r.Y + 2.5), 1.6, 1.6);

            if (slot == _hover && IsInteractive && IsMouseOver)
                Push(dc, s_light, 0.08, foot);
        }
    }

    private static void Push(DrawingContext dc, Brush b, double opacity, Rect r)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        dc.PushOpacity(opacity);
        dc.DrawRectangle(b, null, r);
        dc.Pop();
    }

    #endregion

    #region Input

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int slot = HitTestSlot(e.GetPosition(this));
        if (slot != _hover)
        {
            _hover = slot;
            UpdateToolTip();
            InvalidateVisual();
        }
    }

    /// <inheritdoc/>
    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        InvalidateVisual();
    }

    private void UpdateToolTip()
    {
        if (_hover < 0) { SetAutoToolTip(null); return; }
        PianoPedal pedal = VisiblePedal(_hover);
        int v = GetPedal(pedal);
        string state = v == 0 ? "up" : v >= 127 ? "down" : (v * 100 / 127).ToString(CultureInfo.CurrentCulture) + "%";
        SetAutoToolTip($"{pedal} (CC {ControllerOf(pedal)}) · {state}");
    }

    /// <inheritdoc/>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!IsInteractive) return;
        int slot = HitTestSlot(e.GetPosition(this));
        if (slot < 0) return;
        PianoPedal pedal = VisiblePedal(slot);

        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            bool on = !_latched[(int)pedal];
            SetPedalByUser(pedal, on ? 127 : 0);
            _latched[(int)pedal] = on; // after SetValue: a 0 clears the latch in the change callback
            InvalidateVisual();
        }
        else if (CaptureMouse())
        {
            _held = (int)pedal;
            SetPedalByUser(pedal, 127);
        }
        e.Handled = true;
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
        if (_held < 0) return;
        var pedal = (PianoPedal)_held;
        _held = -1;
        if (!_latched[(int)pedal]) SetPedalByUser(pedal, 0); // momentary: lift on release
    }

    /// <inheritdoc/>
    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        if (!IsInteractive) return;
        int slot = HitTestSlot(e.GetPosition(this));
        if (slot < 0) return;
        SetPedalByUser(VisiblePedal(slot), 0);
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!IsInteractive) return;
        int slot = HitTestSlot(e.GetPosition(this));
        if (slot < 0) return;
        PianoPedal pedal = VisiblePedal(slot);
        int step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 1 : 8;
        int v = Math.Clamp(GetPedal(pedal) + Math.Sign(e.Delta) * step, 0, 127);
        SetPedalByUser(pedal, v);
        _latched[(int)pedal] = v > 0; // partial depth from the wheel stays put
        e.Handled = true;
    }

    #endregion
}
