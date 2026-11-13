using System;
using System.Globalization;

namespace nMIDI.Controls.Primitives;

/// <summary>Bar/beat/tick and clock formatting (no WPF dependency, so it is unit-tested on any OS).</summary>
internal static class MusicalTime
{
    /// <summary>Formats a position in beats as bar, beat and tick ("12.3.240").</summary>
    public static string FormatMusical(double beats, int beatsPerBar, int ticksPerBeat)
    {
        if (beatsPerBar <= 0) throw new ArgumentOutOfRangeException(nameof(beatsPerBar));
        if (ticksPerBeat <= 0) throw new ArgumentOutOfRangeException(nameof(ticksPerBeat));
        beats = double.IsFinite(beats) ? Math.Clamp(beats, 0, 1e9) : 0;
        long totalTicks = (long)Math.Floor(beats * ticksPerBeat + 1e-6);
        long ticksPerBar = (long)beatsPerBar * ticksPerBeat;
        long bar = totalTicks / ticksPerBar + 1;
        long beat = totalTicks % ticksPerBar / ticksPerBeat + 1;
        long tick = totalTicks % ticksPerBeat;
        int tickDigits = ticksPerBeat.ToString(CultureInfo.InvariantCulture).Length;
        return string.Create(CultureInfo.InvariantCulture,
            $"{bar,3}.{beat}.{tick.ToString(new string('0', tickDigits), CultureInfo.InvariantCulture)}");
    }

    /// <summary>Formats a time as minutes, seconds and milliseconds ("01:23.456").</summary>
    public static string FormatClock(TimeSpan t)
        => t.TotalHours >= 1
            ? t.ToString(@"h\:mm\:ss\.fff", CultureInfo.InvariantCulture)
            : t.ToString(@"mm\:ss\.fff", CultureInfo.InvariantCulture);
}
