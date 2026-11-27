using nMIDI.Theory;
using System;
using System.Collections.Generic;
using System.Linq;

namespace nMIDI.PianoRoll
{
    /// <summary>
    /// Which note each row of the piano roll shows. The piano layout has a row for every note, the highest on top
    /// (row = 127 − note). The drum layout has one row per drum only: the drums of a basic kit and every key the
    /// drum track uses, still the highest key on top, so a kit of 15 drums fills 15 tall, named rows.
    /// </summary>
    public sealed class RollRows
    {
        private readonly int[] _noteAt;
        private readonly int[] _rowOf = new int[128];

        private RollRows(int[] notesTopDown, bool isDrums)
        {
            _noteAt = notesTopDown;
            IsDrums = isDrums;
            Array.Fill(_rowOf, -1);
            for (int row = 0; row < notesTopDown.Length; row++) _rowOf[notesTopDown[row]] = row;
        }

        /// <summary>A row for every note, 127 on top.</summary>
        public static RollRows Piano { get; } = new(Enumerable.Range(0, 128).Reverse().ToArray(), isDrums: false);

        /// <summary>One row per drum: the basic kit and the keys in <paramref name="usedNotes"/>.</summary>
        public static RollRows Drums(IEnumerable<int> usedNotes)
        {
            var notes = new SortedSet<int>(DrumMap.CommonKit);
            foreach (int n in usedNotes) if (n is >= 0 and <= 127) notes.Add(n);
            return new RollRows(notes.Reverse().ToArray(), isDrums: true);
        }

        public bool IsDrums { get; }

        public int Count => _noteAt.Length;

        /// <summary>The row of a note, or -1 when the layout has no row for it.</summary>
        public int RowOf(int note) => (uint)note < 128 ? _rowOf[note] : -1;

        /// <summary>The note of a row; rows above or below the layout give its first or last note.</summary>
        public int NoteAt(int row) => _noteAt[Math.Clamp(row, 0, _noteAt.Length - 1)];

        /// <summary>The note <paramref name="rows"/> rows below this one (negative: above), staying inside the layout.</summary>
        public int Shift(int note, int rows)
        {
            int row = RowOf(note);
            return row < 0 ? note : NoteAt(row + rows);
        }

        /// <summary>Notes shown, top row first.</summary>
        public IReadOnlyList<int> Notes => _noteAt;

        public bool SameLayout(RollRows other) => IsDrums == other.IsDrums && _noteAt.AsSpan().SequenceEqual(other._noteAt);
    }
}
