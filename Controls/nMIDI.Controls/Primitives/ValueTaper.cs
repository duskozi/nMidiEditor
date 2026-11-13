using System;
using System.ComponentModel;
using System.Globalization;

namespace nMIDI.Controls.Primitives;

/// <summary>
/// Maps a value in [minimum, maximum] to a normalized control position in [0, 1] and back.
/// Knobs, faders, value boxes and scales all share the same taper so that
/// what the user drags, what is drawn and what the scale says always agree.
/// </summary>
[TypeConverter(typeof(ValueTaperConverter))]
public abstract class ValueTaper
{
    /// <summary>Straight line mapping.</summary>
    public static ValueTaper Linear { get; } = new LinearTaper();

    /// <summary>Logarithmic mapping (frequency, time). Requires minimum &gt; 0, otherwise behaves linearly.</summary>
    public static ValueTaper Logarithmic { get; } = new LogarithmicTaper();

    /// <summary>Console-style audio fader taper for values expressed in dB.</summary>
    public static ValueTaper Decibel { get; } = new DecibelFaderTaper();

    /// <summary>The normalised position, 0 to 1, of a value.</summary>
    public abstract double ToNormalized(double value, double minimum, double maximum);

    /// <summary>The value at a normalised position, 0 to 1.</summary>
    public abstract double FromNormalized(double normalized, double minimum, double maximum);

    /// <summary>Clamps to 0–1, with NaN as 0.</summary>
    protected static double Clamp01(double v) => double.IsNaN(v) ? 0 : Math.Clamp(v, 0.0, 1.0);
}

/// <summary>Straight-line taper.</summary>
public sealed class LinearTaper : ValueTaper
{
    /// <inheritdoc/>
    public override double ToNormalized(double value, double minimum, double maximum)
    {
        double range = maximum - minimum;
        return range <= 0 ? 0 : Clamp01((value - minimum) / range);
    }

    /// <inheritdoc/>
    public override double FromNormalized(double normalized, double minimum, double maximum)
        => minimum + Clamp01(normalized) * (maximum - minimum);
}

/// <summary>Logarithmic taper, for frequencies and times (needs a minimum above 0).</summary>
public sealed class LogarithmicTaper : ValueTaper
{
    /// <inheritdoc/>
    public override double ToNormalized(double value, double minimum, double maximum)
    {
        if (minimum <= 0 || maximum <= minimum)
            return Linear.ToNormalized(value, minimum, maximum);
        if (value <= minimum) return 0;
        return Clamp01(Math.Log(value / minimum) / Math.Log(maximum / minimum));
    }

    /// <inheritdoc/>
    public override double FromNormalized(double normalized, double minimum, double maximum)
    {
        if (minimum <= 0 || maximum <= minimum)
            return Linear.FromNormalized(normalized, minimum, maximum);
        return minimum * Math.Pow(maximum / minimum, Clamp01(normalized));
    }
}

/// <summary>
/// Audio fader taper. Values are dB; <c>maximum</c> is the top of the fader (e.g. +6 dB) and
/// <c>minimum</c> is treated as -∞ (the bottom stop). The curve spends most of the travel
/// in the useful -30..+6 dB region, like a hardware console (0 dB lands at roughly 80%).
/// </summary>
public sealed class DecibelFaderTaper : ValueTaper
{
    /// <summary>Curve exponent applied to linear gain. Smaller = more travel near the top.</summary>
    public double Exponent { get; init; } = 0.25;

    /// <inheritdoc/>
    public override double ToNormalized(double value, double minimum, double maximum)
    {
        if (maximum <= minimum) return 0;
        if (value <= minimum) return 0;
        if (value >= maximum) return 1;
        double rMin = Math.Pow(DbMath.DbToGain(minimum - maximum), Exponent);
        double r = Math.Pow(DbMath.DbToGain(value - maximum), Exponent);
        return Clamp01((r - rMin) / (1 - rMin));
    }

    /// <inheritdoc/>
    public override double FromNormalized(double normalized, double minimum, double maximum)
    {
        if (maximum <= minimum) return minimum;
        normalized = Clamp01(normalized);
        if (normalized <= 0) return minimum;
        if (normalized >= 1) return maximum;
        double rMin = Math.Pow(DbMath.DbToGain(minimum - maximum), Exponent);
        double r = normalized * (1 - rMin) + rMin;
        double gain = Math.Pow(r, 1.0 / Exponent);
        return Math.Clamp(DbMath.GainToDb(gain) + maximum, minimum, maximum);
    }
}

/// <summary>Allows <c>Taper="Log"</c> / <c>"Decibel"</c> / <c>"Linear"</c> in XAML.</summary>
public sealed class ValueTaperConverter : TypeConverter
{
    /// <inheritdoc/>
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    /// <inheritdoc/>
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string s)
        {
            return s.Trim().ToLowerInvariant() switch
            {
                "linear" or "lin" => ValueTaper.Linear,
                "log" or "logarithmic" or "frequency" => ValueTaper.Logarithmic,
                "db" or "decibel" or "fader" or "audio" => ValueTaper.Decibel,
                _ => throw new FormatException($"Unknown taper '{s}'. Use Linear, Log or Decibel."),
            };
        }
        return base.ConvertFrom(context, culture, value);
    }
}
