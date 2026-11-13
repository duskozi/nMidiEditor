using nMIDI.Theory;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace nMIDI.Model
{
    /// <summary>Hits out of a total, shown as a percentage.</summary>
    public readonly record struct Score(int Hit, int Total)
    {
        public double Percent => Total == 0 ? double.NaN : 100.0 * Hit / Total;
        public override string ToString() => Total == 0 ? "–" : string.Create(CultureInfo.InvariantCulture, $"{Percent:0.0}%  ({Hit:N0} of {Total:N0})");
    }

    /// <summary>Hand and finger someone else wrote for a note: in the MIDI file, or in a PIG fingering file.</summary>
    public readonly record struct TruthFinger(Hand Hand, int Finger);

    /// <summary>One note the report marks.</summary>
    public sealed record FingeringDifference(SongTrack Track, SongNote Note, string Why);

    public sealed record BarRow(int Bar, long Tick, int Notes, int HandDiffs, int FingerDiffs, int Corrections, string FingerMatch);

    /// <summary>
    /// How the fingering of a part compares with what the file says: the file's hands (separate LH/RH tracks)
    /// against the algorithm's own split, fingers written in the file (text or lyric "1"…"5") or in a PIG file
    /// against the engine's fingers, the user's corrections against the engine's first choice, and the
    /// incremental result shown in the editor against a fresh full analysis.
    /// </summary>
    public sealed class FingeringReport
    {
        public required FingeringGroup Group { get; init; }
        public int Notes { get; init; }
        /// <summary>The algorithm's hands (file hands ignored) against the file's hands; empty when the file has none.</summary>
        public Score HandAgreement { get; init; }
        /// <summary>Shown fingers against the written fingers.</summary>
        public Score FingerMatch { get; init; }
        /// <summary>Hand and finger both right, where the written finger has a hand (or the file gives the hands).</summary>
        public Score BothRight { get; init; }
        /// <summary>The engine's own choice (corrections ignored) against the user's corrections.</summary>
        public Score Corrections { get; init; }
        /// <summary>Shown (incremental) result against a fresh full run.</summary>
        public Score Stability { get; init; }
        /// <summary>Notes fingered from a standard scale chart.</summary>
        public Score Chart { get; init; }
        public string TruthSource { get; init; } = "";
        public IReadOnlyList<BarRow> Bars { get; init; } = Array.Empty<BarRow>();
        public IReadOnlyList<FingeringDifference> Differences { get; init; } = Array.Empty<FingeringDifference>();
        public TimeSpan Elapsed { get; init; }

        public static FingeringReport Build(Song song, FingeringGroup group, IReadOnlyDictionary<SongNote, TruthFinger>? pig = null,
                                            CancellationToken cancel = default)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var timeline = group.Tracks[0].Timeline ?? throw new InvalidOperationException("The song has not been rebuilt.");
            var tracks = group.Tracks;
            var shown = tracks.Select(t => t.FingeringNow()).ToArray();
            var full = SongFingering.FullRun(group, timeline, withLocks: true, cancel: cancel);
            bool anyLocks = tracks.Any(t => t.FingeringInfo.Locks.Count > 0);
            var raw = anyLocks ? SongFingering.FullRun(group, timeline, withLocks: false, cancel: cancel) : full;
            var algo = group.HandsGiven ? SongFingering.FullRun(group, timeline, withLocks: false, algorithmHands: true, cancel: cancel) : null;

            int notes = 0, handHit = 0, handTotal = 0, fHit = 0, fTotal = 0, bHit = 0, bTotal = 0, cHit = 0, cTotal = 0, sHit = 0, chart = 0;
            var diffs = new List<FingeringDifference>();
            var bars = new SortedDictionary<int, (long Tick, int Notes, int Hand, int Finger, int Corr, int FHit, int FTotal)>();
            string source = pig != null ? "PIG file" : "fingers written in the MIDI file";

            for (int t = 0; t < tracks.Length; t++)
            {
                var track = tracks[t];
                var info = track.FingeringInfo;
                var list = track.Notes;
                if (shown[t].Length != list.Count) continue;
                // A finger written in the file belongs to the note at its tick only when one note starts there.
                var starts = list.GroupBy(n => n.Start).ToDictionary(g => g.Key, g => g.Count());
                for (int i = 0; i < list.Count; i++)
                {
                    var n = list[i];
                    var s = shown[t][i];
                    notes++;
                    int bar = BarOf(song, n.Start);
                    bars.TryGetValue(bar, out var row);
                    if (row.Notes == 0 || n.Start < row.Tick) row.Tick = n.Start;
                    row.Notes++;

                    if (full[t][i].Hand == s.Hand && full[t][i].Finger == s.Finger) sHit++;
                    if (s.FromChart) chart++;

                    var whys = new List<string>();
                    if (algo != null && group.Hands[t] != Hand.None)
                    {
                        handTotal++;
                        if (algo[t][i].Hand == group.Hands[t]) handHit++;
                        else { row.Hand++; whys.Add($"the algorithm alone puts it in the {Name(algo[t][i].Hand)}, the file in the {Name(group.Hands[t])}"); }
                    }

                    TruthFinger? truth = null;
                    if (pig != null) { if (pig.TryGetValue(n, out var p)) truth = p; }
                    else if (info.FileFingers.TryGetValue(n.Start, out var ff) && starts[n.Start] == 1) truth = new TruthFinger(ff.Hand, ff.Finger);
                    if (truth is { } tr)
                    {
                        fTotal++; row.FTotal++;
                        if (tr.Finger == s.Finger) { fHit++; row.FHit++; }
                        else { row.Finger++; whys.Add($"finger {s.Finger}, the {source} says {tr.Finger}"); }
                        var truthHand = tr.Hand != Hand.None ? tr.Hand : group.HandsGiven ? group.Hands[t] : Hand.None;
                        if (truthHand != Hand.None)
                        {
                            bTotal++;
                            if (truthHand == s.Hand && tr.Finger == s.Finger) bHit++;
                            else if (truthHand != s.Hand) whys.Add($"{Name(s.Hand)}, the {source} says {Name(truthHand)}");
                        }
                    }

                    if (info.Locks.TryGetValue((n.Number, n.Start), out var l))
                    {
                        cTotal++; row.Corr++;
                        if (raw[t][i].Finger == l.Finger) cHit++;
                    }
                    bars[bar] = row;
                    if (whys.Count > 0) diffs.Add(new FingeringDifference(track, n, string.Join("; ", whys)));
                }
            }

            return new FingeringReport
            {
                Group = group,
                Notes = notes,
                HandAgreement = new Score(handHit, handTotal),
                FingerMatch = new Score(fHit, fTotal),
                BothRight = new Score(bHit, bTotal),
                Corrections = new Score(cHit, cTotal),
                Stability = new Score(sHit, notes),
                Chart = new Score(chart, notes),
                TruthSource = fTotal > 0 ? source : "",
                Bars = bars.Select(kv => new BarRow(kv.Key, kv.Value.Tick, kv.Value.Notes, kv.Value.Hand, kv.Value.Finger, kv.Value.Corr,
                    kv.Value.FTotal == 0 ? "" : string.Create(CultureInfo.InvariantCulture, $"{100.0 * kv.Value.FHit / kv.Value.FTotal:0}%"))).ToList(),
                Differences = diffs.OrderBy(d => d.Note.Start).ThenBy(d => d.Note.Number).ToList(),
                Elapsed = clock.Elapsed,
            };
        }

        private static string Name(Hand h) => h switch { Hand.Left => "left hand", Hand.Right => "right hand", _ => "no hand" };

        private static int BarOf(Song song, long tick)
        {
            var text = song.FormatBars(tick).Trim();
            return int.TryParse(text.Split('.')[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int bar) ? bar : 1;
        }

        // --- PIG files ---

        /// <summary>
        /// Reads a PIG fingering file ("id onset offset pitch onvel offvel channel finger"; channel 0 = right hand,
        /// 1 = left; a negative finger is the left hand; "3_1" is a substitution, the first finger counts) and matches
        /// its notes to the group's notes by key and time (the first onsets are aligned).
        /// </summary>
        public static Dictionary<SongNote, TruthFinger> MatchPig(string path, FingeringGroup group, out int pigNotes)
        {
            var timeline = group.Tracks[0].Timeline ?? throw new InvalidOperationException("The song has not been rebuilt.");
            var pig = new List<(double Onset, int Pitch, Hand Hand, int Finger)>();
            foreach (var line in File.ReadLines(path))
            {
                if (line.StartsWith("//", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(line)) continue;
                var f = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);   // any whitespace
                if (f.Length < 8 || !double.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double onset)) continue;
                int pitch;
                try { pitch = nPianoFingering.Keyboard.ParsePitch(f[3]); }
                catch (Exception ex) when (ex is FormatException or ArgumentException) { continue; }
                var fingerText = f[7].Split('_')[0];
                if (!int.TryParse(fingerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int finger) || finger == 0) continue;
                var hand = f[6] == "1" || finger < 0 ? Hand.Left : Hand.Right;
                pig.Add((onset, pitch, hand, Math.Abs(finger)));
            }
            pigNotes = pig.Count;
            var result = new Dictionary<SongNote, TruthFinger>(ReferenceEqualityComparer.Instance);
            if (pig.Count == 0) return result;

            var song = group.Tracks.SelectMany(t => t.Notes).Select(n => (Note: n, Time: timeline.TickToTime(n.Start) / 1e6)).OrderBy(x => x.Time).ToList();
            if (song.Count == 0) return result;
            double shift = song[0].Time - pig.Min(p => p.Onset);
            var byPitch = song.GroupBy(x => x.Note.Number).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var p in pig)
            {
                if (!byPitch.TryGetValue(p.Pitch, out var candidates)) continue;
                double t = p.Onset + shift;
                SongNote? best = null;
                double bestDist = 0.08;
                foreach (var c in candidates)
                {
                    double d = Math.Abs(c.Time - t);
                    if (d < bestDist && !result.ContainsKey(c.Note)) { best = c.Note; bestDist = d; }
                }
                if (best != null) result[best] = new TruthFinger(p.Hand, p.Finger);
            }
            return result;
        }
    }
}
