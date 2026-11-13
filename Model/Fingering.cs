using Melanchall.DryWetMidi.Core;
using nMIDI.Theory;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Engine = nPianoFingering;

namespace nMIDI.Model
{
    /// <summary>How a track is fingered.</summary>
    public enum FingerMode
    {
        /// <summary>Keyboard instruments only; the hand comes from the track name, LH/RH or two piano tracks are paired.</summary>
        Auto,
        /// <summary>Split between both hands by the algorithm.</summary>
        BothHands,
        LeftHand,
        RightHand,
        /// <summary>No fingers.</summary>
        Off,
    }

    /// <summary>Where the hands of a part come from when the file gives them.</summary>
    public enum HandsFrom { Auto, Tracks, Algorithm }

    /// <summary>A finger (and hand) fixed by the user for the note with this key that starts at <see cref="Tick"/>.</summary>
    public readonly record struct FingerLock(int Number, long Tick, Hand Hand, int Finger);

    /// <summary>
    /// The fingering data a track keeps in its meta events, so that it is saved in the MIDI file and goes
    /// through undo with the rest of the track:
    /// <list type="bullet">
    /// <item>"nMIDI:fingering mode=LeftHand pair=2" at tick 0: the track's mode and the track it is paired with;</item>
    /// <item>"nMIDI:finger 60 R3" at a note's tick: finger 3 of the right hand fixed on key 60;</item>
    /// <item>plain text or lyric events "1"…"5" (optionally "L3", "R3") at a note's tick are fingers written in the
    ///   file by someone else; the report compares the engine with them.</item>
    /// </list>
    /// </summary>
    public sealed class TrackFingeringInfo
    {
        public const string Prefix = "nMIDI:";
        private const string ModeTag = "nMIDI:fingering";
        private const string LockTag = "nMIDI:finger";

        public static readonly TrackFingeringInfo Empty = new();

        public FingerMode Mode { get; private init; }
        /// <summary>Index of the track this one is fingered together with, or -1.</summary>
        public int Pair { get; private init; } = -1;
        public IReadOnlyDictionary<(int Number, long Tick), FingerLock> Locks { get; private init; } = new Dictionary<(int, long), FingerLock>();
        /// <summary>Fingers written in the file: tick → (hand or None, finger).</summary>
        public IReadOnlyDictionary<long, (Hand Hand, int Finger)> FileFingers { get; private init; } = new Dictionary<long, (Hand, int)>();

        public static bool IsOwnEvent(MidiEvent e) => e is TextEvent t && t.Text != null && t.Text.StartsWith(Prefix, StringComparison.Ordinal);

