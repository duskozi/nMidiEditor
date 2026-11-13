using System;
using System.Collections.Generic;
using System.Linq;

namespace nMIDI.Model
{
    /// <summary>
    /// Note edits as pure functions: each takes notes and returns new ones, so the piano roll can
    /// show a preview while dragging and the same code makes the final edit.
    /// </summary>
    public static class NoteOps
    {
        /// <summary>Notes compared by reference, so two identical notes are still two notes.</summary>
        public static HashSet<SongNote> NewSet(IEnumerable<SongNote>? notes = null) =>
            notes == null ? new HashSet<SongNote>(ReferenceEqualityComparer.Instance)
                          : new HashSet<SongNote>(notes, ReferenceEqualityComparer.Instance);

        /// <summary>Nearest grid line (grid 0 or less = no snapping).</summary>
        public static long Snap(double tick, long grid) =>
            grid > 0 ? (long)Math.Round(tick / grid) * grid : (long)Math.Round(tick);

        /// <summary>Grid line at or before the tick.</summary>
        public static long SnapDown(double tick, long grid) =>
            grid > 0 ? (long)Math.Floor(tick / grid) * grid : (long)Math.Floor(tick);

        /// <summary>Move notes in time and pitch, kept at tick 0 or later and inside 0–127.</summary>
        public static List<SongNote> Move(IReadOnlyCollection<SongNote> notes, long ticks, int semitones)
        {
            if (notes.Count == 0) return new List<SongNote>();
            ticks = Math.Max(ticks, -notes.Min(n => n.Start));
            semitones = Math.Clamp(semitones, -notes.Min(n => n.Number), 127 - notes.Max(n => n.Number));
            return notes.Select(n => n with { Start = n.Start + ticks, End = n.End + ticks, Number = n.Number + semitones }).ToList();
        }

        /// <summary>Move the end of every note by <paramref name="ticks"/>, keeping at least <paramref name="minLength"/>.</summary>
        public static List<SongNote> ResizeEnds(IEnumerable<SongNote> notes, long ticks, long minLength) =>
            notes.Select(n => n with { End = Math.Max(n.Start + Math.Max(1, minLength), n.End + ticks) }).ToList();

        /// <summary>Move the start of every note by <paramref name="ticks"/>, keeping at least <paramref name="minLength"/>.</summary>
        public static List<SongNote> ResizeStarts(IEnumerable<SongNote> notes, long ticks, long minLength) =>
            notes.Select(n => n with { Start = Math.Clamp(n.Start + ticks, 0, Math.Max(0, n.End - Math.Max(1, minLength))) }).ToList();

        /// <summary>Set every note to this length.</summary>
        public static List<SongNote> SetLength(IEnumerable<SongNote> notes, long length) =>
            notes.Select(n => n with { End = n.Start + Math.Max(1, length) }).ToList();

        /// <summary>Split the notes that start before and end after <paramref name="tick"/>; others are returned unchanged.</summary>
        public static List<SongNote> Split(IEnumerable<SongNote> notes, long tick)
        {
            var result = new List<SongNote>();
            foreach (var n in notes)
            {
                if (n.Start < tick && n.End > tick)
                {
                    result.Add(n with { End = tick });
                    result.Add(n with { Start = tick });
                }
                else result.Add(n);
            }
            return result;
        }

        /// <summary>Notes with their velocity changed by a function, kept in 1–127.</summary>
        public static List<SongNote> MapVelocity(IEnumerable<SongNote> notes, Func<SongNote, int, int> velocity) =>
            notes.Select((n, i) => n with { Velocity = Math.Clamp(velocity(n, i), 1, 127) }).ToList();

        /// <summary>
        /// Replace the notes of a track: <paramref name="remove"/> go out and <paramref name="add"/> come in.
        /// Returns the new list, sorted.
        /// </summary>
        public static List<SongNote> Replace(IEnumerable<SongNote> notes, IEnumerable<SongNote> remove, IEnumerable<SongNote> add)
        {
            var gone = NewSet(remove);
            return SongTrack.Sort(notes.Where(n => !gone.Contains(n)).Concat(add));
        }

        /// <summary>
        /// The same over several edited tracks: each removed note goes out of the track that has it, and each added
        /// note goes into the track of its source note (<paramref name="sources"/>[i] for <paramref name="add"/>[i],
        /// e.g. the note it was moved or copied from), or into <paramref name="fallback"/>. With
        /// <paramref name="moveLocks"/>, finger corrections move from each source note to its new note.
        /// </summary>
        public static void ReplaceInTracks(IReadOnlyList<SongTrack> tracks, IReadOnlyList<SongNote> remove, IReadOnlyList<SongNote> add,
                                           IReadOnlyList<SongNote>? sources, SongTrack fallback, bool moveLocks = false)
        {
            var owner = new Dictionary<SongNote, SongTrack>(ReferenceEqualityComparer.Instance);
            var wanted = NewSet(remove);
            if (sources != null) wanted.UnionWith(sources);
            foreach (var t in tracks)
                foreach (var n in t.Notes)
                    if (wanted.Contains(n)) owner.TryAdd(n, t);

            var removeBy = new Dictionary<SongTrack, List<SongNote>>();
            var addBy = new Dictionary<SongTrack, List<SongNote>>();
            var lockBy = new Dictionary<SongTrack, (List<SongNote> From, List<SongNote> To)>();
            List<SongNote> Get(Dictionary<SongTrack, List<SongNote>> d, SongTrack t) => d.TryGetValue(t, out var l) ? l : d[t] = new List<SongNote>();

            foreach (var n in remove) if (owner.TryGetValue(n, out var t)) Get(removeBy, t).Add(n);
            for (int i = 0; i < add.Count; i++)
            {
                var src = sources != null && i < sources.Count ? sources[i] : null;
                var t = src != null && owner.TryGetValue(src, out var o) ? o : fallback;
                Get(addBy, t).Add(add[i]);
                if (moveLocks && src != null)
                {
                    if (!lockBy.TryGetValue(t, out var pair)) lockBy[t] = pair = (new List<SongNote>(), new List<SongNote>());
                    pair.From.Add(src);
                    pair.To.Add(add[i]);
                }
            }
            foreach (var t in removeBy.Keys.Concat(addBy.Keys).Distinct().ToList())
                t.Notes = Replace(t.Notes, removeBy.GetValueOrDefault(t) ?? new List<SongNote>(), addBy.GetValueOrDefault(t) ?? new List<SongNote>());
            foreach (var (t, pair) in lockBy) t.MoveFingerLocks(pair.From, pair.To);
        }

        /// <summary>The track among <paramref name="tracks"/> that has this note (by reference), or null.</summary>
        public static SongTrack? OwnerOf(IEnumerable<SongTrack> tracks, SongNote note) =>
            tracks.FirstOrDefault(t => t.Notes.Contains(note, ReferenceEqualityComparer.Instance));
    }
}
