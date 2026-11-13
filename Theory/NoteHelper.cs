using System;

namespace nMIDI.Theory
{
    /// <summary>Note numbers, names and octaves.</summary>
    public static class NoteHelper
    {
        // The spelling most lead sheets use: flats for Db, Eb, Ab and Bb, a sharp for F#.
        private static readonly string[] Names =
            { "C", "D♭", "D", "E♭", "E", "F", "F♯", "G", "A♭", "A", "B♭", "B" };

        /// <summary>Pitch class, 0 = C … 11 = B. Works for negative numbers too.</summary>
        public static int PC(int midi) => ((midi % 12) + 12) % 12;

        /// <summary>Note name with ♯/♭ signs, e.g. "B♭".</summary>
        public static string Pretty(int midi) => Names[PC(midi)];

        /// <summary>Octave number where middle C (MIDI 60) is C4.</summary>
        public static int Octave(int midi) => (int)Math.Floor(midi / 12.0) - 1;

        public static bool IsBlackKey(int midi) => PC(midi) is 1 or 3 or 6 or 8 or 10;
    }
}
