using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using nMIDI.Controls.Primitives;

namespace nMIDI.Controls;

/// <summary>
/// Peak/RMS level meter with ballistics (instant attack, dB/s release), peak hold and a latching clip LED.
/// <para>
/// Two ways to feed it:
/// <list type="number">
/// <item>Bind <see cref="Peak"/> / <see cref="Rms"/> (linear gain, 1.0 = 0 dBFS) on the UI thread.</item>
/// <item>Call <see cref="PushLevels"/> from ANY thread (e.g. the audio callback). It is lock-free and only stores the
/// maximum since the last frame; the meter picks it up on the next render tick. No Dispatcher flooding.</item>
/// </list>
/// </para>
/// Click the clip LED to reset it.
/// </summary>
public class LevelMeter : DawRenderControl
{
    // Pending values written by PushLevels (float bits, max-since-last-frame).
    private int _pendingPeakBits;
    private int _pendingRmsBits;

    private double _displayPeakDb = double.NegativeInfinity;
    private double _displayRmsDb = double.NegativeInfinity;
    private double _holdDb = double.NegativeInfinity;
    private double _holdAge;
    private TimeSpan _lastTick = TimeSpan.Zero;
    private volatile bool _ticking; // false while hidden/unloaded: PushLevels is ignored

    static LevelMeter()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(LevelMeter), new FrameworkPropertyMetadata(typeof(LevelMeter)));
        // A meter is display-only; keep it out of the tab order.
        FocusableProperty.OverrideMetadata(typeof(LevelMeter), new FrameworkPropertyMetadata(false));
    }

    /// <summary>Creates a <see cref="LevelMeter"/>.</summary>
    public LevelMeter()
    {
        Loaded += (_, _) => UpdateTicking();
        Unloaded += (_, _) => StopTicking();
        IsVisibleChanged += (_, _) => UpdateTicking();
    }

    #region Properties

    /// <summary>Identifies the <see cref="Peak"/> dependency property.</summary>
    public static readonly DependencyProperty PeakProperty = DependencyProperty.Register(
        nameof(Peak), typeof(double), typeof(LevelMeter), new FrameworkPropertyMetadata(0.0));

    /// <summary>Current peak as linear gain (1.0 = 0 dBFS).</summary>
    public double Peak
    {
        get => (double)GetValue(PeakProperty);
        set => SetValue(PeakProperty, value);
    }

    /// <summary>Identifies the <see cref="Rms"/> dependency property.</summary>
    public static readonly DependencyProperty RmsProperty = DependencyProperty.Register(
        nameof(Rms), typeof(double), typeof(LevelMeter), new FrameworkPropertyMetadata(0.0));

    /// <summary>Current RMS as linear gain.</summary>
    public double Rms
    {
        get => (double)GetValue(RmsProperty);
        set => SetValue(RmsProperty, value);
    }

    /// <summary>Identifies the <see cref="Orientation"/> dependency property.</summary>
    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        nameof(Orientation), typeof(Orientation), typeof(LevelMeter),
        new FrameworkPropertyMetadata(Orientation.Vertical, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Vertical (default) or horizontal.</summary>
    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>Identifies the <see cref="MinDb"/> dependency property.</summary>
    public static readonly DependencyProperty MinDbProperty = DependencyProperty.Register(
        nameof(MinDb), typeof(double), typeof(LevelMeter),
        new FrameworkPropertyMetadata(-60.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Level in dB at the bottom of the meter.</summary>
    public double MinDb
    {
        get => (double)GetValue(MinDbProperty);
        set => SetValue(MinDbProperty, value);
    }

    /// <summary>Identifies the <see cref="MaxDb"/> dependency property.</summary>
    public static readonly DependencyProperty MaxDbProperty = DependencyProperty.Register(
        nameof(MaxDb), typeof(double), typeof(LevelMeter),
        new FrameworkPropertyMetadata(6.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Level in dB at the top of the meter.</summary>
    public double MaxDb
    {
        get => (double)GetValue(MaxDbProperty);
        set => SetValue(MaxDbProperty, value);
    }

    /// <summary>Identifies the <see cref="WarningDb"/> dependency property.</summary>
    public static readonly DependencyProperty WarningDbProperty = DependencyProperty.Register(
        nameof(WarningDb), typeof(double), typeof(LevelMeter),
        new FrameworkPropertyMetadata(-12.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Level in dB from which the meter shows the warning colour.</summary>
    public double WarningDb
    {
        get => (double)GetValue(WarningDbProperty);
        set => SetValue(WarningDbProperty, value);
    }

    /// <summary>Identifies the <see cref="DangerDb"/> dependency property.</summary>
    public static readonly DependencyProperty DangerDbProperty = DependencyProperty.Register(
        nameof(DangerDb), typeof(double), typeof(LevelMeter),
        new FrameworkPropertyMetadata(-3.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Level in dB from which the meter shows the danger colour.</summary>
    public double DangerDb
    {
        get => (double)GetValue(DangerDbProperty);
        set => SetValue(DangerDbProperty, value);
    }

    /// <summary>Identifies the <see cref="ReleaseRate"/> dependency property.</summary>
    public static readonly DependencyProperty ReleaseRateProperty = DependencyProperty.Register(
        nameof(ReleaseRate), typeof(double), typeof(LevelMeter), new FrameworkPropertyMetadata(26.0));

    /// <summary>Fall-back speed of the bar in dB per second.</summary>
    public double ReleaseRate
    {
        get => (double)GetValue(ReleaseRateProperty);
        set => SetValue(ReleaseRateProperty, value);
    }

    /// <summary>Identifies the <see cref="PeakHoldTime"/> dependency property.</summary>
    public static readonly DependencyProperty PeakHoldTimeProperty = DependencyProperty.Register(
        nameof(PeakHoldTime), typeof(double), typeof(LevelMeter), new FrameworkPropertyMetadata(1.5));

    /// <summary>Seconds the peak-hold line stays before falling. Use a large value for infinite hold, 0 to disable.</summary>
    public double PeakHoldTime
    {
        get => (double)GetValue(PeakHoldTimeProperty);
        set => SetValue(PeakHoldTimeProperty, value);
    }

    /// <summary>Identifies the <see cref="PeakHoldFallRate"/> dependency property.</summary>
    public static readonly DependencyProperty PeakHoldFallRateProperty = DependencyProperty.Register(
        nameof(PeakHoldFallRate), typeof(double), typeof(LevelMeter), new FrameworkPropertyMetadata(15.0));

    /// <summary>Speed at which the held peak falls once its hold time is over, in dB per second.</summary>
    public double PeakHoldFallRate
    {
        get => (double)GetValue(PeakHoldFallRateProperty);
        set => SetValue(PeakHoldFallRateProperty, value);
    }

    /// <summary>Identifies the <see cref="ShowRms"/> dependency property.</summary>
    public static readonly DependencyProperty ShowRmsProperty = DependencyProperty.Register(
        nameof(ShowRms), typeof(bool), typeof(LevelMeter),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Draws RMS as a solid bar inside a translucent peak bar.</summary>
    public bool ShowRms
    {
        get => (bool)GetValue(ShowRmsProperty);
        set => SetValue(ShowRmsProperty, value);
    }

    /// <summary>Identifies the <see cref="SegmentSize"/> dependency property.</summary>
    public static readonly DependencyProperty SegmentSizeProperty = DependencyProperty.Register(
        nameof(SegmentSize), typeof(double), typeof(LevelMeter),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>LED-ladder look: segment length in DIPs (1px gap). 0 = continuous bar.</summary>
    public double SegmentSize
    {
        get => (double)GetValue(SegmentSizeProperty);
        set => SetValue(SegmentSizeProperty, value);
    }

    /// <summary>Identifies the <see cref="ShowClipIndicator"/> dependency property.</summary>
    public static readonly DependencyProperty ShowClipIndicatorProperty = DependencyProperty.Register(
        nameof(ShowClipIndicator), typeof(bool), typeof(LevelMeter),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Whether the clip LED is shown at the top (or right).</summary>
    public bool ShowClipIndicator
    {
        get => (bool)GetValue(ShowClipIndicatorProperty);
        set => SetValue(ShowClipIndicatorProperty, value);
    }

    /// <summary>Identifies the <see cref="ClipIndicatorSize"/> dependency property.</summary>
    public static readonly DependencyProperty ClipIndicatorSizeProperty = DependencyProperty.Register(
        nameof(ClipIndicatorSize), typeof(double), typeof(LevelMeter),
        new FrameworkPropertyMetadata(5.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Size of the clip LED in pixels.</summary>
    public double ClipIndicatorSize
    {
        get => (double)GetValue(ClipIndicatorSizeProperty);
        set => SetValue(ClipIndicatorSizeProperty, value);
    }

    /// <summary>Identifies the <see cref="IsClipped"/> dependency property.</summary>
    public static readonly DependencyProperty IsClippedProperty = DependencyProperty.Register(
        nameof(IsClipped), typeof(bool), typeof(LevelMeter),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>Latched when a peak reaches 0 dBFS. Set false (or click the LED) to reset.</summary>
    public bool IsClipped
    {
        get => (bool)GetValue(IsClippedProperty);
        set => SetValue(IsClippedProperty, value);
    }

    /// <summary>Identifies the <see cref="SafeBrush"/> dependency property.</summary>
    public static readonly DependencyProperty SafeBrushProperty = RegisterBrush(nameof(SafeBrush), typeof(LevelMeter));
    /// <summary>Brush for the part of the meter below WarningDb.</summary>
    public Brush? SafeBrush { get => (Brush?)GetValue(SafeBrushProperty); set => SetValue(SafeBrushProperty, value); }

    /// <summary>Identifies the <see cref="WarningBrush"/> dependency property.</summary>
    public static readonly DependencyProperty WarningBrushProperty = RegisterBrush(nameof(WarningBrush), typeof(LevelMeter));
    /// <summary>Brush for the part of the meter between WarningDb and DangerDb.</summary>
    public Brush? WarningBrush { get => (Brush?)GetValue(WarningBrushProperty); set => SetValue(WarningBrushProperty, value); }

    /// <summary>Identifies the <see cref="DangerBrush"/> dependency property.</summary>
    public static readonly DependencyProperty DangerBrushProperty = RegisterBrush(nameof(DangerBrush), typeof(LevelMeter));
    /// <summary>Brush for the part of the meter above DangerDb.</summary>
    public Brush? DangerBrush { get => (Brush?)GetValue(DangerBrushProperty); set => SetValue(DangerBrushProperty, value); }

    /// <summary>Identifies the <see cref="PeakHoldBrush"/> dependency property.</summary>
    public static readonly DependencyProperty PeakHoldBrushProperty = RegisterBrush(nameof(PeakHoldBrush), typeof(LevelMeter));
    /// <summary>Brush for the held-peak line.</summary>
    public Brush? PeakHoldBrush { get => (Brush?)GetValue(PeakHoldBrushProperty); set => SetValue(PeakHoldBrushProperty, value); }

    /// <summary>Identifies the <see cref="ClipOnBrush"/> dependency property.</summary>
    public static readonly DependencyProperty ClipOnBrushProperty = RegisterBrush(nameof(ClipOnBrush), typeof(LevelMeter));
    /// <summary>Brush for the clip LED while lit.</summary>
    public Brush? ClipOnBrush { get => (Brush?)GetValue(ClipOnBrushProperty); set => SetValue(ClipOnBrushProperty, value); }

    /// <summary>Identifies the <see cref="ClipOffBrush"/> dependency property.</summary>
    public static readonly DependencyProperty ClipOffBrushProperty = RegisterBrush(nameof(ClipOffBrush), typeof(LevelMeter));
    /// <summary>Brush for the clip LED while off.</summary>
    public Brush? ClipOffBrush { get => (Brush?)GetValue(ClipOffBrushProperty); set => SetValue(ClipOffBrushProperty, value); }

    #endregion

    #region Thread-safe feeding

    /// <summary>
    /// Thread-safe, lock-free. Call from the audio thread with the block's peak and RMS (linear gain).
    /// Multiple pushes per frame are merged by taking the maximum.
    /// </summary>
    public void PushLevels(float peak, float rms)
    {
        if (!_ticking) return; // hidden: don't even touch the shared fields
        StoreMax(ref _pendingPeakBits, peak);
        StoreMax(ref _pendingRmsBits, rms);
    }

    private static void StoreMax(ref int location, float value)
    {
        value = Math.Abs(value);
        if (!float.IsFinite(value)) return;
        int initial;
        do
        {
            initial = Volatile.Read(ref location);
            if (BitConverter.Int32BitsToSingle(initial) >= value) return;
        }
        while (Interlocked.CompareExchange(ref location, BitConverter.SingleToInt32Bits(value), initial) != initial);
    }

    private static double TakePending(ref int location)
        => BitConverter.Int32BitsToSingle(Interlocked.Exchange(ref location, 0)); // 0 bits == 0.0f

    /// <summary>Clears ballistics, peak hold and the clip LED.</summary>
    public void Reset()
    {
        TakePending(ref _pendingPeakBits);
        TakePending(ref _pendingRmsBits);
        _displayPeakDb = _displayRmsDb = _holdDb = double.NegativeInfinity;
        _holdAge = 0;
        SetCurrentValue(IsClippedProperty, false);
        InvalidateVisual();
    }

    #endregion

    #region Animation loop

    private void UpdateTicking()
    {
        if (IsLoaded && IsVisible) StartTicking();
        else StopTicking();
    }

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
        TimeSpan now = e is RenderingEventArgs re ? re.RenderingTime : TimeSpan.FromTicks(Environment.TickCount64 * TimeSpan.TicksPerMillisecond);
        if (now == _lastTick) return; // Rendering can fire more than once per frame
        double dt = _lastTick == TimeSpan.Zero ? 1.0 / 60 : Math.Min((now - _lastTick).TotalSeconds, 0.25);
        _lastTick = now;

        double peakGain = Math.Max(Finite(Peak), Finite(TakePending(ref _pendingPeakBits)));
        double rmsGain = Math.Max(Finite(Rms), Finite(TakePending(ref _pendingRmsBits)));

        if (peakGain >= 1.0 && !IsClipped)
            SetCurrentValue(IsClippedProperty, true);

        bool changed = false;
        changed |= Ballistics(ref _displayPeakDb, DbMath.GainToDb(peakGain), dt);
        changed |= Ballistics(ref _displayRmsDb, DbMath.GainToDb(rmsGain), dt);

        // Peak hold
        double holdTime = PeakHoldTime;
        if (holdTime > 0)
        {
            if (_displayPeakDb >= _holdDb)
            {
                changed |= _displayPeakDb != _holdDb;
                _holdDb = _displayPeakDb;
                _holdAge = 0;
            }
            else
            {
                _holdAge += dt;
                if (_holdAge > holdTime && !double.IsNegativeInfinity(_holdDb))
                {
                    _holdDb = Math.Max(_displayPeakDb, _holdDb - PeakHoldFallRate * dt);
                    if (_holdDb < MinDb - 1) _holdDb = double.NegativeInfinity;
                    changed = true;
                }
            }
        }

        if (changed) InvalidateVisual();
    }

    /// <summary>A gain the meter can show: NaN and infinities (a 0/0 from a view-model, say) count as silence.</summary>
    private static double Finite(double gain) => double.IsFinite(gain) ? Math.Abs(gain) : 0;

    private bool Ballistics(ref double displayDb, double targetDb, double dt)
    {
        if (double.IsNaN(targetDb) || double.IsPositiveInfinity(targetDb)) targetDb = double.NegativeInfinity;
        if (double.IsNaN(displayDb)) displayDb = double.NegativeInfinity;
        double before = displayDb;
        if (targetDb >= displayDb)
        {
            displayDb = targetDb; // instant attack
        }
        else
        {
            displayDb = Math.Max(targetDb, displayDb - ReleaseRate * dt);
            if (displayDb < MinDb - 1) displayDb = double.NegativeInfinity;
        }
        if (double.IsNegativeInfinity(before) && double.IsNegativeInfinity(displayDb)) return false;
        return Math.Abs(displayDb - before) > 0.01 || double.IsNegativeInfinity(before) != double.IsNegativeInfinity(displayDb);
    }

    #endregion

    #region Rendering

    private double DbToFraction(double db)
    {
        double range = MaxDb - MinDb;
        if (range <= 0 || double.IsNegativeInfinity(db)) return 0;
        return Math.Clamp((db - MinDb) / range, 0, 1);
    }

    /// <summary>Bar area (excluding the clip LED) in local coordinates.</summary>
    private Rect GetBarRect(out Rect clipRect)
    {
        double w = ActualWidth, h = ActualHeight;
        double clip = ShowClipIndicator ? Math.Max(0, ClipIndicatorSize) : 0;
        double gap = clip > 0 ? 2 : 0;
        if (Orientation == Orientation.Vertical)
        {
            clipRect = new Rect(0, 0, w, Math.Min(clip, h));
            return new Rect(0, Math.Min(h, clip + gap), w, Math.Max(0, h - clip - gap));
        }
        clipRect = new Rect(Math.Max(0, w - clip), 0, Math.Min(clip, w), h);
        return new Rect(0, 0, Math.Max(0, w - clip - gap), h);
    }

    /// <inheritdoc/>
    protected override void OnRender(DrawingContext dc)
    {
        var snap = new PixelSnapper(this);
        Rect bar = GetBarRect(out Rect clipRect);
        bool vertical = Orientation == Orientation.Vertical;

        if (Background is { } bg)
        {
            // The meter well: a sunken nine-patch (plain rectangle for brushes a nine-patch can't express).
            if (NineFrame.TryFrame(bg, null, 0, 1, FrameStyle.Sunken, out var well))
                NinePainter.Draw(dc, this, well, snap.Rect(bar.X, bar.Y, bar.Width, bar.Height));
            else
                dc.DrawRectangle(bg, null, snap.Rect(bar.X, bar.Y, bar.Width, bar.Height));
        }

        if (ShowClipIndicator && clipRect.Width > 0 && clipRect.Height > 0)
        {
            Brush? clipBrush = IsClipped ? ClipOnBrush : ClipOffBrush;
            if (clipBrush is not null)
                dc.DrawRectangle(clipBrush, null, snap.Rect(clipRect.X, clipRect.Y, clipRect.Width, clipRect.Height));
        }

        if (bar.Width <= 0 || bar.Height <= 0) return;

        double fw = DbToFraction(WarningDb);
        double fd = DbToFraction(DangerDb);
        double peakF = DbToFraction(_displayPeakDb);
        double rmsF = DbToFraction(_displayRmsDb);

        if (SegmentSize > 0)
        {
            DrawLadder(dc, snap, bar, vertical, peakF, ShowRms ? rmsF : peakF, fw, fd);
        }
        else if (ShowRms)
        {
            dc.PushOpacity(0.42);
            DrawBar(dc, snap, bar, vertical, peakF, fw, fd);
            dc.Pop();
            DrawBar(dc, snap, bar, vertical, rmsF, fw, fd);
        }
        else
        {
            DrawBar(dc, snap, bar, vertical, peakF, fw, fd);
        }

        // Peak hold line
        if (PeakHoldTime > 0 && PeakHoldBrush is { } holdBrush && !double.IsNegativeInfinity(_holdDb))
        {
            double hf = DbToFraction(_holdDb);
            if (hf > 0)
            {
                double px = snap.OnePixel * 2;
                Rect r = vertical
                    ? snap.Rect(bar.X, bar.Bottom - hf * bar.Height, bar.Width, px)
                    : snap.Rect(bar.X + hf * bar.Width - px, bar.Y, px, bar.Height);
                dc.DrawRectangle(holdBrush, null, r);
            }
        }
    }

    private NineBatch? _ladderBatch;   // only LED-ladder mode uses it
    private NineBatch _batch => _ladderBatch ??= new();

    /// <summary>
    /// LED-ladder mode: every segment is a small nine-patch LED (lit, peak-only or unlit), all rendered in one
    /// native batch call per frame instead of one WPF rectangle per segment.
    /// </summary>
    private void DrawLadder(DrawingContext dc, PixelSnapper snap, Rect bar, bool vertical, double peakF, double rmsF, double fw, double fd)
    {
        double segment = SegmentSize, pitch = segment + 1;
        double length = vertical ? bar.Height : bar.Width;
        int count = Math.Max(1, (int)(length / pitch));
        _batch.Begin(this, RenderSize);
        for (int i = 0; i < count; i++)
        {
            double f0 = i * pitch / length;
            if (!BrushColors.TryGetSolid(ZoneBrush(f0, fw, fd), out Color c)) continue;
            Rect r = AxisRect(snap, bar, vertical, i * pitch, segment);
            if (f0 < rmsF)
                _batch.Add(new FrameSpec(c, c, Color.FromArgb(0x70, 0, 0, 0), 1, 1, FrameStyle.Glow), r);
            else if (f0 < peakF)
                _batch.Add(FrameSpec.Solid(Color.FromArgb((byte)(c.A * 0.42), c.R, c.G, c.B), Color.FromArgb(0x50, 0, 0, 0), 1), r);
            else
                _batch.Add(FrameSpec.Solid(Color.FromArgb((byte)(c.A * 0.1), c.R, c.G, c.B), Color.FromArgb(0x40, 0, 0, 0), 1), r);
        }
        _batch.Render(dc);
    }

    private void DrawBar(DrawingContext dc, PixelSnapper snap, Rect bar, bool vertical, double fraction, double fw, double fd)
    {
        if (fraction <= 0) return;
        double segment = SegmentSize;
        double length = vertical ? bar.Height : bar.Width;

        if (segment > 0)
        {
            double pitch = segment + 1;
            int count = Math.Max(1, (int)(length / pitch));
            for (int i = 0; i < count; i++)
            {
                double f0 = i * pitch / length;
                if (f0 >= fraction) break;
                Brush? brush = ZoneBrush(f0, fw, fd);
                if (brush is null) continue;
                double start = i * pitch;
                dc.DrawRectangle(brush, null, AxisRect(snap, bar, vertical, start, segment));
            }
            return;
        }

        // Continuous: up to three zone rectangles.
        DrawZone(dc, snap, bar, vertical, 0, Math.Min(fraction, fw), SafeBrush);
        DrawZone(dc, snap, bar, vertical, fw, Math.Min(fraction, fd), WarningBrush);
        DrawZone(dc, snap, bar, vertical, fd, fraction, DangerBrush);
    }

    private static void DrawZone(DrawingContext dc, PixelSnapper snap, Rect bar, bool vertical, double f0, double f1, Brush? brush)
    {
        if (brush is null || f1 <= f0) return;
        double length = vertical ? bar.Height : bar.Width;
        dc.DrawRectangle(brush, null, AxisRect(snap, bar, vertical, f0 * length, (f1 - f0) * length));
    }

    /// <summary>Rectangle covering [start, start+len] along the meter axis (from the bottom when vertical).</summary>
    private static Rect AxisRect(PixelSnapper snap, Rect bar, bool vertical, double start, double len)
        => vertical
            ? snap.Rect(bar.X, bar.Bottom - start - len, bar.Width, len)
            : snap.Rect(bar.X + start, bar.Y, len, bar.Height);

    private Brush? ZoneBrush(double f, double fw, double fd) => f >= fd ? DangerBrush : f >= fw ? WarningBrush : SafeBrush;

    #endregion

    /// <inheritdoc/>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!ShowClipIndicator) return;
        GetBarRect(out Rect clipRect);
        clipRect.Inflate(2, 2);
        if (clipRect.Contains(e.GetPosition(this)))
        {
            SetCurrentValue(IsClippedProperty, false);
            e.Handled = true;
        }
    }
}
