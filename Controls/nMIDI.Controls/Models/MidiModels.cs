using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace nMIDI.Controls.Models;

/// <summary>A MIDI note in a piano roll. Times are in beats (quarter notes).</summary>
public sealed class MidiNote : INotifyPropertyChanged
{
    private int _pitch;
    private double _start;
    private double _length;
    private int _velocity;
    private bool _isSelected;

    /// <summary>Creates a <see cref="MidiNote"/>.</summary>
    public MidiNote() : this(60, 0, 1, 100) { }

    /// <summary>Creates a <see cref="MidiNote"/>.</summary>
    public MidiNote(int pitch, double start, double length, int velocity = 100)
    {
        _pitch = Math.Clamp(pitch, 0, 127);
        _start = Math.Max(0, start);
        _length = Math.Max(MinLength, length);
        _velocity = Math.Clamp(velocity, 1, 127);
    }

    /// <summary>Smallest allowed note length in beats (1/256 note).</summary>
    public const double MinLength = 1.0 / 64;

    /// <summary>MIDI note number, 0 to 127 (60 = middle C).</summary>
    public int Pitch { get => _pitch; set => Set(ref _pitch, Math.Clamp(value, 0, 127)); }

    /// <summary>Start position in beats.</summary>
    public double Start { get => _start; set => Set(ref _start, Math.Max(0, value)); }

    /// <summary>Length in beats.</summary>
    public double Length { get => _length; set => Set(ref _length, Math.Max(MinLength, value)); }

    /// <summary>End position in beats.</summary>
    public double End => Start + Length;

    /// <summary>Velocity, 1 to 127.</summary>
    public int Velocity { get => _velocity; set => Set(ref _velocity, Math.Clamp(value, 1, 127)); }

    /// <summary>Whether the note is selected in the piano roll.</summary>
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    private Brush? _color;

    /// <summary>Optional per-note colour (e.g. the track colour). Null = the control's note brush.</summary>
    public Brush? Brush { get => _color; set => Set(ref _color, value); }

    /// <summary>Raised when a property changes.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        if (name is nameof(Start) or nameof(Length))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(End)));
    }

    /// <summary>A copy of the note, not selected.</summary>
    public MidiNote Clone() => new(Pitch, Start, Length, Velocity) { Brush = Brush };

    /// <inheritdoc/>
    public override string ToString() => $"{MidiNoteName.Get(Pitch)} @{Start:0.###} len {Length:0.###} vel {Velocity}";
}

/// <summary>One controller / pitch-bend event in a <see cref="Controls.ControllerLane"/>. Position in beats.</summary>
public sealed class ControllerPoint : INotifyPropertyChanged
{
    private double _position;
    private int _value;

    /// <summary>Creates a <see cref="ControllerPoint"/>.</summary>
    public ControllerPoint() { }

    /// <summary>Creates a <see cref="ControllerPoint"/>.</summary>
    public ControllerPoint(double position, int value)
    {
        _position = Math.Max(0, position);
        _value = value;
    }

    /// <summary>Position in beats.</summary>
    public double Position
    {
        get => _position;
        set { if (_position != value) { _position = Math.Max(0, value); PropertyChanged?.Invoke(this, new(nameof(Position))); } }
    }

    /// <summary>0..127 for CC, -8192..8191 for pitch bend.</summary>
    public int Value
    {
        get => _value;
        set { if (_value != value) { _value = value; PropertyChanged?.Invoke(this, new(nameof(Value))); } }
    }

    /// <summary>Raised when the position or value changes.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <inheritdoc/>
    public override string ToString() => $"@{Position:0.###} = {Value}";
}

/// <summary>Names of MIDI notes ("C4", "F#3").</summary>
public static class MidiNoteName
{
    private static readonly string[] s_names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    // The spelling most lead sheets use: flats for Db, Eb, Ab and Bb, a sharp for F#.
    private static readonly string[] s_pretty = { "C", "D♭", "D", "E♭", "E", "F", "F♯", "G", "A♭", "A", "B♭", "B" };

    /// <summary>Note name without octave, with ♯/♭ signs, e.g. "B♭".</summary>
    public static string Pretty(int note) => s_pretty[((note % 12) + 12) % 12];

    /// <summary>Whether a MIDI note is a black key.</summary>
    public static bool IsBlackKey(int note)
    {
        int n = ((note % 12) + 12) % 12;
        return n is 1 or 3 or 6 or 8 or 10;
    }

