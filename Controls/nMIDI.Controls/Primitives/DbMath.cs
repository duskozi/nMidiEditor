using System;


namespace nMIDI.Controls.Primitives;

/// <summary>Small audio math helpers.</summary>
public static class DbMath
{
    /// <summary>Decibels from linear gain (0 gives minus infinity).</summary>
    public static double GainToDb(double gain) => gain <= 0 ? double.NegativeInfinity : 20.0 * Math.Log10(gain);

    /// <summary>Linear gain from decibels (minus infinity gives 0).</summary>
    public static double DbToGain(double db) => double.IsNegativeInfinity(db) ? 0 : Math.Pow(10.0, db / 20.0);
}
