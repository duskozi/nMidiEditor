using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using nMIDI.Controls.Models;
using nMIDI.Controls.Primitives;

namespace nMIDI.Controls;

/// <summary>
/// Drum-machine style step grid. Click toggles a step, drag paints, mouse wheel over a step changes its
/// velocity (Shift = fine). Bind <see cref="CurrentStep"/> to the playback position to show the playhead.
/// Data lives in a <see cref="StepPattern"/>; subscribe to <see cref="StepPattern.StepChanged"/> for edits.
/// </summary>
public class StepSequencer : DawRenderControl
{
    private bool _painting;
    private byte _paintVelocity;
    private (int row, int step) _lastPainted = (-1, -1);

    static StepSequencer()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(StepSequencer), new FrameworkPropertyMetadata(typeof(StepSequencer)));
        FocusableProperty.OverrideMetadata(typeof(StepSequencer), new FrameworkPropertyMetadata(true));
    }

    /// <summary>Creates a <see cref="StepSequencer"/>.</summary>
    public StepSequencer()
    {
        SetCurrentValue(PatternProperty, new StepPattern(8, 16));
        // Subscribe only while in the tree so a long-lived pattern (view-model) never keeps a dead view alive.
        Loaded += (_, _) => Attach(Pattern);
        Unloaded += (_, _) => Detach(Pattern);
    }

    private bool _attached;

    private void Attach(StepPattern? p)
    {
        if (p is null || _attached) return;
        p.StepChanged += OnStepChanged;
        _attached = true;
        InvalidateVisual();
    }

    private void Detach(StepPattern? p)
    {
        if (p is null || !_attached) return;
        p.StepChanged -= OnStepChanged;
        _attached = false;
    }

    #region Properties

    /// <summary>Identifies the <see cref="Pattern"/> dependency property.</summary>
    public static readonly DependencyProperty PatternProperty = DependencyProperty.Register(
        nameof(Pattern), typeof(StepPattern), typeof(StepSequencer),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnPatternChanged));

    /// <summary>The steps shown and edited.</summary>
    public StepPattern? Pattern { get => (StepPattern?)GetValue(PatternProperty); set => SetValue(PatternProperty, value); }

    /// <summary>Identifies the <see cref="RowLabels"/> dependency property.</summary>
    public static readonly DependencyProperty RowLabelsProperty = DependencyProperty.Register(
        nameof(RowLabels), typeof(IList<string>), typeof(StepSequencer),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Lane names drawn in the label column (e.g. Kick, Snare, Hat).</summary>
    public IList<string>? RowLabels { get => (IList<string>?)GetValue(RowLabelsProperty); set => SetValue(RowLabelsProperty, value); }

    /// <summary>Identifies the <see cref="LabelWidth"/> dependency property.</summary>
    public static readonly DependencyProperty LabelWidthProperty = DependencyProperty.Register(
        nameof(LabelWidth), typeof(double), typeof(StepSequencer),
        new FrameworkPropertyMetadata(64.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Width of the row label column in pixels.</summary>
    public double LabelWidth { get => (double)GetValue(LabelWidthProperty); set => SetValue(LabelWidthProperty, value); }

    /// <summary>Identifies the <see cref="StepsPerGroup"/> dependency property.</summary>
    public static readonly DependencyProperty StepsPerGroupProperty = DependencyProperty.Register(
        nameof(StepsPerGroup), typeof(int), typeof(StepSequencer),
        new FrameworkPropertyMetadata(4, FrameworkPropertyMetadataOptions.AffectsRender), v => v is int i && i > 0);

    /// <summary>Steps per shaded group (usually steps per beat).</summary>
    public int StepsPerGroup { get => (int)GetValue(StepsPerGroupProperty); set => SetValue(StepsPerGroupProperty, value); }

    /// <summary>Identifies the <see cref="CurrentStep"/> dependency property.</summary>
    public static readonly DependencyProperty CurrentStepProperty = DependencyProperty.Register(
        nameof(CurrentStep), typeof(int), typeof(StepSequencer),
        new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Playhead step, or -1 when stopped.</summary>
    public int CurrentStep { get => (int)GetValue(CurrentStepProperty); set => SetValue(CurrentStepProperty, value); }

    /// <summary>Identifies the <see cref="DefaultVelocity"/> dependency property.</summary>
    public static readonly DependencyProperty DefaultVelocityProperty = DependencyProperty.Register(
        nameof(DefaultVelocity), typeof(int), typeof(StepSequencer),
        new FrameworkPropertyMetadata(100), v => v is int i && i >= 1 && i <= 127);

    /// <summary>Velocity of steps switched on with the mouse or Space.</summary>
    public int DefaultVelocity { get => (int)GetValue(DefaultVelocityProperty); set => SetValue(DefaultVelocityProperty, value); }

    /// <summary>Identifies the <see cref="CellSpacing"/> dependency property.</summary>
    public static readonly DependencyProperty CellSpacingProperty = DependencyProperty.Register(
        nameof(CellSpacing), typeof(double), typeof(StepSequencer),
        new FrameworkPropertyMetadata(3.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Gap between cells in pixels.</summary>
    public double CellSpacing { get => (double)GetValue(CellSpacingProperty); set => SetValue(CellSpacingProperty, value); }

    /// <summary>Identifies the <see cref="StepOffBrush"/> dependency property.</summary>
    public static readonly DependencyProperty StepOffBrushProperty = RegisterBrush(nameof(StepOffBrush), typeof(StepSequencer));
    /// <summary>Brush for the steps that are off.</summary>
    public Brush? StepOffBrush { get => (Brush?)GetValue(StepOffBrushProperty); set => SetValue(StepOffBrushProperty, value); }

    /// <summary>Identifies the <see cref="StepOnBrush"/> dependency property.</summary>
    public static readonly DependencyProperty StepOnBrushProperty = RegisterBrush(nameof(StepOnBrush), typeof(StepSequencer));
    /// <summary>Brush for the steps that are on.</summary>
    public Brush? StepOnBrush { get => (Brush?)GetValue(StepOnBrushProperty); set => SetValue(StepOnBrushProperty, value); }

    /// <summary>Identifies the <see cref="GroupBrush"/> dependency property.</summary>
    public static readonly DependencyProperty GroupBrushProperty = RegisterBrush(nameof(GroupBrush), typeof(StepSequencer));
    /// <summary>Brush for the every other group of steps.</summary>
    public Brush? GroupBrush { get => (Brush?)GetValue(GroupBrushProperty); set => SetValue(GroupBrushProperty, value); }

    /// <summary>Identifies the <see cref="PlayheadBrush"/> dependency property.</summary>
    public static readonly DependencyProperty PlayheadBrushProperty = RegisterBrush(nameof(PlayheadBrush), typeof(StepSequencer));
    /// <summary>Brush for the playhead line.</summary>
    public Brush? PlayheadBrush { get => (Brush?)GetValue(PlayheadBrushProperty); set => SetValue(PlayheadBrushProperty, value); }

    private static void OnPatternChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var seq = (StepSequencer)d;
        seq.Detach(e.OldValue as StepPattern);
        if (seq.IsLoaded) seq.Attach(e.NewValue as StepPattern);
    }

    private void OnStepChanged(object? sender, StepChangedEventArgs e)
    {
        if (Dispatcher.CheckAccess()) InvalidateVisual();
        else Dispatcher.BeginInvoke(InvalidateVisual); // pattern edited from a worker thread
    }

    #endregion

    #region Geometry

    private Rect GridRect => new(LabelWidth, 0, Math.Max(0, ActualWidth - LabelWidth), ActualHeight);

    private bool TryHit(Point p, out int row, out int step)
    {
        row = step = -1;
        StepPattern? pattern = Pattern;
        Rect grid = GridRect;
        if (pattern is null || !grid.Contains(p) || grid.Width <= 0 || grid.Height <= 0) return false;
        step = Math.Clamp((int)((p.X - grid.X) / (grid.Width / pattern.Steps)), 0, pattern.Steps - 1);
        row = Math.Clamp((int)((p.Y - grid.Y) / (grid.Height / pattern.Rows)), 0, pattern.Rows - 1);
        return true;
    }

    #endregion

    #region Rendering

    private readonly NineBatch _batch = new();

    /// <inheritdoc/>
    protected override void OnRender(DrawingContext dc)
    {
        StepPattern? pattern = Pattern;
        if (Background is { } bg)
            dc.DrawRectangle(bg, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (pattern is null) return;

        var snap = new PixelSnapper(this);
        Rect grid = GridRect;
        double cw = grid.Width / pattern.Steps;
        double rh = grid.Height / pattern.Rows;
        if (cw <= 0 || rh <= 0) return;
        double gap = Math.Min(CellSpacing, Math.Min(cw, rh) / 3);
        int group = StepsPerGroup;

        // Alternate group shading.
        if (GroupBrush is { } gb)
        {
            for (int s = 0; s < pattern.Steps; s += group)
            {
                if ((s / group) % 2 == 1)
                    dc.DrawRectangle(gb, null, snap.Rect(grid.X + s * cw, grid.Y, cw * Math.Min(group, pattern.Steps - s), grid.Height));
            }
        }

        int current = CurrentStep;
        if (current >= 0 && current < pattern.Steps && PlayheadBrush is { } ph)
            dc.DrawRectangle(ph, null, snap.Rect(grid.X + current * cw, grid.Y, cw, grid.Height));

        // Cells: one nine-patch batch (a single native call) instead of a rounded rectangle per cell.
        _batch.Begin(this, RenderSize);
        BrushColors.TryGetSolid(StepOffBrush, out Color offColor);
        bool hasOn = BrushColors.TryGetSolid(StepOnBrush, out Color onColor);
        var offSpec = new FrameSpec(offColor, offColor, Color.FromArgb(0x90, 0, 0, 0), 2, 1, FrameStyle.Sunken);
        for (int r = 0; r < pattern.Rows; r++)
        {
            for (int s = 0; s < pattern.Steps; s++)
            {
                Rect cell = snap.Rect(grid.X + s * cw + gap / 2, grid.Y + r * rh + gap / 2, cw - gap, rh - gap);
                int vel = pattern.GetVelocity(r, s);
                if (vel == 0)
                {
                    if (StepOffBrush is not null) _batch.Add(offSpec, cell);
                    continue;
                }
                if (!hasOn) continue;
                // Velocity → brightness (8 levels keep the skin cache small), plus a thin velocity bar at the bottom.
                int level = Math.Clamp((vel + 15) / 16, 1, 8);
                var lit = Color.FromArgb((byte)(onColor.A * (0.35 + 0.65 * level / 8.0)), onColor.R, onColor.G, onColor.B);
                _batch.Add(new FrameSpec(lit, lit, Color.FromArgb(0xA0, 0, 0, 0), 2, 1, FrameStyle.Glow), cell);
                double barH = Math.Max(snap.OnePixel * 2, cell.Height * 0.12);
                _batch.Add(FrameSpec.Solid(onColor, Colors.Transparent, 0, 0), new Rect(cell.X, cell.Bottom - barH, cell.Width * vel / 127.0, barH));
            }
        }
        _batch.Render(dc);

        // Keyboard cursor (arrows move it, Space toggles), shown only while focused from the keyboard.
        if (IsKeyboardFocused && _keyCursor.Row >= 0 && _keyCursor.Row < pattern.Rows && _keyCursor.Step < pattern.Steps
            && FocusBrushFor(this) is { } focus)
        {
            Rect c = snap.Rect(grid.X + _keyCursor.Step * cw + gap / 2, grid.Y + _keyCursor.Row * rh + gap / 2, cw - gap, rh - gap);
            dc.DrawRectangle(null, FrozenPens.Create(focus, snap.OnePixel * 2), c);
        }

        // Labels
        IList<string>? labels = RowLabels;
        if (labels is not null && LabelWidth > 0)
        {
            for (int r = 0; r < pattern.Rows && r < labels.Count; r++)
            {
                FormattedText ft = MakeText(labels[r] ?? string.Empty, snap.PixelsPerDip);
                ft.MaxTextWidth = Math.Max(1, LabelWidth - 8);
                ft.MaxLineCount = 1;
                ft.Trimming = TextTrimming.CharacterEllipsis;
                dc.DrawText(ft, new Point(4, grid.Y + r * rh + (rh - ft.Height) / 2));
            }
        }
    }

    #endregion

    #region Input

    /// <inheritdoc/>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (Pattern is not { } pattern || !TryHit(e.GetPosition(this), out int row, out int step)) return;
        _paintVelocity = pattern.IsOn(row, step) ? (byte)0 : (byte)DefaultVelocity;
        pattern.SetVelocity(row, step, _paintVelocity);
        _lastPainted = (row, step);
        _keyCursor = (row, step);
        _painting = CaptureMouse();
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_painting || Pattern is not { } pattern) return;
        if (!TryHit(e.GetPosition(this), out int row, out int step) || (row, step) == _lastPainted) return;
        _lastPainted = (row, step);
        // Keep an existing step's velocity when painting "on" over it.
        if (_paintVelocity == 0 || !pattern.IsOn(row, step))
            pattern.SetVelocity(row, step, _paintVelocity);
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
        _painting = false;
        _lastPainted = (-1, -1);
    }

    private (int Row, int Step) _keyCursor = (0, 0);

    private static Brush? FocusBrushFor(FrameworkElement e) =>
        e.TryFindResource(Skinning.DawSkinKeys.Focus) as Brush;

    /// <summary>Arrow keys move the cursor cell, Space or Enter toggles it, + and - change its velocity.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || Pattern is not { } pattern || pattern.Rows == 0 || pattern.Steps == 0) return;
        var (row, step) = _keyCursor;
        row = Math.Clamp(row, 0, pattern.Rows - 1);
        step = Math.Clamp(step, 0, pattern.Steps - 1);
        switch (e.Key)
        {
            case Key.Left: step = (step + pattern.Steps - 1) % pattern.Steps; break;
            case Key.Right: step = (step + 1) % pattern.Steps; break;
            case Key.Up: row = Math.Max(0, row - 1); break;
            case Key.Down: row = Math.Min(pattern.Rows - 1, row + 1); break;
            case Key.Home: step = 0; break;
            case Key.End: step = pattern.Steps - 1; break;
            case Key.Space or Key.Enter:
                pattern.SetVelocity(row, step, pattern.IsOn(row, step) ? 0 : DefaultVelocity);
                break;
            case Key.OemPlus or Key.Add or Key.OemMinus or Key.Subtract when pattern.IsOn(row, step):
                int delta = e.Key is Key.OemPlus or Key.Add ? 8 : -8;
                pattern.SetVelocity(row, step, Math.Clamp(pattern.GetVelocity(row, step) + delta, 1, 127));
                break;
            default:
                return;
        }
        _keyCursor = (row, step);
        e.Handled = true;
        InvalidateVisual();
    }

    /// <inheritdoc/>
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        InvalidateVisual();
    }

    /// <inheritdoc/>
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        InvalidateVisual();
    }

    /// <inheritdoc/>
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (Pattern is not { } pattern || !TryHit(e.GetPosition(this), out int row, out int step)) return;
        int vel = pattern.GetVelocity(row, step);
        if (vel == 0) return;
        int delta = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 1 : 8;
        pattern.SetVelocity(row, step, Math.Clamp(vel + Math.Sign(e.Delta) * delta, 1, 127));
        e.Handled = true;
    }

    #endregion
}
