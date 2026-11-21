using System;
using System.Collections.Generic;
using System.Linq;

namespace nMIDI.Theory
{
    /// <summary>
    /// Names the chord that a set of keys makes, as on a lead sheet: "C", "Am7", "F♯m7♭5", "G7/B", "C5".
    /// The root is found by trying every pitch class against a list of chord shapes; a shape that needs fewer
    /// missing tones, and one built on the bass note, wins.
    /// </summary>
    public static class ChordNamer
    {
        private sealed record Shape(string Suffix, int[] Tones, int[] Optional, int Rank);

        // Intervals from the root. Optional tones (usually the fifth) may be missing. Rank breaks ties: lower is
        // the more common reading.
        private static readonly Shape[] Shapes =
        {
            new("",      new[] { 0, 4, 7 },         new[] { 7 },     0),
            new("m",     new[] { 0, 3, 7 },         new[] { 7 },     1),
            new("7",     new[] { 0, 4, 7, 10 },     new[] { 7 },     2),
            new("maj7",  new[] { 0, 4, 7, 11 },     new[] { 7 },     3),
            new("m7",    new[] { 0, 3, 7, 10 },     new[] { 7 },     4),
            new("dim",   new[] { 0, 3, 6 },         Array.Empty<int>(), 6),
            new("aug",   new[] { 0, 4, 8 },         Array.Empty<int>(), 7),
            new("sus4",  new[] { 0, 5, 7 },         Array.Empty<int>(), 8),
            new("sus2",  new[] { 0, 2, 7 },         Array.Empty<int>(), 9),
            new("6",     new[] { 0, 4, 7, 9 },      new[] { 7 },     10),
            new("m6",    new[] { 0, 3, 7, 9 },      new[] { 7 },     11),
            new("m7♭5",  new[] { 0, 3, 6, 10 },     Array.Empty<int>(), 5),
            new("dim7",  new[] { 0, 3, 6, 9 },      Array.Empty<int>(), 6),
            new("m(maj7)", new[] { 0, 3, 7, 11 },   new[] { 7 },     14),
            new("7sus4", new[] { 0, 5, 7, 10 },     new[] { 7 },     12),
            new("add9",  new[] { 0, 2, 4, 7 },      new[] { 7 },     13),
            new("m(add9)", new[] { 0, 2, 3, 7 },    new[] { 7 },     15),
            new("9",     new[] { 0, 2, 4, 7, 10 },  new[] { 7 },     16),
            new("maj9",  new[] { 0, 2, 4, 7, 11 },  new[] { 7 },     17),
            new("m9",    new[] { 0, 2, 3, 7, 10 },  new[] { 7 },     18),
            new("6/9",   new[] { 0, 2, 4, 7, 9 },   new[] { 7 },     19),
            new("7♭9",   new[] { 0, 1, 4, 7, 10 },  new[] { 7 },     20),
            new("7♯9",   new[] { 0, 3, 4, 7, 10 },  new[] { 7 },     21),
            new("11",    new[] { 0, 2, 5, 7, 10 },  new[] { 2, 7 },  22),
            new("m11",   new[] { 0, 2, 3, 5, 7, 10 }, new[] { 2, 7 }, 23),
            new("13",    new[] { 0, 2, 4, 7, 9, 10 }, new[] { 2, 7 }, 24),
            new("7♭5",   new[] { 0, 4, 6, 10 },     Array.Empty<int>(), 25),
            new("7♯5",   new[] { 0, 4, 8, 10 },     Array.Empty<int>(), 26),
            new("5",     new[] { 0, 7 },            Array.Empty<int>(), 30),
        };

        /// <summary>
        /// The chord of these MIDI note numbers, or null when they do not make one (fewer than two pitch
        /// classes, or a set that matches no shape). The lowest note is the bass: when it is not the root, the
        /// name gets a slash, e.g. "C/E".
        /// </summary>
        public static string? Name(IEnumerable<int> notes)
        {
            var list = notes.Where(n => n is >= 0 and < 128).ToList();
            if (list.Count < 2) return null;
            int bass = NoteHelper.PC(list.Min());
            int mask = 0;
            foreach (int n in list) mask |= 1 << NoteHelper.PC(n);
            return Name(mask, bass);
        }

        /// <summary>The chord of a pitch-class set (bit 0 = C … bit 11 = B) over the bass pitch class.</summary>
        public static string? Name(int pitchClasses, int bass)
        {
            int count = BitCount(pitchClasses);
            if (count < 2) return null;

            string? best = null;
            int bestScore = int.MaxValue;
            for (int root = 0; root < 12; root++)
            {
                if ((pitchClasses & (1 << root)) == 0) continue;
                int rel = Rotate(pitchClasses, root);
                foreach (var shape in Shapes)
                {
                    int required = 0, all = 0;
                    foreach (int t in shape.Tones)
                    {
                        all |= 1 << t;
                        if (Array.IndexOf(shape.Optional, t) < 0) required |= 1 << t;
                    }
                    // Every key must belong to the shape, and every required tone must be there.
                    if ((rel & ~all) != 0 || (rel & required) != required) continue;
                    // A two-note "5" only for a bare fifth; other two-note sets are not named.
                    if (count == 2 && shape.Suffix != "5") continue;

                    int missing = BitCount(all & ~rel);
                    int score = missing * 100 + shape.Rank + (root == bass ? 0 : 40);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        string name = RootName(root, shape.Suffix) + shape.Suffix;
                        best = root == bass ? name : name + "/" + NoteHelper.Pretty(bass);
                    }
                }
            }
            return best;
        }

        // Minor and diminished chords are usually spelled with sharps (C♯m, G♯m, not D♭m, A♭m); E♭m and B♭m stay.
        private static readonly string[] MinorRoots = { "C", "C♯", "D", "E♭", "E", "F", "F♯", "G", "G♯", "A", "B♭", "B" };

        private static string RootName(int root, string suffix)
        {
            bool minor = suffix.StartsWith("m", StringComparison.Ordinal) && !suffix.StartsWith("maj", StringComparison.Ordinal) || suffix.StartsWith("dim", StringComparison.Ordinal);
            return minor ? MinorRoots[root] : NoteHelper.Pretty(root);
        }

        /// <summary>Note names with octaves, low to high and without repeats: "C4 E4". Null for no keys.</summary>
        public static string? NoteNames(IEnumerable<int> notes, int max = 8)
        {
            var list = notes.Where(n => n is >= 0 and < 128).Distinct().OrderBy(n => n).ToList();
            if (list.Count == 0) return null;
            var names = list.Take(max).Select(n => NoteHelper.Pretty(n) + NoteHelper.Octave(n));
            return string.Join(" ", names) + (list.Count > max ? " …" : "");
        }

        private static int Rotate(int mask, int root)
        {
            int r = 0;
            for (int pc = 0; pc < 12; pc++)
                if ((mask & (1 << pc)) != 0) r |= 1 << ((pc - root + 12) % 12);
            return r;
        }

        private static int BitCount(int v)
        {
            int c = 0;
            for (; v != 0; v &= v - 1) c++;
            return c;
        }
    }
}
