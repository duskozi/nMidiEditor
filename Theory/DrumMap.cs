using System;
using System.Collections.Generic;

namespace nMIDI.Theory
{
    /// <summary>The General MIDI (GM2) percussion map: which drum each key plays on channel 10.</summary>
    public static class DrumMap
    {
        public const int FirstNamed = 27, LastNamed = 87;

        private static readonly string[] Names =
        {
            "High Q", "Slap", "Scratch Push", "Scratch Pull", "Sticks", "Square Click", "Metronome Click", "Metronome Bell",   // 27–34
            "Acoustic Bass Drum", "Bass Drum", "Side Stick", "Acoustic Snare", "Hand Clap", "Electric Snare",                    // 35–40
            "Low Floor Tom", "Closed Hi-Hat", "High Floor Tom", "Pedal Hi-Hat", "Low Tom", "Open Hi-Hat",                        // 41–46
            "Low-Mid Tom", "Hi-Mid Tom", "Crash Cymbal 1", "High Tom", "Ride Cymbal 1", "Chinese Cymbal",                        // 47–52
            "Ride Bell", "Tambourine", "Splash Cymbal", "Cowbell", "Crash Cymbal 2", "Vibraslap", "Ride Cymbal 2",              // 53–59
            "High Bongo", "Low Bongo", "Mute High Conga", "Open High Conga", "Low Conga", "High Timbale", "Low Timbale",         // 60–66
            "High Agogo", "Low Agogo", "Cabasa", "Maracas", "Short Whistle", "Long Whistle", "Short Guiro", "Long Guiro",       // 67–74
            "Claves", "High Wood Block", "Low Wood Block", "Mute Cuica", "Open Cuica", "Mute Triangle", "Open Triangle",        // 75–81
            "Shaker", "Jingle Bell", "Bell Tree", "Castanets", "Mute Surdo", "Open Surdo",                                       // 82–87
        };

        /// <summary>
        /// The drums of a basic kit, shown in the drum view even before they are used: kick, side stick, snare, clap,
        /// the hi-hats, the toms, crash and ride.
        /// </summary>
        public static IReadOnlyList<int> CommonKit { get; } = new[] { 36, 37, 38, 39, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51 };

        /// <summary>The drum a key plays, or null outside the map.</summary>
        public static string? Name(int note) => note is >= FirstNamed and <= LastNamed ? Names[note - FirstNamed] : null;

        /// <summary>The drum's name, or the note name (like "C4") for keys outside the map.</summary>
        public static string Label(int note) => Name(note) ?? $"{NoteHelper.Pretty(note)}{NoteHelper.Octave(note)}";
    }
}