    /// <summary>
    /// Name of a MIDI note. <paramref name="middleCOctave"/> selects the convention:
    /// 4 = Roland/scientific (note 60 = C4), 3 = Yamaha/Cubase (note 60 = C3).
    /// </summary>
    public static string Get(int note, int middleCOctave = 4)
    {
        int n = ((note % 12) + 12) % 12;
        int octave = (int)Math.Floor(note / 12.0) - (5 - middleCOctave);
        return s_names[n] + octave.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}

/// <summary>Handles note events of the keyboard and piano roll.</summary>
public delegate void NoteEventHandler(object sender, NoteEventArgs e);

/// <summary>Data for note events of the keyboard and piano roll.</summary>
public class NoteEventArgs : RoutedEventArgs
{
    /// <summary>Creates a <see cref="NoteEventArgs"/>.</summary>
    public NoteEventArgs(RoutedEvent routedEvent, object source, int note, int velocity)
        : base(routedEvent, source)
    {
        Note = note;
        Velocity = velocity;
    }

    /// <summary>MIDI note number 0..127.</summary>
    public int Note { get; }

    /// <summary>Velocity 1..127 for note-on, 0 for note-off.</summary>
    public int Velocity { get; }
}

/// <summary>Grid of steps for <see cref="Controls.StepSequencer"/>. Velocity 0 = step off.</summary>
public sealed class StepPattern
{
    private readonly byte[,] _cells;

    /// <summary>Creates a <see cref="StepPattern"/>.</summary>
    public StepPattern(int rows, int steps)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(steps);
        _cells = new byte[rows, steps];
    }

    /// <summary>Number of rows (instruments).</summary>
    public int Rows => _cells.GetLength(0);

    /// <summary>Number of steps (columns).</summary>
    public int Steps => _cells.GetLength(1);

    /// <summary>Raised whenever a cell changes (row, step, new velocity). Raised on the calling thread.</summary>
    public event EventHandler<StepChangedEventArgs>? StepChanged;

    /// <summary>A step's velocity; 0 means off.</summary>
    public byte GetVelocity(int row, int step) => _cells[row, step];

    /// <summary>Whether a step is on.</summary>
    public bool IsOn(int row, int step) => _cells[row, step] > 0;

    /// <summary>Sets a step's velocity, 0 (off) to 127, and raises <see cref="StepChanged"/> if it changed.</summary>
    public void SetVelocity(int row, int step, int velocity)
    {
        byte v = (byte)Math.Clamp(velocity, 0, 127);
        if (_cells[row, step] == v) return;
        _cells[row, step] = v;
        StepChanged?.Invoke(this, new StepChangedEventArgs(row, step, v));
    }

    /// <summary>Switches every step off.</summary>
    public void Clear()
    {
        for (int r = 0; r < Rows; r++)
            for (int s = 0; s < Steps; s++)
                SetVelocity(r, s, 0);
    }
}

/// <summary>Data for <see cref="StepPattern.StepChanged"/>.</summary>
public sealed class StepChangedEventArgs : EventArgs
{
    /// <summary>Creates a <see cref="StepChangedEventArgs"/>.</summary>
    public StepChangedEventArgs(int row, int step, int velocity)
    {
        Row = row;
        Step = step;
        Velocity = velocity;
    }

    /// <summary>The row, from 0.</summary>
    public int Row { get; }

    /// <summary>The step (column), from 0.</summary>
    public int Step { get; }

    /// <summary>The new velocity; 0 means off.</summary>
    public int Velocity { get; }
}

/// <summary>What is written on lit keys of a <see cref="Controls.PianoKeyboard"/> in the Grand style.</summary>
public enum KeyLabelMode
{
    /// <summary>Finger numbers with the hand.</summary>
    Fingers,
    /// <summary>Note names.</summary>
    Notes,
    /// <summary>No labels.</summary>
    None,
}

/// <summary>A lit key: colour, brightness 0–1, and optionally hand (0 none, 1 left, 2 right) and finger 1–5 (0 = unknown).</summary>
public readonly record struct KeyHighlight(int Note, Color Color, double Intensity, int Hand = 0, int Finger = 0);

/// <summary>Colours of the left and right hand in fingering labels.</summary>
public static class HandColors
{
    /// <summary>Colour of the left hand.</summary>
    public static readonly Color Left = Color.FromRgb(0x3B, 0x82, 0xF6);
    /// <summary>Colour of the right hand.</summary>
    public static readonly Color Right = Color.FromRgb(0xF9, 0x73, 0x16);
    /// <summary>The colour of a hand: 1 = left, anything else = right.</summary>
    public static Color Of(int hand) => hand == 1 ? Left : Right;
}
