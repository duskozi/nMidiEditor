using System;
using System.Collections.Generic;
using System.Linq;
using Engine = nPianoFingering;

namespace nMIDI.Theory
{
    public enum Hand { None = 0, Left = 1, Right = 2 }

    /// <summary>Hand and finger of one note. <see cref="FromChart"/>: from a standard scale fingering; <see cref="Locked"/>: fixed by the user.</summary>
    public readonly record struct PianoFinger(int Note, Hand Hand, int Finger, bool FromChart = false, bool Locked = false);

    /// <summary>
    /// Hand and finger for every note of a single part, from the nPianoFingering engine (lib/nPianoFingering):
    /// a beam search splits the part between the hands, then a Viterbi-style search over an ergonomic
    /// cost model (Parncutt/Jacobs spans, finger rules, standard scale charts) fingers each hand.
    /// Songs use <see cref="nMIDI.Model.SongFingering"/>, which fingers pairs of tracks together and incrementally.
    /// </summary>
    public static class PartFingering
    {

        /// <summary>
        /// Finger a whole part. <paramref name="notes"/> are (start, end in seconds, MIDI note); the result is
        /// aligned with the input order. <paramref name="forced"/> puts every note in one hand (a track named "LH").
        /// </summary>
        public static PianoFinger[] Finger(IReadOnlyList<(double Start, double End, int Note)> notes, Hand forced = Hand.None)
        {
            var result = new PianoFinger[notes.Count];
            if (notes.Count == 0) return result;

            var source = forced switch { Hand.Left => Engine.Hand.Left, Hand.Right => Engine.Hand.Right, _ => Engine.Hand.Unknown };
            var parts = new Engine.PianoNote[notes.Count];
            for (int i = 0; i < notes.Count; i++)
            {
                var (start, end, note) = notes[i];
                parts[i] = new Engine.PianoNote { Pitch = note, Start = start, End = Math.Max(end, start + 0.001), SourceHand = source };
            }

            try
            {
                Engine.PianoAnalyzer.Analyze(parts, nMIDI.Model.FingeringPrefs.Options());
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IndexOutOfRangeException)
            {
                // Never let the fingering break the editor: show no fingers instead.
                for (int i = 0; i < notes.Count; i++) result[i] = new PianoFinger(notes[i].Note, forced, 0);
                return result;
            }

            for (int i = 0; i < notes.Count; i++)
            {
                var p = parts[i];
                var hand = p.Hand switch { Engine.Hand.Left => Hand.Left, Engine.Hand.Right => Hand.Right, _ => Hand.None };
                result[i] = new PianoFinger(notes[i].Note, hand, Math.Clamp(p.Finger, 0, 5), p.FromChart);
            }
            return result;
        }

        /// <summary>Guesses a forced hand from a track name ("Piano LH", "Left hand", "Right" …).</summary>
        public static Hand HandFromTrackName(string name)
        {
            var n = " " + name.ToLowerInvariant().Replace('.', ' ').Replace('-', ' ').Replace('_', ' ') + " ";
            if (n.Contains(" lh ") || n.Contains("left") || n.Contains(" links ") || n.Contains(" lijeva") || n.Contains(" leva")) return Hand.Left;
            if (n.Contains(" rh ") || n.Contains("right") || n.Contains(" rechts ") || n.Contains(" desna")) return Hand.Right;
            return Hand.None;
        }
    }
}
