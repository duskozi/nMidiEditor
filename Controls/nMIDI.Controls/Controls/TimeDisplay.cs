using System;
using nMIDI.Controls.Primitives;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace nMIDI.Controls;

/// <summary>What a <see cref="TimeDisplay"/> shows.</summary>
public enum TimeDisplayMode
{
    /// <summary>Bars : beats : ticks.</summary>
    Musical,
    /// <summary>Minutes : seconds . milliseconds.</summary>
    Clock,
}

/// <summary>LCD-style song position display. Click toggles between musical and clock time.</summary>
public class TimeDisplay : Control
{
    static TimeDisplay()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(TimeDisplay), new FrameworkPropertyMetadata(typeof(TimeDisplay)));
        FocusableProperty.OverrideMetadata(typeof(TimeDisplay), new FrameworkPropertyMetadata(false));
    }

    /// <summary>Creates a <see cref="TimeDisplay"/>.</summary>
    public TimeDisplay() => UpdateText();

    /// <summary>Identifies the <see cref="Position"/> dependency property.</summary>
    public static readonly DependencyProperty PositionProperty = DependencyProperty.Register(
        nameof(Position), typeof(double), typeof(TimeDisplay), new FrameworkPropertyMetadata(0.0, OnInputChanged));

    /// <summary>Song position in beats (quarter notes).</summary>
    public double Position { get => (double)GetValue(PositionProperty); set => SetValue(PositionProperty, value); }

    /// <summary>Identifies the <see cref="Tempo"/> dependency property.</summary>
    public static readonly DependencyProperty TempoProperty = DependencyProperty.Register(
        nameof(Tempo), typeof(double), typeof(TimeDisplay), new FrameworkPropertyMetadata(120.0, OnInputChanged),
        v => v is double d && d > 0 && !double.IsInfinity(d));

    /// <summary>Tempo in beats per minute, for the clock display.</summary>
    public double Tempo { get => (double)GetValue(TempoProperty); set => SetValue(TempoProperty, value); }

    /// <summary>Identifies the <see cref="BeatsPerBar"/> dependency property.</summary>
    public static readonly DependencyProperty BeatsPerBarProperty = DependencyProperty.Register(
        nameof(BeatsPerBar), typeof(int), typeof(TimeDisplay), new FrameworkPropertyMetadata(4, OnInputChanged),
        v => v is int i && i > 0);

    /// <summary>Beats per bar, for the bar display.</summary>
    public int BeatsPerBar { get => (int)GetValue(BeatsPerBarProperty); set => SetValue(BeatsPerBarProperty, value); }

    /// <summary>Identifies the <see cref="TicksPerBeat"/> dependency property.</summary>
    public static readonly DependencyProperty TicksPerBeatProperty = DependencyProperty.Register(
        nameof(TicksPerBeat), typeof(int), typeof(TimeDisplay), new FrameworkPropertyMetadata(960, OnInputChanged),
        v => v is int i && i > 0);

    /// <summary>PPQ resolution shown in the tick field.</summary>
    public int TicksPerBeat { get => (int)GetValue(TicksPerBeatProperty); set => SetValue(TicksPerBeatProperty, value); }

    /// <summary>Identifies the <see cref="Mode"/> dependency property.</summary>
    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(
        nameof(Mode), typeof(TimeDisplayMode), typeof(TimeDisplay),
        new FrameworkPropertyMetadata(TimeDisplayMode.Musical, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnInputChanged));

    /// <summary>Show bars and beats, or clock time.</summary>
    public TimeDisplayMode Mode { get => (TimeDisplayMode)GetValue(ModeProperty); set => SetValue(ModeProperty, value); }

    private static readonly DependencyPropertyKey TextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(Text), typeof(string), typeof(TimeDisplay), new FrameworkPropertyMetadata(string.Empty));

    /// <summary>Identifies the <see cref="Text"/> dependency property.</summary>
    public static readonly DependencyProperty TextProperty = TextPropertyKey.DependencyProperty;

    /// <summary>The text shown: bars or clock time, depending on <see cref="Mode"/>.</summary>
    public string Text => (string)GetValue(TextProperty);

    private static readonly DependencyPropertyKey CaptionPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(Caption), typeof(string), typeof(TimeDisplay), new FrameworkPropertyMetadata("BAR"));

    /// <summary>Identifies the <see cref="Caption"/> dependency property.</summary>
    public static readonly DependencyProperty CaptionProperty = CaptionPropertyKey.DependencyProperty;

    /// <summary>"BAR" or "TIME" – what the digits mean.</summary>
    public string Caption => (string)GetValue(CaptionProperty);

    private static void OnInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((TimeDisplay)d).UpdateText();

    private void UpdateText()
    {
        double pos = double.IsFinite(Position) ? Math.Clamp(Position, 0, 1e9) : 0;
        string text;
        if (Mode == TimeDisplayMode.Musical)
        {
            text = FormatMusical(pos, BeatsPerBar, TicksPerBeat);
            SetValue(CaptionPropertyKey, "BAR");
        }
        else
        {
            text = FormatClock(TimeSpan.FromSeconds(pos * 60.0 / Tempo));
            SetValue(CaptionPropertyKey, "TIME");
        }
        // Avoid churning bindings 60×/s when the visible text did not change.
        if (!string.Equals(text, Text, StringComparison.Ordinal))
            SetValue(TextPropertyKey, text);
    }

    /// <summary>Formats a position in beats as bar, beat and tick ("12.3.240").</summary>
    public static string FormatMusical(double beats, int beatsPerBar, int ticksPerBeat) => MusicalTime.FormatMusical(beats, beatsPerBar, ticksPerBeat);

    /// <summary>Formats a time as minutes, seconds and milliseconds ("01:23.456").</summary>
    public static string FormatClock(TimeSpan t) => MusicalTime.FormatClock(t);

    /// <inheritdoc/>
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        SetCurrentValue(ModeProperty, Mode == TimeDisplayMode.Musical ? TimeDisplayMode.Clock : TimeDisplayMode.Musical);
        e.Handled = true;
    }
}