        public static TrackFingeringInfo Parse(IReadOnlyList<SongMeta> meta)
        {
            if (meta.Count == 0) return Empty;
            var mode = FingerMode.Auto;
            int pair = -1;
            var locks = new Dictionary<(int, long), FingerLock>();
            var file = new Dictionary<long, (Hand, int)>();
            foreach (var m in meta)
            {
                string? text = m.Event switch { TextEvent t => t.Text, LyricEvent l => l.Text, _ => null };
                if (string.IsNullOrWhiteSpace(text)) continue;
                text = text.Trim();
                if (text.StartsWith(ModeTag, StringComparison.Ordinal))
                {
                    foreach (var part in text[ModeTag.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var kv = part.Split('=');
                        if (kv.Length != 2) continue;
                        if (kv[0] == "mode" && Enum.TryParse<FingerMode>(kv[1], out var md)) mode = md;
                        if (kv[0] == "pair" && int.TryParse(kv[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var p)) pair = p;
                    }
                }
                else if (text.StartsWith(LockTag + " ", StringComparison.Ordinal))
                {
                    var parts = text[(LockTag.Length + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var key)
                        && TryFinger(parts[1], out var hand, out var finger))
                        locks[(key, m.Tick)] = new FingerLock(key, m.Tick, hand, finger);
                }
                else if (!text.StartsWith(Prefix, StringComparison.Ordinal) && TryFinger(text, out var h, out var f))
                    file[m.Tick] = (h, f);
            }
            return new TrackFingeringInfo { Mode = mode, Pair = pair, Locks = locks, FileFingers = file };
        }

        /// <summary>"3", "R3", "L3", "r3"…</summary>
        private static bool TryFinger(string s, out Hand hand, out int finger)
        {
            hand = Hand.None;
            finger = 0;
            if (s.Length == 2 && (s[0] is 'L' or 'l' or 'R' or 'r'))
            {
                hand = s[0] is 'L' or 'l' ? Hand.Left : Hand.Right;
                s = s[1..];
            }
            if (s.Length != 1 || s[0] < '1' || s[0] > '5') return false;
            finger = s[0] - '0';
            return true;
        }

        private static string HandLetter(Hand h) => h switch { Hand.Left => "L", Hand.Right => "R", _ => "" };

        /// <summary>The track's meta events with the mode and pair replaced.</summary>
        public static List<SongMeta> WithSetup(IReadOnlyList<SongMeta> meta, FingerMode mode, int pair)
        {
            var list = meta.Where(m => !(m.Event is TextEvent t && t.Text?.StartsWith(ModeTag, StringComparison.Ordinal) == true)).ToList();
            if (mode != FingerMode.Auto || pair >= 0)
                list.Insert(0, new SongMeta(0, new TextEvent(string.Create(CultureInfo.InvariantCulture, $"{ModeTag} mode={mode} pair={pair}"))));
            return list;
        }

        /// <summary>The track's meta events with the finger locks replaced.</summary>
        public static List<SongMeta> WithLocks(IReadOnlyList<SongMeta> meta, IEnumerable<FingerLock> locks)
        {
            var list = meta.Where(m => !(m.Event is TextEvent t && t.Text?.StartsWith(LockTag + " ", StringComparison.Ordinal) == true)).ToList();
            foreach (var l in locks.OrderBy(l => l.Tick).ThenBy(l => l.Number))
            {
                string text = string.Create(CultureInfo.InvariantCulture, $"{LockTag} {l.Number} {HandLetter(l.Hand)}{l.Finger}");
                list.Add(new SongMeta(l.Tick, new TextEvent(text)));
            }
            return list.OrderBy(m => m.Tick).ToList();
        }
    }

    /// <summary>Fingering settings for every song (from the Settings dialog).</summary>
    public static class FingeringPrefs
    {
        private static int _version = 1;
        public static bool UseScaleCharts { get; private set; } = true;
        public static bool FingerChangeOnRepeats { get; private set; } = true;
        public static HandsFrom Hands { get; private set; } = HandsFrom.Auto;
        /// <summary>Write finger corrections and fingering modes into saved MIDI files.</summary>
        public static bool SaveInFile { get; set; } = true;
        public static int Version => Volatile.Read(ref _version);

        public static void Set(bool charts, bool repeats, HandsFrom hands)
        {
            if (charts == UseScaleCharts && repeats == FingerChangeOnRepeats && hands == Hands) return;
            UseScaleCharts = charts;
            FingerChangeOnRepeats = repeats;
            Hands = hands;
            Interlocked.Increment(ref _version);
        }

        public static Engine.AnalyzerOptions Options() => new()
        {
            Hands =
            {
                Source = Hands switch
                {
                    HandsFrom.Tracks => Engine.HandSource.Tracks,
                    HandsFrom.Algorithm => Engine.HandSource.Algorithm,
                    _ => Engine.HandSource.Auto,
                },
            },
            Fingering = { UseScaleCharts = UseScaleCharts, FingerChangeOnRepeats = FingerChangeOnRepeats },
        };
    }

    /// <summary>Tracks fingered together, with the hand each track gives (None = split by the algorithm).</summary>
    public sealed record FingeringGroup(SongTrack[] Tracks, Hand[] Hands, bool HandsGiven)
    {
        public string Describe() => Tracks.Length == 1
            ? Hands[0] switch { Hand.Left => "left hand", Hand.Right => "right hand", _ => "both hands" }
            : "paired: " + string.Join(" + ", Tracks.Select((t, i) => $"{t.Index + 1}. {t.Name} ({ShortHand(Hands[i])})"));

        private static string ShortHand(Hand hand) => hand switch { Hand.Left => "LH", Hand.Right => "RH", _ => "auto" };
    }

    /// <summary>
    /// Works out the fingering of every fingered track of a song, in the background and incrementally: one
    /// <see cref="Engine.FingeringSession"/> per group of tracks, so an edit refingers only the notes around it.
    /// Readers get the latest finished result; until a new one is ready, unchanged notes keep their old finger.
    /// </summary>
    public sealed class SongFingering
    {
        private readonly Song _song;
        private readonly object _gate = new();
        private readonly Dictionary<string, Engine.FingeringSession> _sessions = new();
        private int _sessionsVersion;

        private object[]? _requestedKey;
        private Job? _pending;
        private bool _running;
        private CancellationTokenSource? _cancel;

        private object[]? _groupsKey;
        private List<FingeringGroup> _groups = new();

        private Dictionary<SongTrack, TrackResult> _results = new(ReferenceEqualityComparer.Instance);
        private readonly ConditionalWeakTable<SongTrack, InterimCache> _interim = new();

        public SongFingering(Song song) { _song = song; }

        /// <summary>Raised (on a worker thread) when a new result is ready.</summary>
        public event EventHandler? Updated;

        /// <summary>Notes refingered by the last run and how long it took.</summary>
        public (int Refingered, bool Full, TimeSpan Elapsed) LastRun { get; private set; }

        private sealed record TrackResult(IReadOnlyList<SongNote> Notes, IReadOnlyList<SongMeta> Meta, PianoFinger[] Fingers);
        private sealed class InterimCache
        {
            public IReadOnlyList<SongNote>? Notes;
            public TrackResult? From;
            public PianoFinger[] Fingers = Array.Empty<PianoFinger>();
        }

        private sealed record GroupJob(FingeringGroup Group, IReadOnlyList<SongNote>[] Notes, TrackFingeringInfo[] Info, IReadOnlyList<SongMeta>[] Meta);

        private sealed record Job(
            object[] Key,
            List<GroupJob> Groups,
            SongTrack[] Unfingered,
            IReadOnlyList<SongNote>[] UnfingeredNotes,
            IReadOnlyList<SongMeta>[] UnfingeredMeta,
            TempoTimeline Timeline,
            int PrefsVersion);

        // --- Groups ---

        /// <summary>The groups the song's tracks are fingered in (tracks not listed are not fingered).</summary>
        public IReadOnlyList<FingeringGroup> Groups
        {
            get
            {
                var key = ShapeKey(includeNotes: false);
                lock (_gate)
                {
                    if (_groupsKey == null || !SameKey(_groupsKey, key)) { _groups = ResolveGroups(_song.Tracks); _groupsKey = key; }
                    return _groups;
                }
            }
        }

        public FingeringGroup? GroupOf(SongTrack track) => Groups.FirstOrDefault(g => g.Tracks.Contains(track));

        /// <summary>True for General MIDI keyboard instruments (pianos, harpsichord, clavinet, celesta, organs) and tracks named like piano parts.</summary>
        public static bool IsKeyboard(SongTrack t)
        {
            if (t.IsDrums) return false;
            var name = t.Name.ToLowerInvariant();
            if (PartFingering.HandFromTrackName(t.Name) != Hand.None) return true;
            if (name.Contains("piano") || name.Contains("klavier") || name.Contains("keys")) return true;
            var program = t.Controls.FirstOrDefault(c => c.Kind == CtrlKind.Program && c.Channel == t.Channel)
                          ?? t.Controls.FirstOrDefault(c => c.Kind == CtrlKind.Program);
            int p = program?.Value ?? 0;
            return p <= 8 || (p >= 16 && p <= 20);
        }

        internal static List<FingeringGroup> ResolveGroups(IReadOnlyList<SongTrack> tracks)
        {
            var groups = new List<FingeringGroup>();
            var used = new HashSet<SongTrack>(ReferenceEqualityComparer.Instance);
            Hand Named(SongTrack t) => t.FingeringInfo.Mode switch
            {
                FingerMode.LeftHand => Hand.Left,
                FingerMode.RightHand => Hand.Right,
                FingerMode.BothHands => Hand.None,
                _ => PartFingering.HandFromTrackName(t.Name),
            };
            bool Fingered(SongTrack t) => t.Notes.Count > 0 && !t.IsDrums && t.FingeringInfo.Mode switch
            {
                FingerMode.Off => false,
                FingerMode.Auto => IsKeyboard(t),
                _ => true,
            };
            void AddPair(SongTrack a, SongTrack b)
            {
                Hand ha = Named(a), hb = Named(b);
                bool given = ha != Hand.None && hb != Hand.None && ha != hb;
                if (!given)
                {
                    // Lower part = left hand, unless one of the tracks says otherwise.
                    double ma = a.Notes.Average(n => n.Number), mb = b.Notes.Average(n => n.Number);
                    if (ha != Hand.None) hb = ha == Hand.Left ? Hand.Right : Hand.Left;
                    else if (hb != Hand.None) ha = hb == Hand.Left ? Hand.Right : Hand.Left;
                    else { ha = ma <= mb ? Hand.Left : Hand.Right; hb = ha == Hand.Left ? Hand.Right : Hand.Left; }
                }
                var pair = a.Index <= b.Index ? new[] { a, b } : new[] { b, a };
                var hands = a.Index <= b.Index ? new[] { ha, hb } : new[] { hb, ha };
                groups.Add(new FingeringGroup(pair, hands, true));
                used.Add(a); used.Add(b);
            }

            // 1. Pairs the user chose.
            foreach (var t in tracks)
            {
                int p = t.FingeringInfo.Pair;
                if (used.Contains(t) || p < 0 || p >= tracks.Count || tracks[p] == t || used.Contains(tracks[p])) continue;
                if (!Fingered(t) && t.FingeringInfo.Mode == FingerMode.Off) continue;
                if (tracks[p].Notes.Count == 0 || t.Notes.Count == 0 || tracks[p].IsDrums || t.IsDrums) continue;
                AddPair(t, tracks[p]);
            }

            // 2. Automatic pairs among Auto tracks: one LH and one RH by name, or exactly two keyboard tracks.
            var auto = tracks.Where(t => !used.Contains(t) && Fingered(t) && t.FingeringInfo.Mode == FingerMode.Auto && t.FingeringInfo.Pair < 0).ToList();
            var lefts = auto.Where(t => PartFingering.HandFromTrackName(t.Name) == Hand.Left).ToList();
            var rights = auto.Where(t => PartFingering.HandFromTrackName(t.Name) == Hand.Right).ToList();
            if (lefts.Count == 1 && rights.Count == 1) AddPair(lefts[0], rights[0]);
            else if (auto.Count == 2 && Math.Abs(auto[0].Index - auto[1].Index) == 1) AddPair(auto[0], auto[1]);

            // 3. Everything else on its own.
            foreach (var t in tracks)
            {
                if (used.Contains(t) || !Fingered(t)) continue;
                var h = Named(t);
                groups.Add(new FingeringGroup(new[] { t }, new[] { h }, h != Hand.None));
            }
            return groups.OrderBy(g => g.Tracks[0].Index).ToList();
        }

        /// <summary>Forget all results and sessions: everything is fingered again from scratch.</summary>
        public void Reset()
        {
            lock (_gate)
            {
                _requestedKey = null;
                _cancel?.Cancel();
            }
            lock (_sessions) _sessions.Clear();
            Request();
        }

        // --- Reading results ---

        /// <summary>Hand and finger of each note of <paramref name="track"/> (same order as its notes).</summary>
        public PianoFinger[] For(SongTrack track)
        {
            Request();
            var results = Volatile.Read(ref _results);
            results.TryGetValue(track, out var done);
            if (done != null && ReferenceEquals(done.Notes, track.Notes)) return done.Fingers;
            return Interim(track, done);
        }

        /// <summary>The fingering of <paramref name="track"/>, worked out now if it is not ready (tests, exports, the report).</summary>
        public PianoFinger[] Now(SongTrack track)
        {
            var job = MakeJob(out var key);
            if (job != null && !IsDone(job))
            {
                lock (_sessions) Run(job, CancellationToken.None);
            }
            var results = Volatile.Read(ref _results);
            return results.TryGetValue(track, out var done) && ReferenceEquals(done.Notes, track.Notes) ? done.Fingers : Interim(track, done);
        }

        /// <summary>
        /// Fresh full analysis of a group, not cached (for the report). <paramref name="withLocks"/> false ignores the
        /// user's corrections; <paramref name="algorithmHands"/> true ignores the file's hands.
        /// </summary>
        public static PianoFinger[][] FullRun(FingeringGroup group, TempoTimeline timeline, bool withLocks = true, bool algorithmHands = false,
                                              CancellationToken cancel = default)
        {
            var notes = group.Tracks.Select(t => t.Notes).ToArray();
            var info = group.Tracks.Select(t => withLocks ? t.FingeringInfo : TrackFingeringInfo.Empty).ToArray();
            var parts = Build(group, notes, info, timeline);
            var options = FingeringPrefs.Options();
            if (algorithmHands) options.Hands.Source = Engine.HandSource.Algorithm;
            Engine.PianoAnalyzer.Analyze(parts, options, cancel);
            return Split(group, notes, parts);
        }

        private bool IsDone(Job job)
        {
            var results = Volatile.Read(ref _results);
            foreach (var g in job.Groups)
                for (int i = 0; i < g.Group.Tracks.Length; i++)
                {
                    if (!results.TryGetValue(g.Group.Tracks[i], out var r)) return false;
                    if (!ReferenceEquals(r.Notes, g.Notes[i]) || !ReferenceEquals(r.Meta, g.Meta[i])) return false;
                }
            return true;
        }

        private PianoFinger[] Interim(SongTrack track, TrackResult? done)
        {
            var notes = track.Notes;
            var cache = _interim.GetOrCreateValue(track);
            lock (cache)
            {
                if (ReferenceEquals(cache.Notes, notes) && ReferenceEquals(cache.From, done)) return cache.Fingers;
                var old = new Dictionary<SongNote, PianoFinger>(ReferenceEqualityComparer.Instance);
                if (done != null)
                    for (int i = 0; i < done.Notes.Count && i < done.Fingers.Length; i++) old[done.Notes[i]] = done.Fingers[i];
                var fingers = new PianoFinger[notes.Count];
                for (int i = 0; i < notes.Count; i++)
                    fingers[i] = old.TryGetValue(notes[i], out var f) ? f : new PianoFinger(notes[i].Number, Hand.None, 0);
                cache.Notes = notes; cache.From = done; cache.Fingers = fingers;
                return fingers;
            }
        }

        // --- Background work ---

        private object[] ShapeKey(bool includeNotes)
        {
            var tracks = _song.Tracks;
            var key = new object[2 + tracks.Count * 6];
            key[0] = includeNotes && tracks.Count > 0 ? (object?)tracks[0].Timeline ?? "" : "";
            key[1] = includeNotes ? FingeringPrefs.Version : 0;
            int k = 2;
            foreach (var t in tracks)
            {
                key[k++] = t;
                key[k++] = includeNotes ? t.Notes : t.Notes.Count > 0;
                key[k++] = t.Meta;
                key[k++] = t.Name;
                key[k++] = t.Channel;
                key[k++] = t.Controls;
            }
            return key;
        }

        private static bool SameKey(object[] a, object[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (!ReferenceEquals(a[i], b[i]) && !Equals(a[i], b[i])) return false;
            return true;
        }

        private Job? MakeJob(out object[] key)
        {
            key = ShapeKey(includeNotes: true);
            var timeline = _song.Tracks.Count > 0 ? _song.Tracks[0].Timeline : null;
            if (timeline == null) return null;
            var groups = Groups;
            var fingered = new HashSet<SongTrack>(groups.SelectMany(g => g.Tracks), ReferenceEqualityComparer.Instance);
            var others = _song.Tracks.Where(t => !fingered.Contains(t)).ToArray();
            return new Job(key,
                groups.Select(g => new GroupJob(g,
                                                g.Tracks.Select(t => t.Notes).ToArray(),
                                                g.Tracks.Select(t => t.FingeringInfo).ToArray(),
                                                g.Tracks.Select(t => t.Meta).ToArray())).ToList(),
                others, others.Select(t => t.Notes).ToArray(), others.Select(t => t.Meta).ToArray(), timeline, FingeringPrefs.Version);
        }

        private void Request()
        {
            var key = ShapeKey(includeNotes: true);
            lock (_gate)
            {
                if (_requestedKey != null && SameKey(_requestedKey, key)) return;
            }
            var job = MakeJob(out key);
            if (job == null) return;
            lock (_gate)
            {
                _requestedKey = key;
                _pending = job;
                _cancel?.Cancel();
                if (_running) return;
                _running = true;
            }
            Task.Run(Worker);
        }

        private void Worker()
        {
            while (true)
            {
                Job job;
                CancellationToken token;
                lock (_gate)
                {
                    if (_pending == null) { _running = false; return; }
                    job = _pending;
                    _pending = null;
                    // The previous run is over, so its token source can go.
                    _cancel?.Dispose();
                    _cancel = new CancellationTokenSource();
                    token = _cancel.Token;
                }
                try
                {
                    lock (_sessions) Run(job, token);
                    Updated?.Invoke(this, EventArgs.Empty);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IndexOutOfRangeException)
                {
                    // Never let the fingering break the editor: the tracks keep their previous fingers.
                }
            }
        }

        private void Run(Job job, CancellationToken token)
        {
            if (job.PrefsVersion != _sessionsVersion) { _sessions.Clear(); _sessionsVersion = job.PrefsVersion; }
            var results = new Dictionary<SongTrack, TrackResult>(ReferenceEqualityComparer.Instance);
            var live = new HashSet<string>();
            int refingered = 0;
            bool full = false;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            foreach (var g in job.Groups)
            {
                token.ThrowIfCancellationRequested();
                var key = string.Join("|", g.Group.Tracks.Select((t, i) => RuntimeHelpers.GetHashCode(t) + ":" + g.Group.Hands[i]));
                live.Add(key);
                if (!_sessions.TryGetValue(key, out var session)) _sessions[key] = session = new Engine.FingeringSession(FingeringPrefs.Options());
                var parts = Build(g.Group, g.Notes, g.Info, job.Timeline);
                var r = session.Update(parts, token);
                refingered += r.Refingered;
                full |= r.Full;
                var split = Split(g.Group, g.Notes, parts);
                for (int i = 0; i < g.Group.Tracks.Length; i++) results[g.Group.Tracks[i]] = new TrackResult(g.Notes[i], g.Meta[i], split[i]);
            }
            for (int i = 0; i < job.Unfingered.Length; i++)
                results[job.Unfingered[i]] = new TrackResult(job.UnfingeredNotes[i], job.UnfingeredMeta[i],
                    job.UnfingeredNotes[i].Select(n => new PianoFinger(n.Number, Hand.None, 0)).ToArray());
            foreach (var stale in _sessions.Keys.Where(k => !live.Contains(k)).ToList()) _sessions.Remove(stale);
            LastRun = (refingered, full, clock.Elapsed);
            Volatile.Write(ref _results, results);
        }

        private static List<Engine.PianoNote> Build(FingeringGroup group, IReadOnlyList<SongNote>[] notes, TrackFingeringInfo[] info, TempoTimeline timeline)
        {
            var parts = new List<Engine.PianoNote>(notes.Sum(n => n.Count));
            for (int t = 0; t < notes.Length; t++)
            {
                var source = group.Hands[t] switch { Hand.Left => Engine.Hand.Left, Hand.Right => Engine.Hand.Right, _ => Engine.Hand.Unknown };
                var locks = info[t].Locks;
                foreach (var n in notes[t])
                {
                    double start = timeline.TickToTime(n.Start) / 1e6, end = timeline.TickToTime(n.End) / 1e6;
                    var p = new Engine.PianoNote { Pitch = n.Number, Start = start, End = Math.Max(end, start + 0.001), Track = t, SourceHand = source };
                    if (locks.Count > 0 && locks.TryGetValue((n.Number, n.Start), out var l))
                    {
                        p.LockedFinger = l.Finger;
                        p.LockedHand = l.Hand switch { Hand.Left => Engine.Hand.Left, Hand.Right => Engine.Hand.Right, _ => Engine.Hand.Unknown };
                    }
                    parts.Add(p);
                }
            }
            return parts;
        }

        private static PianoFinger[][] Split(FingeringGroup group, IReadOnlyList<SongNote>[] notes, List<Engine.PianoNote> parts)
        {
            var result = new PianoFinger[notes.Length][];
            int k = 0;
            for (int t = 0; t < notes.Length; t++)
            {
                var fingers = new PianoFinger[notes[t].Count];
                for (int i = 0; i < fingers.Length; i++, k++)
                {
                    var p = parts[k];
                    var hand = p.Hand switch { Engine.Hand.Left => Hand.Left, Engine.Hand.Right => Hand.Right, _ => Hand.None };
                    fingers[i] = new PianoFinger(notes[t][i].Number, hand, Math.Clamp(p.Finger, 0, 5), p.FromChart, p.LockedFinger > 0);
                }
                result[t] = fingers;
            }
            return result;
        }
    }
}
