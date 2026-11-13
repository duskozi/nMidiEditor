using System;
using System.Globalization;

namespace nMIDI.Controls.Primitives;

/// <summary>
/// Ready-made value formatters for <see cref="DawRangeControl.ValueFormatter"/>.
/// Usage in XAML: <c>ValueFormatter="{x:Static nc:ValueFormatters.Pan}"</c>.
/// </summary>
public static class ValueFormatters
{
    private static CultureInfo C => CultureInfo.CurrentCulture;

    /// <summary>Decibels with a sign and one decimal ("-6.0 dB").</summary>
    public static Func<double, string> Decibel { get; } =
        v => double.IsNegativeInfinity(v) ? "-∞ dB" : v.ToString("+0.0;-0.0;0.0", C) + " dB";

    /// <summary>-1–1 as "40L", "C", "40R".</summary>
    public static Func<double, string> Pan { get; } =
        v => Math.Abs(v) < 0.005 ? "C" : (Math.Abs(v) * 100).ToString("0", C) + (v < 0 ? "L" : "R");

    /// <summary>A 0–1 value as a percentage.</summary>
    public static Func<double, string> Percent { get; } =
        v => (v * 100).ToString("0", C) + "%";

    /// <summary>Hz, or kHz from 1000.</summary>
    public static Func<double, string> Frequency { get; } =
        v => v >= 1000 ? (v / 1000).ToString(v >= 10000 ? "0.0" : "0.00", C) + " kHz" : v.ToString("0", C) + " Hz";

    /// <summary>Value is in seconds.</summary>
    public static Func<double, string> Time { get; } =
        v => v < 1 ? (v * 1000).ToString(v < 0.01 ? "0.0" : "0", C) + " ms" : v.ToString("0.00", C) + " s";

    /// <summary>Two decimals, for BPM.</summary>
    public static Func<double, string> Tempo { get; } =
        v => v.ToString("0.00", C);

    /// <summary>Rounded to a whole number.</summary>
    public static Func<double, string> Integer { get; } =
        v => Math.Round(v).ToString("0", C);
}
