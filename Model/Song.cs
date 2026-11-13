using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using nMIDI.Theory;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace nMIDI.Model
{
    /// <summary>One note, in ticks. Notes are immutable: an edit replaces them.</summary>
    public sealed record SongNote(int Number, int Channel, long Start, long End, int Velocity, int OffVelocity = 0)
    {
        public long Length => End - Start;
    }

    /// <summary>Kinds of channel events other than notes.</summary>
    public enum CtrlKind { Controller, Program, PitchBend, ChannelPressure, PolyPressure }

    /// <summary>
    /// A channel event that is not a note: controller (Number = CC, Value 0–127), program change
    /// (Value = program), pitch bend (Value 0–16383, 8192 = centre), channel pressure (Value) or
    /// polyphonic aftertouch (Number = key, Value).
    /// </summary>
    public sealed record SongCtrl(long Tick, int Channel, CtrlKind Kind, int Number, int Value);

    /// <summary>A meta or system-exclusive event kept as it was read, with its position.</summary>
    public sealed record SongMeta(long Tick, MidiEvent Event);

    public sealed record TempoChange(long Tick, int MicrosecondsPerQuarter)
    {
        public double Bpm => 60_000_000.0 / MicrosecondsPerQuarter;
        public static int FromBpm(double bpm) => (int)Math.Round(60_000_000.0 / Math.Clamp(bpm, 5, 1000));
    }

    public sealed record TimeSignatureChange(long Tick, int Numerator, int Denominator);

    /// <summary>A channel event with its position, ready for the player.</summary>
    public readonly record struct SongEvent(long Tick, double Time, int Track, ChannelEvent Event);

    /// <summary>
    /// A track of the song. Its note and controller lists are never changed in place: an edit
    /// assigns a new list, so the undo history can keep the old one by reference.
    /// </summary>
    public sealed class SongTrack
    {
        private IReadOnlyList<SongNote> _notes = Array.Empty<SongNote>();
        private IReadOnlyList<SongNote>? _fingered;
        private Dictionary<int, int[]> _byKey = new();

        public SongTrack(string name, int channel)
        {
            Name = name;
            Channel = Math.Clamp(channel, 0, 15);
        }

        /// <summary>Position in the song's track list, set by <see cref="Song.Rebuild"/>.</summary>
        public int Index { get; internal set; }

        public string Name { get; set; }

        /// <summary>Channel (0–15) that new notes and controllers of this track use.</summary>
        public int Channel { get; set; }

        /// <summary>Notes sorted by start, then key.</summary>
        public IReadOnlyList<SongNote> Notes
        {
            get => _notes;
            set => _notes = value;
        }

        /// <summary>Controllers, program changes, pitch bend and pressure, sorted by tick.</summary>
        public IReadOnlyList<SongCtrl> Controls { get; set; } = Array.Empty<SongCtrl>();

        /// <summary>Meta and SysEx events of the track other than its name, kept for saving.</summary>
        public IReadOnlyList<SongMeta> Meta { get; set; } = Array.Empty<SongMeta>();

        /// <summary>Channels used by the track's events, or its own channel when it has none.</summary>
        public IReadOnlyList<int> Channels
        {
            get
            {
                var set = new SortedSet<int>();
                foreach (var n in Notes) set.Add(n.Channel);
                foreach (var c in Controls) set.Add(c.Channel);
                if (set.Count == 0) set.Add(Channel);
                return set.ToList();
            }
        }

        public bool IsDrums => Channel == 9;

        /// <summary>The first program of the track's channel, as a General MIDI name, or "Drums".</summary>
        public string Instrument
        {
            get
            {
                if (IsDrums) return "Drums";
                var program = Controls.FirstOrDefault(c => c.Kind == CtrlKind.Program && c.Channel == Channel)
                              ?? Controls.FirstOrDefault(c => c.Kind == CtrlKind.Program);
                return GeneralMidi.Instruments[Math.Clamp(program?.Value ?? 0, 0, 127)];
            }
        }

        /// <summary>Replace the notes with these, sorted.</summary>
        public void SetNotes(IEnumerable<SongNote> notes) => Notes = Sort(notes);

        public void SetControls(IEnumerable<SongCtrl> controls) =>
            Controls = controls.OrderBy(c => c.Tick).ToList();

        public static List<SongNote> Sort(IEnumerable<SongNote> notes)
        {
            var list = notes.ToList();
            // List.Sort is not stable; the index breaks ties so equal notes keep their order.
            var keyed = list.Select((n, i) => (n, i)).ToList();
            keyed.Sort((a, b) =>
            {
                int c = a.n.Start.CompareTo(b.n.Start);
                if (c == 0) c = a.n.Number.CompareTo(b.n.Number);
                return c != 0 ? c : a.i.CompareTo(b.i);
            });
            return keyed.Select(x => x.n).ToList();
        }

        /// <summary>
        /// Hand and finger of each note (same order as <see cref="Notes"/>); finger 0 when the track is not
        /// fingered. The song works it out in the background: until it is ready, unchanged notes keep their
        /// old finger and new notes have none.
        /// </summary>
        public PianoFinger[] Fingering => Owner?.Fingering.For(this) ?? Alone();

        /// <summary>The fingering, worked out now if needed (for tests, exports and the report).</summary>
        public PianoFinger[] FingeringNow() => Owner?.Fingering.Now(this) ?? Alone();

        /// <summary>A track outside a song is fingered on its own, synchronously.</summary>
        private PianoFinger[] Alone()
        {
            var notes = _notes;
            if (IsDrums || Timeline == null) return notes.Select(n => new PianoFinger(n.Number, Hand.None, 0)).ToArray();
            var timeline = Timeline;
            var part = notes.Select(n => (timeline.TickToTime(n.Start) / 1e6, timeline.TickToTime(n.End) / 1e6, n.Number)).ToList();
            return PartFingering.Finger(part, PartFingering.HandFromTrackName(Name));
        }

        /// <summary>The song the track belongs to, set by <see cref="Song.Rebuild"/> and <see cref="Song.SetTracks"/>.</summary>
        internal Song? Owner { get; set; }

        private (IReadOnlyList<SongMeta>? Meta, TrackFingeringInfo Info) _fingeringInfo;

        /// <summary>Fingering mode, pair, finger corrections and fingers written in the file (kept in <see cref="Meta"/>).</summary>
        public TrackFingeringInfo FingeringInfo
        {
            get
            {
                var meta = Meta;
                var cached = _fingeringInfo;
                if (ReferenceEquals(cached.Meta, meta)) return cached.Info;
                var info = TrackFingeringInfo.Parse(meta);
                _fingeringInfo = (meta, info);
                return info;
            }
        }

        public void SetFingeringSetup(FingerMode mode, int pair) => Meta = TrackFingeringInfo.WithSetup(Meta, mode, pair);

        public void SetFingerLocks(IEnumerable<FingerLock> locks) => Meta = TrackFingeringInfo.WithLocks(Meta, locks);

        /// <summary>After notes were moved or transposed, move their finger corrections with them.</summary>
        public void MoveFingerLocks(IReadOnlyList<SongNote> before, IReadOnlyList<SongNote> after)
        {
            var locks = FingeringInfo.Locks;
            if (locks.Count == 0 || before.Count != after.Count) return;
            var map = locks.ToDictionary(kv => kv.Key, kv => kv.Value);
            bool changed = false;
            var moved = new List<FingerLock>();
            for (int i = 0; i < before.Count; i++)
            {
                if (!map.Remove((before[i].Number, before[i].Start), out var l)) continue;
                moved.Add(l with { Number = after[i].Number, Tick = after[i].Start });
                changed = true;
            }
            if (!changed) return;
            foreach (var l in moved) map[(l.Number, l.Tick)] = l;
            SetFingerLocks(map.Values);
        }

        internal TempoTimeline? Timeline { get; set; }

        /// <summary>The key index for <see cref="FindNote"/>, rebuilt the first time it is needed after the notes changed.</summary>
        private void EnsureIndex()
        {
            var notes = _notes;
            if (ReferenceEquals(_fingered, notes)) return;
            _byKey = Enumerable.Range(0, notes.Count)
                .GroupBy(i => notes[i].Number)
                .ToDictionary(g => g.Key, g => g.OrderBy(i => notes[i].Start).ToArray());
            _fingered = notes;
        }

        /// <summary>Index of the note with this key that is sounding (or starts) at <paramref name="tick"/>, or -1.</summary>
        public int FindNote(int number, double tick)
        {
            EnsureIndex();
            if (!_byKey.TryGetValue(number, out var indices)) return -1;
            int lo = 0, hi = indices.Length - 1, found = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (_notes[indices[mid]].Start <= tick + 1) { found = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            return found >= 0 ? indices[found] : -1;
        }
    }

    /// <summary>What the player needs from a song, built in one go after every edit and never changed.</summary>
    public sealed class PlaybackData
    {
        public PlaybackData(TempoTimeline timeline, SongEvent[] events, long lengthTicks, (double Time, bool Accent)[]? clicks = null)
        {
            Timeline = timeline;
            Events = events;
            LengthTicks = lengthTicks;
            LengthTime = timeline.TickToTime(lengthTicks);
            Clicks = clicks ?? Array.Empty<(double, bool)>();
        }

        /// <summary>Metronome clicks: the time of every beat, accented on the first beat of a bar.</summary>
        public (double Time, bool Accent)[] Clicks { get; }

        public TempoTimeline Timeline { get; }

        /// <summary>All channel events of all tracks, sorted by time (controllers, then note-offs, then note-ons at the same tick).</summary>
        public SongEvent[] Events { get; }

        public long LengthTicks { get; }

        /// <summary>Length in microseconds at normal speed.</summary>
        public double LengthTime { get; }
    }

    /// <summary>
    /// An editable MIDI song: tracks with notes, controllers and meta events, plus the tempo and
    /// time signature changes and the other events of the conductor track.
    ///
    /// The tempo usually lives in its own conductor track with no notes, so tempo and time signature
    /// changes are collected from every track. After an edit, <see cref="Rebuild"/> makes new
    /// <see cref="Playback"/> data; the player and the playhead both use its timeline.
    /// </summary>
    public sealed class Song
    {
        private List<SongTrack> _tracks = new();

        public Song(int ticksPerQuarter)
        {
            TicksPerQuarter = Math.Clamp(ticksPerQuarter, 24, 32767);
            Fingering = new SongFingering(this);
        }

        /// <summary>Background, incremental fingering of the song's keyboard tracks.</summary>
        public SongFingering Fingering { get; }

        /// <summary>
        /// The editor's own fingering events are saved only when <see cref="FingeringPrefs.SaveInFile"/> is on,
        /// and a finger correction only while its note still exists.
        /// </summary>
        private static bool KeepFingeringEvent(SongTrack track, SongMeta m)
        {
            if (!FingeringPrefs.SaveInFile) return false;
            var text = ((TextEvent)m.Event).Text ?? "";
            var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 3 && parts[0] == "nMIDI:finger" && int.TryParse(parts[1], out int number))
                return track.Notes.Any(n => n.Number == number && n.Start == m.Tick);
            return true;
        }

        /// <summary>Full path of the file, or null for a song that was never saved.</summary>
        public string? Path { get; set; }

        public string Name => Path != null ? System.IO.Path.GetFileName(Path) : "Untitled.mid";

        public long TicksPerQuarter { get; }

        public IReadOnlyList<SongTrack> Tracks => _tracks;

        public IReadOnlyList<TempoChange> Tempos { get; set; } = new[] { new TempoChange(0, 500_000) };

        public IReadOnlyList<TimeSignatureChange> TimeSignatures { get; set; } = new[] { new TimeSignatureChange(0, 4, 4) };

        /// <summary>Meta and SysEx events of the conductor track (markers, key signatures, text, song name...).</summary>
        public IReadOnlyList<SongMeta> Conductor { get; set; } = Array.Empty<SongMeta>();

        /// <summary>The format the file was read in (0 or 1); new songs are 1.</summary>
        public int SourceFormat { get; set; } = 1;

        public PlaybackData Playback { get; private set; } = null!;

        public TempoTimeline Timeline => Playback.Timeline;
        public SongEvent[] Events => Playback.Events;
        public long LengthTicks => Playback.LengthTicks;
        public double LengthTime => Playback.LengthTime;

        /// <summary>Ticks per beat of the first time signature.</summary>
        public long TicksPerBeat => Math.Max(1, TicksPerQuarter * 4 / Math.Max(1, TimeSignatures[0].Denominator));

        /// <summary>Beats per bar of the first time signature.</summary>
        public int BeatsPerBar => Math.Max(1, TimeSignatures[0].Numerator);

        public long TicksPerBar => TicksPerBeat * BeatsPerBar;

        public int NoteCount => _tracks.Sum(t => t.Notes.Count);

        // --- Tracks ---

        /// <summary>Replace the track list (used by the undo history and by track commands).</summary>
        public void SetTracks(IEnumerable<SongTrack> tracks)
        {
            _tracks = tracks.ToList();
            for (int i = 0; i < _tracks.Count; i++) { _tracks[i].Index = i; _tracks[i].Owner = this; }
        }

        /// <summary>
        /// Rebuild what depends on the notes and the tempo: track indices, the tempo timeline and the
        /// sorted event list for the player. Call after every edit.
        /// </summary>
        public void Rebuild()
        {
            Tempos = NormalizeTempos(Tempos);
            TimeSignatures = NormalizeSignatures(TimeSignatures);
            var timeline = TempoTimeline.From(Tempos, TicksPerQuarter);

            var all = new List<(SongEvent Event, int Rank, int Order)>();
            int order = 0;
            long length = 0;
            for (int index = 0; index < _tracks.Count; index++)
            {
                var track = _tracks[index];
                track.Index = index;
                track.Owner = this;
                track.Timeline = timeline;

                foreach (var c in track.Controls)
                {
                    all.Add((new SongEvent(c.Tick, timeline.TickToTime(c.Tick), index, ToMidi(c)), 0, order++));
                    length = Math.Max(length, c.Tick);
                }

                foreach (var n in track.Notes)
                {
                    all.Add((new SongEvent(n.Start, timeline.TickToTime(n.Start), index, NoteOn(n)), 2, order++));
                    all.Add((new SongEvent(n.End, timeline.TickToTime(n.End), index, NoteOff(n)), 1, order++));
                    length = Math.Max(length, n.End);
                }
            }

            all.Sort((a, b) =>
            {
                int c = a.Event.Tick.CompareTo(b.Event.Tick);
                if (c == 0) c = a.Rank.CompareTo(b.Rank);
                return c != 0 ? c : a.Order.CompareTo(b.Order);
            });

            // Metronome clicks up to well after the end, so a loop or a recording past the end still has them.
            var clicks = new List<(double, bool)>();
            foreach (var (tick, _, beatTicks, beats) in BarLines(0, length + TicksPerQuarter * 4 * 64))
                for (int b = 0; b < beats; b++)
                    clicks.Add((timeline.TickToTime(tick + b * beatTicks), b == 0));

            Playback = new PlaybackData(timeline, all.Select(x => x.Event).ToArray(), length, clicks.ToArray());
        }

        private static List<TempoChange> NormalizeTempos(IEnumerable<TempoChange> tempos)
        {
            // One tempo per tick (the last one wins), always one at tick 0.
            var list = tempos.Where(t => t.Tick >= 0 && t.MicrosecondsPerQuarter > 0)
                             .GroupBy(t => t.Tick).Select(g => g.Last())
                             .OrderBy(t => t.Tick).ToList();
            if (list.Count == 0 || list[0].Tick > 0) list.Insert(0, new TempoChange(0, list.Count > 0 ? list[0].MicrosecondsPerQuarter : 500_000));
            return list;
        }

        private static List<TimeSignatureChange> NormalizeSignatures(IEnumerable<TimeSignatureChange> signatures)
        {
            var list = signatures.Where(s => s.Tick >= 0 && s.Numerator > 0 && s.Denominator > 0)
                                 .GroupBy(s => s.Tick).Select(g => g.Last())
                                 .OrderBy(s => s.Tick).ToList();
            if (list.Count == 0)
                list.Add(new TimeSignatureChange(0, 4, 4));
            else if (list[0].Tick > 0)
                list.Insert(0, list[0] with { Tick = 0 });
            return list;
        }

        private static NoteOnEvent NoteOn(SongNote n) =>
            new((SevenBitNumber)n.Number, (SevenBitNumber)Math.Clamp(n.Velocity, 1, 127)) { Channel = (FourBitNumber)n.Channel };

        private static NoteOffEvent NoteOff(SongNote n) =>
            new((SevenBitNumber)n.Number, (SevenBitNumber)Math.Clamp(n.OffVelocity, 0, 127)) { Channel = (FourBitNumber)n.Channel };

        public static ChannelEvent ToMidi(SongCtrl c)
        {
            ChannelEvent e = c.Kind switch
            {
                CtrlKind.Controller => new ControlChangeEvent((SevenBitNumber)Math.Clamp(c.Number, 0, 127), (SevenBitNumber)Math.Clamp(c.Value, 0, 127)),
                CtrlKind.Program => new ProgramChangeEvent((SevenBitNumber)Math.Clamp(c.Value, 0, 127)),
                CtrlKind.PitchBend => new PitchBendEvent((ushort)Math.Clamp(c.Value, 0, 16383)),
                CtrlKind.ChannelPressure => new ChannelAftertouchEvent((SevenBitNumber)Math.Clamp(c.Value, 0, 127)),
                _ => new NoteAftertouchEvent((SevenBitNumber)Math.Clamp(c.Number, 0, 127), (SevenBitNumber)Math.Clamp(c.Value, 0, 127)),
            };
            e.Channel = (FourBitNumber)Math.Clamp(c.Channel, 0, 15);
            return e;
        }

        /// <summary>A controller, program, pitch bend or pressure event as a <see cref="SongCtrl"/>; null for notes.</summary>
        public static SongCtrl? ControlFrom(long tick, ChannelEvent e) => FromMidi(tick, e);

        private static SongCtrl? FromMidi(long tick, ChannelEvent e) => e switch
        {
            ControlChangeEvent cc => new SongCtrl(tick, cc.Channel, CtrlKind.Controller, cc.ControlNumber, cc.ControlValue),
            ProgramChangeEvent pc => new SongCtrl(tick, pc.Channel, CtrlKind.Program, 0, pc.ProgramNumber),
            PitchBendEvent pb => new SongCtrl(tick, pb.Channel, CtrlKind.PitchBend, 0, pb.PitchValue),
            ChannelAftertouchEvent ca => new SongCtrl(tick, ca.Channel, CtrlKind.ChannelPressure, 0, ca.AftertouchValue),
            NoteAftertouchEvent na => new SongCtrl(tick, na.Channel, CtrlKind.PolyPressure, na.NoteNumber, na.AftertouchValue),
            _ => null,
        };

        // --- New song ---

        /// <summary>An empty song: 480 PPQ, 120 BPM, 4/4 and one piano track on channel 1.</summary>
        public static Song CreateEmpty()
        {
            var song = new Song(480);
            var track = new SongTrack("Track 1", 0);
            track.SetControls(new[] { new SongCtrl(0, 0, CtrlKind.Program, 0, 0) });
            song.SetTracks(new[] { track });
            song.Rebuild();
            return song;
        }

        // --- Loading ---

        public static Song Load(string path)
        {
            var settings = new ReadingSettings
            {
                InvalidChunkSizePolicy = InvalidChunkSizePolicy.Ignore,
                NotEnoughBytesPolicy = NotEnoughBytesPolicy.Ignore,
                NoHeaderChunkPolicy = NoHeaderChunkPolicy.Ignore,
                UnknownChunkIdPolicy = UnknownChunkIdPolicy.ReadAsUnknownChunk,
                MissedEndOfTrackPolicy = MissedEndOfTrackPolicy.Ignore,
                InvalidChannelEventParameterValuePolicy = InvalidChannelEventParameterValuePolicy.SnapToLimits,
                InvalidMetaEventParameterValuePolicy = InvalidMetaEventParameterValuePolicy.SnapToLimits,
                InvalidSystemCommonEventParameterValuePolicy = InvalidSystemCommonEventParameterValuePolicy.SnapToLimits,
            };
            var file = MidiFile.Read(path, settings);
            var song = FromFile(file);
            song.Path = System.IO.Path.GetFullPath(path);
            return song;
        }

        public static Song FromFile(MidiFile file)
        {
            int tpq = file.TimeDivision is TicksPerQuarterNoteTimeDivision t ? t.TicksPerQuarterNote : 480;
            var song = new Song(tpq);
            try { song.SourceFormat = file.OriginalFormat == MidiFileFormat.SingleTrack ? 0 : 1; }
            catch (InvalidOperationException) { song.SourceFormat = 1; }

            var tempos = new List<TempoChange>();
            var signatures = new List<TimeSignatureChange>();
            var conductor = new List<SongMeta>();

            // Absolute times of every event of every chunk.
            var chunks = new List<List<(long Tick, MidiEvent Event)>>();
            foreach (var chunk in file.GetTrackChunks())
            {
                long tick = 0;
                var events = new List<(long, MidiEvent)>();
                foreach (var e in chunk.Events)
                {
                    tick += e.DeltaTime;
                    switch (e)
                    {
                        case SetTempoEvent tempo:
                            tempos.Add(new TempoChange(tick, (int)Math.Clamp(tempo.MicrosecondsPerQuarterNote, 1, 16_777_215)));
                            break;
                        case TimeSignatureEvent sig:
                            signatures.Add(new TimeSignatureChange(tick, sig.Numerator, sig.Denominator));
                            break;
                        case EndOfTrackEvent:
                            break;
                        default:
                            events.Add((tick, e));
                            break;
                    }
                }
                chunks.Add(events);
            }

            static bool HasChannelEvents(List<(long Tick, MidiEvent Event)> events) => events.Any(e => e.Event is ChannelEvent);
            var withChannel = chunks.Where(HasChannelEvents).ToList();
            var tracks = new List<SongTrack>();

            if (withChannel.Count == 1 &&
                withChannel[0].Where(e => e.Event is ChannelEvent).Select(e => ((ChannelEvent)e.Event).Channel).Distinct().Count() > 1)
            {
                // Format 0 keeps everything in one chunk. Split it by channel so each instrument gets its own
                // track; the chunk's meta events go to the conductor track.
                foreach (var chunk in chunks)
                    foreach (var e in chunk)
                        if (e.Event is not ChannelEvent) conductor.Add(new SongMeta(e.Tick, e.Event));

                foreach (var group in withChannel[0].Where(e => e.Event is ChannelEvent)
                                                    .GroupBy(e => (int)((ChannelEvent)e.Event).Channel).OrderBy(g => g.Key))
                {
                    var track = new SongTrack("", group.Key);
                    Fill(track, group.ToList());
                    track.Name = track.Instrument;
                    tracks.Add(track);
                }
            }
            else
            {
                foreach (var chunk in chunks)
                {
                    if (!HasChannelEvents(chunk))
                    {
                        // The conductor track (or another chunk without notes): only meta events.
                        foreach (var e in chunk) conductor.Add(new SongMeta(e.Tick, e.Event));
                        continue;
                    }

                    var channelEvents = chunk.Where(e => e.Event is ChannelEvent).ToList();
                    int channel = channelEvents.GroupBy(e => (int)((ChannelEvent)e.Event).Channel)
                                               .OrderByDescending(g => g.Count(e => e.Event is NoteOnEvent)).ThenBy(g => g.Key)
                                               .First().Key;
                    var track = new SongTrack("", channel);
                    Fill(track, channelEvents);

                    string? name = null;
                    var meta = new List<SongMeta>();
                    foreach (var (tick, e) in chunk)
                    {
                        if (e is ChannelEvent) continue;
                        if (name == null && e is SequenceTrackNameEvent tn && !string.IsNullOrWhiteSpace(tn.Text))
                        {
                            name = tn.Text.Trim();
                            continue;
                        }
                        if (e is SequenceTrackNameEvent) continue;
                        meta.Add(new SongMeta(tick, e));
                    }
                    name ??= meta.Select(m => m.Event).OfType<InstrumentNameEvent>().Select(e => e.Text?.Trim())
                                 .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
                    track.Meta = meta;
                    track.Name = string.IsNullOrWhiteSpace(name) ? track.Instrument : name!;
                    tracks.Add(track);
                }
            }

            song.Tempos = tempos;
            song.TimeSignatures = signatures;
            song.Conductor = conductor;
            song.SetTracks(tracks);
            song.Rebuild();
            return song;
        }

        /// <summary>Notes and controllers of a track from its channel events.</summary>
        private static void Fill(SongTrack track, List<(long Tick, MidiEvent Event)> events)
        {
            track.Notes = PairNotes(events);
            var controls = new List<SongCtrl>();
            foreach (var (tick, e) in events)
                if (e is ChannelEvent ce && FromMidi(tick, ce) is { } ctrl) controls.Add(ctrl);
            track.Controls = controls;   // already in time order
        }

        public static bool IsNoteOn(MidiEvent e) => e is NoteOnEvent on && on.Velocity > 0;

        /// <summary>Match note-ons with note-offs per channel and key. Unfinished notes end at the last event.</summary>
        private static List<SongNote> PairNotes(List<(long Tick, MidiEvent Event)> events)
        {
            var open = new Dictionary<int, Stack<(long Start, int Velocity)>>();
            var notes = new List<SongNote>();
            long last = events.Count > 0 ? events[^1].Tick : 0;

            foreach (var (tick, e) in events)
            {
                if (e is not NoteEvent ne) continue;
                int key = ne.Channel * 128 + ne.NoteNumber;
                if (IsNoteOn(ne))
                {
                    if (!open.TryGetValue(key, out var stack)) open[key] = stack = new();
                    stack.Push((tick, ((NoteOnEvent)ne).Velocity));
                }
                else if (open.TryGetValue(key, out var stack) && stack.Count > 0)
                {
                    var (start, velocity) = stack.Pop();
                    int offVelocity = ne is NoteOffEvent off ? off.Velocity : 0;
                    notes.Add(new SongNote(ne.NoteNumber, ne.Channel, start, Math.Max(start + 1, tick), velocity, offVelocity));
                }
            }

            foreach (var (key, stack) in open)
                foreach (var (start, velocity) in stack)
                    notes.Add(new SongNote(key % 128, key / 128, start, Math.Max(start + 1, last), velocity));

            return SongTrack.Sort(notes);
        }

        // --- Saving ---

        /// <summary>
        /// Write the song as a standard MIDI file: format 1 (a conductor track, then one track per
        /// song track) or format 0 (everything in one track). The file is written next to the target
        /// first and then moved over it, so a failed save never leaves a broken file behind.
        /// </summary>
        public void Save(string path, int format = 1)
        {
            var file = ToMidiFile(format);
            string full = System.IO.Path.GetFullPath(path);
            string temp = full + ".saving";
            file.Write(temp, overwriteFile: true, format: format == 0 ? MidiFileFormat.SingleTrack : MidiFileFormat.MultiTrack);
            File.Move(temp, full, overwrite: true);
        }

        public MidiFile ToMidiFile(int format = 1)
        {
            // Rank orders events at the same tick: meta first, then controllers, note-offs, note-ons.
            var conductor = new List<(long Tick, int Rank, MidiEvent Event)>();
            foreach (var t in Tempos) conductor.Add((t.Tick, 0, new SetTempoEvent(t.MicrosecondsPerQuarter)));
            foreach (var s in TimeSignatures) conductor.Add((s.Tick, 0, new TimeSignatureEvent((byte)s.Numerator, (byte)s.Denominator)));
            foreach (var m in Conductor) conductor.Add((m.Tick, 0, m.Event.Clone()));

            var tracks = new List<List<(long Tick, int Rank, MidiEvent Event)>>();
            foreach (var track in _tracks)
            {
                var events = new List<(long Tick, int Rank, MidiEvent Event)>();
                if (format != 0) events.Add((0, -1, new SequenceTrackNameEvent(track.Name)));
                foreach (var m in track.Meta)
                    if (!TrackFingeringInfo.IsOwnEvent(m.Event) || KeepFingeringEvent(track, m)) events.Add((m.Tick, 0, m.Event.Clone()));
                foreach (var c in track.Controls) events.Add((c.Tick, 1, ToMidi(c)));
                foreach (var n in track.Notes)
                {
                    events.Add((n.Start, 3, NoteOn(n)));
                    events.Add((n.End, 2, NoteOff(n)));
                }
                tracks.Add(events);
            }

            var chunks = new List<TrackChunk>();
            if (format == 0)
            {
                chunks.Add(ToChunk(conductor.Concat(tracks.SelectMany(t => t))));
            }
            else
            {
                chunks.Add(ToChunk(conductor));
                foreach (var events in tracks) chunks.Add(ToChunk(events));
            }

            return new MidiFile(chunks) { TimeDivision = new TicksPerQuarterNoteTimeDivision((short)TicksPerQuarter) };
        }

        private static TrackChunk ToChunk(IEnumerable<(long Tick, int Rank, MidiEvent Event)> events)
        {
            var sorted = events.Select((e, i) => (e.Tick, e.Rank, e.Event, Order: i))
                               .OrderBy(e => e.Tick).ThenBy(e => e.Rank).ThenBy(e => e.Order);
            var chunk = new TrackChunk();
            long last = 0;
            foreach (var e in sorted)
            {
                long tick = Math.Max(0, e.Tick);
                e.Event.DeltaTime = tick - last;
                last = tick;
                chunk.Events.Add(e.Event);
            }
            return chunk;
        }

        // --- Bars and times ---

        /// <summary>Bar, beat and tick (bar and beat counted from 0) of a position, following time signature changes.</summary>
        public (long Bar, long Beat, long Tick) ToBarBeat(long tick)
        {
            tick = Math.Max(0, tick);
            long bar = 0;
            var sigs = TimeSignatures;
            for (int i = 0; i < sigs.Count; i++)
            {
                long start = sigs[i].Tick;
                long beatTicks = Math.Max(1, TicksPerQuarter * 4 / sigs[i].Denominator);
                long barTicks = beatTicks * sigs[i].Numerator;
                long end = i + 1 < sigs.Count ? sigs[i + 1].Tick : long.MaxValue;
                if (tick < end)
                {
                    long into = tick - start;
                    return (bar + into / barTicks, into % barTicks / beatTicks, into % barTicks % beatTicks);
                }
                bar += (end - start + barTicks - 1) / barTicks;
            }
            return (0, 0, tick);
        }

        /// <summary>Every bar line from <paramref name="from"/> to <paramref name="to"/>, following time signature changes.</summary>
        public IEnumerable<(long Tick, long Bar, long BeatTicks, int Beats)> BarLines(long from, long to)
        {
            var sigs = TimeSignatures;
            long bar = 0;
            for (int i = 0; i < sigs.Count; i++)
            {
                long start = sigs[i].Tick;
                long beatTicks = Math.Max(1, TicksPerQuarter * 4 / sigs[i].Denominator);
                long barTicks = beatTicks * sigs[i].Numerator;
                long end = i + 1 < sigs.Count ? sigs[i + 1].Tick : long.MaxValue;
                if (end <= from)
                {
                    bar += (end - start + barTicks - 1) / barTicks;
                    continue;
                }
                long k = Math.Max(0, (from - start) / barTicks);
                for (long tick = start + k * barTicks; tick < end && tick <= to; tick += barTicks, k++)
                    yield return (tick, bar + k, beatTicks, sigs[i].Numerator);
                if (end > to) yield break;
                bar += (end - start + barTicks - 1) / barTicks;
            }
        }

        /// <summary>The tick of a bar, beat and tick (bar and beat counted from 0), following time signature changes.</summary>
        public long FromBarBeat(long bar, long beat, long tick)
        {
            var sigs = TimeSignatures;
            long barsBefore = 0;
            for (int i = 0; i < sigs.Count; i++)
            {
                long start = sigs[i].Tick;
                long beatTicks = Math.Max(1, TicksPerQuarter * 4 / sigs[i].Denominator);
                long barTicks = beatTicks * sigs[i].Numerator;
                long end = i + 1 < sigs.Count ? sigs[i + 1].Tick : long.MaxValue;
                if (end == long.MaxValue) return start + (bar - barsBefore) * barTicks + beat * beatTicks + tick;
                long bars = (end - start + barTicks - 1) / barTicks;
                if (bar < barsBefore + bars) return start + (bar - barsBefore) * barTicks + beat * beatTicks + tick;
                barsBefore += bars;
            }
            return Math.Max(0, tick);
        }

        /// <summary>Bar, beat and tick of a position, counting from 1.</summary>
        public string FormatBars(long tick)
        {
            var (bar, beat, ticks) = ToBarBeat(tick);
            return $"{bar + 1,3}.{beat + 1}.{ticks:000}";
        }

        public static string FormatTime(double microseconds)
        {
            var t = TimeSpan.FromTicks((long)(Math.Max(0, microseconds) * 10));
            return $"{(int)t.TotalMinutes:00}:{t.Seconds:00}.{t.Milliseconds:000}";
        }

        public static bool IsMidiFile(string path)
        {
            string ext = System.IO.Path.GetExtension(path);
            return ext.Equals(".mid", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".midi", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".kar", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".rmi", StringComparison.OrdinalIgnoreCase);
        }

        internal static IEnumerable<string> FilesIn(IEnumerable<string> paths) => paths.Where(p => System.IO.File.Exists(p) && IsMidiFile(p));
    }

    /// <summary>
    /// Ticks ↔ microseconds for a song, from all its tempo changes. Piecewise linear;
    /// both directions use a binary search over the tempo segments.
    /// </summary>
    public sealed class TempoTimeline
    {
        private readonly long[] _ticks;          // segment start, in ticks
        private readonly double[] _times;        // segment start, in microseconds
        private readonly double[] _usPerTick;    // tempo of the segment

        private TempoTimeline(long[] ticks, double[] times, double[] usPerTick)
        {
            _ticks = ticks;
            _times = times;
            _usPerTick = usPerTick;
        }

        public static TempoTimeline From(IReadOnlyList<TempoChange> tempos, long ticksPerQuarter)
        {
            var changes = tempos.Where(c => c.Tick >= 0 && c.MicrosecondsPerQuarter > 0).OrderBy(c => c.Tick).ToList();
            if (changes.Count == 0 || changes[0].Tick > 0)
                changes.Insert(0, new TempoChange(0, 500_000));

            int n = changes.Count;
            var ticks = new long[n];
            var times = new double[n];
            var rates = new double[n];
            for (int i = 0; i < n; i++)
            {
                ticks[i] = changes[i].Tick;
                rates[i] = changes[i].MicrosecondsPerQuarter / (double)Math.Max(1, ticksPerQuarter);
                times[i] = i == 0 ? 0 : times[i - 1] + (ticks[i] - ticks[i - 1]) * rates[i - 1];
            }
            return new TempoTimeline(ticks, times, rates);
        }

        /// <summary>Microseconds from the start at normal speed.</summary>
        public double TickToTime(double tick)
        {
            int i = Segment(_ticks, tick);
            return _times[i] + (tick - _ticks[i]) * _usPerTick[i];
        }

        public double TimeToTick(double time)
        {
            int lo = 0, hi = _times.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (_times[mid] <= time) lo = mid; else hi = mid - 1;
            }
            return _ticks[lo] + (time - _times[lo]) / _usPerTick[lo];
        }

        /// <summary>Tempo in beats (quarter notes) per minute at a tick.</summary>
        public double BpmAt(double tick, long ticksPerQuarter) =>
            60_000_000.0 / (_usPerTick[Segment(_ticks, tick)] * ticksPerQuarter);

        private static int Segment(long[] starts, double tick)
        {
            int lo = 0, hi = starts.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (starts[mid] <= tick) lo = mid; else hi = mid - 1;
            }
            return lo;
        }
    }

    public static class GeneralMidi
    {
        public static readonly string[] Instruments =
        [
            "Acoustic Grand Piano", "Bright Acoustic Piano", "Electric Grand Piano", "Honky-tonk Piano", "Electric Piano 1", "Electric Piano 2", "Harpsichord", "Clavinet",
            "Celesta", "Glockenspiel", "Music Box", "Vibraphone", "Marimba", "Xylophone", "Tubular Bells", "Dulcimer",
            "Drawbar Organ", "Percussive Organ", "Rock Organ", "Church Organ", "Reed Organ", "Accordion", "Harmonica", "Tango Accordion",
            "Nylon Guitar", "Steel Guitar", "Jazz Guitar", "Clean Guitar", "Muted Guitar", "Overdriven Guitar", "Distortion Guitar", "Guitar Harmonics",
            "Acoustic Bass", "Finger Bass", "Pick Bass", "Fretless Bass", "Slap Bass 1", "Slap Bass 2", "Synth Bass 1", "Synth Bass 2",
            "Violin", "Viola", "Cello", "Contrabass", "Tremolo Strings", "Pizzicato Strings", "Orchestral Harp", "Timpani",
            "String Ensemble 1", "String Ensemble 2", "Synth Strings 1", "Synth Strings 2", "Choir Aahs", "Voice Oohs", "Synth Voice", "Orchestra Hit",
            "Trumpet", "Trombone", "Tuba", "Muted Trumpet", "French Horn", "Brass Section", "Synth Brass 1", "Synth Brass 2",
            "Soprano Sax", "Alto Sax", "Tenor Sax", "Baritone Sax", "Oboe", "English Horn", "Bassoon", "Clarinet",
            "Piccolo", "Flute", "Recorder", "Pan Flute", "Blown Bottle", "Shakuhachi", "Whistle", "Ocarina",
            "Square Lead", "Sawtooth Lead", "Calliope Lead", "Chiff Lead", "Charang Lead", "Voice Lead", "Fifths Lead", "Bass + Lead",
            "New Age Pad", "Warm Pad", "Polysynth Pad", "Choir Pad", "Bowed Pad", "Metallic Pad", "Halo Pad", "Sweep Pad",
            "Rain", "Soundtrack", "Crystal", "Atmosphere", "Brightness", "Goblins", "Echoes", "Sci-fi",
            "Sitar", "Banjo", "Shamisen", "Koto", "Kalimba", "Bagpipe", "Fiddle", "Shanai",
            "Tinkle Bell", "Agogo", "Steel Drums", "Woodblock", "Taiko Drum", "Melodic Tom", "Synth Drum", "Reverse Cymbal",
            "Guitar Fret Noise", "Breath Noise", "Seashore", "Bird Tweet", "Telephone Ring", "Helicopter", "Applause", "Gunshot",
        ];
    }
}
