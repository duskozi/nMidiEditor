using Melanchall.DryWetMidi.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace nMIDI.Model
{
    /// <summary>A copied time range of a song: per track, its notes and controllers, from the start of the range.</summary>
    public sealed record RangeClip(long Length, IReadOnlyList<(int Track, IReadOnlyList<SongNote> Notes, IReadOnlyList<SongCtrl> Controls)> Tracks);

    /// <summary>
    /// Song-wide edits: inserting and deleting time, copying and pasting time ranges, and track
    /// commands (add, duplicate, delete, move, merge, split). They change the song in place;
    /// the caller records the undo step first and calls <see cref="Song.Rebuild"/> after.
    /// </summary>
    public static class SongOps
    {
        // --- Time ---

        /// <summary>
        /// Push everything at or after <paramref name="at"/> later by <paramref name="length"/> ticks;
        /// notes that sound across it get longer.
        /// </summary>
        public static void InsertTime(Song song, long at, long length)
        {
            if (length <= 0) return;
            long Shift(long t) => t >= at ? t + length : t;

            foreach (var track in song.Tracks)
            {
                track.Notes = SongTrack.Sort(track.Notes.Select(n => n.Start >= at
                    ? n with { Start = n.Start + length, End = n.End + length }
                    : n.End > at ? n with { End = n.End + length } : n));
                track.Controls = track.Controls.Select(c => c with { Tick = Shift(c.Tick) }).OrderBy(c => c.Tick).ToList();
                track.Meta = track.Meta.Select(m => m with { Tick = Shift(m.Tick) }).ToList();
            }
            song.Tempos = song.Tempos.Select(t => t.Tick > 0 ? t with { Tick = Shift(t.Tick) } : t).ToList();
            song.TimeSignatures = song.TimeSignatures.Select(s => s.Tick > 0 ? s with { Tick = Shift(s.Tick) } : s).ToList();
            song.Conductor = song.Conductor.Select(m => m with { Tick = Shift(m.Tick) }).ToList();
        }

        /// <summary>
        /// Remove the time from <paramref name="from"/> to <paramref name="to"/> and pull what follows earlier.
        /// Notes that start inside are deleted; notes that sound into the range are cut at its start (or
        /// shortened, if they last past its end). The last controller, tempo and key values inside the
        /// range move to its start, so what follows still sounds the same.
        /// </summary>
        public static void DeleteTime(Song song, long from, long to)
        {
            if (to <= from) return;
            long length = to - from;

            foreach (var track in song.Tracks)
            {
                var notes = new List<SongNote>();
                foreach (var n in track.Notes)
                {
                    if (n.Start >= to) notes.Add(n with { Start = n.Start - length, End = n.End - length });
                    else if (n.Start >= from) continue;
                    else if (n.End <= from) notes.Add(n);
                    else if (n.End >= to) notes.Add(n with { End = n.End - length });
                    else notes.Add(n with { End = from });
                }
                track.Notes = SongTrack.Sort(notes);
                track.Controls = CarryControls(track.Controls, from, to);
                track.Meta = track.Meta.Where(m => m.Tick < from || m.Tick >= to)
                                       .Select(m => m.Tick >= to ? m with { Tick = m.Tick - length } : m).ToList();
            }

            // Tempo: the last change inside the range moves to its start (unless one already follows at its end).
            var tempos = song.Tempos.ToList();
            var lastTempo = tempos.LastOrDefault(t => t.Tick >= from && t.Tick < to && t.Tick > 0);
            tempos = tempos.Where(t => t.Tick < from || t.Tick >= to || t.Tick == 0)
                           .Select(t => t.Tick >= to ? t with { Tick = t.Tick - length } : t).ToList();
            if (lastTempo != null && !song.Tempos.Any(t => t.Tick == to)) tempos.Add(lastTempo with { Tick = from });
            song.Tempos = tempos;

            song.TimeSignatures = song.TimeSignatures.Where(s => s.Tick < from || s.Tick >= to || s.Tick == 0)
                                                     .Select(s => s.Tick >= to ? s with { Tick = s.Tick - length } : s).ToList();

            var conductor = new List<SongMeta>();
            SongMeta? lastKey = null;
            foreach (var m in song.Conductor)
            {
                if (m.Tick >= from && m.Tick < to)
                {
                    if (m.Event is KeySignatureEvent) lastKey = m;
                    continue;
                }
                conductor.Add(m.Tick >= to ? m with { Tick = m.Tick - length } : m);
            }
            if (lastKey != null && !song.Conductor.Any(m => m.Event is KeySignatureEvent && m.Tick == to))
                conductor.Add(lastKey with { Tick = from });
            song.Conductor = conductor;
        }

        /// <summary>Controllers without the range, with the last value of each controller inside it moved to the range start.</summary>
        private static List<SongCtrl> CarryControls(IReadOnlyList<SongCtrl> controls, long from, long to)
        {
            long length = to - from;
            var result = new List<SongCtrl>();
            var last = new Dictionary<(CtrlKind, int, int), SongCtrl>();
            var atEnd = new HashSet<(CtrlKind, int, int)>();
            foreach (var c in controls)
            {
                var key = (c.Kind, c.Number, c.Channel);
                if (c.Tick >= from && c.Tick < to) last[key] = c;
                else
                {
                    if (c.Tick == to) atEnd.Add(key);
                    result.Add(c.Tick >= to ? c with { Tick = c.Tick - length } : c);
                }
            }
            foreach (var (key, c) in last)
                if (!atEnd.Contains(key)) result.Add(c with { Tick = from });
            return result.OrderBy(c => c.Tick).ToList();
        }

        /// <summary>Copy the notes (starting in the range, cut at its end) and controllers of some tracks.</summary>
        public static RangeClip CopyRange(Song song, long from, long to, IEnumerable<SongTrack>? tracks = null)
        {
            var list = new List<(int, IReadOnlyList<SongNote>, IReadOnlyList<SongCtrl>)>();
            foreach (var track in tracks ?? song.Tracks)
            {
                var notes = track.Notes.Where(n => n.Start >= from && n.Start < to)
                                       .Select(n => n with { Start = n.Start - from, End = Math.Min(n.End, to) - from }).ToList();
                var controls = track.Controls.Where(c => c.Tick >= from && c.Tick < to)
                                             .Select(c => c with { Tick = c.Tick - from }).ToList();
                list.Add((track.Index, notes, controls));
            }
            return new RangeClip(to - from, list);
        }

        /// <summary>
        /// Paste a copied range at <paramref name="at"/>. With <paramref name="insert"/>, the song first gets
        /// that much time inserted there; otherwise the range is mixed into what is already there.
        /// Each copied track goes to the track at the same position plus <paramref name="trackOffset"/>.
        /// </summary>
        public static void PasteRange(Song song, RangeClip clip, long at, bool insert, int trackOffset = 0)
        {
            if (insert) InsertTime(song, at, clip.Length);
            foreach (var (index, notes, controls) in clip.Tracks)
            {
                int target = index + trackOffset;
                if (target < 0 || target >= song.Tracks.Count) continue;
                var track = song.Tracks[target];
                track.Notes = SongTrack.Sort(track.Notes.Concat(notes.Select(n => n with { Start = n.Start + at, End = n.End + at })));
                track.Controls = track.Controls.Concat(controls.Select(c => c with { Tick = c.Tick + at })).OrderBy(c => c.Tick).ToList();
            }
        }

        /// <summary>Delete what starts in a range of some tracks, without moving anything else.</summary>
        public static void ClearRange(Song song, long from, long to, IEnumerable<SongTrack> tracks)
        {
            foreach (var track in tracks)
            {
                track.Notes = track.Notes.Where(n => n.Start < from || n.Start >= to).ToList();
                track.Controls = track.Controls.Where(c => c.Tick < from || c.Tick >= to).ToList();
            }
        }

        /// <summary>Length of <paramref name="bars"/> bars starting at the bar that contains <paramref name="tick"/>, and that bar's start.</summary>
        public static (long Start, long Length) Bars(Song song, long tick, int bars)
        {
            var (bar, _, _) = song.ToBarBeat(tick);
            long start = song.FromBarBeat(bar, 0, 0);
            long end = song.FromBarBeat(bar + Math.Max(1, bars), 0, 0);
            return (start, end - start);
        }

        // --- Tracks ---

        /// <summary>The lowest channel no track uses (skipping the drum channel), or 0.</summary>
        public static int FreeChannel(Song song)
        {
            var used = song.Tracks.SelectMany(t => t.Channels).Append(9).ToHashSet();
            for (int c = 0; c < 16; c++) if (!used.Contains(c)) return c;
            return 0;
        }

        public static SongTrack AddTrack(Song song, int position, string? name = null, int? channel = null)
        {
            int ch = channel ?? FreeChannel(song);
            var track = new SongTrack(name ?? $"Track {song.Tracks.Count + 1}", ch);
            if (ch != 9) track.Controls = new List<SongCtrl> { new(0, ch, CtrlKind.Program, 0, 0) };
            var tracks = song.Tracks.ToList();
            tracks.Insert(Math.Clamp(position, 0, tracks.Count), track);
            song.SetTracks(tracks);
            return track;
        }

        public static SongTrack DuplicateTrack(Song song, SongTrack source)
        {
            var copy = new SongTrack(source.Name + " copy", source.Channel)
            {
                Notes = source.Notes.ToList(),
                Controls = source.Controls.ToList(),
                Meta = source.Meta.ToList(),
            };
            var tracks = song.Tracks.ToList();
            tracks.Insert(tracks.IndexOf(source) + 1, copy);
            song.SetTracks(tracks);
            return copy;
        }

        public static void DeleteTrack(Song song, SongTrack track) =>
            song.SetTracks(song.Tracks.Where(t => t != track));

        /// <summary>Move a track up (negative) or down in the list.</summary>
        public static void MoveTrack(Song song, SongTrack track, int by)
        {
            var tracks = song.Tracks.ToList();
            int i = tracks.IndexOf(track);
            int j = Math.Clamp(i + by, 0, tracks.Count - 1);
            if (i < 0 || i == j) return;
            tracks.RemoveAt(i);
            tracks.Insert(j, track);
            song.SetTracks(tracks);
        }

        /// <summary>Put the notes, controllers and meta events of <paramref name="others"/> into <paramref name="into"/> and remove them.</summary>
        public static void MergeTracks(Song song, SongTrack into, IReadOnlyCollection<SongTrack> others)
        {
            var merged = others.Where(t => t != into).ToList();
            if (merged.Count == 0) return;
            into.Notes = SongTrack.Sort(into.Notes.Concat(merged.SelectMany(t => t.Notes)));
            into.Controls = into.Controls.Concat(merged.SelectMany(t => t.Controls)).OrderBy(c => c.Tick).ToList();
            into.Meta = into.Meta.Concat(merged.SelectMany(t => t.Meta)).OrderBy(m => m.Tick).ToList();
            song.SetTracks(song.Tracks.Where(t => !merged.Contains(t)));
        }

        /// <summary>Give each channel of a track its own track. Returns the new tracks (the original keeps its first channel).</summary>
        public static List<SongTrack> SplitByChannel(Song song, SongTrack track)
        {
            var channels = track.Channels;
            var created = new List<SongTrack>();
            if (channels.Count < 2) return created;
            var tracks = song.Tracks.ToList();
            int position = tracks.IndexOf(track);
            var allNotes = track.Notes;
            var allControls = track.Controls;

            foreach (int ch in channels)
            {
                var target = ch == channels[0] ? track : new SongTrack($"{track.Name} ch {ch + 1}", ch);
                target.Channel = ch;
                target.Notes = allNotes.Where(n => n.Channel == ch).ToList();
                target.Controls = allControls.Where(c => c.Channel == ch).ToList();
                if (target != track)
                {
                    tracks.Insert(++position, target);
                    created.Add(target);
                }
            }
            song.SetTracks(tracks);
            return created;
        }

        /// <summary>Move the notes below <paramref name="splitPoint"/> to a new track under this one (for example a left hand).</summary>
        public static SongTrack SplitByPitch(Song song, SongTrack track, int splitPoint, string lowName)
        {
            var low = new SongTrack(lowName, track.Channel)
            {
                Notes = track.Notes.Where(n => n.Number < splitPoint).ToList(),
                Controls = track.Controls.ToList(),   // both halves keep the controllers (pedal, volume...)
            };
            track.Notes = track.Notes.Where(n => n.Number >= splitPoint).ToList();
            var tracks = song.Tracks.ToList();
            tracks.Insert(tracks.IndexOf(track) + 1, low);
            song.SetTracks(tracks);
            return low;
        }

        /// <summary>Move every note and controller of a track to another channel.</summary>
        public static void SetChannel(SongTrack track, int channel)
        {
            channel = Math.Clamp(channel, 0, 15);
            track.Channel = channel;
            track.Notes = track.Notes.Select(n => n with { Channel = channel }).ToList();
            track.Controls = track.Controls.Select(c => c with { Channel = channel }).ToList();
        }

        /// <summary>Set a controller or program value at tick 0 of the track's channel, replacing one that is there.</summary>
        public static void SetInitial(SongTrack track, CtrlKind kind, int number, int value)
        {
            var controls = track.Controls.Where(c => !(c.Tick == 0 && c.Kind == kind && c.Number == number && c.Channel == track.Channel)).ToList();
            controls.Insert(0, new SongCtrl(0, track.Channel, kind, number, value));
            track.Controls = controls.OrderBy(c => c.Tick).ToList();
        }

        /// <summary>The value at tick 0 (or the first one) of a controller or program on the track's channel.</summary>
        public static int? GetInitial(SongTrack track, CtrlKind kind, int number) =>
            track.Controls.Where(c => c.Kind == kind && c.Number == number && c.Channel == track.Channel)
                          .Select(c => (int?)c.Value).FirstOrDefault();
    }
}
