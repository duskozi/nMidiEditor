using nMIDI.Theory;
using System;
using System.Collections.Generic;
using System.Linq;

namespace nMIDI.Model
{
    /// <summary>A chord over a stretch of the song; with <paramref name="IsNotes"/>, the names of single notes that make no chord.</summary>
    public readonly record struct ChordSpan(long Start, long End, string Name, bool IsNotes = false);

    /// <summary>
    /// The chords of a song, found beat by beat from the notes of the given tracks (drum tracks are left out).
    /// Each beat takes the keys that sound for a good part of it, so broken chords and arpeggios are named too;
    /// a key that sounds only briefly (a passing note) is dropped when the rest makes a chord without it.
    /// Beats with the same chord join into one span.
    /// </summary>
    public sealed class ChordTimeline
    {
        private readonly ChordSpan[] _spans;
        private readonly long[] _starts;

        public ChordTimeline(IEnumerable<SongTrack> tracks, Song song)
        {
            var notes = tracks.Where(t => !t.IsDrums).SelectMany(t => t.Notes).OrderBy(n => n.Start).ToList();
            var spans = new List<ChordSpan>();
            if (notes.Count > 0)
            {
                long end = notes.Max(n => n.End);
                int next = 0;
                var active = new List<SongNote>();
                foreach (var (barTick, _, beatTicks, beats) in song.BarLines(0, end))
                {
                    long barEnd = barTick + beats * beatTicks;
                    while (next < notes.Count && notes[next].Start < barEnd) active.Add(notes[next++]);
                    active.RemoveAll(n => n.End <= barTick);
                    // A beat without a chord of its own (one or two keys of a broken chord) takes the bar's chord.
                    string? barName = null;
                    bool barDone = false;
                    for (int b = 0; b < beats; b++)
                    {
                        long from = barTick + b * beatTicks, to = from + beatTicks;
                        if (from >= end) break;
                        string? name = NameOf(active, from, to);
                        if (name == null && !active.Any(n => n.Start < to && n.End > from)) continue;
                        if (name == null)
                        {
                            if (!barDone) { barName = NameOf(active, barTick, barEnd); barDone = true; }
                            name = barName;
                        }
                        // No chord in the beat or the bar: the notes themselves, when there are only a few.
                        bool isNotes = false;
                        if (name == null)
                        {
                            name = NotesOf(active, from, to);
                            isNotes = true;
                        }
                        if (name == null) continue;
                        if (spans.Count > 0 && spans[^1].Name == name && spans[^1].IsNotes == isNotes && spans[^1].End == from)
                            spans[^1] = spans[^1] with { End = to };
                        else
                            spans.Add(new ChordSpan(from, to, name, isNotes));
                    }
                }
            }
            _spans = spans.ToArray();
            _starts = _spans.Select(s => s.Start).ToArray();
        }

        public IReadOnlyList<ChordSpan> Spans => _spans;

        public bool IsEmpty => _spans.Length == 0;

        /// <summary>The chord at the tick, or null.</summary>
        public string? At(long tick) => SpanAt(tick)?.Name;

        /// <summary>The span at the tick, or null.</summary>
        public ChordSpan? SpanAt(long tick)
        {
            int i = Array.BinarySearch(_starts, tick);
            if (i < 0) i = ~i - 1;
            return i >= 0 && tick < _spans[i].End ? _spans[i] : null;
        }

        /// <summary>The spans that overlap [from, to).</summary>
        public IEnumerable<ChordSpan> Between(long from, long to)
        {
            int i = Array.BinarySearch(_starts, from);
            if (i < 0) i = Math.Max(0, ~i - 1);
            for (; i < _spans.Length && _spans[i].Start < to; i++)
                if (_spans[i].End > from) yield return _spans[i];
        }

        /// <summary>The names of the notes in a beat that make no chord (up to four keys), e.g. "E4" or "C4 E4".</summary>
        internal static string? NotesOf(IReadOnlyList<SongNote> notes, long from, long to)
        {
            var keys = notes.Where(n => n.Start < to && n.End > from).Select(n => n.Number).Distinct().ToList();
            return keys.Count is >= 1 and <= 4 ? ChordNamer.NoteNames(keys) : null;
        }

        /// <summary>The chord of the notes in one beat.</summary>
        internal static string? NameOf(IReadOnlyList<SongNote> notes, long from, long to)
        {
            long length = Math.Max(1, to - from);
            // How long each pitch class sounds in the beat, and the lowest key that sounds for a good part of it.
            var weight = new long[12];
            int bass = int.MaxValue;
            foreach (var n in notes)
            {
                long overlap = Math.Min(n.End, to) - Math.Max(n.Start, from);
                if (overlap <= 0) continue;
                weight[NoteHelper.PC(n.Number)] += overlap;
                if (overlap * 4 >= length && n.Number < bass) bass = n.Number;
            }
            if (bass == int.MaxValue) return null;

            // Keys that sound for at least a quarter of the beat, strongest first.
            var pcs = Enumerable.Range(0, 12).Where(pc => weight[pc] * 4 >= length).OrderByDescending(pc => weight[pc]).ToList();
            int bassPc = NoteHelper.PC(bass);
            // Try the full set, then drop the weakest keys (never the bass) one at a time.
            for (int drop = 0; drop <= 2 && pcs.Count - drop >= 2; drop++)
            {
                int mask = 1 << bassPc;
                foreach (int pc in pcs.Take(pcs.Count - drop)) mask |= 1 << pc;
                string? name = ChordNamer.Name(mask, bassPc);
                if (name != null) return name;
            }
            return null;
        }
    }
}
